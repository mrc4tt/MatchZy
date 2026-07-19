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
    // teleport + grenade equipped). Menu types stay inside the OpenX methods and every entry point
    // goes through OpenMenuGuarded, so the CS2MenuManager dependency stays optional (see AdminMenu).
    public partial class MatchZy
    {
        [ConsoleCommand("css_nades", "Opens the grenade library menu")]
        public void OnNadesMenuCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
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
            menu.Display(player, 0);
        }

        private void OpenNadesListMenu(CCSPlayerController player, string typeFilter, WasdMenu prev)
        {
            List<(string Name, string Type, string Throw)> entries = new();
            try
            {
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                var dict = File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path)) ?? new()
                    : new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                foreach (var (_, name, info) in OrderedLineupsForMap(player, dict, ""))
                {
                    string type = info.TryGetValue("Type", out var ty) ? ty : "";
                    if (typeFilter != "All" && !string.Equals(type, typeFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string thr = info.TryGetValue("Throw", out var th) ? th : "";
                    entries.Add((name, type, thr));
                }
            }
            catch (Exception e)
            {
                Log($"[NadesMenu] {e.Message}");
            }

            var sub = new WasdMenu($"Nades: {typeFilter} ({entries.Count})", this) { PrevMenu = prev };
            if (entries.Count == 0)
            {
                sub.AddItem("(no lineups on this map)", (_, _) => { });
            }
            else
            {
                foreach (var (name, type, thr) in entries)
                {
                    string label = string.IsNullOrEmpty(type) ? name : $"[{type}] {name}";
                    if (!string.IsNullOrEmpty(thr))
                        label += $" - {thr}";
                    string loadName = name;
                    sub.AddItem(label, (p, _) =>
                    {
                        // Same load path as .loadnade: teleport to the lineup + equip the grenade.
                        HandleLoadNadeCommand(p, loadName);
                    });
                }
            }
            sub.Display(player, 0);
        }
    }
}
