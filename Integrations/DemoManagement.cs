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
        // Earlier files of this map's recording, left behind when the watchdog restarted a stalled
        // recording into a new file. Uploaded together with activeDemoFile when the map ends.
        private readonly List<string> previousDemoSegments = new();
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
            // A new match's recording: parts left by an earlier match that was stopped (no
            // StopDemoRecording) must not be uploaded under this match's id.
            previousDemoSegments.Clear();
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
            previousDemoSegments.Clear();
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

            // Never record over an existing demo. A name format without {TIME} gives the watchdog
            // restart (and a replayed map) the same file name, and tv_record would truncate the
            // earlier recording. Add _part2, _part3, ... instead.
            try
            {
                string baseDemoPath = tempDemoPath.Substring(0, tempDemoPath.Length - ".dem".Length);
                for (int part = 2; File.Exists(Path.Join(Server.GameDirectory, "csgo", tempDemoPath)) && part < 100; part++)
                    tempDemoPath = $"{baseDemoPath}_part{part}.dem";
                demoFileName = Path.GetFileName(tempDemoPath);
            }
            catch (Exception ex)
            {
                Log($"[StartDemoRecording] Could not check for an existing demo file: {ex.Message}");
            }
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
            // The engine resolves a relative tv_record path against the first Game search path in
            // gameinfo.gi, which is csgo/addons/metamod on Metamod servers. An absolute path keeps the
            // demo in csgo/, where the folder above was created and the checks and upload look for it.
            string recordPath = Path.Join(Server.GameDirectory, "csgo", tempDemoPath).Replace('\\', '/');
            // Always quoted: the console tokenizer splits an unquoted argument at a space and at the
            // ':' of a Windows drive letter (C:/...).
            string recordArg = $"\"{recordPath}\"";
            // tv_record_immediate 1 makes GOTV write the .dem while the match runs instead of buffering
            // it, so the file is on disk (which is what the verification below checks) and survives a
            // server crash mid-match. Only sent when the build has the convar.
            if (ConVar.Find("tv_record_immediate") != null)
                Server.ExecuteCommand("tv_record_immediate 1");
            Server.ExecuteCommand($"tv_record {recordArg}");
            isDemoRecording = true;
            Log($"[StartDemoRecording] tv_record {recordArg} (attempt {demoStartAttempts}/{DemoStartMaxAttempts})");
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
                // Keep the stalled file: it holds the match up to the stall and is uploaded too.
                if (!string.IsNullOrEmpty(activeDemoFile) && !previousDemoSegments.Contains(activeDemoFile))
                    previousDemoSegments.Add(activeDemoFile);
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
                // No demo exists, so map_result / match_cancelled and the stats export must not name one.
                activeDemoFile = "";
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

        // The demo file map_result / the stats export should name: the current recording, or the last
        // earlier part when a watchdog restart could not start a new one. Set by StopDemoRecording.
        public string lastStoppedDemoFile = "";

        public void StopDemoRecording(string activeDemoFile, long liveMatchId, int currentMapNumber)
        {
            // Earlier segments from a watchdog restart come first, then the current file. The current
            // file only counts while it is actually recording (a failed restart leaves none).
            var segmentFiles = new List<string>(previousDemoSegments);
            if (isDemoRecording && !string.IsNullOrEmpty(activeDemoFile))
                segmentFiles.Add(activeDemoFile);
            previousDemoSegments.Clear();
            lastStoppedDemoFile = segmentFiles.Count > 0 ? segmentFiles[^1] : "";
            var segmentPaths = segmentFiles.Select(f => Path.Join(Server.GameDirectory + "/csgo/" + f)).ToList();
            (int t1score, int t2score) = GetTeamsScore();
            int roundNumber = t1score + t2score;

            // Snapshot the upload settings now, not when the upload timer fires: the series end
            // restores the match config's cvars right after this, which can clear a demo upload URL
            // that came from the match config.
            string uploadURL = demoUploadURL;
            string headerKey = demoUploadHeaderKey;
            string headerValue = demoUploadHeaderValue;
            bool useS3 = isDemoUploadS3Enabled;
            // Also the event target: the upload (15 s plus the transfer) usually finishes after the
            // series ended and the match was reset, when the remote log URL is back to the server's
            // own. (liveMatchId here is this method's parameter, so it is already fixed.)
            long uploadMatchId = liveMatchId;
            var eventTarget = CurrentRemoteLogTarget();

            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
                demoStartPending = false;
                demoWatchdogTimer?.Kill();
                demoWatchdogTimer = null;
                Log($"[StopDemoRecording] tv_stoprecord - {activeDemoFile}");
            }

            // Also runs when the recording is no longer active: parts left by a watchdog restart that
            // then failed still hold the match up to the stall and must be uploaded.
            if (segmentPaths.Count > 0)
            {
                // Counted from now (the upload starts 15 s later) so the empty-server shutdown waits for it.
                bool countsAsUpload = uploadURL != "";
                if (countsAsUpload)
                    Interlocked.Increment(ref _pendingUploads);
                AddTimer(15, () =>
                {
                    Task.Run(async () =>
                    {
                      try
                      {
                        foreach (string segmentPath in segmentPaths)
                        {
                            bool uploadSuccess = await UploadFileAsync(segmentPath, uploadURL, headerKey, headerValue, uploadMatchId, currentMapNumber, roundNumber, useS3);

                            // Only report the result when an upload was actually configured, otherwise every
                            // server without a demo upload URL would emit a failed demo_upload_ended per map.
                            if (uploadURL == "") return;

                            await SendEventAsync(new MatchZyDemoUploadedEvent
                            {
                                MatchId = uploadMatchId,
                                MapNumber = currentMapNumber,
                                FileName = Path.GetFileName(segmentPath),
                                Success = uploadSuccess,
                            }, eventTarget);
                        }
                      }
                      finally
                      {
                        if (countsAsUpload)
                            Interlocked.Decrement(ref _pendingUploads);
                      }
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
