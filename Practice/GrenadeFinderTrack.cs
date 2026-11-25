using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using Vector3 = System.Numerics.Vector3;

namespace MatchZy
{
    // Grenade Finder, stage 3: what really happened. After the player is put on a found spot, the
    // server's demo throw and the player's own throw are both followed until they rest, and the
    // player is told whether each landed in the box. For their own throw the launch is compared
    // with the planned one (release point and velocity), so a miss names its cause: aim moved,
    // moving, eye not settled, jump and throw not together.
    public partial class MatchZy
    {
        private sealed class FinderTrack
        {
            public int Slot;
            public int Number;
            public bool Demo;
            public uint Handle;
            public FinderResult Plan = null!;
            public Vector3 Last;
            public int Still;
            public int Ticks;
            public string Cause = "";
            public bool Matched;
        }

        private readonly Dictionary<int, (FinderResult Plan, int Number)> finderArmed = new();
        private readonly List<FinderTrack> finderTracks = new();
        private const float FinderArmedRadius = 24f;
        private const float FinderLaunchPosTolerance = 2.0f;
        private const float FinderLaunchVelTolerance = 2.0f;
        private const float FinderRunPosTolerance = 16f;
        private const float FinderRunVelTolerance = 6f;

        private void TrackFinderDemo(int slot, int number, FinderResult plan, uint handle)
        {
            var l = FinderLaunch(plan);
            finderTracks.Add(new FinderTrack { Slot = slot, Number = number, Demo = true, Handle = handle, Plan = plan, Last = l.Position, Matched = true });
        }

        // From OnEntitySpawnedHandler: a real grenade of this player. Counts when they stand on the
        // spot they were put on and it is the planned grenade type.
        private void OnFinderPlayerThrow(CCSPlayerController player, CBaseCSGrenadeProjectile projectile, string nadeType)
        {
            if (!finderArmed.TryGetValue(player.Slot, out var armed) || !FinderTypeIs(armed.Plan.Type, nadeType))
                return;
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                return;
            var plan = armed.Plan;
            bool run0 = FinderKindRuns(plan.Kind);
            var launch = FinderLaunch(plan);
            var pos = new Vector3(projectile.InitialPosition.X, projectile.InitialPosition.Y, projectile.InitialPosition.Z);
            var vel = new Vector3(projectile.InitialVelocity.X, projectile.InitialVelocity.Y, projectile.InitialVelocity.Z);
            if (GrenadeSimLogic.HorizontalDistance(pos, launch.Position) > FinderArmedRadius + GrenadeSimLogic.ReleaseForward + (run0 ? 64f : 0f))
                return; // thrown from somewhere else: not this lineup

            finderThrewSinceGo.Add(player.Slot);
            float posErr = Vector3.Distance(pos, launch.Position);
            float velErr = Vector3.Distance(vel, launch.Velocity);
            // A lineup with its own stored release: that point may have been noted a tick or two
            // after the grenade left the hand (recorded lineups are). Compare along the flight.
            if (plan.Launch != null)
                for (int k = 1; k <= 3; k++)
                {
                    float dt = k / 64f;
                    var p2 = pos + vel * dt + new Vector3(0, 0, -0.5f * GrenadeSimLogic.Gravity * dt * dt);
                    var v2 = vel + new Vector3(0, 0, -GrenadeSimLogic.Gravity * dt);
                    float pe = Vector3.Distance(p2, launch.Position), ve = Vector3.Distance(v2, launch.Velocity);
                    if (pe + ve < posErr + velErr)
                    {
                        posErr = pe; velErr = ve;
                    }
                }
            bool run = FinderKindRuns(plan.Kind);
            // A running jumpthrow is released wherever the player jumped: a few units early or
            // late along the run still counts (the landing shifts by the same few units).
            float along = Vector3.Dot(pos - launch.Position, GrenadeSimLogic.Heading(plan.Yaw));
            bool matched = run
                ? posErr <= FinderRunPosTolerance && velErr <= FinderRunVelTolerance
                : posErr <= FinderLaunchPosTolerance && velErr <= FinderLaunchVelTolerance;
            string cause = "";
            if (!matched)
            {
                // The player's own motion at release, from the launch velocity: the throw adds
                // 1.25 x the player velocity to the aim direction times the throw speed.
                float pitch = pawn.EyeAngles.X, yaw = pawn.EyeAngles.Y;
                var aimOnly = GrenadeSimLogic.LaunchFrom(Vector3.Zero, pitch, yaw, 1f, Vector3.Zero);
                var playerVel = (vel - aimOnly.Velocity) / GrenadeSimLogic.PlayerVelocityScale;
                float dPitch = pitch - plan.Pitch;
                float dYaw = yaw - plan.Yaw;
                while (dYaw > 180f) dYaw -= 360f;
                while (dYaw < -180f) dYaw += 360f;
                float side = MathF.Sqrt(playerVel.X * playerVel.X + playerVel.Y * playerVel.Y);
                var causes = new List<string>();
                if (Math.Abs(dPitch) > 0.05f || Math.Abs(dYaw) > 0.05f)
                    causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.aim_moved", -dPitch, dYaw));
                if (!plan.FreeKind)
                {
                if (run)
                {
                    if (Math.Abs(side - GrenadeSimLogic.RunSpeed) > 4f)
                        causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.not_full_speed", side, GrenadeSimLogic.RunSpeed));
                    if (Math.Abs(along) > FinderRunPosTolerance * 0.75f)
                        causes.Add(along > 0
                            ? Localizer.ForPlayer(player, "matchzy.pp.track.cause.jumped_late", Math.Abs(along))
                            : Localizer.ForPlayer(player, "matchzy.pp.track.cause.jumped_early", Math.Abs(along)));
                }
                else if (pawn.AbsOrigin != null && GrenadeSimLogic.HorizontalDistance(new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z), plan.Feet) > 4f)
                    causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.not_on_spot",
                        GrenadeSimLogic.HorizontalDistance(new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z), plan.Feet)));
                if (!run && side > 2f)
                    causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.moving", side));
                bool jumps = plan.Kind is GrenadeSimLogic.ThrowKind.Jump or GrenadeSimLogic.ThrowKind.RunJump;
                if (!jumps && Math.Abs(playerVel.Z) > 5f)
                    causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.in_air"));
                else if (jumps && Math.Abs(playerVel.Z - GrenadeSimLogic.JumpReleaseVelocityZ) > 3f)
                    causes.Add(Math.Abs(playerVel.Z) < 5f
                        ? Localizer.ForPlayer(player, "matchzy.pp.track.cause.no_jump")
                        : Localizer.ForPlayer(player, "matchzy.pp.track.cause.jump_timing",
                            (GrenadeSimLogic.JumpReleaseVelocityZ - playerVel.Z) / (800f * GrenadeSimLogic.TickInterval)));
                }
                if (causes.Count == 0 && posErr > 6f && pawn.AbsOrigin != null
                    && GrenadeSimLogic.HorizontalDistance(new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z), plan.Feet) <= 4f)
                    causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.blocked"));
                if (causes.Count == 0 && Math.Abs(pos.Z - launch.Position.Z) > 1.5f)
                    causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.eye_height", pos.Z - launch.Position.Z));
                if (causes.Count == 0)
                    causes.Add(Localizer.ForPlayer(player, "matchzy.pp.track.cause.release_differed", posErr, velErr));
                cause = string.Join(", ", causes);
            }
            if (run)
                LearnFinderRun(player, plan, pos, vel);
            FinderLog(string.Format(CultureInfo.InvariantCulture,
                "[Finder] player throw #{0}: launch ({1:0.00} {2:0.00} {3:0.00}) vel ({4:0.00} {5:0.00} {6:0.00}), planned ({7:0.00} {8:0.00} {9:0.00}) vel ({10:0.00} {11:0.00} {12:0.00}), off {13:0.00} u {14:0.00} u/s{15}",
                armed.Number, pos.X, pos.Y, pos.Z, vel.X, vel.Y, vel.Z, launch.Position.X, launch.Position.Y, launch.Position.Z,
                launch.Velocity.X, launch.Velocity.Y, launch.Velocity.Z, posErr, velErr, matched ? "" : " (" + cause + ")")
                + string.Format(CultureInfo.InvariantCulture, " | player feet ({0:0.0} {1:0.0} {2:0.0}) plan feet ({3:0.0} {4:0.0} {5:0.0}) view height {6:0.0} ground {7}",
                    pawn.AbsOrigin?.X ?? 0, pawn.AbsOrigin?.Y ?? 0, pawn.AbsOrigin?.Z ?? 0, plan.Feet.X, plan.Feet.Y, plan.Feet.Z,
                    pawn.ViewOffset.Z, (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0));
            finderTracks.Add(new FinderTrack
            {
                Slot = player.Slot, Number = armed.Number, Handle = projectile.EntityHandle.Raw, Plan = plan, Last = pos, Matched = matched, Cause = cause,
            });
        }

#if HAS_CSS_TRACE
        // A small box (4 u) against what stops a player: for probing a player's space from its middle.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float FinderPlayerProbeCss(Vector3 from, Vector3 to, CBaseEntity? ignore) =>
            FinderPlayerTraceCss(from, to, 2f, ignore);

        // The collision layers of a real player, read off a live pawn (CapturePlayerLayers). A
        // trace that only names what it collides WITH passes through player clips: those stop
        // what moves AS a player. The fit checks ran without that until 2026-11-18 and called
        // spots free that a player stuck fast on (find6 / find7 on banana, against a kerb).
        private static ulong playerLayersWith, playerLayersAs, playerLayersExclude;
        private static bool playerLayersKnown;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static TraceOptions PlayerTraceOptionsCss() => playerLayersKnown
            ? new TraceOptions { InteractsWith = (Contents)playerLayersWith, InteractsAs = (Contents)playerLayersAs, InteractsExclude = (Contents)playerLayersExclude }
            : new TraceOptions { InteractsWith = Masks.PlayerSolid };

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float FinderPlayerTraceCss(Vector3 from, Vector3 to, float half, CBaseEntity? ignore)
        {
            var tr = Trace.TraceHullShape(new Vector(from.X, from.Y, from.Z), new Vector(to.X, to.Y, to.Z),
                new Vector(-half, -half, -half), new Vector(half, half, half), ignore, PlayerTraceOptionsCss());
            if (tr.IsAllSolid)
                return 0f;
            return tr.DidHit() ? tr.Fraction : 1f;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float FinderPlayerSweepCss(Vector3 from, Vector3 to, CBaseEntity? ignore) =>
            FinderPlayerTraceCss(from, to, 16f, ignore);
#endif

        // Debug log of the lineup tools, off unless matchzy_practice_debug_log is on: every line
        // then goes to <plugin>/calib/finder.log, so a test session can be read back in one piece.
        private readonly List<string> finderLogLines = new();
        private string? finderLogPath;
        private int finderLogFlushTick;

        private void FinderLog(string message, bool fileOnly = false)
        {
            if (!practiceDebugLog.Value)
                return;
            if (!fileOnly)
                Log(message);
            finderLogLines.Add(string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} t{1} {2} {3}", DateTime.UtcNow, Server.TickCount, Server.MapName, message));
        }

        private void FlushFinderLog()
        {
            int tick = Server.TickCount;
            if (finderLogLines.Count == 0 || tick - finderLogFlushTick < 64 && tick >= finderLogFlushTick)
                return;
            finderLogFlushTick = tick;
            string path = finderLogPath ??= Path.Join(ModuleDirectory, "calib", "finder.log");
            string text = string.Join("\n", finderLogLines) + "\n";
            finderLogLines.Clear();
            _ = Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    lock (finderLogLock)
                    {
                        // The panel's file API refuses files over 4 MB: start a new one well before.
                        var info = new FileInfo(path);
                        if (info.Exists && info.Length > 1_000_000)
                            File.Move(path, path + ".old", true);
                        File.AppendAllText(path, text);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[MatchZy finder] log write failed: {e.Message}");
                }
            });
        }

        private static readonly object finderLogLock = new();

        // A running throw depends on the player's habit: how far they run before they jump and how
        // fast they are by then. Instead of asking them to hit a mark, the next search uses what
        // they really did (measured from the launch of this throw).
        private void LearnFinderRun(CCSPlayerController player, FinderResult plan, Vector3 pos, Vector3 vel)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid)
                return;
            var dir = GrenadeSimLogic.Heading(plan.Yaw);
            var aimOnly = GrenadeSimLogic.LaunchFrom(Vector3.Zero, pawn.EyeAngles.X, pawn.EyeAngles.Y, 1f, Vector3.Zero);
            var playerVel = (vel - aimOnly.Velocity) / GrenadeSimLogic.PlayerVelocityScale;
            float speed = MathF.Sqrt(playerVel.X * playerVel.X + playerVel.Y * playerVel.Y);
            bool jumps = plan.Kind == GrenadeSimLogic.ThrowKind.RunJump;
            // Only a clean throw of the right kind teaches anything.
            if (speed < 120f || speed > 260f || (jumps && Math.Abs(playerVel.Z - GrenadeSimLogic.JumpReleaseVelocityZ) > 4f) || (!jumps && Math.Abs(playerVel.Z) > 5f))
                return;
            // Release point back to the eye, then along the run from the start spot.
            var eye = pos - aimOnly.Position;
            float released = Vector3.Dot(eye - plan.Feet, dir);
            float runUp = released - (jumps ? speed * GrenadeSimLogic.JumpReleaseTime : 0f);
            if (runUp < 16f || runUp > 400f)
                return;
            if (Math.Abs(runUp - GrenadeSimLogic.RunUp) < 10f && Math.Abs(speed - GrenadeSimLogic.RunSpeed) < 3f)
                return;
            GrenadeSimLogic.RunUp = runUp;
            GrenadeSimLogic.RunSpeed = speed;
            // Results worked out for the old run stay as they were (each keeps its own run).
            FinderLog(string.Format(CultureInfo.InvariantCulture, "[Finder] learned run: {0:0.0} u run-up at {1:0.0} u/s", runUp, speed));
            PrintToPlayerChat(player, jumps
                ? Localizer.ForPlayer(player, "matchzy.pp.track.learned_run_jump", runUp, speed)
                : Localizer.ForPlayer(player, "matchzy.pp.track.learned_run_throw", runUp, speed));
        }

        private void TickFinderTracks()
        {
            for (int i = finderTracks.Count - 1; i >= 0; i--)
            {
                var t = finderTracks[i];
                var ent = new CHandle<CBaseCSGrenadeProjectile>(t.Handle).Value;
                if (ent == null || !ent.IsValid || ent.AbsOrigin == null)
                {
                    finderTracks.RemoveAt(i);
                    // An HE, flash or molotov is gone when it has gone off: that point is its result.
                    if (FinderDetonates(t.Plan.Type) && t.Ticks > 2)
                        ReportFinderTrack(t);
                    continue;
                }
                var pos = new Vector3(ent.AbsOrigin.X, ent.AbsOrigin.Y, ent.AbsOrigin.Z);
                t.Still = Vector3.Distance(pos, t.Last) < VerifyStillDistance ? t.Still + 1 : 0;
                t.Last = pos;
                t.Ticks++;
                if (t.Still < VerifyStillTicks && t.Ticks < VerifyMaxTicks)
                    continue;
                finderTracks.RemoveAt(i);
                ReportFinderTrack(t);
            }
        }

        // A lineup the player threw themselves and that landed is kept: it goes into their saved
        // lineups (savednades.json, same as .savenade), so .shownades shows it on the map and it
        // survives a map change. One entry per spot and aim.
        private void SaveFinderLineup(CCSPlayerController player, FinderResult plan, Vector3 landed)
        {
            try
            {
                string map = Server.MapName;
                string steam = isSaveNadesAsGlobalEnabled ? "default" : player.SteamID.ToString();
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                if (!File.Exists(path))
                    File.WriteAllText(path, "{}");
                var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path))
                    ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                if (!all.TryGetValue(steam, out var mine))
                {
                    mine = new Dictionary<string, Dictionary<string, string>>();
                    all[steam] = mine;
                }
                string position = LineupFormat.Format(plan.Feet.X, plan.Feet.Y, plan.Feet.Z);
                string angles = LineupFormat.Format(plan.Pitch, plan.Yaw, 0f);
                foreach (var entry in mine.Values)
                    if (entry.TryGetValue("Map", out var m) && m == map
                        && entry.TryGetValue("Position", out var p) && p == position
                        && entry.TryGetValue("Angles", out var a) && a == angles)
                        return; // already saved
                if (mine.Count >= maxSavedNades)
                    return;
                string name = LineupNameLogic.NextFree("find", LineupNamesInUse(all, mine, map));
                mine[name] = new Dictionary<string, string>
                {
                    { "Position", position },
                    { "Angles", angles },
                    { "Desc", string.Format(CultureInfo.InvariantCulture, "Grenade Finder: {0}, lands at {1:0} {2:0} {3:0}", FinderKindName(plan.Kind), landed.X, landed.Y, landed.Z) },
                    { "Map", map },
                    { "Type", GetNadeType(plan.Type switch
                        {
                            "he" => "weapon_hegrenade", "flash" => "weapon_flashbang", "molotov" => "weapon_molotov",
                            "decoy" => "weapon_decoy", _ => "weapon_smokegrenade",
                        }) },
                    { "Throw", plan.Kind switch
                        {
                            GrenadeSimLogic.ThrowKind.Stand => "Normal Throw",
                            GrenadeSimLogic.ThrowKind.Jump => "Jumpthrow",
                            GrenadeSimLogic.ThrowKind.Run => "Run Throw",
                            _ => "Run Jumpthrow",
                        } },
                    // For the landing-spot view of the library (not read by anything yet).
                    { "Landing", LineupFormat.Format(landed.X, landed.Y, landed.Z) },
                };
                // The release itself and the eye height it was worked out for (see AddLastThrowToLineup).
                var release = FinderLaunch(plan);
                mine[name]["NadePos"] = LineupFormat.Format(release.Position.X, release.Position.Y, release.Position.Z);
                mine[name]["NadeVel"] = LineupFormat.Format(release.Velocity.X, release.Velocity.Y, release.Velocity.Z);
                mine[name]["ViewHeight"] = plan.EyeHeight.ToString("0.###", CultureInfo.InvariantCulture);
                mine[name]["FlightTime"] = plan.FlightTime.ToString("0.##", CultureInfo.InvariantCulture);
                File.WriteAllText(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
                RefreshNadeMarkersIfActive(player);
                FinderLog($"[Finder] saved lineup {name} on {map}");
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.track.saved", name));
            }
            catch (Exception e)
            {
                FinderLog($"[Finder] saving the lineup failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private void ReportFinderTrack(FinderTrack t)
        {
            var player = Utilities.GetPlayerFromSlot(t.Slot);
            if (player == null || !IsPlayerValid(player))
                return;
            if (t.Plan.Library)
            {
                ReportLibraryTrack(t, player);
                return;
            }
            if (!finderTargets.TryGetValue(t.Slot, out var target))
                return;
            bool hit = FinderHits(target, t.Last);
            bool inside = FinderMiss(target, t.Last) <= 0.5f;
            float fromCentre = GrenadeSimLogic.HorizontalDistance(t.Last, target.Center);
            float fromTest = Vector3.Distance(t.Last, t.Plan.Rest);
            FinderLog(string.Format(CultureInfo.InvariantCulture,
                "[Finder] {0} #{1} rested ({2:0.0} {3:0.0} {4:0.0}): {5}, {6:0.0} u from the centre, {7:0.0} u from the test throw",
                t.Demo ? "demo" : "player throw", t.Number, t.Last.X, t.Last.Y, t.Last.Z, inside ? "IN the box" : hit ? "COVERS the box" : "outside the box", fromCentre, fromTest));
            // Outside: how far outside, and whether it ended on another level (a throw that drops
            // off a ledge is next to the centre on the map and a floor below it).
            float outside = FinderMiss(target, t.Last);
            float level = t.Last.Z - target.Min.Z;
            string where = hit && !inside
                ? Localizer.ForPlayer(player, "matchzy.pp.track.where.covers")
                : inside
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.where.from_centre", fromCentre)
                    : level < -24f
                        ? Localizer.ForPlayer(player, "matchzy.pp.track.where.below", -level)
                        : level > 72f
                            ? Localizer.ForPlayer(player, "matchzy.pp.track.where.above", level)
                            : Localizer.ForPlayer(player, "matchzy.pp.track.where.outside", outside);
            if (t.Demo)
            {
                PrintToPlayerChat(player, hit
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.demo.hit", t.Number, where)
                    : Localizer.ForPlayer(player, "matchzy.pp.track.demo.miss", t.Number, where));
                return;
            }
            // Kept only when the player threw the lineup as planned: a throw they corrected by hand
            // landed from another aim than the one that would be saved.
            if (hit && t.Matched && !t.Plan.NoHit)
                SaveFinderLineup(player, t.Plan, t.Last);
            // Score against the server's own throw of this lineup (that is the landing to copy).
            where += ", " + FinderScoreText(player, fromTest, t.Ticks);
            ShowFinderScore(player, t.Last, fromTest, t.Ticks);
            if (hit)
                PrintToPlayerChat(player, t.Matched
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.verdict.hit_matched", t.Number, where)
                    : Localizer.ForPlayer(player, "matchzy.pp.track.verdict.hit_differed", t.Number, where, t.Cause));
            else
                PrintToPlayerChat(player, t.Matched
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.verdict.miss_matched", t.Number, where, fromTest)
                    : Localizer.ForPlayer(player, "matchzy.pp.track.verdict.miss_differed", t.Number, where, t.Cause));
        }
    }
}
