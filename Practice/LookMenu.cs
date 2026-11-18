using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Vector3 = System.Numerics.Vector3;

namespace MatchZy
{
    // Practice, made simple to use (after a study of how other lineup plugins handle it):
    //   - a menu that needs no menu plugin and no key binds: look up or down, E to pick,
    //   - "type it in chat": the next chat line of the player is taken as the answer,
    //   - the lineup the player aims at is named on screen; E goes there, holding E edits it,
    //   - the lineup marks are on for everyone as soon as practice runs,
    //   - .train: the saved lineups one after the other, judged, with a summary.
    public partial class MatchZy
    {
        // ---- look menu ------------------------------------------------------------------------
        private sealed class LookMenu
        {
            public string Title = "";
            public List<(string Label, Action<CCSPlayerController> Run)> Options = new();
            public int Index;
            public int OpenTick;
            public bool UseWasDown, Armed;
            // W / S as they were last tick, and when the held one repeats next.
            public bool UpWasDown, DownWasDown;
            public int RepeatTick;
        }

        private readonly Dictionary<int, LookMenu> lookMenus = new();
        // Held W / S: the first repeat after a third of a second, then eight rows a second.
        private const int LookMenuRepeatFirst = 22;
        private const int LookMenuRepeatNext = 8;
        private const int LookMenuDebounceTicks = 40;
        private const int LookMenuTimeoutTicks = 64 * 25;

        private void OpenLookMenu(CCSPlayerController player, string title, List<(string Label, Action<CCSPlayerController> Run)> options)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid)
                return;
            options.Add((Localizer.ForPlayer(player, "matchzy.pp.menu.cancel"), _ => { }));
            lookMenus[player.Slot] = new LookMenu
            {
                Title = title, Options = options, Index = 0, OpenTick = Server.TickCount,
                UseWasDown = (player.Buttons & PlayerButtons.Use) != 0,
                // Keys held when the menu opens do not move the cursor until let go and pressed again.
                UpWasDown = (player.Buttons & PlayerButtons.Forward) != 0,
                DownWasDown = (player.Buttons & PlayerButtons.Back) != 0,
            };
        }

        // No walking while a menu is open. The game's own slow-down value (the one a hit sets)
        // held at zero: it comes back by itself, so a menu that goes away without this being
        // undone cannot leave a player frozen.
        private static void SetLookMenuFreeze(CCSPlayerPawn pawn, bool frozen)
        {
            try
            {
                pawn.VelocityModifier = frozen ? 0f : 1f;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
            catch
            {
            }
        }

        private bool LookMenuOpen(int slot) => lookMenus.ContainsKey(slot) || chatAsks.ContainsKey(slot);

        private static string LookHtml(string text) => System.Net.WebUtility.HtmlEncode(text);

        private void TickLookMenus()
        {
            if (lookMenus.Count == 0)
                return;
            int tick = Server.TickCount;
            foreach (var (slot, menu) in lookMenus.ToList())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !IsPlayerValid(player) || pawn == null || !pawn.IsValid || !isPractice || tick - menu.OpenTick > LookMenuTimeoutTicks)
                {
                    lookMenus.Remove(slot);
                    if (pawn != null && pawn.IsValid)
                        SetLookMenuFreeze(pawn, false);
                    continue;
                }
                int n = menu.Options.Count;
                // W / S move the cursor (the author's call 2026-11-18; before that the cursor
                // followed the view up and down). One row per press, repeating while held, and
                // round from the last row to the first. The player stands still meanwhile: the
                // same keys would walk them off.
                SetLookMenuFreeze(pawn, true);
                bool up = (player.Buttons & PlayerButtons.Forward) != 0;
                bool down = (player.Buttons & PlayerButtons.Back) != 0;
                int step = 0;
                if (up && !menu.UpWasDown) { step = -1; menu.RepeatTick = tick + LookMenuRepeatFirst; }
                else if (down && !menu.DownWasDown) { step = 1; menu.RepeatTick = tick + LookMenuRepeatFirst; }
                else if (up != down && tick >= menu.RepeatTick && (up ? menu.UpWasDown : menu.DownWasDown))
                {
                    step = up ? -1 : 1;
                    menu.RepeatTick = tick + LookMenuRepeatNext;
                }
                menu.UpWasDown = up;
                menu.DownWasDown = down;
                if (step != 0)
                    menu.Index = ((menu.Index + step) % n + n) % n;
                int index = Math.Clamp(menu.Index, 0, n - 1);
                bool use = (player.Buttons & PlayerButtons.Use) != 0;
                bool ready = tick - menu.OpenTick > LookMenuDebounceTicks;
                if (use && !menu.UseWasDown && ready)
                    menu.Armed = true; // a press of its own, not the one that opened the menu
                if (!use && menu.UseWasDown && menu.Armed)
                {
                    lookMenus.Remove(slot);
                    SetLookMenuFreeze(pawn, false);
                    player.PrintToCenterHtml(" ");
                    var run = menu.Options[index].Run;
                    Server.NextFrame(() =>
                    {
                        var p = Utilities.GetPlayerFromSlot(slot);
                        if (p != null && IsPlayerValid(p) && isPractice)
                            run(p);
                    });
                    continue;
                }
                menu.UseWasDown = use;
                if (tick % 2 != 0)
                    continue;
                var html = new System.Text.StringBuilder();
                html.Append("<font color='#00C850'><b>").Append(LookHtml(menu.Title)).Append("</b></font><br>");
                int first = Math.Clamp(index - 2, 0, Math.Max(0, n - 5));
                int last = Math.Min(n - 1, first + 4);
                if (first > 0)
                    html.Append("<font color='#666666'>").Append(Localizer.ForPlayer(player, "matchzy.pp.menu.more_up", first)).Append("</font><br>");
                for (int i = first; i <= last; i++)
                    html.Append(i == index
                        ? "<font color='#FFFFFF'><b>► " + LookHtml(menu.Options[i].Label) + "</b></font><br>"
                        : "<font color='#B4B4B4'>" + LookHtml(menu.Options[i].Label) + "</font><br>");
                if (last < n - 1)
                    html.Append("<font color='#666666'>").Append(Localizer.ForPlayer(player, "matchzy.pp.menu.more_down", n - 1 - last)).Append("</font><br>");
                html.Append("<font color='#666666'>").Append(Localizer.ForPlayer(player, "matchzy.pp.menu.footer")).Append("</font>");
                player.PrintToCenterHtml(html.ToString());
            }
        }

        // ---- "type it in chat" ----------------------------------------------------------------
        private readonly Dictionary<int, (string Label, Action<CCSPlayerController, string> Done, int UntilTick)> chatAsks = new();

        private void AskChatLine(CCSPlayerController player, string label, Action<CCSPlayerController, string> done)
        {
            chatAsks[player.Slot] = (label, done, Server.TickCount + 64 * 30);
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.ask.prompt", label));
        }

        private void RegisterLookMenu()
        {
            AddCommandListener("say", OnLookMenuSay, HookMode.Pre);
            AddCommandListener("say_team", OnLookMenuSay, HookMode.Pre);
        }

        // The next chat line of a player who was asked for one is the answer, not a chat message.
        private HookResult OnLookMenuSay(CCSPlayerController? player, CommandInfo info)
        {
            try
            {
                if (player == null || !player.IsValid || !chatAsks.Remove(player.Slot, out var ask))
                    return HookResult.Continue;
                string text = (info.ArgString ?? "").Trim().Trim('"').Trim();
                int slot = player.Slot;
                player.PrintToCenterHtml(" ");
                if (text.Length == 0 || text.Equals("cancel", StringComparison.OrdinalIgnoreCase) || text.StartsWith('.') || text.StartsWith('!'))
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.cancelled"));
                    // A command typed instead of an answer still runs as a command.
                    return text.StartsWith('.') || text.StartsWith('!') ? HookResult.Continue : HookResult.Handled;
                }
                Server.NextFrame(() =>
                {
                    var p = Utilities.GetPlayerFromSlot(slot);
                    if (p != null && IsPlayerValid(p) && isPractice)
                        ask.Done(p, text);
                });
                return HookResult.Handled;
            }
            catch (Exception e)
            {
                Log($"[LookMenu] say: {e.Message}");
                return HookResult.Continue;
            }
        }

        private void TickChatAsks()
        {
            if (chatAsks.Count == 0 || Server.TickCount % 4 != 0)
                return;
            foreach (var (slot, ask) in chatAsks.ToList())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                int left = (ask.UntilTick - Server.TickCount) / 64;
                if (player == null || !IsPlayerValid(player) || left <= 0)
                {
                    chatAsks.Remove(slot);
                    continue;
                }
                player.PrintToCenterHtml("<font color='#00C850'><b>" + Localizer.ForPlayer(player, "matchzy.pp.menu.ask.center_title", LookHtml(ask.Label))
                    + "</b></font><br><font color='#B4B4B4'>" + Localizer.ForPlayer(player, "matchzy.pp.menu.ask.center_cancel")
                    + "</font><br><font color='#666666'>" + Localizer.ForPlayer(player, "matchzy.pp.menu.ask.center_left", left) + "</font>");
            }
        }

        // ---- lineup marks: on by themselves, named when aimed at, hold E to edit ---------------
        // Off (the author's call 2026-11-17): marks come with .shownades as they always did. A
        // player with saved lineups on the map gets one chat line about it instead.
        private const int NadeHoldTicks = 64; // E held this long on a mark = edit it
        private readonly HashSet<ulong> nadeAutoShown = new();
        // When a mark was last named on screen for a player: the armed lineup's line waits meanwhile
        // (one box, not two: the author's call).
        private readonly Dictionary<int, int> nadeHoverTick = new();
        private readonly Dictionary<int, (int Tick, NadeLineup Lineup, bool Done)> nadeUseHold = new();

        private void TickLineupMarks()
        {
            int tick = Server.TickCount;
            // The marks are there without anyone asking (once per player and map; .hidenades stays off).
            if (tick % 64 == 0)
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot || !player.PawnIsAlive || player.TeamNum < 2
                        || !nadeAutoShown.Add(player.SteamID) || _nadeViewers.Contains(player.SteamID))
                        continue;
                    try
                    {
                        int saved = LoadLineupsForCurrentMap(player).Count;
                        if (saved == 0)
                            continue;
                        if (practiceAutoShowLineups.Value)
                            ShowNadesWithFilter(player, "");
                        else
                            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.saved_hint", saved));
                    }
                    catch (Exception e)
                    {
                        Log($"[LookMenu] auto show: {e.Message}");
                    }
                }

            // Held long enough on a mark: its edit menu.
            foreach (var (slot, hold) in nadeUseHold.ToList())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player == null || !IsPlayerValid(player) || (player.Buttons & PlayerButtons.Use) == 0)
                    continue; // the release is handled by the button listener
                if (hold.Done || tick - hold.Tick < NadeHoldTicks)
                    continue;
                nadeUseHold[slot] = (hold.Tick, hold.Lineup, true);
                OpenLineupEditMenu(player, hold.Lineup);
            }

            // What the player aims at, by name, a few times a second.
            if (!grenadeLibraryActive || tick % 6 != 0)
                return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot || !player.PawnIsAlive || !_nadeViewers.Contains(player.SteamID)
                    || LookMenuOpen(player.Slot) || drawSessions.ContainsKey(player.Slot))
                    continue;
                int gi = AimedGroupIndex(player);
                if (gi >= 0 || AimedLandingSpot(player) != null)
                    nadeHoverTick[player.Slot] = tick; // the one box on screen is the mark's for now
                if (gi >= 0)
                {
                    var g = activeNadeGroups[gi];
                    var lu = g.Lineups[g.Current];
                    string many = g.Lineups.Count > 1 ? Localizer.ForPlayer(player, "matchzy.pp.menu.hover_many", g.Current + 1, g.Lineups.Count) : "";
                    string howThrown = lu.Throw != "" ? " · " + lu.Throw : "";
                    player.PrintToCenterAlert(Localizer.ForPlayer(player, "matchzy.pp.menu.hover", lu.Name, lu.Type, howThrown, many));
                    continue;
                }
                var spot = AimedLandingSpot(player);
                if (spot != null)
                    player.PrintToCenterAlert(spot.Lineups.Count == 1
                        ? Localizer.ForPlayer(player, "matchzy.pp.menu.lands_one", spot.Lineups[0].Name)
                        : Localizer.ForPlayer(player, "matchzy.pp.menu.lands_many", spot.Lineups.Count));
            }
        }

        private void OpenLineupEditMenu(CCSPlayerController player, NadeLineup lu)
        {
            if (lu.Global)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.edit.shared", lu.Name));
                return;
            }
            string name = lu.Name;
            OpenLookMenu(player, Localizer.ForPlayer(player, "matchzy.pp.menu.edit.title", name), new List<(string, Action<CCSPlayerController>)>
            {
                (Localizer.ForPlayer(player, "matchzy.pp.menu.edit.rename"), p => AskChatLine(p, Localizer.ForPlayer(p, "matchzy.pp.menu.edit.ask_new_name"), (q, text) => EditSavedLineup(q, name, (slots, entry) =>
                {
                    if (slots.ContainsKey(text))
                    {
                        PrintToPlayerChat(q, Localizer.ForPlayer(q, "matchzy.pp.menu.edit.exists", text));
                        return false;
                    }
                    slots.Remove(name);
                    slots[text] = entry;
                    PrintToPlayerChat(q, Localizer.ForPlayer(q, "matchzy.pp.menu.edit.renamed", name, text));
                    return true;
                }))),
                (Localizer.ForPlayer(player, "matchzy.pp.menu.edit.description"), p => AskChatLine(p, Localizer.ForPlayer(p, "matchzy.pp.menu.edit.ask_description"), (q, text) => EditSavedLineup(q, name, (_, entry) =>
                {
                    entry["Desc"] = text;
                    PrintToPlayerChat(q, Localizer.ForPlayer(q, "matchzy.pp.menu.edit.desc_saved", name));
                    return true;
                }))),
                (Localizer.ForPlayer(player, "matchzy.pp.menu.edit.set"), p => AskChatLine(p, Localizer.ForPlayer(p, "matchzy.pp.menu.edit.ask_set"), (q, text) => EditSavedLineup(q, name, (_, entry) =>
                {
                    entry["Set"] = text;
                    PrintToPlayerChat(q, Localizer.ForPlayer(q, "matchzy.pp.menu.edit.set_saved", name, text));
                    return true;
                }))),
                (Localizer.ForPlayer(player, "matchzy.pp.menu.edit.delete"), p => EditSavedLineup(p, name, (slots, _) =>
                {
                    slots.Remove(name);
                    ForgetDeletedLineups(p, new[] { name });
                    PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.pp.menu.edit.deleted", name));
                    return true;
                })),
            });
        }

        // One change to one of the player's own saved lineups of this map, then the marks are redrawn.
        // A deleted lineup is gone everywhere: not only its marks (redrawn from the file), also
        // what the player had loaded of it. It stayed "armed": the box still named it, its ring
        // hung on the wall and E put the player back on its spot (the author, 2026-11-17).
        private void ForgetDeletedLineups(CCSPlayerController player, IEnumerable<string> names)
        {
            try
            {
                var gone = new HashSet<string>(names, StringComparer.Ordinal);
                int slot = player.Slot;
                ulong id = player.SteamID;
                if (finderArmed.TryGetValue(slot, out var armed) && armed.Plan.Library && gone.Contains(armed.Plan.Name))
                {
                    finderArmed.Remove(slot);
                    if (finderGoEntities.Remove(slot, out var guide))
                        KillFinderEntities(guide);
                    player.PrintToCenterAlert(" ");
                }
                finderTracks.RemoveAll(t => t.Slot == slot && t.Plan.Library && gone.Contains(t.Plan.Name));
                if (_jtPersonal.TryGetValue(id, out var own) && own.Group.Lineups.Any(l => gone.Contains(l.Name)))
                {
                    _jtPersonal.Remove(id);
                    if (_jtViews.TryGetValue(id, out var view))
                        DropJtSpot(player, view, own.Group);
                }
                if (nadeUseHold.TryGetValue(slot, out var hold) && gone.Contains(hold.Lineup.Name))
                    nadeUseHold.Remove(slot);
                if (nadeTrainings.TryGetValue(slot, out var training))
                {
                    int before = training.Queue.Take(training.Index).Count(l => gone.Contains(l.Name));
                    training.Queue.RemoveAll(l => gone.Contains(l.Name));
                    training.Index = Math.Max(0, training.Index - before);
                }
            }
            catch (Exception e)
            {
                Log($"[LookMenu] forget deleted: {e.GetType().Name}: {e.Message}");
            }
        }

        private void EditSavedLineup(CCSPlayerController player, string name, Func<Dictionary<string, Dictionary<string, string>>, Dictionary<string, string>, bool> change)
        {
            try
            {
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                if (!File.Exists(path))
                    return;
                var all = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path));
                if (all == null)
                    return;
                string map = Server.MapName;
                foreach (string owner in new[] { player.SteamID.ToString(), "default" })
                {
                    if (!all.TryGetValue(owner, out var slots) || !slots.TryGetValue(name, out var entry)
                        || !entry.TryGetValue("Map", out var m) || m != map)
                        continue;
                    if (!change(slots, entry))
                        return;
                    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(all, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    RefreshNadeMarkersIfActive(player);
                    return;
                }
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.edit.not_own", name));
            }
            catch (Exception e)
            {
                Log($"[LookMenu] edit: {e.GetType().Name}: {e.Message}");
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.edit.file_failed"));
            }
        }

        // ---- .train: the lineups one after the other --------------------------------------------
        private sealed class NadeTraining
        {
            public List<NadeLineup> Queue = new();
            public int Index;
            public int Perfect, Good, Miss, Skipped;
            public DateTime Started = DateTime.Now;
            public int NextAtTick = -1;
        }

        private readonly Dictionary<int, NadeTraining> nadeTrainings = new();
        private const float TrainPerfect = 16f, TrainGood = 64f;

        [ConsoleCommand("css_train", "Practice: go through your saved lineups one by one (.train [smoke|flash|he|molly|decoy|set name])")]
        public void OnTrainCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (nadeTrainings.ContainsKey(player.Slot))
            {
                EndNadeTraining(player, Localizer.ForPlayer(player, "matchzy.pp.menu.train.stopped"));
                return;
            }
            string want = string.Join(" ", FinderWords(command)).Trim();
            string? kind = want == "" ? null : LineupKind(want switch { "nade" or "hegrenade" => "he", "molotov" or "inc" => "molly", "flashbang" => "flash", _ => want });
            var list = LoadLineupsForCurrentMap(player)
                // Only lineups a throw can be judged for: a known grenade, and a known throw or its own stored release.
                .Where(l => LineupKind(l.Type) != null && (l.NadePos != "" || FinderKindFromThrow(l.Throw) != null))
                .Where(l => want == "" || (kind != null ? LineupKind(l.Type) == kind : string.Equals(l.Set, want, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (list.Count == 0)
            {
                PrintToPlayerChat(player, want == ""
                    ? Localizer.ForPlayer(player, "matchzy.pp.menu.train.none")
                    : Localizer.ForPlayer(player, "matchzy.pp.menu.train.no_match", want));
                return;
            }
            var random = new Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            nadeTrainings[player.Slot] = new NadeTraining { Queue = list };
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.train.start", list.Count));
            LoadTrainingLineup(player);
        }

        [ConsoleCommand("css_skip", "Practice: skip the current lineup of .train")]
        public void OnTrainSkipCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !nadeTrainings.TryGetValue(player.Slot, out var t))
                return;
            t.Skipped++;
            t.Index++;
            t.NextAtTick = -1;
            LoadTrainingLineup(player);
        }

        private void LoadTrainingLineup(CCSPlayerController player)
        {
            if (!nadeTrainings.TryGetValue(player.Slot, out var t))
                return;
            if (t.Index >= t.Queue.Count)
            {
                EndNadeTraining(player, Localizer.ForPlayer(player, "matchzy.pp.menu.train.complete"));
                return;
            }
            var lu = t.Queue[t.Index];
            string howThrown = lu.Throw != "" ? ", " + lu.Throw : "";
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.train.current", t.Index + 1, t.Queue.Count, lu.Name, lu.Type, howThrown));
            LoadNadeLineup(player, lu);
        }

        // From the verdict of a throw of a saved lineup.
        private void OnTrainingVerdict(CCSPlayerController player, FinderResult plan, float off)
        {
            if (!nadeTrainings.TryGetValue(player.Slot, out var t) || t.Index >= t.Queue.Count || t.NextAtTick >= 0
                || !string.Equals(t.Queue[t.Index].Name, plan.Name, StringComparison.Ordinal))
                return;
            string verdict;
            if (off <= TrainPerfect) { t.Perfect++; verdict = Localizer.ForPlayer(player, "matchzy.pp.menu.train.perfect", off); }
            else if (off <= TrainGood) { t.Good++; verdict = Localizer.ForPlayer(player, "matchzy.pp.menu.train.good", off); }
            else { t.Miss++; verdict = Localizer.ForPlayer(player, "matchzy.pp.menu.train.miss", off); }
            player.PrintToCenterAlert(verdict);
            t.NextAtTick = Server.TickCount + 64 * 3;
        }

        private void TickNadeTrainings()
        {
            if (nadeTrainings.Count == 0)
                return;
            foreach (var (slot, t) in nadeTrainings.ToList())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player == null || !IsPlayerValid(player) || !isPractice)
                {
                    nadeTrainings.Remove(slot);
                    continue;
                }
                if (t.NextAtTick < 0 || Server.TickCount < t.NextAtTick)
                    continue;
                t.NextAtTick = -1;
                t.Index++;
                LoadTrainingLineup(player);
            }
        }

        private void EndNadeTraining(CCSPlayerController player, string head)
        {
            if (!nadeTrainings.Remove(player.Slot, out var t))
                return;
            var took = DateTime.Now - t.Started;
            PrintToPlayerChat(player, head);
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.train.summary", t.Perfect, t.Good, t.Miss, t.Skipped, (int)took.TotalMinutes, took.Seconds));
        }

        private void ForgetLookMenuPlayer(int slot)
        {
            lookMenus.Remove(slot);
            chatAsks.Remove(slot);
            nadeUseHold.Remove(slot);
            nadeTrainings.Remove(slot);
        }
    }
}
