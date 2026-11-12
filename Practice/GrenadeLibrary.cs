using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // In-world grenade library (SCL-style). `.shownades` draws every saved lineup for the current
    // map - the caller's PRIVATE savednades PLUS the shared GLOBAL pack (grenadelibrary.json) - as a
    // glowing ghost on the throw spot (the lineup trainer, NadeTutorial.cs). Aim at a ghost + press E
    // to teleport to that lineup. Reuses the interactive spawn-marker aim-cone + teleport pattern.
    // Markers are server-wide entities (all players see them); the set is a single server-wide toggle
    // (last .shownades caller wins), which suits a practice server.
    // Each marker also has a small glowing grenade model of its type that shows through walls, so
    // lineups can be found across the map. `.shownades <filter>` limits what a viewer sees: a type
    // (smoke, flash, he, molly, decoy), mine / global, or text matched in the name and description;
    // `.shownades all` clears it. The filter is per viewer (applied in CheckTransmit).
    public partial class MatchZy
    {
        private sealed class NadeLineup
        {
            public Position Pos = null!;
            public string Name = "";
            public string Type = "";
            public string Throw = "";
            public string Desc = "";
            public bool Global;
        }

        // Lineups saved at (near) the SAME throw position are grouped onto ONE marker (the trainer
        // ghost); F cycles which lineup is selected, and E teleports to it.
        private sealed class NadeMarkerGroup
        {
            public Vector Pos = null!;             // representative throw position (first lineup added)
            public readonly List<NadeLineup> Lineups = new();
            public int Current;
            public CBaseModelEntity? Glow;         // grenade model (or the trainer ghost) glowing through walls
            public bool Ghost;                     // Glow is the trainer ghost: fixed green, not the type color
        }

        // Lineups within this radius of each other share one marker.
        private const float NadeGroupRadiusSq = 45.0f * 45.0f;
        // A player within this radius of a marker stops receiving it (per-player, via CheckTransmit),
        // so the beam does not block your view while you stand on the lineup; it reappears when you
        // walk away - no need to .shownades again.
        private const float NadeHideRadiusSq = 70.0f * 70.0f;

        // SteamIDs of the players who turned .shownades on; only they receive the markers.
        private readonly HashSet<ulong> _nadeViewers = new();
        // Per viewer: their .shownades filter (absent = everything), and per filter the groups it
        // hides, built on first use and dropped whenever the markers are rebuilt.
        private readonly Dictionary<ulong, string> _nadeViewerFilters = new();
        private readonly Dictionary<string, HashSet<NadeMarkerGroup>> _nadeFilterHidden = new(StringComparer.OrdinalIgnoreCase);
        // Glow markers are visible this far.
        private const int NadeGlowRange = 5000;

        // Whether a lineup matches a .shownades filter.
        private static bool LineupMatchesFilter(NadeLineup lu, string filter)
        {
            string f = filter.Trim().ToLowerInvariant();
            string type = lu.Type.ToLowerInvariant();
            switch (f)
            {
                case "smoke": case "smokes": return type == "smoke";
                case "flash": case "flashes": case "flashbang": return type == "flash";
                case "he": case "nade": case "grenade": return type == "he";
                case "molly": case "molotov": case "molotovs": case "inc": case "incendiary": case "fire": return type == "molly";
                case "decoy": return type == "decoy";
                case "mine": case "my": case "private": return !lu.Global;
                case "global": case "shared": return lu.Global;
            }
            return lu.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || lu.Desc.Contains(f, StringComparison.OrdinalIgnoreCase);
        }

        private HashSet<NadeMarkerGroup> NadeGroupsHiddenByFilter(string filter)
        {
            if (_nadeFilterHidden.TryGetValue(filter, out var hidden))
                return hidden;
            hidden = new HashSet<NadeMarkerGroup>();
            foreach (var g in activeNadeGroups)
                if (!g.Lineups.Any(lu => LineupMatchesFilter(lu, filter)))
                    hidden.Add(g);
            _nadeFilterHidden[filter] = hidden;
            return hidden;
        }

        private static bool IsClearNadeFilter(string filter)
            => filter.Length == 0 || filter.Equals("all", StringComparison.OrdinalIgnoreCase) || filter == "*";

        // Remembers the viewer's filter and replies with how many lineups it leaves.
        private void SetNadeViewerFilter(CCSPlayerController player, string filter)
        {
            if (IsClearNadeFilter(filter))
            {
                _nadeViewerFilters.Remove(player.SteamID);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.showing", activeNadeGroups.Sum(g => g.Lineups.Count)));
                if (!_jtModelsPrecached)
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.trainernotready"));
                return;
            }
            _nadeViewerFilters[player.SteamID] = filter;
            int matching = activeNadeGroups.Sum(g => g.Lineups.Count(lu => LineupMatchesFilter(lu, filter)));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.filtered", matching, filter));
        }

        // The grenade model a marker's glow uses.
        private static string NadeGlowModel(string type) => type.ToLowerInvariant() switch
        {
            "flash" => "weapons/models/grenade/flashbang/weapon_flashbang.vmdl",
            "he" => "weapons/models/grenade/hegrenade/weapon_hegrenade.vmdl",
            "molly" => "weapons/models/grenade/molotov/weapon_molotov.vmdl",
            "decoy" => "weapons/models/grenade/decoy/weapon_decoy.vmdl",
            _ => "weapons/models/grenade/smokegrenade/weapon_smokegrenade.vmdl",
        };

        private static List<NadeMarkerGroup> GroupLineups(List<NadeLineup> lineups)
        {
            var groups = new List<NadeMarkerGroup>(128);
            foreach (var lu in lineups)
            {
                Vector p = lu.Pos.PlayerPosition;
                NadeMarkerGroup? g = null;
                foreach (var grp in groups)
                {
                    float dx = grp.Pos.X - p.X, dy = grp.Pos.Y - p.Y, dz = grp.Pos.Z - p.Z;
                    if (dx * dx + dy * dy + dz * dz <= NadeGroupRadiusSq) { g = grp; break; }
                }
                if (g == null) { g = new NadeMarkerGroup { Pos = p }; groups.Add(g); }
                g.Lineups.Add(lu);
            }
            return groups;
        }

        // Console commands so a client can bind a key: bind g "css_shownades".
        [ConsoleCommand("css_shownades", "Toggle the in-world grenade library")]
        public void OnShowNadesConsoleCommand(CCSPlayerController? player, CommandInfo command) => ShowNadesWithFilter(player, command.ArgString);

        [ConsoleCommand("css_hidenades", "Hide the in-world grenade library")]
        public void OnHideNadesConsoleCommand(CCSPlayerController? player, CommandInfo command) => HandleHideNadesCommand(player);

        // Optional throw-style as the 2nd token of .savenade: .savenade <name> <throwtype> <comment>.
        // Returns the display name if the token is a recognized throw style, else null (then that
        // token is part of the comment, not a throw style).
        public static string? NormalizeThrowType(string token) => token.ToLowerInvariant() switch
        {
            "normal" or "normalthrow" => "Normal Throw",
            "jump" or "jumpthrow" or "jt" => "Jumpthrow",
            "run" or "runthrow" => "Run Throw",
            "walk" or "walkthrow" => "Walk Throw",
            "crouch" or "crouchthrow" or "duck" => "Crouch Throw",
            _ => null,
        };

        private bool grenadeLibraryActive;
        private readonly List<NadeMarkerGroup> activeNadeGroups = new(128);
        private readonly List<CEntityInstance> nadeMarkerEntities = new(256); // beams + worldtexts, for teardown
        private readonly Dictionary<int, float> lastNadeMarkerUseTime = new();
        private readonly Dictionary<int, float> lastNadeToggleTime = new();
        // Aim-test heights above a marker: beam nub (~9u) and floating label (~48u).
        private static readonly float[] NadeAimZOffsets = { 9.0f, 48.0f };

        private string GrenadeLibraryPath =>
            Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("grenadelibrary.json"));

        // Smoke color to apply on a rethrow (.throw / .rt), or null when matchzy_smoke_color_enabled
        // is off. Rethrown smokes are Globalname=="custom" so the OnEntitySpawned color path skips
        // them - GrenadeThrownData.Throw applies this instead.
        private (int R, int G, int B)? SmokeColorForThrow(CCSPlayerController player)
        {
            if (!smokeColorEnabled.Value) return null;
            // Called from delayed rethrow timers: the player may have left during the .delay window.
            if (player == null || !player.IsValid) return null;
            var c = GetPlayerTeammateColor(player);
            return (c.R, c.G, c.B);
        }

        // Stable, indexable order of saved lineups (default/global-in-file group first, then the
        // player's own), current map only, name-sorted within each group so #N is deterministic
        // across .listnades and .loadnade #N. Filter matches the lineup name (case-insensitive).
        private List<(string Steam, string Name, Dictionary<string, string> Info)> OrderedLineupsForMap(
            CCSPlayerController player,
            Dictionary<string, Dictionary<string, Dictionary<string, string>>> dict,
            string filter)
        {
            var list = new List<(string, string, Dictionary<string, string>)>(64);
            string map = Server.MapName;
            foreach (string steam in new[] { "default", player.SteamID.ToString() })
            {
                if (!dict.TryGetValue(steam, out var slots)) continue;
                foreach (var kv in slots.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!kv.Value.TryGetValue("Map", out var m) || m != map) continue;
                    if (!string.IsNullOrWhiteSpace(filter) && !kv.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add((steam, kv.Key, kv.Value));
                }
            }
            return list;
        }

        // Type (as stored: Flash/Smoke/HE/Decoy/Molly) -> weapon classname to deploy in hand.
        private static string NadeTypeToWeapon(string type, bool isCT) => type switch
        {
            "Flash" => "weapon_flashbang",
            "Smoke" => "weapon_smokegrenade",
            "HE" => "weapon_hegrenade",
            "Decoy" => "weapon_decoy",
            "Molly" => isCT ? "weapon_incgrenade" : "weapon_molotov",
            _ => "weapon_smokegrenade",
        };

        private static Color NadeTypeColor(string type) => type.ToLowerInvariant() switch
        {
            "smoke" => Color.FromArgb(120, 160, 255),  // blue
            "flash" => Color.FromArgb(255, 225, 90),   // yellow
            "he" => Color.FromArgb(235, 70, 70),       // red
            "molly" => Color.FromArgb(255, 130, 60),   // orange
            "decoy" => Color.FromArgb(180, 180, 180),
            _ => Color.FromArgb(220, 220, 220),
        };

        // ── .shownades / .hidenades ──────────────────────────────────────────────────────
        public void HandleShowNadesCommand(CCSPlayerController? player, CommandInfo? command = null)
            => ShowNadesWithFilter(player, "");

        // .shownades [filter]: without a filter it toggles the library for the caller; with one it
        // shows the library (if it is not up yet) and limits what the caller sees.
        public void ShowNadesWithFilter(CCSPlayerController? player, string filter)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            filter = (filter ?? "").Trim().Trim('"');
            if (filter.Length > 0)
            {
                if (!grenadeLibraryActive || !_nadeViewers.Contains(player!.SteamID))
                {
                    ShowNadesWithFilter(player, "");
                    if (!grenadeLibraryActive)
                        return;
                }
                SetNadeViewerFilter(player!, filter);
                return;
            }
            // Toggle per player: the markers are only sent to players who asked for them (see
            // OnNadeCheckTransmit), so a second player's .shownades adds them as a viewer instead of
            // hiding the library for everyone, and a viewer's .shownades hides it for them.
            if (grenadeLibraryActive)
            {
                if (_nadeViewers.Contains(player!.SteamID))
                {
                    HandleHideNadesCommand(player);
                }
                else
                {
                    _nadeViewers.Add(player.SteamID);
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.showing", activeNadeGroups.Sum(g => g.Lineups.Count)));
                    if (!_jtModelsPrecached)
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.trainernotready"));
                }
                return;
            }
            try
            {
                HideNadeMarkers(); // clear any previous set first
                var lineups = LoadLineupsForCurrentMap(player!);
                if (lineups.Count == 0)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.none"));
                    return;
                }
                var groups = GroupLineups(lineups);
                // Per-group guard: one bad marker draw (e.g. an ArrayTypeMismatchException artifact
                // from the AcceleratorCSS Harmony tracer hooking generic calls) must not kill the
                // whole library - draw what we can and log the rest with a full stack.
                foreach (var g in groups)
                {
                    try
                    {
                        DrawNadeMarkerGroup(g);
                    }
                    catch (Exception ge)
                    {
                        Log($"[GrenadeLibrary] marker draw failed at ({g.Pos.X:0},{g.Pos.Y:0},{g.Pos.Z:0}): {ge}");
                    }
                }
                activeNadeGroups.AddRange(groups);
                grenadeLibraryActive = true;
                _nadeViewers.Add(player!.SteamID);
                SetNadeTransmitListener(true);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.showing", lineups.Count));
                if (!_jtModelsPrecached)
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.trainernotready"));
            }
            catch (Exception e)
            {
                // Full exception (with stack) - Message alone made this class of failure undiagnosable.
                Log($"[GrenadeLibrary] show failed: {e}");
            }
        }

        public void HandleHideNadesCommand(CCSPlayerController? player, CommandInfo? command = null)
        {
            if (!IsPlayerValid(player)) return;
            // Hidden for this player; the markers are removed once nobody is viewing them.
            _nadeViewers.Remove(player!.SteamID);
            _nadeViewerFilters.Remove(player.SteamID);
            if (_nadeViewers.Count == 0)
                HideNadeMarkers();
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.hidden"));
        }

        // Re-render the markers from the current saved lineups if the library is showing. Call after a
        // save / delete / import / libadd / libremove so a changed lineup shows/disappears immediately
        // without the player having to toggle .shownades off and on.
        public void RefreshNadeMarkersIfActive(CCSPlayerController? player)
        {
            if (!grenadeLibraryActive || !isPractice || !IsPlayerValid(player)) return;
            try
            {
                var viewers = new HashSet<ulong>(_nadeViewers);
                var filters = new Dictionary<ulong, string>(_nadeViewerFilters);
                HideNadeMarkers(); // clears markers + grenadeLibraryActive + viewers
                _nadeViewers.UnionWith(viewers);
                foreach (var (steam, f) in filters)
                    _nadeViewerFilters[steam] = f;
                var lineups = LoadLineupsForCurrentMap(player!);
                var groups = GroupLineups(lineups);
                foreach (var g in groups)
                    DrawNadeMarkerGroup(g);
                activeNadeGroups.AddRange(groups);
                grenadeLibraryActive = lineups.Count > 0;
                SetNadeTransmitListener(grenadeLibraryActive);
            }
            catch (Exception e)
            {
                Log($"[GrenadeLibrary] refresh failed: {e.Message}");
            }
        }

        // Per-player marker visibility: hide the beam + label of any marker the player is standing on
        // (within NadeHideRadius) so it doesn't block the throw view; it transmits again once they move
        // off. Registered as a CheckTransmit listener only while markers are shown. Fully guarded - a throw here would crash
        // the transmit path, so it never throws.
        public void OnNadeCheckTransmit(CCheckTransmitInfoList infoList)
        {
            if (!grenadeLibraryActive || activeNadeGroups.Count == 0) return;
            try
            {
                foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
                {
                    if (player == null || !player.IsValid) continue;

                    // Only players who ran .shownades get the markers. One beam + one world text per
                    // lineup group (often 100+ groups) is a lot for every client to draw.
                    if (!_nadeViewers.Contains(player.SteamID))
                    {
                        var hidden = info.TransmitEntities;
                        foreach (var ent in nadeMarkerEntities)
                            if (ent != null && ent.IsValid) hidden.Remove(ent);
                        continue;
                    }

                    var pawn = player.PlayerPawn?.Value;
                    var o = pawn?.AbsOrigin;
                    if (o == null) continue;
                    // AbsOrigin is engine-backed; snapshot it once for this recipient.
                    float x = o.X, y = o.Y, z = o.Z;
                    var transmitEntities = info.TransmitEntities;
                    HashSet<NadeMarkerGroup>? filteredOut = _nadeViewerFilters.TryGetValue(player.SteamID, out var viewerFilter)
                        ? NadeGroupsHiddenByFilter(viewerFilter)
                        : null;
                    foreach (var g in activeNadeGroups)
                    {
                        if (filteredOut != null && filteredOut.Contains(g))
                        {
                            if (g.Glow != null && g.Glow.IsValid) transmitEntities.Remove(g.Glow);
                            continue;
                        }
                        float dx = g.Pos.X - x, dy = g.Pos.Y - y, dz = g.Pos.Z - z;
                        float distSq = dx * dx + dy * dy + dz * dz;
                        if (distSq > NadeHideRadiusSq) continue;
                        if (g.Glow != null && g.Glow.IsValid) transmitEntities.Remove(g.Glow);
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[GrenadeLibrary] transmit: {e.Message}");
            }
        }

        public void HideNadeMarkers()
        {
            foreach (var ent in nadeMarkerEntities)
            {
                try { if (ent != null && ent.IsValid) ent.Remove(); } catch { }
            }
            nadeMarkerEntities.Clear();
            activeNadeGroups.Clear();
            _nadeViewers.Clear();
            _nadeViewerFilters.Clear();
            _nadeFilterHidden.Clear();
            grenadeLibraryActive = false;
            SetNadeTransmitListener(false);
        }

        // The previous map's entities are gone (their wrappers point at freed memory), so on a new
        // map only forget them: no Remove(), no IsValid (which would read the freed memory). Without
        // this the transmit listener and .hidenades worked on dangling pointers, and +use near an old
        // spot teleported to the previous map's spawn positions.
        private void ForgetPracticeEntitiesOnMapStart()
        {
            // .arc beams of the previous map are gone too; their removal timers stopped with the map.
            _liveArcs.Clear();
            _liveArcBeamCount = 0;
            ForgetJtOnMapStart();
            ResetNadeFollow(mapStart: true);
            nadeMarkerEntities.Clear();
            activeNadeGroups.Clear();
            _nadeViewers.Clear();
            _nadeViewerFilters.Clear();
            _nadeFilterHidden.Clear();
            grenadeLibraryActive = false;
            SetNadeTransmitListener(false);
            spawnMarkerEntities.Clear();
            spawnMarkerVisuals.Clear();
            spawnMarkersActive = false;
            activeSpawnMarkers.Clear();
            // Timers flagged STOP_ON_MAPCHANGE are already gone: drop the references.
            spawnMarkerHoverTimer = null;
            spawnMarkerAimedByPlayer.Clear();
            practiceRegenTimer = null;
            startRoundPending = false;
            startRoundFrozen.Clear();
            ForgetBotSpawnMarkersOnMapStart();
            ForgetRecDisplaysOnMapStart();
            ForgetNavFloorCache();
            // Userids from the previous map mean nothing now.
            botReactionStates.Clear();
            botsAttacking.Clear();
            botsEngaged.Clear();
            botLastAim.Clear();
            botAiAwake = false;
            _botJiggleRoll.Clear();
            humanRegenOverride.Clear();
            respawnAtSavedLocation.Clear();
            // AddBot/SpawnBot timers are STOP_ON_MAPCHANGE: a spawn in flight at the map change
            // never reaches the code that clears the flag, and it would block .bot for the session.
            isSpawningBot = false;
            ForgetXrayOnMapStart();
            ForgetRunBoostsOnMapStart();
        }

        // CheckTransmit fires every tick and crosses into managed code for every recipient, so the
        // listener is only registered while markers are shown (.shownades), not for the whole session.
        private Listeners.CheckTransmit? _nadeTransmitHandler;

        private void SetNadeTransmitListener(bool on)
        {
            if (on && _nadeTransmitHandler == null)
            {
                _nadeTransmitHandler = OnNadeCheckTransmit;
                RegisterListener(_nadeTransmitHandler);
            }
            else if (!on && _nadeTransmitHandler != null)
            {
                RemoveListener(_nadeTransmitHandler);
                _nadeTransmitHandler = null;
            }
        }

        // ── marker rendering ─────────────────────────────────────────────────────────────
        private void DrawNadeMarkerGroup(NadeMarkerGroup group)
        {
            NadeLineup lu = group.Lineups[group.Current];
            Color color = NadeTypeColor(lu.Type);
            Vector pos = group.Pos;

            // Lineup trainer (NadeTutorial.cs): a spot is only the glowing ghost; the targets near it
            // and the hud hint on it say the rest (no beam, no label).
            var ghost = SpawnJtGhost(pos, lu.Pos.PlayerAngle.Y);
            if (ghost != null)
            {
                group.Glow = ghost;
                group.Ghost = true;
                nadeMarkerEntities.Add(ghost);
                EnsureJtWatch();
                return;
            }

            // Models not precached yet (plugin loaded in the middle of a map): the grenade glow
            // stands in for the ghost until the next map.
            // Glowing grenade model just above the spot, visible through walls (GlowType 3).
            var glow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
            if (glow != null)
            {
                glow.SetModel(NadeGlowModel(lu.Type));
                glow.Spawnflags = 256u; // no collisions
                glow.Teleport(new Vector(pos.X, pos.Y, pos.Z + 24.0f), new QAngle(0, lu.Pos.PlayerAngle.Y, 0), new Vector(0, 0, 0));
                glow.DispatchSpawn();
                SetNadeGlowColor(glow, color);
                group.Glow = glow;
                nadeMarkerEntities.Add(glow);
            }
            EnsureJtWatch();
        }

        private static void SetNadeGlowColor(CBaseModelEntity glow, Color color)
        {
            glow.Glow.GlowColorOverride = color;
            glow.Glow.GlowRange = NadeGlowRange;
            glow.Glow.GlowRangeMin = 0;
            glow.Glow.GlowTeam = -1;
            glow.Glow.GlowType = 3;
            Utilities.SetStateChanged(glow, "CBaseModelEntity", "m_Glow");
        }

        // ── data loading (private savednades + global pack), current map only ────────────
        private List<NadeLineup> LoadLineupsForCurrentMap(CCSPlayerController player)
        {
            var result = new List<NadeLineup>(256);
            string map = Server.MapName;

            // Private (this player's savednades.json).
            try
            {
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                if (File.Exists(path))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path));
                    string steam = player.SteamID.ToString();
                    if (dict != null && dict.TryGetValue(steam, out var slots))
                        AddLineupsFromSlots(slots, map, global: false, result);
                }
            }
            catch (Exception e) { Log($"[GrenadeLibrary] private load: {e.Message}"); }

            // Global pack (grenadelibrary.json, keyed by map).
            try
            {
                var pack = LoadGlobalPack();
                if (pack.TryGetValue(map, out var slots))
                    AddLineupsFromSlots(slots, map, global: true, result);
            }
            catch (Exception e) { Log($"[GrenadeLibrary] global load: {e.Message}"); }

            return result;
        }

        private static void AddLineupsFromSlots(Dictionary<string, Dictionary<string, string>> slots, string map, bool global, List<NadeLineup> outList)
        {
            foreach (var kv in slots)
            {
                var info = kv.Value;
                // Private entries are keyed by map inside; global slots are already per-map.
                if (!global && (!info.TryGetValue("Map", out var m) || m != map))
                    continue;
                if (!info.TryGetValue("Position", out var posStr) || !info.TryGetValue("Angles", out var angStr))
                    continue;
                if (!TryParseVector(posStr, out Vector pos) || !TryParseAngle(angStr, out QAngle ang))
                    continue;
                outList.Add(new NadeLineup
                {
                    Pos = new Position(pos, ang),
                    Name = kv.Key,
                    Type = info.TryGetValue("Type", out var t) ? t : "",
                    Throw = info.TryGetValue("Throw", out var th) ? th : "",
                    Desc = info.TryGetValue("Desc", out var d) ? d : "",
                    Global = global,
                });
            }
        }

        private static bool TryParseVector(string s, out Vector v)
        {
            v = new Vector(0, 0, 0);
            if (!LineupFormat.TryParse(s, out float x, out float y, out float z)) return false;
            v = new Vector(x, y, z);
            return true;
        }

        private static bool TryParseAngle(string s, out QAngle a)
        {
            a = new QAngle(0, 0, 0);
            if (!LineupFormat.TryParse(s, out float x, out float y, out float z)) return false;
            a = new QAngle(x, y, z);
            return true;
        }

        // Index of the marker group the player's crosshair is on (aim-cone against the beam nub AND
        // the floating label), or -1. Shared by E (teleport) and F (toggle).
        private int AimedGroupIndex(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn?.AbsOrigin == null) return -1;
            Vector origin = pawn.AbsOrigin;
            Vector eye = new(origin.X, origin.Y, origin.Z + 64.0f);
            QAngle ang = pawn.EyeAngles;
            double pitch = ang.X * Math.PI / 180.0;
            double yaw = ang.Y * Math.PI / 180.0;
            var fX = (float)(Math.Cos(pitch) * Math.Cos(yaw));
            var fY = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            var fZ = (float)(-Math.Sin(pitch));

            int best = -1;
            float bestDot = spawnMarkerAimMinDot;
            for (int i = 0; i < activeNadeGroups.Count; i++)
            {
                Vector sp = activeNadeGroups[i].Pos;
                foreach (float zOff in NadeAimZOffsets)
                {
                    float tx = sp.X - eye.X, ty = sp.Y - eye.Y, tz = (sp.Z + zOff) - eye.Z;
                    float dist = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
                    if (dist < 1.0f) continue;
                    float dot = (tx * fX + ty * fY + tz * fZ) / dist;
                    if (dot > bestDot) { bestDot = dot; best = i; }
                }
            }
            return best;
        }

        // ── E-teleport onto the aimed marker (mirrors OnSpawnMarkerButtonHandler) ─────────
        private void OnNadeMarkerButtonHandler(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if (!grenadeLibraryActive || !isPractice) return;
            if (!IsPlayerValid(player) || player.IsBot || !player.UserId.HasValue) return;
            if (player.TeamNum != (byte)CsTeam.CounterTerrorist && player.TeamNum != (byte)CsTeam.Terrorist) return;

            int uid = player.UserId.Value;
            float now = Server.CurrentTime;

            // F (default = weapon inspect / PlayerButtons.Inspect) cycles the aimed group's shown
            // lineup - works out-of-box, no bind needed. .nadetoggle / css_nadetoggle are alternatives.
            if ((pressed & PlayerButtons.Inspect) != 0)
            {
                if (lastNadeToggleTime.TryGetValue(uid, out float lt) && now - lt < spawnMarkerUseCooldown)
                    return;
                lastNadeToggleTime[uid] = now;
                // On a spot, F cycles that spot's lineups; otherwise the marker aimed at.
                if (!ToggleJtZoneLineup(player))
                    ToggleAimedGroup(player);
                return;
            }

            if ((pressed & PlayerButtons.Use) == 0) return;
            if (lastNadeMarkerUseTime.TryGetValue(uid, out float last) && now - last < spawnMarkerUseCooldown)
                return;

            int gi = AimedGroupIndex(player);
            if (gi < 0) return;

            lastNadeMarkerUseTime[uid] = now;
            var group = activeNadeGroups[gi];
            NadeLineup target = group.Lineups[group.Current];
            Server.NextFrame(() =>
            {
                if (IsPlayerValid(player))
                    LoadNadeLineup(player, target);
            });
        }

        // Teleport AND deploy the grenade in hand (same as .loadnade), not just a bare teleport -
        // otherwise you arrive at the lineup with the wrong weapon out. Used by the marker +use and
        // the .nades menu (shared-pack entries).
        private void LoadNadeLineup(CCSPlayerController player, NadeLineup target)
        {
            // CanPracticeTeleport: a valid, living pawn on T/CT (the .loadnade path checks the same).
            // Without it a dead player picking a shared lineup in the .nades menu was teleported and
            // handed a grenade.
            if (!isPractice || !CanPracticeTeleport(player))
                return;
            bool isCT = player.TeamNum == (byte)CsTeam.CounterTerrorist;
            TeleportAndClearPose(player, target.Pos.PlayerPosition, target.Pos.PlayerAngle, wantDucked: false, deployWeapon: NadeTypeToWeapon(target.Type, isCT), giveDeploy: true);
            string tag = target.Global ? " (G)" : "";
            string note = string.IsNullOrEmpty(target.Desc) ? "" : $" - {target.Desc}";
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.loaded", $"[{target.Type}] {target.Name}{tag}{note}"));
            ShowNadeTutorial(player, target.Name, target.Type, target.Throw, target.Pos.PlayerPosition, target.Pos.PlayerAngle);
        }

        // Shared-pack (grenadelibrary.json) lineups for the current map, for the .nades menu.
        private List<NadeLineup> LoadPackLineupsForCurrentMap()
        {
            var result = new List<NadeLineup>();
            try
            {
                string map = Server.MapName;
                if (LoadGlobalPack().TryGetValue(map, out var slots))
                    AddLineupsFromSlots(slots, map, global: true, result);
            }
            catch (Exception e) { Log($"[GrenadeLibrary] pack load: {e.Message}"); }
            return result;
        }

        // ── F-toggle: cycle which lineup is shown in the aimed group ─────────────────────
        // Bind a key: bind f "css_nadetoggle".
        [ConsoleCommand("css_nadetoggle", "Cycle stacked lineups on the aimed .shownades marker")]
        public void OnNadeToggleConsoleCommand(CCSPlayerController? player, CommandInfo command) => HandleNadeToggleCommand(player);

        public void HandleNadeToggleCommand(CCSPlayerController? player, CommandInfo? command = null)
        {
            if (!grenadeLibraryActive || !isPractice || !IsPlayerValid(player)) return;
            ToggleAimedGroup(player!);
        }

        private void ToggleAimedGroup(CCSPlayerController player)
        {
            int gi = AimedGroupIndex(player);
            if (gi < 0) return;
            var g = activeNadeGroups[gi];
            if (g.Lineups.Count <= 1) return;

            g.Current = (g.Current + 1) % g.Lineups.Count;
            RefreshNadeGroupLabel(g);
        }

        // Glow after the selected lineup of a group changed.
        private void RefreshNadeGroupLabel(NadeMarkerGroup g)
        {
            if (!g.Ghost && g.Glow != null && g.Glow.IsValid)
                SetNadeGlowColor(g.Glow, NadeTypeColor(g.Lineups[g.Current].Type));
        }

        // ── global pack IO + admin add/remove/list ───────────────────────────────────────
        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> LoadGlobalPack()
        {
            string path = GrenadeLibraryPath;
            if (!File.Exists(path))
            {
                _globalPackLoadFailed = false;
                return new();
            }
            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path))
                       ?? new();
                _globalPackLoadFailed = false;
                return loaded;
            }
            catch (Exception e)
            {
                Log($"[GrenadeLibrary] pack parse: {e.Message}");
                // Keep a copy and block saves so .libadd/.libremove can't overwrite the pack with {}.
                if (!_globalPackLoadFailed)
                    BackupCorruptJsonFile(path, "GrenadeLibrary");
                _globalPackLoadFailed = true;
                return new();
            }
        }

        // Set when the global pack failed to parse; cleared by the next successful load.
        private bool _globalPackLoadFailed = false;

        // Returns false (and writes nothing) when the pack on disk failed to parse on the last load.
        private bool SaveGlobalPack(Dictionary<string, Dictionary<string, Dictionary<string, string>>> pack)
        {
            if (_globalPackLoadFailed)
            {
                Log($"[GrenadeLibrary] save refused: {GrenadeLibraryPath} failed to parse. Fix or remove it first.");
                return false;
            }
            File.WriteAllText(GrenadeLibraryPath, JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        // .libadd <name> - promote the caller's saved lineup (current map) into the shared pack.
        public void HandleLibAddCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            if (!IsPlayerAdmin(player, "css_libadd", "@css/config")) { SendPlayerNotAdminMessage(player); return; }
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) { ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".libadd <name>")); return; }

            try
            {
                string map = Server.MapName;
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                if (!File.Exists(path)) { ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.privnotfound", name)); return; }
                var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path));
                string steam = player!.SteamID.ToString();
                if (dict == null || !dict.TryGetValue(steam, out var slots) || !slots.TryGetValue(name, out var info)
                    || !info.TryGetValue("Map", out var m) || m != map)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.privnotfound", name));
                    return;
                }

                var pack = LoadGlobalPack();
                if (!pack.TryGetValue(map, out var mapSlots)) { mapSlots = new(); pack[map] = mapSlots; }
                mapSlots[name] = new Dictionary<string, string>(info);
                if (!SaveGlobalPack(pack))
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pracmisc.libraryunreadable"));
                    return;
                }
                RefreshNadeMarkersIfActive(player);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.added", name));
            }
            catch (Exception e)
            {
                Log($"[GrenadeLibrary] libadd: {e.Message}");
            }
        }

        // .libremove <name> - remove a lineup from the shared pack (current map).
        public void HandleLibRemoveCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            if (!IsPlayerAdmin(player, "css_libremove", "@css/config")) { SendPlayerNotAdminMessage(player); return; }
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) { ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".libremove <name>")); return; }

            try
            {
                string map = Server.MapName;
                var pack = LoadGlobalPack();
                if (pack.TryGetValue(map, out var mapSlots) && mapSlots.Remove(name))
                {
                    if (mapSlots.Count == 0) pack.Remove(map);
                    if (!SaveGlobalPack(pack))
                    {
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pracmisc.libraryunreadable"));
                        return;
                    }
                    RefreshNadeMarkersIfActive(player);
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.removed", name));
                }
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.packnotfound", name));
                }
            }
            catch (Exception e)
            {
                Log($"[GrenadeLibrary] libremove: {e.Message}");
            }
        }

        // .liblist - list the shared pack lineups for the current map.
        public void HandleLibListCommand(CCSPlayerController? player, string _ = "")
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            string map = Server.MapName;
            var pack = LoadGlobalPack();
            if (!pack.TryGetValue(map, out var slots) || slots.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.packempty"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.packcount", slots.Count));
            foreach (var kv in slots)
            {
                string type = kv.Value.TryGetValue("Type", out var t) ? t : "";
                ReplyToUserCommand(player, $" [{type}] {kv.Key}");
            }
        }
    }
}
