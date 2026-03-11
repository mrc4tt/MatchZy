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

        private bool IsGOTVEnabled()
        {
            // Check for -nohltv flag
            int nohltvValue = NativeAPI.GetCommandParamValue("-nohltv", DataType.DATA_TYPE_INT, -1);
            if (nohltvValue == 1)
            {
                return false;
            }

            // Check for +tv_enable value
            string tvEnable = NativeAPI.GetCommandParamValue(
                "+tv_enable",
                DataType.DATA_TYPE_STRING,
                "0"
            );
            if (tvEnable != "1")
            {
                return false;
            }

            return true;
        }

        public void StartDemoRecording()
        {
            // Check if GOTV is properly enabled before starting
            if (!IsGOTVEnabled())
            {
                return;
            }

            string demoFileName = FormatCvarValue(demoNameFormat.Replace(" ", "_")) + ".dem";
            try
            {
                string? directoryPath = Path.GetDirectoryName(
                    Path.Join(Server.GameDirectory + "/csgo/" + demoPath)
                );
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
                Log(
                    $"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording with path. Name: {demoFileName}"
                );
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
            }
        }

        private string FormatDemoName()
        {
            string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");

            var demoName = demoNameFormat
                .Replace("{TIME}", formattedTime)
                .Replace("{MATCH_ID}", $"{liveMatchId}")
                .Replace("{MAP}", Server.MapName)
                .Replace("{MAPNUMBER}", matchConfig.CurrentMapNumber.ToString())
                .Replace("{TEAM1}", matchzyTeam1.teamName)
                .Replace("{TEAM2}", matchzyTeam2.teamName)
                .Replace(" ", "_");
            return $"{demoName}.dem";
        }
    }
}
