using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;

namespace MatchZy
{
    // .nades - WasdMenu browser over the grenade library (private lineups + shared pack) for the
    // current map: filter by grenade type, click a lineup to load it (same path as .loadnade -
    // teleport + grenade equipped). "Delete a lineup" lists the player's own lineups and removes
    // the picked one after a yes/no (same path as .delnade). Menu types stay inside the OpenX methods and every entry point
    // goes through OpenMenuGuarded, so the CS2MenuManager dependency stays optional (see AdminMenu).
    public partial class MatchZy
    {
        [ConsoleCommand("css_nades", "Opens the grenade library menu")]
        public void OnNadesMenuCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;
            // Defer: opening a menu on the chat-dispatch tick gets clobbered by the chat broadcast.
            Server.NextFrame(() =>
            {
                if (IsPlayerValid(player))
                    OpenMenuGuarded(player!, OpenNadesMenu);
            });
        }

        private void OpenNadesMenu(CCSPlayerController player)
        {
            var menu = new WasdMenu("Grenade Library", this);
            foreach (string type in new[] { "All", "Smoke", "Flash", "HE", "Molly", "Decoy" })
            {
                string t = type;
                menu.AddItem(t, (p, _) => OpenNadesListMenu(p, t, menu));
            }
            menu.AddItem("Delete a lineup", (p, _) => OpenNadesDeleteMenu(p, menu));
            menu.Display(player, 0);
        }

        // Only the lineups .delnade can remove: the player's own on this map (the shared "default"
        // group when lineups are saved globally). The shared pack is left to .libremove.
        private void OpenNadesDeleteMenu(CCSPlayerController player, WasdMenu root)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            string ownGroup = isSaveNadesAsGlobalEnabled ? "default" : player.SteamID.ToString();
            List<(string Name, string Type)> own = new();
            try
            {
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                var dict = File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path)) ?? new()
                    : new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                foreach (var (steam, name, info) in OrderedLineupsForMap(player, dict, ""))
                {
                    if (steam != ownGroup)
                        continue;
                    own.Add((name, info.TryGetValue("Type", out var ty) ? ty : ""));
                }
            }
            catch (Exception e)
            {
                Log($"[NadesMenu] {e.Message}");
            }

            var sub = new WasdMenu($"Delete a lineup ({own.Count})", this) { PrevMenu = root };
            if (own.Count == 0)
            {
                sub.AddItem("(no lineups of yours on this map)", (_, _) => { });
            }
            else
            {
                foreach (var (name, type) in own)
                {
                    string label = string.IsNullOrEmpty(type) ? name : $"[{type}] {name}";
                    string target = name;
                    sub.AddItem(label, (p, _) =>
                    {
                        var confirm = new WasdMenu($"Delete {target}?", this) { PrevMenu = sub };
                        confirm.AddItem("Yes, delete it", (p2, _) =>
                        {
                            HandleDeleteNadeCommand(p2, target);
                            // Fresh list, so the deleted lineup is gone and the next one can be picked.
                            OpenNadesDeleteMenu(p2, root);
                        });
                        confirm.AddItem("No, keep it", (p2, _) => OpenNadesDeleteMenu(p2, root));
                        confirm.Display(p, 0);
                    });
                }
            }
            sub.Display(player, 0);
        }

        private void OpenNadesListMenu(CCSPlayerController player, string typeFilter, WasdMenu prev)
        {
            List<(string Steam, string Name, string Type, string Throw)> entries = new();
            // The shared pack (.libadd, grenadelibrary.json) is listed after the saved lineups.
            List<NadeLineup> packEntries = new();
            try
            {
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                var dict = File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path)) ?? new()
                    : new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                foreach (var (steam, name, info) in OrderedLineupsForMap(player, dict, ""))
                {
                    string type = info.TryGetValue("Type", out var ty) ? ty : "";
                    if (typeFilter != "All" && !string.Equals(type, typeFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string thr = info.TryGetValue("Throw", out var th) ? th : "";
                    entries.Add((steam, name, type, thr));
                }
            }
            catch (Exception e)
            {
                Log($"[NadesMenu] {e.Message}");
            }
            foreach (var lineup in LoadPackLineupsForCurrentMap())
            {
                if (typeFilter == "All" || string.Equals(lineup.Type, typeFilter, StringComparison.OrdinalIgnoreCase))
                    packEntries.Add(lineup);
            }

            int total = entries.Count + packEntries.Count;
            var sub = new WasdMenu($"Nades: {typeFilter} ({total})", this) { PrevMenu = prev };
            if (total == 0)
            {
                sub.AddItem("(no lineups on this map)", (_, _) => { });
            }
            else
            {
                foreach (var (steam, name, type, thr) in entries)
                {
                    string label = string.IsNullOrEmpty(type) ? name : $"[{type}] {name}";
                    if (!string.IsNullOrEmpty(thr))
                        label += $" - {thr}";
                    string loadName = name;
                    string loadGroup = steam;
                    sub.AddItem(label, (p, _) =>
                    {
                        // Same load path as .loadnade: teleport to the lineup + equip the grenade.
                        HandleLoadNadeCommand(p, loadName, loadGroup);
                    });
                }
                foreach (var lineup in packEntries)
                {
                    string label = string.IsNullOrEmpty(lineup.Type) ? lineup.Name : $"[{lineup.Type}] {lineup.Name}";
                    label += " (G)";
                    if (!string.IsNullOrEmpty(lineup.Throw))
                        label += $" - {lineup.Throw}";
                    var target = lineup;
                    sub.AddItem(label, (p, _) => LoadNadeLineup(p, target));
                }
            }
            sub.Display(player, 0);
        }
    }
}
