using System;
using System.Collections.Generic;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Grenade predictor (Phase 1). Forward-simulates the held grenade from the current
        // aim using a ballistic integrator + world ray-traces for bounces, then draws the predicted
        // arc and landing marker. Physics constants (gravity/elasticity/friction/throwspeed) are
        // convars so the sim can be calibrated live against real throws. Experimental + gated by
        // matchzy_experimental_predictor; the accuracy is only as good as those constants.
        readonly List<CBeam> predictionBeams = new();
        // Last predicted landing per player, for the calibration readout (predicted vs real).
        readonly Dictionary<int, Vector> lastPredictedLanding = new();

        [ConsoleCommand("css_predict", "Experimental: predict where the held grenade would land")]
        public void OnPredictCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || !player.UserId.HasValue || player.PlayerPawn.Value == null)
                return;
            if (!experimentalPredictor.Value)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.predictdisabled"));
                return;
            }
            var pawn = player.PlayerPawn.Value;
            if (pawn.AbsOrigin == null)
                return;

            string? active = pawn.WeaponServices?.ActiveWeapon?.Value?.DesignerName;
            bool isNade = active is "weapon_smokegrenade" or "weapon_hegrenade" or "weapon_flashbang"
                or "weapon_molotov" or "weapon_incgrenade" or "weapon_decoy";
            if (!isNade)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.predictnotgrenade"));
                return;
            }

            // Eye position + forward from view angles (same basis as the other aim helpers).
            Vector origin = pawn.AbsOrigin;
            QAngle ang = pawn.EyeAngles;
            double pitch = ang.X * Math.PI / 180.0;
            double yaw = ang.Y * Math.PI / 180.0;
            float fx = (float)(Math.Cos(pitch) * Math.Cos(yaw));
            float fy = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            float fz = (float)(-Math.Sin(pitch));

            Vector eye = new Vector(origin.X, origin.Y, origin.Z + 64.0f);
            Vector start = new Vector(eye.X + fx * 16.0f, eye.Y + fy * 16.0f, eye.Z + fz * 16.0f);
            float speed = predictThrowSpeed.Value;
            Vector vel = new Vector(fx * speed, fy * speed, fz * speed);

            var (path, landing, bounces) = SimulateGrenade(start, vel);

            ClearPrediction();
            DrawPrediction(path, landing);
            lastPredictedLanding[player.UserId.Value] = landing;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.predictresult", $"{bounces}"));
        }

        // Ballistic forward-sim with world-trace bounces. Integrates a parabola (gravity), ray-traces
        // each step against solid geometry, and on a hit reflects the velocity across the surface
        // normal (restitution predictElasticity) and bleeds tangential speed (predictFriction) until
        // it comes to rest or the flight time cap. Returns the sampled path, the resting (landing)
        // point, and the bounce count. Constants are convars for live calibration.
        private (List<Vector> path, Vector landing, int bounces) SimulateGrenade(Vector start, Vector startVel)
        {
            const float dt = 1.0f / 64.0f;
            float g = predictGravity.Value;
            float e = predictElasticity.Value;
            float fr = predictFriction.Value;

            var path = new List<Vector> { new Vector(start.X, start.Y, start.Z) };
            Vector pos = new Vector(start.X, start.Y, start.Z);
            Vector v = new Vector(startVel.X, startVel.Y, startVel.Z);
            var opts = new TraceOptions { InteractsWith = Masks.Solid };
            int bounces = 0;

            for (int step = 0; step < 320; step++)   // up to ~5s of flight
            {
                v.Z -= g * dt;
                Vector next = new Vector(pos.X + v.X * dt, pos.Y + v.Y * dt, pos.Z + v.Z * dt);

                TraceResult tr;
                try
                {
                    // Null ignore-entity on purpose: passing an entity makes the trace native read
                    // CCollisionComponent.m_collisionAttribute, a schema class not present on this
                    // CS2 build (logs "InitSchemaFieldsForClass(): 'CCollisionComponent' was not
                    // found!" and yields a garbage filter). The sim starts in front of the player and
                    // travels away, so ignoring nothing is safe.
                    tr = Trace.TraceEndShape(pos, next, null, opts);
                }
                catch
                {
                    break;   // trace failed - stop gracefully
                }

                if (tr.DidHit())
                {
                    Vector n = tr.Normal;
                    Vector hp = tr.HitPoint;
                    // Reflect: v' = v - (1 + e)(v.n)n, then bleed tangential speed via friction.
                    float vn = v.X * n.X + v.Y * n.Y + v.Z * n.Z;
                    v = new Vector(v.X - (1 + e) * vn * n.X, v.Y - (1 + e) * vn * n.Y, v.Z - (1 + e) * vn * n.Z);
                    v = new Vector(v.X * (1 - fr), v.Y * (1 - fr), v.Z * (1 - fr));
                    // Nudge off the surface so the next segment doesn't start solid.
                    pos = new Vector(hp.X + n.X, hp.Y + n.Y, hp.Z + n.Z);
                    path.Add(new Vector(pos.X, pos.Y, pos.Z));
                    bounces++;
                    if (v.X * v.X + v.Y * v.Y + v.Z * v.Z < 900.0f)   // < 30 u/s -> resting
                        break;
                }
                else
                {
                    pos = next;
                    if ((step & 1) == 0)
                        path.Add(new Vector(pos.X, pos.Y, pos.Z));
                }
            }
            return (path, pos, bounces);
        }

        // Draw the predicted path (green poly-line) + a red landing marker. Auto-removed after a
        // while; a new .predict clears the previous one first.
        private void DrawPrediction(List<Vector> pts, Vector landing)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var b = Utilities.CreateEntityByName<CBeam>("beam");
                if (b == null)
                    continue;
                b.LifeState = 1;
                b.Width = 1.5f;
                b.Render = Color.Lime;
                b.EndPos.X = pts[i + 1].X;
                b.EndPos.Y = pts[i + 1].Y;
                b.EndPos.Z = pts[i + 1].Z;
                b.Teleport(pts[i], new QAngle(0, 0, 0), new Vector(0, 0, 0));
                b.DispatchSpawn();
                predictionBeams.Add(b);
            }

            var marker = Utilities.CreateEntityByName<CBeam>("beam");
            if (marker != null)
            {
                marker.LifeState = 1;
                marker.Width = 4.0f;
                marker.Render = Color.Red;
                marker.EndPos.X = landing.X;
                marker.EndPos.Y = landing.Y;
                marker.EndPos.Z = landing.Z + 80.0f;
                marker.Teleport(new Vector(landing.X, landing.Y, landing.Z), new QAngle(0, 0, 0), new Vector(0, 0, 0));
                marker.DispatchSpawn();
                predictionBeams.Add(marker);
            }

            AddTimer(15.0f, ClearPrediction);
        }

        private void ClearPrediction()
        {
            foreach (var b in predictionBeams)
                if (b != null && b.IsValid)
                    SafeRemoveEntity(b, "predict");
            predictionBeams.Clear();
        }

        // Calibration: when a real grenade detonates, compare its position to the last prediction
        // for that player and log the miss distance (matchzy_predict_debug). Tune the predict_*
        // convars until this shrinks.
        public void CalibratePrediction(int userId, float x, float y, float z)
        {
            if (!predictDebug.Value)
                return;
            if (!lastPredictedLanding.TryGetValue(userId, out var predicted))
                return;
            float dx = predicted.X - x, dy = predicted.Y - y, dz = predicted.Z - z;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            Log($"[Predict] predicted vs actual landing miss: {dist:0} units (pred {predicted.X:0}/{predicted.Y:0}/{predicted.Z:0} vs real {x:0}/{y:0}/{z:0})");
            lastPredictedLanding.Remove(userId);
        }
    }
}
