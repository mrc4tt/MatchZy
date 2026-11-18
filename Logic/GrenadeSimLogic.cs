using System.Numerics;

namespace MatchZy
{
    /// <summary>
    /// Grenade throw model and flight simulation for the Grenade Finder (.draw / .find). Pure math,
    /// no CounterStrikeSharp: world collisions come in through <see cref="TraceFn"/>, so the plugin
    /// passes the engine trace and the tests pass a flat floor.
    ///
    /// Throw model (validated in practice-plus step 0 against the engine's launch values, 0.00 %
    /// velocity error for standing throws): the aim pitch is raised by (90 - |pitch|) * 10 / 90
    /// degrees, the grenade starts 16 u in front of the eye along that aim with
    /// 675 * (0.3 + 0.7 * strength) u/s plus 1.25 x the player's velocity.
    /// Flight: CS:GO grenade physics (gravity 800 * 0.4, bounce clip with overbounce 2 and
    /// elasticity 0.45, at rest on a floor below 30 u/s).
    /// </summary>
    public static class GrenadeSimLogic
    {
        public const float BaseSpeed = 675f;
        public const float ReleaseForward = 16f;
        public const float PlayerVelocityScale = 1.25f;
        public const float Gravity = 320f;
        public const float Elasticity = 0.45f;
        // Calibrated against recorded engine flights (finder.log paths, de_mirage, 15 flights and
        // 65 bounces): gravity is exactly 320 with no drag, floor and wall bounces keep 0.45 of
        // the mirrored velocity, the grenade stops dead where it lands once it is below 20 u/s,
        // and a fast, steep floor bounce is damped further by (1.5 - cos to the surface normal).
        public const float StopSpeed = 20f;
        public const float SteepBounceSpeedSqr = 96000f;
        public const float SteepBounceCos = 0.5f;
        // The projectile is a 4 u cube: traces are hull traces of this half size, and positions
        // are the cube's centre (a grenade at rest sits 2 u above the floor).
        public const float HullHalf = 2f;
        public const float FloorNormalZ = 0.7f;
        public const float TickInterval = 1f / 64f;
        public const float SubStep = 1f / 128f;
        public const float StandingEyeHeight = 64f;
        /// <summary>Eye height of the thrower the calculation is for. The game's standing view
        /// height is not the same everywhere (62.2 measured on a slope on inferno): a search from
        /// the player's own spot uses the player's own.</summary>
        public static float EyeHeight = StandingEyeHeight;
        public const float FallLimit = 1500f;

        // Jumpthrow (step 0 data, 8 throws, and 3 live finder throws that all read 218.87): the
        // grenade leaves 0.1039 s after the jump, the same every time. With the jump impulse 301.99 u/s and
        // gravity 800 the eye is then 27.0 u above the standing eye.
        // 25.9: the game's own stashed jumpthrow eye sits 89.9 above the feet (live, inferno), and
        // 81 of 113 recorded jumpthrows of a workshop lineup set give the same. In the air the view
        // height is the full 64 also where the standing one is lower (slopes).
        public const float JumpReleaseRise = 25.9f;
        public const float JumpReleaseVelocityZ = 218.87f;

        /// <summary>World trace of the grenade (a hull of <see cref="HullHalf"/>): true when the
        /// move from..to hits something; hit = where the grenade's centre is at the contact,
        /// normal = surface normal, fraction = 0..1 along the segment.</summary>
        public delegate bool TraceFn(Vector3 from, Vector3 to, out Vector3 hit, out Vector3 normal, out float fraction);

        // Running jumpthrow: run straight ahead (W only) from the spot and jumpthrow RunUp units
        // on; the player keeps their speed for the 0.1039 s between the jump and the release.
        // Not constants: the finder replaces them with the player's own measured run (how far
        // they run before they jump, and how fast they are by then), so lineups fit their habit.
        // Defaults from five measured running throws: the jump came after about 76 u, by then at
        // 238 u/s (full speed, 245, takes a longer run).
        public const float DefaultRunSpeed = 238f;
        public const float DefaultRunUp = 76f;
        public static float RunSpeed = DefaultRunSpeed;
        public static float RunUp = DefaultRunUp;
        public const float JumpReleaseTime = 0.1039f;

        public enum ThrowKind { Stand, Jump, Run, RunJump }

        /// <summary>Unit vector along a yaw, in the XY plane.</summary>
        public static Vector3 Heading(float yawDeg)
        {
            double y = yawDeg * Math.PI / 180.0;
            return new Vector3((float)Math.Cos(y), (float)Math.Sin(y), 0f);
        }

        /// <summary>Where the jump of a running jumpthrow from <paramref name="feet"/> happens.</summary>
        public static Vector3 RunJumpPoint(Vector3 feet, float yawDeg) => feet + Heading(yawDeg) * RunUp;

        public readonly record struct Launch(Vector3 Position, Vector3 Velocity);

        public readonly record struct Flight(Vector3 Rest, bool Stopped, float Time, int Bounces, Vector3 FirstImpact, bool HasImpact);

        /// <summary>Forward vector of view angles (Source convention: positive pitch looks down).</summary>
        public static Vector3 Forward(float pitchDeg, float yawDeg)
        {
            double p = pitchDeg * Math.PI / 180.0, y = yawDeg * Math.PI / 180.0;
            return new Vector3((float)(Math.Cos(p) * Math.Cos(y)), (float)(Math.Cos(p) * Math.Sin(y)), (float)-Math.Sin(p));
        }

        /// <summary>Launch position and velocity for a throw from <paramref name="eye"/> with the given view angles.</summary>
        public static Launch LaunchFrom(Vector3 eye, float pitchDeg, float yawDeg, float strength, Vector3 playerVelocity)
        {
            float pitch = pitchDeg;
            if (pitch > 90f) pitch -= 360f;
            else if (pitch < -90f) pitch += 360f;
            pitch -= (90f - Math.Abs(pitch)) * 10f / 90f;
            Vector3 fwd = Forward(pitch, yawDeg);
            float speed = BaseSpeed * (0.3f + 0.7f * Math.Clamp(strength, 0f, 1f));
            return new Launch(eye + fwd * ReleaseForward, fwd * speed + playerVelocity * PlayerVelocityScale);
        }

        /// <summary>Launch for a throw kind from a player standing with their feet at <paramref name="feet"/>.</summary>
        public static Launch LaunchFrom(Vector3 feet, ThrowKind kind, float pitchDeg, float yawDeg)
        {
            if (kind == ThrowKind.Run)
            {
                // Running throw, no jump: released on the line at full speed.
                Vector3 dir = Heading(yawDeg);
                return LaunchFrom(feet + dir * RunUp + new Vector3(0, 0, EyeHeight), pitchDeg, yawDeg, 1f, dir * RunSpeed);
            }
            if (kind == ThrowKind.RunJump)
            {
                Vector3 dir = Heading(yawDeg);
                Vector3 eye = feet + dir * (RunUp + RunSpeed * JumpReleaseTime) + new Vector3(0, 0, StandingEyeHeight + JumpReleaseRise);
                return LaunchFrom(eye, pitchDeg, yawDeg, 1f, dir * RunSpeed + new Vector3(0, 0, JumpReleaseVelocityZ));
            }
            return kind == ThrowKind.Jump
                ? LaunchFrom(feet + new Vector3(0, 0, StandingEyeHeight + JumpReleaseRise), pitchDeg, yawDeg, 1f, new Vector3(0, 0, JumpReleaseVelocityZ))
                : LaunchFrom(feet + new Vector3(0, 0, EyeHeight), pitchDeg, yawDeg, 1f, Vector3.Zero);
        }

        /// <summary>When a grenade goes off before it has come to rest: After seconds of flight (HE,
        /// flashbang; 0 = never), and on the first surface at least this flat (molotov: normal z;
        /// 0 = never). A smoke or decoy has neither.</summary>
        public readonly record struct Fuse(float After, float IgniteNormalZ);

        /// <summary>Flies a grenade until it rests on a floor (a smoke detonates there), its fuse goes off,
        /// or maxTime runs out. path, when given, receives the points it passes through (launch, each
        /// step, each bounce).</summary>
        public static Flight Simulate(Launch launch, TraceFn trace, float maxTime = 8f, int ticksPerStep = 1, List<Vector3>? path = null, Fuse fuse = default)
        {
            path?.Add(launch.Position);
            Vector3 pos = launch.Position, vel = launch.Velocity;
            // The engine moves a grenade in steps of 1/128 s (measured on 65 recorded bounces: the
            // bounce depends on where in such a step the contact falls). Free flight is an exact
            // parabola, so it is taken in chords of several steps (the sag of a chord is well
            // under a unit); a chord that hits something is redone step by step.
            const float dt = SubStep;
            int chord = 4 * Math.Max(1, ticksPerStep);
            float chordTime = dt * chord;
            float t = 0f;
            int bounces = 0;
            Vector3 firstImpact = default;
            bool hasImpact = false;
            // A grenade that fell this far below its launch is out of the map (no floor caught it):
            // stop, the traces out there find nothing and only cost time.
            float lowest = launch.Position.Z - FallLimit;
            while (t < maxTime)
            {
                if (pos.Z < lowest)
                    return new Flight(pos, false, t, bounces, firstImpact, hasImpact);
                if (fuse.After > 0f && t >= fuse.After)
                {
                    path?.Add(pos);
                    return new Flight(pos, true, t, bounces, firstImpact, hasImpact);
                }
                // The last stretch before the fuse is taken step by step, to stop on the step.
                bool fuseNear = fuse.After > 0f && t + chordTime > fuse.After;
                Vector3 far = pos + vel * chordTime + new Vector3(0, 0, -0.5f * Gravity * chordTime * chordTime);
                if (!fuseNear && !trace(pos, far, out _, out _, out _))
                {
                    path?.Add(far);
                    pos = far;
                    vel.Z -= Gravity * chordTime;
                    t += chordTime;
                    continue;
                }

                for (int i = 0; i < chord; i++)
                {
                    if (fuse.After > 0f && t >= fuse.After)
                        break; // the check at the top of the loop ends the flight
                    Vector3 next = pos + vel * dt + new Vector3(0, 0, -0.5f * Gravity * dt * dt);
                    if (!trace(pos, next, out Vector3 hit, out Vector3 normal, out float fraction))
                    {
                        pos = next;
                        vel.Z -= Gravity * dt;
                        t += dt;
                        continue;
                    }

                    // The whole step's gravity goes onto the velocity before it is mirrored on the
                    // surface (overbounce 2) and damped; what is left of the step is then moved
                    // with the new velocity, without gravity.
                    if (fuse.IgniteNormalZ > 0f && normal.Z >= fuse.IgniteNormalZ)
                    {
                        path?.Add(hit);
                        return new Flight(hit, true, t + dt * fraction, bounces, hasImpact ? firstImpact : hit, true);
                    }
                    Vector3 impactVel = vel + new Vector3(0, 0, -Gravity * dt);
                    Vector3 bounced = (impactVel - normal * (Vector3.Dot(impactVel, normal) * 2f)) * Elasticity;
                    if (normal.Z > FloorNormalZ && bounced.LengthSquared() > SteepBounceSpeedSqr)
                    {
                        float steep = Vector3.Dot(Vector3.Normalize(bounced), normal);
                        if (steep > SteepBounceCos)
                            bounced *= 1f - steep + 0.5f;
                    }
                    t += dt;
                    bounces++;
                    if (!hasImpact)
                    {
                        firstImpact = hit;
                        hasImpact = true;
                    }
                    if (normal.Z > FloorNormalZ && bounced.LengthSquared() < StopSpeed * StopSpeed)
                    {
                        path?.Add(hit);
                        return new Flight(hit, true, t, bounces, firstImpact, true);
                    }
                    // Just off the surface so the next trace does not start in it.
                    pos = hit + normal * 0.03f;
                    Vector3 rest = pos + bounced * (dt * (1f - fraction));
                    pos = trace(pos, rest, out Vector3 again, out Vector3 againNormal, out _)
                        ? again + againNormal * 0.03f
                        : rest;
                    vel = bounced;
                    path?.Add(pos);
                    if (bounces > 60)
                        return new Flight(pos, false, t, bounces, firstImpact, hasImpact);
                    break; // back to chords from here
                }
            }
            return new Flight(pos, false, t, bounces, firstImpact, hasImpact);
        }

        /// <summary>
        /// Pitches (view pitch, degrees) whose collision-free arc reaches the horizontal distance to
        /// <paramref name="target"/> while coming down through the target's height. Usually two: a
        /// flat and a lobbed arc. Scans pitch -89..60 and refines each crossing by bisection.
        /// </summary>
        public static List<float> BallisticPitches(Vector3 feet, ThrowKind kind, Vector3 target)
        {
            Vector2 flat = new(target.X - feet.X, target.Y - feet.Y);
            float yaw = (float)(Math.Atan2(flat.Y, flat.X) * 180.0 / Math.PI);
            float want = flat.Length();
            var result = new List<float>();
            float prevPitch = -89f;
            float prev = ReachAtHeight(feet, kind, prevPitch, yaw, target.Z) - want;
            for (float p = -88.5f; p <= 60f; p += 0.5f)
            {
                float cur = ReachAtHeight(feet, kind, p, yaw, target.Z) - want;
                if (!float.IsNaN(prev) && !float.IsNaN(cur) && Math.Sign(prev) != Math.Sign(cur))
                {
                    float lo = prevPitch, hi = p, flo = prev;
                    for (int i = 0; i < 30; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        float fm = ReachAtHeight(feet, kind, mid, yaw, target.Z) - want;
                        if (float.IsNaN(fm)) break;
                        if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; }
                        else hi = mid;
                    }
                    result.Add(0.5f * (lo + hi));
                }
                prev = cur;
                prevPitch = p;
            }
            return result;
        }

        /// <summary>Horizontal distance at which the collision-free arc comes down through height
        /// z; NaN when it never gets that high.</summary>
        public static float ReachAtHeight(Vector3 feet, ThrowKind kind, float pitchDeg, float yawDeg, float z)
        {
            var l = LaunchFrom(feet, kind, pitchDeg, yawDeg);
            // z(t) = z0 + vz t - g t^2 / 2 = z, later root (descending).
            float a = -0.5f * Gravity, b = l.Velocity.Z, c = l.Position.Z - z;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return float.NaN;
            float t = (-b - MathF.Sqrt(disc)) / (2f * a);
            if (t <= 0f) return float.NaN;
            Vector2 start = new(l.Position.X - feet.X, l.Position.Y - feet.Y);
            Vector2 v = new(l.Velocity.X, l.Velocity.Y);
            return (start + v * t).Length();
        }

        /// <summary>Yaw (degrees) from feet towards a point.</summary>
        public static float YawTo(Vector3 feet, Vector3 target) =>
            (float)(Math.Atan2(target.Y - feet.Y, target.X - feet.X) * 180.0 / Math.PI);

        public static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

        /// <summary>Even-odd point-in-polygon test in the XY plane (the polygon closes itself).</summary>
        public static bool InsidePolygon(Vector3 point, IReadOnlyList<Vector3> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.Y > point.Y) != (b.Y > point.Y)
                    && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Area centroid of a polygon in XY (vertex average for degenerate shapes); z = mean z.</summary>
        public static Vector3 Centroid(IReadOnlyList<Vector3> polygon)
        {
            double area = 0, cx = 0, cy = 0, z = 0;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                double cross = (double)polygon[j].X * polygon[i].Y - (double)polygon[i].X * polygon[j].Y;
                area += cross;
                cx += (polygon[j].X + polygon[i].X) * cross;
                cy += (polygon[j].Y + polygon[i].Y) * cross;
                z += polygon[i].Z;
            }
            z /= Math.Max(1, polygon.Count);
            if (Math.Abs(area) < 1e-3)
            {
                var avg = Vector3.Zero;
                foreach (var p in polygon) avg += p;
                return avg / Math.Max(1, polygon.Count);
            }
            area *= 0.5;
            return new Vector3((float)(cx / (6 * area)), (float)(cy / (6 * area)), (float)z);
        }
    }
}
