using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        public bool isStopCommandAvailable = true;
        public bool pauseAfterRoundRestore = true;
        public string lastBackupFileName = "";
        public string lastMatchZyBackupFileName = "";
        public bool isRoundRestoring = false;
        public bool isSpawnKeeping = false;
        public bool isRoundRestorePending = false;
        public string pendingRestoreFileName = "";
        private Dictionary<ulong, DateTime> pendingRestartConfirmations = new();
        private const int RESTART_CONFIRMATION_TIMEOUT_SECONDS = 30;
        private Dictionary<ulong, DateTime> stopCommandCooldowns = new();
        private const int STOP_COMMAND_COOLDOWN_SECONDS = 3;
        private DateTime stopVoteStartTime = DateTime.MinValue;
        private const int STOP_VOTE_TIMEOUT_SECONDS = 30;
        private Dictionary<ulong, DateTime> pendingRestoreCurrentConfirmations = new();
        private const int RESTORE_CURRENT_CONFIRMATION_TIMEOUT_SECONDS = 15;

        public Dictionary<string, bool> stopData = new() { { "ct", false }, { "t", false } };

        public string backupUploadURL = "";
        public string backupUploadHeaderKey = "";
        public string backupUploadHeaderValue = "";

        // Sanitizes Valve backup script lines before executing them on the server during restore.
        // Blocks commands that can crash or hijack a dedicated server (e.g., playdemo, tv_record, quit).
        private static string SanitizeValveBackup(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input ?? string.Empty;

            var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var filtered = new List<string>();

            // Expand this list as needed
            var blocked = new Regex(
                @"^(playdemo|tv_record|tv_stoprecord|tv_autorecord|stopdemo|demo_(play|record|pause)|quit|exit)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            );

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    filtered.Add(line);
                    continue;
                }
                if (blocked.IsMatch(trimmed))
                    continue; // drop dangerous lines
                filtered.Add(line);
            }
            return string.Join("\n", filtered);
        }

        public void SetupRoundBackupFile()
        {
            string backupFilePrefix = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}";
            Server.ExecuteCommand($"mp_backup_round_file {backupFilePrefix}");
        }

        [ConsoleCommand(
            "css_stop",
            "Restore the backup of the current round (Both teams need to type .stop to restore the current round)"
        )]
        public void OnStopCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;

            if (!isStopCommandAvailable || !isMatchLive)
            {
                return;
            }

            // Check game phase restrictions
            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.backup.stopduringhalftime")
                );
                return;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.backup.stopmatchended")
                );
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.backup.stoptacticaltimeout")
                );
                return;
            }
            if (playerHasTakenDamage && stopCommandNoDamage.Value)
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.restore.stopcommandrequiresnodamage")
                );
                return;
            }

            // Check cooldown per player
            if (stopCommandCooldowns.TryGetValue(player.SteamID, out DateTime lastUse))
            {
                var timeElapsed = (DateTime.Now - lastUse).TotalSeconds;
                if (timeElapsed < STOP_COMMAND_COOLDOWN_SECONDS)
                {
                    ReplyToUserCommand(
                        player,
                        $"Please wait {STOP_COMMAND_COOLDOWN_SECONDS - (int)timeElapsed}s before using .stop again"
                    );
                    return;
                }
            }

            // Check if vote has timed out
            if (stopVoteStartTime != DateTime.MinValue)
            {
                var voteAge = (DateTime.Now - stopVoteStartTime).TotalSeconds;
                if (voteAge > STOP_VOTE_TIMEOUT_SECONDS)
                {
                    // Reset expired vote
                    ResetStopData();
                    PrintLocalizedToAll("matchzy.backup.voteexpired");
                }
            }

            // Validate player team
            if (player.TeamNum != 2 && player.TeamNum != 3)
            {
                return;
            }

            // Update cooldown
            stopCommandCooldowns[player.SteamID] = DateTime.Now;

            // Determine team info
            string stopTeamKey = "";
            string stopTeamName = "";
            string remainingStopTeam = "";

            if (player.TeamNum == 2) // Terrorist
            {
                stopTeamKey = "t";
                stopTeamName = reverseTeamSides["TERRORIST"].teamName;
                remainingStopTeam = reverseTeamSides["CT"].teamName;
            }
            else // CT
            {
                stopTeamKey = "ct";
                stopTeamName = reverseTeamSides["CT"].teamName;
                remainingStopTeam = reverseTeamSides["TERRORIST"].teamName;
            }

            // Check if this team already voted
            if (stopData[stopTeamKey])
            {
                ReplyToUserCommand(
                    player,
                    $"{stopTeamName} has already voted to restore. Waiting for {remainingStopTeam}..."
                );
                return;
            }

            // Start vote timer if this is the first vote
            if (stopVoteStartTime == DateTime.MinValue)
            {
                stopVoteStartTime = DateTime.Now;
            }

            // Register vote
            stopData[stopTeamKey] = true;

            // Check if both teams have voted
            if (stopData["t"] && stopData["ct"])
            {
                // Both teams agreed - restore round
                if (!string.IsNullOrEmpty(lastMatchZyBackupFileName))
                {
                    PrintLocalizedToAll("matchzy.backup.teamsagreed");
                    RestoreRoundBackup(player, lastMatchZyBackupFileName);

                    // Reset stop data after restore
                    AddTimer(0.5f, () => ResetStopData());
                }
                else
                {
                    PrintLocalizedToAll("matchzy.backup.nobackupavailable");
                    Log(
                        $"[OnStopCommand] lastMatchZyBackupFileName not found, unable to restore round!"
                    );
                    ResetStopData();
                }
            }
            else
            {
                // One team voted, waiting for other
                int remainingSeconds =
                    STOP_VOTE_TIMEOUT_SECONDS
                    - (int)(DateTime.Now - stopVoteStartTime).TotalSeconds;

                PrintToAllChat(
                    Localizer["matchzy.restore.teamwantstorestore", stopTeamName, remainingStopTeam]
                );
                PrintLocalizedToAll("matchzy.backup.votepending", remainingSeconds);
            }
        }

        // Add this helper method to reset stop data
        private void ResetStopData()
        {
            stopData["t"] = false;
            stopData["ct"] = false;
            stopVoteStartTime = DateTime.MinValue;
        }

        [ConsoleCommand("css_restorecurrent", "Restores the current round to its beginning")]
        [ConsoleCommand("css_restartround", "Restores the current round to its beginning")]
        [ConsoleCommand("css_rr", "Restores the current round to its beginning")]
        [ConsoleCommand("css_rrestore", "Restores the current round to its beginning")]
        [CommandHelper(minArgs: 0, usage: "[yes]")]
        public void OnRestoreCurrentRoundCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_restorecurrent", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!isMatchLive)
            {
                ReplyToUserCommand(player, "Match is not live!");
                return;
            }

            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, "Cannot restore during halftime.");
                return;
            }

            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, "Cannot restore after match has ended.");
                return;
            }

            // Get current round number
            var gameRules = GetGameRules();
            if (gameRules == null)
            {
                ReplyToUserCommand(player, "Failed to get game rules.");
                return;
            }

            int currentRound = gameRules.TotalRoundsPlayed;
            string round = currentRound.ToString("D2");
            string currentRoundBackup =
                $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";

            // Check if backup exists
            string backupPath = Path.Combine(
                Server.GameDirectory,
                "csgo",
                "MatchZyDataBackup",
                currentRoundBackup
            );

            if (!File.Exists(backupPath))
            {
                ReplyToUserCommand(player, $"Backup for round {currentRound} not found!");
                ReplyToUserCommand(
                    player,
                    "The round may have just started. Try using !restore {currentRound} instead."
                );
                return;
            }

            // Handle confirmation
            string argument = "";
            if (command != null && command.ArgCount >= 2)
            {
                argument = command.ArgByIndex(1).ToLower();
            }

            bool isConfirming = argument == "yes" || argument == "confirm";

            if (!isConfirming)
            {
                // First time - ask for confirmation
                if (player != null)
                {
                    pendingRestoreCurrentConfirmations[player.SteamID] = DateTime.Now;
                }

                ReplyToUserCommand(player, "========================================");
                ReplyToUserCommand(player, $"⚠️  Restart Round {currentRound}?");
                ReplyToUserCommand(player, "This will reload the round from the beginning.");
                ReplyToUserCommand(player, "");
                ReplyToUserCommand(player, $"To confirm, type: !restartround yes or !rr yes");
                ReplyToUserCommand(
                    player,
                    $"(Expires in {RESTORE_CURRENT_CONFIRMATION_TIMEOUT_SECONDS}s)"
                );
                ReplyToUserCommand(player, "========================================");
                return;
            }

            // Check if player has pending confirmation
            if (
                player != null
                && pendingRestoreCurrentConfirmations.TryGetValue(
                    player.SteamID,
                    out DateTime confirmTime
                )
            )
            {
                var elapsed = (DateTime.Now - confirmTime).TotalSeconds;

                if (elapsed > RESTORE_CURRENT_CONFIRMATION_TIMEOUT_SECONDS)
                {
                    pendingRestoreCurrentConfirmations.Remove(player.SteamID);
                    ReplyToUserCommand(player, "❌ Confirmation expired.");
                    return;
                }

                // Valid confirmation - proceed
                pendingRestoreCurrentConfirmations.Remove(player.SteamID);
            }
            else
            {
                ReplyToUserCommand(player, "❌ No pending restore. Use !restartround or !rr");
                return;
            }

            // Announce and restore
            PrintLocalizedToAll("matchzy.backup.restartinground", currentRound);
            RestoreRoundBackup(player, currentRoundBackup);
        }

        [ConsoleCommand("css_restore", "Restores the specified round")]
        public void OnRestoreCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleRestoreCommand(player, commandArg);
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", "!restore <round>")
                );
            }
        }

        [ConsoleCommand("css_restorelast", "Quickly restore the previous round")]
        [ConsoleCommand("css_rl", "Quickly restore the previous round")]
        public void OnRestoreLastCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_restorelast", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!isMatchLive)
            {
                ReplyToUserCommand(player, "Match is not live!");
                return;
            }

            if (!string.IsNullOrEmpty(lastMatchZyBackupFileName))
            {
                ReplyToUserCommand(player, "Restoring last round...");
                RestoreRoundBackup(player, lastMatchZyBackupFileName);
            }
            else
            {
                ReplyToUserCommand(player, "No previous backup found!");
            }
        }

        private void HandleRestoreCommand(CCSPlayerController? player, string commandArg)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (!isMatchLive)
                return;

            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int roundNumber) && roundNumber >= 0)
                {
                    string round = roundNumber.ToString("D2");
                    string requiredBackupFileName =
                        $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    RestoreRoundBackup(player, requiredBackupFileName);
                }
                else
                {
                    ReplyToUserCommand(
                        player,
                        Localizer.ForPlayer(player, "matchzy.backup.restoreinvalidvalue")
                    );
                }
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", "!restore <round>")
                );
            }
        }

        public static string ExtractJsonFileName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            if (!input.Contains('\\') && !input.Contains('/'))
            {
                // If no directory separators are found, return the input as-is
                return input;
            }

            // Find the index of ".json" in the input
            int jsonIndex = input.IndexOf(".json", StringComparison.OrdinalIgnoreCase);
            if (jsonIndex != -1)
            {
                int startIndex = input.LastIndexOfAny(new[] { '\\', '/' }, jsonIndex);

                if (startIndex >= 0)
                {
                    int length = jsonIndex - startIndex + 5;

                    if (length > 0 && startIndex + 1 + length <= input.Length)
                    {
                        string fileName = input.Substring(startIndex + 1, length);
                        return fileName;
                    }
                }
            }

            return string.Empty;
        }

        private void RestoreRoundBackup(CCSPlayerController? player, string fileName)
        {
            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.backup.restoreduringhalftime")
                );
                return;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.backup.restorematchended")
                );
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.backup.restoretacticaltimeout")
                );
                return;
            }
            string backupFolder = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");

            string filePath = Path.Combine(backupFolder, fileName);

            if (!File.Exists(filePath))
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.backup.restoredoesntexist", fileName)
                );
                return;
            }

            var gameRules = GetGameRules();
            if (gameRules == null)
            {
                ReplyToUserCommand(player, "Failed to get game rules.");
                return;
            }
            bool liveSetupRequired = false;

            // We set active timeouts to false so that timeout does not start after the round has been restored.
            // This is to prevent any buggish behaviour with timeouts (like incorrect timeout used showing, or force-unpausing the match once timeout ends)
            gameRules.CTTimeOutActive = gameRules.TerroristTimeOutActive = false;

            // Server.ExecuteCommand($"mp_backup_restore_load_file {fileName}");

            Dictionary<string, string> backupData = new();
            try
            {
                using (StreamReader fileReader = File.OpenText(filePath))
                {
                    string jsonContent = fileReader.ReadToEnd();
                    if (!string.IsNullOrEmpty(jsonContent))
                    {
                        JsonSerializerOptions options = new() { AllowTrailingCommas = true };
                        backupData =
                            JsonSerializer.Deserialize<Dictionary<string, string>>(
                                jsonContent,
                                options
                            ) ?? new Dictionary<string, string>();
                    }
                    else
                    {
                        // Handle the case where the JSON content is empty or null
                        backupData = new();
                    }
                }

                // MatchID is set first to avoid generating a new one.
                if (backupData.TryGetValue("matchid", out var matchId)
                    && long.TryParse(matchId, out var parsedBackupId)
                    && parsedBackupId > 0)
                {
                    liveMatchId = parsedBackupId;
                }
                else if (matchId != null)
                {
                    Log($"[BackupRestore] Backup contains invalid matchid='{matchId}'; ignoring.");
                }
                if (backupData.TryGetValue("match_loaded", out var matchLoaded))
                {
                    isMatchSetup = bool.Parse(matchLoaded);
                }
                if (backupData.TryGetValue("match_config", out var matchConfigValue))
                {
                    matchConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchConfig>(
                        matchConfigValue
                    )!;
                    SetupRoundBackupFile();
                }
                if (backupData.TryGetValue("team1", out var team1config))
                {
                    var _t1 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team1config);
                    if (_t1 != null)
                        matchzyTeam1 = _t1;
                    else
                        Console.WriteLine(
                            "[MatchZy] [RestoreRoundBackup] team1 deserialization returned null."
                        );
                }
                if (backupData.TryGetValue("team2", out var team2config))
                {
                    var _t2 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team2config);
                    if (_t2 != null)
                        matchzyTeam2 = _t2;
                    else
                        Console.WriteLine(
                            "[MatchZy] [RestoreRoundBackup] team2 deserialization returned null."
                        );
                }
                if (backupData.TryGetValue("team1_side", out var team1Side))
                {
                    if (team1Side == "CT")
                    {
                        teamSides[matchzyTeam1] = "CT";
                        reverseTeamSides["CT"] = matchzyTeam1;
                        teamSides[matchzyTeam2] = "TERRORIST";
                        reverseTeamSides["TERRORIST"] = matchzyTeam2;
                        // SwapSidesInTeamData(false);
                    }
                    else if (team1Side == "TERRORIST")
                    {
                        teamSides[matchzyTeam1] = "TERRORIST";
                        reverseTeamSides["TERRORIST"] = matchzyTeam1;
                        teamSides[matchzyTeam2] = "CT";
                        reverseTeamSides["CT"] = matchzyTeam2;
                        // SwapSidesInTeamData(false);
                    }
                }
                if (backupData.TryGetValue("map_name", out var map_name))
                {
                    if (map_name != Server.MapName)
                    {
                        ChangeMap(map_name, 0);
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        // Returning from here, backup will be restored again once the map is changed.
                        return;
                    }
                }

                // This is done after checking map_name so that we load the correct map first
                if (gameRules.WarmupPeriod)
                {
                    if (!isRoundRestorePending)
                    {
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        PrintToAllChat(Localizer["matchzy.restore.loadedsuccessfully", fileName]);
                        return;
                    }
                    else
                    {
                        liveSetupRequired = true;
                    }
                }
                if (backupData.TryGetValue("TerroristTimeOuts", out var terroristTimeouts))
                {
                    gameRules.TerroristTimeOuts = int.Parse(terroristTimeouts);
                }

                if (backupData.TryGetValue("CTTimeOuts", out var ctTimeouts))
                {
                    gameRules.CTTimeOuts = int.Parse(ctTimeouts);
                }
                if (backupData.TryGetValue("valve_backup", out var valveBackup))
                {
                    string tempFileName = fileName.Replace(".json", ".txt");
                    if (backupData.TryGetValue("round", out var roundNumber))
                    {
                        tempFileName =
                            $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{roundNumber}.txt";
                    }
                    string tempFilePath = Path.Combine(Server.GameDirectory, "csgo", tempFileName);

                    if (!File.Exists(tempFilePath))
                    {
                        var safeScript = SanitizeValveBackup(valveBackup);
                        File.WriteAllText(tempFilePath, safeScript);
                    }
                    int restoreTimer = liveSetupRequired ? 2 : 0;
                    if (liveSetupRequired)
                    {
                        SetupLiveFlagsAndCfg();
                    }
                    AddTimer(
                        restoreTimer,
                        () =>
                        {
                            string fileName = Path.GetFileName(tempFilePath);

                            isRoundRestoring = true;
                            isSpawnKeeping = true;
                            Server.ExecuteCommand($"mp_backup_restore_load_file {fileName}");
                        }
                    );
                    // AddTimer(5, () => File.Delete(tempFilePath));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[MatchZy] [RestoreRoundBackup - FATAL] {e}");
                return;
            }

            PrintToAllChat(Localizer["matchzy.restore.restoredsuccessfully", fileName]);
            if (pauseAfterRoundRestore)
            {
                Server.ExecuteCommand("mp_pause_match;");
                stopData["ct"] = false;
                stopData["t"] = false;
                isPaused = true;
                unpauseData["pauseTeam"] = "RoundRestore";
                pausedStateTimer ??= AddTimer(
                    chatTimerDelay,
                    SendPausedStateMessage,
                    TimerFlags.REPEAT
                );
            }
        }

        public void CreateMatchZyRoundDataBackup()
        {
            if (!isMatchLive || isRoundRestoring)
                return;
            try
            {
                (int t1score, int t2score) = GetTeamsScore();
                int roundNumber = t1score + t2score;
                string round = roundNumber.ToString("D2");
                string matchZyBackupFileName =
                    $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                string filePath = Path.Combine(
                    Server.GameDirectory,
                    "csgo",
                    "MatchZyDataBackup",
                    matchZyBackupFileName
                );

                string? directoryPath = Path.GetDirectoryName(filePath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var gameRules = Utilities
                    .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                    .First()
                    .GameRules!;
                string lastBackupFilePath =
                    $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt";
                ;
                bool lastBackupExists = File.Exists(
                    Path.Combine(Server.GameDirectory, "csgo", lastBackupFilePath)
                );
                lastBackupFilePath = Path.Combine(Server.GameDirectory, "csgo", lastBackupFilePath);

                string valveBackupContent = lastBackupExists
                    ? File.ReadAllText(lastBackupFilePath)
                    : "";

                Dictionary<string, string> roundData = new()
                {
                    { "matchid", liveMatchId.ToString() },
                    { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "map_name", Server.MapName },
                    { "mapnumber", matchConfig.CurrentMapNumber.ToString() },
                    { "round", round },
                    { "team1", GetTeamConfig("team1") },
                    { "team2", GetTeamConfig("team2") },
                    { "team1_name", matchzyTeam1.teamName },
                    { "team1_flag", matchzyTeam1.teamFlag },
                    { "team1_tag", matchzyTeam1.teamTag },
                    { "team1_side", teamSides[matchzyTeam1] },
                    { "team2_name", matchzyTeam2.teamName },
                    { "team2_flag", matchzyTeam2.teamFlag },
                    { "team2_tag", matchzyTeam2.teamTag },
                    { "team2_side", teamSides[matchzyTeam2] },
                    { "team1_score", t1score.ToString() },
                    { "team2_score", t2score.ToString() },
                    { "team1_series_score", matchzyTeam1.seriesScore.ToString() },
                    { "team2_series_score", matchzyTeam2.seriesScore.ToString() },
                    { "TerroristTimeOuts", gameRules.TerroristTimeOuts.ToString() },
                    { "CTTimeOuts", gameRules.CTTimeOuts.ToString() },
                    { "match_loaded", isMatchSetup.ToString() },
                    { "match_config", GetMatchConfig() },
                    { "valve_backup", SanitizeValveBackup(valveBackupContent) },
                };
                JsonSerializerOptions options = new() { WriteIndented = true };
                string defaultJson = JsonSerializer.Serialize(roundData, options);

                File.WriteAllText(filePath, defaultJson);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[MatchZy] [Exception] {e}");
            }
        }

        public List<string> GetBackups(string matchID)
        {
            string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");

            if (!Directory.Exists(backupDir))
            {
                return [];
            }

            var directoryInfo = new DirectoryInfo(backupDir);
            var files = directoryInfo.GetFiles();

            var pattern = $"matchzy_{matchID}_";
            var backups = new List<string>();

            foreach (var file in files)
            {
                if (file.Name.Contains(pattern))
                {
                    backups.Add(file.FullName);
                }
            }

            backups.Sort((x, y) => string.Compare(y, x, StringComparison.Ordinal));
            return backups;
        }

        public string GetBackupInfo(string filePath)
        {
            string info = "";
            if (!File.Exists(filePath))
            {
                return "";
            }

            Dictionary<string, string> backupData = new();
            try
            {
                using (StreamReader fileReader = File.OpenText(filePath))
                {
                    string jsonContent = fileReader.ReadToEnd();
                    if (string.IsNullOrEmpty(jsonContent))
                    {
                        return "";
                    }
                    else
                    {
                        JsonSerializerOptions options = new() { AllowTrailingCommas = true };
                        backupData =
                            JsonSerializer.Deserialize<Dictionary<string, string>>(
                                jsonContent,
                                options
                            ) ?? new Dictionary<string, string>();
                    }
                }

                info =
                    $"{filePath.Split("/")[^1]} {backupData["timestamp"]} {backupData["team1_name"]} {backupData["team2_name"]} {backupData["map_name"]} {backupData["team1_score"]} {backupData["team2_score"]}";
            }
            catch (Exception e)
            {
                Console.WriteLine($"[MatchZy] [Exception] {e}");
                return "";
            }

            return info;
        }

        public string GetMatchConfig()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(matchConfig);
        }

        public string GetTeamConfig(string team)
        {
            Team teamConfig = team == "team1" ? matchzyTeam1 : matchzyTeam2;
            return Newtonsoft.Json.JsonConvert.SerializeObject(teamConfig);
        }

        [ConsoleCommand("get5_loadbackup", "Restore the backup from the provided file")]
        [ConsoleCommand("matchzy_loadbackup", "Restore the backup from the provided file")]
        [CommandHelper(minArgs: 1, usage: "<backup_file_name>")]
        public void OnLoadBackupCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            // var fileName = command.GetArg(1);
            var fileName = ExtractJsonFileName(command.ArgString);

            RestoreRoundBackup(player, fileName);
        }

        [ConsoleCommand("css_backupmenu", "Shows available backups with restore commands")]
        [ConsoleCommand("css_backups", "Shows available backups with restore commands")]
        [ConsoleCommand("css_backup", "Shows available backups with restore commands")]
        public void OnBackupMenuCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_backupmenu", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!isMatchLive)
            {
                ReplyToUserCommand(player, "Match is not live!");
                return;
            }

            List<string> backups = GetBackups(liveMatchId.ToString());

            if (backups.Count == 0)
            {
                ReplyToUserCommand(player, "No backups found for this match.");
                return;
            }

            // Show current match context
            (int t1score, int t2score) = GetTeamsScore();
            int currentRound = t1score + t2score;
            ReplyToUserCommand(
                player,
                $"Current: Round {currentRound} — {ChatColors.Green}{matchzyTeam1.teamName} {t1score}-{t2score} {matchzyTeam2.teamName}"
            );
            ReplyToUserCommand(player, "───────────────────────────────────");

            int displayed = 0;
            foreach (string backupPath in backups)
            {
                if (displayed >= 10)
                    break; // Limit to 10 most recent

                string fileName = Path.GetFileName(backupPath);
                var roundMatch = System.Text.RegularExpressions.Regex.Match(
                    fileName,
                    @"round(\d+)"
                );

                if (!roundMatch.Success)
                    continue; // Skip non-standard backups

                int roundNum = int.Parse(roundMatch.Groups[1].Value);

                // Parse backup JSON directly for better reliability
                var backupData = ParseBackupFile(backupPath);
                if (backupData == null)
                    continue;

                string team1 = backupData.GetValueOrDefault("team1_name", "");
                string team2 = backupData.GetValueOrDefault("team2_name", "");
                string score1 = backupData.GetValueOrDefault("team1_score", "0");
                string score2 = backupData.GetValueOrDefault("team2_score", "0");
                string timestamp = backupData.GetValueOrDefault("timestamp", "");

                // Fallback to CT/T if team names are empty or default
                if (string.IsNullOrWhiteSpace(team1) || team1 == "team1")
                    team1 = "CT";
                if (string.IsNullOrWhiteSpace(team2) || team2 == "team2")
                    team2 = "T";

                // Determine which half
                int totalScore = int.Parse(score1) + int.Parse(score2);
                int maxRounds = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 24;
                int halfRounds = maxRounds / 2;
                string halfLabel =
                    totalScore <= halfRounds ? "1st"
                    : totalScore <= maxRounds ? "2nd"
                    : "OT";

                // Time ago
                string timeAgo = "";
                if (DateTime.TryParse(timestamp, out DateTime backupTime))
                {
                    var diff = DateTime.Now - backupTime;
                    timeAgo =
                        diff.TotalMinutes < 1 ? "just now"
                        : diff.TotalMinutes < 60 ? $"{(int)diff.TotalMinutes}m ago"
                        : $"{(int)diff.TotalHours}h {diff.Minutes}m ago";
                }

                ReplyToUserCommand(
                    player,
                    $"  {ChatColors.Yellow}R{roundNum}{ChatColors.Default}"
                        + $" | {score1}-{score2}"
                        + $" ({halfLabel})"
                        + $" {ChatColors.Grey}{timeAgo}{ChatColors.Default}"
                        + $" → {ChatColors.Green}!restore {roundNum}"
                );

                displayed++;
            }

            if (displayed == 0)
            {
                ReplyToUserCommand(player, "No valid round backups found.");
            }
            else
            {
                ReplyToUserCommand(player, "───────────────────────────────────");
                ReplyToUserCommand(
                    player,
                    $"Tip: {ChatColors.Green}!restore <round>{ChatColors.Default}"
                        + $" or {ChatColors.Green}!restorelast{ChatColors.Default} for previous round"
                );
            }
        }

        // Add this helper method to parse backup files directly
        private Dictionary<string, string>? ParseBackupFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string jsonContent = File.ReadAllText(filePath);
                if (string.IsNullOrEmpty(jsonContent))
                {
                    return null;
                }

                JsonSerializerOptions options = new() { AllowTrailingCommas = true };

                return JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options);
            }
            catch (Exception e)
            {
                Log($"[ParseBackupFile] Error parsing {filePath}: {e.Message}");
                return null;
            }
        }

        [ConsoleCommand("css_listbackups", "List all the backups for the provided matchid")]
        [ConsoleCommand("get5_listbackups", "List all the backups for the provided matchid")]
        [ConsoleCommand("matchzy_listbackups", "List all the backups for the provided matchid")]
        public void OnListBackupCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            var matchId = command.ArgCount >= 2 ? command.GetArg(1) : liveMatchId.ToString();
            List<string> backups = GetBackups(matchId);

            if (backups.Count == 0)
            {
                command.ReplyToCommand($"Found no backup files for match ID: {matchId}");
                return; // FIX: Add return here
            }

            // Header
            command.ReplyToCommand($"=== Backups for Match {matchId} ({backups.Count} found) ===");

            int index = 1;
            foreach (string backup in backups)
            {
                string backupInfo = GetBackupInfo(backup);

                if (!string.IsNullOrEmpty(backupInfo))
                {
                    // Parse the space-separated info
                    var parts = backupInfo.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 7)
                    {
                        string fileName = parts[0];
                        string timestamp = parts[1];
                        string team1 = parts[2];
                        string team2 = parts[3];
                        string map = parts[4];
                        string score1 = parts[5];
                        string score2 = parts[6];

                        // Extract round number from filename (e.g., "matchzy_123_1_round05.json" -> "5")
                        var roundMatch = System.Text.RegularExpressions.Regex.Match(
                            fileName,
                            @"round(\d+)"
                        );
                        string roundNum = roundMatch.Success
                            ? int.Parse(roundMatch.Groups[1].Value).ToString()
                            : "?";

                        // Format: "#1 | Round 5 | Team1 2 - 3 Team2 | de_dust2 | 2024-01-15 14:30:22"
                        command.ReplyToCommand(
                            $"#{index} | Round {roundNum} | {team1} {score1} - {score2} {team2} | {map} | {timestamp}"
                        );
                    }
                    else
                    {
                        // Fallback if format is unexpected
                        command.ReplyToCommand($"#{index} | {backupInfo}");
                    }
                }
                else
                {
                    // If GetBackupInfo failed, show just the filename
                    command.ReplyToCommand($"#{index} | {Path.GetFileName(backup)}");
                }

                index++;
            }

            command.ReplyToCommand($"Use '!restore <round>' to restore a specific round");
        }
    }
}
