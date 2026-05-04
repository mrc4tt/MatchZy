using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
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
            public int NumMaps = 1;
            public bool SkipVeto = true;
            public bool KnifeRound = true;
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
                ReplyToUserCommand(player, "A match is already running. Use .stopmatch first.");
                return;
            }
            if (isMatchSetup)
            {
                ReplyToUserCommand(
                    player,
                    $"A match is already configured (id {liveMatchId}). Use .stopmatch first."
                );
                return;
            }
            if (mapRotationList.Count == 0)
            {
                ReplyToUserCommand(
                    player,
                    "matchzymaps.cfg has no maps — add some before running .matchsetup."
                );
                return;
            }
            if (activeSetup != null && activeSetup.AdminSteamId != player.SteamID)
            {
                ReplyToUserCommand(
                    player,
                    "Another admin is already in the setup wizard. Wait for them to finish or cancel."
                );
                return;
            }

            activeSetup = new MatchSetupState { AdminSteamId = player.SteamID };
            // Defer to next frame so menu render isn't clobbered when entered via .chat dispatch
            // (the originating chat line is still being broadcast on this tick).
            Server.NextFrame(() => OpenSeriesMenu(player));
        }

        // ─── Wizard menus ──────────────────────────────────────────────────────────

        private void OpenSeriesMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var menu = new WasdMenu($"{chatPrefix} Match Setup — Series", this);
            menu.AddItem(
                "Best of 1 (BO1)",
                (p, _) =>
                {
                    activeSetup!.NumMaps = 1;
                    activeSetup.SelectedMaps.Clear();
                    OpenMapModeMenu(p);
                }
            );
            menu.AddItem(
                "Best of 3 (BO3)",
                (p, _) =>
                {
                    activeSetup!.NumMaps = 3;
                    activeSetup.SelectedMaps.Clear();
                    OpenMapModeMenu(p);
                }
            );
            menu.AddItem(
                "Best of 5 (BO5)",
                (p, _) =>
                {
                    activeSetup!.NumMaps = 5;
                    activeSetup.SelectedMaps.Clear();
                    OpenMapModeMenu(p);
                }
            );
            menu.AddItem("Cancel Setup", (p, _) => CancelSetup(p));
            menu.Display(player, 0);
        }

        private void OpenMapModeMenu(CCSPlayerController player)
        {
            if (!ValidateSetupOwner(player))
                return;
            var s = activeSetup!;
            int poolSize = mapRotationList.Count;
            var menu = new WasdMenu($"{chatPrefix} Map Selection (BO{s.NumMaps})", this);
            menu.AddItem("« Back", (p, _) => OpenSeriesMenu(p));
            menu.AddItem(
                $"Pre-pick {s.NumMaps} map(s) — no veto",
                (p, _) =>
                {
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
                        s.SkipVeto = false;
                        s.SelectedMaps = new List<string>(mapRotationList);
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
            var menu = new WasdMenu($"{chatPrefix} Pick Maps — {s.SelectedMaps.Count}/{s.NumMaps}", this);
            foreach (var map in mapRotationList)
            {
                int pickIdx = s.SelectedMaps.IndexOf(map);
                string label = pickIdx >= 0 ? $"[{pickIdx + 1}] {map}" : $"[ ] {map}";
                string capturedMap = map;
                menu.AddItem(
                    label,
                    (p, _) =>
                    {
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
            var menu = new WasdMenu($"{chatPrefix} Side Selection", this);
            menu.AddItem(
                "Knife round decides sides",
                (p, _) =>
                {
                    activeSetup!.KnifeRound = true;
                    OpenConfirmMenu(p);
                }
            );
            menu.AddItem(
                "Skip knife (Team1 starts CT)",
                (p, _) =>
                {
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

            var menu = new WasdMenu($"{chatPrefix} Confirm Match", this);
            menu.AddItem($"BO{s.NumMaps} — change", (p, _) => OpenSeriesMenu(p));
            string mapSummary = s.SkipVeto
                ? $"Maps ({s.SelectedMaps.Count}/{s.NumMaps}): {string.Join(" → ", s.SelectedMaps)}"
                : $"Veto pool: {s.SelectedMaps.Count} maps";
            menu.AddItem(mapSummary, (p, _) => OpenMapModeMenu(p));
            menu.AddItem(
                $"Sides: {(s.KnifeRound ? "Knife" : "Team1 CT")} — change",
                (p, _) => OpenSidesMenu(p)
            );
            menu.AddItem(
                $"Teams: {team1} vs {team2}",
                (p, o) =>
                {
                    ReplyToUserCommand(
                        p,
                        "Type .team1 <name> / .team2 <name> in chat — menu refreshes automatically."
                    );
                    o.PostSelectAction = CS2MenuManager.API.Enum.PostSelectAction.Nothing;
                }
            );
            menu.AddItem(
                $"{ChatColors.Green}>> START MATCH",
                (p, _) => FinalizeMatchSetup(p)
            );
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

            string team1 = ResolveTeam1Name();
            string team2 = ResolveTeam2Name();

            // Don't set "matchid" — InitMatchAsync allocates a fresh parent row when liveMatchId is -1.
            // Passing a synthetic id makes the maps-table insert violate the FK to matchzy_stats_matches.
            var json = new JObject
            {
                ["num_maps"] = s.NumMaps,
                ["maplist"] = new JArray(s.SelectedMaps),
                ["skip_veto"] = s.SkipVeto,
                ["clinch_series"] = true,
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
                ReplyToUserCommand(player, $"Setup failed: {ex.Message}");
                return;
            }

            if (ok)
            {
                PrintToAllChat(
                    $"{ChatColors.Green}Match configured by admin: BO{s.NumMaps} ({(s.SkipVeto ? "no veto" : "veto")})"
                );
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    "LoadMatchFromJSON returned false — check server log for validation errors."
                );
            }
        }

        private void CancelSetup(CCSPlayerController player)
        {
            activeSetup = null;
            ReplyToUserCommand(player, "Match setup cancelled.");
        }

        private bool ValidateSetupOwner(CCSPlayerController player)
        {
            if (activeSetup == null)
            {
                ReplyToUserCommand(player, "No active match setup. Type .matchsetup to start.");
                return false;
            }
            if (activeSetup.AdminSteamId != player.SteamID)
            {
                ReplyToUserCommand(player, "This setup belongs to another admin.");
                return false;
            }
            return true;
        }

        private string ResolveTeam1Name()
        {
            string fromConvar = teamNameCt.Value?.Trim() ?? "";
            if (!string.IsNullOrEmpty(fromConvar))
                return fromConvar;
            if (
                !string.IsNullOrEmpty(matchzyTeam1.teamName)
                && matchzyTeam1.teamName != "COUNTER-TERRORISTS"
            )
                return matchzyTeam1.teamName;
            return "Team1";
        }

        private string ResolveTeam2Name()
        {
            string fromConvar = teamNameT.Value?.Trim() ?? "";
            if (!string.IsNullOrEmpty(fromConvar))
                return fromConvar;
            if (
                !string.IsNullOrEmpty(matchzyTeam2.teamName)
                && matchzyTeam2.teamName != "TERRORISTS"
            )
                return matchzyTeam2.teamName;
            return "Team2";
        }
    }
}
