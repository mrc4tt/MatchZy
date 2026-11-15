using System;
using System.Collections.Generic;
using System.Numerics;

namespace MatchZy
{
    // Which .showspawns marker a +use press picks, kept free of CounterStrikeSharp so it can be
    // unit tested (tests/MatchZy.Tests). The plugin feeds it the pawn origin, eye position and
    // view angles plus the drawn markers (spawn position and the floor Z the box was drawn on).
    public static class SpawnPickLogic
    {
        // Floor box half size: 32u box = player hull footprint.
        public const float HalfSize = 16.0f;
        // Box lifted off the floor (issue MatchZy-Enhanced#11: at +0 it sinks under shallow water).
        public const float FloorLift = 8.0f;
        // Height of the spawn number above the floor; the aim target runs from the box up to it.
        public const float LabelHeight = 40.0f;
        // Aim tolerance in world units around a marker's vertical line, so far markers are not
        // easier to hit than near ones (an angular cone grew to ~350u at 2000u).
        public const float AimRadius = 24.0f;
        public const float AimMaxDistance = 4096.0f;
        // Two markers whose miss distances differ by less than this count as equally aimed at; the
        // nearer one wins then (it is in front of the other).
        public const float AimTieDistance = 4.0f;
        // The number: 80 px font at 0.25 u/px = 20 u line, glyphs ~14 u tall and ~11 u wide per
        // digit, centred on LabelHeight; a billboard, so a short vertical cylinder matches it from
        // every side. Kept tight so the crosshair only counts as on the number when it is.
        public const float LabelHalfHeight = 7.0f;
        public const float LabelRadius = 6.0f;
        // issue #11: only the spawn you're exactly on is excluded from aiming.
        public const float StandRadiusSq = 8.0f * 8.0f;
        public const float StandHeight = 90.0f;
        // Standing inside a box (this far up/down from its floor) + USE snaps onto that spawn.
        public const float StandInHeight = 36.0f;

        // Forward unit vector for Source view angles in degrees (positive pitch looks down).
        public static Vector3 Forward(float pitchDegrees, float yawDegrees)
        {
            double pitch = pitchDegrees * Math.PI / 180.0;
            double yaw = yawDegrees * Math.PI / 180.0;
            return new Vector3(
                (float)(Math.Cos(pitch) * Math.Cos(yaw)),
                (float)(Math.Cos(pitch) * Math.Sin(yaw)),
                (float)(-Math.Sin(pitch)));
        }

        // The marker USE picks, or -1: the one the player aims at most precisely (nearest along the
        // view when several are about equally on the line), else the box they stand in (nearest box center).
        public static int PickMarker(Vector3 origin, Vector3 eye, Vector3 forward,
            IReadOnlyList<(Vector3 Spawn, float FloorZ)> markers)
        {
            // 1. Exact: the crosshair is on a drawn box or on its number. The front-most one under
            //    the crosshair wins, like clicking what you see.
            int exactIndex = -1;
            float exactAlong = float.MaxValue;
            for (int i = 0; i < markers.Count; i++)
            {
                var (sp, floorZ) = markers[i];
                if (IsStoodOn(origin, sp))
                    continue;
                float along = float.MaxValue;
                if (RayHitsFloorSquare(eye, forward, AimMaxDistance, sp.X, sp.Y, floorZ + FloorLift, HalfSize, out float tBox))
                    along = tBox;
                var (labelDist, labelAlong) = RayToVerticalSegment(eye, forward, AimMaxDistance,
                    sp.X, sp.Y, floorZ + LabelHeight - LabelHalfHeight, floorZ + LabelHeight + LabelHalfHeight);
                if (labelDist <= LabelRadius && labelAlong < along)
                    along = labelAlong;
                if (along < exactAlong)
                {
                    exactAlong = along;
                    exactIndex = i;
                }
            }
            if (exactIndex >= 0)
                return exactIndex;

            // 2. Near miss: within the aim tolerance of a marker's line (keeps far markers easy).
            int bestIndex = -1;
            float bestAlong = float.MaxValue;
            float bestDist = float.MaxValue;
            for (int i = 0; i < markers.Count; i++)
            {
                var (sp, floorZ) = markers[i];
                // Exclude the spawn you're standing exactly on so you can still re-center onto a
                // neighbouring marker you're merely near.
                if (IsStoodOn(origin, sp))
                    continue;
                var (dist, along) = RayToVerticalSegment(eye, forward, AimMaxDistance,
                    sp.X, sp.Y, floorZ + FloorLift, floorZ + LabelHeight);
                if (dist > AimRadius)
                    continue;
                // The marker aimed at most precisely wins: with spawns close together a nearer
                // neighbour inside the tolerance used to steal the press from the one under the
                // crosshair. Only when two are about equally on the line (one behind the other)
                // does the nearer one win.
                bool better = bestIndex < 0
                    || dist < bestDist - AimTieDistance
                    || (dist <= bestDist + AimTieDistance && along < bestAlong);
                if (better)
                {
                    bestAlong = along;
                    bestDist = dist;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
                return bestIndex;

            float bestDistSq = float.MaxValue;
            for (int i = 0; i < markers.Count; i++)
            {
                var (sp, floorZ) = markers[i];
                float dx = origin.X - sp.X, dy = origin.Y - sp.Y, dz = origin.Z - floorZ;
                if (Math.Abs(dx) > HalfSize || Math.Abs(dy) > HalfSize
                    || dz < -StandInHeight || dz > StandInHeight)
                    continue;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static bool IsStoodOn(Vector3 origin, Vector3 spawn)
        {
            float hdx = spawn.X - origin.X, hdy = spawn.Y - origin.Y, vdz = spawn.Z - origin.Z;
            return hdx * hdx + hdy * hdy <= StandRadiusSq && Math.Abs(vdz) <= StandHeight;
        }

        // Whether the view ray hits the drawn square (half size h around x, y at height z) from
        // above, and at which ray distance.
        public static bool RayHitsFloorSquare(Vector3 eye, Vector3 forward, float maxDistance,
            float x, float y, float z, float h, out float along)
        {
            along = 0f;
            if (forward.Z >= -1e-6f || eye.Z <= z)
                return false;
            float t = (z - eye.Z) / forward.Z;
            if (t < 0f || t > maxDistance)
                return false;
            float px = eye.X + forward.X * t, py = eye.Y + forward.Y * t;
            if (Math.Abs(px - x) > h || Math.Abs(py - y) > h)
                return false;
            along = t;
            return true;
        }

        // Closest distance between the view ray (eye + forward * t, 0 <= t <= maxDistance, forward a
        // unit vector) and the vertical segment (x, y, zBottom..zTop), and the ray distance t at
        // that point.
        public static (float Distance, float Along) RayToVerticalSegment(Vector3 eye,
            Vector3 forward, float maxDistance, float x, float y, float zBottom, float zTop)
        {
            var a = new Vector3(x, y, zBottom);
            float height = zTop - zBottom;
            Vector3 w = eye - a;
            float fz = forward.Z;               // forward . (0, 0, 1)
            float fw = Vector3.Dot(forward, w);
            float denom = 1.0f - fz * fz;       // 0 when looking straight up/down along the segment
            float t = denom < 1e-6f ? 0.0f : Math.Clamp((fz * w.Z - fw) / denom, 0.0f, maxDistance);
            // Clamp onto the segment, then re-project the ray onto that point.
            float s = Math.Clamp(w.Z + fz * t, 0.0f, height);
            var onSegment = new Vector3(x, y, zBottom + s);
            t = Math.Clamp(Vector3.Dot(onSegment - eye, forward), 0.0f, maxDistance);
            s = Math.Clamp(w.Z + fz * t, 0.0f, height);
            onSegment = new Vector3(x, y, zBottom + s);
            return (Vector3.Distance(eye + forward * t, onSegment), t);
        }
    }
}
