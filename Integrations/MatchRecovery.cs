using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Match recovery. A loaded match lives only in the plugin's memory, so a server crash, a
        // restart or a plugin reload in the middle of a match left the server without its match
        // config: no rosters, team names or match id, and with matchzy_kick_when_no_match_loaded
        // every player was kicked. An admin then often reached for Valve's raw
        // mp_backup_restore_load_file, which brings the score back but none of MatchZy's state.
        //
        //  - active_match.json (next to the round backups) records the loaded match until the series
        //    ends or the match is cancelled.
        //  - matchzy_auto_recover: a few seconds after server_ready with no match loaded, a fresh
        //    marker restores the newest round backup of that match (config, teams, score), or loads
        //    the match config again when no round was backed up yet.
        //  - The connect gate lets in a player on that match's roster instead of kicking them.
        //  - matchzy_raw_backup_restore: mp_backup_restore_load_file is sent through MatchZy's restore.

        private const string ActiveMatchFileName = "active_match.json";
        private const float AutoRecoverDelay = 10f;

        private sealed class ActiveMatchMarker
        {
            public long MatchId { get; set; }
            public string MapName { get; set; } = "";
            public DateTime SavedAtUtc { get; set; }
            public string MatchJson { get; set; } = "";
        }

        // Parsed marker for the connect gate; null = not read yet / no marker.
        private ActiveMatchMarker? cachedActiveMatch;
        private JObject? cachedActiveMatchConfig;
        private bool activeMatchCacheValid;
        // File name of the restore MatchZy itself sends to the engine (passes the raw listener).
        private string? ownRawRestoreFile;
        // Marker writes and deletes run off the game thread one after another, in call order: a
        // cancel (delete) followed by a new load (write) must not end with the file deleted.
        private Task activeMatchIo = Task.CompletedTask;
        private readonly object activeMatchIoLock = new();

        private void QueueActiveMatchIo(Action io)
        {
            lock (activeMatchIoLock)
            {
                activeMatchIo = activeMatchIo.ContinueWith(_ =>
                {
                    try { io(); }
                    catch (Exception e) { Console.WriteLine($"[MatchZy] [ActiveMatch] {e.Message}"); }
                }, TaskScheduler.Default);
            }
        }

        private string BackupFolderPath() => Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");

        private void SaveActiveMatch(string jsonData)
        {
            try
            {
                long matchId = liveMatchId;
                try
                {
                    var json = JObject.Parse(jsonData);
                    if (json["matchid"] != null && long.TryParse(json["matchid"]!.ToString(), out long parsed) && parsed > 0)
                        matchId = parsed;
                }
                catch { }
                var marker = new ActiveMatchMarker
                {
                    MatchId = matchId,
                    MapName = Server.MapName,
                    SavedAtUtc = DateTime.UtcNow,
                    MatchJson = jsonData,
                };
                cachedActiveMatch = marker;
                cachedActiveMatchConfig = null;
                activeMatchCacheValid = true;
                string folder = BackupFolderPath();
                string text = JsonSerializer.Serialize(marker);
                QueueActiveMatchIo(() =>
                {
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, ActiveMatchFileName);
                    File.WriteAllText(path + ".tmp", text);
                    File.Move(path + ".tmp", path, overwrite: true);
                });
            }
            catch (Exception e)
            {
                Log($"[ActiveMatch] {e.Message}");
            }
        }

        private void ClearActiveMatch(string why)
        {
            try
            {
                cachedActiveMatch = null;
                cachedActiveMatchConfig = null;
                activeMatchCacheValid = true;
                string path = Path.Combine(BackupFolderPath(), ActiveMatchFileName);
                QueueActiveMatchIo(() =>
                {
                    if (File.Exists(path))
                        File.Delete(path);
                });
                Log($"[ActiveMatch] Cleared ({why}).");
            }
            catch (Exception e)
            {
                Log($"[ActiveMatch] {e.Message}");
            }
        }

        // The marker if it exists and is younger than matchzy_auto_recover_max_age (counted from the
        // marker or the newest backup of its match, whichever is later).
        private ActiveMatchMarker? ReadActiveMatch(out FileInfo? newestBackup)
        {
            newestBackup = null;
            string path = Path.Combine(BackupFolderPath(), ActiveMatchFileName);
            var marker = ReadActiveMatchRaw();
            if (marker == null)
                return null;

            newestBackup = FindNewestBackupFor(marker);
            DateTime lastActivity = File.GetLastWriteTimeUtc(path);
            if (newestBackup != null && newestBackup.LastWriteTimeUtc > lastActivity)
                lastActivity = newestBackup.LastWriteTimeUtc;
            int maxAge = Math.Max(1, autoRecoverMaxAge.Value);
            if (DateTime.UtcNow - lastActivity > TimeSpan.FromMinutes(maxAge))
            {
                Log($"[ActiveMatch] Match {marker.MatchId} was last active {(DateTime.UtcNow - lastActivity).TotalMinutes:F0} min ago (limit {maxAge} min); not recovering it.");
                return null;
            }
            return marker;
        }

        private ActiveMatchMarker? ReadActiveMatchRaw()
        {
            string path = Path.Combine(BackupFolderPath(), ActiveMatchFileName);
            if (!File.Exists(path))
                return null;
            try
            {
                var marker = JsonSerializer.Deserialize<ActiveMatchMarker>(File.ReadAllText(path));
                return marker == null || string.IsNullOrWhiteSpace(marker.MatchJson) ? null : marker;
            }
            catch (Exception e)
            {
                Log($"[ActiveMatch] {ActiveMatchFileName} is unreadable, ignoring it: {e.Message}");
                return null;
            }
        }

        // The backup an admin most likely wants after a crash: the newest one of the interrupted
        // match (active_match.json, whatever its age), else the newest MatchZy round backup at all.
        private FileInfo? NewestRecoverableBackup()
        {
            var marker = ReadActiveMatchRaw();
            if (marker != null && FindNewestBackupFor(marker) is FileInfo forMarker)
                return forMarker;
            string folder = BackupFolderPath();
            if (!Directory.Exists(folder))
                return null;
            return new DirectoryInfo(folder)
                .GetFiles("matchzy_*_round*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static readonly System.Text.RegularExpressions.Regex BackupNamePattern =
            new(@"^matchzy_(\d+)_(\d+)_round\d+\.json$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Turns what admins actually type into a MatchZy backup file name. Accepted: the .json name,
        // the .txt name Valve's mp_backup_restore_load_file uses, a name without extension, a path,
        // "latest" (or nothing), or a round number (of the live match, else of the interrupted one).
        // Returns "" when nothing fits.
        private string ResolveBackupName(string raw)
        {
            string arg = (raw ?? "").Trim().Trim('"').Trim();
            if (arg == "" || arg.Equals("latest", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("last", StringComparison.OrdinalIgnoreCase) || arg.Equals("newest", StringComparison.OrdinalIgnoreCase))
                return NewestRecoverableBackup()?.Name ?? "";

            if (int.TryParse(arg, out int round) && round >= 0)
            {
                string r = round.ToString("D2");
                if (isMatchLive && liveMatchId > 0)
                    return $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{r}.json";
                var newest = NewestRecoverableBackup();
                var m = newest == null ? null : BackupNamePattern.Match(newest.Name);
                return m is { Success: true } ? $"matchzy_{m.Groups[1].Value}_{m.Groups[2].Value}_round{r}.json" : "";
            }

            string name = Path.GetFileName(arg.Replace('\\', '/'));
            string ext = Path.GetExtension(name);
            if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext == "")
                name = Path.ChangeExtension(name, ".json");
            return name;
        }

        private static readonly System.Text.RegularExpressions.Regex ValveTxtNamePattern =
            new(@"^matchzy_(\d+)_(\d+)_round(\d+)\.(txt|json)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // .txt fallback: Valve's round file exists but MatchZy's .json of the same name does not (the
        // JSON was never written, deleted, or the round came from the engine alone). The .txt only
        // has the score, money and weapons, so MatchZy's part is rebuilt and written as that .json:
        //  1. from another MatchZy backup of the same match and map (config, teams, match id), with
        //     team1's side worked out for the target round from the halftime / overtime parity;
        //  2. else from the loaded match, or the interrupted match in active_match.json (loaded
        //     first), with the sides it starts with, flipped by the same parity.
        // The normal restore then runs on it and uses the .txt from disk. Returns the .json name,
        // or "" when there is nothing to rebuild from (the caller then lets the .txt run as is).
        private string BuildBackupFromValveTxt(string name, out string source)
        {
            source = "";
            var m = ValveTxtNamePattern.Match(Path.GetFileName(name));
            if (!m.Success)
                return "";
            string id = m.Groups[1].Value, mapNo = m.Groups[2].Value, roundText = m.Groups[3].Value;
            if (!int.TryParse(roundText, out int round) || !long.TryParse(id, out long matchId))
                return "";
            string txtName = $"matchzy_{id}_{mapNo}_round{roundText}.txt";
            string jsonName = $"matchzy_{id}_{mapNo}_round{roundText}.json";
            if (FindValveRoundBackupOnDisk(txtName) == null)
                return "";
            string folder = BackupFolderPath();

            Dictionary<string, string>? data = null;
            string? refSide = null;
            int refRound = 0;
            if (Directory.Exists(folder))
            {
                var reference = new DirectoryInfo(folder)
                    .GetFiles($"matchzy_{id}_{mapNo}_round*.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (reference != null)
                {
                    try
                    {
                        data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(reference.FullName),
                            new JsonSerializerOptions { AllowTrailingCommas = true });
                    }
                    catch (Exception e)
                    {
                        Log($"[TxtFallback] {reference.Name} is unreadable: {e.Message}");
                    }
                    if (data != null)
                    {
                        refSide = data.GetValueOrDefault("team1_side");
                        int.TryParse(data.GetValueOrDefault("round"), out refRound);
                        source = reference.Name;
                    }
                }
            }

            if (data == null)
            {
                // No MatchZy backup of this match: use the loaded match, else the interrupted one.
                string mapName = Server.MapName;
                if (!isMatchSetup)
                {
                    var marker = ReadActiveMatchRaw();
                    if (marker == null || (marker.MatchId > 0 && marker.MatchId != matchId))
                        return "";
                    if (!LoadMatchFromJSON(marker.MatchJson, skipMapChange: IsSameMap(marker.MapName, Server.MapName)))
                        return "";
                    source = ActiveMatchFileName;
                    if (!string.IsNullOrEmpty(marker.MapName))
                        mapName = marker.MapName;
                }
                else
                {
                    source = "loaded match";
                }
                if (!teamSides.TryGetValue(matchzyTeam1, out refSide))
                    refSide = "CT";
                refRound = 0;
                data = new Dictionary<string, string>
                {
                    { "matchid", id },
                    { "map_name", mapName },
                    { "game_mode", "live" },
                    { "match_loaded", "True" },
                };
            }

            // team1's side at the target round from the reference round's side.
            bool flip = AreSidesSwitchedAfter(round) != AreSidesSwitchedAfter(refRound);
            string side1 = refSide == "TERRORIST" ? "TERRORIST" : "CT";
            if (flip)
                side1 = side1 == "CT" ? "TERRORIST" : "CT";
            data["team1_side"] = side1;
            data["team2_side"] = side1 == "CT" ? "TERRORIST" : "CT";
            data["matchid"] = id;
            data["mapnumber"] = mapNo;
            data["round"] = roundText;
            data["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            // The engine's .txt brings the score, money and weapons; nothing round-specific from
            // the reference may override it.
            data["valve_backup"] = "";
            data["scoreboard"] = "";
            data["advanced_stats"] = "";
            foreach (var key in new[] { "team1_score", "team2_score", "team1_score_ct", "team1_score_t", "team2_score_ct", "team2_score_t" })
                data.Remove(key);
            data["rebuilt_from_txt"] = "true";
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, jsonName), JsonSerializer.Serialize(data));
            }
            catch (Exception e)
            {
                Log($"[TxtFallback] Could not write {jsonName}: {e.Message}");
                return "";
            }
            Log($"[TxtFallback] Rebuilt {jsonName} from {txtName} + {source}: team1 '{matchzyTeam1.teamName}' on {side1} at round {round}.");
            PrintLocalizedToAdmins("matchzy.recover.fromtxt", txtName, side1 == "CT" ? "CT" : "T");
            return jsonName;
        }

        // Shared by matchzy_loadbackup and matchzy_recover.
        private void LoadBackupFromArgument(CCSPlayerController? player, string rawArgs, string caller)
        {
            string fileName = ResolveBackupName(rawArgs);
            if (fileName == "")
            {
                Log($"[LoadBackup] No backup found for '{rawArgs}' ({caller}).");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backupmsg.loadbackupbadname", rawArgs));
                ShowRecentBackupsFromDisk(player);
                return;
            }
            if (!File.Exists(Path.Combine(BackupFolderPath(), fileName)))
            {
                string rebuilt = BuildBackupFromValveTxt(fileName, out _);
                if (rebuilt != "")
                {
                    Log($"[LoadBackup] '{rawArgs}' -> no {fileName}; restoring the rebuilt {rebuilt} ({caller}).");
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.recover.using", rebuilt));
                    RestoreRoundBackup(player, rebuilt);
                    return;
                }
                Log($"[LoadBackup] '{rawArgs}' -> {fileName}, which does not exist ({caller}).");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.backup.restoredoesntexist", fileName));
                ShowRecentBackupsFromDisk(player);
                return;
            }
            Log($"[LoadBackup] '{rawArgs}' -> restoring {fileName} for {caller}.");
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.recover.using", fileName));
            RestoreRoundBackup(player, fileName);
        }

        // .recover / matchzy_recover [latest | round | file]: bring back the interrupted match
        // without knowing a file name. No argument = the newest backup of the interrupted match.
        [ConsoleCommand("matchzy_recover", "Restore the interrupted match: newest backup, a round number or a backup file")]
        [ConsoleCommand("css_recover", "Restore the interrupted match: newest backup, a round number or a backup file")]
        public void OnRecoverCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            string args = command?.ArgString ?? "";
            string caller = player == null ? "server console" : player.PlayerName;
            Server.NextFrame(() => LoadBackupFromArgument(player, args, caller));
        }

        // Newest MatchZy round backup written for the marker's match since it was loaded. Prefers the
        // marker's match id; a match id that changed after the load (allocated later) falls back to
        // the newest backup written since then.
        private FileInfo? FindNewestBackupFor(ActiveMatchMarker marker)
        {
            string folder = BackupFolderPath();
            if (!Directory.Exists(folder))
                return null;
            var since = marker.SavedAtUtc.AddMinutes(-1);
            var files = new DirectoryInfo(folder)
                .GetFiles("matchzy_*_round*.json")
                .Where(f => f.LastWriteTimeUtc >= since)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            if (files.Count == 0)
                return null;
            string prefix = $"matchzy_{marker.MatchId}_";
            return files.FirstOrDefault(f => f.Name.StartsWith(prefix, StringComparison.Ordinal)) ?? files[0];
        }

        // Called a few seconds after server_ready.
        private void ScheduleAutoRecover()
        {
            AddTimer(AutoRecoverDelay, () =>
            {
                try
                {
                    TryAutoRecover();
                }
                catch (Exception e)
                {
                    Log($"[AutoRecover] {e.Message}");
                }
            });
        }

        private void TryAutoRecover()
        {
            if (!autoRecover.Value)
                return;
            // A panel that loaded a match (or an admin who started something) in the meantime wins.
            if (isMatchSetup || matchStarted || isPractice || isDryRun || isVeto || isRoundRestorePending)
                return;
            var marker = ReadActiveMatch(out var backup);
            if (marker == null)
                return;

            if (backup != null)
            {
                Log($"[AutoRecover] Match {marker.MatchId} was interrupted. Restoring {backup.Name}.");
                PrintLocalizedToAll("matchzy.recover.restoring", marker.MatchId, backup.Name);
                bool ok = RestoreRoundBackup(null, backup.Name);
                PublishMatchRecovered(marker.MatchId, ok ? "round_backup" : "failed", backup.Name);
                if (ok)
                    return;
                Log($"[AutoRecover] Restoring {backup.Name} was refused; loading the match config instead.");
            }

            Log($"[AutoRecover] Match {marker.MatchId} was interrupted before a round backup. Loading its match config again.");
            bool sameMap = IsSameMap(marker.MapName, Server.MapName);
            if (LoadMatchFromJSON(marker.MatchJson, skipMapChange: sameMap))
            {
                PrintLocalizedToAll("matchzy.recover.reloaded", marker.MatchId);
                PublishMatchRecovered(marker.MatchId, "match_config", null);
            }
            else
            {
                Log($"[AutoRecover] The saved match config of match {marker.MatchId} could not be loaded.");
                PublishMatchRecovered(marker.MatchId, "failed", null);
            }
        }

        private void PublishMatchRecovered(long matchId, string source, string? file)
        {
            try
            {
                PublishLifecycleEvent(new MatchRecoveredEvent
                {
                    MatchId = matchId,
                    MapName = Server.MapName,
                    PluginVersion = ModuleVersion,
                    Source = source,
                    BackupFile = file,
                });
            }
            catch (Exception e)
            {
                Log($"[AutoRecover] {e.Message}");
            }
        }

        // Connect gate: no match is loaded, but this player is on the roster of the interrupted
        // match that active_match.json still holds. Kicking them would lock the teams out of their
        // own match (the recovery or an admin's matchzy_loadbackup brings the match back).
        private bool IsOnInterruptedMatchRoster(ulong steamId, out long matchId)
        {
            matchId = 0;
            if (!activeMatchCacheValid)
            {
                cachedActiveMatch = ReadActiveMatch(out _);
                cachedActiveMatchConfig = null;
                activeMatchCacheValid = true;
            }
            var marker = cachedActiveMatch;
            if (marker == null)
                return false;
            if (cachedActiveMatchConfig == null)
            {
                try { cachedActiveMatchConfig = JObject.Parse(marker.MatchJson); }
                catch { return false; }
            }
            var cfg = cachedActiveMatchConfig;
            matchId = marker.MatchId;
            foreach (var section in new[] { "team1", "team2", "spectators" })
            {
                var block = cfg[section];
                if (block == null)
                    continue;
                if (LookupRosterEntry(block["players"], steamId) || LookupRosterEntry(block["coaches"], steamId))
                    return true;
            }
            return false;
        }

        // matchzy_raw_backup_restore: redirect (default) | block | allow.
        private HookResult OnRawBackupRestoreCommand(CCSPlayerController? player, CommandInfo info)
        {
            try
            {
                string file = info.ArgCount > 1 ? info.GetArg(1).Trim().Trim('"') : "";
                if (ownRawRestoreFile != null && string.Equals(file, ownRawRestoreFile, StringComparison.OrdinalIgnoreCase))
                {
                    ownRawRestoreFile = null;
                    return HookResult.Continue;
                }
                string mode = (rawBackupRestoreMode.Value ?? "redirect").Trim().ToLowerInvariant();
                if (mode == "allow" || file == "")
                {
                    if (file != "")
                        Log($"[RawRestore] mp_backup_restore_load_file {file} runs without MatchZy (matchzy_raw_backup_restore allow): rosters, team names and pause state are not restored.");
                    return HookResult.Continue;
                }

                string jsonName = Path.GetFileNameWithoutExtension(file) + ".json";
                bool hasMatchZyBackup = File.Exists(Path.Combine(BackupFolderPath(), jsonName));
                if (mode == "redirect" && hasMatchZyBackup)
                {
                    Log($"[RawRestore] mp_backup_restore_load_file {file} -> MatchZy restore of {jsonName}.");
                    PrintLocalizedToAdmins("matchzy.rawrestore.redirected", jsonName);
                    Server.NextFrame(() =>
                    {
                        try { RestoreRoundBackup(null, jsonName); }
                        catch (Exception e) { Log($"[RawRestore] {e.Message}"); }
                    });
                    return HookResult.Stop;
                }

                if (mode == "redirect")
                {
                    // No MatchZy .json: rebuild it from the .txt plus another backup of the match or
                    // the interrupted / loaded match, so the restore still brings rosters and names.
                    string rebuilt = BuildBackupFromValveTxt(file, out string rebuiltFrom);
                    if (rebuilt != "")
                    {
                        Log($"[RawRestore] mp_backup_restore_load_file {file} -> MatchZy restore of rebuilt {rebuilt} (from {rebuiltFrom}).");
                        PrintLocalizedToAdmins("matchzy.rawrestore.redirected", rebuilt);
                        Server.NextFrame(() =>
                        {
                            try { RestoreRoundBackup(null, rebuilt); }
                            catch (Exception e) { Log($"[RawRestore] {e.Message}"); }
                        });
                        return HookResult.Stop;
                    }
                    // Nothing to rebuild from (no backup of this match and no match): let it run.
                    Log($"[RawRestore] mp_backup_restore_load_file {file}: no MatchZy backup {jsonName}, running it as is. Rosters, team names and pause state are not restored.");
                    return HookResult.Continue;
                }
                Log($"[RawRestore] Blocked mp_backup_restore_load_file {file} (matchzy_raw_backup_restore block). Use matchzy_loadbackup <file>.json or .restore <round>.");
                PrintLocalizedToAdmins("matchzy.rawrestore.blocked");
                return HookResult.Stop;
            }
            catch (Exception e)
            {
                Log($"[RawRestore] {e.Message}");
                return HookResult.Continue;
            }
        }
    }
}
