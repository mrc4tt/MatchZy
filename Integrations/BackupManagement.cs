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
        public CounterStrikeSharp.API.Modules.Timers.Timer? restoreUnpauseTimer = null;
        private int restoreUnpauseSecondsLeft = 0;
        private Dictionary<ulong, DateTime> pendingRestartConfirmations = new();
        private const int RESTART_CONFIRMATION_TIMEOUT_SECONDS = 30;
        private Dictionary<ulong, DateTime> stopCommandCooldowns = new();
        private const int STOP_COMMAND_COOLDOWN_SECONDS = 3;
        private DateTime stopVoteStartTime = DateTime.MinValue;
        private const int STOP_VOTE_TIMEOUT_SECONDS = 30;

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
            var blocked = new Regex(@"^(playdemo|tv_record|tv_stoprecord|tv_autorecord|stopdemo|demo_(play|record|pause)|quit|exit)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        [ConsoleCommand("css_stop", "Restore the backup of the current round (Both teams need to type .stop to restore the current round)")]
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.stopduringhalftime"));
                return;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.stopmatchended"));
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.stoptacticaltimeout"));
                return;
            }
            if (playerHasTakenDamage && stopCommandNoDamage.Value)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.restore.stopcommandrequiresnodamage"));
                return;
            }

            // Check cooldown per player
            if (stopCommandCooldowns.TryGetValue(player.SteamID, out DateTime lastUse))
            {
                var timeElapsed = (DateTime.Now - lastUse).TotalSeconds;
                if (timeElapsed < STOP_COMMAND_COOLDOWN_SECONDS)
                {
                    ReplyToUserCommand(player, $"Please wait {STOP_COMMAND_COOLDOWN_SECONDS - (int)timeElapsed}s before using .stop again");
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
                ReplyToUserCommand(player, $"{stopTeamName} has already voted to restore. Waiting for {remainingStopTeam}...");
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
                    Log($"[OnStopCommand] lastMatchZyBackupFileName not found, unable to restore round!");
                    ResetStopData();
                }
            }
            else
            {
                // One team voted, waiting for other
                int remainingSeconds = STOP_VOTE_TIMEOUT_SECONDS - (int)(DateTime.Now - stopVoteStartTime).TotalSeconds;

                PrintToAllChat(Localizer["matchzy.restore.teamwantstorestore", stopTeamName, remainingStopTeam]);
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
        [CommandHelper(minArgs: 0, usage: "")]
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
            string currentRoundBackup = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";

            // Check if backup exists
            string backupPath = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup", currentRoundBackup);

            if (!File.Exists(backupPath))
            {
                ReplyToUserCommand(player, $"Backup for round {currentRound} not found!");
                ReplyToUserCommand(player, $"The round may have just started. Try using !restore {currentRound} instead.");
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", "!restore <round>"));
            }
        }

        [ConsoleCommand("css_restorelast", "Quickly restore the previous round")]
        [ConsoleCommand("css_rl", "Quickly restore the previous round")]
        public void OnRestoreLastCommand(CCSPlayerController? player, CommandInfo? command)
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
                    string requiredBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    RestoreRoundBackup(player, requiredBackupFileName);
                }
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoreinvalidvalue"));
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", "!restore <round>"));
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoreduringhalftime"));
                return;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restorematchended"));
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoretacticaltimeout"));
                return;
            }
            string backupFolder = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");

            string filePath = Path.Combine(backupFolder, fileName);

            if (!File.Exists(filePath))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoredoesntexist", fileName));
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
                        backupData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();
                    }
                    else
                    {
                        // Handle the case where the JSON content is empty or null
                        backupData = new();
                    }
                }

                // MatchID is set first to avoid generating a new one.
                if (backupData.TryGetValue("matchid", out var matchId) && long.TryParse(matchId, out var parsedBackupId) && parsedBackupId > 0)
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
                    matchConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchConfig>(matchConfigValue)!;
                    SetupRoundBackupFile();
                }
                if (backupData.TryGetValue("team1", out var team1config))
                {
                    var _t1 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team1config);
                    if (_t1 != null)
                        matchzyTeam1 = _t1;
                    else
                        Console.WriteLine("[MatchZy] [RestoreRoundBackup] team1 deserialization returned null.");
                }
                if (backupData.TryGetValue("team2", out var team2config))
                {
                    var _t2 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team2config);
                    if (_t2 != null)
                        matchzyTeam2 = _t2;
                    else
                        Console.WriteLine("[MatchZy] [RestoreRoundBackup] team2 deserialization returned null.");
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
                {
                    backupData.TryGetValue("valve_backup", out var valveBackup);

                    string csgoDir = Path.Combine(Server.GameDirectory, "csgo");
                    // The .txt the engine itself wrote for this round, if it is still around. Two names
                    // can point at it: the one built from the current match id, and the one carried by
                    // the JSON backup's own file name (they differ once the match id changed since).
                    string tempFileName = fileName.Replace(".json", ".txt");
                    if (backupData.TryGetValue("round", out var roundNumber))
                    {
                        tempFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{roundNumber}.txt";
                    }
                    string tempFilePath = Path.Combine(csgoDir, tempFileName);

                    var safeScript = SanitizeValveBackup(valveBackup);
                    if (!string.IsNullOrWhiteSpace(safeScript))
                    {
                        // Always write the copy carried by the JSON backup. Only writing when the file is
                        // absent means a stale or truncated .txt left in csgo/ by an earlier match with the
                        // same match id and round number gets loaded instead, and the engine fails silently.
                        if (File.Exists(tempFilePath))
                        {
                            long existingLength = new FileInfo(tempFilePath).Length;
                            if (existingLength != safeScript.Length)
                            {
                                Log($"[RestoreRoundBackup] {tempFileName} on disk is {existingLength} bytes, backup carries {safeScript.Length}. Overwriting with the backup copy.");
                            }
                        }
                        File.WriteAllText(tempFilePath, safeScript);
                        Log($"[RestoreRoundBackup] Wrote {tempFilePath} ({safeScript.Length} bytes) for restore of {fileName}.");
                    }
                    else
                    {
                        // The JSON snapshot has no embedded copy: the engine had not written its own round
                        // file yet when the snapshot was taken (round_start races mp_backup_round_auto).
                        // The engine file usually lands a moment later and is still on disk, so load that
                        // one instead of refusing the restore.
                        string? diskBackup = FindValveRoundBackupOnDisk(csgoDir, tempFileName, fileName);
                        if (diskBackup == null)
                        {
                            // Nothing to load: the round would stay exactly as it is while we announce a
                            // successful restore and pause the match. Report it instead.
                            Log($"[RestoreRoundBackup] {fileName} has no valve_backup data and no matching .txt in csgo/, nothing to restore.");
                            ReplyToUserCommand(player, $"Backup {fileName} contains no round data, nothing was restored.");
                            return;
                        }

                        tempFilePath = diskBackup;
                        tempFileName = Path.GetFileName(tempFilePath);
                        Log($"[RestoreRoundBackup] {fileName} carries no valve_backup, falling back to {tempFileName} ({new FileInfo(tempFilePath).Length} bytes) on disk.");
                    }

                    int restoreTimer = liveSetupRequired ? 2 : 0;
                    if (liveSetupRequired)
                    {
                        SetupLiveFlagsAndCfg();
                    }
                    // Scoreboard state belonging to the restored round, applied once the engine is done.
                    string scoreboardJson = backupData.GetValueOrDefault("scoreboard", "");
                    int restoredRoundsPlayed = 0;
                    if (backupData.TryGetValue("round", out var restoredRound))
                    {
                        int.TryParse(restoredRound, out restoredRoundsPlayed);
                    }
                    AddTimer(
                        restoreTimer,
                        () =>
                        {
                            var rules = GetGameRules();
                            if (rules == null)
                            {
                                Log($"[RestoreRoundBackup FATAL] Game rules unavailable, cannot load {tempFileName}.");
                                return;
                            }

                            int preRoundsPlayed = rules.TotalRoundsPlayed;
                            (int preTeam1Score, int preTeam2Score) = GetTeamsScore();
                            string loadFileName = Path.GetFileName(tempFilePath);

                            isRoundRestoring = true;
                            isSpawnKeeping = true;
                            Log(
                                $"[RestoreRoundBackup] Loading {loadFileName}. Rounds played: {preRoundsPlayed}, score: {preTeam1Score}-{preTeam2Score}, target round: {restoredRoundsPlayed}."
                            );
                            Server.ExecuteCommand($"mp_backup_restore_load_file {loadFileName}");
                            Server.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
                            Server.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");
                            // Settle the pause state after the load, not before it: the live cfgs set
                            // mp_backup_restore_load_autopause 1, so the engine pauses on its own here and
                            // our own pause/unpause has to be the last thing that touches it.
                            AddTimer(1.0f, HandleRestorePauseState);
                            // The engine rewrites the scoreboard while it loads the backup, so roll it
                            // back afterwards. Applied twice because the round restart that follows the
                            // load can land between the two.
                            AddTimer(1.2f, () => RestoreScoreboardState(scoreboardJson, restoredRoundsPlayed));
                            AddTimer(3.0f, () => RestoreScoreboardState(scoreboardJson, restoredRoundsPlayed));
                            AddTimer(3.5f, () => VerifyRoundRestore(player, fileName, restoredRoundsPlayed, preRoundsPlayed, preTeam1Score, preTeam2Score));
                        }
                    );
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[MatchZy] [RestoreRoundBackup - FATAL] {e}");
                return;
            }
            // The result is announced from VerifyRoundRestore instead of here: at this point the load
            // command has only been queued, so announcing a successful restore now is a guess.
        }

        // Locates the round file the engine wrote itself (mp_backup_round_auto) for a MatchZy JSON backup
        // that carries no embedded copy. Two names can point at the same round: the one built from the
        // current match id and map number, and the JSON backup's own name with a .txt extension. Both are
        // checked, and an empty file counts as not found.
        private static string? FindValveRoundBackupOnDisk(string csgoDir, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string candidate = Path.Combine(csgoDir, Path.GetFileNameWithoutExtension(name) + ".txt");
                if (File.Exists(candidate) && new FileInfo(candidate).Length > 0)
                {
                    return candidate;
                }
            }
            return null;
        }

        // mp_backup_restore_load_file fails silently. A missing, stale or malformed .txt leaves the round
        // exactly as it was while MatchZy has already announced a restore and paused the match, which
        // looks to everyone like "the command did nothing". Compare the round counter against what the
        // backup said it should be and report what actually happened.
        private void VerifyRoundRestore(CCSPlayerController? player, string fileName, int expectedRoundsPlayed, int preRoundsPlayed, int preTeam1Score, int preTeam2Score)
        {
            var rules = GetGameRules();
            if (rules == null)
                return;

            int roundsPlayed = rules.TotalRoundsPlayed;
            (int team1Score, int team2Score) = GetTeamsScore();
            Log(
                $"[RestoreRoundBackup] Post-load state for {fileName}: rounds played {preRoundsPlayed} -> {roundsPlayed} (expected {expectedRoundsPlayed}), score {preTeam1Score}-{preTeam2Score} -> {team1Score}-{team2Score}."
            );

            // Restoring the round that is already loaded cannot move the counter, so there is nothing to
            // check. Same when the counter did land on the round the backup was taken at.
            if (preRoundsPlayed == expectedRoundsPlayed || roundsPlayed == expectedRoundsPlayed)
            {
                PrintToAllChat(Localizer["matchzy.restore.restoredsuccessfully", fileName]);
                return;
            }

            // The engine ignored the file. Clear the restore flags: isRoundRestoring gates
            // CreateMatchZyRoundDataBackup, and it is normally cleared by the round start that a
            // successful load triggers, so leaving it set here would stop every later round backup.
            isRoundRestoring = false;
            isSpawnKeeping = false;
            Log($"[RestoreRoundBackup FATAL] Engine did not load {fileName}. Rounds played is still {roundsPlayed}, expected {expectedRoundsPlayed}.");
            PrintToAllChat($"{ChatColors.Red}Restore of {fileName} failed.{ChatColors.Default} The server did not load the round backup, match state is unchanged.");
            if (IsPlayerValid(player))
            {
                ReplyToUserCommand(player, "mp_backup_restore_load_file did not take effect. See the server console for the backup file details.");
            }
        }

        // Brings MatchZy's pause state in line with what the engine did after a backup was loaded.
        // With matchzy_pause_after_restore enabled we own the pause, and matchzy_restore_unpause_delay
        // decides whether it is lifted automatically or has to be unpaused manually. With it disabled we
        // must actively unpause, because the engine's own mp_backup_restore_load_autopause already paused
        // the match and MatchZy would think the game is running (leaving .unpause doing nothing).
        private void HandleRestorePauseState()
        {
            restoreUnpauseTimer?.Kill();
            restoreUnpauseTimer = null;

            if (!pauseAfterRoundRestore)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                unpauseData["pauseTeam"] = "";
                pausedStateTimer?.Kill();
                pausedStateTimer = null;
                return;
            }

            Server.ExecuteCommand("mp_pause_match;");
            stopData["ct"] = false;
            stopData["t"] = false;
            isPaused = true;
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            unpauseData["pauseTeam"] = "RoundRestore";

            if (!restoreAutoUnpause.Value)
            {
                // Manual: both teams (or an admin) have to use .unpause.
                pausedStateTimer ??= AddTimer(chatTimerDelay, SendPausedStateMessage, TimerFlags.REPEAT);
                return;
            }

            int delay = Math.Max(1, restoreUnpauseDelay.Value);
            restoreUnpauseSecondsLeft = delay;
            PrintToAllChat($"Round restored. Match unpauses in {ChatColors.Green}{delay}{ChatColors.Default} seconds. Use {ChatColors.Green}.pause{ChatColors.Default} if you are not ready.");
            restoreUnpauseTimer = AddTimer(
                1.0f,
                () =>
                {
                    // A manual unpause, or any other pause taking over, cancels the countdown.
                    if (!isPaused || (string)unpauseData["pauseTeam"] != "RoundRestore")
                    {
                        restoreUnpauseTimer?.Kill();
                        restoreUnpauseTimer = null;
                        return;
                    }

                    restoreUnpauseSecondsLeft--;
                    if (restoreUnpauseSecondsLeft > 0)
                    {
                        if (restoreUnpauseSecondsLeft <= 5 || restoreUnpauseSecondsLeft % 10 == 0)
                        {
                            PrintToAllChat($"Unpausing in {ChatColors.Green}{restoreUnpauseSecondsLeft}{ChatColors.Default}...");
                        }
                        return;
                    }

                    restoreUnpauseTimer?.Kill();
                    restoreUnpauseTimer = null;
                    Server.ExecuteCommand("mp_unpause_match;");
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;
                    unpauseData["pauseTeam"] = "";
                    pausedStateTimer?.Kill();
                    pausedStateTimer = null;
                    PrintToAllChat($"{ChatColors.Green}Match is live!{ChatColors.Default}");
                },
                TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE
            );
        }

        // One player's scoreboard line as it looked when the backup was written.
        private class ScoreboardSnapshot
        {
            public string SteamId { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsBot { get; set; }
            public int Score { get; set; }
            public int Mvps { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }
            public int Assists { get; set; }
            public int Damage { get; set; }
            public int HeadShotKills { get; set; }
            public int EnemiesFlashed { get; set; }
            public int UtilityDamage { get; set; }
            public int Objective { get; set; }
            public int EquipmentValue { get; set; }
            public int MoneySaved { get; set; }
            public int KillReward { get; set; }
            public int LiveTime { get; set; }
            public int CashEarned { get; set; }
        }

        private string CaptureScoreboardSnapshot()
        {
            var snapshot = new List<ScoreboardSnapshot>();
            try
            {
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || p.IsHLTV)
                        continue;
                    var stats = p.ActionTrackingServices?.MatchStats;
                    if (stats == null)
                        continue;
                    snapshot.Add(
                        new ScoreboardSnapshot
                        {
                            SteamId = p.IsBot ? "" : p.SteamID.ToString(),
                            Name = p.PlayerName,
                            IsBot = p.IsBot,
                            Score = p.Score,
                            Mvps = p.MVPs,
                            Kills = stats.Kills,
                            Deaths = stats.Deaths,
                            Assists = stats.Assists,
                            Damage = stats.Damage,
                            HeadShotKills = stats.HeadShotKills,
                            EnemiesFlashed = stats.EnemiesFlashed,
                            UtilityDamage = stats.UtilityDamage,
                            Objective = stats.Objective,
                            EquipmentValue = stats.EquipmentValue,
                            MoneySaved = stats.MoneySaved,
                            KillReward = stats.KillReward,
                            LiveTime = stats.LiveTime,
                            CashEarned = stats.CashEarned,
                        }
                    );
                }
            }
            catch (Exception e)
            {
                Log($"[CaptureScoreboardSnapshot] {e.Message}");
            }
            return JsonSerializer.Serialize(snapshot);
        }

        // Puts the scoreboard back to the restored round. mp_backup_restore_load_file only restores
        // the score, the round number and the money: the round-history strip at the top of the
        // scoreboard and every player's kills/deaths/assists/damage keep the values from the rounds
        // that were rolled back, so a restored match still looks like the later rounds were played.
        private void RestoreScoreboardState(string scoreboardJson, int roundsPlayed)
        {
            if (!restoreScoreboardStats.Value)
                return;

            try
            {
                var rules = GetGameRules();
                var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                if (rules != null && proxy != null && roundsPlayed >= 0)
                {
                    var results = rules.MatchStats_RoundResults;
                    var aliveCt = rules.MatchStats_PlayersAlive_CT;
                    var aliveT = rules.MatchStats_PlayersAlive_T;
                    for (int i = roundsPlayed; i < results.Length; i++)
                        results[i] = 0;
                    for (int i = roundsPlayed; i < aliveCt.Length; i++)
                        aliveCt[i] = 0;
                    for (int i = roundsPlayed; i < aliveT.Length; i++)
                        aliveT[i] = 0;
                    Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
                }

                if (string.IsNullOrWhiteSpace(scoreboardJson))
                    return;

                var snapshot = JsonSerializer.Deserialize<List<ScoreboardSnapshot>>(scoreboardJson) ?? new List<ScoreboardSnapshot>();

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || p.IsHLTV)
                        continue;
                    var stats = p.ActionTrackingServices?.MatchStats;
                    if (stats == null)
                        continue;

                    // Humans match on SteamID so a reconnect still gets its own line back. Bots have no
                    // usable SteamID, so they match on name. Anyone missing from the snapshot joined
                    // after the backup was written and starts from zero.
                    ScoreboardSnapshot? entry =
                        p.IsBot ? snapshot.Find(s => s.IsBot && s.Name == p.PlayerName) : snapshot.Find(s => !s.IsBot && s.SteamId == p.SteamID.ToString());

                    stats.Kills = entry?.Kills ?? 0;
                    stats.Deaths = entry?.Deaths ?? 0;
                    stats.Assists = entry?.Assists ?? 0;
                    stats.Damage = entry?.Damage ?? 0;
                    stats.HeadShotKills = entry?.HeadShotKills ?? 0;
                    stats.EnemiesFlashed = entry?.EnemiesFlashed ?? 0;
                    stats.UtilityDamage = entry?.UtilityDamage ?? 0;
                    stats.Objective = entry?.Objective ?? 0;
                    stats.EquipmentValue = entry?.EquipmentValue ?? 0;
                    stats.MoneySaved = entry?.MoneySaved ?? 0;
                    stats.KillReward = entry?.KillReward ?? 0;
                    stats.LiveTime = entry?.LiveTime ?? 0;
                    stats.CashEarned = entry?.CashEarned ?? 0;
                    p.Score = entry?.Score ?? 0;
                    p.MVPs = entry?.Mvps ?? 0;

                    Utilities.SetStateChanged(p, "CCSPlayerController", "m_iScore");
                    Utilities.SetStateChanged(p, "CCSPlayerController", "m_iMVPs");
                    Utilities.SetStateChanged(p, "CCSPlayerController", "m_pActionTrackingServices");
                }
            }
            catch (Exception e)
            {
                Log($"[RestoreScoreboardState] {e.Message}");
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
                string matchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                string filePath = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup", matchZyBackupFileName);

                string? directoryPath = Path.GetDirectoryName(filePath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
                string lastBackupFilePath = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt";
                ;
                bool lastBackupExists = File.Exists(Path.Combine(Server.GameDirectory, "csgo", lastBackupFilePath));
                lastBackupFilePath = Path.Combine(Server.GameDirectory, "csgo", lastBackupFilePath);

                string valveBackupContent = lastBackupExists ? File.ReadAllText(lastBackupFilePath) : "";

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
                    // Scoreboard snapshot: the engine keeps kills/deaths/damage and the round-history
                    // strip as they were when the backup is loaded, so we have to put them back ourselves.
                    { "scoreboard", CaptureScoreboardSnapshot() },
                };
                JsonSerializerOptions options = new() { WriteIndented = true };
                string defaultJson = JsonSerializer.Serialize(roundData, options);

                File.WriteAllText(filePath, defaultJson);

                if (!lastBackupExists)
                {
                    // The engine writes its own round file (mp_backup_round_auto) around the same tick as
                    // this round_start snapshot, so it is often not on disk yet and the JSON ends up with
                    // an empty valve_backup - which used to make the restore of that round a no-op. Fill
                    // it in once the engine is done.
                    string pendingJsonPath = filePath;
                    string pendingValvePath = lastBackupFilePath;
                    AddTimer(
                        2.0f,
                        () =>
                        {
                            try
                            {
                                if (!File.Exists(pendingValvePath) || !File.Exists(pendingJsonPath))
                                    return;
                                string valveContent = SanitizeValveBackup(File.ReadAllText(pendingValvePath));
                                if (string.IsNullOrWhiteSpace(valveContent))
                                    return;
                                var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(pendingJsonPath));
                                if (stored == null || !string.IsNullOrWhiteSpace(stored.GetValueOrDefault("valve_backup", "")))
                                    return;
                                stored["valve_backup"] = valveContent;
                                File.WriteAllText(pendingJsonPath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
                                Log($"[CreateMatchZyRoundDataBackup] Filled in valve_backup for {Path.GetFileName(pendingJsonPath)} from {Path.GetFileName(pendingValvePath)}.");
                            }
                            catch (Exception ex)
                            {
                                Log($"[CreateMatchZyRoundDataBackup valve_backup fill-in] {ex.Message}");
                            }
                        }
                    );
                }
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
                        backupData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();
                    }
                }

                info = $"{filePath.Split("/")[^1]} {backupData["timestamp"]} {backupData["team1_name"]} {backupData["team2_name"]} {backupData["map_name"]} {backupData["team1_score"]} {backupData["team2_score"]}";
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
        [ConsoleCommand("css_loadbackup", "Restore the backup from the provided file")]
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
        public void OnBackupMenuCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_backupmenu", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!isMatchLive)
            {
                // No live match (e.g. after a server crash): list the newest backup
                // files on disk so the admin can restore without knowing the filename.
                ShowRecentBackupsFromDisk(player);
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
            ReplyToUserCommand(player, $"Current: Round {currentRound} - {ChatColors.Green}{matchzyTeam1.teamName} {t1score}-{t2score} {matchzyTeam2.teamName}");
            ReplyToUserCommand(player, "───────────────────────────────────");

            int displayed = 0;
            foreach (string backupPath in backups)
            {
                if (displayed >= 10)
                    break; // Limit to 10 most recent

                string fileName = Path.GetFileName(backupPath);
                var roundMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"round(\d+)");

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

                ReplyToUserCommand(player, $"  {ChatColors.Yellow}R{roundNum}{ChatColors.Default}" + $" | {score1}-{score2}" + $" ({halfLabel})" + $" {ChatColors.Grey}{timeAgo}{ChatColors.Default}" + $" → {ChatColors.Green}!restore {roundNum}");

                displayed++;
            }

            if (displayed == 0)
            {
                ReplyToUserCommand(player, "No valid round backups found.");
            }
            else
            {
                ReplyToUserCommand(player, "───────────────────────────────────");
                ReplyToUserCommand(player, $"Tip: {ChatColors.Green}!restore <round>{ChatColors.Default}" + $" or {ChatColors.Green}!restorelast{ChatColors.Default} for previous round");
            }
        }

        // Crash-recovery listing: with no live match there is no liveMatchId to filter
        // on, so list the newest backup files on disk across all matches. Restoring one
        // via loadbackup rebuilds the full match state (config, teams, scores, map)
        // from the file, including a changelevel if the map differs.
        private void ShowRecentBackupsFromDisk(CCSPlayerController? player)
        {
            string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");
            if (!Directory.Exists(backupDir))
            {
                ReplyToUserCommand(player, "No backups found (backup folder does not exist).");
                return;
            }

            var files = new DirectoryInfo(backupDir)
                .GetFiles("matchzy_*.json")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(5)
                .ToList();

            if (files.Count == 0)
            {
                ReplyToUserCommand(player, "No backups found.");
                return;
            }

            ReplyToUserCommand(player, "Recent backups (newest first):");
            ReplyToUserCommand(player, "───────────────────────────────────");

            int displayed = 0;
            foreach (var file in files)
            {
                var backupData = ParseBackupFile(file.FullName);
                if (backupData == null)
                    continue;

                string matchId = backupData.GetValueOrDefault("matchid", "?");
                string mapName = backupData.GetValueOrDefault("map_name", "?");
                string round = backupData.GetValueOrDefault("round", "?");
                string team1 = backupData.GetValueOrDefault("team1_name", "");
                string team2 = backupData.GetValueOrDefault("team2_name", "");
                string score1 = backupData.GetValueOrDefault("team1_score", "0");
                string score2 = backupData.GetValueOrDefault("team2_score", "0");
                string timestamp = backupData.GetValueOrDefault("timestamp", "");

                if (string.IsNullOrWhiteSpace(team1) || team1 == "team1")
                    team1 = "CT";
                if (string.IsNullOrWhiteSpace(team2) || team2 == "team2")
                    team2 = "T";

                string timeAgo = "";
                if (DateTime.TryParse(timestamp, out DateTime backupTime))
                {
                    var diff = DateTime.Now - backupTime;
                    timeAgo =
                        diff.TotalMinutes < 1 ? "just now"
                        : diff.TotalMinutes < 60 ? $"{(int)diff.TotalMinutes}m ago"
                        : diff.TotalHours < 24 ? $"{(int)diff.TotalHours}h {diff.Minutes}m ago"
                        : $"{(int)diff.TotalDays}d ago";
                }

                ReplyToUserCommand(player, $"  {ChatColors.Yellow}#{matchId} R{round}{ChatColors.Default} | {team1} {ChatColors.Green}{score1}-{score2}{ChatColors.Default} {team2} | {mapName} {ChatColors.Grey}{timeAgo}");
                ReplyToUserCommand(player, $"  → {ChatColors.Green}!loadbackup {file.Name}");
                displayed++;
            }

            if (displayed == 0)
            {
                ReplyToUserCommand(player, "No valid round backups found.");
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
                        var roundMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"round(\d+)");
                        string roundNum = roundMatch.Success ? int.Parse(roundMatch.Groups[1].Value).ToString() : "?";

                        // Format: "#1 | Round 5 | Team1 2 - 3 Team2 | de_dust2 | 2024-01-15 14:30:22"
                        command.ReplyToCommand($"#{index} | Round {roundNum} | {team1} {score1} - {score2} {team2} | {map} | {timestamp}");
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
