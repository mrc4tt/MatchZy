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

        // Set by scrim/hill going-live: their cfg does mp_restartgame, which clobbers a tv_record
        // fired on a fixed timer. Instead we defer the start to the first live round_start AFTER the
        // restart settles (HandlePostRoundStartEvent), which no fixed delay can race. A fallback timer
        // still fires it if that round_start never arrives.
        public bool demoStartPending = false;

        private bool IsGOTVEnabled()
        {
            // Check for -nohltv flag
            int nohltvValue = NativeAPI.GetCommandParamValue("-nohltv", DataType.DATA_TYPE_INT, -1);
            if (nohltvValue == 1)
            {
                return false;
            }

            // Check for +tv_enable value
            string tvEnable = NativeAPI.GetCommandParamValue("+tv_enable", DataType.DATA_TYPE_STRING, "0");
            if (tvEnable != "1")
            {
                return false;
            }

            return true;
        }

        public void StartDemoRecording()
        {
            // Idempotent: already recording (e.g. pending-flag + fallback timer both fired) -> no-op,
            // don't restart the demo mid-file.
            if (isDemoRecording)
            {
                demoStartPending = false;
                return;
            }
            // Check if GOTV is properly enabled before starting
            if (!IsGOTVEnabled())
            {
                return;
            }
            demoStartPending = false;

            string demoFileName = FormatCvarValue(demoNameFormat.Replace(" ", "_")) + ".dem";
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

                string tempDemoPath = demoPath == "" ? demoFileName : demoPath + demoFileName;
                activeDemoFile = tempDemoPath;
                Server.ExecuteCommand($"tv_record {tempDemoPath}");
                isDemoRecording = true;
            }
            catch (Exception ex)
            {
                Log($"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording with path. Name: {demoFileName}");
                Server.ExecuteCommand($"tv_record {demoFileName}");
                isDemoRecording = true;
            }
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
