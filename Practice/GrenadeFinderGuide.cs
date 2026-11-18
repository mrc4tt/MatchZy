using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using Vector3 = System.Numerics.Vector3;

namespace MatchZy
{
    // What the Grenade Finder's throw model gives every lineup, not only the ones it found itself:
    // the aim mark (a cross where the crosshair has to be), the calculated flight path and landing
    // point, the on-screen "what to do / ready / aim moved" line, and the verdict after the throw.
    // A lineup loaded from the saved library (.loadnade, E on a .shownades marker) is armed the
    // same way as a finder result.
    public partial class MatchZy
    {
        private const int GuidePathSegments = 28;
        private const float LibraryOnTarget = 24f;
        private static readonly Color GuidePathColor = Color.FromArgb(255, 255, 170, 40);

        // Calculated flight of a throw (hull traces against the world, the pawn ignored).
        private static GrenadeSimLogic.Flight FinderFlight(Vector3 feet, GrenadeSimLogic.ThrowKind kind, float pitch, float yaw, CBaseEntity? ignore, List<Vector3>? path = null, GrenadeSimLogic.Fuse fuse = default)
        {
            GrenadeSimLogic.TraceFn trace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                FinderGrenadeTrace(a, b, out hit, out normal, out fraction, ignore != null && ignore.IsValid ? ignore : null);
            return GrenadeSimLogic.Simulate(GrenadeSimLogic.LaunchFrom(feet, kind, pitch, yaw), trace, 20f, 1, path, fuse);
        }

        // Throw kind of a saved lineup; null when the throw is not one the model covers (walk,
        // crouch) or was never noted.
        private static GrenadeSimLogic.ThrowKind? FinderKindFromThrow(string throwType) => (throwType ?? "").Trim().ToLowerInvariant() switch
        {
            "normal throw" => GrenadeSimLogic.ThrowKind.Stand,
            "jumpthrow" => GrenadeSimLogic.ThrowKind.Jump,
            "run throw" => GrenadeSimLogic.ThrowKind.Run,
            "run jumpthrow" => GrenadeSimLogic.ThrowKind.RunJump,
            _ => null,
        };

        // Draws the guide of a lineup for one player: aim cross, flight path, landing mark, and
        // for running throws the gate. Replaces the guide they had.
        private void DrawFinderGuide(int slot, FinderResult plan, CCSPlayerPawn? pawn, bool aimMark = true)
        {
            if (finderGoEntities.Remove(slot, out var old))
                KillFinderEntities(old);
            var ents = new List<uint>();
            finderGoEntities[slot] = ents;
            if (!HasCssTraceApi)
                return;

            // Aim mark: a cross in the world exactly where the crosshair has to be. Its size grows
            // with the distance so it looks the same from the spot.
            var eyeAt = plan.Feet + new Vector3(0, 0, plan.EyeHeight);
            var look = GrenadeSimLogic.Forward(plan.Pitch, plan.Yaw);
            float reach = 256f;
            if (FinderTrace(eyeAt, eyeAt + look * reach, out var aimHit, out _, out _, pawn))
                reach = Math.Max(48f, Vector3.Distance(eyeAt, aimHit) - 6f);
            var mark = eyeAt + look * reach;
            var side = GrenadeSimLogic.Heading(plan.Yaw - 90f);
            var upward = Vector3.Normalize(Vector3.Cross(side, look));
            // The aim mark (the author's picture, 2026-11-18): a thin ring with a short line down
            // from the top and up from the bottom, open in the middle so the crosshair shows in
            // it. Red; it turns green while the player stands on the spot with the crosshair in it
            // (TickFinderAimMark).
            float ring = Math.Max(3f, reach * 0.035f), thick = Math.Max(0.5f, reach * 0.005f);
            finderAimMarks.Remove(slot);
            if (aimMark)
            {
                var marks = new List<uint>();
                Vector3 At(float deg, float r) => mark + (side * MathF.Cos(deg * MathF.PI / 180f) + upward * MathF.Sin(deg * MathF.PI / 180f)) * r;
                void Line(Vector3 a, Vector3 b)
                {
                    uint? beam = FinderBeam(a, b, AimMarkOff, thick);
                    if (!beam.HasValue)
                        return;
                    ents.Add(beam.Value);
                    marks.Add(beam.Value);
                }
                const int parts = 16;
                for (int i = 0; i < parts; i++)
                    Line(At(i * 360f / parts, ring), At((i + 1) * 360f / parts, ring));
                Line(At(90f, ring), At(90f, ring * 0.3f));
                Line(At(270f, ring), At(270f, ring * 0.3f));
                finderAimMarks[slot] = new AimMark { Beams = marks };
            }

            // Flight path: the calculated one, thinned to a handful of beams, and where it ends.
            var path = new List<Vector3>();
            GrenadeSimLogic.TraceFn guideTrace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                FinderGrenadeTrace(a, b, out hit, out normal, out fraction, pawn != null && pawn.IsValid ? pawn : null);
            var flight = GrenadeSimLogic.Simulate(FinderLaunch(plan), guideTrace, 9f, 1, path, FinderFuseOf(plan.Type));
            if (path.Count > 1)
            {
                int step = Math.Max(1, (path.Count + GuidePathSegments - 1) / GuidePathSegments);
                var from = path[0];
                for (int i = step; ; i += step)
                {
                    var to = path[Math.Min(i, path.Count - 1)];
                    AddGuideBeam(ents, from, to, GuidePathColor, 0.6f);
                    from = to;
                    if (i >= path.Count - 1)
                        break;
                }
            }
            if (flight.Stopped)
                ents.AddRange(FloorHuggingBox(flight.Rest - new Vector3(0, 0, GrenadeSimLogic.HullHalf), 10f, GuidePathColor, pawn));

            // Running throws: a gate to run through (floor line, a post each side, a bar on top),
            // and the path from the start to it.
            if (FinderKindRuns(plan.Kind))
            {
                var at = plan.Feet + GrenadeSimLogic.Heading(plan.Yaw) * plan.RunUp + new Vector3(0, 0, 1f);
                var across = GrenadeSimLogic.Heading(plan.Yaw + 90f) * 40f;
                var top = new Vector3(0, 0, 72f);
                var start = plan.Feet + new Vector3(0, 0, 1f);
                foreach (var (a, b) in new[]
                {
                    (at - across, at + across), (at - across, at - across + top), (at + across, at + across + top),
                    (at - across + top, at + across + top), (start, at),
                })
                    AddGuideBeam(ents, a, b, Color.Yellow, 2f);
                // In the language of the player the guide is drawn for.
                string gateKey = plan.Kind == GrenadeSimLogic.ThrowKind.Run ? "matchzy.pp.track.gate.throw" : "matchzy.pp.track.gate.jumpthrow";
                var gatePlayer = Utilities.GetPlayerFromSlot(slot);
                string gateText = gatePlayer != null && gatePlayer.IsValid ? Localizer.ForPlayer(gatePlayer, gateKey) : Localizer[gateKey].Value;
                uint? gateLabel = FinderLabel(gateText, at + new Vector3(0, 0, 84f), Color.Yellow);
                if (gateLabel.HasValue)
                    ents.Add(gateLabel.Value);
            }
        }

        private sealed class AimMark
        {
            public List<uint> Beams = new();
            public bool On;
        }

        private readonly Dictionary<int, AimMark> finderAimMarks = new();
        // Pure red and pure green, and lines twice as thick as at first: the pale thin ring was
        // hard to make out against a wall (the author, 2026-11-18).
        private static readonly Color AimMarkOff = Color.FromArgb(255, 255, 0, 0);
        private static readonly Color AimMarkOn = Color.FromArgb(255, 0, 255, 0);
        // The crosshair counts as in the ring within this much of the lineup's aim.
        private const float AimMarkDegrees = 0.3f;

        // Red ring -> green while the player stands on the spot of their loaded lineup and aims
        // where it is thrown; red again the moment they do not.
        private void TickFinderAimMark()
        {
            if (finderAimMarks.Count == 0 || Server.TickCount % 4 != 0)
                return;
            foreach (var (slot, m) in finderAimMarks.ToList())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !IsPlayerValid(player) || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null
                    || !finderArmed.TryGetValue(slot, out var armed))
                {
                    finderAimMarks.Remove(slot);
                    continue;
                }
                var plan = armed.Plan;
                float dx = pawn.AbsOrigin.X - plan.Feet.X, dy = pawn.AbsOrigin.Y - plan.Feet.Y;
                float dPitch = pawn.EyeAngles.X - plan.Pitch, dYaw = pawn.EyeAngles.Y - plan.Yaw;
                while (dYaw > 180f) dYaw -= 360f;
                while (dYaw < -180f) dYaw += 360f;
                bool on = dx * dx + dy * dy <= 3f * 3f && dPitch * dPitch + dYaw * dYaw <= AimMarkDegrees * AimMarkDegrees;
                if (on == m.On)
                    continue;
                m.On = on;
                foreach (uint raw in m.Beams)
                {
                    var beam = new CHandle<CBeam>(raw).Value;
                    if (beam == null || !beam.IsValid)
                        continue;
                    beam.Render = on ? AimMarkOn : AimMarkOff;
                    Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");
                }
            }
        }

        private static void AddGuideBeam(List<uint> ents, Vector3 a, Vector3 b, Color color, float width)
        {
            uint? beam = FinderBeam(a, b, color, width);
            if (beam.HasValue)
                ents.Add(beam.Value);
        }

        // A saved lineup was loaded: arm it like a finder result (guide, on-screen line, verdict).
        // Only smokes and decoys (they end where they come to rest) with a throw kind the model
        // knows; anything else keeps the plain library behaviour.
        private void ArmLibraryLineup(CCSPlayerController player, string name, string nadeType, string throwType, Vector origin, QAngle angle, string nadePos = "", string nadeVel = "")
        {
            int slot = player.Slot;
            // A lineup of the finder's own that is being loaded through the library is still a
            // library lineup from here on.
            finderArmed.Remove(slot);
            if (finderGoEntities.Remove(slot, out var old))
                KillFinderEntities(old);
            if (!HasCssTraceApi)
                return;
            // Every grenade type (the path ends where an HE or flash goes off, a molotov ignites).
            string? mapped = (nadeType ?? "").Trim().ToLowerInvariant() switch
            {
                "smoke" => "smoke", "decoy" => "decoy", "he" => "he", "flash" => "flash",
                "molly" or "molotov" or "incendiary" => "molotov",
                _ => null,
            };
            if (mapped == null)
                return;
            string type = mapped;
            // The lineup's own release, when it was saved with one: any throw (walk, crouch, right
            // click) can then be shown and judged, not only the kinds the model knows.
            GrenadeSimLogic.Launch? own = null;
            if (LineupFormat.TryParse(nadePos, out float px, out float py, out float pz) && LineupFormat.TryParse(nadeVel, out float vx, out float vy, out float vz)
                && vx * vx + vy * vy + vz * vz > 1f)
                own = new GrenadeSimLogic.Launch(new Vector3(px, py, pz), new Vector3(vx, vy, vz));
            var known = FinderKindFromThrow(throwType);
            if (known == null && own == null)
                return;
            var kind = known ?? GrenadeSimLogic.ThrowKind.Stand;
            var pawn = player.PlayerPawn?.Value;
            var plan = new FinderResult
            {
                Feet = new Vector3(origin.X, origin.Y, origin.Z), Pitch = angle.X, Yaw = angle.Y, Kind = kind,
                Library = true, Name = name, Launch = own, FreeKind = own != null && known == null, Type = type,
                What = own != null && known == null ? (string.IsNullOrWhiteSpace(throwType) ? Localizer.ForPlayer(player, "matchzy.pp.track.throw_as_saved") : throwType.Trim()) : "",
            };
            GrenadeSimLogic.TraceFn ownTrace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                FinderGrenadeTrace(a, b, out hit, out normal, out fraction, pawn != null && pawn.IsValid ? pawn : null);
            var flight = own is GrenadeSimLogic.Launch l0
                ? GrenadeSimLogic.Simulate(l0, ownTrace, 20f, 1, null, FinderFuseOf(type))
                : FinderFlight(plan.Feet, kind, plan.Pitch, plan.Yaw, pawn, null, FinderFuseOf(type));
            if (!flight.Stopped)
                return;
            plan.Rest = flight.Rest;
            plan.FlightTime = flight.Time;
            plan.HasSim = true;
            finderDecoy[slot] = type == "decoy";
            finderType[slot] = type;
            finderThrewSinceGo.Remove(slot);
            finderArmed[slot] = (plan, 0);
            // No aim mark of its own: the spot's rings (SpotRings.cs) are already on that line.
            DrawFinderGuide(slot, plan, pawn, aimMark: false);
        }

        // Accuracy of a throw against where the lineup lands: 100 % within 4 u, then 1.5 % less
        // per unit (0 % from 70 u), with the time in the air.
        private static int FinderScore(float off) => (int)MathF.Round(Math.Clamp(100f - Math.Max(0f, off - 4f) * 1.5f, 0f, 100f));

        private string FinderScoreText(CCSPlayerController player, float off, int ticks) =>
            Localizer.ForPlayer(player, "matchzy.pp.track.score_text", FinderScore(off), ticks / 64f);

        // The score as a text over the landing spot for a few seconds.
        private void ShowFinderScore(CCSPlayerController player, Vector3 at, float off, int ticks)
        {
            int score = FinderScore(off);
            var color = score >= 90 ? Color.Lime : score >= 50 ? Color.Yellow : Color.OrangeRed;
            uint? label = FinderLabel(Localizer.ForPlayer(player, "matchzy.pp.track.score_label", score, ticks / 64f),
                new Vector3(at.X, at.Y, at.Z + 40f), color);
            if (!label.HasValue)
                return;
            var ents = new List<uint> { label.Value };
            AddTimer(6.0f, () => KillFinderEntities(ents), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        // Verdict for a throw of a saved lineup: there is no drawn box, the lineup's own calculated
        // landing point is the target.
        private void ReportLibraryTrack(FinderTrack t, CCSPlayerController player)
        {
            float off = Vector3.Distance(t.Last, t.Plan.Rest);
            bool onTarget = off <= LibraryOnTarget;
            OnTrainingVerdict(player, t.Plan, off);
            FinderLog(string.Format(CultureInfo.InvariantCulture,
                "[Finder] lineup \"{0}\" throw rested ({1:0.0} {2:0.0} {3:0.0}): {4:0.0} u from the calculated landing ({5:0.0} {6:0.0} {7:0.0}), launch {8}",
                t.Plan.Name, t.Last.X, t.Last.Y, t.Last.Z, off, t.Plan.Rest.X, t.Plan.Rest.Y, t.Plan.Rest.Z, t.Matched ? "matched" : "differed: " + t.Cause));
            string where = Localizer.ForPlayer(player, "matchzy.pp.track.lib.where", off, FinderScoreText(player, off, t.Ticks));
            ShowFinderScore(player, t.Last, off, t.Ticks);
            if (onTarget)
                PrintToPlayerChat(player, t.Matched
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.lib.on_target_matched", t.Plan.Name, where)
                    : Localizer.ForPlayer(player, "matchzy.pp.track.lib.on_target_differed", t.Plan.Name, where, t.Cause));
            else
                PrintToPlayerChat(player, t.Matched
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.lib.off_matched", t.Plan.Name, where)
                    : Localizer.ForPlayer(player, "matchzy.pp.track.lib.missed", t.Plan.Name, where, t.Cause));
        }
    }
}
