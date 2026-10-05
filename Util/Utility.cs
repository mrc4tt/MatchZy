using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Case-resolved relative cfg paths (e.g. "matchzy/warmup.cfg" or "MatchZy/warmup.cfg")
        // - see MatchZyCfgRel. Computed so they follow the on-disk folder casing.
        public string warmupCfgPath => MatchZyCfgRel("warmup.cfg");
        public string knifeCfgPath => MatchZyCfgRel("knife.cfg");
        public string liveCfgPath => MatchZyCfgRel("live.cfg");
        public string liveWingmanCfgPath => MatchZyCfgRel("live_wingman.cfg");
        public string scrimCfgPath => MatchZyCfgRel("scrim.cfg");
        public string hillCfgPath => MatchZyCfgRel("hill.cfg");

        private void PrintToAllChat(string message)
        {
            Server.PrintToChatAll($"{chatPrefix} {message}");
        }

        /// <summary>
        /// Sends a localized message to each player in their own language.
        /// Uses Localizer.ForPlayer() per-player instead of a single server-wide broadcast.
        /// </summary>
        private void PrintLocalizedToAll(string key, params object[] args)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot)
                    continue;
                // CSTV gets it too, in the server language, so it shows in demos and the broadcast
                // (Server.PrintToChatAll used to reach it).
                string text = player.IsHLTV ? Localizer[key, args] : Localizer.ForPlayer(player, key, args);
                player.PrintToChat($"{chatPrefix} {text}");
            }
        }

        private void PrintToPlayerChat(CCSPlayerController player, string message)
        {
            player.PrintToChat($"{chatPrefix} {message}");
        }

        private void PrintToAdmins(string message)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (AdminManager.PlayerHasPermissions(player, "@css/generic"))
                {
                    player.PrintToChat($"{chatPrefix} {message}");
                }
            }
        }

        // Like PrintToAdmins, but each admin gets the message in their own language.
        private void PrintLocalizedToAdmins(string key, params object[] args)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
                    continue;
                if (AdminManager.PlayerHasPermissions(player, "@css/generic"))
                    player.PrintToChat($"{chatPrefix} {Localizer.ForPlayer(player, key, args)}");
            }
        }

        private void ReplyToUserCommand(CCSPlayerController? player, string message, bool console = false)
        {
            if (player == null)
            {
                Server.PrintToConsole($"{chatPrefix} {message}");
            }
            else
            {
                if (console)
                {
                    player.PrintToConsole($"{chatPrefix} {message}");
                }
                else
                {
                    player.PrintToChat($"{chatPrefix} {message}");
                }
            }
        }

        // Single source of truth for the MatchZy config directory, resolved
        // case-insensitively (reuses an existing "MatchZy" OR "matchzy", else defaults to
        // lowercase "matchzy"). A server picks its casing simply by creating/naming that
        // folder under csgo/cfg - every MatchZy file (cfgs, savednades, admins, whitelist)
        // then lives in it consistently. Cached: the resolved name is stable per session.
        private string? _matchZyCfgDirCache;
        private string MatchZyCfgDir => _matchZyCfgDirCache ??= new ConfigManager().GetMatchZyCfgDir();
        // Folder name only (e.g. "matchzy") - for `exec <name>/x.cfg` paths, which are
        // relative to csgo/cfg.
        private string MatchZyCfgDirName => Path.GetFileName(MatchZyCfgDir.TrimEnd('/', '\\'));
        // Relative path for exec/execifexists and Path.Join(gamedir/csgo/cfg, ...): "<name>/<file>".
        private string MatchZyCfgRel(string file) => $"{MatchZyCfgDirName}/{file}";

        // Optional per-server override for a mode cfg: "<mode>_override.cfg" next to it (e.g.
        // warmup_override.cfg). The plugin never ships, creates or rewrites these files, so an
        // admin's changes survive every update. It runs right after the mode cfg, before the match
        // config cvars, so its values win over the mode cfg and the match config wins over both.
        private static string ModeOverrideExec(string cfgPath) =>
            $"execifexists {cfgPath.Substring(0, cfgPath.Length - ".cfg".Length)}_override.cfg";

        // Runs a mode cfg: the built-in default's settings first, then the server's mode cfg, then
        // its override. A cvar the server's cfg does not set (an older or trimmed copy) used to keep
        // whatever the previous mode left: mp_weapons_allow_typecount 0 from a warmup override
        // carried into a live.cfg without that line, and nobody could buy in the match. The
        // defaults only fill those gaps; every value the server's cfg or override sets still wins,
        // and the match config cvars (applied after) win over all of it.
        // The match config cvars are applied last, so they win in every mode (warmup and knife
        // included), not only where a caller re-applies them.
        private void ExecModeCfg(string cfgPath, string extraCommands = "")
        {
            foreach (string chunk in ModeDefaultSettings(Path.GetFileName(cfgPath)))
                Server.ExecuteCommand(chunk);
            Server.ExecuteCommand($"exec {cfgPath};{ModeOverrideExec(cfgPath)}{extraCommands}");
            if (matchConfig.ChangedCvars.Count > 0)
                ExecuteChangedConvars();
        }

        // Commands (mp_warmup_start/end, ...) are left out: only "name value" settings are applied,
        // so nothing in the default runs twice. Server-level settings that some default cfgs carry
        // (hibernation, CSTV voice, sv_lan, sv_pure, Steam group, ...) are left out too: they belong
        // to the server's own config, and filling them in would override server.cfg on every mode
        // switch. Split into short commands for the command buffer.
        private static readonly Dictionary<string, List<string>> _modeDefaultSettings = new();
        private static readonly HashSet<string> _modeDefaultSkip = new(StringComparer.OrdinalIgnoreCase)
        {
            "exec", "execifexists", "mp_restartgame", "mp_warmup_start", "mp_warmup_end",
            "sv_hibernate_when_empty", "sv_hibernate_postgame_delay", "tv_relayvoice", "sv_lan",
            "sv_pure", "sv_pure_kick_clients", "sv_pure_trace", "sv_steamgroup_exclusive",
            "sv_kick_ban_duration", "sv_competitive_minspec", "mp_logdetail",
        };

        // Cuts a "//" comment and splits on ";", both only outside quotes, so a quoted value
        // ("http://...", "a;b") stays whole.
        private static List<string> SplitCfgLine(string line)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                    inQuotes = !inQuotes;
                else if (!inQuotes && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    break;
                else if (!inQuotes && c == ';')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            parts.Add(current.ToString());
            return parts;
        }

        private static List<string> ModeDefaultSettings(string fileName)
        {
            if (_modeDefaultSettings.TryGetValue(fileName, out var cached))
                return cached;
            var chunks = new List<string>();
            var sb = new System.Text.StringBuilder();
            string content = ConfigManager.ReadDefaultCfg(fileName) ?? "";
            foreach (string rawLine in content.Split('\n'))
            {
                foreach (string part in SplitCfgLine(rawLine))
                {
                    string setting = part.Trim();
                    int space = setting.IndexOfAny(new[] { ' ', '\t' });
                    // No value: a command, not a setting.
                    if (space <= 0 || _modeDefaultSkip.Contains(setting.Substring(0, space)))
                        continue;
                    if (sb.Length > 0 && sb.Length + setting.Length > 900)
                    {
                        chunks.Add(sb.ToString());
                        sb.Clear();
                    }
                    if (sb.Length > 0)
                        sb.Append(';');
                    sb.Append(setting);
                }
            }
            if (sb.Length > 0)
                chunks.Add(sb.ToString());
            _modeDefaultSettings[fileName] = chunks;
            return chunks;
        }

        private void LoadAdmins()
        {
            // Reset first so a reload actually drops admins removed from the file
            // (upstream mutated the field in place, leaving stale entries on reload).
            loadedAdmins = new Dictionary<string, string>();

            string configDir = MatchZyCfgDir;
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);
            string filePath = Path.Join(configDir, "admins.json");

            if (!File.Exists(filePath))
            {
                // Write a template so the file is discoverable and self-documenting. The
                // placeholder key is non-numeric, so it never matches a real SteamID64.
                try
                {
                    Dictionary<string, string> template = new() { { "STEAM_ID_64_HERE", "" } };
                    File.WriteAllText(filePath, JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true }));
                    Log($"[LoadAdmins] No admins.json found - created a template at '{filePath}'.");
                }
                catch (Exception e)
                {
                    Log($"[LoadAdmins] Failed to create admins.json at '{filePath}': {e.Message}");
                }
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    Log($"[LoadAdmins] admins.json is empty at '{filePath}'.");
                    return;
                }

                JsonSerializerOptions options = new()
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                };
                Dictionary<string, string> parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();

                // Keep only real SteamID64 keys (17-digit numeric). IsPlayerAdmin compares
                // against player.SteamID.ToString() (a SteamID64), so template/placeholder
                // rows like "STEAM_ID_64_HERE" or "steamid" are dropped and can't grant admin.
                foreach (var kvp in parsed)
                {
                    string sid = kvp.Key.Trim();
                    if (sid.Length == 17 && ulong.TryParse(sid, out _))
                    {
                        loadedAdmins[sid] = kvp.Value ?? "";
                        // Flags in the value were honored in 0.8.89 - 0.8.92 and limited that admin.
                        // They are ignored now, so such an entry is a FULL admin: say so loudly.
                        if ((kvp.Value ?? "").Contains('@'))
                            Log($"[LoadAdmins] WARNING: admins.json entry {sid} has '{kvp.Value}' as its value. Flags are no longer read there, so this player is a FULL MatchZy admin. For a limited admin, remove the entry and use CounterStrikeSharp's admins.json with flags.");
                    }
                }

                Log($"[LoadAdmins] Loaded {loadedAdmins.Count} admin(s).");
            }
            catch (Exception e)
            {
                Log($"[LoadAdmins] Failed to parse admins.json at '{filePath}': {e.Message}");
            }
        }

        private bool IsPlayerAdmin(CCSPlayerController? player, string command = "", params string[] permissions)
        {
            if (everyoneIsAdmin.Value)
                return true; // Everyone is treated as admin if matchzy_everyone_is_admin is true.
            string[] updatedPermissions = permissions.Concat(new[] { "@css/root" }).ToArray();
            RequiresPermissionsOr attr = new(updatedPermissions) { Command = command };
            if (attr.CanExecuteCommand(player))
                return true; // Admin exists in admins.json of CSSharp
            if (player == null)
                return true; // Sent via server, hence should be treated as an admin.
            // MatchZy's admins.json lists SteamID64s only: every listed player is a full MatchZy admin.
            // The value is free text (e.g. the player's name) and is not read as flags; limited
            // admins belong in CounterStrikeSharp's own admin system, which is checked above.
            return loadedAdmins.ContainsKey(player.SteamID.ToString());
        }

        private int GetRealPlayersCount()
        {
            return playerData.Count;
        }

        // matchzy_ready_hint_style 2: upstream MatchZy's ready reminder. No center text; every
        // matchzy_chat_messages_timer_delay seconds the chat lists who is not ready yet, or how many
        // are ready once everyone is. Each player reads it in their own language.
        private void SendUnreadyPlayersMessage()
        {
            if (!isWarmup || matchStarted || !readyAvailable || isDryRun || readyHintStyle.Value != 2)
                return;
            List<string> unreadyPlayers = new();
            int perTeam = readyPerTeam.Value;

            foreach (var key in playerReadyStatus.Keys)
            {
                if (playerReadyStatus[key] == false && playerData.TryGetValue(key, out var p) && p != null && p.IsValid)
                {
                    // Per-team mode: a side that already has N ready players is done, so its
                    // remaining players are not holding anything up.
                    if (perTeam > 0 && (p.TeamNum == (int)CsTeam.CounterTerrorist || p.TeamNum == (int)CsTeam.Terrorist) && IsTeamReady(p.TeamNum))
                        continue;
                    // Coaches do not count towards the ready-up (GetTeamPlayerCount), so they hold nothing up.
                    if (matchzyTeam1.coach.Contains(p) || matchzyTeam2.coach.Contains(p))
                        continue;
                    unreadyPlayers.Add(p.PlayerName);
                }
            }
            if (unreadyPlayers.Count > 0)
            {
                string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                string key = isRoundRestorePending ? "matchzy.ready.readytotestorebackupinfomessage" : "matchzy.utility.unreadyplayers";
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
                        continue;
                    string minimumReadyRequiredMessage = isMatchSetup || readyPerTeam.Value > 0 ? "" : Localizer.ForPlayer(player, "matchzy.minimum.ready.required", $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}");
                    player.PrintToChat($"{chatPrefix} {Localizer.ForPlayer(player, key, unreadyPlayerList, minimumReadyRequiredMessage)}");
                }
            }
            else
            {
                int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
                if (isMatchSetup)
                {
                    PrintLocalizedToAll("matchzy.utility.readyplayers", countOfReadyPlayers);
                }
                else if (perTeam > 0)
                {
                    PrintLocalizedToAll("matchzy.cc.minreadyplayersperteam", perTeam);
                }
                else
                {
                    PrintLocalizedToAll("matchzy.utility.minimumreadyplayers", minimumReadyRequired, countOfReadyPlayers);
                }
            }
            SendShortHandedHints();
        }

        // Arms the style-2 chat reminder while it is selected, and stops it otherwise. Called from
        // the 1 s ready timer, so changing matchzy_ready_hint_style mid-warmup takes effect at once.
        // Chat line to the players of a short-handed side (see ShortHandedSide): how many are
        // missing and that .forceready starts without them. side = null: both sides.
        private void SendShortHandedHints(int? onlySide = null)
        {
            foreach (int side in new[] { (int)CsTeam.CounterTerrorist, (int)CsTeam.Terrorist })
            {
                if (onlySide.HasValue && onlySide.Value != side)
                    continue;
                if (ShortHandedSide(side) is not (int present, int required))
                    continue;
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || p.IsBot || p.IsHLTV || p.TeamNum != side || IsMatchCoach(p))
                        continue;
                    PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.ready.shorthandedchat", present, required, required - present));
                }
            }
        }

        private void SyncUnreadyChatReminder()
        {
            if (readyHintStyle.Value == 2 && readyAvailable && !matchStarted && !isDryRun)
            {
                // No STOP_ON_MAPCHANGE: that kills the timer but leaves this field set, so ??= would never
                // re-arm it on the next map. StartWarmup and the match-start paths kill and null it.
                unreadyPlayerMessageTimer ??= AddTimer(Math.Max(5, chatTimerDelay), SendUnreadyPlayersMessage, TimerFlags.REPEAT);
            }
            else if (unreadyPlayerMessageTimer != null)
            {
                unreadyPlayerMessageTimer.Kill();
                unreadyPlayerMessageTimer = null;
            }
        }

        public void UnreadyHintMessageStart()
        {
            if (!isWarmup || matchStarted)
                return;
            List<string> unreadyPlayers = new();
            List<int> keysToRemove = new(); // Track stale keys

            foreach (var key in playerReadyStatus.Keys.ToList()) // ToList() to avoid collection modification issues
            {
                if (playerReadyStatus[key] == false)
                {
                    if (playerData.TryGetValue(key, out var player) && player != null)
                    {
                        unreadyPlayers.Add(player.PlayerName);
                    }
                    else
                    {
                        // Player no longer exists, mark for removal
                        keysToRemove.Add(key);
                    }
                }
            }

            // Clean up stale entries
            foreach (var key in keysToRemove)
            {
                playerReadyStatus.Remove(key);
            }

            // Style 2 (upstream-style chat reminder) shows no center text.
            if (unreadyPlayers.Count > 0 && readyHintStyle.Value != 2)
            {
                string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                // The center HUD does not wrap: keep the list short, as the ready hint does.
                if (unreadyPlayerList.Length > 64)
                    unreadyPlayerList = unreadyPlayerList.Substring(0, 64) + "...";
                // Per player, in their own language.
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || p.IsBot || p.IsHLTV)
                        continue;
                    p.PrintToCenter($" {Localizer.ForPlayer(p, "matchzy.hint.notready", unreadyPlayerList)}");
                }
            }
        }

        // Cached ready-panel DATA (recomputed only on change). Localized HTML is built per
        // player each tick so every player sees the panel in their own language.
        private int _rpReady, _rpRequired, _rpTotal, _rpFilled, _rpCtCount, _rpCtReady, _rpTCount, _rpTReady;
        // Loaded match: players each side needs (roster-sized), 0 otherwise.
        private int _rpCtNeed, _rpTNeed;
        private string _rpWaiting = "";
        private uint _readyTickCounter;
        // Bumped every time ComputeReadyData actually recomputes. Everything the panel renders
        // from the shared numbers (including the progress bar) is keyed on this, so a tick that
        // changed nothing can skip the whole build instead of rebuilding and then discovering
        // the result was identical.
        private int _rpVersion;
        // Inputs of the last full ready-panel pass (RenderReadyPanel skips unchanged ticks).
        private int _rpLastRenderVersion = -1;
        private int _clanTagVersion = -1;
        private string? _rpLastRenderMode;
        private bool _rpLastRenderNotReadyVisible;
        private string _rpBar = "";
        private int _rpBarVersion = -1;
        // Last panel state per player (by userid). The panel is only re-sent when its content
        // changes (or on a slow keepalive) so PrintToCenterHtml does not re-trigger its show
        // animation every tick - that per-tick re-fire is what makes the panel flash.
        // The stamp fields exist so the comparison does not require building the HTML first:
        // rendering one panel is a StringBuilder plus ~6 localizer lookups and as many
        // interpolated strings, and it ran for every player on every tick of the ready phase.
        private readonly Dictionary<int, PanelState> _lastPanelHtml = new();

        private struct PanelState
        {
            public int Version;
            public string Mode;
            public bool NotReadyVisible;
            public bool IsPlaying;
            public bool IsReady;
            public string Html;
        }
        // Cached gamerules proxy for the ready-phase HUD sync (re-fetched when invalid, e.g. after
        // a map change). Avoids a FindAllEntitiesByDesignerName scan every tick.
        private CCSGameRulesProxy? _readyProxy;
        // True while the ready phase runs a "fake warmup" (real warmup is forced off to hide the
        // banner, so warmup behaviour - no round end, respawn, no time expiry - is re-created via
        // convars). Reset when leaving the ready phase so it never leaks into live play.
        private bool _fakeWarmupActive;

        // Recompute the shared ready numbers only when ready status changed.
        private void ComputeReadyData()
        {
            if (!_readyStatusDirty)
                return;

            int readyCount = 0, totalPlayers = 0;
            List<string> notReady = new();
            foreach (var key in playerReadyStatus.Keys)
            {
                if (playerData.TryGetValue(key, out var p) && p != null && p.IsValid)
                {
                    // Only T/CT - spectators aren't part of the ready gate.
                    if (p.TeamNum != 2 && p.TeamNum != 3)
                        continue;
                    totalPlayers++;
                    if (playerReadyStatus[key])
                        readyCount++;
                    else
                        notReady.Add(p.PlayerName);
                }
            }

            (_rpCtCount, _rpCtReady) = GetTeamPlayerCount((int)CsTeam.CounterTerrorist);
            (_rpTCount, _rpTReady) = GetTeamPlayerCount((int)CsTeam.Terrorist);
            int perTeam = readyPerTeam.Value;
            if (perTeam > 0)
            {
                // Per-team ready-up: progress is N per side, extra ready players on one side do not
                // count towards the other.
                _rpReady = Math.Min(_rpCtReady, perTeam) + Math.Min(_rpTReady, perTeam);
                _rpRequired = perTeam * 2;
                _rpCtNeed = _rpTNeed = 0;
            }
            else if (isMatchSetup)
            {
                // Loaded match: what each team needs (players_per_team, or its roster size when
                // smaller), not matchzy_minimum_ready_required, which is the pug total. A side
                // that is ready (incl. .forceready) counts as complete.
                _rpCtNeed = RequiredPlayersOnSide((int)CsTeam.CounterTerrorist);
                _rpTNeed = RequiredPlayersOnSide((int)CsTeam.Terrorist);
                _rpReady = (IsTeamReady((int)CsTeam.CounterTerrorist) ? _rpCtNeed : Math.Min(_rpCtReady, _rpCtNeed))
                    + (IsTeamReady((int)CsTeam.Terrorist) ? _rpTNeed : Math.Min(_rpTReady, _rpTNeed));
                _rpRequired = _rpCtNeed + _rpTNeed;
            }
            else
            {
                _rpCtNeed = _rpTNeed = 0;
                _rpReady = readyCount;
                _rpRequired = minimumReadyRequired > 0 ? minimumReadyRequired : totalPlayers;
            }
            _rpTotal = totalPlayers;
            _rpFilled = Math.Clamp((int)Math.Round(12.0 * _rpReady / Math.Max(1, _rpRequired)), 0, 12);

            string list = string.Join(", ", notReady.Take(6));
            if (notReady.Count > 6)
                list += $" +{notReady.Count - 6}";
            // Bound the length: long / decorated names entity-expand a lot and can push the HTML
            // panel past its size cap, dropping trailing lines.
            if (list.Length > 64)
                list = list.Substring(0, 64) + "...";
            // Raw here (used by both the plain-center text and the HTML panel). The HTML panel
            // entity-encodes it via PanelSafe at render; the classic center text wants it plain.
            _rpWaiting = notReady.Count > 0 ? list : "";


            _readyStatusDirty = false;
            _rpVersion++;
        }

        // Make a string safe for a CS2 center-HTML panel: escape the HTML metacharacters and convert
        // any non-ASCII character to a numeric HTML entity. The panel renderer breaks on raw
        // multibyte UTF-8 (localized text / player names with accents), cutting the line and
        // everything after it; numeric entities render correctly.
        private static string PanelSafe(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default:
                        if (c > 0x7F)
                            sb.Append("&#").Append((int)c).Append(';');
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // Undo the ready-phase HUD manipulation (forced m_bWarmupPeriod=false / m_bGameRestart)
        // before switching to a non-ready mode. A leaked m_bGameRestart makes the engine think a
        // restart is in progress, so the next mode's mp_restartgame/mp_warmup_start are ignored
        // (e.g. practice: prac.cfg loads but the round never restarts and warmup time is wrong).
        // The mode's own cfg (mp_warmup_start / mp_warmup_end) then takes over cleanly.
        private void RestoreReadyPhaseGameState()
        {
            _readyProxy = null;
            // Clear the fake-warmup flag without resetting its convars - the next mode's cfg
            // (e.g. prac.cfg) sets round conditions / respawn itself, so it defines the final state.
            _fakeWarmupActive = false;
            var p = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            if (p?.GameRules == null)
                return;
            p.GameRules.GameRestart = false;
            p.GameRules.WarmupPeriod = true;
            Utilities.SetStateChanged(p, "CCSGameRulesProxy", "m_pGameRules");
        }

        // 1s timer: keep data fresh, re-assert clan tags ([READY]/[UNREADY] scoreboard), and
        // - in classic style (matchzy_ready_hint_style 0) - broadcast the plain center text.
        private void SendReadyStatusHintMessage()
        {
            // Dryrun (.dryrun) is a practice-style mode with no ready gate - don't show the ready hint.
            if (!readyAvailable || matchStarted || isDryRun)
                return;
            try
            {
                ComputeReadyData();
                // Tags only change with the ready data (ready toggles, connects, team changes all bump
                // its version); event paths that need an immediate update call HandleClanTags directly.
                if (_clanTagVersion != _rpVersion)
                {
                    _clanTagVersion = _rpVersion;
                    HandleClanTags();
                }
                SyncUnreadyChatReminder();
                HandleJoinStartCountdown();

                if (readyHintStyle.Value == 0)
                {
                    // Classic plain-center hint, built per player so everyone reads it in their own
                    // language (it used to be one broadcast in the server's language). Built from
                    // the cached numbers: a few localizer lookups per player once a second.
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p == null || !p.IsValid || p.IsBot || p.IsHLTV)
                            continue;
                        p.PrintToCenter(BuildClassicReadyHint(p));
                    }
                }
            }
            catch (Exception e)
            {
                // Usually the server not being ready yet (retried on the next tick); logged because
                // the join-mode match start runs in here too.
                Log($"[SendReadyStatusHintMessage] {e.Message}");
            }
        }

        private string BuildClassicReadyHint(CCSPlayerController player)
        {
            string line1 = Localizer.ForPlayer(player, "matchzy.hint.waitingforplayers", _rpReady, _rpTotal);
            string line2 = Localizer.ForPlayer(player, "matchzy.hint.usereadycommand");
            if (ShortHandedSide(player.TeamNum) is (int shortPresent, int shortRequired))
                line2 += "\n" + Localizer.ForPlayer(player, "matchzy.ready.shorthanded", shortPresent, shortRequired);
            return _rpWaiting.Length > 0
                ? $"{line1}\n{line2}\n{Localizer.ForPlayer(player, "matchzy.hint.notready", _rpWaiting)}"
                : $"{line1}\n{line2}";
        }

        public override void Unload(bool hotReload)
        {
            // The ready-phase "fake warmup" (matchzy_ready_hint_style 1) forces m_bWarmupPeriod off +
            // RoundTime 0 + mp_ignore_round_win_conditions each tick from RenderReadyPanel. If the
            // plugin unloads/reloads while it is active, that per-tick upkeep stops but the gamerules
            // are left mid-override, so the round instantly times out ("penalty for running out of
            // time", the score jumps). Restore normal warmup so unload/reload leaves a sane state.
            // A plain unload (server quit, css_plugins unload) announces itself before the queue
            // closes; quit already did so from its command listener. Not on a hot reload: the new
            // instance sends server_ready.
            if (!hotReload)
                AnnounceShutdown("plugin_unload");
            CloseEventQueue();
            if (_fakeWarmupActive)
            {
                Server.ExecuteCommand("mp_ignore_round_win_conditions 0; mp_respawn_on_death_ct 0; mp_respawn_on_death_t 0; mp_warmup_start; mp_warmup_pausetimer 1");
                _fakeWarmupActive = false;
            }
            base.Unload(hotReload);
        }

        // OnTick: re-send the center HTML panel EVERY tick so the native "WARMUP" HUD (from
        // mp_warmup_pausetimer) cannot flicker through it. Rendered per player and localized,
        // so each sees the panel in their own language with their OWN ready state highlighted.
        private void RenderReadyPanel()
        {
            // Styles 1 (HTML panel) and 2 (chat reminder) both run the ready phase as a "fake
            // warmup" with real warmup off, because that is the only way to remove the native
            // "Warmup" pill (client Panorama, keyed on m_bWarmupPeriod). Only style 1 draws the panel.
            int hintStyle = readyHintStyle.Value;
            if (!readyAvailable || matchStarted || (hintStyle != 1 && hintStyle != 2) || isDryRun)
            {
                // Left the ready phase (dryrun / classic style / match started) - drop the fake-warmup
                // override so it never leaks into live play. Fires within a tick of matchStarted
                // flipping, well inside the knife/live freezetime, so the round conditions are normal
                // before play.
                if (_fakeWarmupActive)
                {
                    Server.ExecuteCommand("mp_ignore_round_win_conditions 0; mp_respawn_on_death_ct 0; mp_respawn_on_death_t 0");
                    _fakeWarmupActive = false;

                    // Mode 1 ended real warmup (m_bWarmupPeriod=false) + set mp_roundtime 60 to hide the
                    // native warmup HUD. If we are leaving mode 1 but are STILL in the ready phase (e.g.
                    // switched to classic style 0), re-START real warmup so the native warmup HUD comes
                    // back instead of a leftover 60:00 round timer. A bare m_bWarmupPeriod=true netvar
                    // flip is NOT enough - the client warmup HUD only re-appears when warmup actually
                    // (re)starts, so run mp_warmup_start. Skip when the match is starting - live play
                    // must not be forced back into warmup.
                    if (readyAvailable && !matchStarted)
                    {
                        Server.ExecuteCommand("mp_warmup_start; mp_warmup_pausetimer 1");
                        // The last HTML panel (mode 1) has a ~5s center-HUD lifetime and would linger
                        // on top of the classic center hint after switching to mode 0. Blank it now and
                        // draw the classic hint immediately instead of waiting for its 1s timer.
                        _lastPanelHtml.Clear();
                        foreach (var p in Utilities.GetPlayers())
                        {
                            if (p != null && p.IsValid && !p.IsBot && !p.IsHLTV)
                                p.PrintToCenterHtml(" ", 1);
                        }
                        SendReadyStatusHintMessage();
                    }
                }
                return;
            }
            try
            {
                ComputeReadyData();
                _readyTickCounter++;

                // Ready-phase HUD sync (per tick):
                //  - Hide the native "WARMUP" banner: m_bWarmupPeriod=false hides the client HUD
                //    netvar while the plugin ready gate still holds players.
                //  - Anti-flash: with warmup forced off, the center HTML panel becomes subject to
                //    CS2's game-restart HUD flashing, so sync m_bGameRestart to (RestartRoundTime <
                //    now). Technique from Ghost23161/FlashingHtmlHudFix. One SetStateChanged on
                //    m_pGameRules (not a specific field - that fails offset resolution) covers both.
                if (_readyProxy == null || !_readyProxy.IsValid)
                    _readyProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                var gr = _readyProxy?.GameRules;
                if (gr != null)
                {
                    bool changed = false;
                    if (gr.WarmupPeriod)
                    {
                        gr.WarmupPeriod = false;
                        changed = true;
                        if (!_fakeWarmupActive)
                        {
                            // Forcing real warmup off (to hide the banner) also disables warmup
                            // behaviour - the round would start counting and could end. Re-create a
                            // "fake warmup" so the ready phase still plays like warmup: round never
                            // ends, no time expiry, respawn on death. Reset on leaving ready (above).
                            Server.ExecuteCommand("mp_ignore_round_win_conditions 1; mp_respawn_on_death_ct 1; mp_respawn_on_death_t 1; mp_roundtime 60; mp_roundtime_defuse 60; mp_roundtime_hostage 60");
                            _fakeWarmupActive = true;
                        }
                    }
                    if (!gr.WarmupPeriod)
                    {
                        // Anti-flash is for the center HTML panel only; style 2 draws no panel, so
                        // leave m_bGameRestart to the engine there.
                        bool expectedRestart = gr.RestartRoundTime < Server.CurrentTime;
                        if (hintStyle == 2 && _lastPanelHtml.Count > 0)
                        {
                            // Switched from style 1 mid-warmup: drop the anti-flash m_bGameRestart it
                            // left set and blank the last panel (5s lifetime) instead of letting it linger.
                            _lastPanelHtml.Clear();
                            gr.GameRestart = false;
                            changed = true;
                            foreach (var cp in Utilities.GetPlayers())
                            {
                                if (cp != null && cp.IsValid && !cp.IsBot && !cp.IsHLTV)
                                    cp.PrintToCenterHtml(" ", 1);
                            }
                        }
                        if (hintStyle == 1 && gr.GameRestart != expectedRestart)
                        {
                            gr.GameRestart = expectedRestart;
                            changed = true;
                        }
                        // Neutralize the top round timer during the ready phase. Forcing warmup off
                        // (to hide the native banner) reveals + runs the round timer HUD (the "warmup
                        // timer is back" bug). The client always DRAWS the round timer while warmup is
                        // off - its visibility is keyed on m_bWarmupPeriod, not on the time values, so
                        // no netvar blanks it. So freeze it at a NON-zero value: RoundStartTime=now +
                        // RoundTime=60 each tick pins the remaining time at a static 1:00. Do NOT use 0
                        // here - remaining=0 makes the round perpetually "expired", so CS2 fires the
                        // running-out-of-time penalty and ends rounds every tick (score climbs, and the
                        // leak on unload is instant). 60 never reaches 0, so the round never times out.
                        if (_fakeWarmupActive)
                        {
                            gr.RoundStartTime = Server.CurrentTime;
                            gr.RoundTime = 60;
                            changed = true;
                        }
                    }
                    if (changed)
                        Utilities.SetStateChanged(_readyProxy!, "CCSGameRulesProxy", "m_pGameRules");
                }

                // Catch-all respawn: while the fake warmup suppresses the engine's auto-respawn, keep
                // every T/CT human spawned. Rescues anyone stuck in the observer cam after picking a
                // team (the reported "stuck as spectator" bug) that the join-respawn handler missed.
                // Throttled to ~1s; only the dead (Health <= 0) are respawned so a live player is
                // never yanked to spawn, and coaches are skipped (they are intentionally unspawned).
                if (_fakeWarmupActive && _readyTickCounter % 64 == 0)
                {
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p == null || !p.IsValid || p.IsBot || p.IsHLTV)
                            continue;
                        if (p.TeamNum != (byte)CsTeam.Terrorist && p.TeamNum != (byte)CsTeam.CounterTerrorist)
                            continue;
                        if (matchzyTeam1.coach.Contains(p) || matchzyTeam2.coach.Contains(p))
                            continue;
                        if (p.PlayerPawn?.Value != null && p.PlayerPawn.Value.Health > 0)
                            continue;
                        p.Respawn();
                    }
                }

                // Style 2: no center HUD at all, the chat reminder (SendUnreadyPlayersMessage) carries
                // the ready status.
                if (hintStyle != 1)
                    return;

                // Blink = alternate the NOT-READY line visible/hidden ~twice a second when
                // matchzy_ready_hint_blink is on; always visible otherwise.
                bool notReadyVisible = !readyHintBlinkEnabled.Value || (_readyTickCounter % 64 < 40);
                // Keepalive: force a re-send about every 2s so the 5s panel never expires even if
                // the content is unchanged. Between keepalives we only send on content change.
                bool keepalive = (_readyTickCounter % 128 == 0);

                // Computed fresh each render (not cached in ComputeReadyData): switching
                // .match/.scrim/.hill during the ready phase flips these flags WITHOUT setting
                // _readyStatusDirty (StartScrimMode early-returns in warmup), so a cached mode
                // would go stale. It is only a few bool reads.
                string mode = isMatchSetup ? "Match Setup"
                    : isPlayOutEnabled2 ? "Hill"
                    : isPlayOutEnabled ? "Scrim"
                    : "Match";

                // Depends only on _rpFilled, so it follows the ready-data version rather than
                // allocating two strings and a concat on every tick.
                if (_rpBarVersion != _rpVersion)
                {
                    _rpBar =
                        $"<font class='fontSize-m' color='#37ff8b'>{new string('█', _rpFilled)}</font>" +
                        $"<font class='fontSize-m' color='#3a3a3a'>{new string('█', 12 - _rpFilled)}</font>";
                    _rpBarVersion = _rpVersion;
                }
                string bar = _rpBar;

                // Nothing the panels depend on changed since the last pass (ready data, mode, blink
                // phase) and no keepalive is due: skip the 64-slot walk. Every 8th tick still runs so a
                // team change, which only bumps the ready data on the next 1 s compute, shows within
                // ~125 ms instead of up to a second.
                if (!keepalive
                    && _rpLastRenderVersion == _rpVersion
                    && ReferenceEquals(_rpLastRenderMode, mode)
                    && _rpLastRenderNotReadyVisible == notReadyVisible
                    && _readyTickCounter % 8 != 0)
                    return;
                _rpLastRenderVersion = _rpVersion;
                _rpLastRenderMode = mode;
                _rpLastRenderNotReadyVisible = notReadyVisible;

                // Slot loop instead of Utilities.GetPlayers(): that helper allocates a fresh List
                // on every call, and this runs every tick for the whole ready phase.
                int maxPlayers = Server.MaxPlayers;
                for (int slot = 0; slot < maxPlayers; slot++)
                {
                    var target = Utilities.GetPlayerFromSlot(slot);
                    if (target == null || !target.IsValid || target.IsBot || target.IsHLTV || !target.UserId.HasValue)
                        continue;

                    int uid = target.UserId.Value;
                    bool isPlayingNow = target.TeamNum == 2 || target.TeamNum == 3;
                    bool isReadyNow = isPlayingNow
                        && playerReadyStatus.TryGetValue(uid, out bool readyFlag)
                        && readyFlag;

                    // Nothing this player's panel depends on has changed -> skip the build
                    // entirely. The blink state only matters while they are shown as NOT ready.
                    if (_lastPanelHtml.TryGetValue(uid, out var cachedState)
                        && cachedState.Version == _rpVersion
                        && ReferenceEquals(cachedState.Mode, mode)
                        && cachedState.IsPlaying == isPlayingNow
                        && cachedState.IsReady == isReadyNow
                        && (isReadyNow || !isPlayingNow || cachedState.NotReadyVisible == notReadyVisible))
                    {
                        if (keepalive)
                            target.PrintToCenterHtml(cachedState.Html, 5);
                        continue;
                    }

                    var sb = new StringBuilder();
                    // Every localized/dynamic value goes through PanelSafe: CS2's center-HTML panel
                    // breaks on raw multibyte UTF-8 (Danish o-slash / a-ring, Albanian e-diaeresis),
                    // dropping that line and everything after it (e.g. the trailing NOT-READY line).
                    // WARMUP badge: the native warmup is suppressed (m_bWarmupPeriod=false above), so
                    // the panel carries its own indicator. Static (no per-tick change) so it does not
                    // defeat the change-detection below and re-trigger the panel's show animation.
                    sb.Append($"<font class='fontSize-l' color='#ff9a3c'>&#9679; {PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.warmuptag"))}</font><br>");
                    sb.Append($"<font class='fontSize-m' color='#ffcf3f'>{PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.title"))}</font><br>");
                    sb.Append($"<font class='fontSize-sm' color='#c8c8c8'>{PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.mode", mode))}</font><br>");
                    sb.Append($"{bar} <font class='fontSize-m' color='#ffffff'>{_rpReady} / {_rpRequired}</font><br>");
                    // Loaded match: ready / needed per side; otherwise ready / on the side.
                    int ctOf = _rpCtNeed > 0 ? _rpCtNeed : _rpCtCount;
                    int tOf = _rpTNeed > 0 ? _rpTNeed : _rpTCount;
                    sb.Append($"<font class='fontSize-sm' color='#9ecbff'>CT {_rpCtReady}/{ctOf}</font><font class='fontSize-sm' color='#ffffff'> &nbsp; </font><font class='fontSize-sm' color='#ffb36b'>T {_rpTReady}/{tOf}</font>");
                    // Short-handed team (a rostered player missing): say how to start without them.
                    if (ShortHandedSide(target.TeamNum) is (int shortPresent, int shortRequired))
                        sb.Append($"<br><font class='fontSize-sm' color='#ffcf3f'>{PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.shorthanded", shortPresent, shortRequired))}</font>");

                    // Self-status (YOU ARE (NOT) READY) is the most important line, so render it
                    // BEFORE the "waiting on" list. CS2's center-HTML panel has a size cap and drops
                    // the tail when a long localized string + player names push it over; keeping the
                    // status ahead of the expendable waiting-on line means the status always shows.
                    bool isPlaying = isPlayingNow;
                    if (isPlaying)
                    {
                        if (isReadyNow)
                            sb.Append($"<br><font class='fontSize-m' color='#37ff8b'>&#10004; {PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.youready"))}</font>");
                        else if (notReadyVisible)
                            sb.Append($"<br><font class='fontSize-m' color='#ff3b3b'>&#10008; {PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.notready"))}</font>");
                        else
                            sb.Append("<br><font class='fontSize-m' color='#ff3b3b'>&nbsp;</font>"); // blink off-frame: keep height
                    }

                    if (_rpWaiting.Length > 0)
                        sb.Append($"<br><font class='fontSize-sm' color='#9a9a9a'>{PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.waitingon", _rpWaiting))}</font>");

                    // Only re-send when the content changed (or on the keepalive tick). Re-sending
                    // identical HTML every tick re-triggers the panel's show animation -> flashing.
                    string html = sb.ToString();
                    bool unchanged = _lastPanelHtml.TryGetValue(uid, out var prev) && prev.Html == html;

                    _lastPanelHtml[uid] = new PanelState
                    {
                        Version = _rpVersion,
                        Mode = mode,
                        NotReadyVisible = notReadyVisible,
                        IsPlaying = isPlayingNow,
                        IsReady = isReadyNow,
                        Html = html,
                    };

                    // A stamp can change without the rendered text changing (e.g. a ready count
                    // that does not move the bar). Keep the text comparison as the final gate so
                    // the panel's show animation is not re-triggered.
                    if (!keepalive && unchanged)
                        continue;

                    target.PrintToCenterHtml(html, 5);
                }
            }
            catch (Exception)
            {
                // Server not ready yet; retried on the next tick.
            }
        }

        private void PrintWrappedLine(HudDestination destination, string message)
        {
            if (destination != HudDestination.Center)
            {
                var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var part in parts)
                    Server.PrintToChatAll($" {part}");
            }
            else
                VirtualFunctions.ClientPrintAll(destination, $" {message}", 0, 0, 0, 0, 0);
        }

        private void SendPausedStateMessage()
        {
            if (isPaused && matchStarted)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin")
                {
                    PrintLocalizedToAll("matchzy.pause.adminpausedthematch");
                }
                else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintLocalizedToAll("matchzy.pause.pausedbecauserestore");
                }
                else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintLocalizedToAll("matchzy.pause.teamwantstounpause", reverseTeamSides["TERRORIST"].teamName, reverseTeamSides["CT"].teamName);
                }
                else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintLocalizedToAll("matchzy.pause.teamwantstounpause", reverseTeamSides["CT"].teamName, reverseTeamSides["TERRORIST"].teamName);
                }
                else if (!(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintLocalizedToAll("matchzy.pause.pausedthematch", pauseTeamName);
                }
            }
        }

        private void SendTechPausedStateMessage()
        {
            if (isPaused && matchStarted)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin")
                {
                    PrintLocalizedToAll("matchzy.pause.adminpausedthematch");
                }
                else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintLocalizedToAll("matchzy.pause.pausedbecauserestore");
                }
                else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintLocalizedToAll("matchzy.pause.teamwantstounpause", reverseTeamSides["TERRORIST"].teamName, reverseTeamSides["CT"].teamName);
                }
                else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintLocalizedToAll("matchzy.pause.teamwantstounpause", reverseTeamSides["CT"].teamName, reverseTeamSides["TERRORIST"].teamName);
                }
            }
        }

        private void ExecWarmupCfg()
        {
            if (!warmupEnabled.Value)
            {
                ApplyBotTeam();
                return;
            }
            // Backup
            var absolutePath = Path.Join(Server.GameDirectory, "csgo", "cfg", warmupCfgPath);
            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath)))
            {
                ExecModeCfg(warmupCfgPath);
            }
            else
            {
                KickAllBotsProtectCSTV();
                Server.ExecuteCommand("mp_autokick 0;mp_autoteambalance 0;mp_buy_anywhere 0;mp_buytime 15;mp_death_drop_gun 0;mp_free_armor 1;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_radar_showall 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_solid_teammates 0;mp_spectators_max 20;mp_maxmoney 16000;mp_startmoney 16000;mp_timelimit 0;sv_alltalk 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_deadtalk 1;sv_full_alltalk 0;sv_grenade_trajectory 0;sv_hibernate_when_empty 0;sv_hide_roundtime_until_seconds 1;mp_weapons_allow_typecount -1;ammo_grenade_limit_flashbang 2;mp_respawn_immunitytime 0;sv_infinite_ammo 0;sv_showimpacts 0;sv_voiceenable 1;sm_cvar sv_mute_players_with_social_penalties 0;sv_mute_players_with_social_penalties 0;tv_relayvoice 0;sv_cheats 0;mp_ct_default_melee weapon_knife;mp_ct_default_secondary weapon_hkp2000;mp_ct_default_primary \"\";mp_t_default_melee weapon_knife;mp_t_default_secondary weapon_glock;mp_t_default_primary \"\";mp_maxrounds 24;mp_warmuptime 9999;cash_team_bonus_shorthanded 0;mp_restartgame 1;mp_warmup_online_enabled 1;mp_warmup_start;mp_warmup_pausetimer 1");
            }
            // The warmup cfg sets bot_quota 0; put a bot team back.
            ApplyBotTeam();
        }

        private void StartWarmup()
        {
            // Start of a ready phase (the no-show timer of a loaded match counts from here).
            MarkReadyPhaseStarted();
            unreadyPlayerMessageTimer?.Kill();
            unreadyPlayerMessageTimer = null;
            //unreadyPlayerMessageTimer ??= AddTimer(chatTimerDelay, SendUnreadyPlayersMessage, TimerFlags.REPEAT);

            // Ready-status HTML panel: re-send every 1s (each print lasts ~2s, so it stays
            // solid with overlap) and drive the optional blink on the NOT-READY line.
            readyStatusHintTimer?.Kill();
            readyStatusHintTimer = AddTimer(1.0f, SendReadyStatusHintMessage, TimerFlags.REPEAT);

            isWarmup = true;
            _readyStatusDirty = true; // Force recompute on warmup start
            ExecWarmupCfg();

            // mp_warmup_pausetimer 1 set inside the cfg exec loses a cross-frame race:
            // mp_warmup_start defers the warmup (re)init to a later frame, which resets
            // pausetimer back to 0 after the exec's line already ran. Re-assert it from
            // code once that has settled so the HUD shows plain "WARMUP" (no countdown).
            AddTimer(3.0f, () =>
            {
                if (isWarmup)
                    Server.ExecuteCommand("mp_warmup_online_enabled 1;mp_warmup_pausetimer 1");
            });

            // Also resets player money to mp_startmoney via InGameMoneyServices.
            AddTimer(
                2.0f,
                () =>
                {
                    try
                    {
                        ClearRoundHistory();

                        int startMoney = ConVar.Find("mp_startmoney")?.GetPrimitiveValue<int>() ?? 800;

                        foreach (var p in Utilities.GetPlayers())
                        {
                            if (p == null || !p.IsValid)
                                continue;
                            try
                            {
                                p.Score = 0;
                                var ats = p.ActionTrackingServices;
                                if (ats != null)
                                {
                                    var ms = ats.MatchStats;
                                    ms.Kills = 0;
                                    ms.Deaths = 0;
                                    ms.Assists = 0;
                                    ms.Damage = 0;
                                    ms.HeadShotKills = 0;
                                    ms.EnemiesFlashed = 0;
                                    ms.EquipmentValue = 0;
                                    ms.MoneySaved = 0;
                                    ms.KillReward = 0;
                                    ms.LiveTime = 0;
                                    ms.Objective = 0;
                                    ms.UtilityDamage = 0;
                                }
                                var moneyServices = p.InGameMoneyServices;
                                if (moneyServices != null)
                                {
                                    moneyServices.Account = startMoney;
                                    moneyServices.StartAccount = startMoney;
                                    moneyServices.TotalCashSpent = 0;
                                    moneyServices.CashSpentThisRound = 0;
                                }
                            }
                            catch (Exception pex)
                            {
                                Log($"[StartWarmup per-player reset] slot={p.Slot} {pex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[StartWarmup reset deferred] {ex.Message}");
                    }
                }
            );

            // The round-history strip at the top of the scoreboard is not cleared by entering warmup:
            // warmup.cfg has no mp_restartgame, so after .endmatch the icons of the played rounds stay
            // on screen next to a 0-0 score. Clear it again once the warmup restart has settled.
            ScheduleRoundHistoryWipe();

            // Ensure spectator limit is properly set
            Server.ExecuteCommand("mp_spectators_max 20");

            ClearClanTags();
        }

        // Wipes the per-round win icons ("skulls") shown above the scoreboard, plus the round counters
        // that decide how many of them the client draws. Only safe outside a running match, so a live
        // round is never renumbered.
        private void ClearRoundHistory()
        {
            if (matchStarted || isMatchLive)
                return;
            try
            {
                var rules = GetGameRules();
                var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                // No game rules during a map change or shutdown: the delayed wipes can land there,
                // and there is nothing to wipe, so skip without logging.
                if (rules == null || proxy == null)
                    return;

                rules.MatchStats_RoundResults.Clear();
                rules.MatchStats_PlayersAlive_CT.Clear();
                rules.MatchStats_PlayersAlive_T.Clear();
                rules.TotalRoundsPlayed = 0;
                rules.ITotalRoundsPlayed = 0;
                rules.RoundsPlayedThisPhase = 0;
                Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
            }
            catch (Exception e)
            {
                Log($"[ClearRoundHistory] {e.Message}");
            }
        }

        // A single ClearRoundHistory() call loses a race against the mode-change cfg: mp_restartgame /
        // mp_warmup_start finish on a later frame and stamp the round counters again, so the icons come
        // back after the wipe. Retry over the window in which those settle. The wipe is idempotent and
        // self-guarded (no-op once a match is live), so extra ticks are free.
        private void ScheduleRoundHistoryWipe()
        {
            ClearRoundHistory();
            AddTimer(2.0f, () => ClearRoundHistory(), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(5.0f, () => ClearRoundHistory(), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(8.0f, () => ClearRoundHistory(), TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void StartKnifeRound()
        {
            // Warmup aim-bots must never survive into the knife round.
            KillWarmupBots();
            // Kills unready players message timer
            if (unreadyPlayerMessageTimer != null)
            {
                unreadyPlayerMessageTimer.Kill();
                unreadyPlayerMessageTimer = null;
            }

            // Kill ready status hint timer
            if (readyStatusHintTimer != null)
            {
                readyStatusHintTimer.Kill();
                readyStatusHintTimer = null;
            }

            // Setting match phases bools
            matchStarted = true;
            isKnifeRound = true;
            readyAvailable = false;
            isDryRun = false;
            isWarmup = false;
            ClearClanTags();

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath);
            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath)))
            {
                ExecModeCfg(knifeCfgPath);
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                Server.ExecuteCommand("mp_ct_default_secondary \"\";mp_free_armor 1;mp_freezetime 10;mp_give_player_c4 0;mp_maxmoney 0;mp_respawn_immunitytime 0;sv_hide_roundtime_until_seconds 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_t_default_secondary \"\";mp_round_restart_delay 3;mp_team_intro_time 0;mp_restartgame 1;mp_warmup_end;");
            }
            ApplyBotTeam();

            PrintLocalizedToAll("matchzy.liveknife2");
            PrintLocalizedToAll("matchzy.liveknife3");
            PrintLocalizedToAll("matchzy.liveknife1");
        }

        private void SendSideSelectionMessage()
        {
            if (!isSideSelectionPhase)
                return;
            PrintLocalizedToAll("matchzy.knife.sidedecisionpending", knifeWinnerName);
        }

        private void StartAfterKnifeWarmup()
        {
            isWarmup = true;
            //DrawSideSelection();
            ExecWarmupCfg();
            knifeWinnerName = knifeWinner == 3 ? reverseTeamSides["CT"].teamName : reverseTeamSides["TERRORIST"].teamName;
            ShowDamageInfo();
            PrintLocalizedToAll("matchzy.knife.sidedecisionpending", knifeWinnerName);
            sideSelectionMessageTimer ??= AddTimer(chatTimerDelay, SendSideSelectionMessage, TimerFlags.REPEAT);

            DrawSideSelection();
        }

        public void RoundStartMessage()
        {
            if (isKnifeRound)
            {
                PrintLocalizedToAll("matchzy.utility.roundknife");
                PrintLocalizedToAll("matchzy.utility.roundknife");
                PrintLocalizedToAll("matchzy.utility.roundknife");
            }
            else if (isMatchLive)
            {
                PrintLocalizedToAll("matchzy.utility.matchlive");
                PrintLocalizedToAll("matchzy.utility.matchlive");
                PrintLocalizedToAll("matchzy.utility.matchlive");
                PrintLocalizedToAll("matchzy.util.tachint");
                PrintLocalizedToAll("matchzy.util.techhint");

                // Re-announcing LIVE (unpause / resume): report the ACTUAL recording state rather than
                // tv_enable, which only says GOTV exists on this server.
                if (isDemoRecording)
                {
                    PrintLocalizedToAll("matchzy.util.cstvrecording");
                }

                // Only show OT notice for match mode (scrim/hill have OT disabled)
                if (!isPlayOutEnabled && !isPlayOutEnabled2)
                {
                    PrintLocalizedToAll("matchzy.util.overtimenotie");
                }
            }
        }

        private void SetLiveFlags()
        {
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
            currentMapFinished = false;
            isPlayOutEnabled = false;
            readyAvailable = false;
            isDryRun = false;

            // Reset advanced stats for new match
            ResetAdvancedStats();

            // Start auto-pause monitoring for competitive matches
            StartAutoPauseCheck();
            Log("[AutoPause] Started auto-pause monitoring for match mode");
        }

        private void SetScrimFlags()
        {
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
            currentMapFinished = false;
            readyAvailable = false;
            isPlayOutEnabled = true;
            isDryRun = false;
            isKnifeRound = false;
            isKnifeRequired = false;

            // Reset advanced stats for new match
            ResetAdvancedStats();

            // Start auto-pause monitoring for scrims
            StartAutoPauseCheck();
            Log("[AutoPause] Started auto-pause monitoring for scrim mode");
        }

        private void SetHillFlags()
        {
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
            currentMapFinished = false;
            readyAvailable = false;
            isPlayOutEnabled = false;
            isPlayOutEnabled2 = true;
            isDryRun = false;
            isKnifeRound = false;
            isKnifeRequired = false;

            // Reset advanced stats for new match
            ResetAdvancedStats();

            // Start auto-pause monitoring for hill mode
            StartAutoPauseCheck();
            Log("[AutoPause] Started auto-pause monitoring for hill mode");
        }

        private void SetupLiveFlagsAndCfg()
        {
            SetLiveFlags();
            KillPhaseTimers();
            ExecLiveCFG();
            SideSelectionTimer?.Kill();
            SideSelectionTimer = null;

            // mp_restartgame (in live cfg) resets the engine's internal team display
            // swap state, so reset our tracking flag to match the engine.
            isConvarMappingSwapped = false;

            // Bumped to 3s + HandlePlayoutConfig so transition from scrim/hill back
            // to .match re-applies clinch=1/overtime convars from live.cfg, forcing
            // client UI refresh of trophy icons.
            AddTimer(
                3,
                () =>
                {
                    ExecuteChangedConvars();
                    HandlePlayoutConfig();
                    // Re-apply team names after live CFG to ensure scoreboard
                    // shows correct names after knife round side selection.
                    SetTeamNames();

                    AddTimer(
                        1,
                        () =>
                        {
                            var clinch = ConVar.Find("mp_match_can_clinch")?.GetPrimitiveValue<bool>();
                            var maxr = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>();
                            var ot = ConVar.Find("mp_overtime_enable")?.GetPrimitiveValue<bool>();
                        }
                    );
                }
            );
        }

        private void SetupScrimFlagsAndCfg()
        {
            SetScrimFlags();
            KillPhaseTimers();
            ExecScrimCFG();

            // mp_restartgame (in scrim cfg) resets the engine's internal team display
            // swap state, so reset our tracking flag to match the engine.
            isConvarMappingSwapped = false;

            // Bumped to 3s: scrim.cfg + ExecScrimCFG both queue mp_restartgame 1 →
            // engine restart fires ~T=1s. Need to set playout convars AFTER restart
            // settles, otherwise our mp_match_can_clinch override loses the race.
            AddTimer(
                3,
                () =>
                {
                    ExecuteChangedConvars();
                    HandlePlayoutConfig();
                    SetTeamNames();

                    AddTimer(
                        1,
                        () =>
                        {
                            var clinch = ConVar.Find("mp_match_can_clinch")?.GetPrimitiveValue<bool>();
                            var maxr = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>();
                            var ot = ConVar.Find("mp_overtime_enable")?.GetPrimitiveValue<bool>();
                        }
                    );
                }
            );
        }

        private void SetupHillFlagsAndCfg()
        {
            SetHillFlags();
            KillPhaseTimers();
            ExecHillCFG();

            // mp_restartgame (in hill cfg) resets the engine's internal team display
            // swap state, so reset our tracking flag to match the engine.
            isConvarMappingSwapped = false;

            // Bumped to 3s: see SetupScrimFlagsAndCfg for race-condition rationale.
            AddTimer(
                3,
                () =>
                {
                    ExecuteChangedConvars();
                    HandlePlayoutConfig();
                    SetTeamNames();
                }
            );
        }

        private void StartLive()
        {
            // Coming out of the knife round's side selection: report the winner and its choice.
            if (isSideSelectionPhase)
                SendKnifeWonEvent();
            // Warmup aim-bots must never survive into a live match.
            KillWarmupBots();
            SetupLiveFlagsAndCfg();
            // Early clinch/overtime apply from live.cfg → forces UI to refresh trophy
            // icons when transitioning from scrim/hill back to match mode.
            Server.NextFrame(() => HandlePlayoutConfig());
            // live.cfg is followed by mp_restartgame (1s with a cfg file, 3s on the inline fallback),
            // and that restart wipes an in-flight tv_record. ArmDemoStart waits for the restart to
            // land, starts on the first round_start after it, and verifies the file afterwards.
            ArmDemoStart();
            // Live flags are set: team tags (matchzy_team_clantag_enabled) or no tags.
            HandleClanTags();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.json";

            PrintLocalizedToAll("matchzy.util.live1");
            PrintLocalizedToAll("matchzy.util.live2");
            PrintLocalizedToAll("matchzy.util.live3");

            PrintLocalizedToAll("matchzy.util.tachint");
            PrintLocalizedToAll("matchzy.util.techhint");

            // Only show OT notice for match mode (scrim/hill have OT disabled)
            if (!isPlayOutEnabled && !isPlayOutEnabled2)
            {
                PrintLocalizedToAll("matchzy.util.overtimenotie");
            }

            // "CSTV Recording..." is no longer printed here. tv_enable being 1 only means GOTV exists,
            // not that the demo survived the cfg's mp_restartgame - the announcement now comes from
            // the demo pipeline once the .dem is confirmed on disk (AnnounceDemoStatus).

            var goingLiveEvent = new GoingLiveEvent { MatchId = liveMatchId, MapNumber = matchConfig.CurrentMapNumber };

            PublishEvent(goingLiveEvent);
        }

        private void StartScrim()
        {
            SetupScrimFlagsAndCfg();
            // Early playout-convar apply so trophy/clinch UI renders correctly on round 1.
            // SetupScrimFlagsAndCfg also re-applies at +3s to win over cfg restart race.
            Server.NextFrame(() =>
            {
                HandlePlayoutConfig();
                ForceScoreboardRefresh();
            });
            // scrim.cfg is followed by mp_restartgame, which clobbers a tv_record fired before it.
            // ArmDemoStart waits for the restart to land, starts on the first round_start after it,
            // and verifies the file afterwards.
            ArmDemoStart();
            // Live flags are set: team tags (matchzy_team_clantag_enabled) or no tags.
            HandleClanTags();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.json";

            PrintLocalizedToAll("matchzy.util.live1");
            PrintLocalizedToAll("matchzy.util.live2");
            PrintLocalizedToAll("matchzy.util.live3");

            PrintLocalizedToAll("matchzy.util.tachint");
            PrintLocalizedToAll("matchzy.util.techhint");

            // "CSTV Recording..." is no longer printed here. tv_enable being 1 only means GOTV exists,
            // not that the demo survived the cfg's mp_restartgame - the announcement now comes from
            // the demo pipeline once the .dem is confirmed on disk (AnnounceDemoStatus).

            var goingLiveEvent = new GoingLiveEvent { MatchId = liveMatchId, MapNumber = matchConfig.CurrentMapNumber };

            PublishEvent(goingLiveEvent);
        }

        private void StartHill()
        {
            SetupHillFlagsAndCfg();
            // Early playout-convar apply so trophy/clinch UI renders correctly on round 1.
            Server.NextFrame(() =>
            {
                HandlePlayoutConfig();
                ForceScoreboardRefresh();
            });
            // hill.cfg is followed by mp_restartgame, which clobbers a tv_record fired before it.
            // ArmDemoStart waits for the restart to land, starts on the first round_start after it,
            // and verifies the file afterwards.
            ArmDemoStart();
            // Live flags are set: team tags (matchzy_team_clantag_enabled) or no tags.
            HandleClanTags();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.json";

            PrintLocalizedToAll("matchzy.util.hilllive1");
            PrintLocalizedToAll("matchzy.util.hilllive2");
            PrintLocalizedToAll("matchzy.util.hilllive3");

            PrintLocalizedToAll("matchzy.util.tachint");
            PrintLocalizedToAll("matchzy.util.techhint");

            // "CSTV Recording..." is no longer printed here. tv_enable being 1 only means GOTV exists,
            // not that the demo survived the cfg's mp_restartgame - the announcement now comes from
            // the demo pipeline once the .dem is confirmed on disk (AnnounceDemoStatus).

            var goingLiveEvent = new GoingLiveEvent { MatchId = liveMatchId, MapNumber = matchConfig.CurrentMapNumber };

            PublishEvent(goingLiveEvent);
        }

        // Bumps m_nRoundEndCount netvar so client re-evaluates scoreboard UI
        // (trophy/clinch icons, per-round dots). Used after HandlePlayoutConfig at
        // match-start so cosmetic UI matches the actual playout convars before round 1.
        private void ForceScoreboardRefresh()
        {
            try
            {
                var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                var rules = GetGameRules();
                if (proxy == null || rules == null)
                    return;
                rules.RoundEndCount = unchecked((byte)(rules.RoundEndCount + 1));
                // m_nRoundEndCount is nested under m_pGameRules pointer in proxy → can't
                // resolve direct offset. Mark parent pointer dirty → full re-replicate.
                Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
            }
            catch (Exception ex)
            {
                Log($"[ForceScoreboardRefresh] {ex.Message}");
            }
        }

        private void KillPhaseTimers()
        {
            unreadyPlayerMessageTimer?.Kill();
            sideSelectionMessageTimer?.Kill();
            pausedStateTimer?.Kill();
            readyStatusHintTimer?.Kill();
            matchEndMapChangeTimer?.Kill();
            restoreUnpauseTimer?.Kill();
            unreadyPlayerMessageTimer = null;
            sideSelectionMessageTimer = null;
            pausedStateTimer = null;
            restoreUnpauseTimer = null;
            readyStatusHintTimer = null;
            matchEndMapChangeTimer = null;
        }

        private (int alivePlayers, int totalHealth) GetAlivePlayers(int team)
        {
            int count = 0;
            int totalHealth = 0;
            foreach (var key in playerData.Keys)
            {
                CCSPlayerController player = playerData[key];
                if (team == 2 && reverseTeamSides["TERRORIST"].coach.Contains(player))
                    continue;
                if (team == 3 && reverseTeamSides["CT"].coach.Contains(player))
                    continue;
                if (!IsPlayerValid(player))
                    continue;
                if (player.TeamNum == team)
                {
                    // PlayerPawn.Value is guaranteed non-null by IsPlayerValid, but add defensive check
                    var health = player.PlayerPawn.Value?.Health ?? 0;
                    if (health > 0)
                        count++;
                    totalHealth += health;
                }
            }
            return (count, totalHealth);
        }

        // The map_result send + database writes of the last map; series_end waits for it so a panel
        // never gets series_end before map_result.
        private Task? lastMapResultTask = null;

        private void ResetMatch(bool warmupCfgRequired = true, string? cancelReason = null)
        {
            try
            {
                seriesEnded = false;
                currentMapFinished = false;
                matchLoadGeneration++;
                // Send match_cancelled whenever a loaded or started match is stopped with a reason:
                // also in warmup, veto, knife and between the maps of a series, where the panel
                // otherwise never learnt that the match it created was gone.
                bool cancellingMatch = cancelReason != null && (matchStarted || isMatchSetup);
                if (cancellingMatch && liveMatchId > 0)
                {
                    (int t1score, int t2score) = GetTeamsScore();
                    string? demoFilename = !string.IsNullOrEmpty(activeDemoFile) ? Path.GetFileName(activeDemoFile) : null;

                    var cancelledEvent = new MatchCancelledEvent
                    {
                        MatchId = liveMatchId,
                        Reason = cancelReason!,
                        DemoFilename = demoFilename,
                        Team1 = new MatchZyTeamWrapper(matchzyTeam1.id, matchzyTeam1.teamName),
                        Team2 = new MatchZyTeamWrapper(matchzyTeam2.id, matchzyTeam2.teamName),
                        Team1Score = t1score,
                        Team2Score = t2score,
                    };

                    // Target taken now: matchConfig is reset (to the server's remote log settings) below.
                    PublishEvent(cancelledEvent);
                }

                // Close the database rows of a match that is being stopped before it could finish.
                // Only SetMatchEndDataAsync writes end_time, and it runs on the natural-end
                // (HandleMatchEnd) and surrender (EndSeries) paths only, so a match stopped from
                // here kept end_time NULL forever and read as if it were still running.
                //
                // Gated on liveMatchId rather than isMatchLive: the row is INSERTed by
                // HandleMatchStart -> InitMatchAsync at ready-up, which is BEFORE StartLive sets
                // isMatchLive, so a stop during the knife round or side selection also has a row to
                // close. Everything is captured first - the state wipe below sets liveMatchId to -1,
                // and none of it may be read inside Task.Run.
                if (cancellingMatch && liveMatchId > 0)
                {
                    long cancelledMatchId = liveMatchId;
                    int cancelledMapNumber = matchConfig.CurrentMapNumber;
                    (int cancelT1score, int cancelT2score) = GetTeamsScore();
                    int cancelledSeriesT1 = matchzyTeam1.seriesScore;
                    int cancelledSeriesT2 = matchzyTeam2.seriesScore;

                    Task.Run(async () =>
                    {
                        await database.SetMatchCancelledAsync(cancelledMatchId, cancelledMapNumber, cancelT1score, cancelT2score, cancelledSeriesT1, cancelledSeriesT2);
                    });
                }

                if (matchStarted && isDemoRecording)
                {
                    Server.ExecuteCommand($"tv_stoprecord");
                    isDemoRecording = false;
                }
                // Reset match data
                matchStarted = false;
                matchStartInProgress = false;
                readyAvailable = true;
                isPaused = false;
                isMatchSetup = false;
                isG5ApiMatch = false;
                isWarmup = true;
                isKnifeRound = false;
                isSideSelectionPhase = false;
                isMatchLive = false;
                isConvarMappingSwapped = false;
                StopAutoPauseCheck();
                liveMatchId = -1;
                isPractice = false;
                isDryRun = false;
                isVeto = false;
                isPreVeto = false;
                isKnifeRequired = knifeEnabledDefault;
                isMatchModeEnabled = true;
                isPlayOutEnabled = false;
                isPlayOutEnabled2 = false;
                lastBackupFileName = "";
                lastMatchZyBackupFileName = "";
                isRoundRestorePending = false;
                playerHasTakenDamage = false;

                foreach (var key in playerReadyStatus.Keys)
                {
                    playerReadyStatus[key] = false;
                }
                pendingRestoreTechPauses = null;
                _readyStatusDirty = true;

                teamReadyOverride = new()
                {
                    { CsTeam.Terrorist, false },
                    { CsTeam.CounterTerrorist, false },
                    { CsTeam.Spectator, false },
                };

                HandleClanTags();
                // Leaving sleep mode. isSleep was never cleared, so after one sleep period the
                // "only from practice/sleep/dryrun" guard of .match/.scrim/.hill always passed and
                // could reset a match that was already loaded.
                // A stopped or cancelled match restores its "cvars" too (only the natural series end
                // did before, so a cancelled match's settings stayed on the server).
                if (resetCvarsOnSeriesEnd && matchConfig.OriginalCvars.Count > 0)
                    ResetChangedConvars();
                // After the restore above, which can put back matchzy_knife_enabled_default.
                isKnifeRequired = knifeEnabledDefault;
                friendlyFireOverride = null;
                CancelVetoStepTimer();
                readyPhaseStartedAt = null;

                isSleep = false;
                overtimePausesUsed.Clear();
                ClearPracticeTimers();
                ResetGGVotes();
                // The previous match's last veto action must not decide who starts the next veto.
                lastVetoTeam = CsTeam.None;

                // Reset the FIELD (this used to declare a local of the same name, so the field kept
                // the previous match's pauseTeam).
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                unpauseData["pauseTeam"] = "";

                stopData["ct"] = false;
                stopData["t"] = false;
                pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
                noFlashList = new();
                lastGrenadesData = new();
                nadeSpecificLastGrenadeData = new();
                UnpauseMatch();
                matchzyTeam1.teamName = "COUNTER-TERRORISTS";
                matchzyTeam2.teamName = "TERRORISTS";
                matchzyTeam1.teamTag = matchzyTeam2.teamTag = "";
                RemoveBotTeamBots();
                matchzyTeam1.teamPlayers = null;
                matchzyTeam2.teamPlayers = null;
                matchzyTeam1.openRoster = matchzyTeam2.openRoster = false;
                matchzyTeam1.botTeam = matchzyTeam2.botTeam = false;
                HashSet<CCSPlayerController> coaches = GetAllCoaches();

                foreach (var coach in coaches)
                {
                    if (!IsPlayerValid(coach))
                        continue;
                    ApplyClanTag(coach, "");
                    SetPlayerVisible(coach);
                }

                matchzyTeam1.coach = new();
                matchzyTeam2.coach = new();
                coachKillTimer?.Kill();
                coachKillTimer = null;

                matchzyTeam1.seriesScore = 0;
                matchzyTeam2.seriesScore = 0;

                if (autoTeamNamesEnabled.Value)
                {
                    Server.ExecuteCommand($"mp_teamname_1 {TeamNameArg(matchzyTeam1.teamName)}");
                    Server.ExecuteCommand($"mp_teamname_2 {TeamNameArg(matchzyTeam2.teamName)}");
                }
                else
                {
                    Server.ExecuteCommand("mp_teamname_1 \"\"; mp_teamname_2 \"\"");
                }

                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;

                // Back to the server's own remote log settings: a URL from the previous match's
                // "cvars" block must not keep receiving the next match's events.
                // Built complete before it is published: an event sent from a thread-pool task in
                // between must never see a config without the remote log URL.
                var resetConfig = new MatchConfig();
                ApplyDefaultRemoteLog(resetConfig);
                matchConfig = resetConfig;

                KillPhaseTimers();
                matchEndMapChangeTimer?.Kill();
                matchEndMapChangeTimer = null;
                UpdatePlayersMap();
                if (warmupCfgRequired)
                {
                    StartWarmup();
                }
                else
                {
                    unreadyPlayerMessageTimer?.Kill();
                    unreadyPlayerMessageTimer = null;
                }
            }
            catch (Exception ex)
            {
                Log($"[ResetMatch - FATAL] [ERROR]: {ex.Message}");
            }
        }

        private void UpdatePlayersMap()
        {
            try
            {
                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");

                // Use HashSet for efficient lookups during cleanup
                var validUserIds = new HashSet<int>();
                int newConnectedPlayers = 0;

                // Update existing players and add new ones
                foreach (var player in playerEntities)
                {
                    // Combined null/validity check
                    if (player?.IsValid != true || player.IsBot || player.IsHLTV)
                        continue;

                    // Early continue if not connected
                    if (player.Connected != PlayerConnectedState.Connected)
                        continue;

                    // Early continue if no UserId
                    if (!player.UserId.HasValue)
                        continue;

                    int userId = player.UserId.Value;

                    // Match/whitelist validation
                    if (isMatchSetup || matchModeOnly)
                    {
                        CsTeam team = GetPlayerTeam(player);
                        if (team == CsTeam.None)
                            continue;
                    }

                    // Track valid user IDs for cleanup phase
                    validUserIds.Add(userId);

                    // Update or add player - dictionary update is idempotent
                    playerData[userId] = player;

                    if (isMatchSetup)
                        AssignRosteredCoach(player);

                    // Only add to ready status if not already present
                    if (!playerReadyStatus.ContainsKey(userId))
                    {
                        playerReadyStatus[userId] = false;
                    }

                    newConnectedPlayers++;
                }

                // Efficient cleanup using HashSet - O(n) instead of O(n²)
                var keysToRemove = playerReadyStatus.Keys.Where(key => !validUserIds.Contains(key)).ToList();

                foreach (var key in keysToRemove)
                {
                    playerReadyStatus.Remove(key);
                    playerData.Remove(key); // Also remove from playerData to keep in sync
                }

                // Update connected players count
                connectedPlayers = newConnectedPlayers;
            }
            catch (Exception e)
            {
                Log($"[UpdatePlayersMap FATAL] An error occurred: {e.Message}");
            }
        }

        public void DetermineKnifeWinner()
        {
            // Knife Round code referred from Get5, thanks to the Get5 team for their amazing job!
            (int tAlive, int tHealth) = GetAlivePlayers(2);
            (int ctAlive, int ctHealth) = GetAlivePlayers(3);
            if (ctAlive > tAlive)
            {
                knifeWinner = 3;
            }
            else if (tAlive > ctAlive)
            {
                knifeWinner = 2;
            }
            else if (ctHealth > tHealth)
            {
                knifeWinner = 3;
            }
            else if (tHealth > ctHealth)
            {
                knifeWinner = 2;
            }
            else
            {
                // Choosing a winner randomly
                Random random = new();
                knifeWinner = random.Next(2, 4);
            }

            // The match team that won, and the side it won on (for knife_won).
            knifeWinnerTeam = knifeWinner == 3 ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
            knifeWinnerSide = knifeWinner == 3 ? "CT" : "TERRORIST";
        }

        private Team? knifeWinnerTeam = null;
        private string knifeWinnerSide = "";

        // Sends knife_won once the knife winner's side choice is applied (StartLive after side selection).
        private void SendKnifeWonEvent()
        {
            if (knifeWinnerTeam == null || !teamSides.TryGetValue(knifeWinnerTeam, out string? side))
                return;
            var knifeWonEvent = new MatchZyKnifeWonEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                Team = knifeWinnerTeam == matchzyTeam1 ? "team1" : "team2",
                Side = side == "CT" ? "ct" : "t",
                Swapped = side != knifeWinnerSide,
            };
            knifeWinnerTeam = null;
            PublishEvent(knifeWonEvent);
        }

        private void HandleKnifeWinner(EventCsWinPanelRound @event)
        {
            DetermineKnifeWinner();
            // Below code is working partially (Winner audio plays correctly for knife winner team, but may display round winner incorrectly)
            // Hence we restart the game with StartAfterKnifeWarmup and allow the winning team to choose side

            @event.FunfactToken = "";

            // Commenting these assignments as they were crashing the server.
            // long empty = 0;
            // @event.FunfactPlayer = null;
            // @event.FunfactData1 = empty;
            // @event.FunfactData2 = empty;
            // @event.FunfactData3 = empty;
            int finalEvent = 10;
            if (knifeWinner == 3)
            {
                finalEvent = 8;
            }
            else if (knifeWinner == 2)
            {
                finalEvent = 9;
            }

            @event.FinalEvent = finalEvent;
        }

        private void HandleMapChangeCommand(CCSPlayerController? player, string mapName)
        {
            if (!IsPlayerAdmin(player, "css_map", "@css/map"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, $"Map cannot be changed once the match is started!");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchstarted"));
                return;
            }

            mapName = (mapName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(mapName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".map <map name/id/ws:name>"));
                return;
            }

            // A purely numeric argument is a Steam Workshop published-file id.
            bool isWorkshopId = IsWorkshopId(mapName);

            // "ws/<id>" is an explicit workshop-id form; strip the prefix and treat as id.
            if (!isWorkshopId && mapName.StartsWith("ws/", StringComparison.OrdinalIgnoreCase))
            {
                string idPart = mapName["ws/".Length..];
                if (!IsWorkshopId(idPart))
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.invalidmap"));
                    return;
                }
                mapName = idPart;
                isWorkshopId = true;
            }

            // "ws:<name>" targets a map from the server's hosted workshop collection via
            // ds_workshop_changelevel. It cannot be validated with IsMapValid (workshop maps
            // are not mounted until loaded), and the name is kept as typed (case may matter).
            bool isWorkshopName = !isWorkshopId && mapName.StartsWith("ws:", StringComparison.OrdinalIgnoreCase);

            string targetMap = isWorkshopName ? mapName["ws:".Length..].Trim() : mapName.ToLower();
            if ((isWorkshopName && string.IsNullOrEmpty(targetMap)) || !IsSafeMapName(targetMap))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.invalidmap"));
                return;
            }

            if (!isWorkshopId && !isWorkshopName)
            {
                // Resolve a NAMED map to one the server actually has BEFORE any teardown:
                // try the name as given, then a "de_" prefix so a bare "mirage" -> "de_mirage"
                // (but "cs_office"/"ar_baggage"/workshop-mounted names validate as-is). Upstream
                // stops the demo + kicks bots BEFORE validating, so a typo leaves the server torn
                // down with no map change and the recording lost - validate first, act second.
                // A known workshop map by its plain name (e.g. the +host_workshop_map boot map) is
                // loaded through its workshop id by BuildMapChangeCommand, never a plain changelevel.
                if (!IsKnownWorkshopMap(targetMap) && !IsStockMap(targetMap))
                {
                    string prefixed = "de_" + targetMap;
                    if (IsStockMap(prefixed) || IsKnownWorkshopMap(prefixed))
                    {
                        targetMap = prefixed;
                    }
                    else
                    {
                        // Validate first, act second: a collection map that is not known yet must be
                        // asked for explicitly as ws:<name>, so a typo never tears the server down.
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.invalidmap"));
                        return;
                    }
                }
            }

            // Debounce: on servers that add '.' as a chat trigger, one ".map" fires BOTH the .map
            // chat dispatch AND css_map, so HandleMapChangeCommand runs twice for one request.
            // Ignore a second request within 2s so the map does not change twice (a double
            // changelevel disconnects players: NETWORK_DISCONNECT_CREATE_SERVER_FAILED). Placed
            // after validation so a typo never blocks an immediate retry.
            //
            // Server.CurrentTime is map-relative (it restarts near 0 on every map load) while this
            // field survives the map change, so after a successful .map the stamp is in the NEW
            // map's future and the delta goes negative. A plain "< 2.0f" test then swallowed every
            // later .map until curtime climbed back past the old stamp. Treat a negative delta as
            // "clock restarted" and let the request through. OnMapStart also clears the stamp.
            float mapChangeDelta = Server.CurrentTime - _lastMapChangeRequestTime;
            if (mapChangeDelta >= 0.0f && mapChangeDelta < 2.0f)
                return;
            _lastMapChangeRequestTime = Server.CurrentTime;

            // Validated named map (or a workshop id) - safe to tear down and change now.
            // Stop demo recording before map change to prevent GOTV crash.
            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
            }
            KickAllBotsProtectCSTV();

            PrintLocalizedToAll("matchzy.utility.changingmap", targetMap);

            // Capture for lambda
            string finalMap = targetMap;
            bool finalIsWorkshop = isWorkshopId;
            bool finalIsWorkshopName = isWorkshopName;
            Server.NextFrame(() =>
            {
                string? command = finalIsWorkshop ? $"host_workshop_map {finalMap}"
                    : finalIsWorkshopName ? $"ds_workshop_changelevel \"{finalMap}\""
                    : BuildMapChangeCommand(finalMap);
                if (command != null)
                    Server.ExecuteCommand(command);
            });
        }

        private void HandleReadyRequiredCommand(CCSPlayerController? player, string commandArg)
        {
            if (!IsPlayerAdmin(player, "css_readyrequired", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int readyRequired) && readyRequired >= 0 && readyRequired <= 32)
                {
                    minimumReadyRequired = readyRequired;
                    string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.minreadyplayers", minimumReadyRequiredFormatted));
                    CheckLiveRequired();
                }
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.rrinvalidvalue"));
                }
            }
            else
            {
                string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.currentreadyrequired", minimumReadyRequiredFormatted));
            }
        }

        private void CheckLiveRequired(bool fromJoinCountdown = false)
        {
            if (!readyAvailable || matchStarted)
                return;
            // Join ready mode starts the match from its own countdown (HandleJoinStartCountdown), not
            // the moment the last player joins or someone types .ready.
            if (IsJoinReadyMode() && !fromJoinCountdown)
                return;
            // The veto picked another map and the changelevel is queued: the match starts there,
            // never on the map that is about to unload.
            if (mapChangePending)
                return;

            // Todo: Implement a same ready system for both pug and match
            int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
            bool liveRequired = false;
            if (isMatchSetup)
            {
                if (IsTeamsReady() && IsSpectatorsReady())
                {
                    liveRequired = true;
                }
            }
            else if (readyPerTeam.Value > 0)
            {
                // Per-team ready-up in a pug: N ready on CT and N ready on T, not N in total.
                liveRequired = IsTeamsReady();
            }
            else if (minimumReadyRequired == 0)
            {
                if (countOfReadyPlayers >= connectedPlayers && connectedPlayers > 0)
                {
                    liveRequired = true;
                }
            }
            else if (countOfReadyPlayers >= minimumReadyRequired)
            {
                liveRequired = true;
            }

            if (liveRequired)
            {
                HandleMatchStart();
            }
        }

        // A throw anywhere in the match start used to leave matchStartInProgress set, and every later
        // ready-up then returned at the re-entry guard: the match could not start until a map change
        // or .stopmatch. Both the synchronous part and the deferred part reset the flag on a throw.
        private void HandleMatchStart()
        {
            try
            {
                HandleMatchStartCore();
            }
            catch (Exception e)
            {
                Log($"[HandleMatchStart FATAL] {e}");
                if (!matchStarted)
                    matchStartInProgress = false;
            }
        }

        private void HandleMatchStartCore()
        {
            // Re-entry guard: knife/live start is deferred (Task.Run → NextFrame), so matchStarted
            // isn't set yet when a second CheckLiveRequired fires. Without this the match starts
            // twice (e.g. "KNIFE!" announced 6x). Restore path re-arms below via early return.
            if (matchStartInProgress || matchStarted)
                return;
            matchStartInProgress = true;

            isPractice = false;
            isDryRun = false;
            if (isRoundRestorePending)
            {
                string restoreFile = pendingRestoreFileName;
                // isRoundRestorePending must still be set during the call: in warmup it is what makes
                // RestoreRoundBackup apply the queued backup instead of queueing it again.
                bool restoreStarted = RestoreRoundBackup(null, restoreFile);
                isRoundRestorePending = false;
                pendingRestoreFileName = "";
                if (restoreStarted)
                {
                    matchStartInProgress = false;
                    return;
                }
                // The queued restore was refused (its round data is gone or incomplete). Start the
                // match normally instead of leaving the server in warmup with everyone ready.
                Log($"[HandleMatchStart] Queued restore of {restoreFile} was refused; starting the match normally.");
            }

            // Auto-naming disabled and both teams still on the scrim defaults (a Get5/JSON match has
            // explicit names by now and never enters the rename branches below): record the side
            // mapping (pauses/stats/side-swap depend on it) but keep the scoreboard vanilla by
            // leaving mp_teamname_1/2 empty and skipping the rename branches.
            bool vanillaTeamNames = !autoTeamNamesEnabled.Value && matchzyTeam1.teamName == "COUNTER-TERRORISTS" && matchzyTeam2.teamName == "TERRORISTS";
            if (vanillaTeamNames)
            {
                teamSides[matchzyTeam1] = "CT";
                reverseTeamSides["CT"] = matchzyTeam1;
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                Server.ExecuteCommand("mp_teamname_1 \"\"; mp_teamname_2 \"\"");
            }

            // Get custom team names from config
            string customCTName = teamNameCt.Value?.Trim() ?? "";
            string customTName = teamNameT.Value?.Trim() ?? "";

            // Handle CT team naming
            if (!vanillaTeamNames && matchzyTeam1.teamName == "COUNTER-TERRORISTS")
            {
                teamSides[matchzyTeam1] = "CT";
                reverseTeamSides["CT"] = matchzyTeam1;

                // Check if custom CT name is provided and not empty
                if (!string.IsNullOrEmpty(customCTName))
                {
                    matchzyTeam1.teamName = customCTName;
                }
                else
                {
                    // Use default behavior - pick from player name
                    foreach (var key in playerData.Keys)
                    {
                        if (playerData[key].TeamNum == 3)
                        {
                            matchzyTeam1.teamName = "team_" + RemoveSpecialCharacters(playerData[key].PlayerName.Replace(" ", "_")).TrimStart('-', '_');
                            break;
                        }
                    }
                }

                // Update coach clan tags
                foreach (var coach in matchzyTeam1.coach)
                {
                    ApplyClanTag(coach, $"[{matchzyTeam1.teamName} COACH]");
                }
            }

            // Handle T team naming
            if (!vanillaTeamNames && matchzyTeam2.teamName == "TERRORISTS")
            {
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["TERRORIST"] = matchzyTeam2;

                // Check if custom T name is provided and not empty
                if (!string.IsNullOrEmpty(customTName))
                {
                    matchzyTeam2.teamName = customTName;
                }
                else
                {
                    // Use default behavior - pick from player name
                    foreach (var key in playerData.Keys)
                    {
                        if (playerData[key].TeamNum == 2)
                        {
                            matchzyTeam2.teamName = "team_" + RemoveSpecialCharacters(playerData[key].PlayerName.Replace(" ", "_")).TrimStart('-', '_');
                            break;
                        }
                    }
                }

                // Update coach clan tags
                foreach (var coach in matchzyTeam2.coach)
                {
                    ApplyClanTag(coach, $"[{matchzyTeam2.teamName} COACH]");
                }
            }

            if (!vanillaTeamNames)
            {
                Server.ExecuteCommand($"mp_teamname_1 {TeamNameArg(reverseTeamSides["CT"].teamName)}");
                Server.ExecuteCommand($"mp_teamname_2 {TeamNameArg(reverseTeamSides["TERRORIST"].teamName)}");
            }

            HandleClanTags();

            string seriesType = "BO" + matchConfig.NumMaps.ToString();
            string mapName = Server.MapName;
            string serverIp = GetServerIpForStats();

            // Capture all state needed for DB init, then run async to avoid blocking game thread
            string team1Name = matchzyTeam1.teamName;
            string team2Name = matchzyTeam2.teamName;
            bool matchSetup = isMatchSetup;
            long currentMatchId = liveMatchId;
            int currentMapNum = matchConfig.CurrentMapNumber;
            // ResetMatch bumps this: a callback from a start that was reset meanwhile must not run,
            // even when a new start has set matchStartInProgress again.
            int startGeneration = matchLoadGeneration;

            Task.Run(async () =>
            {
                // Allocate (or reuse) matchid with exponential backoff so a
                // transient DB blip doesn't leave liveMatchId stuck at -1.
                // 5 attempts: 0ms, 200ms, 500ms, 1s, 2s.
                long newMatchId = -1;
                int[] backoffMs = { 0, 200, 500, 1000, 2000 };
                for (int attempt = 0; attempt < backoffMs.Length; attempt++)
                {
                    if (backoffMs[attempt] > 0)
                        await Task.Delay(backoffMs[attempt]);
                    newMatchId = await database.InitMatchAsync(team1Name, team2Name, "-", matchSetup, currentMatchId, currentMapNum, seriesType, mapName, serverIp);
                    if (newMatchId > 0)
                        break;
                    Log($"[HandleMatchStart] InitMatchAsync attempt {attempt + 1}/{backoffMs.Length} returned {newMatchId}, retrying...");
                }

                // Continue match start on game thread
                Server.NextFrame(() =>
                {
                    try
                    {
                        // The DB retries above can take a few seconds. If the match was reset or
                        // stopped meanwhile (ResetMatch clears matchStartInProgress), do not start a
                        // knife round or go live on a server that has moved on.
                        if (!matchStartInProgress || matchStarted || startGeneration != matchLoadGeneration)
                        {
                            Log("[HandleMatchStart] Match start abandoned: the match was reset while the database was being initialized.");
                            // A row created for this start would otherwise stay open (end_time NULL) forever.
                            if (newMatchId > 0 && newMatchId != currentMatchId)
                            {
                                long orphanId = newMatchId;
                                Task.Run(async () => await database.SetMatchCancelledAsync(orphanId, currentMapNum, 0, 0, 0, 0));
                            }
                            return;
                        }

                        // Keep a matchid we already have (from the match config or the allocation at load)
                        // when the database is unreachable: -1 silenced every event for the whole map.
                        if (newMatchId > 0)
                            liveMatchId = newMatchId;
                        else if (currentMatchId > 0)
                            liveMatchId = currentMatchId;
                        else
                            liveMatchId = -1;

                        if (liveMatchId == -1)
                        {
                            Log("[HandleMatchStart] CRITICAL: Database initialization failed! Match stats will NOT be recorded.");
                        }
                        else
                        {
                            Log($"[HandleMatchStart] Match initialized successfully with matchId: {liveMatchId}");
                        }

                        SetupRoundBackupFile();
                        GetSpawns();

                        if (isPreVeto)
                        {
                            // The veto is not the match start: the players ready up again afterwards and
                            // HandleMatchStart runs a second time. Leaving the re-entry guard set made
                            // that second call return at once, so warmup never ended after a veto.
                            matchStartInProgress = false;
                            CreateVeto();
                        }
                        else if (isKnifeRequired)
                        {
                            StartKnifeRound();
                        }
                        else if (isPlayOutEnabled)
                        {
                            StartScrim();
                        }
                        else if (isPlayOutEnabled2)
                        {
                            StartHill();
                        }
                        else
                        {
                            StartLive();
                        }
                        if (matchStartMessage.Value.Trim() != "" && matchStartMessage.Value.Trim() != "\"\"")
                        {
                            List<string> matchStartMessages = [.. matchStartMessage.Value.Split("$$$")];
                            foreach (string message in matchStartMessages)
                            {
                                PrintToAllChat(GetColorTreatedString(FormatCvarValue(message.Trim())));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log($"[HandleMatchStart FATAL] deferred start: {e}");
                        if (!matchStarted)
                            matchStartInProgress = false;
                    }
                }); // Server.NextFrame
            }); // Task.Run
        }

        public void HandleClanTags(int? forceUpdateSlot = null)
        {
            if (TeamClanTagsActive)
            {
                bool changed = false;
                foreach (var player in Utilities.GetPlayers())
                    changed |= ApplyTeamClanTag(player);
                if (changed)
                    PokeClanNameRefresh();
                return;
            }

            // Clear clan tags if match is live or in practice/dryrun mode
            if (matchStarted || isPractice || isDryRun)
            {
                ClearClanTags();
                return;
            }

            // [READY]/[UNREADY] scoreboard tags disabled by convar - strip any and stop.
            if (!readyClanTagEnabled.Value)
            {
                ClearClanTags();
                return;
            }

            if (!readyAvailable)
                return;

            bool anyChanged = false;

            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot || !player.UserId.HasValue)
                    continue;
                // Coaches keep their [TEAM COACH] tag; they do not ready up.
                if (IsMatchCoach(player))
                    continue;

                // Only T/CT get ready tags. Spectators/unassigned → strip any stale tag.
                if (player.TeamNum != 2 && player.TeamNum != 3)
                {
                    if (!string.IsNullOrEmpty(player.Clan))
                    {
                        ApplyClanTag(player, string.Empty);
                        anyChanged = true;
                    }
                    continue;
                }

                int userId = player.UserId.Value;
                string clanTag = GetPlayerClanTag(player, userId);

                bool isForced = forceUpdateSlot.HasValue && player.Slot == forceUpdateSlot.Value;
                if (isForced || player.Clan != clanTag)
                {
                    ApplyClanTag(player, clanTag);
                    anyChanged = true;
                }
            }

            if (anyChanged)
                PokeClanNameRefresh();
        }

        private void ClearClanTags()
        {
            try
            {
                bool anyChanged = false;
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot || IsMatchCoach(player))
                        continue;

                    if (!string.IsNullOrEmpty(player.Clan))
                    {
                        ApplyClanTag(player, string.Empty);
                        anyChanged = true;
                    }
                }
                if (anyChanged)
                    PokeClanNameRefresh();
            }
            catch (Exception)
            {
                // Silently catch if server isn't ready yet (during plugin load)
                // This is expected and will be retried when players connect
            }
        }

        // Set a player's clan tag and force the client scoreboard to re-read it.
        // m_szClan alone often won't live-refresh a Tab-open scoreboard, so also
        // fire a per-client event that triggers a name/clan redraw. (m_szClanName
        // is not networked, so SetStateChanged on it would only warn and no-op.)
        private void ApplyClanTag(CCSPlayerController player, string tag)
        {
            player.Clan = tag ?? "";
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
            new EventNextlevelChanged(false).FireEventToClient(player);
        }

        // Nudge the game to re-push team/clan names to all clients this tick.
        private void PokeClanNameRefresh()
        {
            var gameRules = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault();
            if (gameRules?.GameRules == null)
                return;

            // Server-side timer field (not networked): writing the value is enough,
            // the engine re-pushes team/clan names once CurrentTime passes it.
            gameRules.GameRules.NextUpdateTeamClanNamesTime = Server.CurrentTime - 0.01f;
        }

        // The team tag (team1.tag / team2.tag from the match config) while live. Roster players get
        // their team's tag; anyone else gets the tag of the team on their side. Coaches keep their
        // coach tag. Returns true when the tag changed.
        private bool ApplyTeamClanTag(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid || player.IsBot || player.IsHLTV || IsMatchCoach(player))
                return false;
            string tag = "";
            if (player.TeamNum == 2 || player.TeamNum == 3)
            {
                if (LookupRosterEntry(matchzyTeam1.teamPlayers, player.SteamID))
                    tag = matchzyTeam1.teamTag;
                else if (LookupRosterEntry(matchzyTeam2.teamPlayers, player.SteamID))
                    tag = matchzyTeam2.teamTag;
                else if (reverseTeamSides.TryGetValue(player.TeamNum == 3 ? "CT" : "TERRORIST", out Team? team))
                    tag = team.teamTag;
            }
            if (player.Clan == tag)
                return false;
            ApplyClanTag(player, tag);
            return true;
        }

        // Spawn / team change while live: the engine can reset m_szClan, and a halftime swap moves
        // non-roster players to the other team's side. Next frame so TeamNum is the new team.
        private bool TeamClanTagsActive =>
            matchStarted && isMatchLive && !isPractice && !isDryRun && teamClanTagEnabled.Value;

        // Also clears a team tag left from before the setting was turned off mid-match.
        private void RefreshTeamClanTag(CCSPlayerController? player)
        {
            if (!isMatchLive || player == null || !player.IsValid || player.IsBot)
                return;
            Server.NextFrame(() =>
            {
                if (!player.IsValid)
                    return;
                if (TeamClanTagsActive)
                {
                    if (ApplyTeamClanTag(player))
                        PokeClanNameRefresh();
                }
                else if (!IsMatchCoach(player) && player.Clan.Length > 0
                    && (player.Clan == matchzyTeam1.teamTag || player.Clan == matchzyTeam2.teamTag))
                {
                    ApplyClanTag(player, "");
                    PokeClanNameRefresh();
                }
            });
        }

        private string GetPlayerClanTag(CCSPlayerController player, int userId)
        {
            // Join ready mode: nobody types .ready, so there is no ready status to show.
            if (IsJoinReadyMode())
                return string.Empty;
            if (readyAvailable && !matchStarted && !isPractice && !isDryRun)
            {
                return playerReadyStatus.TryGetValue(userId, out bool isReady) && isReady ? "[READY]" : "[UNREADY]";
            }

            return string.Empty;
        }

        private void HandleMatchEnd()
        {
            // currentMapFinished: isMatchLive stays true for a while after the map ends, and a second
            // cs_win_panel_match would count the map in the series score twice.
            if (!isMatchLive || currentMapFinished)
                return;
            currentMapFinished = true;

            // Get restart delay from server config (no GOTV broadcast delay needed)
            // With tv_record_immediate 1, demo writes in real-time, no flush delay needed
            int restartDelay = _cvMatchRestartDelay?.GetPrimitiveValue<int>() ?? 25;

            int currentMapNumber = matchConfig.CurrentMapNumber;
            Log($"[HandleMatchEnd] MAP ENDED, isMatchSetup: {isMatchSetup} matchid: {liveMatchId} currentMapNumber: {currentMapNumber} restartDelay: {restartDelay}");

            StopDemoRecording(activeDemoFile, liveMatchId, currentMapNumber);

            string winnerName = GetMatchWinnerName();
            (int t1score, int t2score) = GetTeamsScore();
            int team1SeriesScore = matchzyTeam1.seriesScore;
            int team2SeriesScore = matchzyTeam2.seriesScore;

            string statsPath = Server.GameDirectory + "/csgo/MatchZy_Stats/" + liveMatchId.ToString();

            // Get player stats for the map_result event
            (Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary, List<StatsPlayer> playerStatsListTeam1, List<StatsPlayer> playerStatsListTeam2) = GetPlayerStatsDict();

            // Get demo filename (StopDemoRecording above picked the file that was actually recorded)
            string? demoFilename = !string.IsNullOrEmpty(lastStoppedDemoFile) ? Path.GetFileName(lastStoppedDemoFile) : null;

            // Decide here whether this map ends the series, so the database write below knows
            // whether to close the match row (it used to write the MAP winner and an end_time into
            // matchzy_stats_matches after every map of a series).
            // Count maps played, not maps won: a drawn map is played but adds to no series score,
            // so the old NumMaps - wins formula ran past the end of the map list after a draw.
            int remainingMaps = matchConfig.NumMaps - (currentMapNumber + 1);
            // Clinched once the trailing team can no longer catch up. Comparing a score with
            // NumMaps/2+1 ignored drawn maps: a BO5 at W,W,D,D (2-0, one map left) still played
            // map 5 for nothing.
            bool seriesOver = !isMatchSetup
                || remainingMaps <= 0
                || (matchConfig.SeriesCanClinch && Math.Abs(team1SeriesScore - team2SeriesScore) > remainingMaps);
            string? seriesWinnerName = !isMatchSetup
                ? (winnerName == "Draw" ? null : winnerName)
                : team1SeriesScore > team2SeriesScore ? matchzyTeam1.teamName
                : team2SeriesScore > team1SeriesScore ? matchzyTeam2.teamName
                : null;

            var mapResultEvent = new MapResultEvent
            {
                MatchId = liveMatchId,
                MapNumber = currentMapNumber,
                Winner = BuildWinner(t1score, t2score),
                StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, team1SeriesScore, t1score, 0, 0, playerStatsListTeam1),
                StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, team2SeriesScore, t2score, 0, 0, playerStatsListTeam2),
                DemoFilename = demoFilename,
            };

            // Collect match stats JSON on main thread (accesses native APIs like Server.MapName)
            MatchStatsJson? matchStatsForExport = CollectMatchStatsForExport(demoFilename ?? string.Empty, t1score, t2score, playerStatsListTeam1, playerStatsListTeam2);

            // Capture matchId before async context - liveMatchId may be reset to -1 by ResetMatch
            long matchId = liveMatchId;

            // Queued now, in order with round_end; EndSeries queues series_end after it.
            lastMapResultTask = SendEventAsync(mapResultEvent);
            Task.Run(async () =>
            {
                // Write this round's player stats before exporting the CSV. The round_end task does
                // the same upsert, but it runs independently and could still be in flight here.
                await database.UpdatePlayerStatsAsync(matchId, currentMapNumber, playerStatsDictionary.ToDictionary(kvp => (long)kvp.Key, kvp => kvp.Value));
                await database.SetMatchEndDataAsync(matchId, currentMapNumber, winnerName, t1score, t2score, seriesOver ? (seriesWinnerName ?? "Draw") : null, team1SeriesScore, team2SeriesScore);
                await database.WritePlayerStatsToCsvAsync(statsPath, matchId, currentMapNumber);

                // Write pre-collected HLTV-style JSON stats (file I/O only, no native calls)
                if (matchStatsForExport != null && demoFilename != null)
                {
                    await WriteMatchStatsJsonAsync(matchStatsForExport, demoFilename, statsPath);
                }
            });

            // The series winner (null on a tie). Passing null for every series end printed
            // "have tied the match" and reported winner "none" even for a 2-0.
            if (seriesOver)
            {
                EndSeries(seriesWinnerName, restartDelay, t1score, t2score, writeEndData: false);
                return;
            }

            if (matchzyTeam1.seriesScore > matchzyTeam2.seriesScore)
            {
                PrintLocalizedToAll("matchzy.util.serieswinning", matchzyTeam1.teamName, matchzyTeam1.seriesScore, matchzyTeam2.seriesScore);
            }
            else if (matchzyTeam2.seriesScore > matchzyTeam1.seriesScore)
            {
                PrintLocalizedToAll("matchzy.util.serieswinning", matchzyTeam2.teamName, matchzyTeam2.seriesScore, matchzyTeam1.seriesScore);
            }
            else
            {
                PrintLocalizedToAll("matchzy.util.seriestied", matchzyTeam1.seriesScore, matchzyTeam2.seriesScore);
            }

            matchConfig.CurrentMapNumber += 1;
            string nextMap = matchConfig.Maplist[matchConfig.CurrentMapNumber];

            if (isPaused)
                UnpauseMatch();

            stopData["ct"] = false;
            stopData["t"] = false;

            KillPhaseTimers();

            // Ensure win panel is displayed for the configured duration
            Server.ExecuteCommand($"mp_win_panel_display_time {restartDelay}");

            // Ensure engine won't auto-restart the map during our scheduled change
            var matchEndRestartConVar = _cvMatchEndRestart;
            if (matchEndRestartConVar?.GetPrimitiveValue<bool>() == true)
            {
                Server.ExecuteCommand("mp_match_end_restart 0");
            }

            // For multi-map series, change map after exactly 15 seconds
            float mapChangeDelay = 15.0f;
            matchEndMapChangeTimer = AddTimer(
                mapChangeDelay,
                () =>
                {
                    if (!isMatchSetup)
                        return;
                    // Trigger change immediately once outer delay elapses
                    Server.ExecuteCommand("mp_match_end_restart false");
                    ChangeMap(nextMap, 0.0f);
                    matchStarted = false;
                    matchStartInProgress = false;
                    readyAvailable = true;
                    isPaused = false;
                    isWarmup = true;
                    isKnifeRound = false;
                    isKnifeRequired = true;
                    isSideSelectionPhase = false;
                    isMatchLive = false;
                    isConvarMappingSwapped = false;
                    isPractice = false;
                    isDryRun = false;
                    matchEndMapChangeTimer = null;
                    //StartWarmup();
                    SetMapSides();
                }
            );
        }

        private static readonly System.Text.RegularExpressions.Regex SafeMapNameRegex = new(@"^[A-Za-z0-9_\-\./:]+$");

        /// <summary>
        /// Map names end up in changelevel / host_workshop_map / ds_workshop_changelevel console
        /// commands. Only plain map tokens (letters, digits, _ - . / :) are allowed, so a name from a
        /// match config, a veto, a backup or chat cannot carry a quote or ';' into the console.
        /// </summary>
        public static bool IsSafeMapName(string? mapName)
        {
            return !string.IsNullOrWhiteSpace(mapName) && mapName.Length <= 128 && SafeMapNameRegex.IsMatch(mapName);
        }

        private void ChangeMap(string mapName, float delay)
        {
            if (!IsSafeMapName(mapName))
            {
                Log($"[ChangeMap] Refusing map name with invalid characters: {mapName}");
                return;
            }
            Log($"[ChangeMap] Changing map to {mapName} with delay {delay}");
            AddTimer(
                delay,
                () =>
                {
                    // Ensure demo is stopped before map change to prevent GOTV flush crash
                    if (isDemoRecording)
                    {
                        Server.ExecuteCommand("tv_stoprecord");
                        isDemoRecording = false;
                    }

                    // Prevent engine from racing us with its own map change/restart
                    Server.ExecuteCommand("mp_match_end_changelevel 0");
                    Server.ExecuteCommand("mp_match_end_restart 0");
                    Server.ExecuteCommand("mp_endmatch_votenextmap 0");
                    KickAllBotsProtectCSTV();

                    // Execute actual map change on next frame for engine state safety
                    Server.NextFrame(() =>
                    {
                        // Workshop ids/names -> host_workshop_map / ds_workshop_changelevel, stock maps
                        // -> changelevel (see BuildMapChangeCommand). A plain changelevel of a workshop
                        // map fails to mount and crashes the server.
                        string? command = BuildMapChangeCommand(mapName);
                        if (command != null)
                        {
                            Log($"[ChangeMap] {command}");
                            Server.ExecuteCommand(command);
                        }
                        else
                        {
                            Log($"[ChangeMap] WARNING: Map '{mapName}' is not valid, cannot change!");
                        }
                    });
                }
            );
        }

        /// <summary>
        /// Winner object for map_result / series_end: the team with more (map or series) wins and the
        /// side it is on now, or side "0" / team "none" on a draw. The old inline expression reported
        /// side "2" whenever team2 won (even on CT) and team2 as the winner of a draw.
        /// </summary>
        private Winner BuildWinner(int team1Wins, int team2Wins)
        {
            return BuildWinnerFor(team1Wins > team2Wins ? matchzyTeam1 : team2Wins > team1Wins ? matchzyTeam2 : null);
        }

        private Winner BuildWinnerFor(Team? winTeam)
        {
            if (winTeam == null)
                return new Winner("0", "none");
            string side = reverseTeamSides["CT"] == winTeam ? "3" : "2";
            return new Winner(side, winTeam == matchzyTeam1 ? "team1" : "team2");
        }

        private string GetMatchWinnerName()
        {
            (int t1score, int t2score) = GetTeamsScore();
            if (t1score > t2score)
            {
                matchzyTeam1.seriesScore++;
                return matchzyTeam1.teamName;
            }
            else if (t2score > t1score)
            {
                matchzyTeam2.seriesScore++;
                return matchzyTeam2.teamName;
            }
            else
            {
                return "Draw";
            }
        }

        private (int t1score, int t2score) GetTeamsScore()
        {
            int t1score = 0;
            int t2score = 0;

            // Use cached team entities (refreshed on map start) - avoids per-call entity scan
            // Fall back to full scan if cache is stale
            CCSTeam? team1Entity = null;
            CCSTeam? team2Entity = null;

            string team1Side = teamSides[matchzyTeam1]; // "CT" or "TERRORIST"
            string team2Side = teamSides[matchzyTeam2];

            // Map sides to cached entities
            if (_cachedCtTeam != null && _cachedCtTeam.IsValid && _cachedTTeam != null && _cachedTTeam.IsValid)
            {
                team1Entity = team1Side == "CT" ? _cachedCtTeam : _cachedTTeam;
                team2Entity = team2Side == "CT" ? _cachedCtTeam : _cachedTTeam;
            }
            else
            {
                // Cache miss - refresh and retry
                RefreshTeamEntities();
                if (_cachedCtTeam != null && _cachedTTeam != null)
                {
                    team1Entity = team1Side == "CT" ? _cachedCtTeam : _cachedTTeam;
                    team2Entity = team2Side == "CT" ? _cachedCtTeam : _cachedTTeam;
                }
            }

            if (team1Entity != null)
                t1score = team1Entity.Score;
            if (team2Entity != null)
                t2score = team2Entity.Score;

            return (t1score, t2score);
        }

        private int GetRoundNumer()
        {
            (int t1score, int t2score) = GetTeamsScore();

            return t1score + t2score;
        }

        public void HandlePostRoundStartEvent(EventRoundStart @event)
        {
            if (isDryRun)
                RandomizeSpawns();
            if (!matchStarted)
            {
                // (Re)assert the warmup timer pause here: mp_warmup_start defers the warmup
                // (re)init to a later frame that resets mp_warmup_pausetimer back to 0, and
                // the warmup RoundStart is the first reliable point *after* that settles
                // (can be many seconds in, once players spawn). A fixed delay loses the race;
                // this doesn't. Keeps the HUD on plain "WARMUP" with no running countdown.
                // mp_warmup_pausetimer only holds under online warmup; offline warmup
                // (mp_warmup_online_enabled 0) always counts down. Force it on, then pause.
                // Practice lives in warmup as well (prac.cfg: mp_warmup_start + pausetimer 1) and
                // loses the same race, most visibly after a map (re)load into practice where the
                // first player's connect restarts the warmup period: without this the HUD ran a
                // mp_warmuptime countdown. Dryrun is excluded: it plays real rounds (mp_warmup_end).
                if (isWarmup)
                    Server.ExecuteCommand("mp_warmup_online_enabled 1;mp_warmup_pausetimer 1");
                else if (isPractice && !isDryRun)
                    SettlePracticeWarmupState("round_start");

                return;
            }

            // Per-stage wall clock for the live path. The whole handler runs on the game thread
            // inside the round_start tick, so anything slow here is a server stall right as
            // freezetime ends. Stages over RoundStartStageLogMs are named in the log line; the
            // line itself only appears when the handler as a whole crossed that budget.
            long stageStart = Stopwatch.GetTimestamp();
            long handlerStart = stageStart;
            StringBuilder? slowStages = null;
            void Stage(string name)
            {
                long now = Stopwatch.GetTimestamp();
                double ms = (now - stageStart) * 1000.0 / Stopwatch.Frequency;
                stageStart = now;
                if (ms >= RoundStartStageLogMs)
                    (slowStages ??= new StringBuilder()).Append(name).Append('=').Append(ms.ToString("F1")).Append("ms ");
            }

            // Re-apply clinch/overtime convars on round 1 so trophy/clinch UI refreshes
            // immediately rather than waiting for round 2. Runs for ALL match types
            // (scrim/hill/match) - match mode needs it so trophy reappears when
            // transitioning back from scrim/hill where clinch was disabled.
            if (isMatchLive)
            {
                try
                {
                    int roundsPlayed = GetGameRules()?.TotalRoundsPlayed ?? 99;
                    if (roundsPlayed <= 1)
                    {
                        HandlePlayoutConfig();
                    }
                }
                catch (Exception ex)
                {
                    Log($"[HandlePostRoundStartEvent playout-reapply] {ex.Message}");
                }
            }
            Stage("playout");

            playerHasTakenDamage = false;

            // Demo start: the going-live cfgs run mp_restartgame after their exec and that restart
            // clobbers a tv_record issued before it, so the start is deferred to here - the first live
            // round_start once demoStartArmTime has passed. Without the arm-time floor the pending flag
            // was consumed by the mp_warmup_end round_start that fires just BEFORE the restart, which is
            // exactly the case the deferral was meant to avoid. Only fires once (StartDemoRecording
            // clears the flag).
            if (demoStartPending && isMatchLive && Server.CurrentTime >= demoStartArmTime)
                StartDemoRecording();
            Stage("demo");

            HandleCoaches();
            Stage("coaches");
            CreateMatchZyRoundDataBackup();
            // The restored round has started, so the restore is done. Clearing the flag only at the
            // next round end (as before) made a restore of a half's last round skip the halftime
            // side swap, which inverted team1/team2 scores and stats for the whole second half.
            isRoundRestoring = false;
            Stage("backup");
            InitPlayerDamageInfo();
            Stage("dmginfo");
            UpdateHostname();
            Stage("hostname");
            // Set team names immediately
            SetTeamNames();
            Stage("teamnames");
            // Also set with a delay to handle engine halftime processing that may override our names
            AddTimer(
                0.5f,
                () =>
                {
                    SetTeamNames();
                }
            );

            // Initialize advanced stats tracking for this round
            OnAdvancedStatsRoundStart();
            ResetLiveRoundKillCounters();
            ClearLiveRoundPlayStart();
            Stage("advstats");

            // ── Live scorebot: round_start event ──
            liveRoundNumber = GetRoundNumer();
            if (roundStartSentByRestore)
                roundStartSentByRestore = false; // already sent for the restored round
            else if (!string.IsNullOrEmpty(matchConfig.RemoteLogURL))
            {
                var roundStartEvent = new RoundStartLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = GetRoundNumer(),
                };
                PublishEvent(roundStartEvent);
            }
            Stage("remotelog");

            double totalMs = (Stopwatch.GetTimestamp() - handlerStart) * 1000.0 / Stopwatch.Frequency;
            if (totalMs >= RoundStartStageLogMs)
                Log($"[round_start perf] {totalMs:F1} ms on the game thread; stages over {RoundStartStageLogMs:F0} ms: {(slowStages?.ToString() ?? "none (cost spread across stages)")}");
        }

        // Budget above which a round_start stage is named in the perf log line. A 64-tick frame is
        // 15.6 ms and the slow-frame profiler flags anything over 31.2 ms, so 5 ms is the point where
        // one stage on its own starts to eat a meaningful slice of the tick.
        private const double RoundStartStageLogMs = 5.0;

        private void HandlePostRoundEndEvent(EventRoundEnd @event)
        {
            try
            {
                if (isMatchLive)
                {
                    coachKillTimer?.Kill();
                    coachKillTimer = null;
                    (int t1score, int t2score) = GetTeamsScore();
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName} [{t1score} - {t2score}] {matchzyTeam2.teamName}");

                    ShowDamageInfo();

                    // Update advanced stats for this round
                    CsTeam winnerTeam = (CsTeam)@event.Winner;
                    OnAdvancedStatsRoundEnd(winnerTeam);

                    (Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary, List<StatsPlayer> playerStatsListTeam1, List<StatsPlayer> playerStatsListTeam2) = GetPlayerStatsDict();

                    int currentMapNumber = matchConfig.CurrentMapNumber;
                    long matchId = liveMatchId;
                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;
                    // winner.team is the team that won THIS round (the side that won it, mapped to
                    // team1/team2 before any halftime swap below). It used to report whichever team
                    // was ahead on score.
                    string roundWinnerTeam = @event.Winner switch
                    {
                        (int)CsTeam.CounterTerrorist => reverseTeamSides["CT"] == matchzyTeam1 ? "team1" : "team2",
                        (int)CsTeam.Terrorist => reverseTeamSides["TERRORIST"] == matchzyTeam1 ? "team1" : "team2",
                        _ => "none",
                    };
                    Winner winner = new(@event.Winner.ToString(), roundWinnerTeam);

                    var roundEndEvent = new MatchZyRoundEndedEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        // Get5: the rounds played when this round started (the score already counts it).
                        RoundNumber = UseGet5Events ? liveRoundNumber : GetRoundNumer(),
                        Reason = @event.Reason,
                        RoundTime = LiveRoundTimeMs(),
                        Winner = winner,
                        StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, matchzyTeam1.seriesScore, t1score, 0, 0, playerStatsListTeam1),
                        StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, matchzyTeam2.seriesScore, t2score, 0, 0, playerStatsListTeam2),
                    };

                    // Queued now, on the game thread, so it is sent in order with the other events.
                    PublishEvent(roundEndEvent);
                    Task.Run(async () =>
                    {
                        var playerStatsDictInt = playerStatsDictionary.ToDictionary(kvp => (long)kvp.Key, kvp => kvp.Value);
                        await database.UpdatePlayerStatsAsync(matchId, currentMapNumber, playerStatsDictInt);
                    });

                    string round = GetRoundNumer().ToString("D2");
                    lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt";
                    lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    Log($"[HandlePostRoundEndEvent] Setting lastBackupFileName to {lastBackupFileName} and lastMatchZyBackupFileName to {lastMatchZyBackupFileName}");

                    // A new overtime period starts next: every team gets its overtime pause budget.
                    if (!isRoundRestoring && IsOvertimeStartingNext(t1score, t2score))
                        StartOvertimePeriod();

                    // One of the team did not use .stop command hence display the proper message after the round has ended.
                    if (stopData["ct"] && !stopData["t"])
                    {
                        PrintLocalizedToAll("matchzy.util.restorecancelled", reverseTeamSides["CT"].teamName);
                    }
                    else if (!stopData["ct"] && stopData["t"])
                    {
                        PrintLocalizedToAll("matchzy.util.restorecancelled", reverseTeamSides["TERRORIST"].teamName);
                    }

                    // Invalidate .stop requests after a round is completed.
                    stopData["ct"] = false;
                    stopData["t"] = false;

                    bool swapRequired = IsTeamSwapRequired();

                    // If isRoundRestoring is true, sides will be swapped from round restore if required!
                    if (swapRequired && !isRoundRestoring)
                    {
                        SwapSidesInTeamData(false);
                    }

                    isRoundRestoring = false;
                }
            }
            catch (Exception e)
            {
                Log($"[HandlePostRoundEndEvent FATAL] An error occurred: {e.Message}");
            }
        }

        // .pause uses per team in the current overtime period (matchzy_overtime_pauses_per_team).
        private readonly Dictionary<Team, int> overtimePausesUsed = new();
        // A new overtime period starts: reset the .pause budget and tell the players. Tactical
        // timeouts in overtime are the engine's (mp_team_timeout_ot_add_once / _ot_add_each /
        // _ot_max in live.cfg).
        private void StartOvertimePeriod()
        {
            overtimePausesUsed.Clear();
            int pauses = overtimePausesPerTeam.Value;
            if (pauses > 0)
                PrintLocalizedToAll("matchzy.util.overtimenextpauses", pauses);
            else
                PrintLocalizedToAll("matchzy.util.overtimenext");
        }

        private bool IsInOvertime()
        {
            if (!isMatchLive || ConVar.Find("mp_overtime_enable")?.GetPrimitiveValue<bool>() != true)
                return false;
            int maxRounds = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 0;
            (int t1, int t2) = GetTeamsScore();
            return maxRounds > 0 && t1 + t2 >= maxRounds;
        }

        /// <summary>
        /// True right after the round that ends regulation, or an overtime period, level: the next
        /// round starts (another) overtime.
        /// </summary>
        private bool IsOvertimeStartingNext(int t1score, int t2score)
        {
            if (t1score != t2score)
                return false;
            if (ConVar.Find("mp_overtime_enable")?.GetPrimitiveValue<bool>() != true)
                return false;
            int maxRounds = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 0;
            int otMaxRounds = ConVar.Find("mp_overtime_maxrounds")?.GetPrimitiveValue<int>() ?? 0;
            int played = t1score + t2score;
            if (maxRounds <= 0 || played < maxRounds)
                return false;
            if (played == maxRounds)
                return true;
            return otMaxRounds > 0 && (played - maxRounds) % otMaxRounds == 0;
        }

        public bool IsTeamSwapRequired()
        {
            // Handling OTs and side swaps (Referred from Get5)
            var gameRules = GetGameRules();
            if (gameRules == null)
                return false;
            int roundsPlayed = gameRules.TotalRoundsPlayed;

            int roundsPerHalf = ConVar.Find("mp_maxrounds")!.GetPrimitiveValue<int>() / 2;
            int roundsPerOTHalf = ConVar.Find("mp_overtime_maxrounds")!.GetPrimitiveValue<int>() / 2;

            bool halftimeEnabled = ConVar.Find("mp_halftime")!.GetPrimitiveValue<bool>();

            if (halftimeEnabled)
            {
                if (roundsPlayed == roundsPerHalf)
                {
                    return true;
                }
                // Now in OT.
                if (roundsPlayed >= 2 * roundsPerHalf)
                {
                    int otround = roundsPlayed - 2 * roundsPerHalf; // round 33 -> round 3, etc.
                    // Do side swaps at OT halves (rounds 3, 9, ...)
                    if ((otround + roundsPerOTHalf) % (2 * roundsPerOTHalf) == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void PauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            // Check if already paused during match or knife round
            if ((isMatchLive || isKnifeRound) && isPaused)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.paused"));
                return;
            }

            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.duringhalftime"));
                return;
            }

            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchended"));
                return;
            }

            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.tacticaltimeout"));
                return;
            }

            // (A regular pause is governed by matchzy_allow_pause, checked in OnPauseCommand. This
            // used to also require matchzy_enable_tech_pause, so disabling tech pauses disabled
            // .pause as well.)

            // Allow pausing during match or knife round
            if ((isMatchLive || isKnifeRound) && !isPaused)
            {
                string pauseTeamName = "Admin";
                unpauseData["pauseTeam"] = "Admin";
                if (player?.TeamNum == 2)
                {
                    pauseTeamName = reverseTeamSides["TERRORIST"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["TERRORIST"].teamName;
                }
                else if (player?.TeamNum == 3)
                {
                    pauseTeamName = reverseTeamSides["CT"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["CT"].teamName;
                }
                else
                {
                    return;
                }

                // matchzy_overtime_pauses_per_team: limited .pause uses per team in each overtime period.
                Team pausingTeam = player.TeamNum == 2 ? reverseTeamSides["TERRORIST"] : reverseTeamSides["CT"];
                int otPauseLimit = overtimePausesPerTeam.Value;
                bool inOvertime = otPauseLimit > 0 && IsInOvertime();
                if (inOvertime && overtimePausesUsed.GetValueOrDefault(pausingTeam) >= otPauseLimit)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.util.otpausesused", otPauseLimit));
                    return;
                }
                if (inOvertime)
                    overtimePausesUsed[pausingTeam] = overtimePausesUsed.GetValueOrDefault(pausingTeam) + 1;

                PrintLocalizedToAll("matchzy.pause.pausedthematch", pauseTeamName);
                SetMatchPausedFlags("pause");
            }
        }

        private void ForcePauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (!matchStarted)
                return;
            if (!IsPlayerAdmin(player, "css_forcepause", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (isMatchLive && isPaused)
            {
                // ReplyToUserCommand(player, "Match is already paused!");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.paused"));
                return;
            }

            if (IsHalfTimePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command during halftime.");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.duringhalftime"));
                return;
            }

            if (IsPostGamePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchended"));
                return;
            }

            if (IsTacticalTimeoutActive())
            {
                // ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.tacticaltimeout"));
                return;
            }

            unpauseData["pauseTeam"] = "Admin";
            PrintLocalizedToAll("matchzy.pause.adminpausedthematch");
            // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has paused the match.");
            if (player == null)
            {
                Server.PrintToConsole($"[MatchZy] {Localizer["matchzy.pause.adminpausedthematch"]}");
            }

            SetMatchPausedFlags("admin");
        }

        public void ForceUnpauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_forceunpause", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!isPaused)
                return;

            if (isKnifeRound || isMatchLive)
                // Handle force unpause for both technical and regular pauses
                PrintLocalizedToAll("matchzy.pause.adminunpausedthematch");
            // An admin lifting an auto-pause means playing on short-handed (see AcceptShortHandedAfterAutoPause).
            AcceptShortHandedAfterAutoPause();
            Server.ExecuteCommand("mp_unpause_match");
            CancelTechPauseTimer();
            isPaused = false;

            // Reset all pause-related data
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            unpauseData["pauseType"] = "";
            unpauseData["pauseTeam"] = "";

            if (pausedStateTimer != null)
            {
                pausedStateTimer.Kill();
                pausedStateTimer = null;
            }

            // Send webhook event for live scorebot
            if (isMatchLive || isKnifeRound)
            {
                var unpauseEvent = new MatchUnpausedLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = GetRoundNumer(),
                };

                PublishEvent(unpauseEvent);
            }
        }

        private void UnpauseMatch()
        {
            Server.ExecuteCommand("mp_unpause_match;");
            CancelTechPauseTimer();
            isPaused = false;
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            if (!isPaused && pausedStateTimer != null)
            {
                pausedStateTimer.Kill();
                pausedStateTimer = null;
            }

            // Send webhook event for live scorebot
            if (isMatchLive || isKnifeRound)
            {
                var unpauseEvent = new MatchUnpausedLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = GetRoundNumer(),
                };

                PublishEvent(unpauseEvent);
            }
        }

        private void SetMatchPausedFlags(string pauseType = "tech")
        {
            coachKillTimer?.Kill();
            coachKillTimer = null;
            // A regular or admin pause replaces any tech pause window (its timer must not end this one).
            if (pauseType != "tech")
                CancelTechPauseTimer();

            Server.ExecuteCommand("mp_pause_match;");
            isPaused = true;

            // Send webhook event for live scorebot
            string teamName = (string)(unpauseData["pauseTeam"] ?? "");
            int? maxDur = pauseType == "tech" && !Get5TechPauseMode ? techPauseDuration.Value : (int?)null;

            var pauseEvent = new MatchPausedLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                PauseType = pauseType,
                TeamName = string.IsNullOrEmpty(teamName) ? null : teamName,
                MaxDuration = maxDur,
                RoundNumber = GetRoundNumer(),
            };

            PublishEvent(pauseEvent);
        }

        private void SetTechMatchPausedFlags()
        {
            coachKillTimer?.Kill();
            coachKillTimer = null;

            Server.ExecuteCommand("mp_pause_match;");
            isPaused = true;

            // Send webhook event for live scorebot
            string teamName = (string)(unpauseData["pauseTeam"] ?? "");

            var pauseEvent = new MatchPausedLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                PauseType = "tech",
                TeamName = string.IsNullOrEmpty(teamName) ? null : teamName,
                MaxDuration = Get5TechPauseMode ? null : techPauseDuration.Value,
                RoundNumber = GetRoundNumer(),
            };

            PublishEvent(pauseEvent);
        }

        private void StartHillMode()
        {
            if (matchStarted || (!isPractice && !isSleep && !isDryRun))
                return;

            // Explicitly set isDryRun to false to prevent RandomizeSpawns from being called
            isDryRun = false;

            ResetAllPlayerPracticeSettings(enteringPractice: false);
            CleanupAllCollisionTimers();
            ExecUnpracCommands();
            ResetMatch();
            RemoveSpawnBeams();
            isPlayOutEnabled = true;
            isKnifeRound = false;
            isKnifeRequired = false;
            ScheduleRoundHistoryWipe();
        }

        private void StartScrimMode()
        {
            if (matchStarted || (!isPractice && !isSleep && !isDryRun))
                return;

            // Explicitly set isDryRun to false to prevent RandomizeSpawns from being called
            isDryRun = false;

            ResetAllPlayerPracticeSettings(enteringPractice: false);
            CleanupAllCollisionTimers();
            ExecUnpracCommands();
            ResetMatch();
            RemoveSpawnBeams();
            isPlayOutEnabled = true;
            isKnifeRound = false;
            isKnifeRequired = false;
            ScheduleRoundHistoryWipe();
        }

        private void StartMatchMode()
        {
            if (matchStarted || (!isSleep && !isDryRun && !isPractice))
                return;

            // Explicitly set isDryRun to false to prevent RandomizeSpawns from being called
            isDryRun = false;

            ResetAllPlayerPracticeSettings(enteringPractice: false);
            CleanupAllCollisionTimers();
            ExecUnpracCommands();
            ResetMatch();
            RemoveSpawnBeams();
            isMatchModeEnabled = true;
            isPractice = false; // Set it here to be safe
            ScheduleRoundHistoryWipe();
        }

        private void ExecHillCFG()
        {
            int gameMode = GetGameMode();

            // Backup for symlink/current system
            var cfgPath = hillCfgPath;
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", hillCfgPath);

            if (gameMode == 2)
            {
                absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", hillCfgPath);
                cfgPath = hillCfgPath;
            }

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath))
            {
                // ExecModeCfg applies the match config cvars after the mode cfg and before the
                // restart, so round 1 already uses them (start money, freeze time, ...).
                ExecModeCfg(cfgPath);
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                if (gameMode == 2)
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;mp_weapons_allow_typecount 5;sv_hide_roundtime_until_seconds 0;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 16000;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 10;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 8000;");
                    Server.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;mp_weapons_allow_typecount 5;sv_hide_roundtime_until_seconds 0;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 16000;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
            // .friendlyfire / .ff chosen before the match, on top of the cfg and the match config.
            ApplyFriendlyFireOverride();
        }

        private void ExecScrimCFG()
        {
            int gameMode = GetGameMode();

            // Backup
            var cfgPath = scrimCfgPath;
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", scrimCfgPath);

            if (gameMode == 2)
            {
                absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", scrimCfgPath);
                cfgPath = scrimCfgPath;
            }

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath))
            {
                // ExecModeCfg applies the match config cvars after the mode cfg and before the
                // restart, so round 1 already uses them (start money, freeze time, ...).
                ExecModeCfg(cfgPath);
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                if (gameMode == 2)
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;mp_weapons_allow_typecount 5;sv_hide_roundtime_until_seconds 0;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 10;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 8000;");
                    Server.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;mp_weapons_allow_typecount 5;sv_hide_roundtime_until_seconds 0;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
            // .friendlyfire / .ff chosen before the match, on top of the cfg and the match config.
            ApplyFriendlyFireOverride();
        }

        private void ExecLiveCFG()
        {
            int gameMode = GetGameMode();

            // Backup
            var cfgPath = liveCfgPath;
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", liveCfgPath);

            if (gameMode == 2)
            {
                absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", liveWingmanCfgPath);
                cfgPath = liveWingmanCfgPath;
            }

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath))
            {
                // ExecModeCfg applies the match config cvars after the mode cfg and before the
                // restart, so round 1 already uses them (start money, freeze time, ...).
                ExecModeCfg(cfgPath);
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                if (gameMode == 2)
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;mp_weapons_allow_typecount 5;sv_hide_roundtime_until_seconds 0;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 10;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 8000;");
                    Server.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;mp_weapons_allow_typecount 5;sv_hide_roundtime_until_seconds 0;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
            // .friendlyfire / .ff chosen before the match, on top of the cfg and the match config.
            ApplyFriendlyFireOverride();
            // live.cfg sets bot_quota 0; put a bot team back.
            ApplyBotTeam();
        }

        private void SendPlayerNotAdminMessage(CCSPlayerController? player)
        {
            // ReplyToUserCommand(player, "You do not have permission to use this command!");
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.dontpermission"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.util.notadmin"));
        }

        private string GetColorTreatedString(string message)
        {
            // Adding extra space before args if message starts with a color name
            // This is because colors cannot be applied from 1st character, hence we make first character as an empty space
            if (message.StartsWith('{'))
                message = " " + message;

            foreach (var field in typeof(ChatColors).GetFields())
            {
                string pattern = $"{{{field.Name}}}";
                string? replacement = field.GetValue(null)?.ToString();

                if (replacement is null)
                    return message;

                // Create a case-insensitive regular expression pattern for the color name
                string patternIgnoreCase = Regex.Escape(pattern);
                message = Regex.Replace(message, patternIgnoreCase, replacement, RegexOptions.IgnoreCase);
            }

            return message;
        }

        private void SendAvailableCommandsMessage(CCSPlayerController? player)
        {
            if (!IsPlayerValid(player))
                return;

            bool isAdmin = IsPlayerAdmin(player, "css_matchhelp", "@css/map", "@custom/prac");

            // ── PRACTICE MODE ──
            if (isPractice)
            {
                // Chat summary in the player's language; the command names themselves are not translated.
                string L(string key, params object[] a) => Localizer.ForPlayer(player!, key, a);
                player!.PrintToChat($"{chatPrefix} {L("matchzy.help.practice.title")}");
                player.PrintToChat(L("matchzy.help.line", L("matchzy.help.label.spawns"), ".spawn .ctspawn .tspawn .bestspawn .worstspawn"));
                player.PrintToChat(L("matchzy.help.line", L("matchzy.help.label.bots"), ".bot .cbot .boost .nobot .clearbots"));
                player.PrintToChat(L("matchzy.help.line", L("matchzy.help.label.positions"), ".sbp .lbp .listbotpos .delbotpos .showbotpos .botjiggle"));
                player.PrintToChat(L("matchzy.help.line", L("matchzy.help.label.nades"), ".savenade .loadnade .listnades .rethrow .throwindex"));
                player.PrintToChat(L("matchzy.help.line", L("matchzy.help.label.utility"), ".clear .ff .god .traj .impacts .break .cam .timer"));
                player.PrintToChat(L("matchzy.help.line", L("matchzy.help.label.teams"), ".ct .t .spec"));
                if (isAdmin)
                {
                    player.PrintToChat(L("matchzy.help.adminline", L("matchzy.help.label.admin"), ".exitprac .match .scrim .dryrun .fas .grt .rs"));
                }
                player.PrintToChat($" {L(isAdmin ? "matchzy.help.fulllist.admin" : "matchzy.help.fulllist")}");

                // Console output (unchanged - detailed practice docs)
                player.PrintToConsole("=== Practice Mode Command List ===\n");
                player.PrintToConsole("\n【Spawn Point Operations】\n" + ".spawn <number>  Teleport to the specified competitive spawn point of your team\n" + ".ctspawn <number>  Teleport to the specified CT competitive spawn point (alias: .cts)\n" + ".tspawn <number>  Teleport to the specified T competitive spawn point (alias: .ts)\n" + ".bestspawn  Teleport to the nearest team spawn point\n" + ".worstspawn  Teleport to the farthest team spawn point\n" + ".bestctspawn  Teleport to the nearest CT spawn point\n" + ".worstctspawn  Teleport to the farthest CT spawn point\n" + ".besttspawn  Teleport to the nearest T spawn point\n" + ".worsttspawn  Teleport to the farthest T spawn point\n" + ".showspawns  Highlight all competitive spawn points\n" + ".hidespawns  Hide highlighted spawn points\n");
                player.PrintToConsole("\n【Bot Control】\n" + ".bot  Add a bot at the player's current position\n" + ".crouchbot  Add a crouching bot at the player's current position (alias: .cbot)\n" + ".boost  Add a bot at the current position and boost the player on top of it\n" + ".crouchboost  Add a crouching bot and boost the player on top of it\n" + ".nobot  Remove the bot under the crosshair\n" + ".clearbots  Remove all bots\n");
                player.PrintToConsole("\n【Bot Positions】\n" + ".savebotpos <name>  Save your current spot as a named bot placement for this map (alias: .sbp)\n" + ".loadbotpos <name>  Spawn a bot at that saved spot; no name spawns all saved for this map (alias: .lbp)\n" + ".listbotpos  List saved bot placement names on this map (alias: .listbp)\n" + ".delbotpos <name>  Delete a saved bot placement (alias: .dbp)\n" + ".showbotpos  Toggle in-world markers at every saved bot placement (alias: .showbp)\n" + ".botjiggle  Toggle all practice bots strafing side-to-side (matchzy_botjiggle_range tunes width)\n");
                player.PrintToConsole("\n【Teams & Modes】\n" + ".ct, .t, .spec  Switch the player to the requested team\n" + ".fas /.watchme  Force all players into spectator mode except the one issuing the command\n" + ".dryrun  Enable Dryrun Mode (alias: .dry)\n" + ".god  Enable God Mode\n");
                player.PrintToConsole("\n【Grenade Management】\n" + ".savenade <n> <optional description>  Save a grenade crosshair (alias: .sn)\n" + ".loadnade <n>  Load a grenade crosshair (alias: .ln)\n" + ".deletenade <n>  Delete a saved grenade crosshair from file (alias: .dn)\n" + ".importnade <code>  Save a crosshair using a code printed in chat or from savednades.cfg\n" + ".listnades <optional filter>  List all saved crosshairs, filter optional (alias: .lin)\n");
                player.PrintToConsole("\n【Grenade Throwing】\n" + ".rethrow  Re-throw your last thrown grenade (alias: .rt)\n" + ".last  Teleport to where you threw your last grenade\n" + ".back <number>  Teleport to a specific grenade history position\n" + ".delay <delay_in_seconds>  Set delay on last grenade (used with .rethrow or .throwindex)\n" + ".throwindex <index> <optional index> <optional index>  Throw grenade(s) from specific history index(es)\n" + ".lastindex  Print the index of your last thrown grenade\n" + ".rethrowsmoke  Throw your last smoke grenade\n" + ".rethrownade  Throw your last HE grenade\n" + ".rethrowflash  Throw your last flashbang\n" + ".rethrowmolotov  Throw your last molotov/incendiary\n" + ".rethrowdecoy  Throw your last decoy\n");
                player.PrintToConsole("\n【Utilities】\n" + ".clear  Clear all active smokes, molotovs, and incendiaries\n" + ".fastforward  Fast forward server time by 10 seconds (alias: .ff)\n" + ".noflash  Toggle flash immunity (players without noflash still get blinded, alias: .noblind)\n" + ".timer  Start a timer immediately; use .timer again to stop and show duration\n" + ".break  Break all breakable entities (windows, wooden doors, vents, etc.)\n" + ".nobreak  Restore all breakable entities ");
                player.PrintToConsole("\n【Display & Toggles】\n" + ".solid  Toggle mp_solid_teammates (teammate collision) - Current: " + ConVar.Find("mp_solid_teammates")!.GetPrimitiveValue<int>() + "\n" + ".impacts  Toggle sv_showimpacts (show bullet impacts) - Current: " + ConVar.Find("sv_showimpacts")!.GetPrimitiveValue<int>() + "\n" + ".traj  Toggle sv_grenade_trajectory_prac_pipreview (grenade trajectory preview) - Current: " + ConVar.Find("sv_grenade_trajectory_prac_pipreview")!.GetPrimitiveValue<bool>() + "\n");
                return;
            }

            // ── DRY RUN ──
            string H(string key, params object[] a) => Localizer.ForPlayer(player!, key, a);
            if (isDryRun)
            {
                player!.PrintToChat($"{chatPrefix} {H("matchzy.help.dryrun.title")}");
                player.PrintToChat(H("matchzy.help.line", H("matchzy.help.label.exit"), ".exitdry .stopdry .enddry"));
                if (isAdmin)
                {
                    player.PrintToChat(H("matchzy.help.adminline", H("matchzy.help.label.admin"), ".match .prac"));
                }
                return;
            }

            // ── VETO ──
            if (isVeto)
            {
                player!.PrintToChat($"{chatPrefix} {H("matchzy.help.veto.title")}");
                player.PrintToChat(H("matchzy.help.line", H("matchzy.help.label.banpick"), ".ban <map> .pick <map>"));
                player.PrintToChat($" {H("matchzy.help.veto.captains")}");
                return;
            }

            // ── WARMUP (not ready phase) ──
            if (isWarmup && !readyAvailable)
            {
                player!.PrintToChat($"{chatPrefix} {H("matchzy.help.warmup.title")}");
                player.PrintToChat(H("matchzy.help.mode", ".match", H("matchzy.help.mode.match")));
                player.PrintToChat(H("matchzy.help.mode", ".scrim", H("matchzy.help.mode.scrim")));
                player.PrintToChat(H("matchzy.help.mode", ".prac", H("matchzy.help.mode.prac")));
                player.PrintToChat(H("matchzy.help.mode", ".dry", H("matchzy.help.mode.dry")));
                if (isAdmin)
                    player.PrintToChat(H("matchzy.help.line", H("matchzy.help.label.available"), ".start, .knife, .playout, .coach <side>, .endmatch"));
                return;
            }

            // ── MATCH / SCRIM STATUS (ready phase) ──
            // Compact status block (no ready panel here - the ready-up hint is its own HUD panel).
            // Match vs Scrim is distinguished by isMatchModeEnabled; the toggle values (knife / demo /
            // playout) come straight from the live flags, so scrim naturally shows Knife: Disabled,
            // Playout: Enabled.
            if (readyAvailable && !matchStarted)
            {
                string on = H("matchzy.cc.statuson");
                string off = H("matchzy.cc.statusoff");
                bool isScrim = !isMatchModeEnabled;
                string knife = isKnifeRequired ? on : off;
                string demorec = IsGOTVEnabled() ? on : off;
                string playout = isPlayOutEnabled ? on : off;

                player!.PrintToChat($"{chatPrefix} {H(isScrim ? "matchzy.help.title.scrim" : "matchzy.help.title.match")}");
                player.PrintToChat(H("matchzy.help.status", knife, demorec, playout));
                string cmds = isScrim
                    ? ".start, .playout, .coach <side>, .endmatch"
                    : ".start, .knife, .playout, .coach <side>, .endmatch";
                player.PrintToChat(H("matchzy.help.line", H("matchzy.help.label.available"), cmds));
                return;
            }

            // ── KNIFE ROUND - SIDE SELECTION ──
            if (isSideSelectionPhase)
            {
                player!.PrintToChat($"{chatPrefix} {H("matchzy.help.knife.title")}");
                player.PrintToChat(H("matchzy.help.knife.stay"));
                player.PrintToChat(H("matchzy.help.knife.switch"));
                player.PrintToChat(H("matchzy.help.knife.side"));
                return;
            }

            // ── MATCH LIVE - PAUSED ──
            if (matchStarted && isMatchLive && isPaused)
            {
                player!.PrintToChat($"{chatPrefix} {H("matchzy.help.paused.title")}");
                player.PrintToChat(H("matchzy.help.paused.unpause"));
                if (isAdmin)
                {
                    player.PrintToChat(H("matchzy.help.adminline", H("matchzy.help.label.admin"), H("matchzy.help.paused.admin")));
                }
                return;
            }

            // ── MATCH LIVE - PLAYING ──
            if (matchStarted && isMatchLive)
            {
                player!.PrintToChat($"{chatPrefix} {H("matchzy.help.live.title")}");
                player.PrintToChat(H("matchzy.help.line", H("matchzy.help.label.pause"), ".pause .tac .tech"));
                if (isStopCommandAvailable)
                {
                    player.PrintToChat(H("matchzy.help.line", H("matchzy.help.label.round"), H("matchzy.help.live.round")));
                }
                if (isAdmin)
                {
                    player.PrintToChat(H("matchzy.help.adminline", H("matchzy.help.label.admin"), H("matchzy.help.live.admin")));
                }
                return;
            }

            // ── FALLBACK ──
            player!.PrintToChat($"{chatPrefix} {Localizer.ForPlayer(player, "matchzy.util.nocommands")}");
        }

        public void LoadClientNames()
        {
            string namesFileName = "Match_" + liveMatchId.ToString() + ".ini";
            string namesFilePath = Server.GameDirectory + "/csgo/MatchZyPlayerNames/" + namesFileName;
            string? directoryPath = Path.GetDirectoryName(namesFilePath);
            if (directoryPath != null)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\"Names\"");
            sb.AppendLine("{");

            WriteClientNamesInFile(sb, matchzyTeam1.teamPlayers);
            WriteClientNamesInFile(sb, matchzyTeam2.teamPlayers);
            WriteClientNamesInFile(sb, matchConfig.Spectators);

            sb.AppendLine("}");
            File.WriteAllText(namesFilePath, sb.ToString());
            Server.ExecuteCommand($"sv_load_forced_client_names_file MatchZyPlayerNames/" + namesFileName);
        }

        public void WriteClientNamesInFile(StringBuilder sb, JToken? players)
        {
            // Only the object form ({"steamid64": "name"}) carries names. An array roster or
            // "players": "any" has none, and casting its entries to JProperty would throw.
            if (players is not JObject playersObject)
                return;
            foreach (JProperty player in playersObject.Properties())
            {
                string steamId = player.Name;
                // KeyValues: escape backslashes before quotes, or a name ending in \ eats the closing
                // quote and the whole forced-names file is rejected. Keys must be SteamID64s.
                string escapedName = player.Value.ToString().Trim().Replace("\\", "\\\\").Replace("\"", "\\\"");

                if (string.IsNullOrEmpty(escapedName) || !ulong.TryParse(steamId, out _))
                    continue;

                sb.AppendLine($"\t\"{steamId}\"\t\t\"{escapedName}\"");
            }
        }

        // A team name as one console argument: quoted, so a name with spaces is not cut at the first
        // space on the scoreboard, and without '"' or ';', which would end the argument or the command.
        static string TeamNameArg(string? name)
        {
            return "\"" + (name ?? "").Replace("\"", "").Replace(";", "") + "\"";
        }

        // URLs can carry credentials (user:pass@host, API keys or presigned signatures in the query
        // string), so logs and chat replies only show scheme, host, port and path.
        static string RedactUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return "<invalid url>";
            string redacted = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{uri.AbsolutePath}";
            if (!string.IsNullOrEmpty(uri.Query))
                redacted += "?<redacted>";
            return redacted;
        }

        static bool IsValidUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? result))
            {
                return result != null && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
            }

            return false;
        }

        /// <summary>
        /// Reads mp_freezetime as a float, BY ITS ACTUAL CVAR TYPE. mp_freezetime is a float cvar in
        /// CS2, so GetPrimitiveValue&lt;int&gt; reinterprets the raw float bits as an int (garbage).
        /// This switches on cvar.Type and reads the matching primitive, so it works whether Valve ships
        /// it as a float or an int on a given build. Returns the fallback if the cvar is missing/odd.
        /// </summary>
        public float GetFreezeTime(float fallback = 15f)
        {
            try
            {
                ConVar? cvar = ConVar.Find("mp_freezetime");
                if (cvar == null)
                    return fallback;
                float value = cvar.Type switch
                {
                    ConVarType.Float32 => cvar.GetPrimitiveValue<float>(),
                    ConVarType.Float64 => float.TryParse(cvar.StringValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f64) ? f64 : fallback,
                    ConVarType.Int32 => cvar.GetPrimitiveValue<int>(),
                    ConVarType.Int16 => cvar.GetPrimitiveValue<short>(),
                    ConVarType.UInt32 => cvar.GetPrimitiveValue<uint>(),
                    ConVarType.UInt16 => cvar.GetPrimitiveValue<ushort>(),
                    _ => float.TryParse(GetConvarStringValue(cvar), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback,
                };
                return value >= 0f ? value : fallback;
            }
            catch (Exception ex)
            {
                Log($"[GetFreezeTime] {ex.Message}");
                return fallback;
            }
        }

        public string GetConvarStringValue(ConVar? cvar)
        {
            try
            {
                if (cvar == null)
                    return "";
                // Written back later as a console command ("name value"), so the value must be in the
                // form the engine parses: 1/0 for bools (not True/False) and '.' decimals on every
                // server locale.
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string convarValue = cvar.Type switch
                {
                    ConVarType.Bool => cvar.GetPrimitiveValue<bool>() ? "1" : "0",
                    ConVarType.Float32 => cvar.GetPrimitiveValue<float>().ToString(inv),
                    // GetPrimitiveValue only accepts float for Float64 and reads 4 of its 8 bytes; the
                    // native string form is exact and always uses '.'.
                    ConVarType.Float64 => cvar.StringValue,
                    ConVarType.UInt16 => cvar.GetPrimitiveValue<ushort>().ToString(inv),
                    ConVarType.Int16 => cvar.GetPrimitiveValue<short>().ToString(inv),
                    ConVarType.UInt32 => cvar.GetPrimitiveValue<uint>().ToString(inv),
                    ConVarType.Int32 => cvar.GetPrimitiveValue<int>().ToString(inv),
                    ConVarType.Int64 => cvar.GetPrimitiveValue<long>().ToString(inv),
                    ConVarType.UInt64 => cvar.GetPrimitiveValue<ulong>().ToString(inv),
                    ConVarType.String => cvar.StringValue,
                    _ => "",
                };
                return convarValue;
            }
            catch (Exception ex)
            {
                Log($"[GetConvarStringValue - FATAL] Exception occurred: {ex.Message}");
                return "";
            }
        }

        public void SetConvarValue(ConVar? cvar, string value)
        {
            if (cvar == null)
                return;
            Dictionary<ConVarType, Action<string>> conversionMap = new()
            {
                {
                    ConVarType.Bool,
                    // Accept both numeric ("0"/"1") and textual ("true"/"false") forms.
                    // The old expression fell through to Convert.ToBoolean("0"), which throws
                    // "String '0' was not recognized as a valid Boolean".
                    v => cvar.SetValue(int.TryParse(v, out int intValue) ? intValue >= 1 : Convert.ToBoolean(v))
                },
                // Invariant: values are stored with '.' decimals; the server locale may use ','.
                { ConVarType.Float32, v => cvar.SetValue(Convert.ToSingle(v, System.Globalization.CultureInfo.InvariantCulture)) },
                { ConVarType.Float64, v => cvar.SetValue(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture)) },
                { ConVarType.UInt16, v => cvar.SetValue(Convert.ToUInt16(v)) },
                { ConVarType.Int16, v => cvar.SetValue(Convert.ToInt16(v)) },
                { ConVarType.UInt32, v => cvar.SetValue(Convert.ToUInt32(v)) },
                { ConVarType.Int32, v => cvar.SetValue(Convert.ToInt32(v)) },
                { ConVarType.Int64, v => cvar.SetValue(Convert.ToInt64(v)) },
                { ConVarType.UInt64, v => cvar.SetValue(Convert.ToUInt64(v)) },
                { ConVarType.String, v => cvar.SetValue(v) },
            };

            if (conversionMap.TryGetValue(cvar.Type, out var conversion))
            {
                try
                {
                    conversion(value);
                }
                catch (Exception ex)
                {
                    Log($"[SetConvarValue - FATAL] Exception occurred: {ex.Message}");
                }
            }
        }

        // Never settable from a match config, even though they are real convars / MatchZy settings.
        private static readonly HashSet<string> BlockedMatchCvars = new(StringComparer.OrdinalIgnoreCase)
        {
            "rcon_password",
            "matchzy_everyone_is_admin",
        };

        // MatchZy / Get5 settings implemented as console commands that only change a setting. Commands
        // that perform an action (loading a match or backup, adding players, ending the match, ...) are
        // deliberately not listed. PluginSettingAccessors and the FakeConVar settings are added on top.
        private static readonly HashSet<string> MatchConfigSettingCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "matchzy_chat_prefix", "matchzy_admin_chat_prefix",
            "matchzy_remote_log_url", "matchzy_remote_log_header_key", "matchzy_remote_log_header_value",
            "matchzy_remote_log_auth_key", "matchzy_remote_log_auth_value",
            "get5_remote_log_url", "get5_remote_log_header_key", "get5_remote_log_header_value",
            "matchzy_reset_cvars_on_series_end", "matchzy_allow_pause", "matchzy_allow_unpause",
            "matchzy_ct_name", "matchzy_t_name", "matchzy_nade_pose_flicker_free",
        };

        // Settings whose value is used as a file path or file name.
        private static readonly HashSet<string> PathMatchCvars = new(StringComparer.OrdinalIgnoreCase)
        {
            "matchzy_demo_path", "matchzy_demo_name_format",
        };

        private static readonly System.Text.RegularExpressions.Regex MatchCvarNameRegex = new(@"^[A-Za-z0-9_]+$");

        private HashSet<string>? _matchConfigPluginSettings;
        private Dictionary<string, (object FakeConVar, System.Reflection.PropertyInfo Value)>? _fakeConVarsByName;

        // Every FakeConVar field on the plugin, by convar name (found by reflection, so a new
        // FakeConVar setting needs no list update).
        private Dictionary<string, (object FakeConVar, System.Reflection.PropertyInfo Value)> FakeConVarsByName
        {
            get
            {
                if (_fakeConVarsByName != null)
                    return _fakeConVarsByName;
                var map = new Dictionary<string, (object, System.Reflection.PropertyInfo)>(StringComparer.OrdinalIgnoreCase);
                foreach (var fieldInfo in GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    if (!fieldInfo.FieldType.IsGenericType || fieldInfo.FieldType.GetGenericTypeDefinition() != typeof(FakeConVar<>))
                        continue;
                    object? fakeConVar = fieldInfo.GetValue(this);
                    var valueProperty = fieldInfo.FieldType.GetProperty("Value");
                    if (fakeConVar != null && valueProperty != null && fieldInfo.FieldType.GetProperty("Name")?.GetValue(fakeConVar) is string fakeName)
                        map[fakeName] = (fakeConVar, valueProperty);
                }
                _fakeConVarsByName = map;
                return map;
            }
        }

        /// <summary>
        /// Sets a FakeConVar setting directly. Running it as a console command does not work for a
        /// match config value: the command is sent as name "value", and FakeConVar only strips the
        /// quotes for string settings, so every bool/int/float setting failed to parse. Numbers are
        /// read with a '.' decimal separator on every server locale.
        /// Returns false when the name is not a FakeConVar; logs and returns true when the value is
        /// invalid (it was handled, just not applied).
        /// </summary>
        private bool TrySetFakeConVar(string name, string value)
        {
            if (!FakeConVarsByName.TryGetValue(name, out var entry))
                return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Type type = entry.Value.PropertyType;
            string v = value.Trim().Trim('"').Trim();
            object? parsed = null;
            if (type == typeof(string))
                parsed = v;
            else if (type == typeof(bool))
            {
                if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)) parsed = true;
                else if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)) parsed = false;
            }
            else if (type == typeof(int) && int.TryParse(v, System.Globalization.NumberStyles.Integer, inv, out int i))
                parsed = i;
            else if (type == typeof(float) && float.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out float f) && float.IsFinite(f))
                parsed = f;
            else if (type == typeof(double) && double.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out double d) && double.IsFinite(d))
                parsed = d;
            else
            {
                try { parsed = System.ComponentModel.TypeDescriptor.GetConverter(type).ConvertFromInvariantString(v); }
                catch { parsed = null; }
            }

            if (parsed == null)
            {
                Log($"[MatchConfigCvars] '{v}' is not a valid value for {name} ({type.Name}); ignored.");
                return true;
            }
            try
            {
                entry.Value.SetValue(entry.FakeConVar, parsed);
            }
            catch (Exception ex)
            {
                Log($"[MatchConfigCvars] Could not set {name}: {ex.InnerException?.Message ?? ex.Message}");
            }
            return true;
        }

        // Current value of a FakeConVar setting in the form its console command accepts, or null.
        private string? GetFakeConVarValue(string name)
        {
            if (!FakeConVarsByName.TryGetValue(name, out var entry))
                return null;
            object? value = entry.Value.GetValue(entry.FakeConVar);
            return value is bool flag ? (flag ? "true" : "false") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Every matchzy_/get5_ name a match config may set: the setting commands above, the settings
        /// in PluginSettingAccessors and every FakeConVar field on the plugin (found by reflection, so a
        /// new FakeConVar setting is allowed without touching this list).
        /// </summary>
        private HashSet<string> MatchConfigPluginSettings
        {
            get
            {
                if (_matchConfigPluginSettings != null)
                    return _matchConfigPluginSettings;
                var names = new HashSet<string>(MatchConfigSettingCommands, StringComparer.OrdinalIgnoreCase);
                names.UnionWith(PluginSettingAccessors.Keys);
                names.UnionWith(FakeConVarsByName.Keys);
                names.ExceptWith(BlockedMatchCvars);
                _matchConfigPluginSettings = names;
                return names;
            }
        }

        private static bool IsSafeRelativePath(string value)
        {
            if (value == "")
                return true;
            if (value.StartsWith('/') || value.StartsWith('\\') || value.Contains(':'))
                return false;
            return !value.Split('/', '\\').Any(part => part == "..");
        }

        /// <summary>
        /// Guards the match config "cvars" block (and backups that carry it), which is turned into
        /// console commands. Match configs can come from a URL, so only real engine convars and the
        /// MatchZy/Get5 settings in MatchConfigPluginSettings are accepted, with a value that has no
        /// quotes, ';' or line breaks. matchzy_/get5_ action commands (matchzy_loadmatch_url,
        /// get5_endmatch, matchzy_loadbackup, ...) and matchzy_everyone_is_admin are refused.
        /// </summary>
        private bool IsAllowedMatchCvar(string name, string value, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(name) || !MatchCvarNameRegex.IsMatch(name))
            {
                reason = "invalid name";
                return false;
            }
            if (BlockedMatchCvars.Contains(name))
            {
                reason = "not allowed from a match config";
                return false;
            }
            if (value.IndexOfAny(new[] { '"', ';', '\n', '\r' }) >= 0)
            {
                reason = "value contains a quote, ';' or a line break";
                return false;
            }
            if (PathMatchCvars.Contains(name) && !IsSafeRelativePath(value))
            {
                reason = "value must be a relative path without '..'";
                return false;
            }
            bool pluginName = name.StartsWith("matchzy_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("get5_", StringComparison.OrdinalIgnoreCase);
            if (pluginName)
            {
                if (MatchConfigPluginSettings.Contains(name))
                    return true;
                reason = "not a MatchZy setting (action commands are not allowed)";
                return false;
            }
            if (ConVar.Find(name) == null)
            {
                reason = "not a convar";
                return false;
            }
            return true;
        }

        public void ExecuteChangedConvars()
        {
            foreach (string key in matchConfig.ChangedCvars.Keys)
            {
                string value = matchConfig.ChangedCvars[key];
                if (!IsAllowedMatchCvar(key, value, out string reason))
                {
                    Log($"[ExecuteChangedConvars] Skipping '{key}': {reason}");
                    continue;
                }
                if (TrySetFakeConVar(key, value))
                    continue;
                if (RemoteLogCvars.Contains(key))
                {
                    // This match only; the server's own setting is untouched.
                    ApplyMatchRemoteLogCvar(key, value);
                    continue;
                }
                Server.ExecuteCommand($"{key} \"{value}\"");
            }
        }

        private Dictionary<string, (Func<string> Get, Action<string> Set)>? _pluginSettingAccessors;

        /// <summary>
        /// MatchZy settings implemented as console commands (no convar behind them), readable and
        /// restorable directly. Used to put back values a match config's "cvars" block changed.
        /// The remote log settings are deliberately not here: series_end is sent after the series
        /// ends and must still reach the match's URL.
        /// </summary>
        private Dictionary<string, (Func<string> Get, Action<string> Set)> PluginSettingAccessors
        {
            get
            {
                if (_pluginSettingAccessors != null)
                    return _pluginSettingAccessors;

                (Func<string>, Action<string>) Bool(Func<bool> get, Action<bool> set) =>
                    (() => get() ? "true" : "false", v => set(ParseCvarBool(v, get())));
                (Func<string>, Action<string>) Int(Func<int> get, Action<int> set) =>
                    (() => get().ToString(), v => { if (int.TryParse(v.Trim('"'), out int n)) set(n); });
                (Func<string>, Action<string>) Str(Func<string> get, Action<string> set) =>
                    (get, v => set(v.Trim('"')));

                var map = new Dictionary<string, (Func<string>, Action<string>)>(StringComparer.OrdinalIgnoreCase)
                {
                    ["matchzy_whitelist_enabled_default"] = Bool(() => isWhitelistRequired, v => isWhitelistRequired = v),
                    ["matchzy_knife_enabled_default"] = Bool(() => knifeEnabledDefault, v => knifeEnabledDefault = v),
                    ["matchzy_playout_enabled_default"] = Bool(() => isPlayOutEnabled, v => isPlayOutEnabled = v),
                    ["matchzy_save_nades_as_global_enabled"] = Bool(() => isSaveNadesAsGlobalEnabled, v => isSaveNadesAsGlobalEnabled = v),
                    ["matchzy_kick_when_no_match_loaded"] = Bool(() => matchModeOnly, v => matchModeOnly = v),
                    ["matchzy_minimum_ready_required"] = Int(() => minimumReadyRequired, v => minimumReadyRequired = v),
                    ["matchzy_demo_path"] = Str(() => demoPath, v => demoPath = v),
                    ["matchzy_demo_name_format"] = Str(() => demoNameFormat, v => demoNameFormat = v),
                    ["matchzy_demo_upload_url"] = Str(() => demoUploadURL, v => demoUploadURL = v),
                    ["matchzy_demo_upload_s3"] = Bool(() => isDemoUploadS3Enabled, v => isDemoUploadS3Enabled = v),
                    ["matchzy_demo_upload_header_key"] = Str(() => demoUploadHeaderKey, v => demoUploadHeaderKey = v),
                    ["matchzy_demo_upload_header_value"] = Str(() => demoUploadHeaderValue, v => demoUploadHeaderValue = v),
                    ["matchzy_stop_command_available"] = Bool(() => isStopCommandAvailable, v => isStopCommandAvailable = v),
                    ["matchzy_use_pause_command_for_tactical_pause"] = Bool(() => isPauseCommandForTactical, v => isPauseCommandForTactical = v),
                    ["matchzy_pause_after_restore"] = Bool(() => pauseAfterRoundRestore, v => pauseAfterRoundRestore = v),
                    ["matchzy_allow_force_ready"] = Bool(() => allowForceReady, v => allowForceReady = v),
                    ["matchzy_chat_messages_timer_delay"] = Int(() => chatTimerDelay, v => chatTimerDelay = v),
                    ["matchzy_max_saved_last_grenades"] = Int(() => maxLastGrenadesSavedLimit, v => maxLastGrenadesSavedLimit = v),
                    ["matchzy_remote_backup_url"] = Str(() => backupUploadURL, v => backupUploadURL = v),
                    ["matchzy_remote_backup_header_key"] = Str(() => backupUploadHeaderKey, v => backupUploadHeaderKey = v),
                    ["matchzy_remote_backup_header_value"] = Str(() => backupUploadHeaderValue, v => backupUploadHeaderValue = v),
                    // Console-command aliases of FakeConVar settings (ReadyTime.cs, TechPauseGet5.cs).
                    ["matchzy_time_to_start"] = Int(() => forfeitReadyTimeout.Value, v => forfeitReadyTimeout.Value = Math.Max(0, v)),
                    ["matchzy_time_to_start_veto"] = Int(() => forfeitVetoReadyTimeout.Value, v => forfeitVetoReadyTimeout.Value = Math.Max(-1, v)),
                    // Restoring only puts the value back; matchzy_tech_pause_mode is restored on its own
                    // (GetCvarValues records it when a match config sets one of these).
                    ["get5_max_tech_pauses"] = Int(() => maxTechPausesAllowed.Value, v => maxTechPausesAllowed.Value = Math.Max(0, v)),
                    ["get5_tech_pause_time"] = Int(() => techPauseDuration.Value, v => techPauseDuration.Value = v),
                    ["get5_allow_technical_pause"] = Bool(() => techPauseEnabled.Value, v => techPauseEnabled.Value = v),
                };
                // Alternative names for the same tech pause settings.
                map["matchzy_max_tech_pauses"] = map["get5_max_tech_pauses"];
                map["matchzy_tech_pause_time"] = map["get5_tech_pause_time"];
                // Get5 aliases share the setting.
                foreach (var alias in new[] { "demo_upload_url", "demo_upload_s3", "demo_upload_header_key", "demo_upload_header_value", "allow_force_ready", "remote_backup_url", "remote_backup_header_key", "remote_backup_header_value", "time_to_start", "time_to_start_veto" })
                    map["get5_" + alias] = map["matchzy_" + alias];
                _pluginSettingAccessors = map;
                return map;
            }
        }

        public void ResetChangedConvars()
        {
            foreach (string key in matchConfig.OriginalCvars.Keys)
            {
                string value = matchConfig.OriginalCvars[key];
                // Console-command settings are put back on their field directly (a command cannot
                // set an empty URL, for example).
                if (PluginSettingAccessors.TryGetValue(key, out var accessor))
                {
                    accessor.Set(value);
                    continue;
                }
                if (!IsAllowedMatchCvar(key, value, out string reason))
                {
                    Log($"[ResetChangedConvars] Skipping '{key}': {reason}");
                    continue;
                }
                if (TrySetFakeConVar(key, value))
                    continue;
                // Quoted: an original value with spaces used to be split into several arguments.
                Server.ExecuteCommand($"{key} \"{value}\"");
            }
        }

        public string FormatCvarValue(string value)
        {
            string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            (int team1Score, int team2Score) = GetTeamsScore();

            var formattedValue = value.Replace("{TIME}", formattedTime.Replace(" ", "_")).Replace("{MATCH_ID}", $"{liveMatchId}").Replace("{MAP}", Server.MapName).Replace("{MAPNUMBER}", matchConfig.CurrentMapNumber.ToString()).Replace("{TEAM1}", matchzyTeam1.teamName).Replace("{TEAM2}", matchzyTeam2.teamName).Replace("{TEAM1_SCORE}", team1Score.ToString()).Replace("{TEAM2_SCORE}", team2Score.ToString());
            return formattedValue;
        }

        public void UpdateHostname()
        {
            string hostname = hostnameFormat.Value.Trim();
            if (hostname == "" || hostname == "\"\"")
                return;
            string formattedHostname = FormatCvarValue(hostname).Replace("\"", "");
            // Runs every round start; skip the console command when the hostname is already set.
            if (ConVar.Find("hostname")?.StringValue == formattedHostname)
                return;
            // Quoted: team names keep their spaces here (only the demo file name replaces them).
            Server.ExecuteCommand($"hostname \"{formattedHostname}\"");
        }

        // Cached cs_gamerules proxy. FindAllEntitiesByDesignerName walks the WHOLE active
        // entity list and reads DesignerName out of native memory for every entity on the way,
        // so an uncached GetGameRules() is thousands of native string reads. It is called from
        // 16 sites here - several of them per round (round_start, phase checks, warmup checks) -
        // and the entity lives for the whole map, so the scan is pure waste after the first hit.
        // Cleared in OnMapStart (see MatchZy.cs); the IsValid check below is the second line of
        // defence for a proxy that dies mid-map.
        private CCSGameRulesProxy? _gameRulesProxy;

        internal void InvalidateGameRulesCache()
        {
            _gameRulesProxy = null;
        }

        // Returns null when the cs_gamerules entity is momentarily absent (map /
        // round / phase transitions). The old .First() threw InvalidOperationException
        // there, crashing the server mid-match. Callers MUST null-check.
        public CCSGameRules? GetGameRules()
        {
            if (_gameRulesProxy != null && _gameRulesProxy.IsValid)
                return _gameRulesProxy.GameRules;

            _gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            return _gameRulesProxy?.GameRules;
        }

        // -1 when gamerules absent (no real phase is negative), so callers comparing
        // == 4 / == 5 just get false instead of a throw.
        public int GetGamePhase()
        {
            return GetGameRules()?.GamePhase ?? -1;
        }

        public bool IsHalfTimePhase()
        {
            try
            {
                return GetGamePhase() == 4;
            }
            catch (Exception e)
            {
                Log($"[IsHalfTime FATAL] An error occurred: {e.Message}");
                return false;
            }
        }

        public bool IsPostGamePhase()
        {
            try
            {
                return GetGamePhase() == 5;
            }
            catch (Exception e)
            {
                Log($"[IsPostGamePhase FATAL] An error occurred: {e.Message}");
                return false;
            }
        }

        public bool IsTacticalTimeoutActive()
        {
            var gameRules = GetGameRules();
            if (gameRules == null)
                return false;

            return (gameRules.CTTimeOutActive || gameRules.TerroristTimeOutActive) && gameRules.FreezePeriod;
        }

        // Bots have no SteamID (0), so all of them would share one stats row. Give each a stable id
        // from its name instead: 90000000000000000 + FNV-1a(name). Real SteamID64s sit around
        // 7.656e16, so the range cannot collide with an account, and it still fits the signed
        // BIGINT/INTEGER steamid64 column. Same name, same id, so a bot keeps one row per map.
        internal const ulong BotStatsIdBase = 90_000_000_000_000_000UL;

        internal static ulong BotStatsId(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return BotStatsIdBase + hash;
        }

        // Who gets stats: every tracked human (playerData), plus bots on CT/T when
        // matchzy_stats_include_bots is on or the match has a bot team. Bots are deliberately NOT added to playerData, which
        // also drives the ready system, veto and pause counts.
        private IEnumerable<CCSPlayerController> StatsPlayers()
        {
            foreach (var player in playerData.Values)
            {
                // Coaches are on CT/T but do not play; they only die once per round at the end of
                // freeze time, which would give them a stats row full of suicides.
                if (IsMatchCoach(player))
                    continue;
                yield return player;
            }

            if (!statsIncludeBots.Value && !HasBotTeam())
                yield break;

            foreach (var bot in Utilities.GetPlayers())
            {
                if (bot is { IsValid: true, IsBot: true, IsHLTV: false } && bot.TeamNum is 2 or 3)
                    yield return bot;
            }
        }

        public (Dictionary<ulong, Dictionary<string, object>>, List<StatsPlayer>, List<StatsPlayer>) GetPlayerStatsDict()
        {
            Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary = new Dictionary<ulong, Dictionary<string, object>>();
            List<StatsPlayer> playerStatsListTeam1 = new();
            List<StatsPlayer> playerStatsListTeam2 = new();
            var gameRules = GetGameRules();
            int roundsPlayed = gameRules?.TotalRoundsPlayed ?? 0;
            try
            {
                foreach (CCSPlayerController player in StatsPlayers())
                {
                    if (!player.IsValid || player.ActionTrackingServices == null)
                        continue;

                    var playerStats = player.ActionTrackingServices.MatchStats;
                    ulong steamid64 = player.IsBot ? BotStatsId(player.PlayerName) : player.SteamID;

                    // Create a nested dictionary to store individual stats for the player
                    Dictionary<string, object> stats = new Dictionary<string, object>
                    {
                        { "PlayerName", player.PlayerName },
                        { "Kills", playerStats.Kills },
                        { "Deaths", playerStats.Deaths },
                        { "Assists", playerStats.Assists },
                        { "Damage", playerStats.Damage },
                        { "Enemy2Ks", playerStats.Enemy2Ks },
                        { "Enemy3Ks", playerStats.Enemy3Ks },
                        { "Enemy4Ks", playerStats.Enemy4Ks },
                        { "Enemy5Ks", playerStats.Enemy5Ks },
                        { "EntryCount", playerStats.EntryCount },
                        { "EntryWins", playerStats.EntryWins },
                        { "1v1Count", playerStats.I1v1Count },
                        { "1v1Wins", playerStats.I1v1Wins },
                        { "1v2Count", playerStats.I1v2Count },
                        { "1v2Wins", playerStats.I1v2Wins },
                        { "UtilityCount", playerStats.Utility_Count },
                        { "UtilitySuccess", playerStats.Utility_Successes },
                        { "UtilityDamage", playerStats.UtilityDamage },
                        { "UtilityEnemies", playerStats.Utility_Enemies },
                        { "FlashCount", playerStats.Flash_Count },
                        { "FlashSuccess", playerStats.Flash_Successes },
                        { "HealthPointsRemovedTotal", playerStats.HealthPointsRemovedTotal },
                        { "HealthPointsDealtTotal", playerStats.HealthPointsDealtTotal },
                        { "ShotsFiredTotal", playerStats.ShotsFiredTotal },
                        { "ShotsOnTargetTotal", playerStats.ShotsOnTargetTotal },
                        { "EquipmentValue", playerStats.EquipmentValue },
                        { "MoneySaved", playerStats.MoneySaved },
                        { "KillReward", playerStats.KillReward },
                        { "LiveTime", playerStats.LiveTime },
                        { "HeadShotKills", playerStats.HeadShotKills },
                        { "CashEarned", playerStats.CashEarned },
                        { "EnemiesFlashed", playerStats.EnemiesFlashed },
                    };

                    string teamName = "Spectator";
                    if (player.TeamNum == 3)
                    {
                        teamName = reverseTeamSides["CT"].teamName;
                    }
                    else if (player.TeamNum == 2)
                    {
                        teamName = reverseTeamSides["TERRORIST"].teamName;
                    }

                    stats["TeamName"] = teamName;

                    // Indexer, not Add: two playerData entries can resolve to the same SteamID
                    // (a reconnect leaves the old userid behind for a tick). Add would throw
                    // ArgumentException there, and the catch below wraps the whole loop, so one
                    // duplicate discarded every remaining player's stats.
                    playerStatsDictionary[steamid64] = stats;

                    // Fields the engine's MatchStats does not track come from the advanced stats
                    // (humans only; bots report 0 for these).
                    advancedStats.TryGetValue(player.SteamID, out var adv);
                    PlayerStats playerStatsInstance = new()
                    {
                        Kills = playerStats.Kills,
                        Deaths = playerStats.Deaths,
                        Assists = playerStats.Assists,
                        FlashAssists = adv?.FlashAssists ?? 0,
                        TeamKills = adv?.TeamKills ?? 0,
                        Suicides = adv?.Suicides ?? 0,
                        Damage = playerStats.Damage,
                        UtilityDamage = playerStats.UtilityDamage,
                        EnemiesFlashed = playerStats.EnemiesFlashed,
                        FriendliesFlashed = adv?.FriendliesFlashed ?? 0,
                        KnifeKills = adv?.KnifeKills ?? 0,
                        HeadshotKills = playerStats.HeadShotKills,
                        RoundsPlayed = roundsPlayed,
                        BombDefuses = adv?.BombDefuses ?? 0,
                        BombPlants = adv?.BombPlants ?? 0,
                        Kills1 = adv?.Kills1 ?? 0,
                        Kills2 = playerStats.Enemy2Ks,
                        Kills3 = playerStats.Enemy3Ks,
                        Kills4 = playerStats.Enemy4Ks,
                        Kills5 = playerStats.Enemy5Ks,
                        OneV1s = playerStats.I1v1Wins,
                        OneV2s = playerStats.I1v2Wins,
                        OneV3s = adv?.Clutch1v3Wins ?? 0,
                        OneV4s = adv?.Clutch1v4Wins ?? 0,
                        OneV5s = adv?.Clutch1v5Wins ?? 0,
                        FirstKillsT = adv?.FirstKillsT ?? 0,
                        FirstKillsCT = adv?.FirstKillsCT ?? 0,
                        FirstDeathsT = adv?.FirstDeathsT ?? 0,
                        FirstDeathsCT = adv?.FirstDeathsCT ?? 0,
                        TradeKills = adv?.TradeKills ?? 0,
                        // Get5 semantics: number of rounds with a kill, assist, survival or trade.
                        Kast = adv?.KastRounds ?? 0,
                        Score = player.Score,
                        Mvps = player.MVPs,
                    };

                    StatsPlayer statsPlayer = new()
                    {
                        SteamId = steamid64.ToString(),
                        Name = player.PlayerName,
                        Stats = playerStatsInstance,
                    };

                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;

                    if (player.TeamNum == 3)
                    {
                        if (ctTeamNum == 1)
                            playerStatsListTeam1.Add(statsPlayer);
                        if (ctTeamNum == 2)
                            playerStatsListTeam2.Add(statsPlayer);
                    }
                    else if (player.TeamNum == 2)
                    {
                        if (tTeamNum == 1)
                            playerStatsListTeam1.Add(statsPlayer);
                        if (tTeamNum == 2)
                            playerStatsListTeam2.Add(statsPlayer);
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[GetPlayerStatsDict FATAL] An error occurred: {e.Message}");
            }

            // Zero collected stats means nothing reaches matchzy_stats_players for this round.
            // That used to be completely silent: the maps and matches tables kept filling up as
            // normal, so the database looked healthy while every player row was missing. Name the
            // likely cause instead - an empty playerData means GetPlayerTeam rejected every
            // player, which is a match-config / side-mapping problem, not a database one.
            if (playerStatsDictionary.Count == 0 && isMatchLive)
            {
                int botsInPlay = Utilities.GetPlayers().Count(p => p is { IsValid: true, IsBot: true, IsHLTV: false } && p.TeamNum is 2 or 3);
                if (playerData.Count == 0 && botsInPlay > 0 && !statsIncludeBots.Value && !HasBotTeam())
                    Log($"[GetPlayerStatsDict] No human players in play, only {botsInPlay} bot(s), so no player stats are recorded. Set matchzy_stats_include_bots true to record bots.");
                else if (playerData.Count == 0)
                    Log("[GetPlayerStatsDict] WARNING: playerData is empty during a live round, so no player stats will be recorded. Every connected player resolved to CsTeam.None - check the team rosters in the match config and the CT/T side mapping.");
                else
                    Log($"[GetPlayerStatsDict] WARNING: no player stats collected from {playerData.Count} playerData entries - none were valid or had ActionTrackingServices.");
            }

            return (playerStatsDictionary, playerStatsListTeam1, playerStatsListTeam2);
        }

        static string RemoveSpecialCharacters(string input)
        {
            // First explicitly remove asterisks
            input = input.Replace("*", "");
            input = input.Replace("__", "");

            Regex regex = new("[^\\p{L}0-9 _-]");
            return regex.Replace(input, "");
        }

        private void Log(string message)
        {
            Console.WriteLine("[MatchZy] " + message);
        }

        // Practice has two shapes (see practiceUsesWarmup). Whenever the engine (re)starts a warmup
        // under practice - first player connect after a map load, mp_warmup_start's deferred init -
        // put practice back into the shape it wants:
        //  - prac.cfg practice: a live 60 min round. End the warmup the engine just began; without
        //    this the player sat through a mp_warmuptime countdown (5s on a typical server cfg), or,
        //    with the timer paused, stayed in warmup for good (no 60 min round timer).
        //  - built-in fallback practice: a paused warmup. Pin mp_warmuptime long (a short inherited
        //    value goes straight into the final-seconds countdown, which ignores the pause) and
        //    re-assert the pause the deferred init reset.
        private void SettlePracticeWarmupState(string where)
        {
            if (!isPractice || isDryRun)
                return;
            bool inWarmup = GetGameRules()?.WarmupPeriod ?? false;
            if (practiceUsesWarmup)
            {
                Log($"[PracticeWarmup] {where}: warmup-based practice, pausetimer was {ConVar.Find("mp_warmup_pausetimer")?.GetPrimitiveValue<int>()}, warmuptime {ConVar.Find("mp_warmuptime")?.GetPrimitiveValue<float>()}, re-asserting pause");
                Server.ExecuteCommand("mp_warmuptime 9999;mp_warmuptime_all_players_connected 0;mp_warmup_online_enabled 1;mp_warmup_pausetimer 1");
                return;
            }
            if (inWarmup)
            {
                Log($"[PracticeWarmup] {where}: engine warmup active under prac.cfg practice, ending it (mp_warmup_end)");
                Server.ExecuteCommand("mp_warmup_end");
            }
        }

        // Called by the explicit mode commands (css_prac / css_match / css_scrim / css_sleep /
        // css_exitprac). Consumes this map's AutoStart latch and writes the chosen mode back to
        // matchzy_autostart_mode so it survives a changelevel.
        //
        // Why both: Load() queues config.cfg via Server.ExecuteCommand, which APPENDS to the command
        // buffer. A mode-switch script doing
        //   css_plugins load "MatchZy"; matchzy_autostart_mode 2; css_prac
        // runs its remaining lines first, THEN config.cfg lands and its `matchzy_autostart_mode 1`
        // overwrites the script's 2. The latch stops the deferred load-time AutoStart from starting
        // warmup on top of the practice css_prac just started; writing the cvar makes the mode stick
        // when the script (or CS2MapChange) reloads the map right after to apply the game mode,
        // where OnMapStart's AutoStart would otherwise read the clobbered 1 and come up in warmup.
        private void SetExplicitMode(int mode)
        {
            autoStartLatched = true;
            explicitAutoStartMode = mode;
            // Cvar writes inside this window are the command buffer still draining (our own write
            // below, then the config.cfg that Load() appended): keep the explicit choice. A write
            // after the window is an admin/cfg deliberately changing the mode: it wins.
            explicitAutoStartModeGraceTick = Server.TickCount + 5;
            if (autoStartModeCvar.Value != mode)
            {
                Log($"[SetExplicitMode] matchzy_autostart_mode {autoStartModeCvar.Value} -> {mode} (explicit mode command)");
                autoStartModeCvar.Value = mode;
            }
        }

        private void OnAutoStartModeCvarChanged(object? sender, int value)
        {
            if (explicitAutoStartMode is not int explicitMode || value == explicitMode)
                return;
            if (Server.TickCount <= explicitAutoStartModeGraceTick)
            {
                Log($"[AutoStart] matchzy_autostart_mode set to {value} right after an explicit mode command (cfg queued at load); keeping mode {explicitMode} for the next AutoStart");
                return;
            }
            explicitAutoStartMode = null;
        }

        private void AutoStart()
        {
            // Per-map latch: AutoStart is triggered from multiple sites (Load timer, OnMapStart,
            // first player connect). Run at most once per map load; latch is re-armed in OnMapStart.
            // The latch is also consumed by the explicit mode commands (css_prac / css_dryrun /
            // css_match / css_scrim / css_sleep): a mode-switch script that does
            //   css_plugins load "MatchZy"; matchzy_autostart_mode 2; css_prac
            // runs its remaining lines first, THEN the config.cfg queued by Load() lands
            // (Server.ExecuteCommand appends to the command buffer) and its `matchzy_autostart_mode 1`
            // overwrites the script's 2; the deferred load-time AutoStart read 1 and StartWarmup
            // clobbered the practice session css_prac had just started. Consuming the latch in the
            // command keeps that from happening, while OnMapStart re-arms it so a later changelevel
            // (external too) still re-applies the mode on the new map.
            // Several triggers per map is normal (they are there so at least one fires), so the
            // skipped ones are silent.
            if (autoStartLatched)
                return;
            autoStartLatched = true;

            // A mode chosen explicitly since the last AutoStart (css_prac etc.) beats the cvar: the
            // cvar may hold config.cfg's value, exec'd after the script that set it. Write it back so
            // later map changes keep the mode too.
            if (explicitAutoStartMode is int explicitMode)
            {
                explicitAutoStartMode = null;
                if (autoStartModeCvar.Value != explicitMode)
                {
                    Log($"[AutoStart] restoring matchzy_autostart_mode {autoStartModeCvar.Value} -> {explicitMode} (explicit mode command overridden by a later cfg exec)");
                    autoStartModeCvar.Value = explicitMode;
                }
            }

            // Read the ConVar live at consumption time. AutoStart fires ~1s after load / map start,
            // by which point any cfg (e.g. a mapchange script doing `matchzy_autostart_mode 2`) has
            // fully exec'd - so this always reflects the intended mode, with no load-time snapshot race.
            autoStartMode = autoStartModeCvar.Value;

            Log($"[AutoStart] autoStartMode: {autoStartMode}");
            if (autoStartMode == 0)
            {
                isMatchModeEnabled = false;
                StartSleepMode();
            }

            if (autoStartMode == 1)
            {
                isMatchModeEnabled = true;
                readyAvailable = true;
                isPractice = false;
                StartWarmup();
            }

            if (autoStartMode == 2)
            {
                isMatchModeEnabled = false;
                StartPracticeMode();
            }
        }

        public int GetGameMode()
        {
            var convar = ConVar.Find("game_mode");
            if (convar != null)
            {
                return convar.GetPrimitiveValue<int>();
            }

            return -1;
        }

        public int GetGameType()
        {
            var convar = ConVar.Find("game_type");
            if (convar != null)
            {
                return convar.GetPrimitiveValue<int>();
            }

            return -1;
        }

        public void SetCorrectGameMode()
        {
            ConVar.Find("game_mode")!.SetValue(matchConfig.Wingman ? 2 : 1);
            ConVar.Find("game_type")!.SetValue(0); // Classic GameType
        }

        public bool IsMapReloadRequiredForGameMode(bool wingman)
        {
            int expectedMode = wingman ? 2 : 1;
            if (GetGameMode() != expectedMode || GetGameType() != 0)
            {
                return true;
            }

            return false;
        }

        public bool IsWingmanMode()
        {
            if (GetGameMode() == 2 && GetGameType() == 0)
                return true;
            return false;
        }

        public bool IsPlayerValid(CCSPlayerController? player)
        {
            return player != null && player.IsValid && player.Connected == PlayerConnectedState.Connected && player.PlayerPawn.IsValid && player.PlayerPawn.Value != null && player.PlayerPawn.Value.IsValid;
        }

        public bool IsHumanPlayerValid(CCSPlayerController? player)
        {
            return IsPlayerValid(player) && !player!.IsBot && !player.IsHLTV;
        }

        // Issues #391/#393: after .last / .loadnade / .back, the engine can
        // leave the player stuck in a half-crouch ("MJ peek" / swimming peek).
        // Resetting the duck flags addresses the cases where the duck state is
        // the visible cause.
        //
        // The full-body throw/lean pose (post-AG2 / Animation Graph 2.0) cannot
        // be cleared from here: it lives in m_pGraphInstanceAG2, which CSS does
        // not expose (no SetAnimGraphParameter, and bumping the serialized-recipe
        // version + networked dirty flags has no visible effect - tested). The
        // reliable fix is to rebuild the pawn via Respawn(); see
        // RespawnAndTeleport, which the teleport commands now route through.
        public static void ResetPlayerCrouch(CCSPlayerController? player, bool wantDucked = false)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
                return;
            var pawn = player.PlayerPawn.Value;
            if (pawn.MovementServices == null || pawn.MovementServices.Handle == IntPtr.Zero)
                return;
            var ms = new CCSPlayer_MovementServices(pawn.MovementServices.Handle);
            ms.DuckAmount = wantDucked ? 1.0f : 0.0f;
            ms.Ducked = wantDucked;
            ms.Ducking = false;
            ms.DesiresDuck = wantDucked;
            ms.DuckOverride = false;
            ms.DuckRootOffset = 0.0f;
            ms.DuckViewOffset = wantDucked ? 1.0f : 0.0f;
            ms.LastDuckTime = 0.0f;
        }

        // Issues #391/#393 + MatchZy-Enhanced#10: teleport to the target position,
        // keep the body flat, and put the thrown grenade back in hand at the lineup
        // WITHOUT respawning (respawn wipes the inventory). deployWeapon, if given,
        // is the weapon CLASSNAME ("weapon_smokegrenade" etc.) to end up holding.
        // giveDeploy = true for grenade restores (.last/.back/.ln) where the nade
        // may have been consumed; false for loadpos (switch-only, never dup a rifle).
        // afterRestore is a legacy caller hook run right after the teleport.
        // All thrown-grenade weapon classnames. molotov maps to weapon_incgrenade
        // on CT / weapon_molotov on T; both variants of every nade are listed.
        private static readonly HashSet<string> _grenadeClassnames = new()
        {
            "weapon_molotov",
            "weapon_incgrenade",
            "weapon_smokegrenade",
            "weapon_hegrenade",
            "weapon_decoy",
            "weapon_flashbang",
        };

        private static bool IsGrenadeClassname(string classname) => _grenadeClassnames.Contains(classname);

        // Experimental flicker-free nade-restore mode (matchzy_nade_pose_flicker_free).
        // false (default) = proven 1-frame knife bounce (tiny knife flash, always clears
        // the pose). true = same-frame reselect (no knife flash, but only clears the pose
        // if the SelectItem holster cancels the throw gesture synchronously - untested per
        // build, toggle live to compare).
        public static bool nadePoseFlickerFree = false;

        public static void TeleportAndClearPose(CCSPlayerController? player, Vector position, QAngle angle, bool wantDucked = false, string? deployWeapon = null, bool giveDeploy = false, Action? afterRestore = null)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
                return;
            var pawn = player.PlayerPawn.Value;

            // Teleport with the FULL lineup angle: this snaps the LOCAL player's VIEW to the throw
            // pitch/yaw (the client owns its own view, so only a teleport can force it) - required
            // to actually reproduce the lineup aim.
            pawn.Teleport(position, angle, new Vector(0, 0, 0));
            pawn.ResetNoclipToWalk();
            // Issue MatchZy-Enhanced#10: the same teleport also writes the pitch into the model's
            // transform, tilting the WHOLE body sideways at steep angles (look up → .last/.back →
            // you see your own sprawled body). Flatten the model back to yaw-only. Must flatten the
            // SOURCE rotation (m_angRotation / node.Rotation), NOT the derived AbsRotation - the anim
            // system recomputes AbsRotation from the source every tick, so flattening AbsRotation
            // alone is clobbered. Re-apply over a few frames while the teleport rotation settles.
            FlattenBodyRotationFrames(player, angle.Y, 6);
            ResetPlayerCrouch(player, wantDucked);

            afterRestore?.Invoke();

            float bodyYaw = angle.Y;
            Server.NextFrame(() =>
            {
                if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
                    return;
                FlattenBodyRotation(player.PlayerPawn.Value, bodyYaw);

                if (string.IsNullOrEmpty(deployWeapon))
                    return;

                bool owns = player.PlayerPawn.Value.WeaponServices?.MyWeapons
                    .Any(h => h.Value != null && h.Value.IsValid && h.Value.DesignerName == deployWeapon) ?? false;

                if (IsGrenadeClassname(deployWeapon!))
                {
                    // CRITICAL: do NOT deploy the grenade with SelectItem subType=0. On a
                    // grenade that makes the engine THROW it - that was the .loadnade
                    // auto-throw (the owned smoke was launched mid-restore, dead into the
                    // wall at tight corners). Deploy the KNIFE first via SelectItem (a knife
                    // never throws; its full-body idle clears the frozen throw pose, issue
                    // #391), then next frame give the nade if it was consumed and draw it via
                    // SelectItem subType=4 (reselect/redeploy - a real deploy animation with
                    // NO throw), so a later manual throw fires from the proper deploy state.
                    // Fall back to the EquipWeaponByName pointer switch if SelectItem is
                    // unavailable.
                    string nade = deployWeapon!;
                    bool needGive = giveDeploy && !owns;
                    if (!SwitchWeaponNative(player, "weapon_knife"))
                        EquipWeaponByName(player, "weapon_knife");
                    Server.NextFrame(() =>
                    {
                        if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
                            return;
                        if (needGive)
                            player.GiveNamedItem(nade);
                        if (!SwitchWeaponNative(player, nade, 4))
                            EquipWeaponByName(player, nade);
                    });
                }
                else
                {
                    // Non-grenade (loadpos switch): no throw risk. SelectItem, else
                    // give if missing, else pointer-switch.
                    if (SwitchWeaponNative(player, deployWeapon!)) { }
                    else if (giveDeploy && !owns)
                        player.GiveNamedItem(deployWeapon!);
                    else
                        EquipWeaponByName(player, deployWeapon!);
                }
            });
        }

        // Lightweight upright teleport for non-nade repositioning (.spawn, best/worst
        // spawn). Same body-tilt fix as TeleportAndClearPose (issue MatchZy-Enhanced#8:
        // a CS2 update made Teleport write pitch/roll into the model transform, tilting
        // the WHOLE body sideways at steep angles) but WITHOUT the grenade/weapon
        // re-deploy - a plain spawn teleport has no stuck throw pose to clear, so the
        // heavier TeleportAndClearPose path (knife-bounce + SelectItem) is overkill.
        // Full-angle teleport snaps the LOCAL player's view; flatten the SOURCE rotation
        // (m_angRotation), not the derived AbsRotation, over a few frames while the
        // teleport rotation settles.
        public static void TeleportUpright(CCSPlayerController? player, Vector position, QAngle angle)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
                return;
            player.PlayerPawn.Value.Teleport(position, angle, new Vector(0, 0, 0));
            player.PlayerPawn.Value.ResetNoclipToWalk();
            FlattenBodyRotationFrames(player, angle.Y, 6);
        }

        // Server-side weapon switch by classname. CS2's `use weapon_x` / `slotN`
        // console commands do NOT switch when issued via ExecuteClientCommand on
        // this build, so we set the active-weapon handle directly (same mechanism
        // as DropWeaponByDesignerName) and flag it networked-dirty so the client
        // re-deploys the viewmodel. Returns true if the weapon was found+equipped.
        public static bool EquipWeaponByName(CCSPlayerController? player, string classname)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
                return false;
            var pawn = player.PlayerPawn.Value;
            var ws = pawn.WeaponServices;
            if (ws == null)
                return false;
            var matched = ws.MyWeapons.FirstOrDefault(x => x.Value != null && x.Value.IsValid && x.Value.DesignerName == classname);
            if (matched == null || !matched.IsValid)
                return false;
            ws.ActiveWeapon.Raw = matched.Raw;
            // m_hActiveWeapon lives in the WeaponServices component, not on the pawn, so mark the
            // pawn's networked pointer to it. Naming the component class here instead notifies the
            // PAWN at the component-relative offset, i.e. some unrelated pawn field.
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pWeaponServices");
            return true;
        }

        // Engine weapon-select (CCSPlayer_WeaponServices::SelectItem(this, weapon,
        // subType) - vtable slot from gamedata, e.g. 31/linux, 30/windows on 14186). This is the ONLY way to
        // deploy an already-owned weapon: it holsters the current weapon and
        // deploys the target (redraws viewmodel + plays the deploy anim, which
        // clears the frozen throw pose), with no GiveNamedItem (no-op when owned)
        // and no entity deletion (crashes). The vtable index comes from the gamedata
        // key "CCSPlayer_WeaponServices_SelectItem" (offset entry), shipped only in
        // the plugin's own gamedata/matchzy.json so both the fork and stock upstream
        // CounterStrikeSharp resolve it. Cached lazily; -1 =
        // unavailable -> caller falls back to a pointer switch.
        private const int SelectItemOffsetUntried = -2;
        private const int SelectItemOffsetUnavailable = -1;
        private static int _selectItemOffset = SelectItemOffsetUntried;
        private static int SelectItemOffset()
        {
            if (_selectItemOffset != SelectItemOffsetUntried)
                return _selectItemOffset;
            try
            {
                _selectItemOffset = GameData.GetOffset("CCSPlayer_WeaponServices_SelectItem");
            }
            catch (Exception e)
            {
                _selectItemOffset = SelectItemOffsetUnavailable;
                Console.WriteLine("[MatchZy] CCSPlayer_WeaponServices_SelectItem offset missing from gamedata.json - falling back to pointer switch. " + e.Message);
            }
            return _selectItemOffset;
        }

        // Deploy an owned weapon by classname via the engine SelectItem vfunc.
        // Returns false if the offset is unavailable or the weapon isn't owned
        // (caller falls back).
        public static bool SwitchWeaponNative(CCSPlayerController? player, string classname, int subType = 0)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value?.WeaponServices == null)
                return false;
            int offset = SelectItemOffset();
            if (offset < 0)
                return false;
            var ws = player.PlayerPawn.Value.WeaponServices;
            var matched = ws.MyWeapons.FirstOrDefault(x => x.Value != null && x.Value.IsValid && x.Value.DesignerName == classname);
            if (matched?.Value == null || !matched.Value.IsValid)
                return false;
            // SelectItem(this, weapon, subType): 0 = normal select for a different weapon
            // (on a grenade this triggers the THROW). 4 = the reselect/redeploy path,
            // used to draw a weapon with its deploy animation WITHOUT throwing - needed
            // to put a restored grenade in hand so a later manual throw animates from the
            // proper deploy state (subType 0 / a pointer switch releases mid-windup).
            var wsHandle = ws.Handle;
            VirtualFunction.CreateVoid<IntPtr, IntPtr, int>(wsHandle, offset)(wsHandle, matched.Value.Handle, subType);
            return true;
        }

        // Remove every weapon of the given classname from the player's inventory.
        // Uses entity-IO "Kill" (the engine queues a clean delete) instead of
        // CBaseEntity.Remove() - Remove() frees a still-networked weapon mid-frame
        // and crashes the server (WriteEnterPVS: GetEntServerClass failed). Returns
        // true if at least one matching weapon was scheduled for removal.
        public static bool RemoveWeaponByName(CCSPlayerController? player, string classname)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value?.WeaponServices == null)
                return false;
            bool any = false;
            foreach (var h in player.PlayerPawn.Value.WeaponServices.MyWeapons)
            {
                var w = h.Value;
                if (w != null && w.IsValid && w.DesignerName == classname)
                {
                    w.AcceptInput("Kill");
                    any = true;
                }
            }
            return any;
        }

        // Re-flatten a live player's body/entity transform to yaw-only after a
        // Teleport, so a saved pitch (look up/down) doesn't tilt the whole model.
        // See TeleportAndClearPose / issue MatchZy-Enhanced#10.
        private static void FlattenBodyRotation(CCSPlayerPawn pawn, float yaw)
        {
            var node = pawn.CBodyComponent?.SceneNode;
            if (node == null)
                return;
            // Flatten the SOURCE rotation (m_angRotation) - the anim system derives AbsRotation
            // from this each tick, so flattening the source is what actually holds the body flat.
            node.Rotation.X = 0f;
            node.Rotation.Y = yaw;
            node.Rotation.Z = 0f;
            // Also flatten the current derived value so this same frame renders flat.
            node.AbsRotation.X = 0f;
            node.AbsRotation.Y = yaw;
            node.AbsRotation.Z = 0f;
        }

        // Flatten the body transform across the next few frames. The teleport rotation re-syncs
        // for a couple ticks, so a single write is clobbered; re-applying over ~6 frames holds it.
        private static void FlattenBodyRotationFrames(CCSPlayerController player, float yaw, int frames)
        {
            if (frames <= 0 || player == null || !player.IsValid || player.PlayerPawn.Value == null)
                return;
            FlattenBodyRotation(player.PlayerPawn.Value, yaw);
            Server.NextFrame(() => FlattenBodyRotationFrames(player, yaw, frames - 1));
        }

        public static Color GetPlayerTeammateColor(CCSPlayerController playerController)
        {
            return playerController.CompTeammateColor switch
            {
                1 => Color.FromArgb(50, 255, 0),
                2 => Color.FromArgb(255, 255, 0),
                3 => Color.FromArgb(255, 132, 0),
                4 => Color.FromArgb(255, 0, 255),
                0 => Color.FromArgb(0, 187, 255),
                _ => Color.Red,
            };
        }

        // live.cfg / live_wingman.cfg content keyed by path, revalidated by mtime. HandlePlayoutConfig
        // reads two convars out of the file on every call, and it runs on the game thread at round
        // start (rounds 0 and 1) as well as from the StartLive/StartScrim/StartHill NextFrame. A
        // cached copy turns those into a stat() plus a regex over an in-memory string.
        private static readonly Dictionary<string, (DateTime LastWriteUtc, string Content)> _cfgFileCache = new();
        private static readonly object _cfgFileCacheLock = new();

        private static string ReadCfgFileCached(string filePath)
        {
            // Missing file: fall through to ReadAllText so the caller sees the same
            // FileNotFoundException it always did.
            DateTime lastWrite = File.GetLastWriteTimeUtc(filePath);
            lock (_cfgFileCacheLock)
            {
                if (_cfgFileCache.TryGetValue(filePath, out var cached) && cached.LastWriteUtc == lastWrite)
                    return cached.Content;
            }
            string content = File.ReadAllText(filePath);
            lock (_cfgFileCacheLock)
            {
                _cfgFileCache[filePath] = (lastWrite, content);
            }
            return content;
        }

        public static string? GetConvarValueFromCFGFile(string filePath, string convarName)
        {
            string fileContent = ReadCfgFileCached(filePath);

            string pattern = @$"^{convarName}\s+(.+)$";

            // The static overload goes through Regex's own parsed-pattern cache, so the pattern is
            // parsed once per convar name instead of on every call.
            Match match = Regex.Match(fileContent, pattern, RegexOptions.Multiline);
            string? value = match.Success ? match.Groups[1].Value : null;
            return value;
        }

        public async Task<bool> UploadFileAsync(string? filePath, string fileUploadURL, string headerKey, string headerValue, long matchId, int mapNumber, int roundNumber, bool useS3PresignedPut = false)
        {
            if (filePath == null || fileUploadURL == "")
            {
                Log($"[UploadFileAsync] Not able to upload the file, either filePath or fileUploadURL is not set. filePath: {filePath} upload URL set: {fileUploadURL != ""}");
                return false;
            }

            try
            {
                // Host only: a presigned S3 URL carries a live signature in its query string.
                string uploadHost = Uri.TryCreate(fileUploadURL, UriKind.Absolute, out var uploadUri) ? uploadUri.Host : "(invalid URL)";
                Log($"[UploadFileAsync] Going to upload the file to {uploadHost}. Complete path: {filePath}" + (useS3PresignedPut ? " (HTTP PUT, S3-compatible)" : ""));

                if (!File.Exists(filePath))
                {
                    Log($"[UploadFileAsync ERROR] File not found: {filePath}");
                    return false;
                }

                // Demos run to hundreds of MB, so this cannot use _sharedHttpClient (10s timeout):
                // every real upload would be cancelled mid-transfer.
                using var httpClient = new HttpClient { Timeout = useS3PresignedPut ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(30) };

                if (useS3PresignedPut)
                {
                    using FileStream s3FileStream = File.OpenRead(filePath);
                    using StreamContent s3Content = new(s3FileStream);
                    s3Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                    using HttpRequestMessage s3Request = new(HttpMethod.Put, fileUploadURL) { Content = s3Content };

                    if (!string.IsNullOrEmpty(headerKey) && !string.IsNullOrEmpty(headerValue))
                    {
                        s3Request.Headers.TryAddWithoutValidation(headerKey, headerValue);
                    }

                    using HttpResponseMessage s3Response = await httpClient.SendAsync(s3Request);

                    if (s3Response.IsSuccessStatusCode)
                    {
                        Log($"[UploadFileAsync] File upload successful for matchId: {matchId} mapNumber: {mapNumber} fileName: {Path.GetFileName(filePath)}.");
                        return true;
                    }

                    Log($"[UploadFileAsync ERROR] Failed to upload file. Status code: {s3Response.StatusCode} Response: {await s3Response.Content.ReadAsStringAsync()}");
                    return false;
                }

                // Stream the file instead of loading a demo of hundreds of MB into memory.
                using FileStream fileStream = File.OpenRead(filePath);
                using var request = new HttpRequestMessage(HttpMethod.Post, fileUploadURL);
                using var content = new StreamContent(fileStream);
                content.Headers.Add("Content-Type", "application/octet-stream");

                content.Headers.Add("MatchZy-FileName", Path.GetFileName(filePath));
                content.Headers.Add("MatchZy-MatchId", matchId.ToString());
                content.Headers.Add("MatchZy-MapNumber", mapNumber.ToString());
                content.Headers.Add("MatchZy-RoundNumber", roundNumber.ToString());

                // For Get5 Panel
                content.Headers.Add("Get5-FileName", Path.GetFileName(filePath));
                content.Headers.Add("Get5-MatchId", matchId.ToString());
                content.Headers.Add("Get5-MapNumber", mapNumber.ToString());
                content.Headers.Add("Get5-RoundNumber", roundNumber.ToString());

                if (!string.IsNullOrEmpty(headerKey) && !string.IsNullOrEmpty(headerValue))
                {
                    request.Headers.TryAddWithoutValidation(headerKey, headerValue);
                }

                request.Content = content;
                using HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Log($"[UploadFileAsync] File upload successful for matchId: {matchId} mapNumber: {mapNumber} fileName: {Path.GetFileName(filePath)}.");
                    return true;
                }

                Log($"[UploadFileAsync ERROR] Failed to upload file. Status code: {response.StatusCode} Response: {await response.Content.ReadAsStringAsync()}");
                return false;
            }
            catch (Exception e)
            {
                Log($"[UploadFileAsync FATAL] An error occurred: {e.Message}");
                return false;
            }
        }

        // whitelist.cfg parsed once and reused until the file changes (one stat per connect instead
        // of reading and scanning the whole file on the game thread).
        private HashSet<string>? _whitelistCache;
        private string? _whitelistCachePath;
        private DateTime _whitelistCacheWriteTime;

        private HashSet<string> GetWhitelist(string path)
        {
            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (_whitelistCache == null || _whitelistCachePath != path || _whitelistCacheWriteTime != writeTime)
            {
                _whitelistCache = new HashSet<string>(File.ReadAllLines(path).Select(line => line.Trim()), StringComparer.Ordinal);
                _whitelistCachePath = path;
                _whitelistCacheWriteTime = writeTime;
            }
            return _whitelistCache;
        }

        public bool HandlePlayerWhitelist(CCSPlayerController player, string steamId)
        {
            // Whitelist off: no file access at all. It used to create and read whitelist.cfg on the
            // game thread on every connect even when the whitelist was not in use.
            if (!isWhitelistRequired)
                return false;

            string whitelistfileName = MatchZyCfgRel("whitelist.cfg");
            string whitelistPath = Path.Join(Server.GameDirectory + "/csgo/cfg", whitelistfileName);
            string? directoryPath = Path.GetDirectoryName(whitelistPath);
            if (directoryPath != null && !Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
            if (!File.Exists(whitelistPath))
                File.WriteAllLines(whitelistPath, new[] { "Steamid1", "Steamid2" });

            if (!GetWhitelist(whitelistPath).Contains(steamId))
            {
                Log($"[EventPlayerConnectFull] KICKING PLAYER STEAMID: {steamId}, Name: {player.PlayerName} (Not whitelisted!)");
                PrintLocalizedToAll("matchzy.util.kicknotwhitelisted", player.PlayerName);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds the value stored in matchzy_stats_matches.server_ip, as "ip:port".
        ///
        /// The port matters: it is what identifies a specific server when several run on one box,
        /// and it is what existing rows (and anything querying them) already contain. Reading only
        /// the "ip" convar dropped it and made new rows inconsistent with the old ones.
        ///
        /// Must be called on the game thread - capture the result before any Task.Run.
        /// </summary>
        private static string GetServerIpForStats()
        {
            // "ip" is the BIND address, so it reads 0.0.0.0 ("all interfaces") unless the server was
            // started with -ip. That is expected, not a bug - the port is what distinguishes servers
            // sharing a host, and anything needing the public address can map it from the port.
            string ip = ConVar.Find("ip")?.StringValue ?? "0";
            int port = ConVar.Find("hostport")?.GetPrimitiveValue<int>() ?? 0;
            return port > 0 ? $"{ip}:{port}" : ip;
        }

        // Kicks on the next frame, off the event/command dispatch stack. Re-validates across
        // the frame boundary: the player may already have disconnected.
        public void KickPlayerDeferred(CCSPlayerController player)
        {
            Server.NextFrame(() =>
            {
                if (!IsPlayerValid(player) || !player.UserId.HasValue)
                    return;
                Server.ExecuteCommand($"kickid {player.UserId.Value}");
            });
        }

        /// <summary>
        /// Puts every connected human on the side the loaded match assigns them: roster players on
        /// their team's current side, listed spectators on Spectator, and (when the match has team
        /// rosters) anyone not in the match on Spectator. Players already on the server when a
        /// match was loaded used to stay wherever they were, so teams could start mixed.
        /// </summary>
        private void PlaceMatchPlayers()
        {
            if (!isMatchSetup || !IsTeamWhitelistConfigured())
                return;
            foreach (var p in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(p) || p.IsBot || p.IsHLTV)
                    continue;
                if (IsMatchCoach(p))
                    continue;
                CsTeam target = GetPlayerTeam(p);
                SwitchPlayerTeam(p, target == CsTeam.None ? CsTeam.Spectator : target);
            }
        }

        public void SwitchPlayerTeam(CCSPlayerController player, CsTeam team)
        {
            // None is what GetPlayerTeam returns for a player who is not part of the match.
            // SwitchTeam(None) followed by the warmup Respawn below on an unassigned player is
            // the respawn-a-non-T/CT crash class - never move anyone to None.
            if (team == CsTeam.None)
                return;
            if (player.Team == team)
                return;

            Server.NextFrame(() =>
            {
                // Re-validate across the frame boundary: this runs a frame after the team event
                // that triggered it, and the player can have disconnected in between. Calling
                // SwitchTeam/ChangeTeam/Respawn on a freed controller takes the server down.
                // Controller-level only: a player without a pawn (auto-assigned from the team
                // menu, or coming from Spectator) must be corrected too.
                if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected)
                    return;
                if (player.Team == team)
                    return;
                // Never move a player who is still in the team menu (team None). Forcing the join
                // from there (HandleCommand_JoinTeam on an unassigned controller) crashed the server
                // right after a player connected (0.8.88). They pick a team themselves; the jointeam
                // listener only lets them pick their roster side and this handler corrects the rest.
                if (player.TeamNum == (byte)CsTeam.None)
                    return;

                if (team == CsTeam.Spectator)
                {
                    MoveToSpectatorWhenDead(player, suicideSent: false);
                }
                else if (player.TeamNum == (byte)CsTeam.Spectator)
                {
                    // From Spectator / the team menu: SwitchTeam only writes the team number and
                    // Respawn on an observer crashes, so use the engine's join handler (same path as
                    // practice .t/.ct) and respawn during warmup once the team has landed.
                    try
                    {
                        handleCommandJoinTeam.Value.Invoke(player, (byte)team, 2, 0f);
                    }
                    catch (Exception joinEx)
                    {
                        // ChangeTeam leaves the controller observing; respawning that controller is
                        // the never-respawn-a-spectator crash class, so no respawn on this path.
                        Log($"[SwitchPlayerTeam] HandleCommand_JoinTeam unavailable ({joinEx.Message}), falling back to ChangeTeam without a respawn");
                        player.ChangeTeam(team);
                        return;
                    }
                    RespawnWhenTeamApplied(player, team, RespawnRetryAttempts, keepGoing: () => isMatchSetup && !matchStarted && !IsMatchCoach(player) && !IsSideFull(team, player));
                }
                else
                {
                    player.SwitchTeam(team);
                    var gameRules = GetGameRules();
                    if (gameRules != null && gameRules.WarmupPeriod && IsPlayerValid(player))
                    {
                        player.Respawn();
                    }
                }
            });
        }

        /// <summary>
        /// Moves a player to Spectator only once their pawn is dead. ChangeTeam on a LIVE pawn runs
        /// the engine's weapon-strip path, where other plugins' weapon hooks re-enter on a
        /// half-destroyed weapon -> SIGSEGV. A suicide is not enough on its own: it is a no-op on a
        /// pawn with TakesDamage=false (e.g. after .coachtest), and the death can land a few ticks
        /// later, so the pawn is re-checked every frame (bounded) before ChangeTeam runs. Gives up
        /// rather than ever calling ChangeTeam on a live pawn.
        /// </summary>
        private void MoveToSpectatorWhenDead(CCSPlayerController player, bool suicideSent, int attemptsLeft = 16)
        {
            if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected)
                return;
            if (player.Team == CsTeam.Spectator)
                return;
            if (player.PawnIsAlive)
            {
                if (!suicideSent)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn != null && pawn.IsValid)
                    {
                        pawn.TakesDamage = true;
                        pawn.CommitSuicide(explode: false, force: true);
                    }
                    suicideSent = true;
                }
                if (attemptsLeft <= 0)
                {
                    Log($"[MoveToSpectatorWhenDead] {player.PlayerName} is still alive; not moving them to Spectator (ChangeTeam on a live pawn crashes).");
                    return;
                }
                bool sent = suicideSent;
                Server.NextFrame(() => MoveToSpectatorWhenDead(player, sent, attemptsLeft - 1));
                return;
            }
            // PawnIsAlive is a networked controller field that can lag the pawn by a tick (and practice
            // respawns on death): also check the pawn itself before the team change.
            var deadPawn = player.PlayerPawn.Value;
            if (deadPawn != null && deadPawn.IsValid && deadPawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
            {
                if (attemptsLeft <= 0)
                {
                    Log($"[MoveToSpectatorWhenDead] {player.PlayerName}'s pawn is still alive; not moving them to Spectator.");
                    return;
                }
                Server.NextFrame(() => MoveToSpectatorWhenDead(player, true, attemptsLeft - 1));
                return;
            }
            try
            {
                player.ChangeTeam(CsTeam.Spectator);
            }
            catch (Exception ex)
            {
                Log($"[MoveToSpectatorWhenDead] ChangeTeam failed: {ex.Message}");
            }
        }

        public void SetPlayerInvisible(CCSPlayerController player, bool setWeaponsInvisible)
        {
            if (!IsPlayerValid(player))
                return;
            var playerPawnValue = player.PlayerPawn.Value;

            if (playerPawnValue != null && playerPawnValue.IsValid)
            {
                playerPawnValue.Render = Color.FromArgb(0, 0, 0, 0);
                Utilities.SetStateChanged(playerPawnValue, "CBaseModelEntity", "m_clrRender");
            }

            if (!setWeaponsInvisible)
                return;

            var activeWeapon = playerPawnValue!.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && activeWeapon.IsValid)
            {
                activeWeapon.Render = Color.FromArgb(0, 0, 0, 0);
                activeWeapon.ShadowStrength = 0.0f;
                Utilities.SetStateChanged(activeWeapon, "CBaseModelEntity", "m_clrRender");
            }

            var myWeapons = playerPawnValue.WeaponServices?.MyWeapons;
            if (myWeapons != null)
            {
                foreach (var gun in myWeapons)
                {
                    var weapon = gun.Value;
                    if (weapon != null)
                    {
                        weapon.Render = Color.FromArgb(0, 0, 0, 0);
                        weapon.ShadowStrength = 0.0f;
                        Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");
                    }
                }
            }
        }

        public void SetPlayerVisible(CCSPlayerController player)
        {
            if (!IsPlayerValid(player))
                return;

            var playerPawnValue = player.PlayerPawn.Value;
            if (playerPawnValue == null)
                return;

            playerPawnValue.Render = Color.FromArgb(255, 255, 255, 255);
            Utilities.SetStateChanged(playerPawnValue, "CBaseModelEntity", "m_clrRender");
        }

        public void DropWeaponByDesignerName(CCSPlayerController player, string weaponName, bool remove = false)
        {
            if (!IsPlayerValid(player) || player.PlayerPawn.Value!.WeaponServices is null)
                return;
            var matchedWeapon = player.PlayerPawn.Value!.WeaponServices!.MyWeapons.Where(x => x.Value?.DesignerName == weaponName).FirstOrDefault();

            if (matchedWeapon != null && matchedWeapon.IsValid)
            {
                player.PlayerPawn.Value!.WeaponServices!.ActiveWeapon.Raw = matchedWeapon.Raw;
                player.DropActiveWeapon();
            }
        }

        // Dryrun-only: scatters players across all enabled spawns each dryrun round. Live
        // matches never call this (the matchzy_random_spawns convar and the 0.8.65 coach
        // auto-scatter were removed in 0.8.67 - competitive rounds always use engine spawns
        // plus the coach reseat in EnforceCompetitiveSpawns).
        public void RandomizeSpawns()
        {
            // Build per-team pools from ALL enabled spawn entities (GetTopCompetitiveSpawns with a
            // large cap), NOT the min-priority spawnsData set. spawnsData only holds the lowest-priority
            // spawns (e.g. Dust2 T = 5 of 10), so randomizing over it still reused the same 5 - the
            // "spawns are always the same" complaint. Fall back to spawnsData if the live scan fails.
            Dictionary<byte, List<Position>> teamSpawns = new();
            foreach (byte side in new[] { (byte)CsTeam.CounterTerrorist, (byte)CsTeam.Terrorist })
            {
                List<Position> pool = GetTopCompetitiveSpawns(side, 32);
                if (pool.Count == 0 && spawnsData.TryGetValue(side, out List<Position>? fallback))
                    pool = fallback.Select(p => new Position(p)).ToList();
                teamSpawns[side] = pool;
            }


            // Exclude coaches: they are placed at their own viewing spot by the coach system and
            // must NOT be teleported onto a competitive spawn (that both mis-seats the coach and eats
            // a spawn a real player needs). So a CT with 5 players + 1 coach still shuffles only the 5.
            HashSet<CCSPlayerController> coaches = GetAllCoaches();
            Random random = new();
            int moved = 0, skipped = 0;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(player) || coaches.Contains(player))
                    continue;
                if (!teamSpawns.TryGetValue(player.TeamNum, out List<Position>? pool) || pool.Count == 0)
                {
                    skipped++;
                    continue;
                }
                int randomIndex = random.Next(pool.Count);
                Position spawnPosition = pool[randomIndex];
                pool.RemoveAt(randomIndex); // claim so no two players share a spawn
                spawnPosition.Teleport(player);
                moved++;
            }
        }
    }
}
