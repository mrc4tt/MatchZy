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

        // RegexOptions.Compiled emits IL and JITs the matcher when the Regex is CONSTRUCTED,
        // not when it is matched. This pattern used to be built inside SanitizeValveBackup,
        // i.e. once per live round on the game thread (CreateMatchZyRoundDataBackup), again in
        // the 2s valve_backup fill-in, and again on every restore. Build it once and reuse it.
        private static readonly Regex BlockedBackupCommands = new Regex(
            @"^(playdemo|tv_record|tv_stoprecord|tv_autorecord|stopdemo|demo_(play|record|pause)|quit|exit)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Sanitizes Valve backup script lines before executing them on the server during restore.
        // Blocks commands that can crash or hijack a dedicated server (e.g., playdemo, tv_record, quit).
        private static string SanitizeValveBackup(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input ?? string.Empty;

            var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var filtered = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    filtered.Add(line);
                    continue;
                }
                if (BlockedBackupCommands.IsMatch(trimmed))
                    continue; // drop dangerous lines
                filtered.Add(line);
            }
            return string.Join("\n", filtered);
        }

        public void SetupRoundBackupFile()
        {
            // The engine resolves a relative mp_backup_round_file prefix against the first Game search path
            // in gameinfo.gi, which is csgo/addons/metamod on Metamod servers. An absolute prefix keeps the
            // round files in csgo/, where the backup and restore code reads them.
            string backupFilePrefix = Path.Join(Server.GameDirectory, "csgo", $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}").Replace('\\', '/');
            // Always quoted: the console tokenizer splits an unquoted argument at a space and at the
            // ':' of a Windows drive letter (C:/...).
            string prefixArg = $"\"{backupFilePrefix}\"";
            Server.ExecuteCommand($"mp_backup_round_file {prefixArg}");
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
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.stopcooldown", STOP_COMMAND_COOLDOWN_SECONDS - (int)timeElapsed));
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.alreadyvoted", stopTeamName, remainingStopTeam));
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

                PrintLocalizedToAll("matchzy.restore.teamwantstorestore", stopTeamName, remainingStopTeam);
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.matchnotlive"));
                return;
            }

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

            // Get current round number
            var gameRules = GetGameRules();
            if (gameRules == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.nogamerules"));
                return;
            }

            int currentRound = gameRules.TotalRoundsPlayed;
            string round = currentRound.ToString("D2");
            string currentRoundBackup = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";

            // Check if backup exists
            string backupPath = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup", currentRoundBackup);

            if (!File.Exists(backupPath))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.roundbackupnotfound", currentRound));
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.tryrestoreround", currentRound));
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.matchnotlive"));
                return;
            }

            if (!string.IsNullOrEmpty(lastMatchZyBackupFileName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.restoringlast"));
                RestoreRoundBackup(player, lastMatchZyBackupFileName);
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.noprevbackup"));
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

        /// <summary>
        /// Restores (or queues) a round backup. Returns false when the restore was refused, in which
        /// case nothing about the running match has been changed: every check that can refuse runs
        /// before the backup's match id, config, teams, sides and timeouts are applied.
        /// </summary>
        private bool RestoreRoundBackup(CCSPlayerController? player, string fileName)
        {
            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoreduringhalftime"));
                return false;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restorematchended"));
                return false;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoretacticaltimeout"));
                return false;
            }
            string backupFolder = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");

            string filePath = Path.Combine(backupFolder, fileName);

            if (!File.Exists(filePath))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoredoesntexist", fileName));
                return false;
            }

            var gameRules = GetGameRules();
            if (gameRules == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.nogamerules"));
                return false;
            }
            bool liveSetupRequired = false;

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

                // Refuse a backup without usable round data BEFORE anything is applied. It used to be
                // checked only after the match id, config, teams, sides and timeouts had been
                // overwritten, so a refused restore still changed the running match (and a refused
                // queued restore left the server in warmup with everyone ready).
                if (!HasUsableValveRoundData(backupData, fileName))
                {
                    Log($"[RestoreRoundBackup] {fileName} has no usable valve_backup data and no complete .txt in csgo/, nothing to restore.");
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.nousabledata", fileName));
                    return false;
                }

                // We set active timeouts to false so that timeout does not start after the round has been restored.
                // This is to prevent any buggish behaviour with timeouts (like incorrect timeout used showing, or force-unpausing the match once timeout ends)
                gameRules.CTTimeOutActive = gameRules.TerroristTimeOutActive = false;

                // Get5: technical pauses used are per map and survive a restore of the same map; only a
                // backup of another match or map brings its own counts.
                // Decided here, before this pass can switch the match id / map, queue the restore or
                // change map: the pass that finally loads the round runs with both already switched.
                // Applied when the round is actually loaded (going live resets the counters before).
                bool sameMatchAndMap = backupData.TryGetValue("matchid", out var backupIdText) && backupIdText == liveMatchId.ToString()
                    && backupData.TryGetValue("mapnumber", out var backupMapText) && backupMapText == matchConfig.CurrentMapNumber.ToString();
                if (!sameMatchAndMap)
                {
                    pendingRestoreTechPauses =
                        backupData.TryGetValue("team1_tech_pauses_used", out var team1Tech) && int.TryParse(team1Tech, out int team1TechUsed)
                        && backupData.TryGetValue("team2_tech_pauses_used", out var team2Tech) && int.TryParse(team2Tech, out int team2TechUsed)
                            ? (Math.Max(0, team1TechUsed), Math.Max(0, team2TechUsed))
                            : null;
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
                    var restoredConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchConfig>(matchConfigValue)!;
                    // A backup without remote log settings (older or hand-made) would stop the match
                    // from reporting; fall back to the server's own settings.
                    if (string.IsNullOrEmpty(restoredConfig.RemoteLogURL))
                        ApplyDefaultRemoteLog(restoredConfig);
                    matchConfig = restoredConfig;
                    SetupRoundBackupFile();
                }
                // Copy the restored values INTO the existing Team objects rather than replacing the
                // references. teamSides is a Dictionary<Team, string> and Team has no Equals /
                // GetHashCode override, so it is keyed by reference: swapping in fresh instances
                // orphaned every teamSides entry, and the only place that re-registered them was
                // the optional "team1_side" block below. A backup without that key left every
                // teamSides[matchzyTeam1] lookup throwing KeyNotFound for the rest of the session,
                // which made GetPlayerTeam return None for everyone. It also preserves the runtime
                // `coach` set, which is JsonIgnore'd and would otherwise be silently emptied.
                if (backupData.TryGetValue("team1", out var team1config))
                {
                    var _t1 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team1config);
                    if (_t1 != null)
                        CopyTeamData(_t1, matchzyTeam1);
                    else
                        Console.WriteLine("[MatchZy] [RestoreRoundBackup] team1 deserialization returned null.");
                }
                if (backupData.TryGetValue("team2", out var team2config))
                {
                    var _t2 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team2config);
                    if (_t2 != null)
                        CopyTeamData(_t2, matchzyTeam2);
                    else
                        Console.WriteLine("[MatchZy] [RestoreRoundBackup] team2 deserialization returned null.");
                }
                // Backup files can be loaded from anywhere (matchzy_loadbackup), and the team name and
                // tag end up in console commands (mp_teamname_N), so clean them the same way a match
                // config's team names are cleaned.
                foreach (var team in new[] { matchzyTeam1, matchzyTeam2 })
                {
                    team.teamName = RemoveSpecialCharacters(team.teamName ?? "");
                    team.teamTag = RemoveSpecialCharacters(team.teamTag ?? "");
                    team.teamFlag = RemoveSpecialCharacters(team.teamFlag ?? "");
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
                    if (!IsSameMap(map_name, Server.MapName))
                    {
                        // Backups store the plain map name; a workshop map whose id is not known
                        // (e.g. after a server restart) cannot be loaded from it.
                        if (BuildMapChangeCommand(map_name) == null)
                        {
                            Log($"[RestoreRoundBackup] Backup is for map '{map_name}', which cannot be loaded here (not a stock map, workshop id unknown). Load that map first, then restore.");
                            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.invalidmap"));
                            return false;
                        }
                        ChangeMap(map_name, 0);
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        // Returning from here, backup will be restored again once the map is changed.
                        return true;
                    }
                }

                // This is done after checking map_name so that we load the correct map first
                if (gameRules.WarmupPeriod)
                {
                    if (!isRoundRestorePending)
                    {
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        // Nothing has been restored yet. In warmup the backup is only QUEUED here and
                        // applied by HandleMatchStart once the match goes live. It used to print
                        // "loaded successfully" first, which read like the round was already back, so
                        // people ran the command a second time (forcing the restore immediately).
                        PrintLocalizedToAll("matchzy.restore.queued", fileName);
                        Log($"[RestoreRoundBackup] Queued {fileName} during warmup; it will be applied when the match starts. Repeat the command to restore immediately.");
                        return true;
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

                    // Two candidates can feed mp_backup_restore_load_file: the copy embedded in the
                    // JSON snapshot, and the .txt the engine wrote itself. Pick between them on
                    // COMPLETENESS, never blindly.
                    //
                    // The embedded copy is preferred when it is complete, because it definitely
                    // belongs to this match: a .txt left in csgo/ by an earlier match with the same
                    // match id and round number would otherwise be loaded instead.
                    //
                    // But it must not be preferred blindly. The snapshot is taken on round_start
                    // (CreateMatchZyRoundDataBackup), the same tick the engine writes its own round
                    // file, so ReadAllText can catch that file mid-write and store a TRUNCATED copy.
                    // Overwriting the engine's now-complete .txt with that truncated copy made
                    // mp_backup_restore_load_file fail silently - which looked exactly like ".restore
                    // does nothing while a manual mp_backup_restore_load_file works", because the
                    // manual path loads the intact engine file that .restore had clobbered.
                    var safeScript = SanitizeValveBackup(valveBackup);
                    bool embeddedUsable = IsCompleteValveBackup(safeScript);

                    string? diskContent = null;
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            diskContent = File.ReadAllText(tempFilePath);
                        }
                        catch (Exception ex)
                        {
                            Log($"[RestoreRoundBackup] Could not read {tempFileName}: {ex.Message}");
                        }
                    }
                    bool diskUsable = IsCompleteValveBackup(diskContent);

                    if (embeddedUsable)
                    {
                        if (diskUsable && diskContent!.Length != safeScript.Length)
                        {
                            Log($"[RestoreRoundBackup] {tempFileName} on disk is {diskContent.Length} chars, backup carries {safeScript.Length}. Both parse as complete; using the backup copy (it is known to belong to this match).");
                        }
                        File.WriteAllText(tempFilePath, safeScript);
                        Log($"[RestoreRoundBackup] Wrote {tempFilePath} ({safeScript.Length} chars) for restore of {fileName}.");
                    }
                    else if (diskUsable)
                    {
                        // The embedded copy is missing or truncated but the engine's own file is
                        // intact. Load that and leave it alone - overwriting it here is what broke
                        // the restore.
                        Log(
                            string.IsNullOrWhiteSpace(safeScript)
                                ? $"[RestoreRoundBackup] {fileName} carries no valve_backup; loading the engine's own {tempFileName} ({diskContent!.Length} chars) instead."
                                : $"[RestoreRoundBackup] valve_backup in {fileName} is incomplete ({safeScript.Length} chars, unbalanced); keeping the engine's own {tempFileName} ({diskContent!.Length} chars) instead."
                        );
                    }
                    else
                    {
                        // Neither candidate is usable under the expected name. Widen the search to the
                        // other name this round can be stored under before giving up.
                        string? diskBackup = FindValveRoundBackupOnDisk(csgoDir, tempFileName, fileName);
                        if (diskBackup == null)
                        {
                            // Nothing to load: the round would stay exactly as it is while we announce a
                            // successful restore and pause the match. Report it instead.
                            Log($"[RestoreRoundBackup] {fileName} has no usable valve_backup data and no complete .txt in csgo/, nothing to restore.");
                            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.nousabledata", fileName));
                            pendingRestoreTechPauses = null;
                            return false;
                        }

                        tempFilePath = diskBackup;
                        tempFileName = Path.GetFileName(tempFilePath);
                        Log($"[RestoreRoundBackup] Falling back to {tempFileName} ({new FileInfo(tempFilePath).Length} bytes) on disk.");
                    }

                    int restoreTimer = liveSetupRequired ? 2 : 0;
                    if (liveSetupRequired)
                    {
                        string gameMode = backupData.GetValueOrDefault("game_mode", "live");
                        if (gameMode == "scrim")
                            SetupScrimFlagsAndCfg();
                        else if (gameMode == "hill")
                            SetupHillFlagsAndCfg();
                        else
                            SetupLiveFlagsAndCfg();
                        // The match goes live here instead of through StartLive/StartScrim/StartHill,
                        // which is where the GOTV recording is armed: without this the rest of the
                        // map after a queued (warmup / crash recovery) restore had no demo.
                        if (!isDemoRecording)
                            ArmDemoStart();
                    }
                    // Scoreboard state belonging to the restored round, applied once the engine is done.
                    string scoreboardJson = backupData.GetValueOrDefault("scoreboard", "");
                    string advancedStatsJson = backupData.GetValueOrDefault("advanced_stats", "");
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
                            SendBackupLoadedEvents(fileName, restoredRoundsPlayed);
                            if (pendingRestoreTechPauses is var (team1Tech, team2Tech))
                            {
                                technicalPauseUsed[matchzyTeam1] = team1Tech;
                                technicalPauseUsed[matchzyTeam2] = team2Tech;
                                pendingRestoreTechPauses = null;
                            }
                            // Put the advanced stats back to the start of the restored round (also undoes
                            // the reset that setting the match live again does).
                            RestoreAdvancedStatsSnapshot(advancedStatsJson);
                            Server.ExecuteCommand($"mp_teamname_1 {TeamNameArg(matchzyTeam1.teamName)}");
                            Server.ExecuteCommand($"mp_teamname_2 {TeamNameArg(matchzyTeam2.teamName)}");
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
                return false;
            }
            // The result is announced from VerifyRoundRestore instead of here: at this point the load
            // command has only been queued, so announcing a successful restore now is a guess.
            return true;
        }

        /// <summary>
        /// Whether a backup has round data mp_backup_restore_load_file can load: a complete embedded
        /// valve_backup, or a complete .txt on disk under one of the names the restore looks for. Uses
        /// the backup's own match id and map number, which is what the restore applies before it
        /// builds the file name, so this check needs no state change.
        /// </summary>
        private bool HasUsableValveRoundData(Dictionary<string, string> backupData, string fileName)
        {
            backupData.TryGetValue("valve_backup", out var valveBackup);
            if (IsCompleteValveBackup(SanitizeValveBackup(valveBackup)))
                return true;

            long matchId = backupData.TryGetValue("matchid", out var idText) && long.TryParse(idText, out var parsedId) && parsedId > 0 ? parsedId : liveMatchId;
            int mapNumber = backupData.TryGetValue("mapnumber", out var mapText) && int.TryParse(mapText, out var parsedMap) ? parsedMap : matchConfig.CurrentMapNumber;
            string tempFileName = fileName.Replace(".json", ".txt");
            if (backupData.TryGetValue("round", out var roundNumber))
                tempFileName = $"matchzy_{matchId}_{mapNumber}_round{roundNumber}.txt";
            return FindValveRoundBackupOnDisk(Path.Combine(Server.GameDirectory, "csgo"), tempFileName, fileName) != null;
        }

        // Locates the round file the engine wrote itself (mp_backup_round_auto) for a MatchZy JSON backup
        // that carries no embedded copy. Two names can point at the same round: the one built from the
        // current match id and map number, and the JSON backup's own name with a .txt extension. Both are
        // checked, and an empty file counts as not found.
        private string? FindValveRoundBackupOnDisk(string csgoDir, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string candidate = Path.Combine(csgoDir, Path.GetFileNameWithoutExtension(name) + ".txt");
                if (!File.Exists(candidate))
                    continue;
                try
                {
                    // Non-empty is not enough: a file caught mid-write by the engine parses as
                    // nothing and makes mp_backup_restore_load_file fail without a word.
                    if (IsCompleteValveBackup(File.ReadAllText(candidate)))
                        return candidate;
                }
                catch (Exception ex)
                {
                    Log($"[FindValveRoundBackupOnDisk] Could not read {candidate}: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Cheap completeness test for a Valve round backup. The file is KeyValues, so a complete
        /// one has at least one block and balanced braces; a truncated write (the engine is still
        /// flushing it when a round_start snapshot reads it) leaves them unbalanced. Braces inside
        /// quoted values, and \" escapes, are ignored so map names and cvar values cannot skew it.
        ///
        /// This deliberately checks structure only, not schema - it has to stay correct across CS2
        /// updates that add or rename keys.
        /// </summary>
        private static bool IsCompleteValveBackup(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            int depth = 0;
            bool sawBlock = false;
            bool inQuotes = false;
            bool escaped = false;

            foreach (char c in content)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                    continue;

                if (c == '{')
                {
                    depth++;
                    sawBlock = true;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth < 0)
                        return false;
                }
            }

            return sawBlock && depth == 0 && !inQuotes;
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
                PrintLocalizedToAll("matchzy.restore.restoredsuccessfully", fileName);
                return;
            }

            // The engine ignored the file. Clear the restore flags: isRoundRestoring gates
            // CreateMatchZyRoundDataBackup, and it is normally cleared by the round start that a
            // successful load triggers, so leaving it set here would stop every later round backup.
            isRoundRestoring = false;
            isSpawnKeeping = false;
            // backup_loaded / round_start were already sent for this load; let the next real round
            // start through.
            roundStartSentByRestore = false;
            Log($"[RestoreRoundBackup FATAL] Engine did not load {fileName}. Rounds played is still {roundsPlayed}, expected {expectedRoundsPlayed}.");
            PrintLocalizedToAll("matchzy.backupmsg.restorefailed", fileName);
            if (IsPlayerValid(player))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.restorefaileddetails"));
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
                CancelTechPauseTimer();
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
            // Get5 announces the pause a restore leaves the match in as pause_type "backup" (the
            // legacy format never sent one).
            if (UseGet5Events)
            {
                PublishEvent(new MatchPausedLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    PauseType = "backup",
                    TeamName = null,
                    MaxDuration = null,
                    RoundNumber = GetRoundNumer(),
                });
            }

            if (!restoreAutoUnpause.Value)
            {
                // Manual: both teams (or an admin) have to use .unpause.
                pausedStateTimer ??= AddTimer(chatTimerDelay, SendPausedStateMessage, TimerFlags.REPEAT);
                return;
            }

            int delay = Math.Max(1, restoreUnpauseDelay.Value);
            restoreUnpauseSecondsLeft = delay;
            PrintLocalizedToAll("matchzy.backupmsg.restoredunpausein", delay);
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
                            PrintLocalizedToAll("matchzy.backupmsg.unpausingin", restoreUnpauseSecondsLeft);
                        }
                        return;
                    }

                    restoreUnpauseTimer?.Kill();
                    restoreUnpauseTimer = null;
                    Server.ExecuteCommand("mp_unpause_match;");
                    CancelTechPauseTimer();
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;
                    unpauseData["pauseTeam"] = "";
                    pausedStateTimer?.Kill();
                    pausedStateTimer = null;
                    PrintLocalizedToAll("matchzy.backupmsg.matchlive");
                    // Pairs with the game_paused (pause_type backup) sent when the pause began.
                    if (UseGet5Events)
                    {
                        PublishEvent(new MatchUnpausedLiveEvent
                        {
                            MatchId = liveMatchId,
                            MapNumber = matchConfig.CurrentMapNumber,
                            RoundNumber = GetRoundNumer(),
                        });
                    }
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

        // Game-thread half of the scoreboard snapshot: entity reads only. The JSON encoding of the
        // list happens in WriteRoundDataBackupAsync on the thread pool; ScoreboardSnapshot is a plain
        // POCO so the list can cross threads once it is built.
        private List<ScoreboardSnapshot> CaptureScoreboardSnapshot()
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
            return snapshot;
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

        // Serialises the backup writes. Two round_starts close together (a restart, or the
        // fill-in pass landing on the same file) must not interleave writes to the same path.
        private static readonly SemaphoreSlim backupWriteLock = new SemaphoreSlim(1, 1);

        // System.Text.Json caches its per-type metadata on the options instance, so a fresh
        // JsonSerializerOptions per write rebuilt that metadata every round. One shared instance.
        // WriteIndented is deliberately off: the payload is dominated by one giant escaped string
        // (valve_backup), which indentation cannot break up - it only costs a pass.
        internal static readonly JsonSerializerOptions BackupJsonOptions = new() { WriteIndented = false };

        /// <summary>
        /// Round snapshot for the restore system. Split in two on purpose:
        ///
        /// The gathering below needs the game thread (entity + convar reads), so it stays here and
        /// is cheap. Everything after it - reading the engine's valve backup .txt, sanitising it,
        /// serialising a dictionary that embeds that whole blob as an escaped JSON string, and
        /// writing the result - is pure file IO and string work that needs nothing from the engine.
        /// It used to run inline and cost ~90 ms on the game thread of EVERY live round, which the
        /// slow-frame profiler attributed as "MatchZy round_start". It now runs on the thread pool.
        /// </summary>
        public void CreateMatchZyRoundDataBackup()
        {
            if (!isMatchLive || isRoundRestoring)
                return;
            try
            {
                // ---- game thread: engine state only ----
                (int t1score, int t2score) = GetTeamsScore();
                int roundNumber = t1score + t2score;
                string round = roundNumber.ToString("D2");
                string matchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                string filePath = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup", matchZyBackupFileName);
                string lastBackupFilePath = Path.Combine(
                    Server.GameDirectory,
                    "csgo",
                    $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt"
                );

                // GetGameRules() is the cached lookup and returns null instead of throwing when
                // cs_gamerules is momentarily absent, which .First() did - the exact pattern the
                // comment on GetGameRules warns about. Skip the snapshot rather than write a
                // backup with bogus timeout counts.
                var gameRules = GetGameRules();
                if (gameRules == null)
                {
                    Log("[CreateMatchZyRoundDataBackup] cs_gamerules not available; skipping this round's snapshot.");
                    return;
                }

                Dictionary<string, string> roundData = new()
                {
                    { "matchid", liveMatchId.ToString() },
                    { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "map_name", Server.MapName },
                    { "mapnumber", matchConfig.CurrentMapNumber.ToString() },
                    // Game mode, so a restore that has to set the round up again uses the same cfg
                    // (a scrim or hill game used to come back as a match, with overtime and clinch on).
                    { "game_mode", isPlayOutEnabled2 ? "hill" : isPlayOutEnabled ? "scrim" : "live" },
                    { "round", round },
                    { "team1", "" },
                    { "team2", "" },
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
                    { "team1_tech_pauses_used", technicalPauseUsed.GetValueOrDefault(matchzyTeam1).ToString() },
                    { "team2_tech_pauses_used", technicalPauseUsed.GetValueOrDefault(matchzyTeam2).ToString() },
                    { "match_loaded", isMatchSetup.ToString() },
                    { "match_config", "" },
                    // Filled in off-thread once the engine's own round file has been read.
                    { "valve_backup", "" },
                    // Scoreboard snapshot: the engine keeps kills/deaths/damage and the round-history
                    // strip as they were when the backup is loaded, so we have to put them back ourselves.
                    // Gathered here, JSON-encoded off-thread.
                    { "scoreboard", "" },
                    // Advanced stats (rating, KAST, opening duels, clutches) at the start of this round.
                    { "advanced_stats", CaptureAdvancedStatsSnapshot() },
                };
                List<ScoreboardSnapshot> scoreboard = CaptureScoreboardSnapshot();
                MatchConfig configSnapshot = matchConfig.SnapshotForBackup();
                Team team1Snapshot = matchzyTeam1.SnapshotForBackup();
                Team team2Snapshot = matchzyTeam2.SnapshotForBackup();

                // ---- thread pool: file IO + JSON ----
                // roundData and scoreboard are handed over wholesale and never touched here again.
                // Remote backup upload settings, captured on the game thread.
                string uploadUrl = backupUploadURL;
                string uploadHeaderKey = backupUploadHeaderKey;
                string uploadHeaderValue = backupUploadHeaderValue;
                long uploadMatchId = liveMatchId;
                int uploadMapNumber = matchConfig.CurrentMapNumber;
                int uploadRound = t1score + t2score;
                _ = Task.Run(async () =>
                {
                    await WriteRoundDataBackupAsync(
                        filePath, lastBackupFilePath, roundData, scoreboard,
                        configSnapshot, team1Snapshot, team2Snapshot);
                    // matchzy_remote_backup_url (get5_remote_backup_url) was stored but never used.
                    if (uploadUrl != "")
                        await UploadRoundBackupAsync(filePath, uploadUrl, uploadHeaderKey, uploadHeaderValue, uploadMatchId, uploadMapNumber, uploadRound);
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[MatchZy] [Exception] {e}");
            }
        }

        /// <summary>
        /// Off-thread half of <see cref="CreateMatchZyRoundDataBackup"/>: read the engine's valve
        /// backup, sanitise, serialise, write. Never throws - an unobserved task exception would
        /// tear down the process.
        /// </summary>
        /// <summary>
        /// POSTs a finished round backup (the MatchZy JSON) to matchzy_remote_backup_url, with the
        /// Get5 headers panels use to file it, so a crashed match can be restored elsewhere.
        /// Worker thread only; never throws.
        /// </summary>
        private async Task UploadRoundBackupAsync(string filePath, string url, string headerKey, string headerValue, long matchId, int mapNumber, int roundNumber)
        {
            // Counted so the empty-server shutdown waits for it.
            Interlocked.Increment(ref _pendingUploads);
            try
            {
                await UploadRoundBackupCoreAsync(filePath, url, headerKey, headerValue, matchId, mapNumber, roundNumber).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingUploads);
            }
        }

        private async Task UploadRoundBackupCoreAsync(string filePath, string url, string headerKey, string headerValue, long matchId, int mapNumber, int roundNumber)
        {
            try
            {
                if (!IsValidUrl(url) || !File.Exists(filePath))
                    return;
                string body;
                await backupWriteLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    body = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                }
                finally
                {
                    backupWriteLock.Release();
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                string fileName = Path.GetFileName(filePath);
                content.Headers.Add("MatchZy-FileName", fileName);
                content.Headers.Add("MatchZy-MatchId", matchId.ToString());
                content.Headers.Add("MatchZy-MapNumber", mapNumber.ToString());
                content.Headers.Add("MatchZy-RoundNumber", roundNumber.ToString());
                content.Headers.Add("Get5-FileName", fileName);
                content.Headers.Add("Get5-MatchId", matchId.ToString());
                content.Headers.Add("Get5-MapNumber", mapNumber.ToString());
                content.Headers.Add("Get5-RoundNumber", roundNumber.ToString());
                request.Content = content;
                if (!string.IsNullOrEmpty(headerKey) && !string.IsNullOrEmpty(headerValue))
                    request.Headers.TryAddWithoutValidation(headerKey, headerValue);

                using var response = await _sharedHttpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    Log($"[UploadRoundBackup] Upload of {fileName} failed: {response.StatusCode}");
            }
            catch (Exception e)
            {
                Log($"[UploadRoundBackup] {e.Message}");
            }
        }

        private async Task WriteRoundDataBackupAsync(
            string filePath,
            string lastBackupFilePath,
            Dictionary<string, string> roundData,
            List<ScoreboardSnapshot> scoreboard,
            MatchConfig configSnapshot,
            Team team1Snapshot,
            Team team2Snapshot
        )
        {
            JsonSerializerOptions options = BackupJsonOptions;
            bool needsFillIn = false;

            await backupWriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string? directoryPath = Path.GetDirectoryName(filePath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                // This is triggered from round_start, the same tick the engine writes its own round
                // file, so the read can catch that file mid-write. A truncated copy stored here is
                // permanent (the fill-in below only repairs an INCOMPLETE valve_backup) and made the
                // later restore load a corrupt file. Treat an incomplete read as "not there yet".
                string valveBackupContent = "";
                needsFillIn = true;
                if (File.Exists(lastBackupFilePath))
                {
                    string rawValveBackup = await File.ReadAllTextAsync(lastBackupFilePath).ConfigureAwait(false);
                    if (IsCompleteValveBackup(rawValveBackup))
                    {
                        valveBackupContent = rawValveBackup;
                        needsFillIn = false;
                    }
                    else
                    {
                        Log($"[CreateMatchZyRoundDataBackup] {Path.GetFileName(lastBackupFilePath)} is still being written ({rawValveBackup.Length} chars, unbalanced); leaving valve_backup empty for the fill-in pass.");
                    }
                }

                roundData["valve_backup"] = SanitizeValveBackup(valveBackupContent);
                // Keep Newtonsoft's existing field/property names and nested JSON strings.
                // Only detached snapshots are read here, never live plugin or engine state.
                roundData["team1"] = Newtonsoft.Json.JsonConvert.SerializeObject(team1Snapshot);
                roundData["team2"] = Newtonsoft.Json.JsonConvert.SerializeObject(team2Snapshot);
                roundData["match_config"] = Newtonsoft.Json.JsonConvert.SerializeObject(configSnapshot);
                roundData["scoreboard"] = JsonSerializer.Serialize(scoreboard);
                await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(roundData, options)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log($"[CreateMatchZyRoundDataBackup write] {e.Message}");
                return;
            }
            finally
            {
                backupWriteLock.Release();
            }

            if (!needsFillIn)
                return;

            // The engine writes its own round file (mp_backup_round_auto) around the same tick as this
            // round_start snapshot, so it is often not on disk yet and the JSON ends up with an empty
            // valve_backup - which used to make the restore of that round a no-op. Fill it in once the
            // engine is done. This was an AddTimer(2.0f) whose body did the same IO on the game thread
            // (~9 ms/round in the profiler's "timer" bucket); it is a plain delay now and never touches
            // the game thread, so unlike the timer it is not cancelled by unload or map change. That is
            // intentional: the work only repairs a backup file already on disk and is guarded by the
            // File.Exists / IsCompleteValveBackup checks below.
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await backupWriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(lastBackupFilePath) || !File.Exists(filePath))
                    return;
                string valveContent = SanitizeValveBackup(await File.ReadAllTextAsync(lastBackupFilePath).ConfigureAwait(false));
                if (!IsCompleteValveBackup(valveContent))
                    return;
                var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await File.ReadAllTextAsync(filePath).ConfigureAwait(false)
                );
                // Repair an incomplete stored copy too, not just an empty one: a truncated
                // valve_backup is what silently breaks the restore, and "non-empty" used to be
                // treated as good enough.
                if (stored == null || IsCompleteValveBackup(stored.GetValueOrDefault("valve_backup", "")))
                    return;
                stored["valve_backup"] = valveContent;
                await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(stored, options)).ConfigureAwait(false);
                Log($"[CreateMatchZyRoundDataBackup] Filled in valve_backup for {Path.GetFileName(filePath)} from {Path.GetFileName(lastBackupFilePath)}.");
            }
            catch (Exception ex)
            {
                Log($"[CreateMatchZyRoundDataBackup valve_backup fill-in] {ex.Message}");
            }
            finally
            {
                backupWriteLock.Release();
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
            // Every reply below reaches the caller only. Invoked from chat that means one player's
            // chat window and nothing in the server log, which is why "!loadbackup did nothing"
            // could not be told apart from "!loadbackup was never dispatched". Log the entry so the
            // server console shows what actually arrived.
            string rawArgs = command.ArgString;
            string callerName = player == null ? "server console" : player.PlayerName;
            Log($"[OnLoadBackupCommand] Invoked by {callerName}, argc={command.ArgCount}, args='{rawArgs}'");

            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                Log($"[OnLoadBackupCommand] Rejected: {callerName} is not an admin.");
                SendPlayerNotAdminMessage(player);
                return;
            }

            // var fileName = command.GetArg(1);
            var fileName = ExtractJsonFileName(rawArgs);

            // ExtractJsonFileName returns "" when it cannot find a .json name in the argument.
            // Passing that on produced a "backup does not exist" reply naming an empty file, which
            // reads like the command did nothing at all.
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Log($"[OnLoadBackupCommand] No .json file name could be read from '{rawArgs}'.");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.loadbackupbadname", rawArgs));
                return;
            }

            Log($"[OnLoadBackupCommand] Restoring '{fileName}' for {callerName}.");
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.nobackupsformatch"));
                return;
            }

            // Show current match context
            (int t1score, int t2score) = GetTeamsScore();
            int currentRound = t1score + t2score;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.menucurrent", currentRound, matchzyTeam1.teamName, t1score, t2score, matchzyTeam2.teamName));
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
                string halfLabel = Localizer.ForPlayer(
                    player,
                    totalScore <= halfRounds ? "matchzy.backupmsg.halffirst"
                    : totalScore <= maxRounds ? "matchzy.backupmsg.halfsecond"
                    : "matchzy.backupmsg.halfovertime"
                );

                // Time ago
                string timeAgo = "";
                if (DateTime.TryParse(timestamp, out DateTime backupTime))
                {
                    var diff = DateTime.Now - backupTime;
                    timeAgo = BackupTimeAgo(player, diff, false);
                }

                ReplyToUserCommand(player, $"  {ChatColors.Yellow}R{roundNum}{ChatColors.Default}" + $" | {score1}-{score2}" + $" ({halfLabel})" + $" {ChatColors.Grey}{timeAgo}{ChatColors.Default}" + $" → {ChatColors.Green}!restore {roundNum}");

                displayed++;
            }

            if (displayed == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.novalidbackups"));
            }
            else
            {
                ReplyToUserCommand(player, "───────────────────────────────────");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.menutip"));
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.nobackupfolder"));
                return;
            }

            var files = new DirectoryInfo(backupDir)
                .GetFiles("matchzy_*.json")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(5)
                .ToList();

            if (files.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.nobackups"));
                return;
            }

            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.recentheader"));
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
                    timeAgo = BackupTimeAgo(player, diff, true);
                }

                ReplyToUserCommand(player, $"  {ChatColors.Yellow}#{matchId} R{round}{ChatColors.Default} | {team1} {ChatColors.Green}{score1}-{score2}{ChatColors.Default} {team2} | {mapName} {ChatColors.Grey}{timeAgo}");
                ReplyToUserCommand(player, $"  → {ChatColors.Green}!loadbackup {file.Name}");
                displayed++;
            }

            if (displayed == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.novalidbackups"));
            }
        }

        // Localized "time since backup" label for the backup listings.
        private string BackupTimeAgo(CCSPlayerController? player, TimeSpan diff, bool includeDays)
        {
            if (diff.TotalMinutes < 1)
                return Localizer.ForPlayer(player, "matchzy.backupmsg.justnow");
            if (diff.TotalMinutes < 60)
                return Localizer.ForPlayer(player, "matchzy.backupmsg.minutesago", (int)diff.TotalMinutes);
            if (!includeDays || diff.TotalHours < 24)
                return Localizer.ForPlayer(player, "matchzy.backupmsg.hoursago", (int)diff.TotalHours, diff.Minutes);
            return Localizer.ForPlayer(player, "matchzy.backupmsg.daysago", (int)diff.TotalDays);
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
            ListBackups(player, command.ArgCount >= 2 ? command.GetArg(1) : null, message => command.ReplyToCommand(message));
        }

        // Chat form (.listbackups [matchid]): the dot dispatch has no CommandInfo to reply through.
        private void HandleListBackupsChatCommand(CCSPlayerController? player, string arg)
        {
            ListBackups(player, string.IsNullOrWhiteSpace(arg) ? null : arg.Trim(), message => ReplyToUserCommand(player, message));
        }

        private void ListBackups(CCSPlayerController? player, string? matchIdArg, Action<string> reply)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            var matchId = matchIdArg ?? liveMatchId.ToString();
            List<string> backups = GetBackups(matchId);

            if (backups.Count == 0)
            {
                reply(Localizer.ForPlayer(player, "matchzy.backupmsg.listnone", matchId));
                return; // FIX: Add return here
            }

            // Header
            reply(Localizer.ForPlayer(player, "matchzy.backupmsg.listheader", matchId, backups.Count));

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
                        reply(Localizer.ForPlayer(player, "matchzy.backupmsg.listrow", index, roundNum, team1, score1, score2, team2, map, timestamp));
                    }
                    else
                    {
                        // Fallback if format is unexpected
                        reply($"#{index} | {backupInfo}");
                    }
                }
                else
                {
                    // If GetBackupInfo failed, show just the filename
                    reply($"#{index} | {Path.GetFileName(backup)}");
                }

                index++;
            }

            reply(Localizer.ForPlayer(player, "matchzy.backupmsg.listfooter"));
        }
    }
}
