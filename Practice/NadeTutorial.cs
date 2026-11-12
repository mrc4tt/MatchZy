using System.Drawing;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Lineup trainer on top of the grenade library (.shownades), built the way lineup workshop maps
    // do it, but from the plugin so it works on every map:
    //  - each lineup spot is a glowing green "ghost" (CS2's editor player-start model);
    //  - near a spot (JtApproachDistance) the player sees a red target (CS2's grenade_target model)
    //    on the surface each lineup of that spot aims at, so the throws can be seen from the way in;
    //    the selected lineup's target glows green (F cycles);
    //  - standing on the spot (a small zone, no key needed) adds a hud hint at the bottom saying how
    //    to throw ("Left Click + Jump"), in the player's language; the ghost hides there;
    //  - E on a marker, .nades and .loadnade teleport onto the spot as before; a lineup loaded that
    //    is not on a shown marker gets a one-off spot for that player.
    // Targets and hints are per player (CheckTransmit / hud hint activator); the ghost is a library
    // marker, so .shownades viewers and filters apply to it.
    public partial class MatchZy
    {
        private const string JtGhostModel = "models/editor/playerstart.vmdl";
        private const string JtTargetModel = "models/dev/grenade_trajectory/grenade_target.vmdl";
        private static readonly Color JtGhostColor = Color.FromArgb(140, 0, 255, 161);
        private static readonly Color JtGhostGlow = Color.FromArgb(255, 127, 255, 0);
        private static readonly Color JtTargetColor = Color.FromArgb(255, 224, 56, 5);
        private static readonly Color JtTargetGlow = Color.FromArgb(255, 0, 255, 21);

        private const float JtZoneRadiusSq = 22.0f * 22.0f;   // horizontal, around the saved position
        // View offsets of a standing and a crouched player: the target sits on the line from the
        // eye through the crosshair as it was saved, so it covers the crosshair 1:1 on the spot.
        private const float JtEyeHeight = 64.06f;
        private const float JtEyeHeightDucked = 46.05f;
        // A ray that runs farther than this (sky, far end of the map) gets its target here instead,
        // still on the crosshair line, so it does not shrink to nothing.
        private const float JtMaxTargetDistance = 1200.0f;
        private const float JtApproachDistSq = 320.0f * 320.0f; // targets show from this far
        private const float JtTraceLength = 4096.0f;
        private const float JtPersonalSeconds = 20.0f;         // a one-off spot not stepped on goes away

        // Precached on every map load (OnServerPrecacheResources). After a hot reload in the middle
        // of a map it stays false until the next map: the markers then keep the grenade glow and
        // the targets use a world-text ring.
        private bool _jtModelsPrecached;

        // Per player: the targets of every spot near them (so rings never jump between spots while
        // walking around), the spot they stand on, and what the hud hint shows.
        private sealed class JtView
        {
            public readonly Dictionary<NadeMarkerGroup, JtSpotTargets> Spots = new();
            public NadeMarkerGroup? OnSpot;
            public int HintFor = -1;
            public bool HintShown;
        }

        private sealed class JtSpotTargets
        {
            public readonly List<CBaseEntity> Targets = new();
            public int Shown = -1;
        }

        private sealed class JtPersonal
        {
            public NadeMarkerGroup Group = null!;
            public float Expires;
            public bool Entered;
        }

        private readonly Dictionary<ulong, JtView> _jtViews = new();
        private readonly Dictionary<ulong, JtPersonal> _jtPersonal = new();
        private CounterStrikeSharp.API.Modules.Timers.Timer? _jtTimer;
        private Listeners.CheckTransmit? _jtTransmit;
        private CEnvHudHint? _jtHint;

        // Targets of a spot stay until the player is this far away (a little more than they appear
        // at), so walking along the edge does not make them blink.
        private const float JtKeepDistSq = 380.0f * 380.0f;

        private void RegisterNadeTutorial()
        {
            RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
            {
                manifest.AddResource(JtGhostModel);
                manifest.AddResource(JtTargetModel);
                _jtModelsPrecached = true;
                Log("[NadeTutorial] Lineup trainer models precached.");
            });
        }

        // Lang key suffix for a stored throw style ("Jumpthrow" -> "jump"), or null when none is saved.
        private static string? ThrowKey(string throwType) => throwType switch
        {
            "Normal Throw" => "normal",
            "Jumpthrow" => "jump",
            "Run Throw" => "run",
            "Walk Throw" => "walk",
            "Crouch Throw" => "crouch",
            _ => null,
        };

        // Short English key hint (used where a text cannot be per-player language).
        private static string ThrowLabel(string throwType) => ThrowKey(throwType) switch
        {
            "normal" => "Left Click",
            "jump" => "Left Click + Jump",
            "run" => "Run + Left Click",
            "walk" => "Walk + Left Click",
            "crouch" => "Crouch + Left Click",
            _ => throwType,
        };

        // After a lineup load (.loadnade, a .shownades marker, the .nades menu). A lineup of a shown
        // marker selects it there (its targets stay where they are); any other lineup gets a one-off
        // spot for this player.
        private void ShowNadeTutorial(CCSPlayerController player, string name, string nadeType, string throwType, Vector origin, QAngle angle)
        {
            if (!isPractice)
                return;
            ulong id = player.SteamID;
            NadeMarkerGroup? marker = null;
            if (grenadeLibraryActive && _nadeViewers.Contains(id))
            {
                foreach (var g in activeNadeGroups)
                {
                    int i = g.Lineups.FindIndex(l => JtHorizontalDistSq(l.Pos.PlayerPosition, origin) < 1.0f
                        && Math.Abs(l.Pos.PlayerAngle.Y - angle.Y) < 0.01f && Math.Abs(l.Pos.PlayerAngle.X - angle.X) < 0.01f);
                    if (i < 0) continue;
                    marker = g;
                    g.Current = i; // the loaded lineup is the selected one
                    break;
                }
            }
            // A previous one-off spot of this player goes (with its targets).
            if (_jtPersonal.Remove(id, out var old) && _jtViews.TryGetValue(id, out var view))
                DropJtSpot(player, view, old.Group);
            if (marker == null)
            {
                var g = new NadeMarkerGroup { Pos = new Vector(origin.X, origin.Y, origin.Z) };
                g.Lineups.Add(new NadeLineup
                {
                    Pos = new Position(origin, angle),
                    Name = name,
                    Type = nadeType,
                    Throw = throwType,
                });
                _jtPersonal[id] = new JtPersonal { Group = g, Expires = Server.CurrentTime + JtPersonalSeconds };
            }
            EnsureJtWatch();
        }

        private static float JtHorizontalDistSq(Vector a, Vector b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static float JtDistSq(Vector a, Vector b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        // ── watch ────────────────────────────────────────────────────────────────────────

        private void EnsureJtWatch()
        {
            _jtTimer ??= AddTimer(0.1f, CheckJtZones, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            if (_jtTransmit == null)
            {
                _jtTransmit = OnJtCheckTransmit;
                RegisterListener(_jtTransmit);
            }
        }

        private void StopJtWatch()
        {
            _jtTimer?.Kill();
            _jtTimer = null;
            if (_jtTransmit != null)
            {
                RemoveListener(_jtTransmit);
                _jtTransmit = null;
            }
        }

        private void CheckJtZones()
        {
            try
            {
                if (!isPractice)
                {
                    foreach (ulong id in _jtViews.Keys.ToList())
                        ClearJtView(Utilities.GetPlayerFromSteamId(id), id);
                    _jtPersonal.Clear();
                    StopJtWatch();
                    return;
                }
                float now = Server.CurrentTime;
                var seen = new HashSet<ulong>();
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
                        continue;
                    ulong id = player.SteamID;
                    seen.Add(id);
                    // Following a grenade (.follownade): the pawn flies away and comes back; keep the
                    // rings exactly as they were until it is back.
                    if (_nadeFollows.ContainsKey(id))
                        continue;
                    var o = player.PawnIsAlive ? player.PlayerPawn.Value?.AbsOrigin : null;
                    if (o == null)
                    {
                        ClearJtView(player, id);
                        continue;
                    }
                    UpdateJtView(player, id, new Vector(o.X, o.Y, o.Z), now);
                }
                foreach (ulong id in _jtViews.Keys.Where(k => !seen.Contains(k)).ToList())
                    ClearJtView(null, id);
                foreach (ulong id in _jtPersonal.Keys.Where(k => !seen.Contains(k)).ToList())
                    _jtPersonal.Remove(id);
                if (_jtViews.Count == 0 && _jtPersonal.Count == 0 && !grenadeLibraryActive)
                    StopJtWatch();
            }
            catch (Exception e)
            {
                Log($"[NadeTutorial] check failed: {e.Message}");
            }
        }

        private void UpdateJtView(CCSPlayerController player, ulong id, Vector o, float now)
        {
            _jtViews.TryGetValue(id, out var view);

            // The spots whose targets this player should see.
            var near = new List<NadeMarkerGroup>();
            _jtPersonal.TryGetValue(id, out var personal);
            if (personal != null)
            {
                float d = JtDistSq(personal.Group.Pos, o);
                bool had = view != null && view.Spots.ContainsKey(personal.Group);
                if (d <= (had ? JtKeepDistSq : JtApproachDistSq))
                    near.Add(personal.Group);
            }
            if (grenadeLibraryActive && _nadeViewers.Contains(id))
            {
                HashSet<NadeMarkerGroup>? hidden = _nadeViewerFilters.TryGetValue(id, out var filter) ? NadeGroupsHiddenByFilter(filter) : null;
                foreach (var g in activeNadeGroups)
                {
                    if (hidden != null && hidden.Contains(g)) continue;
                    bool had = view != null && view.Spots.ContainsKey(g);
                    if (JtDistSq(g.Pos, o) <= (had ? JtKeepDistSq : JtApproachDistSq))
                        near.Add(g);
                }
            }

            if (near.Count == 0 && view == null)
            {
                ExpireJtPersonal(id, personal, null, now);
                return;
            }
            if (view == null)
            {
                view = new JtView();
                _jtViews[id] = view;
            }

            foreach (var g in view.Spots.Keys.Where(g => !near.Contains(g)).ToList())
                DropJtSpot(player, view, g);
            foreach (var g in near)
            {
                if (!view.Spots.TryGetValue(g, out var spot))
                {
                    spot = new JtSpotTargets();
                    foreach (var lu in g.Lineups)
                    {
                        var t = SpawnJtTarget(lu);
                        if (t != null) spot.Targets.Add(t);
                    }
                    view.Spots[g] = spot;
                }
                if (spot.Shown != g.Current)
                {
                    for (int i = 0; i < spot.Targets.Count; i++)
                        SetJtTargetSelected(spot.Targets[i], i == g.Current);
                    spot.Shown = g.Current;
                }
            }

            NadeMarkerGroup? on = near.FirstOrDefault(g => JtOnSpot(g, o));
            if (on != null && personal != null && on == personal.Group)
                personal.Entered = true;
            if (on != view.OnSpot || (on != null && view.HintFor != on.Current))
            {
                view.OnSpot = on;
                if (on == null)
                {
                    if (view.HintShown) HideJtHint(player);
                    view.HintShown = false;
                    view.HintFor = -1;
                }
                else
                {
                    var lineup = on.Lineups[Math.Clamp(on.Current, 0, on.Lineups.Count - 1)];
                    string? key = ThrowKey(lineup.Throw);
                    string hint = Localizer.ForPlayer(player, key != null ? $"matchzy.gl.hint.{key}" : "matchzy.gl.hint.unknown");
                    if (on.Lineups.Count > 1)
                        hint += "   " + Localizer.ForPlayer(player, "matchzy.gl.hint.more", on.Current + 1, on.Lineups.Count);
                    ShowJtHint(player, hint);
                    view.HintShown = true;
                    view.HintFor = on.Current;
                }
            }
            ExpireJtPersonal(id, personal, view, now);
            if (view.Spots.Count == 0 && !view.HintShown)
                _jtViews.Remove(id);
        }

        // A one-off spot goes once the player has stood on it and left, or never came near.
        private void ExpireJtPersonal(ulong id, JtPersonal? personal, JtView? view, float now)
        {
            if (personal == null) return;
            bool shown = view != null && view.Spots.ContainsKey(personal.Group);
            bool on = view != null && view.OnSpot == personal.Group;
            if ((personal.Entered && !on && !shown) || (!personal.Entered && !shown && now > personal.Expires))
                _jtPersonal.Remove(id);
        }

        private static void DropJtSpot(CCSPlayerController? player, JtView view, NadeMarkerGroup g)
        {
            if (!view.Spots.Remove(g, out var spot)) return;
            foreach (var t in spot.Targets)
                try { if (t != null && t.IsValid) t.Remove(); } catch { }
        }

        private void ClearJtView(CCSPlayerController? player, ulong id)
        {
            if (!_jtViews.Remove(id, out var view)) return;
            foreach (var g in view.Spots.Keys.ToList())
                DropJtSpot(player, view, g);
            if (view.HintShown && player != null && player.IsValid)
                HideJtHint(player);
        }

        // On any lineup position of the spot (a marker groups lineups saved within 45 units).
        private static bool JtOnSpot(NadeMarkerGroup g, Vector o)
        {
            foreach (var l in g.Lineups)
                if (JtInZone(l.Pos.PlayerPosition, o))
                    return true;
            return JtInZone(g.Pos, o);
        }

        // Height window covers a jumpthrow (up to ~60 units) and small slopes.
        private static bool JtInZone(Vector spot, Vector o) =>
            JtHorizontalDistSq(spot, o) <= JtZoneRadiusSq && o.Z - spot.Z > -12.0f && o.Z - spot.Z < 80.0f;

        // F on a spot cycles that spot's lineup (also when not aiming at the marker).
        private bool ToggleJtZoneLineup(CCSPlayerController player)
        {
            if (!_jtViews.TryGetValue(player.SteamID, out var view) || view.OnSpot == null || view.OnSpot.Lineups.Count <= 1)
                return false;
            var g = view.OnSpot;
            g.Current = (g.Current + 1) % g.Lineups.Count;
            RefreshNadeGroupLabel(g);
            return true;
        }

        // ── targets ──────────────────────────────────────────────────────────────────────

        private CBaseEntity? SpawnJtTarget(NadeLineup lu)
        {
            float eyeHeight = ThrowKey(lu.Throw) == "crouch" ? JtEyeHeightDucked : JtEyeHeight;
            Vector eye = new(lu.Pos.PlayerPosition.X, lu.Pos.PlayerPosition.Y, lu.Pos.PlayerPosition.Z + eyeHeight);
            double pitch = lu.Pos.PlayerAngle.X * Math.PI / 180.0, yaw = lu.Pos.PlayerAngle.Y * Math.PI / 180.0;
            var dir = new Vector((float)(Math.Cos(pitch) * Math.Cos(yaw)), (float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)-Math.Sin(pitch));

            Vector hit = new(eye.X + dir.X * 600.0f, eye.Y + dir.Y * 600.0f, eye.Z + dir.Z * 600.0f);
            Vector? normal = null;
#if HAS_CSS_TRACE
            if (HasCssTraceApi)
            {
                try
                {
                    var r = TraceJtAim(eye, dir);
                    if (r.HasValue && JtDistSq(r.Value.Hit, eye) <= JtMaxTargetDistance * JtMaxTargetDistance)
                    {
                        hit = r.Value.Hit;
                        normal = r.Value.Normal;
                    }
                }
                catch { }
            }
#endif
            // Facing: along the surface normal when one was hit, else back toward the player.
            float faceYaw, facePitch;
            if (normal != null)
            {
                faceYaw = (float)(Math.Atan2(normal.Y, normal.X) * 180.0 / Math.PI);
                facePitch = (float)(-Math.Asin(Math.Clamp(normal.Z, -1f, 1f)) * 180.0 / Math.PI);
            }
            else
            {
                faceYaw = lu.Pos.PlayerAngle.Y + 180.0f;
                facePitch = -lu.Pos.PlayerAngle.X;
            }
            var extra = ParseJtAngles(lineupTargetAngles.Value);

            if (_jtModelsPrecached)
            {
                var prop = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
                if (prop == null) return null;
                prop.SetModel(JtTargetModel);
                prop.Spawnflags = 256u; // no collisions
                // The target model is a flat ring whose face points along its local up axis: pitch +90
                // turns that axis onto the facing direction (a ring flat on the wall, not lying on it).
                prop.Teleport(hit, new QAngle(facePitch + 90.0f + extra.X, faceYaw + extra.Y, extra.Z), new Vector(0, 0, 0));
                prop.DispatchSpawn();
                prop.Render = JtTargetColor;
                Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_clrRender");
                return prop;
            }

            // Fallback ring (models not precached on this map yet).
            var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (text == null) return null;
            text.MessageText = "( o )";
            text.Color = JtTargetColor;
            text.FontName = "Arial Bold";
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bEnabled", true);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flFontSize", 60.0f);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flWorldUnitsPerPx", 0.25f);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bFullbright", true);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyHorizontal", 1);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyVertical", 1);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nReorientMode", 1);
            text.Teleport(hit, new QAngle(0, lu.Pos.PlayerAngle.Y, 90), new Vector(0, 0, 0));
            text.DispatchSpawn();
            return text;
        }

        private static void SetJtTargetSelected(CBaseEntity target, bool selected)
        {
            if (!target.IsValid) return;
            if (target.DesignerName == "point_worldtext")
            {
                var text = target.As<CPointWorldText>();
                text.Color = selected ? JtTargetGlow : JtTargetColor;
                Utilities.SetStateChanged(text, "CPointWorldText", "m_Color");
                return;
            }
            var model = target.As<CBaseModelEntity>();
            model.Glow.GlowColorOverride = JtTargetGlow;
            model.Glow.GlowRange = 0;
            model.Glow.GlowRangeMin = 0;
            model.Glow.GlowTeam = -1;
            model.Glow.GlowType = selected ? 3 : 0;
            Utilities.SetStateChanged(model, "CBaseModelEntity", "m_Glow");
        }

        private static Vector ParseJtAngles(string s)
        {
            var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float f(int i) => i < p.Length && float.TryParse(p[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
            return new Vector(f(0), f(1), f(2));
        }

#if HAS_CSS_TRACE
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (Vector Hit, Vector Normal)? TraceJtAim(Vector eye, Vector dir)
        {
            var opts = new TraceOptions { InteractsWith = Masks.Solid };
            var end = new Vector(eye.X + dir.X * JtTraceLength, eye.Y + dir.Y * JtTraceLength, eye.Z + dir.Z * JtTraceLength);
            var tr = Trace.TraceEndShape(eye, end, null, opts);
            if (!tr.DidHit())
                return null;
            var n = tr.Normal;
            // A little off the surface so the model does not z-fight with it.
            var hit = new Vector(tr.HitPoint.X + n.X * 1.5f, tr.HitPoint.Y + n.Y * 1.5f, tr.HitPoint.Z + n.Z * 1.5f);
            return (hit, new Vector(n.X, n.Y, n.Z));
        }
#endif

        // Targets go only to the player standing on the spot. Never throws (transmit path).
        private void OnJtCheckTransmit(CCheckTransmitInfoList infoList)
        {
            if (_jtViews.Count == 0) return;
            try
            {
                foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
                {
                    if (player == null || !player.IsValid) continue;
                    foreach (var (id, view) in _jtViews)
                    {
                        if (id == player.SteamID) continue;
                        foreach (var spot in view.Spots.Values)
                            foreach (var t in spot.Targets)
                                if (t != null && t.IsValid) info.TransmitEntities.Remove(t);
                    }
                }
            }
            catch { }
        }

        // ── hud hint ─────────────────────────────────────────────────────────────────────

        private CEnvHudHint? JtHintEntity()
        {
            if (_jtHint != null && _jtHint.IsValid)
                return _jtHint;
            _jtHint = Utilities.CreateEntityByName<CEnvHudHint>("env_hudhint");
            _jtHint?.DispatchSpawn();
            return _jtHint;
        }

        // The hint shows to the activator only and stays until hidden.
        private void ShowJtHint(CCSPlayerController player, string message)
        {
            try
            {
                var pawn = player.PlayerPawn.Value;
                var hint = JtHintEntity();
                if (pawn == null || hint == null) return;
                hint.Message = message;
                hint.AcceptInput("ShowHudHint", pawn, pawn);
            }
            catch (Exception e)
            {
                Log($"[NadeTutorial] hint: {e.Message}");
            }
        }

        private void HideJtHint(CCSPlayerController player)
        {
            try
            {
                var pawn = player.PlayerPawn.Value;
                var hint = _jtHint != null && _jtHint.IsValid ? _jtHint : null;
                if (pawn == null || hint == null) return;
                hint.AcceptInput("HideHudHint", pawn, pawn);
            }
            catch { }
        }

        // ── marker parts (used by DrawNadeMarkerGroup) ─────────────────────────────────

        // The ghost in place of the grenade glow, when the models are precached.
        private CBaseModelEntity? SpawnJtGhost(Vector pos, float yaw)
        {
            if (!_jtModelsPrecached) return null;
            var ghost = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
            if (ghost == null) return null;
            ghost.SetModel(JtGhostModel);
            ghost.Spawnflags = 256u;
            ghost.Teleport(new Vector(pos.X, pos.Y, pos.Z), new QAngle(0, yaw, 0), new Vector(0, 0, 0));
            ghost.DispatchSpawn();
            // See-through like the workshop trainers' ghost.
            ghost.RenderMode = RenderMode_t.kRenderTransAlpha;
            ghost.Render = JtGhostColor;
            Utilities.SetStateChanged(ghost, "CBaseModelEntity", "m_clrRender");
            ghost.Glow.GlowColorOverride = JtGhostGlow;
            ghost.Glow.GlowRange = NadeGlowRange;
            ghost.Glow.GlowRangeMin = 0;
            ghost.Glow.GlowTeam = -1;
            ghost.Glow.GlowType = 3;
            Utilities.SetStateChanged(ghost, "CBaseModelEntity", "m_Glow");
            return ghost;
        }


        // The previous map's entities are gone: forget them without touching them.
        private void ForgetJtOnMapStart()
        {
            _jtViews.Clear();
            _jtPersonal.Clear();
            _jtHint = null;
            _jtTimer = null; // STOP_ON_MAPCHANGE already removed it
            if (_jtTransmit != null)
            {
                RemoveListener(_jtTransmit);
                _jtTransmit = null;
            }
        }
    }
}
