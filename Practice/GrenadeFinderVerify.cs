using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using Stopwatch = System.Diagnostics.Stopwatch;
using Vector3 = System.Numerics.Vector3;

namespace MatchZy
{
    // Grenade Finder, stage 2: the simulation in GrenadeFinder.cs only proposes throws; this throws
    // them for real. Each candidate is launched as a real projectile from its exact release point
    // and velocity (the step-0 throw model, which matches the engine to 0.00 % for standing throws),
    // its resting point is read when it stops, and it is removed before it does anything. Only
    // throws whose real projectile rests inside the .draw box are shown. When none does, the closest
    // ones are varied by up to 1 degree of pitch/yaw and thrown again, for a few rounds.
    //
    // Grenades collide with each other in CS2, so probes must not meet in the air: they are spawned
    // one at a time with a gap, never two from the same spot close together, and every throw that
    // lands in the box is thrown once more, spaced far apart, before it is shown. Only a throw
    // whose second, undisturbed flight also rests in the box counts.
    //
    // Probes are the grenade the player holds (a smoke unless it is a decoy), so the test uses that
    // projectile's own physics. A smoke probe is removed the moment it stops, before it pops.
    // The thrower is set like a normal rethrow (.rt).
    public partial class MatchZy
    {
        private sealed class VerifyProbe
        {
            public FinderResult Source = null!;
            public uint Handle;
            public Vector3 Last;
            public int Still;
            public int Ticks;
            public System.Text.StringBuilder? Path;
            public bool LogPath;
            public bool Detonated;
        }

        private sealed class VerifyJob
        {
            public int Slot;
            public FinderTarget Target = null!;
            public Vector3 PlayerFeet;
            public CBaseEntity? Ignore;
            public readonly List<FinderResult> Pending = new();
            public readonly Dictionary<(int, int, int), int> LastSpawnAt = new();
            public readonly List<FinderResult> Confirmed = new();
            public bool Confirming;
            public int NextSpawnTick;
            public readonly List<VerifyProbe> Live = new();
            public readonly List<FinderResult> Verified = new();
            public readonly List<FinderResult> Tested = new();
            public readonly List<FinderResult> AllTested = new();
            public int Round;
            public bool Decoy;
            public string Type = "smoke";
            public bool Here;
            public bool Quick;
            public bool AreaStage;
            public int Done, Total;
            public double NextHint;
            public readonly Stopwatch Clock = Stopwatch.StartNew();
        }

        private const int VerifyBatch = 64;
        private const int VerifyQuickBatch = 12;
        // Ticks between two probes, and between two probes from the same spot (they fly nearly the
        // same arc, so they need the distance). The confirming throws fly practically alone.
        private const int VerifySpawnGap = 2;
        private const int VerifySameSpotGap = 3;
        private const int VerifyConfirmGap = 24;
        private const int VerifyConfirmMax = 12;
        private const int VerifyRounds = 3;
        private const int VerifyWant = 3;
        private const float VerifyAgreeDistance = 3f;
        private const int VerifyStillTicks = 4;
        private const float VerifyStillDistance = 0.05f;
        private const int VerifyMaxTicks = 64 * 21;
        private const ushort DecoyItemIndex = 47;
        private const ushort SmokeItemIndex = 45;
        private VerifyJob? verifyJob;

        private void StartFinderVerify(CCSPlayerController player, FinderJob job, FinderTarget target, List<FinderResult> candidates)
        {
            CancelFinderVerify();
            // The grenade chosen at .draw, unless the player changed to the other one since.
            // The grenade of this search (set when .find was typed).
            string type = FinderTypeFor(job.Slot);
            bool decoy = type == "decoy";
            finderDecoy[job.Slot] = decoy;
            var v = new VerifyJob { Slot = job.Slot, Target = target, PlayerFeet = job.PlayerFeet, Ignore = job.Ignore, Total = candidates.Count, Decoy = decoy, Type = type, Here = job.Here, Quick = job.Quick, AreaStage = job.AreaStage };
            v.Pending.AddRange(candidates);
            verifyJob = v;
            if (finderProbeTransmit == null)
            {
                finderProbeTransmit = OnFinderProbeTransmit;
                RegisterListener(finderProbeTransmit);
            }
            if (!v.Quick)
                PrintToPlayerChat(player, decoy
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.verify.checking_decoy", candidates.Count)
                    : Localizer.ForPlayer(player, "matchzy.pp.track.verify.checking_smoke", candidates.Count));
        }

        // The test throws are the finder's own measuring, not something to watch: they are not
        // sent to any client (no grenades or trails flying around while it works).
        private Listeners.CheckTransmit? finderProbeTransmit;

        private void OnFinderProbeTransmit(CCheckTransmitInfoList infoList)
        {
            var live = verifyJob?.Live;
            if (live == null || live.Count == 0)
                return;
            try
            {
                foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
                {
                    if (player == null || !player.IsValid)
                        continue;
                    var sent = info.TransmitEntities;
                    foreach (var probe in live)
                    {
                        var ent = new CHandle<CBaseEntity>(probe.Handle).Value;
                        if (ent != null && ent.IsValid)
                            sent.Remove(ent);
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[Finder] transmit: {e.Message}");
            }
        }

        private void TickFinderVerify()
        {
            var v = verifyJob;
            if (v == null)
                return;
            var player = Utilities.GetPlayerFromSlot(v.Slot);
            if (!isPractice || player == null || !player.IsValid || !finderTargets.ContainsKey(v.Slot))
            {
                CancelFinderVerify();
                return;
            }

            int tick = Server.TickCount;
            if (v.Pending.Count > 0 && tick >= v.NextSpawnTick)
            {
                int sameSpotGap = v.Confirming ? VerifyConfirmGap : VerifySameSpotGap;
                int pick = v.Pending.FindIndex(c => !v.LastSpawnAt.TryGetValue(FinderKey(c.Feet), out int at) || tick - at >= sameSpotGap);
                if (pick >= 0)
                {
                    var r = v.Pending[pick];
                    v.Pending.RemoveAt(pick);
                    v.LastSpawnAt[FinderKey(r.Feet)] = tick;
                    v.NextSpawnTick = tick + (v.Confirming ? VerifyConfirmGap : VerifySpawnGap);
                    uint? handle = SpawnVerifyProbe(player, r, v.Decoy, FinderDetonates(v.Type) ? v.Type : null);
                    if (handle.HasValue && v.Type == "he")
                    {
                        var he = new CHandle<CBaseCSGrenadeProjectile>(handle.Value).Value;
                        if (he != null && he.IsValid)
                        {
                            he.Damage = 0f; // a hidden test grenade hurts nobody
                            he.DmgRadius = 0f;
                        }
                    }
                    if (handle.HasValue)
                    {
                        var l = FinderLaunch(r);
                        v.Live.Add(new VerifyProbe
                        {
                            Source = r, Handle = handle.Value, Last = l.Position,
                            // The confirming throws' real flight paths, for the debug log.
                            Path = v.Confirming ? new System.Text.StringBuilder() : null,
                        });
                    }
                    else
                    {
                        v.Done++;
                    }
                }
            }

            for (int i = v.Live.Count - 1; i >= 0; i--)
            {
                var probe = v.Live[i];
                var ent = new CHandle<CBaseCSGrenadeProjectile>(probe.Handle).Value;
                bool gone = ent == null || !ent.IsValid || ent.AbsOrigin == null;
                if (!gone)
                {
                    var pos = new Vector3(ent!.AbsOrigin!.X, ent.AbsOrigin.Y, ent.AbsOrigin.Z);
                    probe.Still = Vector3.Distance(pos, probe.Last) < VerifyStillDistance ? probe.Still + 1 : 0;
                    probe.Last = pos;
                    probe.Ticks++;
                    if (probe.Path != null && probe.Ticks % 2 == 0 && probe.Path.Length < 12000)
                        probe.Path.Append(CultureInfo.InvariantCulture, $"{pos.X:0.0},{pos.Y:0.0},{pos.Z:0.0};");
                    // At rest = the position did not change for a few ticks (strict, so a grenade that
                // still creeps down a slope is not read early). Not the velocity:
                    // a projectile's AbsVelocity can read ~0 mid-flight, which ended probes in the
                    // air (and counted a mid-air point inside the box as a hit).
                    // An HE or flash test grenade is taken away a tick before its fuse runs out:
                    // where it is then is where it goes off, without the bang or the blinding.
                    bool fuseOut = v.Type is "he" or "flash" && probe.Ticks >= (int)MathF.Round(FinderFuseSeconds(v.Type) * 64f) - 1;
                    if (fuseOut)
                    {
                        var fv = ent.AbsVelocity;
                        probe.Last = pos + new Vector3(fv.X, fv.Y, fv.Z) / 64f;
                        probe.Detonated = true;
                    }
                    else if (probe.Still < VerifyStillTicks && probe.Ticks < VerifyMaxTicks)
                        continue;
                    SafeRemoveEntity(ent, "finderprobe");
                }
                else if (FinderDetonates(v.Type) && probe.Ticks > 2)
                {
                    probe.Detonated = true; // went off by itself (a molotov that ignited)
                    if (v.Type == "molotov")
                        RemoveFinderFireNear(probe.Last);
                }
                v.Live.RemoveAt(i);
                v.Done++;
                var result = new FinderResult
                {
                    Feet = probe.Source.Feet, Pitch = probe.Source.Pitch, Yaw = probe.Source.Yaw, Kind = probe.Source.Kind,
                    RunUp = probe.Source.RunUp, RunSpeed = probe.Source.RunSpeed,
                    Rest = probe.Last, Error = GrenadeSimLogic.HorizontalDistance(probe.Last, v.Target.Center),
                    Miss = FinderMiss(v.Target, probe.Last),
                    SimRest = probe.Source.HasSim ? probe.Source.Rest : null,
                    FlightTime = probe.Ticks / 64f,
                };
                if (!v.Confirming)
                {
                    v.Tested.Add(result);
                    v.AllTested.Add(result);
                }
                // Only a probe that really came to rest on a surface counts.
                bool onSurface = probe.Detonated || probe.Still >= VerifyStillTicks
                    && FinderTrace(probe.Last + new Vector3(0, 0, 2f), probe.Last - new Vector3(0, 0, 6f), out _, out var n, out _, v.Ignore != null && v.Ignore.IsValid ? v.Ignore : null)
                    && n.Z >= GrenadeSimLogic.FloorNormalZ;
                FinderLog(string.Format(CultureInfo.InvariantCulture,
                    "[Finder] probe {0} r{1} {2} feet ({3:0.0} {4:0.0} {5:0.0}) aim ({6:0.000} {7:0.000}) rest ({8:0.0} {9:0.0} {10:0.0}) t {11:0.00} surface={12} box={13} sim ({14:0.0} {15:0.0} {16:0.0})",
                    v.Confirming ? "confirm" : "test", v.Round, result.Kind, result.Feet.X, result.Feet.Y, result.Feet.Z, result.Pitch, result.Yaw,
                    probe.Last.X, probe.Last.Y, probe.Last.Z, result.FlightTime, onSurface, FinderHits(v.Target, probe.Last),
                    probe.Source.Rest.X, probe.Source.Rest.Y, probe.Source.Rest.Z), fileOnly: true);
                if (probe.Path != null)
                {
                    var pl = FinderLaunch(result);
                    FinderLog(string.Format(CultureInfo.InvariantCulture,
                        "[Finder] path launch ({0:0.00} {1:0.00} {2:0.00}) vel ({3:0.00} {4:0.00} {5:0.00}) every 2 ticks: {6}",
                        pl.Position.X, pl.Position.Y, pl.Position.Z, pl.Velocity.X, pl.Velocity.Y, pl.Velocity.Z, probe.Path), fileOnly: true);
                }
                if (onSurface && FinderHits(v.Target, probe.Last))
                    (v.Confirming ? v.Confirmed : v.Verified).Add(result);
                else if (v.Confirming)
                    FinderLog(string.Format(CultureInfo.InvariantCulture,
                        "[Finder] dropped: second throw of ({0:0.00} {1:0.00}) rested {2:0.0} u from the first",
                        result.Pitch, result.Yaw, Vector3.Distance(probe.Last, probe.Source.Rest)));
            }

            double now = Server.CurrentTime;
            if (now >= v.NextHint)
            {
                v.NextHint = now + 0.5;
                player.PrintToCenterAlert(v.Confirming
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.verify.confirming", v.Done, v.Total)
                    : v.Round == 0
                    ? Localizer.ForPlayer(player, "matchzy.pp.track.verify.testing", v.Done, v.Total)
                    : Localizer.ForPlayer(player, "matchzy.pp.track.verify.fine_tuning", v.Done, v.Total, v.Round, VerifyRounds));
            }

            if (v.Pending.Count > 0 || v.Live.Count > 0)
                return;

            if (v.Confirming)
            {
                v.Confirming = false;
                if (v.Confirmed.Count > 0)
                {
                    verifyJob = null;
                    FinderLog($"[Finder] confirmed {v.Confirmed.Count} of {v.Total} throws (thrown twice) in {v.Clock.Elapsed.TotalSeconds:0.0} s (round {v.Round})");
                    ShowFinderResults(player, v.Slot, v.Confirmed, v.PlayerFeet, v.Ignore, v.Clock.Elapsed.TotalSeconds, v.Here);
                    return;
                }
                v.Verified.Clear();
            }
            else if (v.Verified.Count > 0)
            {
                // The calculation and the game agree on these (same resting point within 3 u): that
                // is the second opinion already, no need to throw them again.
                var agreed = v.Verified.Where(r => r.SimRest is Vector3 sim && Vector3.Distance(sim, r.Rest) <= VerifyAgreeDistance).ToList();
                if (agreed.Count > 0)
                {
                    verifyJob = null;
                    FinderLog($"[Finder] {agreed.Count} of {v.Tested.Count} throws landed where calculated, in {v.Clock.Elapsed.TotalSeconds:0.0} s (round {v.Round})");
                    ShowFinderResults(player, v.Slot, agreed, v.PlayerFeet, v.Ignore, v.Clock.Elapsed.TotalSeconds, v.Here);
                    return;
                }
                // Only one or two so far: one fine-tuning round around the closest throws first,
                // so there is more than a single lineup to choose from.
                if (v.Verified.Count < VerifyWant && v.Round == 0 && !v.Quick && !v.Here && QueueVerifyVariants(v))
                    return;
                // Second, undisturbed throw of the best ones (spread over spots, nearest first).
                var again = new List<FinderResult>();
                foreach (var r in v.Verified
                    .OrderBy(r => MathF.Floor(GrenadeSimLogic.HorizontalDistance(r.Feet, v.PlayerFeet) / 64f))
                    .ThenBy(r => r.Error))
                {
                    if (v.Here
                        ? again.Any(c => c.Kind == r.Kind && Math.Abs(c.Pitch - r.Pitch) < 1f && Math.Abs(c.Yaw - r.Yaw) < 1f)
                        : again.Count(c => c.Kind == r.Kind && GrenadeSimLogic.HorizontalDistance(c.Feet, r.Feet) < 48f) >= 2)
                        continue;
                    again.Add(r);
                    if (again.Count == VerifyConfirmMax)
                        break;
                }
                v.Confirming = true;
                v.Pending.AddRange(again);
                v.LastSpawnAt.Clear();
                v.Done = 0;
                v.Total = again.Count;
                v.NextSpawnTick = Server.TickCount + VerifyConfirmGap;
                return;
            }

            // From one spot the calculation already looked in 0.1 degree steps: one sweep is enough.
            if (v.Round < (v.Quick || v.Here ? 1 : VerifyRounds) && QueueVerifyVariants(v))
                return;

            verifyJob = null;
            player.PrintToCenterAlert(" ");
            var closest = v.Tested.OrderBy(t => t.Error).FirstOrDefault();
            if (v.AreaStage)
            {
                FinderLog($"[Finder] no throw from the area landed in the box ({v.Tested.Count} tested, closest {closest?.Error ?? -1:0} u): searching wider");
                ContinueFinderWide(player, v.PlayerFeet);
                return;
            }
            if (v.Here && !v.Quick)
            {
                var nearest = v.AllTested.Where(t => t.Miss > 0f).OrderBy(t => t.Miss).Take(40).ToList();
                foreach (var t in nearest)
                    t.NoHit = true;
                FinderLog(string.Format(CultureInfo.InvariantCulture,
                    "[Finder] no throw from the spot itself landed in the box ({0} tested, closest {1:0.0} u outside)", v.AllTested.Count, nearest.FirstOrDefault()?.Miss ?? -1f));
                // A near miss (a few units outside) is still worth showing. Anything further is
                // not a lineup for this box (measured: "closest" landed on the ground 100 u below
                // a ledge, and the player was put on it as if it were a result): look around instead.
                if (nearest.Count > 0 && nearest[0].Miss <= 24f)
                {
                    ShowFinderResults(player, v.Slot, nearest.Where(t => t.Miss <= 24f).ToList(), v.PlayerFeet, v.Ignore, v.Clock.Elapsed.TotalSeconds, true);
                    return;
                }
                ContinueFinderAround(player, v.PlayerFeet); // says so in chat itself
                return;
            }
            if (v.Quick)
            {
                player.PrintToCenterAlert(Localizer.ForPlayer(player, "matchzy.pp.track.verify.live_none"));
                return;
            }
            FinderLog($"[Finder] no engine throw landed in the box: {v.Tested.Count} tested, closest {closest?.Error ?? -1:0} u");
            PrintToPlayerChat(player, closest == null
                ? Localizer.ForPlayer(player, "matchzy.pp.track.verify.none")
                : Localizer.ForPlayer(player, "matchzy.pp.track.verify.none_closest", closest.Error));
        }

        // Next round: the throws whose real projectile came closest, each varied around its aim.
        // Round 1 steps 0.5 degrees, each later round half that around the new best.
        private bool QueueVerifyVariants(VerifyJob v)
        {
            float step = v.Round == 0 ? 0.5f : v.Round == 1 ? 0.25f : 0.125f;
            var seeds = v.Tested
                .Where(t => t.Miss < 400f)
                .OrderBy(t => t.Miss)
                .Take(6)
                .ToList();
            if (seeds.Count == 0)
                return false;
            var pool = v.Tested.Where(t => t.Miss < 400f).OrderBy(t => t.Miss).ToList();
            v.Round++;
            v.Done = 0;
            v.LastSpawnAt.Clear();
            v.Tested.Clear();
            // First round: the real throws usually stop at the same obstacle (a wall
            // under a window), so a small step around them changes nothing. Sweep the height in
            // quarter degrees up to 2 degrees either way, and a little to the sides.
            bool sweep = !v.Quick && v.Round == 1;
            if (sweep)
            {
                var spread = new List<FinderResult>();
                foreach (var t in pool)
                {
                    if (spread.Any(c => c.Kind == t.Kind && GrenadeSimLogic.HorizontalDistance(c.Feet, t.Feet) < 24f
                            && Math.Abs(c.Pitch - t.Pitch) < 3f && Math.Abs(c.Yaw - t.Yaw) < 1.5f))
                        continue;
                    spread.Add(t);
                    if (spread.Count == 8)
                        break;
                }
                foreach (var s in spread)
                {
                    for (int i = -8; i <= 8; i++)
                        if (i != 0)
                            v.Pending.Add(new FinderResult { Feet = s.Feet, Kind = s.Kind, Pitch = Math.Clamp(s.Pitch + i * 0.25f, -89f, 89f), Yaw = s.Yaw });
                    foreach (float dyaw in new[] { -1f, -0.5f, 0.5f, 1f })
                        v.Pending.Add(new FinderResult { Feet = s.Feet, Kind = s.Kind, Pitch = s.Pitch, Yaw = s.Yaw + dyaw });
                    v.Tested.Add(s);
                }
                v.Total = v.Pending.Count;
                return true;
            }
            foreach (var s in seeds)
            {
                for (int dp = -1; dp <= 1; dp++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dp == 0 && dy == 0)
                            continue;
                        v.Pending.Add(new FinderResult
                        {
                            Feet = s.Feet, Kind = s.Kind,
                            Pitch = Math.Clamp(s.Pitch + dp * step, -89f, 89f), Yaw = s.Yaw + dy * step,
                        });
                    }
                }
                // Keep the seed itself in the running so the best so far is not lost.
                v.Tested.Add(s);
            }
            v.Total = v.Pending.Count;
            return true;
        }

        // The fire a hidden molotov test left behind.
        private void RemoveFinderFireNear(Vector3 at)
        {
            try
            {
                foreach (var fire in Utilities.FindAllEntitiesByDesignerName<CInferno>("inferno"))
                {
                    var o = fire.AbsOrigin;
                    if (fire.IsValid && o != null && Vector3.Distance(new Vector3(o.X, o.Y, o.Z), at) < 160f)
                        SafeRemoveEntity(fire, "finderfire");
                }
            }
            catch (Exception e)
            {
                FinderLog($"[Finder] fire not removed: {e.Message}", fileOnly: true);
            }
        }

        private uint? SpawnVerifyProbe(CCSPlayerController player, FinderResult r, bool decoy, string? type = null)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid)
                return null;
            var l = FinderLaunch(r);
            var pos = new Vector(l.Position.X, l.Position.Y, l.Position.Z);
            var ang = new QAngle(0, 0, 0);
            var vel = new Vector(l.Velocity.X, l.Velocity.Y, l.Velocity.Z);
            var spin = new Vector(600, 0, 0);
            ushort item = decoy ? DecoyItemIndex : SmokeItemIndex;
            CBaseCSGrenadeProjectile? e = null;
            // The other grenade types (.simtest he / flash / molotov): made as the practice
            // rethrow makes them.
            if (type is "he" or "flash" or "molotov")
            {
                item = type == "he" ? (ushort)44 : type == "flash" ? (ushort)43 : (ushort)46;
                try
                {
                    if (type == "he")
                        e = GrenadeFunctions.CHEGrenadeProjectile_CreateFunc?.Invoke(pos.Handle, ang.Handle, vel.Handle, spin.Handle, IntPtr.Zero, item);
                    else if (type == "molotov")
                        e = GrenadeFunctions.CMolotovProjectile_CreateFunc?.Invoke(pos.Handle, ang.Handle, vel.Handle, spin.Handle, IntPtr.Zero, item);
                }
                catch (Exception ex)
                {
                    FinderLog($"[Finder] grenade factory failed: {ex.Message}");
                }
                if (e == null)
                {
                    CBaseCSGrenadeProjectile? made = type == "he"
                        ? Utilities.CreateEntityByName<CHEGrenadeProjectile>("hegrenade_projectile")
                        : type == "flash"
                            ? Utilities.CreateEntityByName<CFlashbangProjectile>("flashbang_projectile")
                            : Utilities.CreateEntityByName<CMolotovProjectile>("molotov_projectile");
                    made?.DispatchSpawn();
                    e = made;
                }
            }
            else
            try
            {
                e = decoy
                    ? GrenadeFunctions.CDecoyProjectile_CreateFunc?.Invoke(pos.Handle, ang.Handle, vel.Handle, spin.Handle, IntPtr.Zero, item)
                    : GrenadeFunctions.CSmokeGrenadeProjectile_CreateFunc?.Invoke(pos.Handle, ang.Handle, vel.Handle, spin.Handle, IntPtr.Zero, item, (int)player.Team);
            }
            catch (Exception ex)
            {
                FinderLog($"[Finder] grenade factory failed: {ex.Message}");
            }
            if (e == null && type is not ("he" or "flash" or "molotov"))
            {
                CBaseCSGrenadeProjectile? created = decoy
                    ? Utilities.CreateEntityByName<CDecoyProjectile>("decoy_projectile")
                    : Utilities.CreateEntityByName<CSmokeGrenadeProjectile>("smokegrenade_projectile");
                created?.DispatchSpawn();
                e = created;
            }
            if (e == null || !e.IsValid)
                return null;
            e.ItemIndex = item;
            e.InitialPosition.X = pos.X; e.InitialPosition.Y = pos.Y; e.InitialPosition.Z = pos.Z;
            e.InitialVelocity.X = vel.X; e.InitialVelocity.Y = vel.Y; e.InitialVelocity.Z = vel.Z;
            e.AngVelocity.X = spin.X; e.AngVelocity.Y = spin.Y; e.AngVelocity.Z = spin.Z;
            e.Teleport(pos, ang, vel);
            // "custom": the grenade history, smoke timers and colour paths skip it.
            e.Globalname = "custom";
            e.TeamNum = player.TeamNum;
            e.Thrower.Raw = pawn.EntityHandle.Raw;
            e.OriginalThrower.Raw = pawn.EntityHandle.Raw;
            e.OwnerEntity.Raw = pawn.EntityHandle.Raw;
            return e.EntityHandle.Raw;
        }

        private void CancelFinderVerify()
        {
            var v = verifyJob;
            verifyJob = null;
            if (v == null)
                return;
            foreach (var probe in v.Live)
            {
                var ent = new CHandle<CBaseEntity>(probe.Handle).Value;
                if (ent != null && ent.IsValid)
                    SafeRemoveEntity(ent, "finderprobe");
            }
            v.Live.Clear();
        }
    }
}
