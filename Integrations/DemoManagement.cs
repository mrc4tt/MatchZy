using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        public string demoPath = "demos/";
        public string demoNameFormat = "{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}";
        public string demoUploadURL = "";
        public string demoUploadHeaderKey = "";
        public string demoUploadHeaderValue = "";
        public string activeDemoFile = "";
        public bool isDemoRecording = false;
        public bool isDemoUploadS3Enabled = false;

        // Set by every going-live path (match/scrim/hill): their cfg runs mp_restartgame AFTER the
        // exec, and the engine restart wipes an in-flight tv_record. The start is deferred to the
        // first live round_start that happens once the restart has certainly landed
        // (HandlePostRoundStartEvent), with a fallback timer if that round_start never arrives.
        public bool demoStartPending = false;

        // Server.CurrentTime before which a round_start must NOT start the demo. The going-live cfgs
        // fire a round_start of their own (mp_warmup_end) a moment BEFORE their mp_restartgame lands,
        // so without this floor the pending flag was consumed by the pre-restart round_start and the
        // demo was killed by the restart that followed.
        public float demoStartArmTime = 0f;

        private int demoStartAttempts = 0;
        private const int DemoStartMaxAttempts = 3;

        // Mid-match recording watchdog: the start-time growth check only proves the demo was alive
        // during its first seconds. GOTV can stall later (tv_enable_dynamic spinning the bot down),
        // so a repeating timer keeps sampling the file size for the whole match and restarts the
        // recording if it stops growing.
        private CounterStrikeSharp.API.Modules.Timers.Timer? demoWatchdogTimer;
        private long demoWatchdogLastSize = 0;
        private int demoWatchdogStalledChecks = 0;
        private int demoWatchdogRequiredStrikes = 3;
        private const float DemoWatchdogIntervalSeconds = 60.0f;

        // "CSTV Recording..." is announced once per demo and only once the file is confirmed on disk.
        // The going-live paths used to print it from tv_enable alone, so players were told the match
        // was being recorded even when the recording had already been thrown away.
        private bool demoAnnounced = false;

        /// <summary>
        /// Arm the deferred demo start used by StartLive / StartScrim / StartHill.
        /// </summary>
        /// <param name="restartSettleSeconds">Round starts before this many seconds are ignored, so the cfg's mp_restartgame cannot clobber the recording.</param>
        /// <param name="fallbackSeconds">Start the demo anyway if no qualifying round_start arrives.</param>
        public void ArmDemoStart(float restartSettleSeconds = 4.0f, float fallbackSeconds = 8.0f)
        {
            demoStartAttempts = 0;
            demoAnnounced = false;
            demoStartPending = true;
            demoStartArmTime = Server.CurrentTime + restartSettleSeconds;
            AddTimer(fallbackSeconds, () =>
            {
                if (demoStartPending) StartDemoRecording();
            });
        }

        /// <summary>
        /// Drop all demo state on a map change. The engine stops GOTV recording on a changelevel, but a
        /// map change we do not drive ourselves (CS2-SimpleAdmin css_map, an RTV plugin, a plain
        /// changelevel) never reaches our teardown, so isDemoRecording stayed true forever and every
        /// later StartDemoRecording was swallowed by the "already recording" guard.
        /// </summary>
        public void ResetDemoStateOnMapStart()
        {
            if (isDemoRecording)
            {
                Log($"[Demo] Map changed while recording {activeDemoFile} - clearing stale recording state.");
            }
            isDemoRecording = false;
            demoStartPending = false;
            demoStartArmTime = 0f;
            demoStartAttempts = 0;
            demoAnnounced = false;
            activeDemoFile = "";
            demoWatchdogTimer?.Kill();
            demoWatchdogTimer = null;
        }

        private bool IsGOTVEnabled()
        {
            // -nohltv is a bare flag with no value, so probe the raw process command line for it.
            // Deliberately NOT CounterStrikeSharp.API.CommandLine: that class only exists in the
            // forked CounterStrikeSharp build, and touching it on a stock upstream server throws
            // TypeLoadException the moment this method is JIT-compiled.
            if (HasLaunchOption("-nohltv"))
            {
                Log("[Demo] Not recording: server was started with -nohltv.");
                return false;
            }

            // Prefer the LIVE tv_enable convar over the command line. Plenty of hosts enable GOTV from
            // a cfg (autoexec, server.cfg, a provider's own cstv.cfg) rather than a +tv_enable launch
            // option, and the command-line-only check silently disabled demo recording on those servers.
            ConVar? tvEnable = _cvTvEnable ??= ConVar.Find("tv_enable");
            if (tvEnable != null)
            {
                if (tvEnable.GetPrimitiveValue<bool>()) return true;
                Log("[Demo] Not recording: tv_enable is 0.");
                return false;
            }

            // Convar not resolvable yet - fall back to the launch options.
            string tvEnableParam = NativeAPI.GetCommandParamValue("+tv_enable", DataType.DATA_TYPE_STRING, "0");
            if (tvEnableParam != "1")
            {
                Log("[Demo] Not recording: tv_enable convar not found and +tv_enable is not 1 on the command line.");
                return false;
            }

            return true;
        }

        public void StartDemoRecording()
        {
            demoStartPending = false;

            // Idempotent: already recording (e.g. pending-flag + fallback timer both fired) -> no-op,
            // don't restart the demo mid-file.
            if (isDemoRecording)
            {
                return;
            }
            // Check if GOTV is properly enabled before starting (it logs its own reason if not)
            if (!IsGOTVEnabled())
            {
                AnnounceDemoStatus(false, "CSTV is not enabled on this server - this match is NOT being recorded.");
                return;
            }

            demoStartAttempts++;
            string demoFileName = FormatCvarValue(demoNameFormat.Replace(" ", "_")) + ".dem";
            string tempDemoPath = demoPath == "" ? demoFileName : demoPath + demoFileName;
            try
            {
                string? directoryPath = Path.GetDirectoryName(Path.Join(Server.GameDirectory + "/csgo/" + demoPath));
                if (directoryPath != null)
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // Could not create the demo folder - record into the csgo root instead of not at all.
                Log($"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording without path. Name: {demoFileName}");
                tempDemoPath = demoFileName;
            }

            activeDemoFile = tempDemoPath;
            // tv_record_immediate 1 makes GOTV write the .dem while the match runs instead of buffering
            // it, so the file is on disk (which is what the verification below checks) and survives a
            // server crash mid-match.
            Server.ExecuteCommand($"tv_record_immediate 1;tv_record {tempDemoPath}");
            isDemoRecording = true;
            Log($"[StartDemoRecording] tv_record {tempDemoPath} (attempt {demoStartAttempts}/{DemoStartMaxAttempts})");
            VerifyDemoRecording(tempDemoPath);
        }

        /// <summary>
        /// Confirm a short while later that GOTV is actually recording, and retry if it is not. A
        /// tv_record that the engine drops (an mp_restartgame landing right after it is the usual
        /// cause) reports nothing at all, so without this the plugin believed it was recording for
        /// the whole match and only the missing file at the end gave it away.
        ///
        /// The existence check (6s) is what gates the retry and the "CSTV Recording..." announce:
        /// the engine creates the .dem the moment it accepts tv_record, so a missing file means the
        /// command was dropped. File GROWTH cannot be checked that early: GOTV's async demo writer
        /// (HLTVServerAsync) holds tv_delay seconds of frames in memory before anything past the
        /// header reaches disk, so the file legitimately sits at header size (~75 KB) for the whole
        /// delay window. The growth probe therefore waits tv_delay plus a margin, and only then
        /// treats a static file as a dead recording.
        /// </summary>
        private void VerifyDemoRecording(string expectedDemoFile)
        {
            AddTimer(6.0f, () =>
            {
                // Something else already stopped or replaced the recording - nothing to verify.
                if (!isDemoRecording || activeDemoFile != expectedDemoFile) return;

                string fullPath = Path.Join(Server.GameDirectory + "/csgo/" + expectedDemoFile);
                if (!File.Exists(fullPath))
                {
                    HandleDemoStartFailure(expectedDemoFile, "never appeared on disk");
                    return;
                }

                Log($"[Demo] Recording confirmed on disk: {expectedDemoFile}");
                AnnounceDemoStatus(true, "CSTV Recording...");

                long sizeAtFirstCheck = 0;
                try { sizeAtFirstCheck = new FileInfo(fullPath).Length; } catch { }

                float growthGrace = DemoGrowthGraceSeconds();
                AddTimer(growthGrace, () =>
                {
                    if (!isDemoRecording || activeDemoFile != expectedDemoFile) return;

                    long sizeNow = -1;
                    try { if (File.Exists(fullPath)) sizeNow = new FileInfo(fullPath).Length; } catch { }

                    if (sizeNow > sizeAtFirstCheck)
                    {
                        Log($"[Demo] Recording verified: {expectedDemoFile} is growing on disk ({sizeAtFirstCheck} -> {sizeNow} bytes).");
                        StartDemoWatchdog(expectedDemoFile, sizeNow);
                        return;
                    }
                    HandleDemoStartFailure(expectedDemoFile, $"is on disk but not growing {growthGrace:F0}s after the start ({sizeAtFirstCheck} -> {sizeNow} bytes)");
                });
            });
        }

        /// <summary>
        /// How long after tv_record the .dem may legitimately stay at header size: the GOTV delay
        /// (nothing hits disk before delayed frames exist) plus a small flush margin. The margin
        /// cannot be zero - with tv_delay 0 the file still needs a moment to get past the header.
        /// </summary>
        private float DemoGrowthGraceSeconds()
        {
            return Math.Max(30.0f, GetTvDelaySeconds() + 30.0f);
        }

        private int GetTvDelaySeconds()
        {
            try
            {
                ConVar? tvDelay = _cvTvDelay ??= ConVar.Find("tv_delay");
                if (tvDelay != null) return Math.Max(0, tvDelay.GetPrimitiveValue<int>());
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Keep watching the demo for the rest of the match. Every interval the file size is
        /// sampled; only after enough consecutive samples with no growth is the recording declared
        /// dead and restarted into a fresh file. The strike count is derived from tv_delay: the
        /// async demo writer buffers the GOTV delay in memory, so a healthy recording can go a
        /// delay's worth of seconds without the file moving, and restarting on a shorter stall
        /// would kill a live recording (which is exactly what an early version of this did).
        /// </summary>
        private void StartDemoWatchdog(string expectedDemoFile, long knownSize)
        {
            demoWatchdogTimer?.Kill();
            demoWatchdogTimer = null;
            demoWatchdogLastSize = knownSize;
            demoWatchdogStalledChecks = 0;

            // Stall window: at least 2 minutes, and always past the GOTV delay.
            float stallWindowSeconds = Math.Max(120.0f, GetTvDelaySeconds() + 60.0f);
            demoWatchdogRequiredStrikes = (int)Math.Ceiling(stallWindowSeconds / DemoWatchdogIntervalSeconds);

            demoWatchdogTimer = AddTimer(DemoWatchdogIntervalSeconds, () =>
            {
                // Recording stopped or replaced through the normal paths - watchdog is done.
                if (!isDemoRecording || activeDemoFile != expectedDemoFile)
                {
                    demoWatchdogTimer?.Kill();
                    demoWatchdogTimer = null;
                    return;
                }

                string fullPath = Path.Join(Server.GameDirectory + "/csgo/" + expectedDemoFile);
                long sizeNow = -1;
                try { if (File.Exists(fullPath)) sizeNow = new FileInfo(fullPath).Length; } catch { }

                if (sizeNow > demoWatchdogLastSize)
                {
                    demoWatchdogLastSize = sizeNow;
                    demoWatchdogStalledChecks = 0;
                    return;
                }

                demoWatchdogStalledChecks++;
                Log($"[Demo] Watchdog: {expectedDemoFile} has not grown for {demoWatchdogStalledChecks}/{demoWatchdogRequiredStrikes} check(s) (size {sizeNow} bytes).");
                if (demoWatchdogStalledChecks < demoWatchdogRequiredStrikes) return;

                // Dead mid-match. Restart into a fresh file and let the whole verify chain
                // (existence + growth + this watchdog) run again on the new recording.
                demoWatchdogTimer?.Kill();
                demoWatchdogTimer = null;
                Log($"[Demo] Watchdog: recording {expectedDemoFile} stalled mid-match - restarting into a new demo.");
                isDemoRecording = false;
                demoStartAttempts = 0;
                Server.ExecuteCommand("tv_stoprecord");
                StartDemoRecording();
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        /// <summary>
        /// A demo start attempt turned out dead (file missing or not growing): stop whatever GOTV
        /// thinks it is doing and retry with a fresh tv_record, up to DemoStartMaxAttempts.
        /// </summary>
        private void HandleDemoStartFailure(string expectedDemoFile, string reason)
        {
            // Clear the flag first, otherwise the retry is swallowed by the idempotency guard.
            isDemoRecording = false;

            // If the engine is half-recording (stalled file open), a bare tv_record would be
            // rejected with "already recording" - clear it so the retry starts clean.
            Server.ExecuteCommand("tv_stoprecord");

            if (demoStartAttempts >= DemoStartMaxAttempts)
            {
                Log($"[Demo] GOTV demo {expectedDemoFile} {reason} after {demoStartAttempts} attempts - giving up for this map.");
                AnnounceDemoStatus(false, "CSTV demo could not be started - this match is NOT being recorded.");
                return;
            }
            Log($"[Demo] GOTV demo {expectedDemoFile} {reason} - retrying tv_record.");
            StartDemoRecording();
        }

        /// <summary>
        /// Tell the server once whether this match is being recorded. Retries stay silent - only the
        /// final outcome is announced, so a demo that needed a second tv_record does not print twice.
        /// </summary>
        private void AnnounceDemoStatus(bool recording, string message)
        {
            if (demoAnnounced) return;
            demoAnnounced = true;
            PrintToAllChat($"{(recording ? ChatColors.Green : ChatColors.Red)}{message}");
        }

        public void StopDemoRecording(string activeDemoFile, long liveMatchId, int currentMapNumber)
        {
            string demoPath = Path.Join(Server.GameDirectory + "/csgo/" + activeDemoFile);
            (int t1score, int t2score) = GetTeamsScore();
            int roundNumber = t1score + t2score;

            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
                demoStartPending = false;
                demoWatchdogTimer?.Kill();
                demoWatchdogTimer = null;
                Log($"[StopDemoRecording] tv_stoprecord - {activeDemoFile}");
                AddTimer(15, () =>
                {
                    // Snapshot the upload settings on the main thread - the Task.Run below must not
                    // read plugin state while a convar change or ResetMatch could be mutating it.
                    string uploadURL = demoUploadURL;
                    string headerKey = demoUploadHeaderKey;
                    string headerValue = demoUploadHeaderValue;
                    bool useS3 = isDemoUploadS3Enabled;
                    string demoFileName = Path.GetFileName(demoPath);

                    Task.Run(async () =>
                    {
                        bool uploadSuccess = await UploadFileAsync(demoPath, uploadURL, headerKey, headerValue, liveMatchId, currentMapNumber, roundNumber, useS3);

                        // Only report the result when an upload was actually configured, otherwise every
                        // server without a demo upload URL would emit a failed demo_upload_ended per map.
                        if (uploadURL == "") return;

                        await SendEventAsync(new MatchZyDemoUploadedEvent
                        {
                            MatchId = liveMatchId,
                            MapNumber = currentMapNumber,
                            FileName = demoFileName,
                            Success = uploadSuccess,
                        });
                    });
                });
            }
        }

        [ConsoleCommand("get5_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        [ConsoleCommand("matchzy_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        public void DemoUploadHeaderKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string header = command.ArgByIndex(1).Trim();

            if (header != "") demoUploadHeaderKey = header;
        }

        [ConsoleCommand("get5_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        [ConsoleCommand("matchzy_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        public void DemoUploadHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string headerValue = command.ArgByIndex(1).Trim();

            if (headerValue != "") demoUploadHeaderValue = headerValue;
        }

        private string FormatDemoName()
        {
            string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");

            var demoName = demoNameFormat.Replace("{TIME}", formattedTime).Replace("{MATCH_ID}", $"{liveMatchId}").Replace("{MAP}", Server.MapName).Replace("{MAPNUMBER}", matchConfig.CurrentMapNumber.ToString()).Replace("{TEAM1}", matchzyTeam1.teamName).Replace("{TEAM2}", matchzyTeam2.teamName).Replace(" ", "_");
            return $"{demoName}.dem";
        }
    }
}
