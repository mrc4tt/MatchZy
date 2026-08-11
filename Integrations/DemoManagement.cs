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
        }

        private bool IsGOTVEnabled()
        {
            // -nohltv is a bare flag with no value, so it has to be probed with FindParm. The old
            // ParmValue("-nohltv", -1) read whatever token happened to follow it, which both missed a
            // real -nohltv and could misread an unrelated launch option.
            if (CommandLine.HasParam("-nohltv"))
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
        /// Confirm a short while later that GOTV actually produced the demo file, and retry if it did
        /// not. A tv_record that the engine drops (an mp_restartgame landing right after it is the
        /// usual cause) reports nothing at all, so without this the plugin believed it was recording
        /// for the whole match and only the missing file at the end gave it away.
        /// </summary>
        private void VerifyDemoRecording(string expectedDemoFile)
        {
            AddTimer(6.0f, () =>
            {
                // Something else already stopped or replaced the recording - nothing to verify.
                if (!isDemoRecording || activeDemoFile != expectedDemoFile) return;

                string fullPath = Path.Join(Server.GameDirectory + "/csgo/" + expectedDemoFile);
                if (File.Exists(fullPath))
                {
                    Log($"[Demo] Recording confirmed on disk: {expectedDemoFile}");
                    AnnounceDemoStatus(true, "CSTV Recording...");
                    return;
                }

                // Clear the flag first, otherwise the retry is swallowed by the idempotency guard.
                isDemoRecording = false;
                if (demoStartAttempts >= DemoStartMaxAttempts)
                {
                    Log($"[Demo] GOTV demo {expectedDemoFile} never appeared on disk after {demoStartAttempts} attempts - giving up for this map.");
                    AnnounceDemoStatus(false, "CSTV demo could not be started - this match is NOT being recorded.");
                    return;
                }
                Log($"[Demo] GOTV demo {expectedDemoFile} not on disk - retrying tv_record.");
                StartDemoRecording();
            });
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
