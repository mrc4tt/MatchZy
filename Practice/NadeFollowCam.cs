using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Grenade follow camera (practice, per player). .follownade cycles the mode:
    //   lineup (default) - automatic for a throw from a lineup trainer spot (where the hud hint shows),
    //   always           - every throw,
    //   off              - never.
    //
    // A view entity (m_hViewEntity) on a live player crashed the server in earlier attempts (see the
    // removed gcam). This moves the player's own pawn instead, the way lineup workshop maps do it:
    // the pawn goes noclip and invisible and is placed behind the grenade every tick (smoothed), so
    // the view is the pawn's own view, like flying with noclip. After the grenade lands the camera
    // backs off to look at the landing spot. Jump (or 8 s after the landing, or death, or leaving
    // practice) ends it: the pawn is visible again, walks again and is put back where it threw.
    // A cyan trail marks the flight.
    public partial class MatchZy
    {
        private sealed class NadeFollow
        {
            // Raw entity handles (index + serial): a bare index can be reused by another entity.
            public uint ProjectileRaw;
            public uint WeaponRaw = uint.MaxValue; // the weapon made invisible at the start
            public Vector StartPos = null!;
            public QAngle StartAng = null!;
            public Vector CamPos = null!;
            public Vector? LastNadePos;
            public Vector? Landing;
            public float LandedAt = -1;
            public float StartedAt;
            public float Dist = NfDistFollow, PitchOffset = NfPitchFollow;
            public float TargetDist = NfDistFollow, TargetPitchOffset = NfPitchFollow;
            public int TrailTick;
            // The pawn's own move type before the camera (.noclip stays on after it).
            public MoveType_t PrevMove = MoveType_t.MOVETYPE_WALK;
            // The camera takes over only once the grenade is a little away from the thrower
            // (until then the player sees their own throw as usual).
            public bool Following;
            public uint PrevFov;
            public bool FovSet;
            public bool Smoke;
        }

        // Set up as lineup workshop maps have it: close behind the grenade in flight, far back
        // once it has landed, a wide view, the first look slightly from above.
        private const float NfDistFollow = 108.0f;   // behind the grenade while it flies
        private const float NfStartFactor = 1.25f;   // takes over when the grenade is this many follow distances away
        private const float NfStartPitch = 25.0f;    // first look: from above, down at the grenade
        private const uint NfFov = 95;
        private const float NfSmokeLift = 32.0f;     // a landed smoke is looked at a little above the floor
        // 0: the eye (not the feet) is put on the line behind the grenade, so the grenade sits on
        // the crosshair whichever way the player looks.
        private const float NfPitchFollow = 0.0f;
        private const float NfDistLanded = 608.0f;   // backed off once it lands
        private const float NfPitchLanded = 0.0f;
        private const float NfMinDist = 44.0f;       // pulled in no closer than this when a wall is in the way
        private const float NfFollowLerp = 0.3125f;  // per tick (20 x the tick time), lower = smoother / more lag
        private const float NfLandedSeconds = 8.0f;
        private const float NfMaxSeconds = 30.0f;
        private const float NfStillAtCamSq = 96.0f * 96.0f; // a tick of falling is far less    // hard stop (a stuck decoy, a lost projectile)

        private enum NadeFollowMode { Lineup, Always, Off }

        // Players not listed use the default (Lineup).
        private readonly Dictionary<ulong, NadeFollowMode> _nadeFollowEnabled = new();
        private readonly Dictionary<ulong, NadeFollow> _nadeFollows = new();
        private static readonly Color NfTrailColor = Color.FromArgb(255, 0, 200, 255);
        private const float NfTrailSeconds = 3.0f;
        // Trail beams waiting to be removed, oldest first (raw handle, Server.CurrentTime to remove at).
        // Drained from the tick: a timer per beam was ~21 timers a second for every running camera.
        private readonly Queue<(uint Raw, float KillAt)> _nfTrailBeams = new();

        private void RegisterNadeFollowCam()
        {
            RegisterListener<Listeners.OnTick>(OnNadeFollowTick);
            RegisterListener<Listeners.OnTick>(OnNadeSpeedTick);
            RegisterLookMenu();
            RegisterSpotRings();
        }

        [ConsoleCommand("css_follownade", "Practice: the camera follows your thrown grenade (toggle)")]
        public void OnFollowNadeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            ulong id = player!.SteamID;
            var mode = _nadeFollowEnabled.GetValueOrDefault(id, NadeFollowMode.Lineup) switch
            {
                NadeFollowMode.Lineup => NadeFollowMode.Always,
                NadeFollowMode.Always => NadeFollowMode.Off,
                _ => NadeFollowMode.Lineup,
            };
            _nadeFollowEnabled[id] = mode;
            if (mode == NadeFollowMode.Off)
                StopNadeFollow(player, id, putBack: true);
            string key = mode switch
            {
                NadeFollowMode.Always => "matchzy.pracmisc.follownadealways",
                NadeFollowMode.Off => "matchzy.pracmisc.follownadeoff",
                _ => "matchzy.pracmisc.follownadelineup",
            };
            ReplyToUserCommand(player, Localizer.ForPlayer(player, key));
        }

        // From OnEntitySpawnedHandler (next frame after the projectile spawned, thrower resolved).
        // The last grenade each player threw: the inspect key (F) while it is still in the air
        // puts the camera on it, whatever the mode. No bind to set up, no chat command.
        private readonly Dictionary<ulong, uint> _nadeFollowLast = new();

        private void TryStartNadeFollow(CCSPlayerController player, CBaseCSGrenadeProjectile projectile, bool byKey = false)
        {
            ulong id = player.SteamID;
            if (!isPractice || _nadeFollows.ContainsKey(id))
                return;
            if (!byKey && projectile.IsValid)
                _nadeFollowLast[id] = projectile.EntityHandle.Raw;
            // A throw during a recording (.rec, .stratrec, .stratslot or their countdown) would put
            // the camera flight into the recorded path.
            if (IsPracticeRecordingFor(id))
                return;
            var mode = _nadeFollowEnabled.GetValueOrDefault(id, NadeFollowMode.Lineup);
            bool onLineupSpot = _jtViews.TryGetValue(id, out var view) && JtGroupAlive(id, view.OnSpot);
            // A grenade finder lineup counts as a lineup spot too (the throw may be a running one).
            var here = player.PlayerPawn.Value?.AbsOrigin;
            if (!onLineupSpot && here != null && finderArmed.TryGetValue(player.Slot, out var armedFinder))
            {
                float fdx = here.X - armedFinder.Plan.Feet.X, fdy = here.Y - armedFinder.Plan.Feet.Y;
                onLineupSpot = fdx * fdx + fdy * fdy < 200f * 200f;
            }
            if (!byKey && (mode == NadeFollowMode.Off || (mode == NadeFollowMode.Lineup && !onLineupSpot)))
                return;
            var pawn = player.PlayerPawn.Value;
            var o = pawn?.AbsOrigin;
            if (pawn == null || !pawn.IsValid || o == null || !player.PawnIsAlive || !projectile.IsValid)
                return;
            // Frozen (MOVETYPE_NONE: a countdown, a coach): the camera would unfreeze the pawn, and a
            // freeze lifted meanwhile would be put back afterwards.
            if (pawn.MoveType == MoveType_t.MOVETYPE_NONE)
                return;
            var eye = pawn.EyeAngles;
            var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
            var f = new NadeFollow
            {
                ProjectileRaw = projectile.EntityHandle.Raw,
                WeaponRaw = weapon != null && weapon.IsValid ? weapon.EntityHandle.Raw : uint.MaxValue,
                StartPos = new Vector(o.X, o.Y, o.Z),
                StartAng = new QAngle(eye.X, eye.Y, 0),
                CamPos = new Vector(o.X, o.Y, o.Z),
                StartedAt = Server.CurrentTime,
                PrevMove = pawn.MoveType,
            };
            _nadeFollows[id] = f;
            // The lineup hint goes while the camera flies (it comes back on the spot afterwards).
            if (view != null && view.HintShown)
            {
                HideJtHint(player);
                view.HintShown = false;
                view.HintFor = -1;
            }
            f.Smoke = projectile.DesignerName == "smokegrenade_projectile";
        }

        // The grenade is far enough away: the pawn becomes the camera (noclip, invisible, wide
        // view) right behind and above the grenade, looking down at it along the throw direction.
        private void BeginNadeFollowFlight(CCSPlayerController player, CCSPlayerPawn pawn, NadeFollow f, Vector grenade)
        {
            f.Following = true;
            f.PrevMove = pawn.MoveType;
            pawn.MoveType = MoveType_t.MOVETYPE_NOCLIP;
            pawn.ActualMoveType = MoveType_t.MOVETYPE_NOCLIP;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            SetNadeFollowVisible(pawn, false, f.WeaponRaw);
            f.PrevFov = player.DesiredFOV;
            f.FovSet = true;
            player.DesiredFOV = NfFov;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
            float yaw = pawn.EyeAngles.Y;
            double y = yaw * Math.PI / 180.0, p = NfStartPitch * Math.PI / 180.0;
            f.CamPos = new Vector(
                grenade.X - (float)(Math.Cos(p) * Math.Cos(y)) * NfDistFollow,
                grenade.Y - (float)(Math.Cos(p) * Math.Sin(y)) * NfDistFollow,
                grenade.Z + (float)Math.Sin(p) * NfDistFollow - pawn.ViewOffset.Z);
            // Only a teleport with an angle turns the player's view (hard rule 10); the tilted
            // body does not matter, the pawn is invisible until the camera ends.
            pawn.Teleport(f.CamPos, new QAngle(NfStartPitch, yaw, 0), new Vector(0, 0, 0));
            player.PrintToCenterAlert(Localizer.ForPlayer(player, "matchzy.pracmisc.follownadereturn"));
        }

        // The weapon is the one stored at the start (by raw handle), so the stop restores the same
        // weapon even when the player switched weapons meanwhile.
        private static void SetNadeFollowVisible(CCSPlayerPawn pawn, bool visible, uint weaponRaw)
        {
            try
            {
                var c = visible ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(0, 255, 255, 255);
                pawn.RenderMode = RenderMode_t.kRenderTransAlpha;
                pawn.Render = c;
                Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
                var weapon = weaponRaw == uint.MaxValue ? null : new CHandle<CBasePlayerWeapon>(weaponRaw).Value;
                if (weapon != null && weapon.IsValid)
                {
                    weapon.RenderMode = RenderMode_t.kRenderTransAlpha;
                    weapon.Render = c;
                    Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");
                }
            }
            catch { }
        }

        public void OnNadeFollowTick()
        {
            if (_nadeFollowLast.Count > 0)
            {
                try
                {
                    foreach (var (who, raw) in _nadeFollowLast.ToList())
                    {
                        var thrown = new CHandle<CBaseCSGrenadeProjectile>(raw).Value;
                        if (!isPractice || thrown == null || !thrown.IsValid)
                        {
                            _nadeFollowLast.Remove(who); // landed and gone, or went off
                            continue;
                        }
                        if (_nadeFollows.ContainsKey(who))
                            continue;
                        var presser = Utilities.GetPlayerFromSteamId(who);
                        if (presser == null || !presser.IsValid || !presser.PawnIsAlive || (presser.Buttons & PlayerButtons.Inspect) == 0)
                            continue;
                        _nadeFollowLast.Remove(who);
                        TryStartNadeFollow(presser, thrown, byKey: true);
                    }
                }
                catch (Exception e)
                {
                    Log($"[NadeFollow] key: {e.Message}");
                    _nadeFollowLast.Clear();
                }
            }
            if (_nadeFollows.Count == 0 && _nfTrailBeams.Count == 0)
                return;
            try
            {
                float now = Server.CurrentTime;
                while (_nfTrailBeams.Count > 0 && _nfTrailBeams.Peek().KillAt <= now)
                    KillNadeFollowTrailBeam(_nfTrailBeams.Dequeue().Raw);
                foreach (var (id, f) in _nadeFollows.ToList())
                {
                    var player = Utilities.GetPlayerFromSteamId(id);
                    var pawn = player != null && player.IsValid ? player.PlayerPawn.Value : null;
                    if (!isPractice || player == null || pawn == null || !pawn.IsValid || !player.PawnIsAlive)
                    {
                        StopNadeFollow(player, id, putBack: player != null && player.IsValid && player.PawnIsAlive);
                        continue;
                    }
                    if (!f.Following)
                    {
                        var early = new CHandle<CBaseCSGrenadeProjectile>(f.ProjectileRaw).Value;
                        var eo = pawn.AbsOrigin;
                        if (early == null || !early.IsValid || early.AbsOrigin == null || eo == null || pawn.MoveType == MoveType_t.MOVETYPE_NONE)
                        {
                            _nadeFollows.Remove(id); // gone before the camera took over: nothing to undo
                            continue;
                        }
                        var ga = early.AbsOrigin;
                        float ex = ga.X - eo.X, ey = ga.Y - eo.Y, ez = ga.Z - (eo.Z + pawn.ViewOffset.Z);
                        float limit = NfDistFollow * NfStartFactor;
                        if (ex * ex + ey * ey + ez * ez < limit * limit && now - f.StartedAt < 1.0f)
                            continue;
                        // Back to where they stood when the camera took over is wrong for a jumpthrow
                        // (in the air) or a running throw: a finder lineup has its own spot.
                        if (finderArmed.TryGetValue(player.Slot, out var armedNow))
                        {
                            float sx = f.StartPos.X - armedNow.Plan.Feet.X, sy = f.StartPos.Y - armedNow.Plan.Feet.Y;
                            if (sx * sx + sy * sy < 200f * 200f)
                            {
                                f.StartPos = new Vector(armedNow.Plan.Feet.X, armedNow.Plan.Feet.Y, armedNow.Plan.Feet.Z + 0.1f);
                                f.StartAng = new QAngle(armedNow.Plan.Pitch, armedNow.Plan.Yaw, 0);
                            }
                        }
                        BeginNadeFollowFlight(player, pawn, f, new Vector(ga.X, ga.Y, ga.Z));
                        continue;
                    }
                    // Something else changed the move type: the camera is over. A practice teleport
                    // (.loadnade, .back) moved the pawn away from the camera spot: it stays where that
                    // put it. Still at the camera spot (.noclip turned off, a freeze): put it back where
                    // it threw, not left inside a wall or in the air.
                    if (pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP)
                    {
                        var at = pawn.AbsOrigin;
                        bool atCam = at != null && JtDistSq(at, f.CamPos) <= NfStillAtCamSq;
                        StopNadeFollow(player, id, putBack: atCam);
                        continue;
                    }
                    // Jump, a click or use ends it (the throw's own click is over after 0.4 s).
                    bool jump = (player.Buttons & (PlayerButtons.Jump | PlayerButtons.Attack | PlayerButtons.Attack2 | PlayerButtons.Use)) != 0
                        && now - f.StartedAt > 0.4f;
                    bool expired = now - f.StartedAt > NfMaxSeconds || (f.LandedAt >= 0 && now - f.LandedAt > NfLandedSeconds);
                    if (jump || expired)
                    {
                        StopNadeFollow(player, id, putBack: true);
                        continue;
                    }

                    f.Dist += (f.TargetDist - f.Dist) * 0.3f;
                    f.PitchOffset += (f.TargetPitchOffset - f.PitchOffset) * 0.2f;

                    Vector? target = f.Landing;
                    // The pawn is given the grenade's own speed while it flies: the client then
                    // carries the view on between ticks instead of stepping (smoother to watch).
                    Vector camVelocity = new(0, 0, 0);
                    var proj = new CHandle<CBaseCSGrenadeProjectile>(f.ProjectileRaw).Value;
                    if (proj != null && proj.IsValid && proj.AbsOrigin != null && f.Landing == null)
                    {
                        var g = proj.AbsOrigin;
                        var gp = new Vector(g.X, g.Y, g.Z);
                        var v = proj.AbsVelocity;
                        float speed = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
                        camVelocity = new Vector(v.X, v.Y, v.Z);
                        if (f.LastNadePos != null && ++f.TrailTick % 3 == 0)
                            DrawNadeFollowTrail(f.LastNadePos, gp);
                        if (f.TrailTick % 3 == 0)
                            f.LastNadePos = gp;
                        f.LastNadePos ??= gp;
                        if (speed < 5.0f && now - f.StartedAt > 0.3f)
                            LandNadeFollow(f, gp, now);
                        target = gp;
                    }
                    else if (f.Landing == null)
                    {
                        // Projectile gone (detonated, removed): back off at its last spot.
                        if (f.LastNadePos == null)
                        {
                            StopNadeFollow(player, id, putBack: true);
                            continue;
                        }
                        LandNadeFollow(f, f.LastNadePos, now);
                        target = f.LastNadePos;
                    }
                    if (target == null)
                        continue;
                    if (f.Smoke && f.Landing != null)
                        target = new Vector(target.X, target.Y, target.Z + NfSmokeLift);

                    // Behind the target along the player's own view (they can look around).
                    var eye = pawn.EyeAngles;
                    double yaw = eye.Y * Math.PI / 180.0, pitch = (eye.X + f.PitchOffset) * Math.PI / 180.0;
                    float fx = (float)(Math.Cos(pitch) * Math.Cos(yaw)), fy = (float)(Math.Cos(pitch) * Math.Sin(yaw)), fz = (float)-Math.Sin(pitch);
                    float dist = f.Dist;
                    // A wall between the grenade and the camera: pull the camera in front of it
                    // (a noclip view inside a wall shows the map from the outside).
                    if (HasCssTraceApi)
                    {
                        var from = new System.Numerics.Vector3(target.X, target.Y, target.Z + 2f);
                        var to = from - new System.Numerics.Vector3(fx, fy, fz) * dist;
                        if (FinderTrace(from, to, out _, out _, out float frac, pawn))
                            dist = Math.Max(NfMinDist, dist * frac - 10f);
                    }
                    float dx = target.X - fx * dist, dy = target.Y - fy * dist, dz = target.Z + 2f - fz * dist - pawn.ViewOffset.Z;
                    f.CamPos = new Vector(
                        f.CamPos.X + (dx - f.CamPos.X) * NfFollowLerp,
                        f.CamPos.Y + (dy - f.CamPos.Y) * NfFollowLerp,
                        f.CamPos.Z + (dz - f.CamPos.Z) * NfFollowLerp);
                    pawn.Teleport(f.CamPos, null, camVelocity);
                }
            }
            catch (Exception e)
            {
                Log($"[NadeFollow] {e.Message}");
            }
        }

        private static void LandNadeFollow(NadeFollow f, Vector at, float now)
        {
            f.Landing = new Vector(at.X, at.Y, at.Z);
            f.LandedAt = now;
            f.TargetDist = NfDistLanded;
            f.TargetPitchOffset = NfPitchLanded;
        }

        private void DrawNadeFollowTrail(Vector from, Vector to)
        {
            try
            {
                var beam = Utilities.CreateEntityByName<CBeam>("beam");
                if (beam == null) return;
                beam.LifeState = 1;
                beam.Width = 1.5f;
                beam.Render = NfTrailColor;
                beam.EndPos.X = to.X;
                beam.EndPos.Y = to.Y;
                beam.EndPos.Z = to.Z;
                beam.Teleport(from, new QAngle(0, 0, 0), new Vector(0, 0, 0));
                beam.DispatchSpawn();
                _nfTrailBeams.Enqueue((beam.EntityHandle.Raw, Server.CurrentTime + NfTrailSeconds));
            }
            catch { }
        }

        // Raw handle + entity IO Kill: the wrapper may point at a freed or reused entity.
        private static void KillNadeFollowTrailBeam(uint raw)
        {
            try
            {
                var b = new CHandle<CBeam>(raw).Value;
                if (b != null && b.IsValid) b.AcceptInput("Kill");
            }
            catch { }
        }

        // Walk again, visible again, back where the throw was made (when putBack).
        // unloading: no NextFrame follow-ups (the plugin is going away); one direct teleport and one
        // body flatten instead of TeleportUpright's multi-frame flatten.
        private void StopNadeFollow(CCSPlayerController? player, ulong id, bool putBack, bool unloading = false)
        {
            if (!_nadeFollows.Remove(id, out var f))
                return;
            if (f.FovSet && player != null && player.IsValid)
            {
                player.DesiredFOV = f.PrevFov;
                Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
            }
            var pawn = player != null && player.IsValid ? player.PlayerPawn.Value : null;
            if (pawn == null || !pawn.IsValid)
                return;
            if (!f.Following)
                return; // the camera never took over: nothing was changed
            // The camera's own noclip is still on (not turned off by .noclip or a freeze meanwhile):
            // only then is a previous .noclip put back after the teleport.
            bool ownNoclip = pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP;
            try
            {
                // A ladder state does not survive the move away from the ladder: walk instead.
                var restore = f.PrevMove == MoveType_t.MOVETYPE_LADDER ? MoveType_t.MOVETYPE_WALK : f.PrevMove;
                if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP && restore != MoveType_t.MOVETYPE_NOCLIP)
                {
                    pawn.MoveType = restore;
                    pawn.ActualMoveType = restore;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
                }
                SetNadeFollowVisible(pawn, true, f.WeaponRaw);
                if (putBack && player != null && player.PawnIsAlive)
                {
                    // A teleport with the saved pitch tilts the body model (the camera then looks
                    // down into the player's own body): TeleportUpright sets the view and flattens
                    // the body to yaw only, as every other practice teleport does.
                    if (unloading)
                    {
                        pawn.Teleport(f.StartPos, f.StartAng, new Vector(0, 0, 0));
                        pawn.ResetNoclipToWalk();
                        FlattenBodyRotation(pawn, f.StartAng.Y);
                    }
                    else
                    {
                        TeleportUpright(player, f.StartPos, f.StartAng);
                    }
                    if (f.PrevMove == MoveType_t.MOVETYPE_NOCLIP && ownNoclip)
                    {
                        pawn.MoveType = MoveType_t.MOVETYPE_NOCLIP;
                        pawn.ActualMoveType = MoveType_t.MOVETYPE_NOCLIP;
                        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
                    }
                }
                else if (player != null && !unloading)
                {
                    FlattenBodyRotationFrames(player, pawn.EyeAngles.Y, 6);
                }
            }
            catch (Exception e)
            {
                Log($"[NadeFollow] stop: {e.Message}");
            }
        }

        // True while the player records (.rec, .stratrec role, .stratslot take) or waits in one of
        // their countdowns, or in the countdown of a ".strat <name> me" playback.
        private bool IsPracticeRecordingFor(ulong id)
            => activePathRecordings.ContainsKey(id) || pendingRecCountdowns.ContainsKey(id)
               || IsStratRole(id) || IsSlotRecording(id) || (stratPlaybackPending && stratPlaybackOwner == id);

        // A recording or its countdown starts: a running camera ends first and the pawn goes back to
        // where it threw, so neither the freeze nor the recorded path sees the camera flight.
        private void StopNadeFollowForRecording(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid || !_nadeFollows.ContainsKey(player.SteamID))
                return;
            StopNadeFollow(player, player.SteamID, putBack: player.PawnIsAlive);
        }

        // Practice end / unload: stop and put a living pawn back where it threw (else it walks on
        // where the camera was: in a wall, in the sky). Map start: the pawns are gone, only forget.
        private void ResetNadeFollow(bool mapStart, bool unloading = false)
        {
            if (!mapStart)
                foreach (ulong id in _nadeFollows.Keys.ToList())
                {
                    try
                    {
                        var player = Utilities.GetPlayerFromSteamId(id);
                        bool alive = player != null && player.IsValid && player.PawnIsAlive;
                        StopNadeFollow(player, id, putBack: alive, unloading);
                    }
                    catch (Exception e)
                    {
                        _nadeFollows.Remove(id);
                        Log($"[NadeFollow] reset: {e.Message}");
                    }
                }
            _nadeFollows.Clear();
            _nadeFollowLast.Clear();
            // Map start: the beams went with the old map. Unload: the tick that drains them stops, so
            // they are removed now. Practice end: the tick keeps removing them as they come due.
            if (unloading)
                while (_nfTrailBeams.Count > 0)
                    KillNadeFollowTrailBeam(_nfTrailBeams.Dequeue().Raw);
            if (mapStart)
            {
                _nfTrailBeams.Clear();
                _nadeFollowEnabled.Clear();
            }
        }
    }
}
