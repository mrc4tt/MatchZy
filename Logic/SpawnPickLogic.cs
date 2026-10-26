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

        // The marker USE picks, or -1: the one the player aims at (nearest along the view when
        // several are in tolerance), else the box they stand in (nearest box center).
        public static int PickMarker(Vector3 origin, Vector3 eye, Vector3 forward,
            IReadOnlyList<(Vector3 Spawn, float FloorZ)> markers)
        {
            int bestIndex = -1;
            float bestAlong = float.MaxValue;
            for (int i = 0; i < markers.Count; i++)
            {
                var (sp, floorZ) = markers[i];
                // Exclude the spawn you're standing exactly on so you can still re-center onto a
                // neighbouring marker you're merely near.
                float hdx = sp.X - origin.X, hdy = sp.Y - origin.Y, vdz = sp.Z - origin.Z;
                if (hdx * hdx + hdy * hdy <= StandRadiusSq && Math.Abs(vdz) <= StandHeight)
                    continue;
                var (dist, along) = RayToVerticalSegment(eye, forward, AimMaxDistance,
                    sp.X, sp.Y, floorZ + FloorLift, floorZ + LabelHeight);
                if (dist <= AimRadius && along < bestAlong)
                {
                    bestAlong = along;
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
