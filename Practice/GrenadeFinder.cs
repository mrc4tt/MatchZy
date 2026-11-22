using Stopwatch = System.Diagnostics.Stopwatch;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Vector3 = System.Numerics.Vector3;

namespace MatchZy
{
    // Grenade Finder, stage 1 (practice-plus item 6). `.draw` marks the target the PRACC way: R on
    // the first corner, move the aim to unfold the area and R, R for the height; the box follows
    // the crosshair live at the height of the first corner (exactly where the crosshair hit)
    // (`.draw auto` fits a box to the clicked surface in one R). The grenade has to come to rest inside that box (a floor spot, a ledge, the
    // top of a beam). `.find [radius]` then searches
    // the walkable nav mesh around you for spots a smoke thrown from (standing or jumpthrow) comes
    // to rest in that box, using the step-0 throw model and a traced flight simulation
    // (GrenadeSimLogic). Results are numbered boxes on the floor plus a chat list; `.findgo <n>`
    // puts you on spot n with the exact view angles and a smoke in hand.
    // Stage 2 (not here yet): verify the best spots with real test projectiles, stability check,
    // aim + E pick, run throws.
    public partial class MatchZy
    {
        // Target volume: a box on a surface (floor, ledge, beam top) the grenade has to end up in.
        private sealed class FinderTarget
        {
            public Vector3 Min, Max;
            // Lowest floor under the box. The box bottom is a flat plane at the first corner's
            // height; on a slope or beside a step the real floor inside the area is lower.
            public float Floor;
            // .find cover (smoke only): the grenade may rest just outside the box when its cloud
            // covers it (the usual window smoke lies on the ledge below the window). Off by default:
            // the grenade has to rest inside the drawn area.
            public bool Cover;
            public readonly List<uint> Entities = new();
            public Vector3 Center => new((Min.X + Max.X) / 2f, (Min.Y + Max.Y) / 2f, Min.Z);
            public float Half => Math.Max(Max.X - Min.X, Max.Y - Min.Y) / 2f;
        }

        // How far outside the box a resting point is (0 = inside), in all three directions.
        private static float FinderMiss(FinderTarget t, Vector3 rest)
        {
            float lowZ = Math.Min(t.Min.Z, t.Floor) - 4f;
            float dx = Math.Max(Math.Max(t.Min.X - rest.X, rest.X - t.Max.X), 0f);
            float dy = Math.Max(Math.Max(t.Min.Y - rest.Y, rest.Y - t.Max.Y), 0f);
            float dz = Math.Max(Math.Max(lowZ - rest.Z, rest.Z - t.Max.Z), 0f);
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // A smoke cloud reaches well over 100 u; a grenade resting this close to the box with an
        // open line to it covers it.
        private const float FinderCoverReach = 60f;

        private static bool FinderHits(FinderTarget t, Vector3 rest)
        {
            if (rest.X >= t.Min.X && rest.X <= t.Max.X && rest.Y >= t.Min.Y && rest.Y <= t.Max.Y
                && rest.Z >= Math.Min(t.Min.Z, t.Floor) - 4f && rest.Z <= t.Max.Z)
                return true;
            if (!t.Cover || FinderMiss(t, rest) > FinderCoverReach)
                return false;
            // Open line from a little above the grenade to the nearest part of the box (not behind a wall).
            float lowZ = Math.Min(t.Min.Z, t.Floor);
            var nearest = new Vector3(Math.Clamp(rest.X, t.Min.X, t.Max.X), Math.Clamp(rest.Y, t.Min.Y, t.Max.Y),
                Math.Clamp(rest.Z + 24f, lowZ + 16f, Math.Max(lowZ + 16f, t.Max.Z)));
            return !FinderTrace(rest + new Vector3(0, 0, 24f), nearest, out _, out _, out _);
        }

        // .draw, PRACC nade-finder style: R sets the first ground point, R the opposite corner, R the
        // height; the box follows the crosshair live in between. R afterwards resets the target.
        private sealed class DrawSession
        {
            public int Step; // 0 = first point, 1 = second corner, 2 = height
            public bool Manual; // three steps (default); .draw auto: one R fits the clicked surface
            public Vector3 First, Second;
            public float Height = 64f;
            public bool ReloadWasDown;
            public readonly List<uint> Preview = new();
            public double NextHint;
        }

        private sealed class FinderResult
        {
            public Vector3 Feet;
            public float Pitch, Yaw;
            public GrenadeSimLogic.ThrowKind Kind;
            public Vector3 Rest;
            public float Error;
            public float FlightTime;
            // Distance outside the box; above 0 only for a "closest throw" shown when nothing lands.
            public float Miss;
            // Shown as "closest throw" because nothing landed.
            public bool NoHit;
            // HasSim: Rest is the calculated resting point (set where the calculation makes a
            // result). SimRest: that point, kept on the result of the real test throw.
            public bool HasSim;
            // A lineup from the saved library (no drawn box: its own landing point is the target).
            public bool Library;
            public string Name = "";
            public Vector3? SimRest;
            // Share of slightly-off throws (aim, run-up) that still land in the box, 0..1.
            public float Robust = -1f;
            // The run the lineup was worked out for (running throws): the model's run-up and
            // speed can change later when a player's own run is learned.
            public float RunUp = GrenadeSimLogic.RunUp, RunSpeed = GrenadeSimLogic.RunSpeed;
            public float EyeHeight = GrenadeSimLogic.EyeHeight;
            public string Type = finderCurrentType;
            // A saved lineup that carries its own release (NadePos / NadeVel in the file): used as
            // it is, whatever kind of throw it was. What = the throw as the file words it.
            public GrenadeSimLogic.Launch? Launch;
            public bool FreeKind;
            public string What = "";
            public int EyeTicks; // checks in a row the player stood still on the spot (eye height refit)
            public bool EyeChecked;
        }

        private sealed class FinderJob
        {
            public int Slot;
            public Vector3 PlayerFeet;
            public List<Vector3> Candidates = new();
            public int Next;
            public readonly List<FinderResult> Found = new();
            // Simulated throws that stopped close to the target: the engine test may still put
            // them inside (the simulation is only approximate).
            public readonly List<FinderResult> Near = new();
            public long Traces;
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public int LastProgress = -1;
            public double MaxTickMs;
            public double StartMs;
            // The searching player's pawn: traces pass through it (their own spot is a candidate).
            public CBaseEntity? Ignore;
            public float Radius;
            public Vector3 Target;
            // Every spot already tried, so a wider pass only checks new ones.
            public readonly HashSet<(int, int, int)> Checked = new();
            // Deep pass around the player first: a fan of aims per spot, kind and arc.
            public List<DeepUnit> Deep = new();
            // Wide pass, one simulated throw per unit so a tick can stop between any two.
            public readonly Stack<WideUnit> Wide = new();
            // .find here: only throws from exactly where the player stands.
            public bool Here;
            // .find live: the short version of it (aims straight at the target only), quiet.
            public bool Quick;
            // The spots around the player are tested for real on their own before anything wider.
            public bool AreaStage;
            // .find from one spot: the first FastCount aims are the straight ones.
            public int FastCount;
            public bool FastChecked;
            public bool Refined;
            public double SlowUnitMs;
            public int SlowUnits;
            public string SlowUnit = "";
            public int Gc0 = GC.CollectionCount(0), Gc2 = GC.CollectionCount(2);
            public double WidenMs;
            public int DeepNext;
        }

        private readonly record struct WideUnit(Vector3 Feet, GrenadeSimLogic.ThrowKind Kind, float Pitch, Vector3 Aim, int Step);
        private readonly record struct DeepUnit(Vector3 Feet, GrenadeSimLogic.ThrowKind Kind, float Pitch, float Yaw);

        private const float DeepRadius = 400f;
        private const float DeepGrid = 32f;
        private const float DeepPitchSpan = 4f;
        private const float DeepPitchStep = 0.5f;
        private const float DeepYawSpan = 1f;
        private const float DeepYawStep = 0.5f;

        // .find widens on its own while nothing is found: these radii, the last one is the whole map.
        private static readonly float[] FinderRadii = { 1500f, 3000f, 5000f, 100000f };

        private const float FinderDefaultRadius = 1500f;
        private const float FinderGrid = 64f;
        private const int FinderMaxCandidates = 900;
        private const float FinderMinDistance = 120f;
        private const float FinderMaxDistance = 2800f;
        private const float FinderLevelTolerance = 40f;
        private const int FinderRefineSteps = 4;
        private const int FinderShowResults = 6;
        private const double FinderTickBudgetMs = 4.0;

        private readonly Dictionary<int, FinderTarget> finderTargets = new();
        private readonly Dictionary<int, List<FinderResult>> finderResults = new();
        private readonly Dictionary<int, List<uint>> finderResultEntities = new();
        private FinderJob? finderJob;
        private readonly Dictionary<int, DrawSession> drawSessions = new();

        [ConsoleCommand("css_draw", "Practice: mark a grenade target box (R x3), then .find searches throw spots")]
        // .draw / .find can be switched off by the server (matchzy_practice_finder_enabled).
        private bool FinderAllowed(CCSPlayerController player)
        {
            if (practiceFinderEnabled.Value)
                return true;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.off"));
            return false;
        }

        public void OnFinderDrawCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (!FinderAllowed(player))
                return;
            if (!HasCssTraceApi)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.needs_build"));
                return;
            }
            string arg = FinderWords(command).FirstOrDefault() ?? "";
            // .draw again (or .draw off) turns the finder off and removes everything.
            if (arg is "clear" or "off" or "remove" || drawSessions.ContainsKey(player.Slot) || finderTargets.ContainsKey(player.Slot))
            {
                CancelDrawSession(player.Slot);
                ClearFinderTarget(player.Slot);
                ClearFinderResults(player.Slot);
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.draw.off"));
                return;
            }
            bool manual = arg != "auto";
            drawSessions[player.Slot] = new DrawSession { Manual = manual };
            // The grenade in hand is the one searched for: say so, and take it out if none is.
            string drawType = FinderHeldType(player) ?? FinderTypeFor(player.Slot);
            finderType[player.Slot] = drawType;
            finderDecoy[player.Slot] = drawType == "decoy";
            int drawSlot = player.Slot;
            Server.NextFrame(() =>
            {
                var dp = Utilities.GetPlayerFromSlot(drawSlot);
                if (dp != null && IsPlayerValid(dp) && isPractice)
                    FinderTakeOutGrenade(dp, drawType);
            });
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.searching_for", FinderTypeWord(player, drawType)));
            PrintToPlayerChat(player, manual
                ? Localizer.ForPlayer(player, "matchzy.pp.finder.draw.manual")
                : Localizer.ForPlayer(player, "matchzy.pp.finder.draw.auto"));
        }

        [ConsoleCommand("css_find", "Practice: find spots to throw a smoke into your .draw target")]
        public void OnFinderFindCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (!FinderAllowed(player))
                return;
            if (!finderTargets.TryGetValue(player.Slot, out var target))
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.find.no_target"));
                return;
            }
            if (finderJob != null)
            {
                PrintToPlayerChat(player, finderJob.Slot == player.Slot ? Localizer.ForPlayer(player, "matchzy.pp.finder.find.still_searching") : Localizer.ForPlayer(player, "matchzy.pp.finder.find.other_player"));
                return;
            }
            // One search at a time, also while its throws are being checked: a second .find would
            // cancel the first one's check without a word. (The fallbacks from a finished check
            // come in with finderAreaNext set and their own verify already gone.)
            if (verifyJob != null)
            {
                PrintToPlayerChat(player, verifyJob.Slot == player.Slot ? Localizer.ForPlayer(player, "matchzy.pp.finder.find.still_checking") : Localizer.ForPlayer(player, "matchzy.pp.finder.find.other_player"));
                return;
            }
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                return;
            float radius = FinderDefaultRadius;
            // Default: the lineup from exactly where the player stands (after .draw they go, or
            // noclip, to the position they want to throw from). When nothing from that exact spot
            // lands, it goes on with the spots around it by itself. ".find area" starts there.
            var words = FinderWords(command);
            // The grenade in hand is the one searched for (a fallback search keeps the first one's).
            if (!finderAreaNext)
            {
                string held = FinderHeldType(player) ?? FinderTypeFor(player.Slot);
                if (held != FinderTypeFor(player.Slot))
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.searching_for_now", FinderTypeWord(player, held)));
                finderType[player.Slot] = held;
                finderDecoy[player.Slot] = held == "decoy";
            }
            // No grenade in hand: every type is tried from this spot, one after the other, and the
            // player is told which of them reach the box (an HE or flash goes off after 1.6 s and
            // a molotov after 2 s in the air, so those only reach so far).
            if (!finderAreaNext && !finderMultiStep)
            {
                finderMultiQueue.Clear();
                finderMultiResults.Clear();
                finderMultiNotes.Clear();
                finderMulti = false;
                var pp = player.PlayerPawn?.Value?.AbsOrigin;
                if (FinderTryEveryType && FinderHeldType(player) == null && pp != null)
                {
                    float away = GrenadeSimLogic.HorizontalDistance(new Vector3(pp.X, pp.Y, pp.Z), target.Center);
                    finderMultiQueue.Add("smoke");
                    if (away <= 1700f) finderMultiQueue.Add("molotov"); else finderMultiNotes.Add(Localizer.ForPlayer(player, "matchzy.pp.finder.multi.molotov_too_far"));
                    if (away <= 1300f) { finderMultiQueue.Add("flash"); finderMultiQueue.Add("he"); }
                    else finderMultiNotes.Add(Localizer.ForPlayer(player, "matchzy.pp.finder.multi.flash_he_too_far"));
                    finderMulti = true;
                    finderType[player.Slot] = finderMultiQueue[0];
                    finderMultiQueue.RemoveAt(0);
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.multi.trying_all"));
                }
            }
            finderCurrentType = FinderTypeFor(player.Slot);
            if (!finderAreaNext)
                finderDemoThrow = words.RemoveAll(w => w is "demo" or "preview") > 0;
            if (!finderAreaNext)
                finderVerify = words.RemoveAll(w => w is "check" or "verify" or "test") > 0; // no test throws unless asked (".find check"): the author's call, twice
            if (!finderAreaNext)
                finderAllowRun = words.RemoveAll(w => w is "norun" or "still") == 0; // rolled back: running throws included, ".find norun" leaves them out
            // The grenade has to come to rest inside the drawn area. ".find cover" also accepts a
            // smoke that rests just outside it and covers it (not for a decoy).
            if (command != null || finderChatArgs != null)
            {
                bool cover = words.RemoveAll(w => w is "cover" or "smoke") > 0;
                bool decoySearch = FinderHeldIsDecoy(player, out _) || (finderDecoy.TryGetValue(player.Slot, out bool dc) && dc
                    && player.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon.Value?.DesignerName != "weapon_smokegrenade");
                target.Cover = cover && !decoySearch;
            }
            string mode = words.Count > 0 ? words[0] : "";
            if (finderAreaNext)
                mode = "near";
            finderAreaNext = false;
            if (mode is "live" or "walk" or "follow")
            {
                if (finderLive.Remove(player.Slot))
                {
                    player.PrintToCenterAlert(" ");
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.live.off"));
                    return;
                }
                finderLive[player.Slot] = new FinderLiveState();
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.live.on"));
                return;
            }
            // First the exact spot the player stands on, with every aim (also off walls and
            // floors): only that search finds the long bounced lineups, such as the window smoke
            // from T spawn on mirage. Area-first (tried for a day) only tries the straight arcs
            // from each spot and found nothing there. When the spot itself has nothing, the
            // search goes on to the area and then further out by itself. ".find area" starts
            // with the area.
            // Rolled back 2026-11-17 (the author's call): plain .find searches the area again,
            // ".find here" the exact spot.
            if (mode is "here" or "her" or "me" or "spot" or "exact")
            {
                finderCarry.Remove(player.Slot);
                finderHereOnly = true;
                StartFinderHere(player, pawn, target, false);
                return;
            }
            if (words.Count > 0 && float.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
                radius = Math.Clamp(r, 200f, 4000f);

            var setupClock = Stopwatch.StartNew();
            // Going on from a search at one spot: around that spot, not where the player has
            // wandered (or noclipped) to in the meantime.
            var feet = finderAreaFeet ?? new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            finderAreaFeet = null;
            List<Vector3> candidates;
            try
            {
                candidates = FinderCandidates(feet, target.Center, radius, null);
            }
            catch (Exception e)
            {
                FinderLog($"[Finder] nav mesh read failed: {e.GetType().Name}: {e.Message}");
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.find.no_navmesh"));
                return;
            }
            if (candidates.Count == 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.find.no_spots"));
                return;
            }
            ClearFinderResults(player.Slot);
            GrenadeSimLogic.EyeHeight = GrenadeSimLogic.StandingEyeHeight; // other spots than the player's own
            var job = new FinderJob { Slot = player.Slot, PlayerFeet = feet, Radius = radius, Target = target.Center, Ignore = pawn };
            // Deep pass: where the player stands, then a fine grid around them.
            var near = new List<Vector3>();
            bool skipDeep = finderSkipDeep;
            finderSkipDeep = false;
            if (!skipDeep && FinderTrace(feet + new Vector3(0, 0, 16f), feet - new Vector3(0, 0, 64f), out var standOn, out var sn, out _, pawn) && sn.Z >= GrenadeSimLogic.FloorNormalZ)
                near.Add(standOn);
            try
            {
                // skipDeep: the area was already tested for real, only the wide pass is left.
                foreach (var raw in skipDeep ? new List<Vector3>() : FinderCandidates(feet, target.Center, DeepRadius, null, 160, DeepGrid))
                    if (SnapCandidate(raw) is Vector3 snapped)
                        near.Add(snapped);
            }
            catch
            {
                // No nav mesh: the player's own spot only.
            }
            foreach (var c in near)
            {
                if (!job.Checked.Add(FinderKey(c)) && c != near[0])
                    continue;
                // Not from inside or right next to the target (a throw straight up is no lineup).
                if (GrenadeSimLogic.HorizontalDistance(c, target.Center) < FinderMinDistance + target.Half)
                    continue;
                float yaw0 = GrenadeSimLogic.YawTo(c, target.Center);
                bool canRun = FinderRunPathClear(c, yaw0, pawn);
                foreach (var kind in FinderKinds)
                {
                    if (FinderKindRuns(kind) && !canRun)
                        continue;
                    var arcs = GrenadeSimLogic.BallisticPitches(c, kind, target.Center);
                    foreach (float arc in arcs)
                        for (float yo = -DeepYawSpan; yo <= DeepYawSpan + 0.001f; yo += DeepYawStep)
                            for (float pitch = arc - DeepPitchSpan; pitch <= arc + DeepPitchSpan + 0.001f; pitch += DeepPitchStep)
                                if (pitch > -89f && pitch < 89f)
                                    job.Deep.Add(new DeepUnit(c, kind, pitch, yaw0 + yo));
                    // The target is further than a jumpthrow flies: it can only be reached with
                    // bounces (the window smoke from T spawn on mirage: 2500 u, 7 s, off the roofs).
                    // No straight arc exists then and this spot was skipped altogether. A coarse
                    // sweep toward the target instead; the nearest misses are then looked at in
                    // 0.1 degree steps (below).
                    if (FinderAreaBounceSweep && arcs.Count == 0 && kind == GrenadeSimLogic.ThrowKind.Jump)
                        for (float yo = -2f; yo <= 2.001f; yo += 1f)
                            for (float pitch = -50f; pitch <= 6.001f; pitch += 2f)
                                job.Deep.Add(new DeepUnit(c, kind, pitch, yaw0 + yo));
                }
            }
            job.Candidates = candidates.Where(c => !job.Checked.Contains(FinderKey(c))).ToList();
            foreach (var c in job.Candidates)
                job.Checked.Add(FinderKey(c));
            job.StartMs = setupClock.Elapsed.TotalMilliseconds;
            finderJob = job;
            PrintToPlayerChat(player, finderAllowRun
                ? Localizer.ForPlayer(player, "matchzy.pp.finder.search.area.run", near.Count, DeepRadius)
                : Localizer.ForPlayer(player, "matchzy.pp.finder.search.area.norun", near.Count, DeepRadius));
        }

        // .find here: every aim from the exact spot the player stands on. All directions within
        // HereYawSpan of the target (so throws off a wall count too) and all pitches, standing and
        // jumpthrow, 1 degree apart; the real test throws then fine-tune the closest ones.
        private const float HereYawSpan = 60f;
        private const float HereStep = 1f;

        // Nothing from the exact spot: go on with the spots around the player, nearest first, and
        // wider from there (the search .find near starts with).
        private bool finderAreaNext;
        private bool finderSkipDeep;
        private Vector3? finderAreaFeet;

        // Nothing in the player's own area landed for real: the wide search from that spot.
        private void ContinueFinderWide(CCSPlayerController player, Vector3 origin)
        {
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.wide.next"));
            finderAreaFeet = origin;
            finderAreaNext = true;
            finderSkipDeep = true;
            OnFinderFindCommand(player, null);
            finderAreaNext = false;
            finderSkipDeep = false;
            finderAreaFeet = null;
        }

        private void ContinueFinderAround(CCSPlayerController player, Vector3 origin, bool quiet = false)
        {
            finderAreaFeet = origin;
            if (!quiet)
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.around.next"));
            finderAreaNext = true;
            OnFinderFindCommand(player, null);
            finderAreaNext = false;
            finderAreaFeet = null;
        }

        private sealed class FinderLiveState
        {
            public Vector3 Checked = new(float.MaxValue, 0, 0);
            public Vector3 Last;
            public int Still;
        }

        private readonly Dictionary<int, FinderLiveState> finderLive = new();
        private const int LiveStillTicks = 24;
        private const float LiveMoveDistance = 20f;

        // .find live: whenever the player comes to a stop somewhere new, the quick search runs
        // from there on its own.
        private void TickFinderLive()
        {
            if (finderJob != null || verifyJob != null)
                return;
            foreach (var (slot, state) in finderLive)
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !IsPlayerValid(player) || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null
                    || !finderTargets.TryGetValue(slot, out var target))
                    continue;
                var pos = new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
                bool grounded = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
                state.Still = grounded && Vector3.Distance(pos, state.Last) < 0.5f ? state.Still + 1 : 0;
                state.Last = pos;
                if (Vector3.Distance(pos, state.Checked) < LiveMoveDistance)
                    continue;
                if (state.Still < LiveStillTicks)
                {
                    if (state.Still == 0 && Server.TickCount % 32 == 0)
                        player.PrintToCenterAlert(Localizer.ForPlayer(player, "matchzy.pp.finder.live.stop"));
                    continue;
                }
                state.Checked = pos;
                StartFinderHere(player, pawn, target, true);
                return; // one search at a time
            }
        }

        private void StartFinderHere(CCSPlayerController player, CCSPlayerPawn pawn, FinderTarget target, bool quick)
        {
            var setupClock = Stopwatch.StartNew();
            var feet = new Vector3(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            // In the air or in noclip: the floor below is the spot.
            if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0)
            {
                // Where a player dropped here comes to stand (a box of the player's size, not a
                // ray: a ray finds ledges and slopes a player slides off again, and every throw
                // after that leaves from somewhere else than the lineup says).
                if (!FinderPlayerDrop(feet, pawn, out var below, out var bn))
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.here.no_floor"));
                    return;
                }
                if (bn.Z < GrenadeSimLogic.FloorNormalZ)
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.here.too_steep"));
                    return;
                }
                feet = below;
            }
            // The player's own standing view height when they stand here now (not ducked, not in the air).
            bool standing = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0 && (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) == 0;
            float view = pawn.ViewOffset.Z;
            GrenadeSimLogic.EyeHeight = standing && view > 58f && view < 66f ? view : GrenadeSimLogic.StandingEyeHeight;
            ClearFinderResults(player.Slot);
            var job = new FinderJob { Slot = player.Slot, PlayerFeet = feet, Radius = 0f, Target = target.Center, Ignore = pawn, Here = true, Quick = quick };
            job.Checked.Add(FinderKey(feet));
            float yaw0 = GrenadeSimLogic.YawTo(feet, target.Center);
            if (quick)
            {
                // Straight at the target only: a few degrees around each arc that reaches it.
                bool runOk = FinderRunPathClear(feet, yaw0, pawn);
                foreach (var kind in FinderKinds)
                {
                    if (FinderKindRuns(kind) && !runOk)
                        continue;
                    foreach (float arc in GrenadeSimLogic.BallisticPitches(feet, kind, target.Center))
                        for (float yo = -3f; yo <= 3.001f; yo += 0.5f)
                            for (float pitch = arc - DeepPitchSpan; pitch <= arc + DeepPitchSpan + 0.001f; pitch += DeepPitchStep)
                                if (pitch > -89f && pitch < 89f)
                                    job.Deep.Add(new DeepUnit(feet, kind, pitch, yaw0 + yo));
                }
                job.StartMs = setupClock.Elapsed.TotalMilliseconds;
                if (job.Deep.Count == 0)
                {
                    player.PrintToCenterAlert(Localizer.ForPlayer(player, "matchzy.pp.finder.live.out_of_reach"));
                    return;
                }
                finderJob = job;
                return;
            }
            // Fast path first: the aims straight at the target, a few degrees around each arc that
            // reaches it. When one of those lands (the usual case) the full sweep is skipped.
            bool straightRun = FinderRunPathClear(feet, yaw0, pawn);
            foreach (var kind in FinderKinds)
            {
                if (FinderKindRuns(kind) && !straightRun)
                    continue;
                foreach (float arc in GrenadeSimLogic.BallisticPitches(feet, kind, target.Center))
                    for (float yo = -2f; yo <= 2.001f; yo += 0.25f)
                        for (float pitch = arc - 3f; pitch <= arc + 3.001f; pitch += 0.25f)
                            if (pitch > -89f && pitch < 89f)
                                job.Deep.Add(new DeepUnit(feet, kind, pitch, yaw0 + yo));
            }
            job.FastCount = job.Deep.Count;
            // Then everything: straight at the target first, further and further to the sides.
            for (float off = 0f; off <= HereYawSpan + 0.001f; off += HereStep)
            {
                foreach (float side in off == 0f ? new[] { 1f } : new[] { 1f, -1f })
                {
                    float yaw = yaw0 + off * side;
                    bool canRun = FinderRunPathClear(feet, yaw, pawn);
                    foreach (var kind in FinderKinds)
                    {
                        if (FinderKindRuns(kind) && !canRun)
                            continue;
                        for (float pitch = -88f; pitch <= 45f; pitch += HereStep)
                            job.Deep.Add(new DeepUnit(feet, kind, pitch, yaw));
                    }
                }
            }
            job.StartMs = setupClock.Elapsed.TotalMilliseconds;
            finderJob = job;
            PrintToPlayerChat(player, target.Cover
                ? Localizer.ForPlayer(player, "matchzy.pp.finder.here.working.cover", job.Deep.Count)
                : Localizer.ForPlayer(player, "matchzy.pp.finder.here.working", job.Deep.Count));
        }

        // While the player is on (or near) a lineup: what to do, and whether they still stand and
        // aim right, kept on screen. Most misses are a nudged mouse or the wrong kind of throw.
        private readonly Dictionary<int, bool> finderArmedOff = new();

        // The plan was calculated for another eye height than the player has on this spot: the
        // smallest change of aim that puts the landing back where it was (inside the box).
        private void RefitFinderEyeHeight(CCSPlayerController player, CCSPlayerPawn pawn, int slot, int number, FinderResult plan, float view)
        {
            if (!finderTargets.TryGetValue(slot, out var target))
                return;
            float was = plan.EyeHeight, pitch0 = plan.Pitch, yaw0 = plan.Yaw;
            var wanted = plan.Rest;
            plan.EyeHeight = view;
            GrenadeSimLogic.TraceFn trace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                FinderGrenadeTrace(a, b, out hit, out normal, out fraction, pawn);
            float bestScore = float.MaxValue, bestPitch = pitch0, bestYaw = yaw0, bestTime = plan.FlightTime;
            Vector3 bestRest = wanted;
            bool any = false;
            for (int ip = -30; ip <= 30; ip++)
                for (int iy = -2; iy <= 2; iy++)
                {
                    plan.Pitch = Math.Clamp(pitch0 + ip * 0.05f, -89f, 89f);
                    plan.Yaw = yaw0 + iy * 0.1f;
                    var f = GrenadeSimLogic.Simulate(FinderLaunch(plan), trace, 20f, 1, null, FinderFuseOf(plan.Type));
                    if (!f.Stopped)
                        continue;
                    // Inside the box first, then nearest to the old landing, then the smallest change.
                    float score = Math.Max(0f, FinderMiss(target, f.Rest)) * 1000f + Vector3.Distance(f.Rest, wanted) + (Math.Abs(ip) + Math.Abs(iy)) * 0.01f;
                    if (score < bestScore)
                    {
                        bestScore = score; bestPitch = plan.Pitch; bestYaw = plan.Yaw; bestRest = f.Rest; bestTime = f.Time; any = true;
                    }
                }
            plan.Pitch = bestPitch; plan.Yaw = bestYaw;
            if (any)
            {
                plan.Rest = bestRest; plan.FlightTime = bestTime;
            }
            FinderLog(string.Format(CultureInfo.InvariantCulture,
                "[Finder] #{0} refit to eye height {1:0.0} (was {2:0.0}): aim ({3:0.00} {4:0.00}) -> ({5:0.00} {6:0.00}), lands ({7:0.0} {8:0.0} {9:0.0}), {10:0.0} u from the old landing, {11}",
                number, view, was, pitch0, yaw0, plan.Pitch, plan.Yaw, plan.Rest.X, plan.Rest.Y, plan.Rest.Z, Vector3.Distance(plan.Rest, wanted),
                any && FinderMiss(target, plan.Rest) <= 0f ? "in the box" : "NOT in the box"), fileOnly: true);
            if (Math.Abs(plan.Pitch - pitch0) < 0.001f && Math.Abs(plan.Yaw - yaw0) < 0.001f)
                return;
            int slotNow = slot;
            Server.NextFrame(() =>
            {
                var p = Utilities.GetPlayerFromSlot(slotNow);
                var pw = p?.PlayerPawn?.Value;
                if (p == null || !IsPlayerValid(p) || pw == null || !pw.IsValid || !isPractice
                    || !finderArmed.TryGetValue(slotNow, out var now) || !ReferenceEquals(now.Plan, plan))
                    return;
                var at = FinderStandFeet(plan.Feet, pw);
                pw.Teleport(new Vector(at.X, at.Y, at.Z + 0.1f), new QAngle(plan.Pitch, plan.Yaw, 0f), new Vector(0, 0, 0));
                WatchTeleportStuck(p, at, plan.Pitch, plan.Yaw);
                FlattenBodyRotationFrames(p, plan.Yaw, 6);
                DrawFinderGuide(slotNow, plan, pw);
                PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.pp.finder.refit.adjusted", number));
            });
        }

        // Aim lock (the author's call 2026-11-17): on the spot of a loaded lineup, with the pin
        // pulled and the aim within a degree of the lineup's, the view is put back on the exact
        // aim. A nudge of the mouse after the teleport (0.66 degrees, seen in the log) sent a
        // jumpthrow 323 u off. Further off than a degree is the player's own choice: left alone.
        private const float FinderAimLockDegrees = 1.0f;
        private readonly Dictionary<int, int> finderAimLockNoted = new();

        private void TickFinderAimLock()
        {
            if (!practiceAimLock.Value || finderArmed.Count == 0)
                return;
            foreach (var (slot, armed) in finderArmed)
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !IsPlayerValid(player) || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null
                    || (player.Buttons & (PlayerButtons.Attack | PlayerButtons.Attack2)) == 0
                    || (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0)
                    continue;
                var plan = armed.Plan;
                float dx = pawn.AbsOrigin.X - plan.Feet.X, dy = pawn.AbsOrigin.Y - plan.Feet.Y;
                if (dx * dx + dy * dy > 3f * 3f)
                    continue;
                float dPitch = pawn.EyeAngles.X - plan.Pitch, dYaw = pawn.EyeAngles.Y - plan.Yaw;
                while (dYaw > 180f) dYaw -= 360f;
                while (dYaw < -180f) dYaw += 360f;
                float off = MathF.Sqrt(dPitch * dPitch + dYaw * dYaw);
                if (off < 0.02f || off > FinderAimLockDegrees)
                    continue;
                string held = pawn.WeaponServices?.ActiveWeapon?.Value?.DesignerName ?? "";
                if (!(held.Contains("grenade") || held.Contains("flashbang") || held.Contains("molotov") || held.Contains("decoy")))
                    continue;
                // Only Teleport with an angle snaps the local view (hard rule 10); the place and
                // the speed stay as they are. The body is flattened back to yaw only.
                pawn.Teleport(null, new QAngle(plan.Pitch, plan.Yaw, 0f), null);
                FlattenBodyRotationFrames(player, plan.Yaw, 2);
                if (!finderAimLockNoted.TryGetValue(slot, out int last) || Server.TickCount - last > 64 * 5)
                    FinderLog($"[Finder] aim lock: view put back on the lineup's aim (was {off:0.00} degrees off)", fileOnly: true);
                finderAimLockNoted[slot] = Server.TickCount;
            }
        }

        // Stuck after a teleport, as the game itself shows it: on no ground and not moving, for
        // an eighth of a second. (A fall moves, a stand has ground; a player inside a kerb or a
        // wall has neither.) The traces that should rule such spots out were fooled twice
        // (find6 / find7 on banana), so this goes by what happened, not by what was predicted:
        // the player is tried a little higher, then at places around the spot, until they stand.
        private sealed class StuckWatch
        {
            public Vector3 Feet;
            public float Pitch, Yaw;
            public int Until, Still, Attempt;
        }

        private readonly Dictionary<int, StuckWatch> stuckWatches = new();

        private static readonly (float X, float Y, float Z)[] StuckTries = BuildStuckTries();

        private static (float X, float Y, float Z)[] BuildStuckTries()
        {
            var tries = new List<(float, float, float)> { (0, 0, 12), (0, 0, 24) };
            foreach (float r in new[] { 12f, 20f, 32f })
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0;
                    tries.Add(((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 6f));
                }
            return tries.ToArray();
        }

        private void WatchTeleportStuck(CCSPlayerController player, Vector3 feet, float pitch, float yaw)
        {
            stuckWatches[player.Slot] = new StuckWatch { Feet = feet, Pitch = pitch, Yaw = yaw, Until = Server.TickCount + 64 * 2 };
        }

        private void TickStuckWatch()
        {
            if (stuckWatches.Count == 0)
                return;
            int tick = Server.TickCount;
            foreach (var (slot, w) in stuckWatches.ToList())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !IsPlayerValid(player) || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null
                    || !player.PawnIsAlive || tick > w.Until)
                {
                    stuckWatches.Remove(slot);
                    continue;
                }
                var vel = pawn.AbsVelocity;
                bool hangs = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0
                    && vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z < 1f;
                if (!hangs)
                {
                    // Standing (done) or moving (falling onto the place, or walking off).
                    if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0)
                    {
                        if (w.Attempt > 0)
                        {
                            var now = new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
                            float moved = Vector3.Distance(now, w.Feet);
                            FinderLog($"[Finder] stuck fix: stands after try {w.Attempt}, {moved:0.0} u from the spot ({w.Feet.X:0.0} {w.Feet.Y:0.0} {w.Feet.Z:0.0})", fileOnly: true);
                            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.stuck.moved", moved));
                            // The loaded lineup counts this place as its spot from now on.
                            if (finderArmed.TryGetValue(slot, out var armed))
                                armed.Plan.Feet = now;
                        }
                        stuckWatches.Remove(slot);
                    }
                    w.Still = 0;
                    continue;
                }
                if (++w.Still < 8)
                    continue;
                w.Still = 0;
                if (w.Attempt >= StuckTries.Length)
                {
                    FinderLog($"[Finder] stuck fix: no place found around ({w.Feet.X:0.0} {w.Feet.Y:0.0} {w.Feet.Z:0.0})", fileOnly: true);
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.stuck.no_place"));
                    stuckWatches.Remove(slot);
                    continue;
                }
                var t = StuckTries[w.Attempt++];
                if (w.Attempt == 1)
                    FinderLog($"[Finder] stuck fix: the player hangs at ({w.Feet.X:0.0} {w.Feet.Y:0.0} {w.Feet.Z:0.0}), on no ground and not moving", fileOnly: true);
                w.Until = tick + 64 * 2;
                pawn.Teleport(new Vector(w.Feet.X + t.X, w.Feet.Y + t.Y, w.Feet.Z + t.Z), new QAngle(w.Pitch, w.Yaw, 0f), new Vector(0, 0, 0));
                FlattenBodyRotationFrames(player, w.Yaw, 2);
            }
        }

        private void TickFinderArmed()
        {
            if (Server.TickCount % 16 != 0)
                return;
            foreach (var (slot, armed) in finderArmed)
            {
                if (finderJob?.Slot == slot || verifyJob?.Slot == slot)
                    continue;
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !IsPlayerValid(player) || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                    continue;
                var plan = armed.Plan;
                var pos = new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
                float away = GrenadeSimLogic.HorizontalDistance(pos, plan.Feet);
                if (away > 260f)
                    continue;
                string what = plan.Kind switch
                {
                    GrenadeSimLogic.ThrowKind.RunJump => Localizer.ForPlayer(player, "matchzy.pp.finder.armed.what.runjump"),
                    GrenadeSimLogic.ThrowKind.Run => Localizer.ForPlayer(player, "matchzy.pp.finder.armed.what.run"),
                    GrenadeSimLogic.ThrowKind.Jump => Localizer.ForPlayer(player, "matchzy.pp.finder.armed.what.jump"),
                    _ => Localizer.ForPlayer(player, "matchzy.pp.finder.armed.what.stand"),
                };
                if (plan.What != "")
                    what = plan.What.ToUpperInvariant();
                float dPitch = pawn.EyeAngles.X - plan.Pitch;
                float dYaw = pawn.EyeAngles.Y - plan.Yaw;
                while (dYaw > 180f) dYaw -= 360f;
                while (dYaw < -180f) dYaw += 360f;
                float aimOff = MathF.Sqrt(dPitch * dPitch + dYaw * dYaw);
                bool running = FinderKindRuns(plan.Kind) && away > 3f
                    && Vector3.Dot(pos - plan.Feet, GrenadeSimLogic.Heading(plan.Yaw)) > 0f;
                string state;
                if (running)
                    state = aimOff > 0.3f ? Localizer.ForPlayer(player, "matchzy.pp.finder.armed.state.aim_moved", aimOff) : Localizer.ForPlayer(player, "matchzy.pp.finder.armed.state.run_straight");
                else if (away > 3f)
                    state = Localizer.ForPlayer(player, "matchzy.pp.finder.armed.state.not_on_spot", away);
                else if (aimOff > 0.15f)
                    state = Localizer.ForPlayer(player, "matchzy.pp.finder.armed.state.aim_moved_e", aimOff);
                else
                    state = Localizer.ForPlayer(player, "matchzy.pp.finder.armed.state.ready");
                // Standing throws: the view height on the spot itself (lower on slopes) is only
                // known once the player stands there. Refit the aim to it once.
                if (!plan.Library && !plan.EyeChecked && plan.Kind == GrenadeSimLogic.ThrowKind.Stand)
                {
                    var pv = pawn.AbsVelocity;
                    bool settled = away <= 0.5f && (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0
                        && (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) == 0 && pv.X * pv.X + pv.Y * pv.Y + pv.Z * pv.Z < 1f;
                    plan.EyeTicks = settled ? plan.EyeTicks + 1 : 0;
                    if (plan.EyeTicks >= 2)
                    {
                        plan.EyeChecked = true;
                        float view = pawn.ViewOffset.Z;
                        if (view > 58f && view < 66f && Math.Abs(view - plan.EyeHeight) > 0.25f)
                            RefitFinderEyeHeight(player, pawn, slot, armed.Number, plan, view);
                    }
                }

                // While the player aims at a lineup mark that mark is named in the box instead.
                if (nadeHoverTick.TryGetValue(slot, out int hovered) && Server.TickCount - hovered < 20)
                    continue;
                // One box (the alert one): the author did not want a second box under it.
                player.PrintToCenterAlert($"{(plan.Library ? plan.Name : "#" + armed.Number)} {what}\n{state}");
                // E near a library lineup's spot puts the player back on it (checked 4 times a
                // second here; finder results have their own E pick).
                if (plan.Library && !running && (away > 3f || aimOff > 0.15f) && away < 160f && (player.Buttons & PlayerButtons.Use) != 0)
                    ReturnToArmedLineup(player, plan);
            }
        }

        // Back onto a library lineup's spot with its aim (finder results use GoToFinderResult).
        private void ReturnToArmedLineup(CCSPlayerController player, FinderResult plan)
        {
            int slot = player.Slot;
            Server.NextFrame(() =>
            {
                var p = Utilities.GetPlayerFromSlot(slot);
                var pawn = p?.PlayerPawn?.Value;
                if (p == null || !IsPlayerValid(p) || pawn == null || !pawn.IsValid || p.TeamNum < 2 || !isPractice)
                    return;
                var at = FinderStandFeet(plan.Feet, pawn);
                pawn.Teleport(new Vector(at.X, at.Y, at.Z + 0.1f), new QAngle(plan.Pitch, plan.Yaw, 0f), new Vector(0, 0, 0));
                WatchTeleportStuck(p, at, plan.Pitch, plan.Yaw);
                FlattenBodyRotationFrames(p, plan.Yaw, 6);
                ClearGrenadeStash(pawn, "return");
                FinderTakeOutGrenade(p, plan.Type);
            });
        }

        // The game stashes the thrower's position for a jumpthrow when they jump. Seen live: teleported before landing, the stash stays set and the next standing
        // throws leave from that old point (measured: 34 u behind the player, six throws in a row).
        private void ClearGrenadeStash(CCSPlayerPawn pawn, string when)
        {
            if (pawn.GrenadeParametersStashed)
            {
                var sp = pawn.StashedGrenadeThrowPosition;
                FinderLog(string.Format(CultureInfo.InvariantCulture,
                    "[Finder] stale jumpthrow stash cleared at {0}: time {1:0.000} now {2:0.000} pos ({3:0.0} {4:0.0} {5:0.0}) ground {6}",
                    when, pawn.GrenadeParameterStashTime, Server.CurrentTime, sp.X, sp.Y, sp.Z,
                    (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0), fileOnly: true);
            }
            pawn.GrenadeParametersStashed = false;
        }

        // Arguments of a finder command: from the console command, or from a chat line with
        // arguments (".find area"), which reaches the handlers without a CommandInfo.
        private string[]? finderChatArgs;

        private List<string> FinderWords(CommandInfo? command)
        {
            var words = new List<string>();
            if (finderChatArgs != null)
            {
                foreach (var w in finderChatArgs)
                    words.Add(w.Trim().ToLowerInvariant());
                return words;
            }
            for (int i = 1; i < (command?.ArgCount ?? 0); i++)
                words.Add(command!.ArgByIndex(i).Trim().ToLowerInvariant());
            return words;
        }

        // Chat dispatch for the finder commands that take arguments.
        private void HandleFinderChatCommand(CCSPlayerController player, string cmdWord, string args)
        {
            finderChatArgs = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            try
            {
                switch (cmdWord)
                {
                    case ".find": OnFinderFindCommand(player, null); break;
                    case ".findgo": OnFinderGoCommand(player, null); break;
                    case ".results": OnFinderResultsCommand(player, null); break;
                    case ".draw": OnFinderDrawCommand(player, null); break;
                    case ".throwsync": OnThrowSyncCommand(player, null); break;
                    case ".nadespeed": OnNadeSpeedCommand(player, null); break;
                    case ".nextnade": OnNextNadeCommand(player, null); break;
                    case ".prevnade": OnPrevNadeCommand(player, null); break;
                    case ".savelast": case ".save": case ".sl": OnSaveLastCommand(player, null); break;
                    case ".train": OnTrainCommand(player, null); break;
                }
            }
            finally
            {
                finderChatArgs = null;
            }
        }

        [ConsoleCommand("css_findnext", "Practice: next .find result")]
        public void OnFinderNextCommand(CCSPlayerController? player, CommandInfo? command) => StepFinderResult(player, 1);

        [ConsoleCommand("css_findprev", "Practice: previous .find result")]
        public void OnFinderPrevCommand(CCSPlayerController? player, CommandInfo? command) => StepFinderResult(player, -1);

        private void StepFinderResult(CCSPlayerController? player, int step)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (!finderResults.TryGetValue(player.Slot, out var results) || results.Count == 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.results.none"));
                return;
            }
            int current = finderArmed.TryGetValue(player.Slot, out var armed) ? armed.Number : 0;
            int next = ((current - 1 + step) % results.Count + results.Count) % results.Count + 1;
            GoToFinderResult(player, next);
        }

        // .results: the list again; .results <n> = go to one (same as .findgo <n>).
        [ConsoleCommand("css_results", "Practice: list .find results, or go to one: .results [number]")]
        public void OnFinderResultsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (!finderResults.TryGetValue(player.Slot, out var results) || results.Count == 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.results.none"));
                return;
            }
            if (FinderWords(command).Count > 0)
            {
                OnFinderGoCommand(player, command);
                return;
            }
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.results.header", results.Count));
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.results.line",
                    i + 1, FinderKindName(player, r.Kind), r.Robust >= 0f ? FinderRobustText(player, r.Robust) : Localizer.ForPlayer(player, "matchzy.pp.finder.robust.not_rated"),
                    r.NoHit ? Localizer.ForPlayer(player, "matchzy.pp.finder.land.stops_outside", r.Miss) : FinderLandText(player, r)));
            }
        }

        [ConsoleCommand("css_findgo", "Practice: go to a .find result: .findgo <number>")]
        public void OnFinderGoCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (!finderResults.TryGetValue(player.Slot, out var results) || results.Count == 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.results.none"));
                return;
            }
            int n = 1;
            if (FinderWords(command).FirstOrDefault() is string numberArg && !int.TryParse(numberArg, out n))
                n = 1;
            if (n < 1 || n > results.Count)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.go.pick", results.Count));
                return;
            }
            GoToFinderResult(player, n);
        }

        // Teleports the player onto result n (1-based) with its exact view angles and a smoke in
        // hand, and says how to throw it.
        private void GoToFinderResult(CCSPlayerController player, int n)
        {
            if (!finderResults.TryGetValue(player.Slot, out var results) || n < 1 || n > results.Count)
                return;
            var res = results[n - 1];
            int slot = player.Slot;
            Server.NextFrame(() =>
            {
                var p = Utilities.GetPlayerFromSlot(slot);
                var pawn = p?.PlayerPawn?.Value;
                if (p == null || !IsPlayerValid(p) || pawn == null || !pawn.IsValid || p.TeamNum < 2)
                    return;
                // Only Teleport with an angle snaps the local view (hard rule 10).
                var at = FinderStandFeet(res.Feet, pawn);
                pawn.Teleport(new Vector(at.X, at.Y, at.Z + 0.1f), new QAngle(res.Pitch, res.Yaw, 0f), new Vector(0, 0, 0));
                WatchTeleportStuck(p, at, res.Pitch, res.Yaw);
                // The teleport angle also tilts the body by the throw pitch (lying model, camera at
                // the floor): keep the view, flatten the model back to yaw only (as .last / .back).
                FlattenBodyRotationFrames(p, res.Yaw, 6);
                ClearGrenadeStash(pawn, "go");
                finderArmed[slot] = (res, n);
                // Demo: the server throws the same smoke from this exact spot and aim a moment
                // later (a real one, it pops), so the player sees where this lineup lands and can
                // tell a wrong spot from a throw of their own that differs.
                var demo = res;
                bool decoy = res.Type == "decoy";
                finderThrewSinceGo.Remove(slot);
                // One demo per lineup: pressing E again to get back on the spot is not a request
                // for another smoke (several on one arc collide). .findgo to another number, or a
                // new search, shows a demo again.
                bool sameAgain = finderDemoShown.TryGetValue(slot, out var shown) && ReferenceEquals(shown, demo);
                finderDemoShown[slot] = demo;
                int generation = finderDemoGeneration[slot] = (finderDemoGeneration.TryGetValue(slot, out int g) ? g : 0) + 1;
                // Off by default (the author's call for the release: no preview throws at all, the
                // lineup is already confirmed by the hidden test throws). ".find demo" turns the
                // visible demo throw on for a search, for looking into a lineup that misbehaves.
                if (!sameAgain && finderDemoThrow)
                AddTimer(1.0f, () =>
                {
                    var dp = Utilities.GetPlayerFromSlot(slot);
                    // Not when the player already threw (two grenades on the same arc collide) or
                    // moved on to another number.
                    if (dp == null || !IsPlayerValid(dp) || !isPractice || finderThrewSinceGo.Contains(slot)
                        || !finderDemoGeneration.TryGetValue(slot, out int current) || current != generation
                        || !finderArmed.TryGetValue(slot, out var now) || !ReferenceEquals(now.Plan, demo))
                        return;
                    uint? h = SpawnVerifyProbe(dp, demo, decoy, FinderDetonates(demo.Type) ? demo.Type : null);
                    if (h.HasValue)
                        TrackFinderDemo(slot, n, demo, h.Value);
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                // The searched grenade itself: slot4 picked whatever grenade comes first (HE, decoy).
                FinderTakeOutGrenade(p, res.Type);
                string how = res.Kind switch
                {
                    GrenadeSimLogic.ThrowKind.RunJump => Localizer.ForPlayer(p, "matchzy.pp.finder.go.how.runjump"),
                    GrenadeSimLogic.ThrowKind.Run => Localizer.ForPlayer(p, "matchzy.pp.finder.go.how.run"),
                    GrenadeSimLogic.ThrowKind.Jump => Localizer.ForPlayer(p, "matchzy.pp.finder.go.how.jump"),
                    _ => Localizer.ForPlayer(p, "matchzy.pp.finder.go.how.stand"),
                };
                // Running jumpthrow: a line across the path where the jump has to happen.
                DrawFinderGuide(slot, res, pawn);
                bool touchy = res.Robust >= 0f && res.Robust < 0.5f;
                if (touchy)
                    PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.pp.finder.go.touchy", n));
                // The console line stays in the invariant culture (a decimal comma would break setpos).
                string setpos = string.Format(CultureInfo.InvariantCulture,
                    "setpos {0:0.000000} {1:0.000000} {2:0.000000};setang {3:0.000000} {4:0.000000} 0.000000",
                    res.Feet.X, res.Feet.Y, res.Feet.Z + res.EyeHeight, res.Pitch, res.Yaw);
                PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.pp.finder.go.info",
                    n, FinderTypeWord(p, res.Type), how, res.Error, res.FlightTime, setpos));
            });
        }

        // E while results are shown: the result whose marker is closest to the crosshair (within a
        // few degrees) is picked.
        private void TickFinderPick()
        {
            foreach (var (slot, results) in finderResults)
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !player.IsValid || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                    continue;
                bool use = (player.Buttons & PlayerButtons.Use) != 0;
                bool was = finderUseDown.Contains(slot);
                if (use) finderUseDown.Add(slot); else finderUseDown.Remove(slot);
                if (!use || was || LookMenuOpen(slot))
                    continue;
                // E on a saved lineup's mark (.shownades) belongs to that lineup. Without this the
                // finder took every E within 160 u of its own spot and put the player back there,
                // over the lineup they had just picked.
                if (grenadeLibraryActive && _nadeViewers.Contains(player.SteamID)
                    && (AimedLandingSpot(player) != null || AimedGroupIndex(player) >= 0))
                    continue;
                var eye = new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
                var fwd = GrenadeSimLogic.Forward(pawn.EyeAngles.X, pawn.EyeAngles.Y);
                int best = -1;
                // One spot only (a search from one position): E is "take me back there". It works
                // from close by whatever the aim, and from further off with the marker roughly
                // in view; the aim last chosen with .findgo is kept.
                bool oneSpot = results.All(r => Vector3.Distance(r.Feet, results[0].Feet) < 4f);
                if (oneSpot && GrenadeSimLogic.HorizontalDistance(results[0].Feet, eye) < 160f)
                {
                    int current = finderArmed.TryGetValue(slot, out var armedNow) ? armedNow.Number : 1;
                    GoToFinderResult(player, Math.Clamp(current, 1, results.Count));
                    continue;
                }
                float bestCos = MathF.Cos((oneSpot ? 25f : 6f) * MathF.PI / 180f);
                for (int i = 0; i < results.Count; i++)
                {
                    var f = results[i].Feet;
                    // Aim anywhere between the box and the number above it.
                    foreach (float h in new[] { 2f, 18f, FinderLabelHeight })
                    {
                        var to = new Vector3(f.X, f.Y, f.Z + h) - eye;
                        float len = to.Length();
                        if (len < 1f) continue;
                        float cos = Vector3.Dot(to / len, fwd);
                        if (cos > bestCos)
                        {
                            bestCos = cos;
                            best = i;
                        }
                    }
                }
                if (best >= 0 && oneSpot && finderArmed.TryGetValue(slot, out var keep))
                    best = Math.Clamp(keep.Number, 1, results.Count) - 1;
                if (best >= 0)
                    GoToFinderResult(player, best + 1);
            }
        }

        private readonly HashSet<int> finderUseDown = new();
        private const float FinderLabelHeight = 36f;

        // OnTick: advance the running search within a small time budget per tick.
        private void OnFinderTick()
        {
            try
            {
                FlushFinderLog();
                if (isPractice)
                {
                    TickFinderAimLock();
                    TickStuckWatch();
                    TickFinderAimMark();
                    TickSpotRings();
                    TickThrowWatch();
                    TickLookMenus();
                    TickChatAsks();
                    TickLineupMarks();
                    TickNadeTrainings();
                }
                if (drawSessions.Count > 0 || finderTargets.Count > 0)
                    TickDrawSessions();
                if (finderResults.Count > 0)
                    TickFinderPick();
                if (verifyJob != null)
                    TickFinderVerify();
                if (finderLive.Count > 0)
                    TickFinderLive();
                // The grenade each library viewer holds (filters their marks), whether or not
                // any landing mark exists: without marks nothing looked it up and every type showed.
                if (grenadeLibraryActive && Server.TickCount % 8 == 0)
                    RefreshLandingHeld();
                if (landingSpots.Count > 0)
                    TickLandingPick();
                if (finderArmed.Count > 0)
                    TickFinderArmed();
            }
            catch (Exception e)
            {
                // A tick listener must not throw: drop the running work, keep the server going.
                Log($"[Finder] tick failed: {e}");
                CancelFinderVerify();
                finderJob = null;
            }
            if (finderTracks.Count > 0)
                TickFinderTracks();
            var job = finderJob;
            if (job == null)
                return;
            var player = Utilities.GetPlayerFromSlot(job.Slot);
            if (!isPractice || player == null || !player.IsValid || !finderTargets.TryGetValue(job.Slot, out var target))
            {
                finderJob = null;
                return;
            }
            var budget = Stopwatch.StartNew();
            try
            {
                // Units are ordered nearest spot first: once plenty of hits are in, the rest are
                // further away and would rank lower anyway.
                if (job.Found.Count >= (job.Here ? 200 : 40))
                    job.DeepNext = job.Deep.Count;
                while (job.DeepNext < job.Deep.Count && budget.Elapsed.TotalMilliseconds < FinderTickBudgetMs)
                {
                    // The straight aims are through and one of them lands: skip the full sweep.
                    if (job.Here && !job.FastChecked && job.FastCount > 0 && job.DeepNext >= job.FastCount)
                    {
                        job.FastChecked = true;
                        if (job.Found.Count > 0)
                        {
                            job.DeepNext = job.Deep.Count;
                            break;
                        }
                    }
                    double before = budget.Elapsed.TotalMilliseconds;
                    long tracesBefore = job.Traces;
                    var unit = job.Deep[job.DeepNext];
                    SolveDeepUnit(job, target, unit);
                    job.DeepNext++;
                    double took = budget.Elapsed.TotalMilliseconds - before;
                    if (took > 2.0)
                        job.SlowUnits++;
                    if (took > job.SlowUnitMs)
                    {
                        job.SlowUnitMs = took;
                        job.SlowUnit = string.Format(CultureInfo.InvariantCulture, "{0} aim ({1:0.0} {2:0.0}) {3} traces", unit.Kind, unit.Pitch, unit.Yaw, job.Traces - tracesBefore);
                    }
                }
                // From one spot and nothing lands yet: the calculation matches the game to about a
                // unit, so look closer around the nearest misses (0.1 degree steps) before giving up.
                // (Also for the area search: its coarse sweeps for far targets need the same closer look.)
                if ((job.Here || FinderAreaBounceSweep) && !job.Quick && !job.Refined && job.Deep.Count > 0 && job.DeepNext >= job.Deep.Count && job.Found.Count == 0 && job.Near.Count > 0)
                {
                    job.Refined = true;
                    var seeds = new List<FinderResult>();
                    foreach (var r in job.Near.OrderBy(r => FinderMiss(target, r.Rest)))
                    {
                        if (seeds.Any(c => c.Kind == r.Kind && GrenadeSimLogic.HorizontalDistance(c.Feet, r.Feet) < 8f
                                && Math.Abs(c.Pitch - r.Pitch) < 2f && Math.Abs(c.Yaw - r.Yaw) < 2f))
                            continue;
                        seeds.Add(r);
                        if (seeds.Count == (job.Here ? 12 : 20))
                            break;
                    }
                    foreach (var sd in seeds)
                        for (int iy = -10; iy <= 10; iy++)
                            for (int ip = -10; ip <= 10; ip++)
                                job.Deep.Add(new DeepUnit(sd.Feet, sd.Kind, Math.Clamp(sd.Pitch + ip * 0.1f, -89f, 89f), sd.Yaw + iy * 0.1f));
                }
                if (job.DeepNext < job.Deep.Count)
                {
                    int dp = job.DeepNext * 100 / job.Deep.Count;
                    if (dp / 10 != job.LastProgress / 10)
                    {
                        job.LastProgress = dp;
                        player.PrintToCenterAlert(job.Here ? Localizer.ForPlayer(player, "matchzy.pp.finder.progress.here", dp) : Localizer.ForPlayer(player, "matchzy.pp.finder.progress.around", dp));
                    }
                    return;
                }
                // Hits or near misses in the player's own area: that is what they asked for. Those
                // get their real test throws first; the wide pass only runs when none of them lands.
                if (!job.Here && job.Deep.Count > 0 && job.Next == 0 && job.Wide.Count == 0 && (job.Found.Count > 0 || job.Near.Count > 0))
                {
                    job.AreaStage = true;
                    job.Next = job.Candidates.Count;
                }
                while ((job.Wide.Count > 0 || job.Next < job.Candidates.Count) && budget.Elapsed.TotalMilliseconds < FinderTickBudgetMs)
                {
                    if (job.Wide.Count > 0)
                    {
                        SolveWideUnit(job, target, job.Wide.Pop());
                        continue;
                    }
                    QueueFinderCandidate(job, target, job.Candidates[job.Next]);
                    job.Next++;
                }
            }
            catch (Exception e)
            {
                FinderLog($"[Finder] search failed: {e.GetType().Name}: {e.Message}");
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.search.failed"));
                finderJob = null;
                return;
            }
            finally
            {
                job.MaxTickMs = Math.Max(job.MaxTickMs, budget.Elapsed.TotalMilliseconds);
            }

            int pct = job.Candidates.Count == 0 ? 100 : job.Next * 100 / job.Candidates.Count;
            if (pct / 10 != job.LastProgress / 10 && pct < 100)
            {
                job.LastProgress = pct;
                player.PrintToCenterAlert(job.Radius >= 100000f ? Localizer.ForPlayer(player, "matchzy.pp.finder.progress.map", pct) : Localizer.ForPlayer(player, "matchzy.pp.finder.progress.radius", pct, job.Radius));
            }
            if (job.Next < job.Candidates.Count || job.Wide.Count > 0)
                return;

            // Nothing yet: widen to the next radius with the spots not tried so far.
            if (job.Found.Count == 0 && !job.Here && !job.AreaStage && TryWidenFinderJob(player, job))
                return;

            finderJob = null;
            FinishFinderJob(player, job, target);
        }

        // One aim from one spot: a single simulated throw.
        private void SolveDeepUnit(FinderJob job, FinderTarget target, DeepUnit u)
        {
            GrenadeSimLogic.TraceFn trace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
            {
                job.Traces++;
                return FinderGrenadeTrace(a, b, out hit, out normal, out fraction, job.Ignore != null && job.Ignore.IsValid ? job.Ignore : null);
            };
            var flight = GrenadeSimLogic.Simulate(GrenadeSimLogic.LaunchFrom(u.Feet, u.Kind, u.Pitch, u.Yaw), trace, 12f, 2, null, FinderFuseOf(finderCurrentType));
            if (!flight.Stopped)
                return;
            if (!FinderHits(target, flight.Rest))
            {
                RecordNear(job, target, u.Feet, u.Kind, u.Pitch, u.Yaw, flight);
                return;
            }
            job.Found.Add(new FinderResult
            {
                Feet = u.Feet, Pitch = u.Pitch, Yaw = u.Yaw, Kind = u.Kind,
                Rest = flight.Rest, Error = GrenadeSimLogic.HorizontalDistance(flight.Rest, target.Center), FlightTime = flight.Time, HasSim = true,
            });
        }

        // A spot's throws to try: one unit per throw kind and arc (flat, lobbed).
        private void QueueFinderCandidate(FinderJob job, FinderTarget target, Vector3 rawFeet)
        {
            if (SnapCandidate(rawFeet) is not Vector3 feet)
                return;
            bool canRun = FinderRunPathClear(feet, GrenadeSimLogic.YawTo(feet, target.Center), job.Ignore != null && job.Ignore.IsValid ? job.Ignore : null);
            foreach (var kind in FinderKinds)
            {
                if (FinderKindRuns(kind) && !canRun)
                    continue;
                foreach (float startPitch in GrenadeSimLogic.BallisticPitches(feet, kind, target.Center))
                    job.Wide.Push(new WideUnit(feet, kind, startPitch, target.Center, 0));
            }
        }

        // One simulated throw of a unit. A miss queues the corrected throw as the next unit: the
        // aim point starts at the target and moves by the landing error each step, so a grenade
        // that rolls 40 u past is aimed 40 u short next time.
        private void SolveWideUnit(FinderJob job, FinderTarget target, WideUnit u)
        {
            GrenadeSimLogic.TraceFn trace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
            {
                job.Traces++;
                return FinderGrenadeTrace(a, b, out hit, out normal, out fraction, job.Ignore != null && job.Ignore.IsValid ? job.Ignore : null);
            };
            float yaw = GrenadeSimLogic.YawTo(u.Feet, u.Aim);
            var flight = GrenadeSimLogic.Simulate(GrenadeSimLogic.LaunchFrom(u.Feet, u.Kind, u.Pitch, yaw), trace, 12f, 2, null, FinderFuseOf(finderCurrentType));
            if (!flight.Stopped)
                return;
            if (FinderHits(target, flight.Rest))
            {
                job.Found.Add(new FinderResult
                {
                    Feet = u.Feet, Pitch = u.Pitch, Yaw = yaw, Kind = u.Kind,
                    Rest = flight.Rest, Error = GrenadeSimLogic.HorizontalDistance(flight.Rest, target.Center), FlightTime = flight.Time, HasSim = true,
                });
                return;
            }
            RecordNear(job, target, u.Feet, u.Kind, u.Pitch, yaw, flight);
            if (u.Step + 1 >= FinderRefineSteps)
                return;
            // Ended on another level (fell off a ledge, landed on a roof): correct with the first
            // impact instead, so the next try at least hits the target spot.
            bool sameLevel = Math.Abs(flight.Rest.Z - target.Center.Z) <= FinderLevelTolerance;
            var miss = sameLevel || !flight.HasImpact ? flight.Rest : flight.FirstImpact;
            var aim = u.Aim + new Vector3(target.Center.X - miss.X, target.Center.Y - miss.Y, 0f);
            // Same arc family as before: the new pitch closest to the current one.
            var next = GrenadeSimLogic.BallisticPitches(u.Feet, u.Kind, aim);
            if (next.Count == 0)
                return;
            job.Wide.Push(new WideUnit(u.Feet, u.Kind, next.OrderBy(p => Math.Abs(p - u.Pitch)).First(), aim, u.Step + 1));
        }

        private bool TryWidenFinderJob(CCSPlayerController player, FinderJob job)
        {
            foreach (float radius in FinderRadii)
            {
                if (radius <= job.Radius)
                    continue;
                List<Vector3> more;
                var widenClock = Stopwatch.StartNew();
                try
                {
                    more = FinderCandidates(job.PlayerFeet, job.Target, radius, job.Checked, radius >= 100000f ? 3000 : FinderMaxCandidates);
                }
                catch
                {
                    return false;
                }
                job.Radius = radius;
                job.WidenMs = Math.Max(job.WidenMs, widenClock.Elapsed.TotalMilliseconds);
                if (more.Count == 0)
                    continue;
                foreach (var c in more)
                    job.Checked.Add(FinderKey(c));
                job.Candidates = more;
                job.Next = 0;
                job.LastProgress = -1;
                finderJob = job;
                PrintToPlayerChat(player, radius >= 100000f
                    ? Localizer.ForPlayer(player, "matchzy.pp.finder.widen.map", more.Count)
                    : Localizer.ForPlayer(player, "matchzy.pp.finder.widen.radius", radius, more.Count));
                return true;
            }
            return false;
        }

        // Coarse z: the raw nav point and the floor-snapped one must give the same key.
        private static (int, int, int) FinderKey(Vector3 p) => ((int)MathF.Round(p.X / 8f), (int)MathF.Round(p.Y / 8f), (int)MathF.Floor(p.Z / 128f));

        private const float NearMissRadius = 200f;

        private static void RecordNear(FinderJob job, FinderTarget target, Vector3 feet, GrenadeSimLogic.ThrowKind kind, float pitch, float yaw, GrenadeSimLogic.Flight flight)
        {
            float err = GrenadeSimLogic.HorizontalDistance(flight.Rest, target.Center);
            if (err > NearMissRadius || Math.Abs(flight.Rest.Z - target.Center.Z) > 96f || job.Near.Count > 4000)
                return;
            job.Near.Add(new FinderResult { Feet = feet, Pitch = pitch, Yaw = yaw, Kind = kind, Rest = flight.Rest, Error = err, FlightTime = flight.Time, HasSim = true });
        }

        // Simulation done: hand the best candidates to the engine test (GrenadeFinderVerify.cs).
        private void FinishFinderJob(CCSPlayerController player, FinderJob job, FinderTarget target)
        {
            CapturePlayerLayers(job.Ignore);
            FinderLog($"[Finder] {job.Checked.Count} spots ({job.Deep.Count} fine aims around the player), {job.Found.Count} hits, {job.Near.Count} near, {job.Traces} traces, {job.Clock.Elapsed.TotalSeconds:0.0} s, start {job.StartMs:0.0} ms, slowest tick {job.MaxTickMs:0.0} ms, slowest widen {job.WidenMs:0.0} ms, slowest throw {job.SlowUnitMs:0.0} ms ({job.SlowUnit}), {job.SlowUnits} throws over 2 ms, gc {GC.CollectionCount(0) - job.Gc0}/{GC.CollectionCount(2) - job.Gc2}");
            // Simulated hits first, then near misses; nearest to the player first within each.
            var ordered = job.Found
                .OrderBy(r => job.Here ? 0f : MathF.Floor(GrenadeSimLogic.HorizontalDistance(r.Feet, job.PlayerFeet) / 64f)).ThenBy(r => r.Error)
                // Near misses only when the calculation found no hit at all: it matches the game
                // on 96-99 % of throws, so test throws of calculated misses are wasted seconds
                // (measured: 43 of 49, and 62 of 64, test throws were such misses).
                .Concat(job.Found.Count > 0 || !finderVerify ? Enumerable.Empty<FinderResult>()
                    // ...and only the close ones: a calculated miss of 177 u does not land in
                    // the box for real either (54 such test throws took 23 s for nothing).
                    : job.Near.Where(r => r.Error <= 24f).OrderBy(r => r.Error).ThenBy(r => GrenadeSimLogic.HorizontalDistance(r.Feet, job.PlayerFeet)));
            // No test throws by default (the author's call): the calculation matches the game on
            // 96-99 % of throws, and the wait for the test grenades to land was most of the time a
            // search took. The calculated hits are shown at once; ".find check" throws them for
            // real first (hidden), as before.
            if (!finderVerify && job.Found.Count > 0)
            {
                GrenadeSimLogic.TraceFn exactTrace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                    FinderGrenadeTrace(a, b, out hit, out normal, out fraction, job.Ignore != null && job.Ignore.IsValid ? job.Ignore : null);
                var exact = new List<FinderResult>();
                int unfit = 0;
                foreach (var c in ordered)
                {
                    if (!job.Found.Contains(c))
                        continue;
                    // The spot was found with a thin ray. On a slope or next to a kerb a player's
                    // box rests higher than that (seen on inferno 2026-11-17: put at the ray's
                    // height the player hung in the ground, "ground False", and could not jump).
                    // The lineup is worked out from where the player really stands; the full
                    // flight below then has to hit from that height.
                    if (FinderStandAt(c.Feet, job.Ignore != null && job.Ignore.IsValid ? job.Ignore : null, out var stands) && stands.Z > c.Feet.Z)
                        c.Feet = stands;
                    if (FinderReleaseFraction(c, job.Ignore) < 1f)
                        continue;
                    // A spot the player does not fit on, or cannot jump on (a wall against the
                    // shoulder, a low ceiling): teleported there they are stuck.
                    if (!FinderSpotFits(c.Feet, c.Kind is GrenadeSimLogic.ThrowKind.Jump or GrenadeSimLogic.ThrowKind.RunJump, job.Ignore))
                    {
                        unfit++;
                        continue;
                    }
                    if (exact.Any(e => e.Kind == c.Kind && GrenadeSimLogic.HorizontalDistance(e.Feet, c.Feet) < 8f
                            && Math.Abs(e.Pitch - c.Pitch) < 0.4f && Math.Abs(e.Yaw - c.Yaw) < 0.4f))
                        continue;
                    // The search itself flies in coarser steps: once more in full, the result has to hold.
                    var f = GrenadeSimLogic.Simulate(FinderLaunch(c), exactTrace, 20f, 1, null, FinderFuseOf(c.Type));
                    if (!f.Stopped || !FinderHits(target, f.Rest))
                        continue;
                    c.Rest = f.Rest;
                    c.FlightTime = f.Time;
                    c.Error = GrenadeSimLogic.HorizontalDistance(f.Rest, target.Center);
                    c.Miss = FinderMiss(target, f.Rest);
                    c.HasSim = true;
                    exact.Add(c);
                    if (exact.Count == VerifyBatch)
                        break;
                }
                if (exact.Count > 0)
                {
                    FinderLog($"[Finder] {exact.Count} calculated hits shown without test throws, {job.Clock.Elapsed.TotalSeconds:0.0} s ({unfit} left out: the player does not fit or cannot jump there)");
                    player.PrintToCenterAlert(" ");
                    if (finderMulti && job.Here)
                    {
                        NextFinderType(player, job, exact);
                        return;
                    }
                    // The player's own spot has a lineup: keep it, and look at the spots around
                    // them as well, so there is more than one place to throw from to choose
                    // between (".find here" stays with the one spot).
                    if (job.Here && !job.Quick && !finderHereOnly)
                    {
                        finderCarry[job.Slot] = exact;
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.here.found_more", exact.Count));
                        ContinueFinderAround(player, job.PlayerFeet, quiet: true);
                        return;
                    }
                    if (!job.Here && finderCarry.Remove(job.Slot, out var carried))
                        exact.InsertRange(0, carried.Take(2));
                    ShowFinderResults(player, job.Slot, exact, job.PlayerFeet, job.Ignore, job.Clock.Elapsed.TotalSeconds, job.Here);
                    return;
                }
            }
            var candidates = new List<FinderResult>();
            int blocked = 0;
            foreach (var r in ordered)
            {
                // Something right in front of the thrower: the game releases from elsewhere.
                if (FinderReleaseFraction(r, job.Ignore) < 1f)
                {
                    blocked++;
                    continue;
                }
                if (candidates.Any(c => c.Kind == r.Kind && GrenadeSimLogic.HorizontalDistance(c.Feet, r.Feet) < 8f
                        && Math.Abs(c.Pitch - r.Pitch) < 0.4f && Math.Abs(c.Yaw - r.Yaw) < 0.4f))
                    continue;
                candidates.Add(r);
                if (candidates.Count == (job.Quick ? VerifyQuickBatch : VerifyBatch))
                    break;
            }
            if (blocked > 0)
                FinderLog($"[Finder] {blocked} aims left out: something is within 22 u in front of the thrower", fileOnly: true);
            if (candidates.Count == 0 && job.Quick)
            {
                player.PrintToCenterAlert(Localizer.ForPlayer(player, "matchzy.pp.finder.live.no_straight"));
                return;
            }
            if (candidates.Count == 0 && job.Here && finderMulti)
            {
                NextFinderType(player, job, new List<FinderResult>());
                return;
            }
            if (candidates.Count == 0 && job.Here)
            {
                ContinueFinderAround(player, job.PlayerFeet);
                return;
            }
            // Nothing in the player's own area lands in the box: on to the spots further away.
            // (Since far misses are no longer test-thrown the search stopped here and said
            // "nothing comes close", with thousands of spots still unsearched.)
            if (candidates.Count == 0 && !job.Here && finderCarry.Remove(job.Slot, out var own))
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.around.only_own"));
                ShowFinderResults(player, job.Slot, own, job.PlayerFeet, job.Ignore, job.Clock.Elapsed.TotalSeconds, true);
                return;
            }
            if (candidates.Count == 0 && job.AreaStage)
            {
                if (job.Near.Count > 0)
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.area.nothing_rests",
                        job.Near.Count, job.Near.Min(r => FinderMiss(target, r.Rest))));
                ContinueFinderWide(player, job.PlayerFeet);
                return;
            }
            if (candidates.Count == 0)
            {
                player.PrintToCenterAlert(" ");
                PrintToPlayerChat(player, job.Here
                    ? Localizer.ForPlayer(player, "matchzy.pp.finder.none.here")
                    : job.Near.Count > 0
                        ? Localizer.ForPlayer(player, "matchzy.pp.finder.none.checked_closest", job.Checked.Count, job.Near.Min(r => FinderMiss(target, r.Rest)))
                        : Localizer.ForPlayer(player, "matchzy.pp.finder.none.checked", job.Checked.Count));
                return;
            }
            StartFinderVerify(player, job, target, candidates);
        }

        // The throw spot of a search from one position: a green box on the floor with a label, so
        // the player finds it again after walking off (E on it, or .findgo, puts them back on it
        // with the aim set).
        private void MarkFinderHereSpot(CCSPlayerController player, int slot, Vector3 feet)
        {
            if (finderResultEntities.Remove(slot, out var old))
                KillFinderEntities(old);
            var ents = FloorHuggingBox(feet, 12f, Color.Lime, player.PlayerPawn?.Value);
            uint? label = FinderLabel("1", new Vector3(feet.X, feet.Y, feet.Z + FinderLabelHeight), Color.Lime);
            if (label.HasValue)
                ents.Add(label.Value);
            finderResultEntities[slot] = ents;
            finderUseDown.Add(slot);
        }

        // .find here: all results share the spot, so they differ by aim. No floor markers; the
        // player is given aim #1 and switches with .findgo.
        // "Every type" search: one grenade type is done, on to the next, and the summary at the end.
        private bool finderMulti, finderMultiStep;
        private readonly List<string> finderMultiQueue = new();
        private readonly List<FinderResult> finderMultiResults = new();
        private readonly List<string> finderMultiNotes = new();

        private void NextFinderType(CCSPlayerController player, FinderJob job, List<FinderResult> found)
        {
            string done = FinderTypeFor(job.Slot);
            finderMultiNotes.Add(found.Count > 0
                ? Localizer.ForPlayer(player, "matchzy.pp.finder.multi.yes", FinderTypeWord(player, done), found.Count)
                : Localizer.ForPlayer(player, "matchzy.pp.finder.multi.no", FinderTypeWord(player, done)));
            finderMultiResults.AddRange(found.Take(3));
            var pawn = player.PlayerPawn?.Value;
            if (finderMultiQueue.Count > 0 && pawn != null && pawn.IsValid && pawn.AbsOrigin != null && finderTargets.TryGetValue(job.Slot, out var target))
            {
                finderType[job.Slot] = finderMultiQueue[0];
                finderMultiQueue.RemoveAt(0);
                finderCurrentType = FinderTypeFor(job.Slot);
                finderDecoy[job.Slot] = false;
                finderMultiStep = true;
                try
                {
                    StartFinderHere(player, pawn, target, false);
                }
                finally
                {
                    finderMultiStep = false;
                }
                // The next type could not even start (no floor below, say): finish with what there is.
                if (finderJob != null)
                    return;
            }
            finderMulti = false;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.multi.summary", string.Join(", ", finderMultiNotes)));
            var all = new List<FinderResult>(finderMultiResults);
            finderMultiResults.Clear();
            finderMultiQueue.Clear();
            if (all.Count == 0)
            {
                player.PrintToCenterAlert(" ");
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.multi.none"));
                return;
            }
            // More than the one spot: the area around the player too, for the first type that works.
            finderCarry[job.Slot] = all;
            finderType[job.Slot] = all[0].Type;
            finderDecoy[job.Slot] = false;
            finderHereOnly = false;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.multi.more", FinderTypeWord(player, all[0].Type)));
            ContinueFinderAround(player, job.PlayerFeet, quiet: true);
        }

        private void ShowFinderHereResults(CCSPlayerController player, int slot, List<FinderResult> found, double seconds)
        {
            // Nothing lands in the box: the throws that come closest, so the player sees how far
            // off the spot is and can move, or draw the box differently.
            if (found.Count > 0 && found.All(r => r.NoHit))
            {
                var closest = new List<FinderResult>();
                foreach (var r in found.OrderBy(r => r.Miss))
                {
                    if (closest.Any(p => p.Kind == r.Kind && Math.Abs(p.Pitch - r.Pitch) < 2f && Math.Abs(p.Yaw - r.Yaw) < 2f))
                        continue;
                    closest.Add(r);
                    if (closest.Count == 3)
                        break;
                }
                player.PrintToCenterAlert(" ");
                finderResults[slot] = closest;
                MarkFinderHereSpot(player, slot, closest[0].Feet);
                GoToFinderResult(player, 1);
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.closest.header", closest[0].Miss));
                for (int i = 0; i < closest.Count; i++)
                {
                    var r = closest[i];
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.closest.line", i + 1, FinderKindName(player, r.Kind), r.Miss));
                    FinderLog(string.Format(CultureInfo.InvariantCulture,
                        "[Finder] closest #{0} {1} feet ({2:0.0} {3:0.0} {4:0.0}) aim ({5:0.00} {6:0.00}) rested ({7:0} {8:0} {9:0}) {10:0.0} u outside",
                        i + 1, r.Kind, r.Feet.X, r.Feet.Y, r.Feet.Z, r.Pitch, r.Yaw, r.Rest.X, r.Rest.Y, r.Rest.Z, r.Miss));
                }
                return;
            }
            var picked = new List<FinderResult>();
            var hereTarget = finderTargets.TryGetValue(slot, out var ht) ? ht : null;
            if (hereTarget != null)
            {
                foreach (var r in found.OrderBy(r => (int)r.Kind).ThenBy(r => r.Error).Take(24))
                    if (r.Robust < 0f)
                        r.Robust = FinderRobustness(hereTarget, r, player.PlayerPawn?.Value);
                found = FinderKeepForgiving(found.Where(r => r.Robust >= 0f).ToList());
            }
            // Forgiving throws first, then the easiest kind: standing, jumpthrow, run throw, run + jumpthrow.
            foreach (var r in found.OrderByDescending(r => MathF.Round(r.Robust * 4f)).ThenBy(r => (int)r.Kind).ThenBy(r => r.Error))
            {
                if (picked.Any(p => p.Kind == r.Kind && Math.Abs(p.Pitch - r.Pitch) < 2f && Math.Abs(p.Yaw - r.Yaw) < 2f))
                    continue;
                picked.Add(r);
                if (picked.Count == FinderShowResults)
                    break;
            }
            player.PrintToCenterAlert(" ");
            finderResults[slot] = picked;
            MarkFinderHereSpot(player, slot, picked[0].Feet);
            GoToFinderResult(player, 1);
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.here.found", picked.Count, seconds));
            for (int i = 0; i < picked.Count; i++)
            {
                var r = picked[i];
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.here.line",
                    i + 1, FinderKindName(player, r.Kind), FinderRobustText(player, r.Robust), FinderLandText(player, r), r.FlightTime));
                FinderLog(string.Format(CultureInfo.InvariantCulture,
                    "[Finder] here #{0} {1} feet ({2:0.0} {3:0.0} {4:0.0}) aim ({5:0.00} {6:0.00}) test landed ({7:0} {8:0} {9:0}) robust {10:0.00}",
                    i + 1, r.Kind, r.Feet.X, r.Feet.Y, r.Feet.Z, r.Pitch, r.Yaw, r.Rest.X, r.Rest.Y, r.Rest.Z, r.Robust));
            }
        }

        // Shows verified results: numbered boxes, chat list, and puts the player on #1.
        private void ShowFinderResults(CCSPlayerController player, int slot, List<FinderResult> found, Vector3 playerFeet, CBaseEntity? ignore, double seconds, bool here = false)
        {
            if (here)
            {
                ShowFinderHereResults(player, slot, found, seconds);
                return;
            }
            var picked = new List<FinderResult>();
            // Rated: the dozen nearest (a full simulation each, several per result).
            if (finderTargets.TryGetValue(slot, out var areaTarget))
            {
                foreach (var r in found.OrderBy(r => GrenadeSimLogic.HorizontalDistance(r.Feet, playerFeet)).ThenBy(r => r.Error).Take(24))
                    if (r.Robust < 0f)
                        r.Robust = FinderRobustness(areaTarget, r, ignore);
                found = FinderKeepForgiving(found.Where(r => r.Robust >= 0f).ToList());
            }
            if (found.Count == 0)
                return;
            foreach (var r in found
                .OrderByDescending(r => MathF.Round(r.Robust * 4f))
                .ThenBy(r => MathF.Floor(GrenadeSimLogic.HorizontalDistance(r.Feet, playerFeet) / 64f))
                .ThenBy(r => (int)r.Kind)
                .ThenBy(r => r.Error))
            {
                if (picked.Any(p => p.Kind == r.Kind && GrenadeSimLogic.HorizontalDistance(p.Feet, r.Feet) < 48f))
                    continue;
                picked.Add(r);
                if (picked.Count == FinderShowResults)
                    break;
            }
            player.PrintToCenterAlert(" ");
            finderResults[slot] = picked;
            var ents = new List<uint>();
            for (int i = 0; i < picked.Count; i++)
            {
                var r = picked[i];
                var color = i == 0 ? Color.Lime : Color.Yellow;
                ents.AddRange(FloorHuggingBox(r.Feet, 12f, color, ignore));
                uint? label = FinderLabel((i + 1).ToString(CultureInfo.InvariantCulture), new Vector3(r.Feet.X, r.Feet.Y, r.Feet.Z + FinderLabelHeight), color);
                if (label.HasValue)
                    ents.Add(label.Value);
            }
            if (finderResultEntities.Remove(slot, out var previous))
                KillFinderEntities(previous);
            finderResultEntities[slot] = ents;
            finderUseDown.Add(slot);
            // Only put the player on #1 when it is in the area they searched from. Spots further
            // away (the search widened) are shown and listed; they go there themselves.
            float firstAway = GrenadeSimLogic.HorizontalDistance(picked[0].Feet, playerFeet);
            if (firstAway <= DeepRadius + 100f)
                GoToFinderResult(player, 1);
            else
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.area.nearest_away", firstAway));
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.area.found", picked.Count, seconds));
            for (int i = 0; i < picked.Count; i++)
            {
                var r = picked[i];
                float away = GrenadeSimLogic.HorizontalDistance(r.Feet, playerFeet);
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.area.line",
                    i + 1, FinderKindName(player, r.Kind), FinderRobustText(player, r.Robust), away, FinderLandText(player, r)));
                FinderLog(string.Format(CultureInfo.InvariantCulture,
                    "[Finder] #{0} {1} feet ({2:0.0} {3:0.0} {4:0.0}) aim ({5:0.00} {6:0.00}) test landed ({7:0} {8:0} {9:0})",
                    i + 1, r.Kind, r.Feet.X, r.Feet.Y, r.Feet.Z, r.Pitch, r.Yaw, r.Rest.X, r.Rest.Y, r.Rest.Z));
            }
        }

        // Candidate standing spots: nav areas near the player, sampled on a grid, feet on the floor.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<Vector3> FinderCandidates(Vector3 playerFeet, Vector3 target, float radius, HashSet<(int, int, int)>? skip, int cap = FinderMaxCandidates, float grid = FinderGrid)
        {
            var bounds = navBoundsCache ??= ReadNavBounds();
            var points = new List<Vector3>();
            foreach (var b in bounds)
            {
                for (float x = b.MinX + grid / 2f; x < b.MaxX + 0.01f; x += grid)
                {
                    for (float y = b.MinY + grid / 2f; y < b.MaxY + 0.01f; y += grid)
                    {
                        float cx = Math.Min(x, b.MaxX), cy = Math.Min(y, b.MaxY);
                        var p = new Vector3(cx, cy, 0.5f * (b.MinZ + b.MaxZ));
                        if (GrenadeSimLogic.HorizontalDistance(p, playerFeet) > radius)
                            continue;
                        float toTarget = GrenadeSimLogic.HorizontalDistance(p, target);
                        if (toTarget < FinderMinDistance + 96f || toTarget > FinderMaxDistance)
                            continue;
                        points.Add(p);
                    }
                }
            }
            // Nearest to the player first; cap the work.
            points.Sort((a, c) => GrenadeSimLogic.HorizontalDistance(a, playerFeet).CompareTo(GrenadeSimLogic.HorizontalDistance(c, playerFeet)));
            if (skip != null)
                points.RemoveAll(p => skip.Contains(FinderKey(p)));
            if (points.Count > cap)
                points.RemoveRange(cap, points.Count - cap);
            // Raw nav points (mid height of their area): SnapCandidate puts them on the floor when
            // they are processed, spread over ticks instead of hundreds of traces in one frame.
            return points;
        }

        // Drops a player-sized box from here to the floor. False when nothing is below.
        private static bool FinderPlayerDrop(Vector3 from, CBaseEntity? ignore, out Vector3 feet, out Vector3 normal)
        {
#if HAS_CSS_TRACE
            return FinderPlayerDropCss(from, ignore, out feet, out normal);
#else
            feet = default; normal = default;
            return false;
#endif
        }

#if HAS_CSS_TRACE
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool FinderPlayerDropCss(Vector3 from, CBaseEntity? ignore, out Vector3 feet, out Vector3 normal)
        {
            var start = new Vector(from.X, from.Y, from.Z + 2f);
            var end = new Vector(from.X, from.Y, from.Z - 2000f);
            var opts = PlayerTraceOptionsCss();
            var tr = Trace.TraceHullShape(start, end, new Vector(-16, -16, 0), new Vector(16, 16, 72), ignore, opts);
            if (!tr.DidHit() || tr.IsAllSolid)
            {
                feet = default; normal = default;
                return false;
            }
            feet = new Vector3(from.X, from.Y, from.Z + 2f - 2002f * tr.Fraction);
            normal = new Vector3(tr.Normal.X, tr.Normal.Y, tr.Normal.Z);
            return true;
        }
#endif

        private static Vector3? SnapCandidate(Vector3 p) =>
            FinderTrace(p + new Vector3(0, 0, 48f), p + new Vector3(0, 0, -96f), out var hit, out var n, out _) && n.Z > 0.7f
                ? hit
                : null;

        // ignore: an entity the trace passes through (the aiming player's own pawn: a ray from the
        // eye looking down otherwise hits the player's own body and lands at head height).
        private static bool FinderTrace(Vector3 from, Vector3 to, out Vector3 hit, out Vector3 normal, out float fraction, CBaseEntity? ignore = null)
        {
#if HAS_CSS_TRACE
            return FinderTraceCss(from, to, out hit, out normal, out fraction, ignore);
#else
            hit = default; normal = default; fraction = 1f;
            return false;
#endif
        }

#if HAS_CSS_TRACE
        private static Vector? finderTraceFrom, finderTraceTo, finderHullMins, finderHullMaxs;
        private static ulong finderLayersWith = 0x200003001UL, finderLayersAs = 0x8000020000UL, finderLayersExclude = 0UL;

        // The grenade's own move: a hull trace of its size. hit = the grenade's centre at contact.
        private static bool FinderGrenadeTrace(Vector3 from, Vector3 to, out Vector3 hit, out Vector3 normal, out float fraction, CBaseEntity? ignore)
        {
#if HAS_CSS_TRACE
            return FinderHullTraceCss(from, to, out hit, out normal, out fraction, ignore);
#else
            hit = default; normal = default; fraction = 1f;
            return false;
#endif
        }

#if HAS_CSS_TRACE
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool FinderHullTraceCss(Vector3 from, Vector3 to, out Vector3 hit, out Vector3 normal, out float fraction, CBaseEntity? ignore)
        {
            var start = finderTraceFrom ??= new Vector(0, 0, 0);
            var end = finderTraceTo ??= new Vector(0, 0, 0);
            float h = GrenadeSimLogic.HullHalf;
            var mins = finderHullMins ??= new Vector(-h, -h, -h);
            var maxs = finderHullMaxs ??= new Vector(h, h, h);
            start.X = from.X; start.Y = from.Y; start.Z = from.Z;
            end.X = to.X; end.Y = to.Y; end.Z = to.Z;
            // What a thrown grenade collides with, read off a real smoke projectile: world solids
            // and grenade clips, not players. With "everything solid" one inferno throw in five
            // took another path than the game's.
            var opts = new TraceOptions { InteractsWith = (Contents)finderLayersWith, InteractsAs = (Contents)finderLayersAs, InteractsExclude = (Contents)finderLayersExclude };
            var tr = Trace.TraceHullShape(start, end, mins, maxs, ignore, opts);
            if (!tr.DidHit())
            {
                hit = default; normal = default; fraction = 1f;
                return false;
            }
            fraction = tr.Fraction;
            hit = from + (to - from) * fraction;
            normal = new Vector3(tr.Normal.X, tr.Normal.Y, tr.Normal.Z);
            return true;
        }
#endif

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool FinderTraceCss(Vector3 from, Vector3 to, out Vector3 hit, out Vector3 normal, out float fraction, CBaseEntity? ignore)
        {
            // One pair of native vectors for every trace (game thread only): a search makes millions
            // of traces, and two fresh native-backed vectors each fed the GC until it paused a tick.
            var start = finderTraceFrom ??= new Vector(0, 0, 0);
            var end = finderTraceTo ??= new Vector(0, 0, 0);
            start.X = from.X; start.Y = from.Y; start.Z = from.Z;
            end.X = to.X; end.Y = to.Y; end.Z = to.Z;
            // TraceOptions is a fork-only type: it stays inside this method (stock CSS compat).
            var opts = new TraceOptions { InteractsWith = Masks.Solid };
            var tr = Trace.TraceEndShape(start, end, ignore, opts);
            if (!tr.DidHit())
            {
                hit = default; normal = default; fraction = 1f;
                return false;
            }
            hit = new Vector3(tr.HitPoint.X, tr.HitPoint.Y, tr.HitPoint.Z);
            normal = new Vector3(tr.Normal.X, tr.Normal.Y, tr.Normal.Z);
            fraction = tr.Fraction;
            return true;
        }
#endif

        private uint? FinderLabel(string text, Vector3 pos, Color color)
        {
            var label = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (label == null)
                return null;
            label.MessageText = text;
            label.Color = color;
            label.FontName = "Arial Bold";
            Schema.SetSchemaValue(label.Handle, "CPointWorldText", "m_bEnabled", true);
            Schema.SetSchemaValue(label.Handle, "CPointWorldText", "m_flFontSize", 48.0f);
            Schema.SetSchemaValue(label.Handle, "CPointWorldText", "m_flWorldUnitsPerPx", 0.25f);
            Schema.SetSchemaValue(label.Handle, "CPointWorldText", "m_bFullbright", true);
            Schema.SetSchemaValue(label.Handle, "CPointWorldText", "m_nJustifyHorizontal", 1);
            Schema.SetSchemaValue(label.Handle, "CPointWorldText", "m_nJustifyVertical", 1);
            Schema.SetSchemaValue(label.Handle, "CPointWorldText", "m_nReorientMode", 1);
            label.Teleport(new Vector(pos.X, pos.Y, pos.Z), new QAngle(0, 0, 90), new Vector(0, 0, 0));
            label.DispatchSpawn();
            return label.EntityHandle.Raw;
        }

        // True while the player is in a .draw session: other +use features ignore E then.
        private bool IsFinderDrawing(CCSPlayerController? player) =>
            player != null && (drawSessions.ContainsKey(player.Slot) || finderResults.ContainsKey(player.Slot));

        private static readonly Color FinderGreen = Color.FromArgb(255, 60, 255, 60);
        private const float FinderLineWidth = 1.0f;

        // Every tick: live preview of the box under the crosshair, R advances a step.
        private void TickDrawSessions()
        {
            foreach (var (slot, session) in drawSessions.ToList())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                var pawn = player?.PlayerPawn?.Value;
                if (player == null || !player.IsValid || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                    continue;
                var eye = new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
                var fwd = GrenadeSimLogic.Forward(pawn.EyeAngles.X, pawn.EyeAngles.Y);
                bool aimHit = FinderTrace(eye, eye + fwd * 8192f, out var hit, out var hitNormal, out _, pawn);

                List<(Vector3, Vector3)> lines;
                string hint;
                switch (session.Step)
                {
                    case 0 when !session.Manual:
                        // Live: the box that R would set, fitted to the surface under the crosshair.
                        lines = aimHit && FitTargetBox(hit, hitNormal) is (Vector3 fmin, Vector3 fmax) ? BoxLines(fmin, fmax) : new();
                        hint = Localizer.ForPlayer(player, "matchzy.pp.finder.hint.auto");
                        break;
                    case 0:
                        // Exactly the point under the crosshair: the area unfolds at that height.
                        lines = aimHit ? new() { (hit, hit + new Vector3(0, 0, 32f)) } : new();
                        hint = Localizer.ForPlayer(player, "matchzy.pp.finder.hint.first");
                        break;
                    case 1:
                        // The corner follows where the aim crosses the flat plane at the first
                        // corner's height (unfolding on the ledge/floor), not whatever is behind it.
                        if (Math.Abs(fwd.Z) > 1e-4f)
                        {
                            float t = (session.First.Z - eye.Z) / fwd.Z;
                            if (t > 0f)
                            {
                                var onPlane = eye + fwd * t;
                                session.Second = new Vector3(
                                    Math.Clamp(onPlane.X, session.First.X - DrawMaxSide, session.First.X + DrawMaxSide),
                                    Math.Clamp(onPlane.Y, session.First.Y - DrawMaxSide, session.First.Y + DrawMaxSide),
                                    session.First.Z);
                            }
                        }
                        lines = BoxLines(BoxMin(session), BoxMax(session, 0f));
                        hint = Localizer.ForPlayer(player, "matchzy.pp.finder.hint.unfold");
                        break;
                    default:
                        // Height: where the aim ray passes the box's vertical centre line.
                        var min = BoxMin(session);
                        var max = BoxMax(session, 0f);
                        var c = new Vector3((min.X + max.X) / 2f, (min.Y + max.Y) / 2f, min.Z);
                        float flat = GrenadeSimLogic.HorizontalDistance(eye, c);
                        float horiz = MathF.Sqrt(fwd.X * fwd.X + fwd.Y * fwd.Y);
                        if (horiz > 0.01f)
                            session.Height = Math.Clamp(eye.Z + fwd.Z / horiz * flat - min.Z, 8f, 300f);
                        lines = BoxLines(min, BoxMax(session, session.Height));
                        hint = Localizer.ForPlayer(player, "matchzy.pp.finder.hint.height");
                        break;
                }
                UpdatePreview(session.Preview, lines);

                double now = Server.CurrentTime;
                if (now >= session.NextHint)
                {
                    session.NextHint = now + 1.0;
                    player.PrintToCenterAlert(hint);
                }

                bool reload = (player.Buttons & PlayerButtons.Reload) != 0;
                bool pressed = reload && !session.ReloadWasDown;
                session.ReloadWasDown = reload;
                if (!pressed || (!aimHit && session.Step == 0))
                    continue;
                if (session.Step == 0 && !session.Manual)
                {
                    if (FitTargetBox(hit, hitNormal) is not (Vector3 bmin, Vector3 bmax))
                        continue;
                    SetFinderTarget(player, bmin, bmax);
                    drawSessions.Remove(slot);
                    KillFinderEntities(session.Preview);
                }
                else if (session.Step == 0)
                {
                    session.First = hit;
                    session.Second = hit;
                    session.Step = 1;
                }
                else if (session.Step == 1)
                {
                    session.Step = 2;
                }
                else
                {
                    FinishDrawSession(player, session);
                }
            }
            // Target set: R resets it and starts over.
            foreach (var slot in finderTargets.Keys.ToList())
            {
                if (drawSessions.ContainsKey(slot))
                    continue;
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player == null || !player.IsValid)
                    continue;
                bool reload = (player.Buttons & PlayerButtons.Reload) != 0;
                bool was = finderReloadDown.Contains(slot);
                if (reload) finderReloadDown.Add(slot); else finderReloadDown.Remove(slot);
                if (reload && !was && finderJob?.Slot != slot)
                {
                    ClearFinderTarget(slot);
                    ClearFinderResults(slot);
                    drawSessions[slot] = new DrawSession { ReloadWasDown = true };
                }
            }
        }

        private readonly HashSet<int> finderReloadDown = new();
        private const float DrawMaxSide = 256f;

        private const float FitMaxExtent = 64f;
        private const float FitStep = 4f;
        private const float FitTopSearch = 96f;
        private const float FitBoxHeight = 48f;

        // The surface a smoke would rest on at the aimed point, and the box around it.
        // Aimed at a vertical face (a beam or ledge seen from the side or from far away): the top of
        // that object within reach above the hit; none (a tall wall): the floor in front of it.
        // The box then grows along X and Y while the surface stays at the same height, so a beam top
        // gives a narrow box and an open floor a box of up to 128 x 128 u.
        // Where a grenade could rest for an aimed point: the point itself on a floor; on a vertical
        // face (a beam or ledge seen from the side or from far away) the top of that object within
        // reach above; on a tall wall the floor in front of it.
        private static Vector3? SnapToSurface(Vector3 hit, Vector3 normal)
        {
            if (normal.Z >= GrenadeSimLogic.FloorNormalZ)
                return hit;
            var side = new Vector3(normal.X, normal.Y, 0f);
            side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.Zero;
            var inside = hit - side * 6f;
            if (FinderTrace(inside + new Vector3(0, 0, FitTopSearch), inside - new Vector3(0, 0, 4f), out var t, out var tn, out _)
                && tn.Z >= GrenadeSimLogic.FloorNormalZ && t.Z > hit.Z - 4f)
                return t;
            var front = hit + side * 12f;
            if (!FinderTrace(front, front - new Vector3(0, 0, 4096f), out var f, out var fn, out _) || fn.Z < GrenadeSimLogic.FloorNormalZ)
                return null;
            return f;
        }

        private static (Vector3 Min, Vector3 Max)? FitTargetBox(Vector3 hit, Vector3 normal)
        {
            if (SnapToSurface(hit, normal) is not Vector3 top)
                return null;

            float Extent(Vector3 dir)
            {
                float reach = 0f;
                for (float d = FitStep; d <= FitMaxExtent; d += FitStep)
                {
                    var p = top + dir * d;
                    if (!FinderTrace(p + new Vector3(0, 0, 8f), p - new Vector3(0, 0, 8f), out var h, out var n, out _)
                        || n.Z < GrenadeSimLogic.FloorNormalZ || Math.Abs(h.Z - top.Z) > 3f)
                        break;
                    reach = d;
                }
                return reach;
            }
            float px = Extent(Vector3.UnitX), nx = Extent(-Vector3.UnitX);
            float py = Extent(Vector3.UnitY), ny = Extent(-Vector3.UnitY);
            var min = new Vector3(top.X - Math.Max(nx, 4f), top.Y - Math.Max(ny, 4f), top.Z);
            var max = new Vector3(top.X + Math.Max(px, 4f), top.Y + Math.Max(py, 4f), top.Z + FitBoxHeight);
            return (min, max);
        }

        private static Vector3 BoxMin(DrawSession s) =>
            new(Math.Min(s.First.X, s.Second.X), Math.Min(s.First.Y, s.Second.Y), Math.Min(s.First.Z, s.Second.Z));

        private static Vector3 BoxMax(DrawSession s, float height) =>
            new(Math.Max(s.First.X, s.Second.X), Math.Max(s.First.Y, s.Second.Y), Math.Min(s.First.Z, s.Second.Z) + height);

        // The 12 edges of an axis-aligned box (4 when it is flat).
        private static List<(Vector3, Vector3)> BoxLines(Vector3 min, Vector3 max)
        {
            float z0 = min.Z + 1f, z1 = Math.Max(max.Z, min.Z + 1f);
            Vector3 P(float x, float y, float z) => new(x, y, z);
            var bottom = new[] { P(min.X, min.Y, z0), P(max.X, min.Y, z0), P(max.X, max.Y, z0), P(min.X, max.Y, z0) };
            var lines = new List<(Vector3, Vector3)>();
            for (int i = 0; i < 4; i++)
                lines.Add((bottom[i], bottom[(i + 1) % 4]));
            if (z1 - z0 > 2f)
            {
                var top = bottom.Select(b => new Vector3(b.X, b.Y, z1)).ToArray();
                for (int i = 0; i < 4; i++)
                {
                    lines.Add((top[i], top[(i + 1) % 4]));
                    lines.Add((bottom[i], top[i]));
                }
            }
            return lines;
        }

        // Moves the preview beams onto the given lines, creating or removing beams as needed.
        private void UpdatePreview(List<uint> beams, List<(Vector3 A, Vector3 B)> lines)
        {
            while (beams.Count > lines.Count)
            {
                KillFinderEntities(new List<uint> { beams[^1] });
                beams.RemoveAt(beams.Count - 1);
            }
            for (int i = 0; i < lines.Count; i++)
            {
                var (a, b) = lines[i];
                if (i < beams.Count)
                {
                    var beam = new CHandle<CBeam>(beams[i]).Value;
                    if (beam != null && beam.IsValid)
                    {
                        beam.EndPos.X = b.X;
                        beam.EndPos.Y = b.Y;
                        beam.EndPos.Z = b.Z;
                        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
                        beam.Teleport(new Vector(a.X, a.Y, a.Z), new QAngle(0, 0, 0), new Vector(0, 0, 0));
                        continue;
                    }
                }
                uint? created = FinderBeam(a, b, FinderGreen, FinderLineWidth);
                if (!created.HasValue)
                    continue;
                if (i < beams.Count) beams[i] = created.Value; else beams.Add(created.Value);
            }
        }

        private void FinishDrawSession(CCSPlayerController player, DrawSession session)
        {
            drawSessions.Remove(player.Slot);
            var min = BoxMin(session);
            var max = BoxMax(session, session.Height);
            // A single click without moving: give it a small footprint so there is something to hit.
            if (max.X - min.X < 16f) { min.X -= 8f; max.X += 8f; }
            if (max.Y - min.Y < 16f) { min.Y -= 8f; max.Y += 8f; }
            KillFinderEntities(session.Preview);
            SetFinderTarget(player, min, max);
        }

        private const float FinderFloorSearch = 72f;

        private void SetFinderTarget(CCSPlayerController player, Vector3 min, Vector3 max)
        {
            FinderLog(string.Format(CultureInfo.InvariantCulture, "[Finder] target box min ({0:0.0} {1:0.0} {2:0.0}) max ({3:0.0} {4:0.0} {5:0.0})", min.X, min.Y, min.Z, max.X, max.Y, max.Z));
            ClearFinderTarget(player.Slot);
            ClearFinderResults(player.Slot);
            var target = new FinderTarget { Min = min, Max = max, Floor = min.Z };
            // Floor under a 5 x 5 grid over the area, from a little above the box bottom.
            for (int ix = 0; ix <= 4; ix++)
            {
                for (int iy = 0; iy <= 4; iy++)
                {
                    var from = new Vector3(min.X + (max.X - min.X) * ix / 4f, min.Y + (max.Y - min.Y) * iy / 4f, min.Z + 8f);
                    if (FinderTrace(from, from - new Vector3(0, 0, FinderFloorSearch), out var floor, out var n, out _, player.PlayerPawn?.Value)
                        && n.Z >= GrenadeSimLogic.FloorNormalZ)
                        target.Floor = Math.Min(target.Floor, floor.Z);
                }
            }
            // Draw the box down to that floor, so what is shown is what counts.
            if (target.Floor < min.Z - 1f)
            {
                FinderLog(string.Format(CultureInfo.InvariantCulture, "[Finder] floor under the box reaches {0:0.0} u below its bottom, box lowered", min.Z - target.Floor));
                min = new Vector3(min.X, min.Y, target.Floor);
                target.Min = min;
            }
            foreach (var (a, b) in BoxLines(min, max))
            {
                uint? beam = FinderBeam(a, b, FinderGreen, FinderLineWidth);
                if (beam.HasValue)
                    target.Entities.Add(beam.Value);
            }
            finderTargets[player.Slot] = target;
            finderReloadDown.Add(player.Slot);
            player.PrintToCenterAlert(Localizer.ForPlayer(player, "matchzy.pp.finder.target.set.center"));
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.finder.target.set.chat"));
        }

        private bool CancelDrawSession(int slot)
        {
            if (!drawSessions.Remove(slot, out var session))
                return false;
            KillFinderEntities(session.Preview);
            return true;
        }

        private static uint? FinderBeam(Vector3 a, Vector3 b, Color color, float width)
        {
            var beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam == null)
                return null;
            beam.LifeState = 1;
            beam.Width = width;
            beam.Render = color;
            beam.EndPos.X = b.X;
            beam.EndPos.Y = b.Y;
            beam.EndPos.Z = b.Z;
            beam.Teleport(new Vector(a.X, a.Y, a.Z), new QAngle(0, 0, 0), new Vector(0, 0, 0));
            beam.DispatchSpawn();
            return beam.EntityHandle.Raw;
        }

        // A square outline whose corners each sit on the floor below them, so it follows uneven
        // ground (cobblestones, slopes) instead of floating or sinking at one height.
        private List<uint> FloorHuggingBox(Vector3 center, float half, Color color, CBaseEntity? ignore)
        {
            var corners = new Vector3[4];
            var offsets = new (float X, float Y)[] { (-half, -half), (half, -half), (half, half), (-half, half) };
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3(center.X + offsets[i].X, center.Y + offsets[i].Y, center.Z);
                corners[i] = FinderTrace(c + new Vector3(0, 0, 16f), c - new Vector3(0, 0, 32f), out var hit, out _, out _, ignore)
                    ? hit + new Vector3(0, 0, 1f)
                    : c + new Vector3(0, 0, 1f);
            }
            var ents = new List<uint>(4);
            for (int i = 0; i < 4; i++)
            {
                uint? beam = FinderBeam(corners[i], corners[(i + 1) % 4], color, FinderLineWidth);
                if (beam.HasValue)
                    ents.Add(beam.Value);
            }
            return ents;
        }

        // The grenade a search is for: the decoy when one is in hand, else the smoke.
        private static bool FinderHeldIsDecoy(CCSPlayerController player, out bool otherGrenade)
        {
            string held = player.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon.Value?.DesignerName ?? "";
            otherGrenade = held is "weapon_flashbang" or "weapon_hegrenade" or "weapon_molotov" or "weapon_incgrenade";
            return held == "weapon_decoy";
        }

        private readonly Dictionary<int, bool> finderDecoy = new();
        // The grenade a player's search is for: smoke, decoy, he, flash, molotov. The one in their
        // hand when they type .draw / .find; kept until they hold another grenade at the next one.
        private readonly Dictionary<int, string> finderType = new();
        // The type of the search that is running (one at a time): every result made during it is
        // of that type.
        private static string finderCurrentType = "smoke";
        // Seconds from the throw until an HE or flashbang goes off.
        // 103 ticks for an HE and 104 for a flashbang, counted from the release: read off 349
        // recorded lineups of a workshop set (and what the game's own script API uses).
        private static float finderFuseHe = 103f / 64f, finderFuseFlash = 104f / 64f;

        private static float FinderFuseSeconds(string type) => type == "flash" ? finderFuseFlash : finderFuseHe;

        private bool finderDemoThrow;
        // Lineups found on the player's own spot, kept while the spots around them are searched.
        private readonly Dictionary<int, List<FinderResult>> finderCarry = new();
        private bool finderHereOnly;
        private bool finderVerify;

        private static bool FinderDetonates(string type) => type is "he" or "flash" or "molotov";

        // When the grenade goes off in flight (a smoke or decoy only when it has come to rest).
        private static GrenadeSimLogic.Fuse FinderFuseOf(string type)
        {
            if (type is "he" or "flash")
                return new GrenadeSimLogic.Fuse(FinderFuseSeconds(type), 0f);
            if (type != "molotov")
                return default;
            float slope = ConVar.Find("weapon_molotov_maxdetonateslope")?.GetPrimitiveValue<float>() ?? 30f;
            float air = ConVar.Find("molotov_throw_detonate_time")?.GetPrimitiveValue<float>() ?? 2f;
            return new GrenadeSimLogic.Fuse(air, MathF.Cos(slope * MathF.PI / 180f));
        }

        // The grenade in the player's hand as a finder type; null when they hold something else.
        private static string? FinderHeldType(CCSPlayerController player) =>
            (player.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon.Value?.DesignerName ?? "") switch
            {
                "weapon_smokegrenade" => "smoke",
                "weapon_decoy" => "decoy",
                "weapon_hegrenade" => "he",
                "weapon_flashbang" => "flash",
                "weapon_molotov" or "weapon_incgrenade" => "molotov",
                _ => null,
            };

        private string FinderTypeFor(int slot) => finderType.TryGetValue(slot, out var t) ? t : "smoke";

        private string FinderTypeWord(CCSPlayerController player, string type) => type switch
        {
            "he" => Localizer.ForPlayer(player, "matchzy.pp.finder.type.he"),
            "flash" => Localizer.ForPlayer(player, "matchzy.pp.finder.type.flash"),
            "molotov" => Localizer.ForPlayer(player, "matchzy.pp.finder.type.molotov"),
            "decoy" => Localizer.ForPlayer(player, "matchzy.pp.finder.type.decoy"),
            _ => Localizer.ForPlayer(player, "matchzy.pp.finder.type.smoke"),
        };

        // The projectile type name the throw hook uses for this finder type.
        private static bool FinderTypeIs(string type, string nadeType) => type switch
        {
            "he" => nadeType == "hegrenade",
            "molotov" => nadeType is "molotov" or "incendiary",
            _ => nadeType == type,
        };

        private void FinderTakeOutGrenade(CCSPlayerController player, string type)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || player.TeamNum < 2)
                return;
            string weapon = type switch
            {
                "he" => "weapon_hegrenade",
                "flash" => "weapon_flashbang",
                "decoy" => "weapon_decoy",
                // The fire grenade of the player's own side (the other one cannot be held).
                "molotov" => player.TeamNum == 3 ? "weapon_incgrenade" : "weapon_molotov",
                _ => "weapon_smokegrenade",
            };
            // The engine's own weapon select (as .loadnade uses): "use weapon_x" sent to the
            // client does not switch on this game build, the player stood there with their knife.
            void Select(CCSPlayerController p)
            {
                if (!SwitchWeaponNative(p, weapon))
                    EquipWeaponByName(p, weapon);
            }
            if (pawn.WeaponServices?.MyWeapons.Any(w => w.Value?.DesignerName == weapon) == true)
            {
                Select(player);
                return;
            }
            // Not carried: given first, selected a moment later (it is not in the inventory yet).
            player.GiveNamedItem(weapon);
            int slot = player.Slot;
            AddTimer(0.15f, () =>
            {
                var p = Utilities.GetPlayerFromSlot(slot);
                if (p != null && IsPlayerValid(p) && isPractice && p.PawnIsAlive)
                    Select(p);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private readonly Dictionary<int, FinderResult> finderDemoShown = new();
        private readonly Dictionary<int, int> finderDemoGeneration = new();
        private readonly Dictionary<int, List<uint>> finderGoEntities = new();

        // Standing throws and jumpthrows by default: those are the ones a player repeats (measured:
        // released within 0.3 u of the plan, landing within 2 u). A running throw depends on the
        // player's speed and the moment of release; ".find run" adds them for those who want them.
        private static readonly GrenadeSimLogic.ThrowKind[] FinderStillKinds =
        {
            GrenadeSimLogic.ThrowKind.Stand, GrenadeSimLogic.ThrowKind.Jump,
        };
        private static readonly GrenadeSimLogic.ThrowKind[] FinderAllKinds =
        {
            GrenadeSimLogic.ThrowKind.Stand, GrenadeSimLogic.ThrowKind.Jump, GrenadeSimLogic.ThrowKind.Run, GrenadeSimLogic.ThrowKind.RunJump,
        };
        private static bool finderAllowRun;
        private static GrenadeSimLogic.ThrowKind[] FinderKinds => finderAllowRun ? FinderAllKinds : FinderStillKinds;

        // How forgiving a lineup is: the same throw with the aim 0.2 degrees off each way (and,
        // for running throws, the jump 10 u early or late or a little slower), calculated. A
        // lineup that only works when everything is exact is no use to a player.
        private static float FinderRobustness(FinderTarget target, FinderResult r, CBaseEntity? ignore)
        {
            GrenadeSimLogic.TraceFn trace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                FinderGrenadeTrace(a, b, out hit, out normal, out fraction, ignore != null && ignore.IsValid ? ignore : null);
            int ok = 0, all = 0;
            bool Try(float dPitch, float dYaw)
            {
                all++;
                float pitch0 = r.Pitch, yaw0 = r.Yaw;
                GrenadeSimLogic.Flight f;
                try
                {
                    r.Pitch = pitch0 + dPitch; r.Yaw = yaw0 + dYaw;
                    f = GrenadeSimLogic.Simulate(FinderLaunch(r), trace, 20f, 1, null, FinderFuseOf(r.Type)); // with the result's own eye height
                }
                finally
                {
                    r.Pitch = pitch0; r.Yaw = yaw0;
                }
                bool hit = f.Stopped && FinderHits(target, f.Rest);
                if (hit)
                    ok++;
                return hit;
            }
            // One step of the mouse each way (about 0.13 degrees at common settings), and two. A
            // lineup that misses one step to one side is hair-fine whatever the other three do
            // (seen live: in the box one step left, 380 u off one step right, rated "fine").
            bool oneStep = Try(0.15f, 0f) & Try(-0.15f, 0f) & Try(0f, 0.15f) & Try(0f, -0.15f);
            Try(0.3f, 0f); Try(-0.3f, 0f); Try(0f, 0.3f); Try(0f, -0.3f);
            if (FinderKindRuns(r.Kind))
            {
                float runUp = GrenadeSimLogic.RunUp, speed = GrenadeSimLogic.RunSpeed;
                try
                {
                    GrenadeSimLogic.RunUp = runUp - 10f; Try(0f, 0f);
                    GrenadeSimLogic.RunUp = runUp + 10f; Try(0f, 0f);
                    GrenadeSimLogic.RunUp = runUp; GrenadeSimLogic.RunSpeed = speed - 4f; Try(0f, 0f);
                }
                finally
                {
                    GrenadeSimLogic.RunUp = runUp;
                    GrenadeSimLogic.RunSpeed = speed;
                }
            }
            float share = all == 0 ? 0f : (float)ok / all;
            return oneStep ? share : Math.Min(share, 0.25f);
        }

        // Fewer results that work, rather than many that need a perfect hand: lineups that take
        // a step of the mouse either way are kept, the hair-fine ones only when there is nothing else
        // (they come with a warning then).
        // Fixed switches, kept as fields and not constants: a constant makes the compiler report the
        // branch that is switched off as unreachable code (CS0162).
        private static readonly bool FinderOnlyForgiving = false;
        private static readonly bool FinderTryEveryType = false;
        private static readonly bool FinderAreaBounceSweep = false;

        private static List<FinderResult> FinderKeepForgiving(List<FinderResult> rated)
        {
            if (!FinderOnlyForgiving)
                return rated; // rolled back 2026-11-17: every result is shown, rated but not filtered
            var solid = rated.Where(r => r.Robust >= 0.5f).ToList();
            return solid.Count > 0 ? solid : rated;
        }

        private string FinderLandText(CCSPlayerController player, FinderResult r) => r.Miss > 0.5f
            ? Localizer.ForPlayer(player, "matchzy.pp.finder.land.covers", r.Miss)
            : Localizer.ForPlayer(player, "matchzy.pp.finder.land.in_box", r.Error);

        private string FinderRobustText(CCSPlayerController player, float robust) =>
            robust >= 0.99f ? Localizer.ForPlayer(player, "matchzy.pp.finder.robust.forgiving")
            : robust >= 0.5f ? Localizer.ForPlayer(player, "matchzy.pp.finder.robust.needs_care")
            : Localizer.ForPlayer(player, "matchzy.pp.finder.robust.touchy");

        // Launch of a result, with the run it was worked out for.
        // The game checks the 22 units in front of the thrower's eye before it lets go of a
        // grenade, and when something is there it releases from another point (measured on
        // inferno: 34 u from the usual one, so the throw lands 130 u off). 1 = nothing in the way.
        private static float FinderReleaseFraction(Vector3 eye, float pitch, float yaw, CBaseEntity? ignore)
        {
            var fwd = GrenadeSimLogic.LaunchFrom(Vector3.Zero, pitch, yaw, 1f, Vector3.Zero).Position / GrenadeSimLogic.ReleaseForward;
            return FinderGrenadeTrace(eye, eye + fwd * 22f, out _, out _, out float fraction, ignore != null && ignore.IsValid ? ignore : null) ? fraction : 1f;
        }

        private static float FinderReleaseFraction(FinderResult r, CBaseEntity? ignore)
        {
            var fwd = GrenadeSimLogic.LaunchFrom(Vector3.Zero, r.Pitch, r.Yaw, 1f, Vector3.Zero).Position / GrenadeSimLogic.ReleaseForward;
            return FinderReleaseFraction(FinderLaunch(r).Position - fwd * GrenadeSimLogic.ReleaseForward, r.Pitch, r.Yaw, ignore);
        }

        // Where a player really comes to stand at a lineup spot. A spot found with a thin ray, or
        // saved before the fit check existed, can lie inside a step, a kerb or the uphill side of
        // a slope: put there the player sits in the ground and cannot jump. A player-sized box is
        // dropped onto the spot: it rests higher -> the player is put at that height; it starts
        // inside a wall -> the nearest free place within 16 u. Nothing found: the spot as it is.
        // Reads the collision layers off a live player pawn, once (they are the same for all).
        private void CapturePlayerLayers(CBaseEntity? entity)
        {
#if HAS_CSS_TRACE
            if (playerLayersKnown || entity is not CCSPlayerPawn pawn || !pawn.IsValid)
                return;
            try
            {
                var attr = pawn.Collision?.CollisionAttribute;
                if (attr == null || attr.InteractsWith == 0)
                    return;
                playerLayersWith = attr.InteractsWith;
                playerLayersAs = attr.InteractsAs;
                playerLayersExclude = attr.InteractsExclude;
                playerLayersKnown = true;
                FinderLog($"[Finder] player layers: with 0x{playerLayersWith:X} as 0x{playerLayersAs:X} exclude 0x{playerLayersExclude:X} group {attr.CollisionGroup}", fileOnly: true);
            }
            catch (Exception e)
            {
                Log($"[Finder] player layers: {e.GetType().Name}: {e.Message}");
            }
#endif
        }

        private Vector3 FinderStandFeet(Vector3 feet, CBaseEntity? ignore)
        {
            CapturePlayerLayers(ignore);
#if HAS_CSS_TRACE
            if (!HasCssTraceApi)
                return feet;
            try
            {
                var skip = ignore != null && ignore.IsValid ? ignore : null;
                if (FinderStandAt(feet, skip, out var stand))
                {
                    if (stand != feet)
                        FinderLog($"[Finder] teleport spot ({feet.X:0.0} {feet.Y:0.0} {feet.Z:0.0}): raised {stand.Z - feet.Z:0.0} u, the player's box rests higher there", fileOnly: true);
                    return stand;
                }
                foreach (float r in new[] { 4f, 8f, 12f, 16f })
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        var p = feet + new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 0f);
                        if (FinderStandAt(p, skip, out stand))
                        {
                            FinderLog($"[Finder] teleport spot ({feet.X:0.0} {feet.Y:0.0} {feet.Z:0.0}): the player does not fit, moved {r:0} u to ({stand.X:0.0} {stand.Y:0.0} {stand.Z:0.0})", fileOnly: true);
                            return stand;
                        }
                    }
                FinderLog($"[Finder] teleport spot ({feet.X:0.0} {feet.Y:0.0} {feet.Z:0.0}): the player does not fit and no free place within 16 u", fileOnly: true);
            }
            catch
            {
            }
#endif
            return feet;
        }

        // Nothing inside the space a standing player takes up here. Measured from the middle
        // outwards (a small box moved to the eight edges and corners at three heights), so no
        // trace starts inside a wall: one that does tells nothing.
        private static bool FinderBoxFree(Vector3 feet, CBaseEntity? ignore)
        {
#if HAS_CSS_TRACE
            foreach (float h in new[] { 20f, 44f, 68f })
            {
                var mid = feet + new Vector3(0, 0, h);
                for (int i = 0; i < 8; i++)
                {
                    float x = i is 0 or 1 or 7 ? 14f : i is 3 or 4 or 5 ? -14f : 0f;
                    float y = i is 1 or 2 or 3 ? 14f : i is 5 or 6 or 7 ? -14f : 0f;
                    if (FinderPlayerProbeCss(mid, mid + new Vector3(x, y, 0), ignore) < 1f)
                        return false;
                }
            }
#endif
            return true;
        }

        private static bool FinderStandAt(Vector3 feet, CBaseEntity? ignore, out Vector3 stand)
        {
            // From high first (a kerb is up to 18 u), lower where a ceiling is in the way.
            foreach (float lift in new[] { 16f, 6f, 1f })
            {
                if (!FinderPlayerDrop(feet + new Vector3(0, 0, lift), ignore, out var rest, out var n))
                    continue;
                if (rest.Z >= feet.Z + lift + 1.9f) // the box did not move: it started inside something
                    continue;
                if (n.Z < 0.7f || rest.Z < feet.Z - 4f)
                    continue;
                stand = rest.Z > feet.Z + 0.5f ? rest : feet;
                // The drop alone is not proof: a box that starts inside a kerb taller than the
                // drop height passes straight through it to the floor (find6 / find7 on banana,
                // 2026-11-17: both hard against a kerb, the player stuck in it).
                if (!FinderBoxFree(stand, ignore))
                    continue;
                return true;
            }
            stand = feet;
            return false;
        }

        // Room for a standing player on the spot (two boxes of the player's width, legs and
        // chest), and for a jumpthrow room to rise the height of a jump as well.
        private static bool FinderSpotFits(Vector3 feet, bool jumps, CBaseEntity? ignore)
        {
#if HAS_CSS_TRACE
            if (!HasCssTraceApi)
                return true;
            // A player dropped onto the spot from just above has to come to stand at the spot's
            // own height. Next to a step or a kerb they land on top of it instead (11 u higher on
            // mirage T spawn): the spot was found with a thin ray, the player put there sat in
            // the step and could not jump.
            if (!FinderPlayerDrop(feet + new Vector3(0, 0, 12f), ignore != null && ignore.IsValid ? ignore : null, out var stands, out var floorNormal)
                || floorNormal.Z < 0.7f || Math.Abs(stands.Z - feet.Z) > 2f)
                return false;
            if (!FinderBoxFree(stands, ignore != null && ignore.IsValid ? ignore : null))
                return false;
            float rise = jumps ? 60f : 2f;
            // (The lower box starts 8 u up: on a slope its uphill corner would sit in the ground.)
            foreach (float h in new[] { 24f, 55f })
            {
                var from = feet + new Vector3(0, 0, h);
                if (FinderPlayerSweepCss(from, from + new Vector3(0, 0, rise), ignore != null && ignore.IsValid ? ignore : null) < 1f)
                    return false;
            }
#endif
            return true;
        }

        private static GrenadeSimLogic.Launch FinderLaunch(FinderResult r)
        {
            if (r.Launch is GrenadeSimLogic.Launch own)
                return own;
            float runUp = GrenadeSimLogic.RunUp, speed = GrenadeSimLogic.RunSpeed, eye = GrenadeSimLogic.EyeHeight;
            try
            {
                GrenadeSimLogic.RunUp = r.RunUp;
                GrenadeSimLogic.RunSpeed = r.RunSpeed;
                GrenadeSimLogic.EyeHeight = r.EyeHeight;
                return GrenadeSimLogic.LaunchFrom(r.Feet, r.Kind, r.Pitch, r.Yaw);
            }
            finally
            {
                GrenadeSimLogic.RunUp = runUp;
                GrenadeSimLogic.RunSpeed = speed;
                GrenadeSimLogic.EyeHeight = eye;
            }
        }

        private static bool FinderKindRuns(GrenadeSimLogic.ThrowKind kind) =>
            kind is GrenadeSimLogic.ThrowKind.Run or GrenadeSimLogic.ThrowKind.RunJump;

        private static string FinderKindName(GrenadeSimLogic.ThrowKind kind) => kind switch
        {
            GrenadeSimLogic.ThrowKind.RunJump => "Run + jumpthrow",
            GrenadeSimLogic.ThrowKind.Run => "Run throw",
            GrenadeSimLogic.ThrowKind.Jump => "Jumpthrow",
            _ => "Stand",
        };

        // The same words in the player's language (the one above is also written into saved lineups).
        private string FinderKindName(CCSPlayerController player, GrenadeSimLogic.ThrowKind kind) => kind switch
        {
            GrenadeSimLogic.ThrowKind.RunJump => Localizer.ForPlayer(player, "matchzy.pp.finder.kind.runjump"),
            GrenadeSimLogic.ThrowKind.Run => Localizer.ForPlayer(player, "matchzy.pp.finder.kind.run"),
            GrenadeSimLogic.ThrowKind.Jump => Localizer.ForPlayer(player, "matchzy.pp.finder.kind.jump"),
            _ => Localizer.ForPlayer(player, "matchzy.pp.finder.kind.stand"),
        };

        // A running jumpthrow needs a straight, level, free run from the spot to a little past the
        // jump point: same floor height all the way, nothing in the way at knee or head height.
        private static bool FinderRunPathClear(Vector3 feet, float yaw, CBaseEntity? ignore)
        {
            var dir = GrenadeSimLogic.Heading(yaw);
            float length = GrenadeSimLogic.RunUp + 48f;
            for (float d = 32f; d <= length + 0.001f; d += 32f)
            {
                var p = feet + dir * d;
                if (!FinderTrace(p + new Vector3(0, 0, 18f), p - new Vector3(0, 0, 18f), out var floor, out var n, out _, ignore)
                    || n.Z < 0.99f || Math.Abs(floor.Z - feet.Z) > 2f)
                    return false;
            }
            foreach (float h in new[] { 20f, 48f, 100f })
            {
                var up = new Vector3(0, 0, h);
                if (FinderTrace(feet + up, feet + dir * length + up, out _, out _, out _, ignore))
                    return false;
            }
            // The whole body has to fit, not only its centre line: a run-up was offered along a
            // ledge with a pillar in the way (the gate stood in the pillar). Floor under both
            // shoulders all the way, and a player-wide box swept along the path at two heights.
            var side = GrenadeSimLogic.Heading(yaw + 90f);
            for (float d = 0f; d <= length + 0.001f; d += 16f)
                foreach (float off in new[] { -15f, 15f })
                {
                    var p = feet + dir * d + side * off;
                    if (!FinderTrace(p + new Vector3(0, 0, 18f), p - new Vector3(0, 0, 18f), out var edge, out var en, out _, ignore)
                        || en.Z < 0.7f || Math.Abs(edge.Z - feet.Z) > 6f)
                        return false;
                }
#if HAS_CSS_TRACE
            foreach (float h in new[] { 20f, 54f })
                if (FinderPlayerSweepCss(feet + new Vector3(0, 0, h), feet + dir * length + new Vector3(0, 0, h), ignore) < 1f)
                    return false;
#endif
            return true;
        }
        private readonly HashSet<int> finderThrewSinceGo = new();

        private void ClearFinderTarget(int slot)
        {
            if (finderTargets.Remove(slot, out var t))
                KillFinderEntities(t.Entities);
        }

        private void ClearFinderResults(int slot)
        {
            finderResults.Remove(slot);
            finderDemoShown.Remove(slot);
            // Throws still in the air belong to the box that is being replaced: no verdict (and
            // no auto-save) against the next one.
            finderTracks.RemoveAll(t => t.Slot == slot && !t.Plan.Library);
            if (!finderTargets.ContainsKey(slot))
                finderLive.Remove(slot);
            if (finderGoEntities.Remove(slot, out var goLine))
                KillFinderEntities(goLine);
            finderArmed.Remove(slot);
            finderThrewSinceGo.Remove(slot);
            if (finderResultEntities.Remove(slot, out var ents))
                KillFinderEntities(ents);
            if (finderJob?.Slot == slot)
                finderJob = null;
            if (verifyJob?.Slot == slot)
                CancelFinderVerify();
        }

        private void KillFinderEntities(List<uint> handles)
        {
            foreach (uint raw in handles)
            {
                var ent = new CHandle<CBaseEntity>(raw).Value;
                if (ent != null && ent.IsValid)
                    SafeRemoveEntity(ent, "finder");
            }
            handles.Clear();
        }

        // Practice start/end and unload: remove everything.
        private void ResetGrenadeFinder()
        {
            nadeAutoShown.Clear();
            stuckWatches.Clear();
            finderAimMarks.Clear();
            ClearSpotRings();
            finderAimLockNoted.Clear();
            lookMenus.Clear();
            chatAsks.Clear();
            nadeUseHold.Clear();
            nadeTrainings.Clear();
            throwWatch.Clear();
            lastThrownInfo.Clear();
            CancelFinderVerify();
            finderJob = null;
            foreach (var slot in drawSessions.Keys.ToList())
                CancelDrawSession(slot);
            foreach (var slot in finderTargets.Keys.ToList())
                ClearFinderTarget(slot);
            foreach (var slot in finderResultEntities.Keys.ToList())
                ClearFinderResults(slot);
            finderResults.Clear();
            finderArmed.Clear();
            finderTracks.Clear();
            finderLive.Clear();
            finderDecoy.Clear();
            finderDemoShown.Clear();
            finderDemoGeneration.Clear();
            finderUseDown.Clear();
            finderReloadDown.Clear();
            // Per-session state keyed by slot: the next practice session starts without the last
            // one's grenade type, kept lineups or .nextnade position.
            finderCarry.Clear();
            finderType.Clear();
            finderArmedOff.Clear();
            finderThrewSinceGo.Clear();
            nadeStep.Clear();
            nadeHoverTick.Clear();
            foreach (var slot in finderGoEntities.Keys.ToList())
                if (finderGoEntities.Remove(slot, out var guide))
                    KillFinderEntities(guide);
        }

        // A player left: nothing of theirs stays in the world or waits for whoever gets the slot next.
        private void ForgetFinderPlayer(int slot)
        {
            CancelDrawSession(slot);
            ClearFinderTarget(slot);
            ClearFinderResults(slot);
            if (finderGoEntities.Remove(slot, out var guide))
                KillFinderEntities(guide);
            finderArmed.Remove(slot);
            finderLive.Remove(slot);
            finderDecoy.Remove(slot);
            finderCarry.Remove(slot);
            ForgetThrowWatch(slot);
            ForgetLookMenuPlayer(slot);
            finderDemoGeneration.Remove(slot);
            finderUseDown.Remove(slot);
            finderReloadDown.Remove(slot);
            finderThrewSinceGo.Remove(slot);
            landingUseDown.Remove(slot);
            // Slots are reused: the next player in this slot starts with none of this.
            finderType.Remove(slot);
            finderArmedOff.Remove(slot);
            finderDemoShown.Remove(slot);
            finderAimLockNoted.Remove(slot);
            stuckWatches.Remove(slot);
            nadeStep.Remove(slot);
            nadeHoverTick.Remove(slot);
            landingHeld.Remove(slot);
            landingSelected.Remove(slot);
            finderTracks.RemoveAll(t => t.Slot == slot);
        }

        // Map change: the entities are already gone, only forget them.
        private void ForgetGrenadeFinderOnMapStart()
        {
            verifyJob = null;
            finderJob = null;
            ForgetLandingSpots(removeEntities: false);
            drawSessions.Clear();
            finderTargets.Clear();
            finderResults.Clear();
            finderResultEntities.Clear();
            finderLive.Clear();
            finderGoEntities.Clear();
            finderArmed.Clear();
            finderTracks.Clear();
            finderGoEntities.Clear();
            finderLive.Clear();
        }
    }
}
