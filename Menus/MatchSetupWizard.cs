using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    public partial class MatchZy
    {
        private class MatchSetupState
        {
            public ulong AdminSteamId;
            // Last time the owner used the wizard; an idle wizard stops blocking other admins.
            public DateTime StartedAt = DateTime.UtcNow;
            public int NumMaps = 1;
            public bool SkipVeto = true;
            public bool KnifeRound = true;
            // "competitive", "wingman", "rush" or "1v1" (MatchGameModes).
            public string GameMode = "competitive";
            public List<string> SelectedMaps = new();
        }

        private MatchSetupState? activeSetup;

        [ConsoleCommand("css_matchsetup", "Opens the in-game match config builder")]
        public void OnMatchSetupCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
            {
                ReplyToUserCommand(player, "matchsetup must be run from a client (chat menu).");
                return;
            }
            if (!IsPlayerAdmin(player, "css_matchsetup", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (matchStarted || isMatchLive)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupmatchrunning"));
                return;
            }
            if (isMatchSetup)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupalreadyconfigured", liveMatchId));
                return;
            }
            if (mapRotationList.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupnomaps"));
                return;
            }
            // The lock used to be released only by Cancel / Start / Back, so an admin who closed the
            // menu (ESC, timeout) or left the server blocked the wizard for everyone until a plugin
            // reload. Treat it as abandoned when its owner is gone or it has been idle for a while.
            if (activeSetup != null && activeSetup.AdminSteamId != player.SteamID)
            {
                bool ownerOnline = Utilities.GetPlayers().Any(p => p.IsValid && !p.IsBot && p.SteamID == activeSetup.AdminSteamId);
                if (!ownerOnline || DateTime.UtcNow - activeSetup.StartedAt > TimeSpan.FromMinutes(3))
                    activeSetup = null;
            }
            if (activeSetup != null && activeSetup.AdminSteamId != player.SteamID)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupinuse"));
                return;
            }

            activeSetup = new MatchSetupState { AdminSteamId = player.SteamID };
            // Defer to next frame so menu render isn't clobbered when entered via .chat dispatch
            // (the originating chat line is still being broadcast on this tick).
            // Re-validated: the player can leave before the next frame.
            Server.NextFrame(() => { if (IsPlayerValid(player)) OpenMenuGuarded(player, OpenSeriesMenu); });
        }

        // ─── Wizard menus ──────────────────────────────────────────────────────────

        private void OpenSeriesMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var menu = new WasdMenu($"Match Setup - Series", this);
            menu.AddItem(
                "Best of 1 (BO1)",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    activeSetup!.NumMaps = 1;
                    activeSetup.SelectedMaps.Clear();
                    OpenGameModeMenu(p);
                }
            );
            menu.AddItem(
                "Best of 2 (BO2)",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    activeSetup!.NumMaps = 2;
                    activeSetup.SelectedMaps.Clear();
                    OpenGameModeMenu(p);
                }
            );
            menu.AddItem(
                "Best of 3 (BO3)",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    activeSetup!.NumMaps = 3;
                    activeSetup.SelectedMaps.Clear();
                    OpenGameModeMenu(p);
                }
            );
            menu.AddItem(
                "Best of 5 (BO5)",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    activeSetup!.NumMaps = 5;
                    activeSetup.SelectedMaps.Clear();
                    OpenGameModeMenu(p);
                }
            );
            // Series is the top wizard menu (no PrevMenu), so add an explicit way back to the css_ma
            // admin menu. Abandons the in-progress setup (clears activeSetup) - re-enter via New Match Setup.
            menu.AddItem(
                "« Back to Admin Menu",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    activeSetup = null;
                    OpenMatchAdminMenu(p);
                }
            );
            menu.AddItem("Cancel Setup", (p, _) => CancelSetup(p));
            menu.Display(player, 0);
        }

        private void OpenGameModeMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var menu = new WasdMenu($"Match Setup - Game Mode", this);
            menu.AddItem("« Back", (p, _) => OpenSeriesMenu(p));
            foreach (var (mode, label) in new[]
            {
                ("competitive", "Competitive (5v5)"),
                ("wingman", "Wingman (2v2)"),
                ("rush", "Rush (3v3)"),
                ("1v1", "1v1"),
            })
            {
                string capturedMode = mode;
                menu.AddItem(
                    label,
                    (p, _) =>
                    {
                        // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                        if (!ValidateSetupOwner(p))
                            return;
                        activeSetup!.GameMode = capturedMode;
                        activeSetup.SelectedMaps.Clear();
                        OpenMapModeMenu(p);
                    }
                );
            }
            menu.AddItem("Cancel Setup", (p, _) => CancelSetup(p));
            menu.Display(player, 0);
        }

        // The maps the wizard offers for the chosen mode: rush only on rush maps (rush_001 when the
        // rotation has none), the other modes on the rest of the rotation.
        private List<string> SetupMapPool(MatchSetupState s)
        {
            if (s.GameMode == "rush")
            {
                var rushMaps = mapRotationList.Where(m => IsRushMap(m)).ToList();
                return rushMaps.Count > 0 ? rushMaps : new List<string> { "rush_001" };
            }
            var maps = mapRotationList.Where(m => !IsRushMap(m)).ToList();
            return maps.Count > 0 ? maps : new List<string>(mapRotationList);
        }

        private void OpenMapModeMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var s = activeSetup!;
            var pool = SetupMapPool(s);
            int poolSize = pool.Count;
            var menu = new WasdMenu($"Map Selection (BO{s.NumMaps})", this);
            menu.AddItem("« Back", (p, _) => OpenGameModeMenu(p));
            menu.AddItem(
                $"Pre-pick {s.NumMaps} map(s) - no veto",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    s.SkipVeto = true;
                    s.SelectedMaps.Clear();
                    OpenMapPickMenu(p);
                }
            );
            if (poolSize > s.NumMaps)
            {
                menu.AddItem(
                    $"Veto from full pool ({poolSize} maps)",
                    (p, _) =>
                    {
                        // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                        if (!ValidateSetupOwner(p))
                            return;
                        s.SkipVeto = false;
                        s.SelectedMaps = new List<string>(pool);
                        OpenSidesMenu(p);
                    }
                );
            }
            menu.AddItem("Cancel Setup", (p, _) => CancelSetup(p));
            menu.Display(player, 0);
        }

        private void OpenMapPickMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var s = activeSetup!;
            var menu = new WasdMenu($"Pick Maps - {s.SelectedMaps.Count}/{s.NumMaps}", this);
            foreach (var map in SetupMapPool(s))
            {
                int pickIdx = s.SelectedMaps.IndexOf(map);
                string label = pickIdx >= 0 ? $"[{pickIdx + 1}] {map}" : $"[ ] {map}";
                string capturedMap = map;
                menu.AddItem(
                    label,
                    (p, _) =>
                    {
                        // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                        if (!ValidateSetupOwner(p))
                            return;
                        if (s.SelectedMaps.Contains(capturedMap))
                            s.SelectedMaps.Remove(capturedMap);
                        else if (s.SelectedMaps.Count < s.NumMaps)
                            s.SelectedMaps.Add(capturedMap);
                        OpenMapPickMenu(p);
                    }
                );
            }
            if (s.SelectedMaps.Count == s.NumMaps)
            {
                menu.AddItem(">> Continue", (p, _) => OpenSidesMenu(p));
            }
            menu.AddItem("« Back", (p, _) => OpenMapModeMenu(p));
            menu.AddItem("Cancel Setup", (p, _) => CancelSetup(p));
            menu.Display(player, 0);
        }

        private void OpenSidesMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var menu = new WasdMenu($"Side Selection", this);
            menu.AddItem(
                "Knife round decides sides",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    activeSetup!.KnifeRound = true;
                    OpenConfirmMenu(p);
                }
            );
            menu.AddItem(
                "Skip knife (Team1 starts CT)",
                (p, _) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    activeSetup!.KnifeRound = false;
                    OpenConfirmMenu(p);
                }
            );
            menu.AddItem("« Back", (p, _) => OpenMapModeMenu(p));
            menu.AddItem("Cancel Setup", (p, _) => CancelSetup(p));
            menu.Display(player, 0);
        }

        private void OpenConfirmMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var s = activeSetup!;
            string team1 = ResolveTeam1Name();
            string team2 = ResolveTeam2Name();

            var menu = new WasdMenu($"Confirm Match", this);
            menu.AddItem($"BO{s.NumMaps} - change", (p, _) => OpenSeriesMenu(p));
            menu.AddItem($"Mode: {s.GameMode} - change", (p, _) => OpenGameModeMenu(p));
            string mapSummary = s.SkipVeto ? $"Maps ({s.SelectedMaps.Count}/{s.NumMaps}): {string.Join(" → ", s.SelectedMaps)}" : $"Veto pool: {s.SelectedMaps.Count} maps";
            menu.AddItem(mapSummary, (p, _) => OpenMapModeMenu(p));
            menu.AddItem($"Sides: {(s.KnifeRound ? "Knife" : "Team1 CT")} - change", (p, _) => OpenSidesMenu(p));
            menu.AddItem(
                $"Teams: {team1} vs {team2}",
                (p, o) =>
                {
                    // The wizard can be reclaimed by another admin; a stale menu must not touch it.
                    if (!ValidateSetupOwner(p))
                        return;
                    ReplyToUserCommand(p, Localizer.ForPlayer(p, "matchzy.menu.setupteamnamehint"));
                    o.PostSelectAction = CS2MenuManager.API.Enum.PostSelectAction.Nothing;
                }
            );
            menu.AddItem($"{ChatColors.Green}>> START MATCH", (p, _) => FinalizeMatchSetup(p));
            menu.AddItem("Cancel Setup", (p, _) => CancelSetup(p));
            menu.Display(player, 0);
        }

        // ─── Finalize ──────────────────────────────────────────────────────────────

        private void FinalizeMatchSetup(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var s = activeSetup!;
            activeSetup = null;

            // Another admin's .match or a panel load may have set up a match while the wizard was open.
            if (matchStarted || isMatchLive)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupmatchrunning"));
                return;
            }
            if (isMatchSetup)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupalreadyconfigured", liveMatchId));
                return;
            }

            string team1 = ResolveTeam1Name();
            string team2 = ResolveTeam2Name();

            // Don't set "matchid" - InitMatchAsync allocates a fresh parent row when liveMatchId is -1.
            // Passing a synthetic id makes the maps-table insert violate the FK to matchzy_stats_matches.
            var json = new JObject
            {
                ["num_maps"] = s.NumMaps,
                ["maplist"] = new JArray(s.SelectedMaps),
                ["skip_veto"] = s.SkipVeto,
                ["clinch_series"] = true,
                ["game_mode"] = s.GameMode,
                ["team1"] = new JObject { ["name"] = team1, ["players"] = new JObject() },
                ["team2"] = new JObject { ["name"] = team2, ["players"] = new JObject() },
            };

            if (s.SkipVeto && !s.KnifeRound)
            {
                var sides = new JArray();
                for (int i = 0; i < s.NumMaps; i++)
                    sides.Add("team1_ct");
                json["map_sides"] = sides;
            }
            else if (!s.SkipVeto && !s.KnifeRound)
            {
                // With a veto, map_sides is not used: the veto's side picks and a knife on the decider
                // decide. never_knife makes every map team1 CT, which is what "Team1 CT" promises.
                json["side_type"] = "never_knife";
            }

            string jsonText = json.ToString();
            Log($"[MatchSetup] Loading match: {jsonText}");

            bool ok;
            try
            {
                ok = LoadMatchFromJSON(jsonText);
            }
            catch (Exception ex)
            {
                Log($"[MatchSetup FATAL] {ex.Message}");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupfailed", ex.Message));
                return;
            }

            if (ok)
            {
                PrintLocalizedToAll(s.SkipVeto ? "matchzy.menu.setupconfigurednoveto" : "matchzy.menu.setupconfiguredveto", s.NumMaps);
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setuploadfailed"));
            }
        }

        private void CancelSetup(CCSPlayerController player)
        {
            // Only the owner may cancel: a stale menu of an admin whose lock was reclaimed must not
            // wipe the new owner's setup.
            if (!ValidateSetupOwner(player))
                return;
            activeSetup = null;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupcancelled"));
        }

        private bool ValidateSetupOwner(CCSPlayerController player)
        {
            if (activeSetup == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupnoactive"));
                return false;
            }
            if (activeSetup.AdminSteamId != player.SteamID)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.setupnotowner"));
                return false;
            }
            activeSetup.StartedAt = DateTime.UtcNow;
            return true;
        }

        private string ResolveTeam1Name()
        {
            string fromConvar = teamNameCt.Value?.Trim() ?? "";
            if (!string.IsNullOrEmpty(fromConvar))
                return fromConvar;
            if (!string.IsNullOrEmpty(matchzyTeam1.teamName) && matchzyTeam1.teamName != "COUNTER-TERRORISTS")
                return matchzyTeam1.teamName;
            return "Team1";
        }

        private string ResolveTeam2Name()
        {
            string fromConvar = teamNameT.Value?.Trim() ?? "";
            if (!string.IsNullOrEmpty(fromConvar))
                return fromConvar;
            if (!string.IsNullOrEmpty(matchzyTeam2.teamName) && matchzyTeam2.teamName != "TERRORISTS")
                return matchzyTeam2.teamName;
            return "Team2";
        }
    }
}
