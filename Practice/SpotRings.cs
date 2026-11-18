using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace MatchZy
{
    // Aim rings of a lineup spot, as lineup workshop maps have them (the author's pictures,
    // 2026-11-18): a player who stands on a saved spot sees one thin ring per lineup thrown from
    // there, where that lineup is aimed. Red; the one the crosshair is in is green. Nothing has
    // to be loaded or pressed first, and the rings go when the player walks off.
    public partial class MatchZy
    {
        private sealed class SpotRing
        {
            public float Pitch, Yaw;
            // The eye height the ring was drawn for.
            public float EyeZ;
            public List<uint> Beams = new();
            public bool On;
        }

        private sealed class SpotRingView
        {
            public NadeMarkerGroup Group = null!;
            public List<SpotRing> Rings = new();
        }

        private readonly Dictionary<int, SpotRingView> spotRingViews = new();
        // How far the aim is followed to a surface; nothing there: the ring hangs this far out.
        private const float SpotRingMaxDistance = 1200f;
        private const float SpotRingAirDistance = 600f;
        // The crosshair counts as in a ring within this much of that lineup's aim.
        private const float SpotRingDegrees = 0.5f;

        private void TickSpotRings()
        {
            if (Server.TickCount % 4 != 0)
                return;
            if (!practiceAimRings.Value || (!grenadeLibraryActive && _jtPersonal.Count == 0))
            {
                if (spotRingViews.Count > 0)
                    ClearSpotRings();
                return;
            }
            var seen = new HashSet<int>();
            foreach (var player in Utilities.GetPlayers())
            {
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !player.IsValid || player.IsBot || !player.PawnIsAlive || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                    continue;
                int slot = player.Slot;
                var origin = pawn.AbsOrigin;
                NadeMarkerGroup? on = null;
                if (grenadeLibraryActive && _nadeViewers.Contains(player.SteamID))
                    on = activeNadeGroups.FirstOrDefault(g => LandingGroupShown(slot, g) && JtOnSpot(g, origin));
                if (on == null && _jtPersonal.TryGetValue(player.SteamID, out var personal) && JtOnSpot(personal.Group, origin))
                    on = personal.Group;
                if (on == null)
                    continue;
                seen.Add(slot);
                if (!spotRingViews.TryGetValue(slot, out var view) || !ReferenceEquals(view.Group, on) || view.Rings.Count != on.Lineups.Count)
                {
                    DropSpotRings(slot);
                    view = new SpotRingView { Group = on };
                    foreach (var lu in on.Lineups)
                        view.Rings.Add(DrawSpotRing(lu, pawn, SpotRingEyeZ(lu, pawn)));
                    spotRingViews[slot] = view;
                }
                for (int i = 0; i < view.Rings.Count; i++)
                {
                    // Drawn for the eye the player really has there: the saved spot plus 64 is
                    // not it on a slope (61 to 63), and a ring 3 u too high sat above the
                    // crosshair with the aim exactly right (find5, 2026-11-18).
                    float eyeZ = SpotRingEyeZ(on.Lineups[i], pawn);
                    if (MathF.Abs(eyeZ - view.Rings[i].EyeZ) > 0.4f)
                    {
                        KillFinderEntities(view.Rings[i].Beams);
                        view.Rings[i] = DrawSpotRing(on.Lineups[i], pawn, eyeZ);
                    }
                    var ring = view.Rings[i];
                    float dPitch = pawn.EyeAngles.X - ring.Pitch, dYaw = pawn.EyeAngles.Y - ring.Yaw;
                    while (dYaw > 180f) dYaw -= 360f;
                    while (dYaw < -180f) dYaw += 360f;
                    bool aimed = dPitch * dPitch + dYaw * dYaw <= SpotRingDegrees * SpotRingDegrees;
                    if (aimed == ring.On)
                        continue;
                    ring.On = aimed;
                    foreach (uint raw in ring.Beams)
                    {
                        var beam = new CHandle<CBeam>(raw).Value;
                        if (beam == null || !beam.IsValid)
                            continue;
                        beam.Render = aimed ? AimMarkOn : AimMarkOff;
                        Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");
                    }
                }
            }
            foreach (int slot in spotRingViews.Keys.Where(s => !seen.Contains(s)).ToList())
                DropSpotRings(slot);
        }

        // One ring: on the surface the lineup's aim meets (a little in front of it), turned to the
        // player, its size growing with the distance so it looks the same from the spot.
        // The height of the eye a lineup's ring is drawn for: the player's own while they stand
        // on that lineup's exact place, on the ground; else the saved place plus a standing eye.
        private float SpotRingEyeZ(NadeLineup lu, CCSPlayerPawn pawn)
        {
            float saved = lu.Pos.PlayerPosition.Z + (ThrowKey(lu.Throw) == "crouch" ? JtEyeHeightDucked : JtEyeHeight);
            var o = pawn.AbsOrigin;
            if (o == null || (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0)
                return saved;
            float dx = o.X - lu.Pos.PlayerPosition.X, dy = o.Y - lu.Pos.PlayerPosition.Y;
            if (dx * dx + dy * dy > 3f * 3f || (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 != (ThrowKey(lu.Throw) == "crouch"))
                return saved;
            return o.Z + pawn.ViewOffset.Z;
        }

        private SpotRing DrawSpotRing(NadeLineup lu, CCSPlayerPawn pawn, float eyeZ)
        {
            var ring = new SpotRing { Pitch = lu.Pos.PlayerAngle.X, Yaw = lu.Pos.PlayerAngle.Y, EyeZ = eyeZ };
            try
            {
                var eye = new Vector3(lu.Pos.PlayerPosition.X, lu.Pos.PlayerPosition.Y, eyeZ);
                var look = GrenadeSimLogic.Forward(ring.Pitch, ring.Yaw);
                float reach = SpotRingAirDistance;
                if (FinderTrace(eye, eye + look * SpotRingMaxDistance, out var hit, out _, out _, pawn))
                    reach = Math.Max(32f, Vector3.Distance(eye, hit) - 4f);
                var mark = eye + look * reach;
                var side = GrenadeSimLogic.Heading(ring.Yaw - 90f);
                var upward = Vector3.Normalize(Vector3.Cross(side, look));
                float radius = Math.Max(2f, reach * 0.035f), thick = Math.Max(0.5f, reach * 0.005f);
                Vector3 At(float deg, float r) => mark + (side * MathF.Cos(deg * MathF.PI / 180f) + upward * MathF.Sin(deg * MathF.PI / 180f)) * r;
                void Line(Vector3 a, Vector3 b)
                {
                    uint? beam = FinderBeam(a, b, AimMarkOff, thick);
                    if (beam.HasValue)
                        ring.Beams.Add(beam.Value);
                }
                const int parts = 16;
                for (int i = 0; i < parts; i++)
                    Line(At(i * 360f / parts, radius), At((i + 1) * 360f / parts, radius));
                Line(At(90f, radius), At(90f, radius * 0.3f));
                Line(At(270f, radius), At(270f, radius * 0.3f));
            }
            catch (Exception e)
            {
                Log($"[SpotRings] draw: {e.GetType().Name}: {e.Message}");
            }
            return ring;
        }

        // A spot's rings are sent only to the player standing on it (the author's call,
        // 2026-11-18). Registered once at load; never throws (transmit path).
        private void OnSpotRingCheckTransmit(CCheckTransmitInfoList infoList)
        {
            if (spotRingViews.Count == 0)
                return;
            try
            {
                // Resolved once per call, not once per recipient.
                spotRingTransmit.Clear();
                foreach (var (slot, view) in spotRingViews)
                    foreach (var ring in view.Rings)
                        foreach (uint raw in ring.Beams)
                        {
                            var beam = new CHandle<CBeam>(raw).Value;
                            if (beam != null && beam.IsValid)
                                spotRingTransmit.Add((slot, beam));
                        }
                if (spotRingTransmit.Count == 0)
                    return;
                foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
                {
                    if (player == null || !player.IsValid)
                        continue;
                    int viewer = player.Slot;
                    foreach (var (owner, beam) in spotRingTransmit)
                        if (owner != viewer)
                            info.TransmitEntities.Remove(beam);
                }
            }
            catch
            {
            }
            finally
            {
                spotRingTransmit.Clear();
            }
        }

        private readonly List<(int Owner, CBeam Beam)> spotRingTransmit = new();

        private void RegisterSpotRings() => RegisterListener<Listeners.CheckTransmit>(OnSpotRingCheckTransmit);

        private void DropSpotRings(int slot)
        {
            if (!spotRingViews.Remove(slot, out var view))
                return;
            foreach (var ring in view.Rings)
                KillFinderEntities(ring.Beams);
        }

        private void ClearSpotRings()
        {
            foreach (int slot in spotRingViews.Keys.ToList())
                DropSpotRings(slot);
        }
    }
}
