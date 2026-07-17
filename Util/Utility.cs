using System;
using System.Collections.Generic;
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
                if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
                    continue;
                player.PrintToChat($"{chatPrefix} {Localizer.ForPlayer(player, key, args)}");
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
                        loadedAdmins[sid] = kvp.Value;
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
            if (loadedAdmins.ContainsKey(player.SteamID.ToString()))
                return true; // Admin exists in admins.json of MatchZy
            return false;
        }

        private int GetRealPlayersCount()
        {
            return playerData.Count;
        }

        private void SendUnreadyPlayersMessage()
        {
            if (!isWarmup || matchStarted)
                return;
            List<string> unreadyPlayers = new();

            foreach (var key in playerReadyStatus.Keys)
            {
                if (playerReadyStatus[key] == false)
                {
                    unreadyPlayers.Add(playerData[key].PlayerName);
                }
            }
            if (unreadyPlayers.Count > 0)
            {
                string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                string minimumReadyRequiredMessage = isMatchSetup ? "" : Localizer["matchzy.minimum.ready.required", $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}"];

                if (isRoundRestorePending)
                {
                    //PrintToAllChat(Localizer["matchzy.ready.readytotestorebackupinfomessage", unreadyPlayerList, minimumReadyRequiredMessage]);
                }
                else
                {
                    //PrintToAllChat(Localizer["matchzy.utility.unreadyplayers", unreadyPlayerList, minimumReadyRequiredMessage]);
                }
            }
            else
            {
                int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
                if (isMatchSetup)
                {
                    PrintToAllChat(Localizer["matchzy.utility.readyplayers", countOfReadyPlayers]);
                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, countOfReadyPlayers]);
                }
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

            if (unreadyPlayers.Count > 0)
            {
                string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                PrintWrappedLine(HudDestination.Center, $"NotReady: {unreadyPlayerList}");
            }
        }

        // Cached ready-panel DATA (recomputed only on change). Localized HTML is built per
        // player each tick so every player sees the panel in their own language.
        private int _rpReady, _rpRequired, _rpTotal, _rpFilled, _rpCtCount, _rpCtReady, _rpTCount, _rpTReady;
        private string _rpWaiting = "";
        private uint _readyTickCounter;
        // Last HTML sent to each player (by userid). The panel is only re-sent when its content
        // changes (or on a slow keepalive) so PrintToCenterHtml does not re-trigger its show
        // animation every tick - that per-tick re-fire is what makes the panel flash.
        private readonly Dictionary<int, string> _lastPanelHtml = new();
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

            _rpReady = readyCount;
            _rpTotal = totalPlayers;
            _rpRequired = minimumReadyRequired > 0 ? minimumReadyRequired : totalPlayers;
            _rpFilled = Math.Clamp((int)Math.Round(12.0 * readyCount / Math.Max(1, _rpRequired)), 0, 12);
            (_rpCtCount, _rpCtReady) = GetTeamPlayerCount((int)CsTeam.CounterTerrorist);
            (_rpTCount, _rpTReady) = GetTeamPlayerCount((int)CsTeam.Terrorist);

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

            // Classic plain-center message (style 0) - built here so the 1s timer just broadcasts it.
            string line1 = Localizer["matchzy.hint.waitingforplayers", _rpReady, _rpTotal];
            string line2 = Localizer["matchzy.hint.usereadycommand"];
            _cachedReadyHintMessage = _rpWaiting.Length > 0
                ? $"{line1}\n{line2}\nNotReady: {list}"
                : $"{line1}\n{line2}";

            _readyStatusDirty = false;
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
                HandleClanTags();

                if (readyHintStyle.Value == 0)
                    VirtualFunctions.ClientPrintAll(HudDestination.Center, _cachedReadyHintMessage, 0, 0, 0, 0, 0);
            }
            catch (Exception)
            {
                // Server not ready yet; retried on the next tick.
            }
        }

        // OnTick: re-send the center HTML panel EVERY tick so the native "WARMUP" HUD (from
        // mp_warmup_pausetimer) cannot flicker through it. Rendered per player and localized,
        // so each sees the panel in their own language with their OWN ready state highlighted.
        private void RenderReadyPanel()
        {
            if (!readyAvailable || matchStarted || readyHintStyle.Value != 1 || isDryRun)
            {
                // Left the ready phase (dryrun / classic style / match started) - drop the fake-warmup
                // override so it
                // never leaks into live play. Fires within a tick of matchStarted flipping, well
                // inside the knife/live freezetime, so the round conditions are normal before play.
                if (_fakeWarmupActive)
                {
                    Server.ExecuteCommand("mp_ignore_round_win_conditions 0; mp_respawn_on_death_ct 0; mp_respawn_on_death_t 0");
                    _fakeWarmupActive = false;
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
                    if (readyHideWarmupHud.Value && gr.WarmupPeriod)
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
                        bool expectedRestart = gr.RestartRoundTime < Server.CurrentTime;
                        if (gr.GameRestart != expectedRestart)
                        {
                            gr.GameRestart = expectedRestart;
                            changed = true;
                        }
                        // Freeze the round timer during the ready phase. Forcing warmup off (to hide
                        // the native banner) also drops mp_warmup_pausetimer, so the round timer would
                        // otherwise count down (the "warmup timer is back" bug). Keeping the round
                        // start time current each tick holds the displayed time constant = paused,
                        // matching what pausetimer does in real warmup.
                        if (_fakeWarmupActive)
                        {
                            gr.RoundStartTime = Server.CurrentTime;
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

                string bar =
                    $"<font class='fontSize-m' color='#37ff8b'>{new string('█', _rpFilled)}</font>" +
                    $"<font class='fontSize-m' color='#3a3a3a'>{new string('█', 12 - _rpFilled)}</font>";

                foreach (var target in Utilities.GetPlayers())
                {
                    if (target == null || !target.IsValid || target.IsBot || target.IsHLTV || !target.UserId.HasValue)
                        continue;

                    var sb = new StringBuilder();
                    // WARMUP badge: only when the native "WARMUP" banner is hidden (readyHideWarmupHud),
                    // the panel carries the warmup indicator itself. With the native banner shown
                    // (matchzy_ready_hide_warmup_hud 0) it already sits above the panel, so skip the
                    // badge to avoid a duplicate "WARMUP". Static (no per-tick change) so it does not
                    // defeat the below change-detection and re-trigger the show animation.
                    // Every localized/dynamic value goes through PanelSafe: CS2's center-HTML panel
                    // breaks on raw multibyte UTF-8 (Danish o-slash / a-ring, Albanian e-diaeresis),
                    // dropping that line and everything after it (e.g. the trailing NOT-READY line).
                    if (readyHideWarmupHud.Value)
                        sb.Append($"<font class='fontSize-l' color='#ff9a3c'>&#9679; {PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.warmuptag"))}</font><br>");
                    sb.Append($"<font class='fontSize-m' color='#ffcf3f'>{PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.title"))}</font><br>");
                    sb.Append($"<font class='fontSize-sm' color='#c8c8c8'>{PanelSafe(Localizer.ForPlayer(target, "matchzy.ready.mode", mode))}</font><br>");
                    sb.Append($"{bar} <font class='fontSize-m' color='#ffffff'>{_rpReady} / {_rpRequired}</font><br>");
                    sb.Append($"<font class='fontSize-sm' color='#9ecbff'>CT {_rpCtReady}/{_rpCtCount}</font><font class='fontSize-sm' color='#ffffff'> &nbsp; </font><font class='fontSize-sm' color='#ffb36b'>T {_rpTReady}/{_rpTCount}</font>");

                    // Self-status (YOU ARE (NOT) READY) is the most important line, so render it
                    // BEFORE the "waiting on" list. CS2's center-HTML panel has a size cap and drops
                    // the tail when a long localized string + player names push it over; keeping the
                    // status ahead of the expendable waiting-on line means the status always shows.
                    bool isPlaying = target.TeamNum == 2 || target.TeamNum == 3;
                    if (isPlaying)
                    {
                        if (playerReadyStatus.TryGetValue(target.UserId.Value, out bool isReady) && isReady)
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
                    int uid = target.UserId.Value;
                    if (!keepalive && _lastPanelHtml.TryGetValue(uid, out var prev) && prev == html)
                        continue;
                    _lastPanelHtml[uid] = html;
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
                    PrintToAllChat(Localizer["matchzy.pause.adminpausedthematch"]);
                }
                else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.pausedbecauserestore"]);
                }
                else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["TERRORIST"].teamName, reverseTeamSides["CT"].teamName]);
                }
                else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["CT"].teamName, reverseTeamSides["TERRORIST"].teamName]);
                }
                else if (!(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.pausedthematch", pauseTeamName]);
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
                    PrintToAllChat(Localizer["matchzy.pause.adminpausedthematch"]);
                }
                else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.pausedbecauserestore"]);
                }
                else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["TERRORIST"].teamName, reverseTeamSides["CT"].teamName]);
                }
                else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["CT"].teamName, reverseTeamSides["TERRORIST"].teamName]);
                }
            }
        }

        private void ExecWarmupCfg()
        {
            if (!warmupEnabled.Value)
            {
                return;
            }
            // Backup
            var absolutePath = Path.Join(Server.GameDirectory, "csgo", "cfg", warmupCfgPath);
            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath)))
            {
                Server.ExecuteCommand($"exec {warmupCfgPath}");
            }
            else
            {
                Server.ExecuteCommand("bot_kick;bot_quota 0;mp_autokick 0;mp_autoteambalance 0;mp_buy_anywhere 0;mp_buytime 15;mp_death_drop_gun 0;mp_free_armor 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_radar_showall 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_solid_teammates 0;mp_spectators_max 20;mp_maxmoney 16000;mp_startmoney 16000;mp_timelimit 0;sv_alltalk 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_deadtalk 1;sv_full_alltalk 0;sv_grenade_trajectory 0;sv_hibernate_when_empty 0;mp_weapons_allow_typecount -1;sv_infinite_ammo 0;sv_showimpacts 0;sv_voiceenable 1;sm_cvar sv_mute_players_with_social_penalties 0;sv_mute_players_with_social_penalties 0;tv_relayvoice 1;sv_cheats 0;mp_ct_default_melee weapon_knife;mp_ct_default_secondary weapon_hkp2000;mp_ct_default_primary \"\";mp_t_default_melee weapon_knife;mp_t_default_secondary weapon_glock;mp_t_default_primary \"\";mp_maxrounds 24;mp_warmuptime 9999;cash_team_bonus_shorthanded 0;mp_restartgame 1;mp_warmup_online_enabled 1;mp_warmup_start;mp_warmup_pausetimer 1");
            }
        }

        private void StartWarmup()
        {
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
                        var rules = GetGameRules();
                        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                        if (rules != null && proxy != null)
                        {
                            rules.MatchStats_RoundResults.Clear();
                            rules.MatchStats_PlayersAlive_CT.Clear();
                            rules.MatchStats_PlayersAlive_T.Clear();
                            Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
                        }

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

            // Ensure spectator limit is properly set
            Server.ExecuteCommand("mp_spectators_max 20");

            ClearClanTags();
        }

        private void StartKnifeRound()
        {
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
                Server.ExecuteCommand($"exec {knifeCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                Server.ExecuteCommand("mp_ct_default_secondary \"\";mp_free_armor 1;mp_freezetime 10;mp_give_player_c4 0;mp_maxmoney 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_t_default_secondary \"\";mp_round_restart_delay 3;mp_team_intro_time 0;mp_restartgame 1;mp_warmup_end;");
            }

            PrintToAllChat($"{ChatColors.Olive}KNIFE!");
            PrintToAllChat($"{ChatColors.Lime}KNIFE!");
            PrintToAllChat($"{ChatColors.Green}KNIFE!");
        }

        private void SendSideSelectionMessage()
        {
            if (!isSideSelectionPhase)
                return;
            PrintToAllChat(Localizer["matchzy.knife.sidedecisionpending", knifeWinnerName]);
        }

        private void StartAfterKnifeWarmup()
        {
            isWarmup = true;
            //DrawSideSelection();
            ExecWarmupCfg();
            knifeWinnerName = knifeWinner == 3 ? reverseTeamSides["CT"].teamName : reverseTeamSides["TERRORIST"].teamName;
            ShowDamageInfo();
            PrintToAllChat(Localizer["matchzy.knife.sidedecisionpending", knifeWinnerName]);
            sideSelectionMessageTimer ??= AddTimer(chatTimerDelay, SendSideSelectionMessage, TimerFlags.REPEAT);

            DrawSideSelection();
        }

        public void RoundStartMessage()
        {
            if (isKnifeRound)
            {
                PrintToAllChat(Localizer["matchzy.utility.roundknife"]);
                PrintToAllChat(Localizer["matchzy.utility.roundknife"]);
                PrintToAllChat(Localizer["matchzy.utility.roundknife"]);
            }
            else if (isMatchLive)
            {
                PrintToAllChat(Localizer["matchzy.utility.matchlive"]);
                PrintToAllChat(Localizer["matchzy.utility.matchlive"]);
                PrintToAllChat(Localizer["matchzy.utility.matchlive"]);
                PrintToAllChat($"{ChatColors.Green}.tac {ChatColors.Default} - Tactical pause (4 x 30 seconds per team)");
                PrintToAllChat($"{ChatColors.Green}.tech/.pause {ChatColors.Default} - Technical pause (indefinite)");

                var tvEnableConVar = _cvTvEnable;
                if (tvEnableConVar != null && tvEnableConVar.GetPrimitiveValue<bool>() == true)
                {
                    PrintToAllChat($"{ChatColors.Green}CSTV Recording...");
                }

                // Only show OT notice for match mode (scrim/hill have OT disabled)
                if (!isPlayOutEnabled && !isPlayOutEnabled2)
                {
                    PrintToAllChat($"{ChatColors.Green}Please be aware that this match has overtime enabled, there is no tie.");
                }
            }
        }

        private void SetLiveFlags()
        {
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
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
                            Log($"[MatchDebug] clinch={clinch} maxrounds={maxr} overtime={ot}");
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
                            Log($"[ScrimDebug] clinch={clinch} maxrounds={maxr} overtime={ot}");
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
            SetupLiveFlagsAndCfg();
            // Early clinch/overtime apply from live.cfg → forces UI to refresh trophy
            // icons when transitioning from scrim/hill back to match mode.
            Server.NextFrame(() => HandlePlayoutConfig());
            // mp_restartgame in live.cfg fires ~1s after exec; calling tv_record
            // synchronously here gets clobbered by the restart, producing empty
            // demos. Defer past the restart so tv_record sticks.
            AddTimer(2.0f, () => StartDemoRecording());
            ClearClanTags();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.json";

            PrintToAllChat($"{ChatColors.Olive}LIVE!");
            PrintToAllChat($"{ChatColors.Lime}LIVE!");
            PrintToAllChat($"{ChatColors.Green}LIVE!");

            PrintToAllChat($"{ChatColors.Green}.tac {ChatColors.Default} - Tactical pause (4 x 30 seconds per team)");
            PrintToAllChat($"{ChatColors.Green}.tech/.pause {ChatColors.Default} - Technical pause (indefinite)");

            // Only show OT notice for match mode (scrim/hill have OT disabled)
            if (!isPlayOutEnabled && !isPlayOutEnabled2)
            {
                PrintToAllChat($"{ChatColors.Green}Please be aware that this match has overtime enabled, there is no tie.");
            }

            var tvEnableConVar = _cvTvEnable;
            if (tvEnableConVar != null && tvEnableConVar.GetPrimitiveValue<bool>() == true)
            {
                PrintToAllChat($"{ChatColors.Green}CSTV Recording...");
            }

            var goingLiveEvent = new GoingLiveEvent { MatchId = liveMatchId, MapNumber = matchConfig.CurrentMapNumber };

            Task.Run(async () =>
            {
                await SendEventAsync(goingLiveEvent);
            });
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
            // mp_restartgame in scrim.cfg fires ~1s after exec; calling tv_record
            // synchronously here gets clobbered by the restart, producing empty
            // demos. Defer past the restart so tv_record sticks.
            AddTimer(2.0f, () => StartDemoRecording());
            ClearClanTags();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_data_backup_{liveMatchId}_{matchConfig.CurrentMapNumber}_round_00.json";

            PrintToAllChat($"{ChatColors.Olive}LIVE!");
            PrintToAllChat($"{ChatColors.Lime}LIVE!");
            PrintToAllChat($"{ChatColors.Green}LIVE!");

            PrintToAllChat($"{ChatColors.Green}.tac {ChatColors.Default} - Tactical pause (4 x 30 seconds per team)");
            PrintToAllChat($"{ChatColors.Green}.tech/.pause {ChatColors.Default} - Technical pause (indefinite)");

            var tvEnableConVar = _cvTvEnable;
            if (tvEnableConVar != null && tvEnableConVar.GetPrimitiveValue<bool>() == true)
            {
                PrintToAllChat($"{ChatColors.Green}CSTV Recording...");
            }

            var goingLiveEvent = new GoingLiveEvent { MatchId = liveMatchId, MapNumber = matchConfig.CurrentMapNumber };

            Task.Run(async () =>
            {
                await SendEventAsync(goingLiveEvent);
            });
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
            // mp_restartgame in hill.cfg fires ~1s after exec; calling tv_record
            // synchronously here gets clobbered by the restart, producing empty
            // demos. Defer past the restart so tv_record sticks.
            AddTimer(2.0f, () => StartDemoRecording());
            ClearClanTags();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_data_backup_{liveMatchId}_{matchConfig.CurrentMapNumber}_round_00.json";

            PrintToAllChat($"{ChatColors.Olive}HILL LIVE!");
            PrintToAllChat($"{ChatColors.Lime}HILL LIVE!");
            PrintToAllChat($"{ChatColors.Green}HILL LIVE!");

            PrintToAllChat($"{ChatColors.Green}.tac {ChatColors.Default} - Tactical pause (4 x 30 seconds per team)");
            PrintToAllChat($"{ChatColors.Green}.tech/.pause {ChatColors.Default} - Technical pause (indefinite)");

            var tvEnableConVar = _cvTvEnable;
            if (tvEnableConVar != null && tvEnableConVar.GetPrimitiveValue<bool>() == true)
            {
                PrintToAllChat($"{ChatColors.Green}CSTV Recording...");
            }

            var goingLiveEvent = new GoingLiveEvent { MatchId = liveMatchId, MapNumber = matchConfig.CurrentMapNumber };

            Task.Run(async () =>
            {
                await SendEventAsync(goingLiveEvent);
            });
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
                Log($"[ForceScoreboardRefresh] m_nRoundEndCount={rules.RoundEndCount}");
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
            unreadyPlayerMessageTimer = null;
            sideSelectionMessageTimer = null;
            pausedStateTimer = null;
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

        private void ResetMatch(bool warmupCfgRequired = true, string? cancelReason = null)
        {
            try
            {
                // Send match_cancelled event if match was live and we have a reason
                if (matchStarted && isMatchLive && cancelReason != null)
                {
                    (int t1score, int t2score) = GetTeamsScore();
                    string? demoFilename = !string.IsNullOrEmpty(activeDemoFile) ? Path.GetFileName(activeDemoFile) : null;

                    var cancelledEvent = new MatchCancelledEvent
                    {
                        MatchId = liveMatchId,
                        Reason = cancelReason,
                        DemoFilename = demoFilename,
                        Team1 = new MatchZyTeamWrapper(matchzyTeam1.id, matchzyTeam1.teamName),
                        Team2 = new MatchZyTeamWrapper(matchzyTeam2.id, matchzyTeam2.teamName),
                        Team1Score = t1score,
                        Team2Score = t2score,
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(cancelledEvent);
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
                isKnifeRequired = true;
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
                _readyStatusDirty = true;

                teamReadyOverride = new()
                {
                    { CsTeam.Terrorist, false },
                    { CsTeam.CounterTerrorist, false },
                    { CsTeam.Spectator, false },
                };

                HandleClanTags();
                Dictionary<string, object> unpauseData = new()
                {
                    { "ct", false },
                    { "t", false },
                    { "pauseTeam", "" },
                };

                stopData["ct"] = false;
                stopData["t"] = false;
                pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
                noFlashList = new();
                lastGrenadesData = new();
                nadeSpecificLastGrenadeData = new();
                UnpauseMatch();
                matchzyTeam1.teamName = "COUNTER-TERRORISTS";
                matchzyTeam2.teamName = "TERRORISTS";
                matchzyTeam1.teamPlayers = null;
                matchzyTeam2.teamPlayers = null;
                HashSet<CCSPlayerController> coaches = GetAllCoaches();

                foreach (var coach in coaches)
                {
                    if (!IsPlayerValid(coach))
                        continue;
                    coach.Clan = "";
                    SetPlayerVisible(coach);
                }

                matchzyTeam1.coach = new();
                matchzyTeam2.coach = new();
                coachKillTimer?.Kill();
                coachKillTimer = null;

                matchzyTeam1.seriesScore = 0;
                matchzyTeam2.seriesScore = 0;

                Server.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
                Server.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");

                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;

                matchConfig = new()
                {
                    RemoteLogURL = matchConfig.RemoteLogURL,
                    RemoteLogHeaderKey = matchConfig.RemoteLogHeaderKey,
                    RemoteLogHeaderValue = matchConfig.RemoteLogHeaderValue,
                    RemoteLogAuthKey = matchConfig.RemoteLogAuthKey,
                    RemoteLogAuthValue = matchConfig.RemoteLogAuthValue,
                };

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
            Log($"[KNIFE OVER] CT Alive: {ctAlive} with Total Health: {ctHealth}, T Alive: {tAlive} with Total Health: {tHealth}");
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
                ReplyToUserCommand(player, Localizer["matchzy.utility.matchstarted"]);
                return;
            }

            mapName = (mapName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(mapName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".map <map name/id>"));
                return;
            }

            // A purely numeric argument is a Steam Workshop published-file id.
            bool isWorkshopId = long.TryParse(mapName, out _);

            string targetMap = mapName.ToLower();
            if (!isWorkshopId)
            {
                // Resolve a NAMED map to one the server actually has BEFORE any teardown:
                // try the name as given, then a "de_" prefix so a bare "mirage" -> "de_mirage"
                // (but "cs_office"/"ar_baggage"/workshop-mounted names validate as-is). Upstream
                // stops the demo + kicks bots BEFORE validating, so a typo leaves the server torn
                // down with no map change and the recording lost - validate first, act second.
                if (!Server.IsMapValid(targetMap))
                {
                    string prefixed = "de_" + targetMap;
                    if (Server.IsMapValid(prefixed))
                    {
                        targetMap = prefixed;
                    }
                    else
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.cc.invalidmap"]);
                        return;
                    }
                }
            }

            // Debounce: on servers that add '.' as a chat trigger, one ".map" fires BOTH the .map
            // chat dispatch AND css_map, so HandleMapChangeCommand runs twice for one request.
            // Ignore a second request within 2s so the map does not change twice (a double
            // changelevel disconnects players: NETWORK_DISCONNECT_CREATE_SERVER_FAILED). Placed
            // after validation so a typo never blocks an immediate retry.
            if (Server.CurrentTime - _lastMapChangeRequestTime < 2.0f)
                return;
            _lastMapChangeRequestTime = Server.CurrentTime;

            // Validated named map (or a workshop id) - safe to tear down and change now.
            // Stop demo recording before map change to prevent GOTV crash.
            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
            }
            Server.ExecuteCommand("bot_kick");

            Log($"[MapChange] Changing map to '{targetMap}' (workshop={isWorkshopId})");
            PrintToAllChat(Localizer["matchzy.utility.changingmap", targetMap]);

            // Capture for lambda
            string finalMap = targetMap;
            bool finalIsWorkshop = isWorkshopId;
            Server.NextFrame(() =>
            {
                if (finalIsWorkshop)
                    Server.ExecuteCommand($"host_workshop_map \"{finalMap}\"");
                else
                    Server.ExecuteCommand($"changelevel \"{finalMap}\"");
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

        private void CheckLiveRequired()
        {
            if (!readyAvailable || matchStarted)
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

        private void HandleMatchStart()
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
                RestoreRoundBackup(null, pendingRestoreFileName);
                isRoundRestorePending = false;
                pendingRestoreFileName = "";
                matchStartInProgress = false;
                return;
            }

            // Get custom team names from config
            string customCTName = teamNameCt.Value?.Trim() ?? "";
            string customTName = teamNameT.Value?.Trim() ?? "";

            // Handle CT team naming
            if (matchzyTeam1.teamName == "COUNTER-TERRORISTS")
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
                    coach.Clan = $"[{matchzyTeam1.teamName} COACH]";
                }
            }

            // Handle T team naming
            if (matchzyTeam2.teamName == "TERRORISTS")
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
                    coach.Clan = $"[{matchzyTeam2.teamName} COACH]";
                }
            }

            Server.ExecuteCommand($"mp_teamname_1 {reverseTeamSides["CT"].teamName}");
            Server.ExecuteCommand($"mp_teamname_2 {reverseTeamSides["TERRORIST"].teamName}");

            HandleClanTags();

            string seriesType = "BO" + matchConfig.NumMaps.ToString();
            string mapName = Server.MapName;
            string serverIp = ConVar.Find("ip")?.StringValue ?? "0";

            // Capture all state needed for DB init, then run async to avoid blocking game thread
            string team1Name = matchzyTeam1.teamName;
            string team2Name = matchzyTeam2.teamName;
            bool matchSetup = isMatchSetup;
            long currentMatchId = liveMatchId;
            int currentMapNum = matchConfig.CurrentMapNumber;

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
                    liveMatchId = newMatchId;

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
                }); // Server.NextFrame
            }); // Task.Run
        }

        public void HandleClanTags(int? forceUpdateSlot = null)
        {
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
                    if (player == null || !player.IsValid || player.IsBot)
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
        // dirty m_szClanName and fire a per-client event that triggers a name/clan redraw.
        private void ApplyClanTag(CCSPlayerController player, string tag)
        {
            player.Clan = tag ?? "";
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClanName");
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

            gameRules.GameRules.NextUpdateTeamClanNamesTime = Server.CurrentTime - 0.01f;
            Utilities.SetStateChanged(gameRules, "CCSGameRules", "m_fNextUpdateTeamClanNamesTime");
        }

        private string GetPlayerClanTag(CCSPlayerController player, int userId)
        {
            if (readyAvailable && !matchStarted && !isPractice && !isDryRun)
            {
                return playerReadyStatus.TryGetValue(userId, out bool isReady) && isReady ? "[READY]" : "[UNREADY]";
            }

            return string.Empty;
        }

        private void HandleMatchEnd()
        {
            if (!isMatchLive)
                return;

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

            // Get demo filename
            string? demoFilename = !string.IsNullOrEmpty(activeDemoFile) ? Path.GetFileName(activeDemoFile) : null;

            var mapResultEvent = new MapResultEvent
            {
                MatchId = liveMatchId,
                MapNumber = currentMapNumber,
                Winner = new Winner(t1score > t2score && reverseTeamSides["CT"] == matchzyTeam1 ? "3" : "2", t1score > t2score ? "team1" : "team2"),
                StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, team1SeriesScore, t1score, 0, 0, playerStatsListTeam1),
                StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, team2SeriesScore, t2score, 0, 0, playerStatsListTeam2),
                DemoFilename = demoFilename,
            };

            // Collect match stats JSON on main thread (accesses native APIs like Server.MapName)
            MatchStatsJson? matchStatsForExport = CollectMatchStatsForExport(demoFilename ?? string.Empty, t1score, t2score, playerStatsListTeam1, playerStatsListTeam2);

            // Capture matchId before async context - liveMatchId may be reset to -1 by ResetMatch
            long matchId = liveMatchId;

            Task.Run(async () =>
            {
                await SendEventAsync(mapResultEvent);
                await database.SetMatchEndDataAsync(matchId, currentMapNumber, winnerName, t1score, t2score, winnerName, team1SeriesScore, team2SeriesScore);
                await database.WritePlayerStatsToCsvAsync(statsPath, matchId, currentMapNumber);

                // Write pre-collected HLTV-style JSON stats (file I/O only, no native calls)
                if (matchStatsForExport != null && demoFilename != null)
                {
                    await WriteMatchStatsJsonAsync(matchStatsForExport, demoFilename, statsPath);
                }
            });

            if (!isMatchSetup)
            {
                EndSeries(winnerName, restartDelay, t1score, t2score);
                return;
            }

            int remainingMaps = matchConfig.NumMaps - matchzyTeam1.seriesScore - matchzyTeam2.seriesScore;

            if (matchzyTeam1.seriesScore == matchzyTeam2.seriesScore && remainingMaps <= 0)
            {
                EndSeries(null, restartDelay, t1score, t2score);
                return;
            }
            else if (matchConfig.SeriesCanClinch)
            {
                int mapsToWinSeries = (matchConfig.NumMaps / 2) + 1;
                if (matchzyTeam1.seriesScore == mapsToWinSeries || matchzyTeam2.seriesScore == mapsToWinSeries)
                {
                    EndSeries(null, restartDelay, t1score, t2score);
                    return;
                }
            }
            else if (remainingMaps <= 0)
            {
                EndSeries(null, restartDelay, t1score, t2score);
                return;
            }

            if (matchzyTeam1.seriesScore > matchzyTeam2.seriesScore)
            {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} is winning the series {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");
            }
            else if (matchzyTeam2.seriesScore > matchzyTeam1.seriesScore)
            {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} is winning the series {ChatColors.Green}{matchzyTeam2.seriesScore}-{matchzyTeam1.seriesScore}{ChatColors.Default}");
            }
            else
            {
                Server.PrintToChatAll($"{chatPrefix} The series is tied at {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");
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

        private void ChangeMap(string mapName, float delay)
        {
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
                    Server.ExecuteCommand("bot_kick");

                    // Execute actual map change on next frame for engine state safety
                    Server.NextFrame(() =>
                    {
                        if (long.TryParse(mapName, out _))
                        {
                            Server.ExecuteCommand($"host_workshop_map \"{mapName}\"");
                        }
                        else if (Server.IsMapValid(mapName))
                        {
                            Server.ExecuteCommand($"changelevel \"{mapName}\"");
                        }
                        else
                        {
                            Log($"[ChangeMap] WARNING: Map '{mapName}' is not valid, cannot change!");
                        }
                    });
                }
            );
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
                if (isWarmup)
                    Server.ExecuteCommand("mp_warmup_online_enabled 1;mp_warmup_pausetimer 1");

                // Debug: exercise the coach-spawn flow during warmup so it can be tested with
                // bots without starting a full match. Only the coach handling runs here.
                if (coachDebugEnabled.Value && GetAllCoaches().Count > 0)
                    HandleCoaches();
                return;
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

            playerHasTakenDamage = false;
            HandleCoaches();
            CreateMatchZyRoundDataBackup();
            InitPlayerDamageInfo();
            UpdateHostname();
            // Set team names immediately
            SetTeamNames();
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

            // ── Live scorebot: round_start event ──
            if (!string.IsNullOrEmpty(matchConfig.RemoteLogURL))
            {
                var roundStartEvent = new RoundStartLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = GetRoundNumer(),
                };
                Task.Run(async () =>
                {
                    await SendEventAsync(roundStartEvent);
                });
            }
        }

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
                    Winner winner = new(@event.Winner.ToString(), t1score > t2score ? "team1" : "team2");

                    var roundEndEvent = new MatchZyRoundEndedEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        Reason = @event.Reason,
                        RoundTime = 0,
                        Winner = winner,
                        StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, 0, t1score, 0, 0, playerStatsListTeam1),
                        StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, 0, t2score, 0, 0, playerStatsListTeam2),
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(roundEndEvent);
                        var playerStatsDictInt = playerStatsDictionary.ToDictionary(kvp => (long)kvp.Key, kvp => kvp.Value);
                        await database.UpdatePlayerStatsAsync(matchId, currentMapNumber, playerStatsDictInt);
                    });

                    string round = GetRoundNumer().ToString("D2");
                    lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt";
                    lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    Log($"[HandlePostRoundEndEvent] Setting lastBackupFileName to {lastBackupFileName} and lastMatchZyBackupFileName to {lastMatchZyBackupFileName}");

                    // One of the team did not use .stop command hence display the proper message after the round has ended.
                    if (stopData["ct"] && !stopData["t"])
                    {
                        Server.PrintToChatAll($"{chatPrefix} The round restore request by {ChatColors.Green}{reverseTeamSides["CT"].teamName}{ChatColors.Default} was cancelled as the round ended");
                    }
                    else if (!stopData["ct"] && stopData["t"])
                    {
                        Server.PrintToChatAll($"{chatPrefix} The round restore request by {ChatColors.Green}{reverseTeamSides["TERRORIST"].teamName}{ChatColors.Default} was cancelled as the round ended");
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

            if (!techPauseEnabled.Value && player != null)
            {
                PrintToPlayerChat(player, Localizer["matchzy.pause.techpausenotenabled"]);
                return;
            }

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

                PrintToAllChat(Localizer["matchzy.pause.pausedthematch", pauseTeamName]);
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
            PrintToAllChat(Localizer["matchzy.pause.adminpausedthematch"]);
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
                PrintToAllChat(Localizer["matchzy.pause.adminunpausedthematch"]);
            Server.ExecuteCommand("mp_unpause_match");
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

                Task.Run(async () =>
                {
                    await SendEventAsync(unpauseEvent);
                });
            }
        }

        private void UnpauseMatch()
        {
            Server.ExecuteCommand("mp_unpause_match;");
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

                Task.Run(async () =>
                {
                    await SendEventAsync(unpauseEvent);
                });
            }
        }

        private void SetMatchPausedFlags(string pauseType = "tech")
        {
            coachKillTimer?.Kill();
            coachKillTimer = null;

            Server.ExecuteCommand("mp_pause_match;");
            isPaused = true;

            // Send webhook event for live scorebot
            string teamName = (string)(unpauseData["pauseTeam"] ?? "");
            int? maxDur = pauseType == "tech" ? techPauseDuration.Value : (int?)null;

            var pauseEvent = new MatchPausedLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                PauseType = pauseType,
                TeamName = string.IsNullOrEmpty(teamName) ? null : teamName,
                MaxDuration = maxDur,
                RoundNumber = GetRoundNumer(),
            };

            Task.Run(async () =>
            {
                await SendEventAsync(pauseEvent);
            });
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
                MaxDuration = techPauseDuration.Value,
                RoundNumber = GetRoundNumer(),
            };

            Task.Run(async () =>
            {
                await SendEventAsync(pauseEvent);
            });
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
                Server.ExecuteCommand($"exec {cfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                if (gameMode == 2)
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 16000;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 10;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 8000;");
                    Server.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 16000;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
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
                Server.ExecuteCommand($"exec {cfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                if (gameMode == 2)
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 10;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 8000;");
                    Server.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 0;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 0;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
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
                Server.ExecuteCommand($"exec {cfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                if (gameMode == 2)
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 10;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 8000;");
                    Server.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
        }

        private void SendPlayerNotAdminMessage(CCSPlayerController? player)
        {
            // ReplyToUserCommand(player, "You do not have permission to use this command!");
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.dontpermission"));
            ReplyToUserCommand(player, "You are not an admin. Make sure you has been added as Admin!");
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
                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Practice Mode Commands:");
                player.PrintToChat($" {ChatColors.Green}Spawns:{ChatColors.Default} .spawn .ctspawn .tspawn .bestspawn .worstspawn");
                player.PrintToChat($" {ChatColors.Green}Bots:{ChatColors.Default} .bot .cbot .boost .nobot .clearbots");
                player.PrintToChat($" {ChatColors.Green}Nades:{ChatColors.Default} .savenade .loadnade .listnades .rethrow .throwindex");
                player.PrintToChat($" {ChatColors.Green}Utility:{ChatColors.Default} .clear .ff .god .traj .impacts .break .cam .timer");
                player.PrintToChat($" {ChatColors.Green}Teams:{ChatColors.Default} .ct .t .spec .fas");
                if (isAdmin)
                {
                    player.PrintToChat($" {ChatColors.Red}Admin:{ChatColors.Default} .exitprac .match .scrim .dryrun");
                }
                player.PrintToChat($" {ChatColors.Grey}Full list in console → .mhelp");

                // Console output (unchanged - detailed practice docs)
                player.PrintToConsole("=== Practice Mode Command List ===\n");
                player.PrintToConsole("\n【Spawn Point Operations】\n" + ".spawn <number>  Teleport to the specified competitive spawn point of your team\n" + ".ctspawn <number>  Teleport to the specified CT competitive spawn point (alias: .cts)\n" + ".tspawn <number>  Teleport to the specified T competitive spawn point (alias: .ts)\n" + ".bestspawn  Teleport to the nearest team spawn point\n" + ".worstspawn  Teleport to the farthest team spawn point\n" + ".bestctspawn  Teleport to the nearest CT spawn point\n" + ".worstctspawn  Teleport to the farthest CT spawn point\n" + ".besttspawn  Teleport to the nearest T spawn point\n" + ".worsttspawn  Teleport to the farthest T spawn point\n" + ".showspawns  Highlight all competitive spawn points\n" + ".hidespawns  Hide highlighted spawn points\n");
                player.PrintToConsole("\n【Bot Control】\n" + ".bot  Add a bot at the player's current position\n" + ".crouchbot  Add a crouching bot at the player's current position (alias: .cbot)\n" + ".boost  Add a bot at the current position and boost the player on top of it\n" + ".crouchboost  Add a crouching bot and boost the player on top of it\n" + ".nobot  Remove the bot under the crosshair\n" + ".clearbots  Remove all bots\n");
                player.PrintToConsole("\n【Teams & Modes】\n" + ".ct, .t, .spec  Switch the player to the requested team\n" + ".fas /.watchme  Force all players into spectator mode except the one issuing the command\n" + ".dryrun  Enable Dryrun Mode (alias: .dry)\n" + ".god  Enable God Mode\n");
                player.PrintToConsole("\n【Grenade Management】\n" + ".savenade <n> <optional description>  Save a grenade crosshair (alias: .sn)\n" + ".loadnade <n>  Load a grenade crosshair (alias: .ln)\n" + ".deletenade <n>  Delete a saved grenade crosshair from file (alias: .dn)\n" + ".importnade <code>  Save a crosshair using a code printed in chat or from savednades.cfg (alias: .in)\n" + ".listnades <optional filter>  List all saved crosshairs, filter optional (alias: .lin)\n");
                player.PrintToConsole("\n【Grenade Throwing】\n" + ".rethrow  Re-throw your last thrown grenade (alias: .rt)\n" + ".last  Teleport to where you threw your last grenade\n" + ".back <number>  Teleport to a specific grenade history position\n" + ".delay <delay_in_seconds>  Set delay on last grenade (used with .rethrow or .throwindex)\n" + ".throwindex <index> <optional index> <optional index>  Throw grenade(s) from specific history index(es)\n" + ".lastindex  Print the index of your last thrown grenade\n" + ".rethrowsmoke  Throw your last smoke grenade\n" + ".rethrownade  Throw your last HE grenade\n" + ".rethrowflash  Throw your last flashbang\n" + ".rethrowmolotov  Throw your last molotov/incendiary\n" + ".rethrowdecoy  Throw your last decoy\n");
                player.PrintToConsole("\n【Utilities】\n" + ".clear  Clear all active smokes, molotovs, and incendiaries\n" + ".fastforward  Fast forward server time to 20 seconds (alias: .ff)\n" + ".noflash  Toggle flash immunity (players without noflash still get blinded, alias: .noblind)\n" + ".timer  Start a timer immediately; use .timer again to stop and show duration\n" + ".break  Break all breakable entities (windows, wooden doors, vents, etc.)\n" + ".nobreak  Restore all breakable entities ");
                player.PrintToConsole("\n【Display & Toggles】\n" + ".solid  Toggle mp_solid_teammates (teammate collision) - Current: " + ConVar.Find("mp_solid_teammates")!.GetPrimitiveValue<int>() + "\n" + ".impacts  Toggle sv_showimpacts (show bullet impacts) - Current: " + ConVar.Find("sv_showimpacts")!.GetPrimitiveValue<int>() + "\n" + ".traj  Toggle sv_grenade_trajectory_prac_pipreview (grenade trajectory preview) - Current: " + ConVar.Find("sv_grenade_trajectory_prac_pipreview")!.GetPrimitiveValue<bool>() + "\n");
                return;
            }

            // ── DRY RUN ──
            if (isDryRun)
            {
                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Dryrun Mode:");
                player.PrintToChat($" {ChatColors.Green}Exit:{ChatColors.Default} .exitdry .stopdry .enddry");
                if (isAdmin)
                {
                    player.PrintToChat($" {ChatColors.Red}Admin:{ChatColors.Default} .match .prac");
                }
                return;
            }

            // ── VETO ──
            if (isVeto)
            {
                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Map Veto in progress:");
                player.PrintToChat($" {ChatColors.Green}Ban/Pick:{ChatColors.Default} .ban <map> .pick <map>");
                player.PrintToChat($" {ChatColors.Default}Only team captains can ban/pick.");
                return;
            }

            // ── WARMUP (not ready phase) ──
            if (isWarmup && !readyAvailable)
            {
                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Warmup:");
                player.PrintToChat($" {ChatColors.Default}.match {ChatColors.Green}Match Mode");
                player.PrintToChat($" {ChatColors.Default}.scrim {ChatColors.Green}Playout/Scrim Mode");
                player.PrintToChat($" {ChatColors.Default}.prac {ChatColors.Green}Practice Mode");
                player.PrintToChat($" {ChatColors.Default}.dry {ChatColors.Green}Dryrun Mode");
                return;
            }

            // ── WAITING FOR READY ──
            if (readyAvailable && !matchStarted)
            {
                (int ctCount, int ctReady) = GetTeamPlayerCount((int)CsTeam.CounterTerrorist);
                (int tCount, int tReady) = GetTeamPlayerCount((int)CsTeam.Terrorist);

                int totalReady = playerReadyStatus.Count(kv => kv.Value);
                var unready = playerReadyStatus
                    .Where(kv => !kv.Value && playerData.ContainsKey(kv.Key))
                    .Select(kv => playerData[kv.Key].PlayerName)
                    .ToList();
                string knifeStatus = isKnifeRequired ? $"{ChatColors.Green}ON{ChatColors.Default}" : $"{ChatColors.Red}OFF{ChatColors.Default}";

                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Waiting for players to ready up");
                player.PrintToChat($" {ChatColors.Green}.ready{ChatColors.Default} to ready up, {ChatColors.Green}.unready{ChatColors.Default} to cancel");

                if (minimumReadyRequired > 0)
                {
                    int need = Math.Max(0, minimumReadyRequired - totalReady);
                    string needTxt = need > 0
                        ? $"{ChatColors.Red}need {need} more{ChatColors.Default}"
                        : $"{ChatColors.Green}ready to start!{ChatColors.Default}";
                    player.PrintToChat($" Ready: {ChatColors.Green}{totalReady}/{minimumReadyRequired}{ChatColors.Default} ({needTxt}) | CT {ctReady}/{ctCount} | T {tReady}/{tCount}");
                }
                else
                {
                    player.PrintToChat($" Ready: CT {ChatColors.Green}{ctReady}/{ctCount}{ChatColors.Default} | T {ChatColors.Green}{tReady}/{tCount}{ChatColors.Default} {ChatColors.Grey}(all players must ready)");
                }

                player.PrintToChat($" Knife: {knifeStatus}");

                if (unready.Count > 0)
                {
                    string list = string.Join(", ", unready.Take(6));
                    if (unready.Count > 6)
                        list += $" +{unready.Count - 6}";
                    player.PrintToChat($" {ChatColors.Grey}Not ready: {list}");
                }

                if (isAdmin)
                {
                    player.PrintToChat($" {ChatColors.Red}Admin:{ChatColors.Default} .match .scrim .prac .knife .forceready .force");
                }
                return;
            }

            // ── KNIFE ROUND - SIDE SELECTION ──
            if (isSideSelectionPhase)
            {
                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Knife Winner - Pick your side:");
                player.PrintToChat($" {ChatColors.Green}.stay{ChatColors.Default} - Keep current side");
                player.PrintToChat($" {ChatColors.Green}.switch{ChatColors.Default} - Swap sides");
                player.PrintToChat($" {ChatColors.Green}.ct{ChatColors.Default} / {ChatColors.Green}.t{ChatColors.Default} - Choose specific side");
                return;
            }

            // ── MATCH LIVE - PAUSED ──
            if (matchStarted && isMatchLive && isPaused)
            {
                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Match Paused:");
                player.PrintToChat($" {ChatColors.Green}.unpause{ChatColors.Default} - Request unpause (both teams must agree)");
                if (isAdmin)
                {
                    player.PrintToChat($" {ChatColors.Red}Admin:{ChatColors.Default} .fup (force unpause) .restore <round> .backupmenu");
                }
                return;
            }

            // ── MATCH LIVE - PLAYING ──
            if (matchStarted && isMatchLive)
            {
                player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Match Live:");
                player.PrintToChat($" {ChatColors.Green}Pause:{ChatColors.Default} .pause .tac .tech");
                if (isStopCommandAvailable)
                {
                    player.PrintToChat($" {ChatColors.Green}Round:{ChatColors.Default} .stop (restore round - both teams agree)");
                }
                if (isAdmin)
                {
                    player.PrintToChat($" {ChatColors.Red}Admin:{ChatColors.Default} .fp (force pause) .restore <round> .backupmenu");
                }
                return;
            }

            // ── FALLBACK ──
            player!.PrintToChat($"{chatPrefix} No commands available in current state.");
        }

        private void SendAdminCommandsGuide(CCSPlayerController? player)
        {
            if (!IsPlayerValid(player))
                return;

            // Concise categorized summary in CHAT (admins rarely open console). The full
            // detailed reference still goes to console below.
            player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Admin Commands");
            player.PrintToChat($" {ChatColors.Green}Modes:{ChatColors.Default} .match  .scrim  .prac  .dry  .warmup");
            player.PrintToChat($" {ChatColors.Green}Setup:{ChatColors.Default} .ma (menu)  .matchsetup  .map <name>  .teamsize <n>  .knife");
            player.PrintToChat($" {ChatColors.Green}Control:{ChatColors.Default} .start  .restart  .stop  .end  .restore <round>  .backupmenu");
            player.PrintToChat($" {ChatColors.Green}Pause:{ChatColors.Default} .fp (force pause)  .fup (force unpause)  .tac  .tech");
            player.PrintToChat($" {ChatColors.Grey}Full detailed list in console (press ` and scroll up).");

            // Send to console for detailed view
            player.PrintToConsole("\n" + new string('=', 50));
            player.PrintToConsole("MATCHZY ADMIN COMMANDS GUIDE");
            player.PrintToConsole(new string('=', 50) + "\n");

            // GENERAL/CONFIG COMMANDS
            player.PrintToConsole($"\n{ChatColors.Green}【GENERAL/CONFIG COMMANDS】{ChatColors.Default}");
            player.PrintToConsole("css_roundknife / css_rk / css_kr / css_kniferound - Toggle knife round requirement");
            player.PrintToConsole("css_teamsize - Set number of players required to ready (default: 10)");
            player.PrintToConsole("css_options / css_settings / css_configs - Show current match configuration");
            player.PrintToConsole("css_autopause / css_autopause_minplayers / css_autopause_delay - Configure auto-pause");
            player.PrintToConsole("css_autopause_status / css_autopause_check - Check auto-pause settings");
            player.PrintToConsole("css_version / css_matchzy_version - Display MatchZy version");

            // MATCH MODE COMMANDS
            player.PrintToConsole($"\n{ChatColors.Green}【MATCH MODE COMMANDS】{ChatColors.Default}");
            player.PrintToConsole("css_match - Start match mode");
            player.PrintToConsole("css_scrim / css_playout / css_po - Start scrim/playout mode (all rounds)");
            player.PrintToConsole("css_warmup - Start warmup mode");
            player.PrintToConsole("css_prac / css_tactics - Start practice mode");
            player.PrintToConsole("css_dry / css_dryrun - Start dryrun mode");
            player.PrintToConsole("css_exitprac / css_noprac - Exit practice mode to warmup");
            player.PrintToConsole("css_exitdry / css_exitdryrun / css_stopdry / css_enddry - Exit dryrun mode");

            // READY & SIDE SELECTION
            player.PrintToConsole($"\n{ChatColors.Green}【READY & SIDE SELECTION】{ChatColors.Default}");
            player.PrintToConsole("css_rc / css_rcheck / css_readycheck - Check ready player count");
            player.PrintToConsole("css_forceready - Force a team to be ready");
            player.PrintToConsole("css_ready / css_gaben / .ready - Mark yourself ready");
            player.PrintToConsole("css_unready / css_ur / css_notready - Mark yourself unready");
            player.PrintToConsole("css_ct / .ct - Choose CT side after knife round");
            player.PrintToConsole("css_t / .t - Choose T side after knife round");
            player.PrintToConsole("css_stay - Stay on current side after knife round");
            player.PrintToConsole("css_switch / css_swap - Switch sides after knife round");

            // PAUSE & UNPAUSE
            player.PrintToConsole($"\n{ChatColors.Green}【PAUSE/UNPAUSE COMMANDS】{ChatColors.Default}");
            player.PrintToConsole("css_pause / css_p - Team pause (both teams must unpause)");
            player.PrintToConsole("css_tech - Technical pause (consumes technical pause timeout)");
            player.PrintToConsole("css_unpause / css_up / css_r - Request unpause");
            player.PrintToConsole("css_fp / css_forcepause - Admin: Force pause match");
            player.PrintToConsole("css_fup / css_forceunpause - Admin: Force unpause match");
            player.PrintToConsole("css_tac - Tactical timeout for requested team");

            // MATCH CONTROL
            player.PrintToConsole($"\n{ChatColors.Green}【MATCH CONTROL】{ChatColors.Default}");
            player.PrintToConsole("css_start / css_force / css_forcestart - Force start the match");
            player.PrintToConsole("css_r - Ready up before match or unpause during match");
            player.PrintToConsole("css_restart / css_abort - Restart the match");
            player.PrintToConsole("css_stop - Request round restore (restores to beginning of round)");
            player.PrintToConsole("css_stopgame / css_stopmatch / css_endgame / css_forcestop / css_endmatch / css_forceend / css_end / css_exitscrim - End and reset match");
            player.PrintToConsole("css_asay - Say message as admin");

            // PRACTICE MODE SPECIFIC
            player.PrintToConsole($"\n{ChatColors.Green}【PRACTICE MODE COMMANDS】{ChatColors.Default}");
            player.PrintToConsole("📍 SPAWN OPERATIONS:");
            player.PrintToConsole("   .spawn <#> / .ctspawn <#> / .tspawn <#> - Teleport to spawn");
            player.PrintToConsole("   .bestspawn / .worstspawn - Teleport to nearest/farthest spawn");
            player.PrintToConsole("   .bestctspawn / .worstctspawn / .besttspawn / .worsttspawn");
            player.PrintToConsole("   .showspawns / .hidespawns - Show/hide spawn highlights");
            player.PrintToConsole("🤖 BOT CONTROL:");
            player.PrintToConsole("   .bot / .cbot / .crouchbot - Add bots");
            player.PrintToConsole("   .boost / .crouchboost - Add bot and boost player");
            player.PrintToConsole("   .nobot / .clearbots - Remove bots");
            player.PrintToConsole("💣 GRENADE MANAGEMENT:");
            player.PrintToConsole("   .savenade / .sn - Save grenade crosshair");
            player.PrintToConsole("   .loadnade / .ln - Load grenade crosshair");
            player.PrintToConsole("   .deletenade / .dn - Delete saved grenade");
            player.PrintToConsole("   .importnade / .in - Import grenade from code");
            player.PrintToConsole("   .listnades / .lin - List all saved grenades");
            player.PrintToConsole("📍 GRENADE THROWING:");
            player.PrintToConsole("   .rethrow / .rt - Re-throw last grenade");
            player.PrintToConsole("   .last / .back <#> - Teleport to grenade location");
            player.PrintToConsole("   .throwindex <#> / .lastindex - Throw specific grenade");
            player.PrintToConsole("   .delay <seconds> - Set grenade throw delay");
            player.PrintToConsole("   .rethrowsmoke / .rethrownade / .rethrowflash / .rethrowmolotov / .rethrowdecoy");
            player.PrintToConsole("🔧 UTILITIES:");
            player.PrintToConsole("   .clear - Clear all smokes/molotovs");
            player.PrintToConsole("   .fastforward / .ff - Jump to 20 seconds");
            player.PrintToConsole("   .noflash / .noblind - Toggle flash immunity");
            player.PrintToConsole("   .timer - Start/stop timer");
            player.PrintToConsole("   .break / .nobreak - Break/restore breakables");
            player.PrintToConsole("🎮 TOGGLES & SIDES:");
            player.PrintToConsole("   .solid - Toggle teammate collision");
            player.PrintToConsole("   .impacts - Toggle bullet impacts");
            player.PrintToConsole("   .traj - Toggle grenade trajectory");
            player.PrintToConsole("   .nadecam - Toggle grenade camera");
            player.PrintToConsole("   .savepos / .loadpos - Save/load position");
            player.PrintToConsole("   .ct / .t / .spec - Switch teams");
            player.PrintToConsole("   .fas / .watchme - Force all spectators");
            player.PrintToConsole("   .god - Enable god mode");

            // MODE-SPECIFIC AVAILABILITY
            player.PrintToConsole($"\n{ChatColors.Green}【MODE AVAILABILITY】{ChatColors.Default}");
            player.PrintToConsole("Practice Mode: Full command access including spawns, bots, nades, etc.");
            player.PrintToConsole("Warmup Mode: .match, .scrim, .prac, .dry, knife round toggle");
            player.PrintToConsole("Ready Phase: .ready, .unready");
            player.PrintToConsole("Knife Round: .stay, .switch, .ct, .t (side selection)");
            player.PrintToConsole("Live Match: .pause, .unpause, .tac, .tech, .stop (if enabled)");
            player.PrintToConsole("Dryrun Mode: .exitdry, .stopdry, .enddry");

            player.PrintToConsole("\n" + new string('=', 50) + "\n");
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
            if (players == null)
                return;
            foreach (JProperty player in players)
            {
                string steamId = player.Name;
                string escapedName = player.Value.ToString().Replace("\"", "\\\"").Trim();

                if (string.IsNullOrEmpty(escapedName))
                    continue;

                sb.AppendLine($"\t\"{steamId}\"\t\t\"{escapedName}\"");
            }
        }

        static bool IsValidUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? result))
            {
                return result != null && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
            }

            return false;
        }

        public string GetConvarStringValue(ConVar? cvar)
        {
            try
            {
                if (cvar == null)
                    return "";
                string convarValue = cvar.Type switch
                {
                    ConVarType.Bool => cvar.GetPrimitiveValue<bool>().ToString(),
                    ConVarType.Float32 or ConVarType.Float64 => cvar.GetPrimitiveValue<float>().ToString(),
                    ConVarType.UInt16 => cvar.GetPrimitiveValue<ushort>().ToString(),
                    ConVarType.Int16 => cvar.GetPrimitiveValue<short>().ToString(),
                    ConVarType.UInt32 => cvar.GetPrimitiveValue<uint>().ToString(),
                    ConVarType.Int32 => cvar.GetPrimitiveValue<int>().ToString(),
                    ConVarType.Int64 => cvar.GetPrimitiveValue<long>().ToString(),
                    ConVarType.UInt64 => cvar.GetPrimitiveValue<ulong>().ToString(),
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
                { ConVarType.Float32, v => cvar.SetValue(Convert.ToSingle(v)) },
                { ConVarType.Float64, v => cvar.SetValue(Convert.ToSingle(v)) },
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

        public void ExecuteChangedConvars()
        {
            foreach (string key in matchConfig.ChangedCvars.Keys)
            {
                string value = matchConfig.ChangedCvars[key];
                Server.ExecuteCommand($"{key} \"{value}\"");
            }
        }

        public void ResetChangedConvars()
        {
            foreach (string key in matchConfig.OriginalCvars.Keys)
            {
                string value = matchConfig.OriginalCvars[key];
                Server.ExecuteCommand($"{key} {value}");
            }
        }

        public string FormatCvarValue(string value)
        {
            string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            (int team1Score, int team2Score) = GetTeamsScore();

            var formattedValue = value.Replace("{TIME}", formattedTime.Replace(" ", "_")).Replace("{MATCH_ID}", $"{liveMatchId}").Replace("{MAP}", Server.MapName).Replace("{MAPNUMBER}", matchConfig.CurrentMapNumber.ToString()).Replace("{TEAM1}", matchzyTeam1.teamName.Replace(" ", "_")).Replace("{TEAM2}", matchzyTeam2.teamName.Replace(" ", "_")).Replace("{TEAM1_SCORE}", team1Score.ToString()).Replace("{TEAM2_SCORE}", team2Score.ToString());
            return formattedValue;
        }

        public void UpdateHostname()
        {
            string hostname = hostnameFormat.Value.Trim();
            if (hostname == "" || hostname == "\"\"")
                return;
            string formattedHostname = FormatCvarValue(hostname);
            Server.ExecuteCommand($"hostname {formattedHostname}");
        }

        // Returns null when the cs_gamerules entity is momentarily absent (map /
        // round / phase transitions). The old .First() threw InvalidOperationException
        // there, crashing the server mid-match. Callers MUST null-check.
        public CCSGameRules? GetGameRules()
        {
            return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
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

        public (Dictionary<ulong, Dictionary<string, object>>, List<StatsPlayer>, List<StatsPlayer>) GetPlayerStatsDict()
        {
            Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary = new Dictionary<ulong, Dictionary<string, object>>();
            List<StatsPlayer> playerStatsListTeam1 = new();
            List<StatsPlayer> playerStatsListTeam2 = new();
            var gameRules = GetGameRules();
            int roundsPlayed = gameRules?.TotalRoundsPlayed ?? 0;
            try
            {
                foreach (int key in playerData.Keys)
                {
                    CCSPlayerController player = playerData[key];
                    if (!player.IsValid || player.ActionTrackingServices == null)
                        continue;

                    var playerStats = player.ActionTrackingServices.MatchStats;
                    ulong steamid64 = player.SteamID;

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

                    playerStatsDictionary.Add(steamid64, stats);

                    // Populate PlayerStats instance
                    // Todo: Implement stats which are marked as 0 for now
                    PlayerStats playerStatsInstance = new()
                    {
                        Kills = playerStats.Kills,
                        Deaths = playerStats.Deaths,
                        Assists = playerStats.Assists,
                        FlashAssists = 0,
                        TeamKills = 0,
                        Suicides = 0,
                        Damage = playerStats.Damage,
                        UtilityDamage = playerStats.UtilityDamage,
                        EnemiesFlashed = playerStats.EnemiesFlashed,
                        FriendliesFlashed = 0,
                        KnifeKills = 0,
                        HeadshotKills = playerStats.HeadShotKills,
                        RoundsPlayed = roundsPlayed,
                        BombDefuses = 0,
                        BombPlants = 0,
                        Kills1 = 0,
                        Kills2 = playerStats.Enemy2Ks,
                        Kills3 = playerStats.Enemy3Ks,
                        Kills4 = playerStats.Enemy4Ks,
                        Kills5 = playerStats.Enemy5Ks,
                        OneV1s = playerStats.I1v1Wins,
                        OneV2s = playerStats.I1v2Wins,
                        OneV3s = 0,
                        OneV4s = 0,
                        OneV5s = 0,
                        FirstKillsT = 0,
                        FirstKillsCT = 0,
                        FirstDeathsT = 0,
                        FirstDeathsCT = 0,
                        TradeKills = 0,
                        Kast = 0,
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

        private void AutoStart()
        {
            // Per-map latch: AutoStart is triggered from multiple sites (Load timer, OnMapStart,
            // first player connect). Run at most once per map load; latch is re-armed in OnMapStart.
            if (autoStartLatched)
            {
                //Log($"[AutoStart] skipped duplicate (autoStartMode: {autoStartMode})");
                return;
            }
            autoStartLatched = true;

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
            Utilities.SetStateChanged(pawn, "CCSPlayer_WeaponServices", "m_hActiveWeapon");
            return true;
        }

        // Engine weapon-select (CCSPlayer_WeaponServices::SelectItem(this, weapon,
        // subType) - vtable slot 28/linux, 27/windows). This is the ONLY way to
        // deploy an already-owned weapon: it holsters the current weapon and
        // deploys the target (redraws viewmodel + plays the deploy anim, which
        // clears the frozen throw pose), with no GiveNamedItem (no-op when owned)
        // and no entity deletion (crashes). The vtable index comes from the forked
        // CounterStrikeSharp's gamedata.json key "CCSPlayer_WeaponServices_SelectItem"
        // (offset entry). Cached lazily; -1 = unavailable -> caller falls back to a
        // pointer switch (e.g. on stock CSS without the entry).
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

        public static string? GetConvarValueFromCFGFile(string filePath, string convarName)
        {
            var fileContent = File.ReadAllText(filePath);

            string pattern = @$"^{convarName}\s+(.+)$";

            Regex regex = new(pattern, RegexOptions.Multiline);

            Match match = regex.Match(fileContent);
            string? value = match.Success ? match.Groups[1].Value : null;
            return value;
        }

        public async Task UploadFileAsync(string? filePath, string fileUploadURL, string headerKey, string headerValue, long matchId, int mapNumber, int roundNumber)
        {
            if (filePath == null || fileUploadURL == "")
            {
                Log($"[UploadFileAsync] Not able to upload the file, either filePath or fileUploadURL is not set. filePath: {filePath} fileUploadURL: {fileUploadURL}");
                return;
            }

            try
            {
                Log($"[UploadFileAsync] Going to upload the file on {fileUploadURL}. Complete path: {filePath}");

                if (!File.Exists(filePath))
                {
                    Log($"[UploadFileAsync ERROR] File not found: {filePath}");
                    return;
                }

                byte[] fileContent = await File.ReadAllBytesAsync(filePath);

                using var request = new HttpRequestMessage(HttpMethod.Post, fileUploadURL);
                using var content = new ByteArrayContent(fileContent);
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
                HttpResponseMessage response = await _sharedHttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Log($"[UploadFileAsync] File upload successful for matchId: {matchId} mapNumber: {mapNumber} fileName: {Path.GetFileName(filePath)}.");
                }
                else
                {
                    Log($"[UploadFileAsync ERROR] Failed to upload file. Status code: {response.StatusCode} Response: {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception e)
            {
                Log($"[UploadFileAsync FATAL] An error occurred: {e.Message}");
            }
        }

        public bool HandlePlayerWhitelist(CCSPlayerController player, string steamId)
        {
            string whitelistfileName = MatchZyCfgRel("whitelist.cfg");
            string whitelistPath = Path.Join(Server.GameDirectory + "/csgo/cfg", whitelistfileName);
            string? directoryPath = Path.GetDirectoryName(whitelistPath);
            if (directoryPath != null)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
            if (!File.Exists(whitelistPath))
                File.WriteAllLines(whitelistPath, new[] { "Steamid1", "Steamid2" });

            var whiteList = File.ReadAllLines(whitelistPath);

            if (isWhitelistRequired == true)
            {
                if (!whiteList.Contains(steamId.ToString()))
                {
                    Log($"[EventPlayerConnectFull] KICKING PLAYER STEAMID: {steamId}, Name: {player.PlayerName} (Not whitelisted!)");
                    PrintToAllChat($"Kicking player {player.PlayerName} - Not whitelisted.");
                    return true;
                }
            }

            return false;
        }

        public void SwitchPlayerTeam(CCSPlayerController player, CsTeam team)
        {
            if (player.Team == team)
                return;

            Server.NextFrame(() =>
            {
                if (team == CsTeam.Spectator)
                {
                    player.ChangeTeam(team);
                }
                else
                {
                    player.SwitchTeam(team);
                    var gameRules = GetGameRules();
                    if (gameRules != null && gameRules.WarmupPeriod)
                    {
                        player.Respawn();
                    }
                }
            });
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

        public void RandomizeSpawns()
        {
            List<CCSPlayerController> players = Utilities.GetPlayers();
            Dictionary<byte, List<Position>> teamSpawns = new() { { (byte)CsTeam.CounterTerrorist, spawnsData[(byte)CsTeam.CounterTerrorist].Select(position => new Position(position)).ToList() }, { (byte)CsTeam.Terrorist, spawnsData[(byte)CsTeam.Terrorist].Select(position => new Position(position)).ToList() } };
            Random random = new();
            foreach (var player in players)
            {
                if (!IsPlayerValid(player))
                    continue;

                if (teamSpawns[player.TeamNum].Count == 0)
                    break;
                int randomIndex = random.Next(teamSpawns[player.TeamNum].Count);
                Position spawnPosition = teamSpawns[player.TeamNum][randomIndex];
                teamSpawns[player.TeamNum].RemoveAt(randomIndex);
                spawnPosition.Teleport(player);
            }
        }
    }
}
