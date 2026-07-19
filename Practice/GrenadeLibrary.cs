using System.Drawing;
using System.Globalization;
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
    // vertical beam + a floating world-text label, colored by nade type. Aim at a beam + press E to
    // teleport to that lineup. Reuses the interactive spawn-marker aim-cone + teleport pattern.
    // Markers are server-wide entities (all players see them); the set is a single server-wide toggle
    // (last .shownades caller wins), which suits a practice server.
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

        // Lineups saved at (near) the SAME throw position are grouped onto ONE beam + label instead of
        // overlapping (unreadable). The label shows current/total + "Use F to toggle"; F cycles which
        // lineup is shown, and E teleports to the one currently shown.
        private sealed class NadeMarkerGroup
        {
            public Vector Pos = null!;             // representative throw position (first lineup added)
            public readonly List<NadeLineup> Lineups = new();
            public int Current;
            public CPointWorldText? Label;         // updated on F-toggle
            public CBeam? Beam;
        }

        // Lineups within this radius of each other share one marker.
        private const float NadeGroupRadiusSq = 45.0f * 45.0f;
        // A player within this radius of a marker stops receiving it (per-player, via CheckTransmit),
        // so the beam does not block your view while you stand on the lineup; it reappears when you
        // walk away - no need to .shownades again.
        private const float NadeHideRadiusSq = 70.0f * 70.0f;

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

        // The label text for a group's currently-shown lineup (with the counter + toggle hint when the
        // group holds more than one lineup).
        private static string LabelTextFor(NadeMarkerGroup g)
        {
            var lu = g.Lineups[g.Current];
            var lines = new List<string>(5);
            if (g.Lineups.Count > 1) lines.Add($"{g.Current + 1}/{g.Lineups.Count}");
            lines.Add(string.IsNullOrEmpty(lu.Type) ? "[]" : $"[{lu.Type}]");
            lines.Add(string.IsNullOrWhiteSpace(lu.Desc) ? lu.Name : lu.Desc);
            if (!string.IsNullOrWhiteSpace(lu.Throw)) lines.Add(lu.Throw);
            if (g.Lineups.Count > 1) lines.Add("Use F to toggle");
            return string.Join("\n", lines);
        }

        // Console commands so a client can bind a key: bind g "css_shownades".
        [ConsoleCommand("css_shownades", "Toggle the in-world grenade library")]
        public void OnShowNadesConsoleCommand(CCSPlayerController? player, CommandInfo command) => HandleShowNadesCommand(player);

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
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            // Toggle: if markers are up, this call hides them (so a single key, e.g. bind f
            // "css_shownades", flips the library on/off).
            if (grenadeLibraryActive)
            {
                HandleHideNadesCommand(player);
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
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.showing", lineups.Count));
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
                HideNadeMarkers(); // clears markers + grenadeLibraryActive
                var lineups = LoadLineupsForCurrentMap(player!);
                var groups = GroupLineups(lineups);
                foreach (var g in groups)
                    DrawNadeMarkerGroup(g);
                activeNadeGroups.AddRange(groups);
                grenadeLibraryActive = lineups.Count > 0;
            }
            catch (Exception e)
            {
                Log($"[GrenadeLibrary] refresh failed: {e.Message}");
            }
        }

        // Per-player marker visibility: hide the beam + label of any marker the player is standing on
        // (within NadeHideRadius) so it doesn't block the throw view; it transmits again once they move
        // off. Registered as a CheckTransmit listener in Load. Fully guarded - a throw here would crash
        // the transmit path, so it never throws.
        public void OnNadeCheckTransmit(CCheckTransmitInfoList infoList)
        {
            if (!grenadeLibraryActive || activeNadeGroups.Count == 0) return;
            try
            {
                foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
                {
                    if (player == null || !player.IsValid) continue;
                    var pawn = player.PlayerPawn?.Value;
                    var o = pawn?.AbsOrigin;
                    if (o == null) continue;
                    foreach (var g in activeNadeGroups)
                    {
                        float dx = g.Pos.X - o.X, dy = g.Pos.Y - o.Y, dz = g.Pos.Z - o.Z;
                        if (dx * dx + dy * dy + dz * dz > NadeHideRadiusSq) continue;
                        if (g.Beam != null && g.Beam.IsValid) info.TransmitEntities.Remove(g.Beam);
                        if (g.Label != null && g.Label.IsValid) info.TransmitEntities.Remove(g.Label);
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
            grenadeLibraryActive = false;
        }

        // ── marker rendering ─────────────────────────────────────────────────────────────
        private void DrawNadeMarkerGroup(NadeMarkerGroup group)
        {
            NadeLineup lu = group.Lineups[group.Current];
            Color color = NadeTypeColor(lu.Type);
            Vector pos = group.Pos;

            // Vertical beam at the throw spot (same style as .showspawns markers).
            var beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam != null)
            {
                beam.LifeState = 1;
                beam.Width = 4;
                beam.Render = color;
                Vector basePos = new(pos.X, pos.Y, pos.Z + 2.0f);
                beam.EndPos.X = basePos.X;
                beam.EndPos.Y = basePos.Y;
                beam.EndPos.Z = pos.Z + 16.0f;
                beam.Teleport(basePos, new QAngle(0, 0, 0), new Vector(0, 0, 0));
                beam.DispatchSpawn();
                group.Beam = beam;
                nadeMarkerEntities.Add(beam);
            }

            // Floating label above the beam (optional - matchzy_grenadelibrary_labels).
            var text = grenadeLibraryLabels.Value ? Utilities.CreateEntityByName<CPointWorldText>("point_worldtext") : null;
            if (text != null)
            {
                // SCL-style label: (N/total when grouped) / "[Type]" / comment / throw-style / toggle
                // hint. Built by LabelTextFor so F-toggle can re-render the same entity.
                text.MessageText = LabelTextFor(group);
                text.Color = color;
                text.FontName = "Arial Bold";
                // Enabled / FontSize / Fullbright / justify / reorient are get-only in the wrapper, so
                // they default to off/0 and the label renders INVISIBLE. Set them via schema. Reorient
                // AROUND_UP (1) billboards the text so it faces every viewer (no manual angle needed).
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bEnabled", true);
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flFontSize", 60.0f);
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flWorldUnitsPerPx", grenadeLibraryLabelScale.Value);
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bFullbright", true);
                // Dark background panel behind the text so it stays legible on light walls.
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bDrawBackground", true);
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flBackgroundBorderWidth", 6.0f);
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flBackgroundBorderHeight", 4.0f);
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyHorizontal", 1); // center
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyVertical", 1);   // center
                // AROUND_UP (1): billboard - the label yaws to face every viewer, so it can never be
                // seen from behind (fixed-yaw mode read mirrored from the back side). The original
                // billboard attempt looked mirrored because it ran WITHOUT the roll-90 upright fix;
                // with roll 90 (below) the panel is upright and viewer-facing from every angle.
                Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nReorientMode", 1);
                // Billboard mode owns the yaw; only the roll-90 upright matters here.
                float faceYaw = lu.Pos.PlayerAngle.Y;
                // De-clip: a lineup saved right up against a wall put the label plane INSIDE the wall,
                // cutting off part of the text (e.g. "Use F to toggle" -> "se F to toggle"). Push the
                // label center away from any nearby solid so the whole panel floats clear and reads 100%.
                Vector labelPos = DeclipLabelPosition(new Vector(pos.X, pos.Y, pos.Z + 48.0f));
                text.Teleport(labelPos, new QAngle(0, faceYaw, 90), new Vector(0, 0, 0));
                text.DispatchSpawn();
                group.Label = text;
                nadeMarkerEntities.Add(text);
            }
        }

        // Push a label center horizontally away from nearby walls so the text panel never intersects
        // geometry. Traces 8 compass directions; any solid within `clear` units contributes an
        // opposing push. Total push capped so the label stays recognizably at its marker.
        private static Vector DeclipLabelPosition(Vector c)
        {
#if HAS_CSS_TRACE
            const float clear = 56.0f;   // desired free space around the label center
            const float maxPush = 80.0f;
            float pushX = 0, pushY = 0;
            try
            {
                var opts = new TraceOptions { InteractsWith = Masks.Solid };
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * i / 4.0;
                    float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                    var tr = Trace.TraceEndShape(c, new Vector(c.X + dx * clear, c.Y + dy * clear, c.Z), null, opts);
                    if (!tr.DidHit())
                        continue;
                    float hx = tr.HitPoint.X - c.X, hy = tr.HitPoint.Y - c.Y;
                    float dist = (float)Math.Sqrt(hx * hx + hy * hy);
                    float deficit = clear - dist;
                    if (deficit > 0)
                    {
                        pushX -= dx * deficit;
                        pushY -= dy * deficit;
                    }
                }
            }
            catch
            {
                // Trace unavailable/failed - keep the original spot.
                return c;
            }
            float mag = (float)Math.Sqrt(pushX * pushX + pushY * pushY);
            if (mag > maxPush)
            {
                pushX *= maxPush / mag;
                pushY *= maxPush / mag;
            }
            return new Vector(c.X + pushX, c.Y + pushY, c.Z);
#else
            return c;
#endif
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
            var p = s.Split(' ');
            if (p.Length != 3) return false;
            if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
            v = new Vector(x, y, z);
            return true;
        }

        private static bool TryParseAngle(string s, out QAngle a)
        {
            a = new QAngle(0, 0, 0);
            var p = s.Split(' ');
            if (p.Length != 3) return false;
            if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
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
                if (!IsPlayerValid(player)) return;
                // Teleport AND deploy the grenade in hand (same as .loadnade), not just a bare
                // teleport - otherwise you arrive at the lineup with the wrong weapon out.
                bool isCT = player.TeamNum == (byte)CsTeam.CounterTerrorist;
                TeleportAndClearPose(player, target.Pos.PlayerPosition, target.Pos.PlayerAngle, wantDucked: false, deployWeapon: NadeTypeToWeapon(target.Type, isCT), giveDeploy: true);
                string tag = target.Global ? " (G)" : "";
                string note = string.IsNullOrEmpty(target.Desc) ? "" : $" - {target.Desc}";
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.gl.loaded", $"[{target.Type}] {target.Name}{tag}{note}"));
            });
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
            if (g.Label != null && g.Label.IsValid)
            {
                g.Label.MessageText = LabelTextFor(g);
                g.Label.Color = NadeTypeColor(g.Lineups[g.Current].Type);
                Utilities.SetStateChanged(g.Label, "CPointWorldText", "m_messageText");
            }
        }

        // ── global pack IO + admin add/remove/list ───────────────────────────────────────
        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> LoadGlobalPack()
        {
            string path = GrenadeLibraryPath;
            if (!File.Exists(path))
                return new();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path))
                       ?? new();
            }
            catch (Exception e)
            {
                Log($"[GrenadeLibrary] pack parse: {e.Message}");
                return new();
            }
        }

        private void SaveGlobalPack(Dictionary<string, Dictionary<string, Dictionary<string, string>>> pack)
        {
            File.WriteAllText(GrenadeLibraryPath, JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true }));
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
                SaveGlobalPack(pack);
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
                    SaveGlobalPack(pack);
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
