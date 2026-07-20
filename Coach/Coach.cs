using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public CounterStrikeSharp.API.Modules.Timers.Timer? coachKillTimer = null;

    public HashSet<CCSPlayerController> GetAllCoaches()
    {
        HashSet<CCSPlayerController> coaches = new(matchzyTeam1.coach);
        coaches.UnionWith(matchzyTeam2.coach);

        return coaches;
    }

    public HookResult OnCoachPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        // Debug mode runs the coach flow during warmup too (bot testing without a full match).
        if (!matchStarted && !coachDebugEnabled.Value)
            return HookResult.Continue;

        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;

        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (player == null || !coaches.Contains(player))
            return HookResult.Continue;

        // This player is a coach - immediately move them to their viewing position
        // This happens DURING the spawn event, preventing them from occupying a competitive spawn

        // Load this map's coach JSON once per map. The old "any side empty" guard reloaded every spawn
        // for single-side files AND never reloaded when both sides were saved - so a map change kept
        // the previous map's spots. Track the loaded map and reload only when it changes.
        if (_coachSpawnsLoadedMap != Server.MapName)
        {
            GetCoachSpawns();
            _coachSpawnsLoadedMap = Server.MapName;
        }

        // Deterministic per-coach index so multiple coaches on the same side never collide on the
        // same spot. (The previous new Random() was wall-clock seeded, so coaches spawning on the same
        // tick drew identical indices and stacked.)
        List<CCSPlayerController> sideCoaches = coaches.Where(c => IsPlayerValid(c) && c.TeamNum == player.TeamNum).OrderBy(c => c.Slot).ToList();
        int coachIdx = sideCoaches.IndexOf(player);
        if (coachIdx < 0)
            coachIdx = 0;

        // Priority (matchzy_coaching_mode): mode 1 = a hand-saved spawns/coach/<map>.json spot for this
        // side WINS (admins tune a problem map with .savecoachspawn, no recompile), else compute it;
        // mode 2 = always compute the spot behind the team, ignoring the JSON files. Compute works on
        // every map with no per-map file, clear of the players' spawns.
        bool useJsonSpots = coachingMode.Value != 2;
        Position? newPosition = null;
        if (useJsonSpots && coachSpawns.Count > 0 && coachSpawns.TryGetValue(player.TeamNum, out List<Position>? coachTeamSpawns) && coachTeamSpawns != null && coachTeamSpawns.Count > 0)
        {
            Position basePosition = coachTeamSpawns[coachIdx % coachTeamSpawns.Count];
            int overflow = coachIdx / coachTeamSpawns.Count;
            newPosition = new(new Vector(basePosition.PlayerPosition.X + overflow * 40.0f, basePosition.PlayerPosition.Y, basePosition.PlayerPosition.Z + overflow * 8.0f), basePosition.PlayerAngle);
        }
        else if (TryGetBehindTeamCoachSpawn(player.TeamNum, coachIdx, out Position behindPos))
        {
            newPosition = behindPos;
        }

        if (newPosition != null)
        {
            // Immediate teleport during spawn event
            AddTimer(
                0.01f,
                () =>
                {
                    if (!IsPlayerValid(player) || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null)
                        return;

                    // Freeze BEFORE teleporting: teleporting to an elevated coach spot while MoveType
                    // was still WALK let the pawn fall a frame and play an audible landing sound.
                    // MOVETYPE_NONE first = no fall = silent placement.
                    player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
                    player.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                    player.PlayerPawn.Value.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new Vector(0, 0, 0));

                    // Setup coach properties
                    SetPlayerInvisible(player: player, setWeaponsInvisible: false);
                    // The coach is an INVISIBLE body near the team - teammates spraying through its
                    // spot register team damage on it (and can kill it), which trips team-damage
                    // penalties / weird round endings. Make it untouchable.
                    player.PlayerPawn.Value.TakesDamage = false;

                    HandleCoachWeapons(player);
                    player.InGameMoneyServices!.Account = 0;

                    // Ensure they stay there
                    AddTimer(
                        0.05f,
                        () =>
                        {
                            if (!IsPlayerValid(player) || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null)
                                return;
                            player.PlayerPawn.Value.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new Vector(0, 0, 0));
                        }
                    );

                    Log($"[OnCoachPlayerSpawn] Moved coach {player.PlayerName} to viewing position during spawn");
                }
            );
        }
        else
        {
            Log($"[OnCoachPlayerSpawn] WARNING: No valid coach spawn found for coach {player.PlayerName} (Team: {player.TeamNum}). Coach will spawn at default location!");
        }

        return HookResult.Continue;
    }

    public void HandleCoachCommand(CCSPlayerController? player, string side)
    {
        if (!IsPlayerValid(player))
            return;
        if (isPractice)
        {
            ReplyToUserCommand(player, "Coach command can only be used in match mode!");
            return;
        }

        side = side.Trim().ToLower();

        // If no side specified, use player's current team
        if (string.IsNullOrEmpty(side))
        {
            if (player!.TeamNum == (byte)CsTeam.Terrorist)
            {
                side = "t";
            }
            else if (player!.TeamNum == (byte)CsTeam.CounterTerrorist)
            {
                side = "ct";
            }
            else
            {
                ReplyToUserCommand(player, "Usage: .coach t or .coach ct (or join a team first and use .coach)");
                return;
            }
        }

        if (side != "t" && side != "ct")
        {
            ReplyToUserCommand(player, "Usage: .coach t or .coach ct");
            return;
        }

        if (matchzyTeam1.coach.Contains(player!) || matchzyTeam2.coach.Contains(player!))
        {
            ReplyToUserCommand(player, "You are already coaching a team!");
            return;
        }

        Team matchZyCoachTeam;

        if (side == "t")
        {
            matchZyCoachTeam = reverseTeamSides["TERRORIST"];
        }
        else if (side == "ct")
        {
            matchZyCoachTeam = reverseTeamSides["CT"];
        }
        else
        {
            return;
        }

        matchZyCoachTeam.coach.Add(player!);
        player!.Clan = $"[{matchZyCoachTeam.teamName} COACH]";
        if (player.InGameMoneyServices != null)
            player.InGameMoneyServices.Account = 0;
        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");
    }

    public void HandleCoaches()
    {
        coachKillTimer?.Kill();
        coachKillTimer = null;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (coaches.Count == 0)
            return;
        if (spawnsData.Values.Any(list => list.Count == 0))
            GetSpawns();

        // Coach viewing-position file is OPTIONAL now. If present we relocate coaches to a
        // clean viewing spot; if absent, coaches just stay at their engine spawn until killed.
        // Either way the real-player reseat below still runs - that is what actually fixes
        // "one player doesn't get their normal spawn", and it needs only engine spawns.
        bool haveCoachSpawns = HasCoachSpawns();
        if (!haveCoachSpawns)
        {
            GetCoachSpawns();
            haveCoachSpawns = HasCoachSpawns();
        }

        int freezeTime = ConVar.Find("mp_freezetime")!.GetPrimitiveValue<int>();
        freezeTime = freezeTime > 2 ? freezeTime : 2;
        // Skip coach cleanup while debugging so coaches stay alive/visible for screenshots.
        if (!coachDebugEnabled.Value)
            coachKillTimer ??= AddTimer(freezeTime - 1f, KillCoaches);

        if (haveCoachSpawns)
        {
            foreach (CCSPlayerController coach in coaches)
            {
                if (!IsPlayerValid(coach))
                    continue;
                AddTimer(0.5f, () => HandleCoachTeam(coach));
            }
        }
        else
        {
            Log($"[HandleCoaches] No coach viewing spawns for map {Server.MapName}; coaches stay at engine spawn, but real players are still re-seated onto competitive spawns.");
        }

        // Always force the real (non-coach) players onto the canonical competitive spawns.
        // The game allocates spawn points to ALL bodies on a team, coaches included, so a
        // coach can grab a good spawn and bump a real player to a far/wrong one. Relocating
        // coaches alone does not fix the already-bumped player - this does, regardless of
        // coach count (1-5+) and regardless of whether a coach-spawn file exists.
        // Early pass at 0.2s so the coach-displaced player is restored before anyone registers the
        // wrong spawn; the 0.6s pass stays as an idempotent safety net (players already seated are
        // never touched, so running twice is free).
        AddTimer(0.2f, EnforceCompetitiveSpawns);
        AddTimer(0.6f, EnforceCompetitiveSpawns);

        Log($"[HandleCoaches] Handled {coaches.Count} coach(es)");
    }

    /// <summary>
    /// Re-seats non-coach players onto their team's canonical competitive spawns (the
    /// min-priority set from <see cref="GetSpawns"/>). Spawn-centric greedy: each competitive
    /// spawn claims its nearest unclaimed player, so every spawn gets filled and no two players
    /// share one. If a side has more players than competitive spawns (e.g. bot_quota fill while
    /// testing), the extra players are left where the engine put them rather than stacked.
    /// Only invoked when coaches are present, so coachless matches keep vanilla spawns.
    /// </summary>
    private void EnforceCompetitiveSpawns()
    {
        // Runs on an AddTimer callback: an escaped exception here becomes a CSS runtime error box.
        try
        {
            EnforceCompetitiveSpawnsCore();
        }
        catch (Exception e)
        {
            Log($"[EnforceCompetitiveSpawns] failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private void EnforceCompetitiveSpawnsCore()
    {
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        bool debug = coachDebugEnabled.Value;

        foreach (byte side in new[] { (byte)CsTeam.CounterTerrorist, (byte)CsTeam.Terrorist })
        {
            List<CCSPlayerController> realPlayers = Utilities.GetPlayers().Where(p => IsPlayerValid(p) && p.TeamNum == side && !coaches.Contains(p) && p.PawnIsAlive && p.PlayerPawn.Value?.CBodyComponent?.SceneNode != null).ToList();
            if (realPlayers.Count == 0)
                continue;

            // Pull the canonical first-N competitive spawns straight from the live map entities,
            // ordered by Priority - the exact set the engine would hand to N coachless players.
            // This does NOT depend on the min-priority `spawnsData` filter, which can come up
            // short on maps where the lowest-priority set is smaller than the team size and
            // leave a player stranded. Fall back to spawnsData only if the entity scan fails.
            // Fetch MORE candidates than players (+5): some maps enable more legit spawns than team
            // size (Mirage T has 10), and the engine freely seats players on any of them. With only
            // the strict top-N, a player standing on a perfectly fine spawn outside that subset was
            // force-moved EVERY round (observed: the same two bots re-teleported each round, even on
            // the coachless side). The stability pre-pass below now keeps anyone standing on ANY
            // valid candidate; the extra spawns only ever receive a displaced player as fallback.
            // ALL enabled spawns, not a capped pool: the engine seats players freely across every
            // enabled spawn entity (Mirage CT proved it - a bot sat 93u from the nearest of 11 pooled
            // candidates, i.e. on a spawn outside the pool, and was re-moved every round). The keep
            // pass must recognize every spawn the engine can use; the cap only ever limited that.
            List<Position> spawns = GetTopCompetitiveSpawns(side, 32);
            if (spawns.Count == 0)
                spawns = spawnsData.TryGetValue(side, out List<Position>? fallback) ? fallback : new List<Position>();
            if (spawns.Count == 0)
                continue;

            if (spawns.Count < realPlayers.Count)
            {
                // Overflow (more players than competitive spawns). We still seat as many as we
                // have spawns; leftover players stay put. Log it so it's visible during testing.
                Log($"[EnforceCompetitiveSpawns] Team {side}: {realPlayers.Count} players > {spawns.Count} spawns, seating nearest {spawns.Count}");
            }

            // STABILITY PRE-PASS: any player already standing ON a competitive spawn keeps it and is
            // NOT touched. The old flow teleported every player each round (even the ones the engine
            // had already seated correctly), which read as "our spawns get thrown around whenever a
            // coach is on". After this pass only the genuinely displaced player(s) remain.
            List<CCSPlayerController> remainingPlayers = new(realPlayers);
            List<Position> remainingSpawns = new(spawns);
            // Keep-tolerance MUST exceed the 64u near-duplicate dedupe in GetTopCompetitiveSpawns:
            // the engine can seat a player on a spawn entity that the dedupe dropped (its kept twin
            // is up to 64u away), and with a 40u tolerance that player read as "not on a spawn" and
            // was re-teleported EVERY round (observed on Mirage CT: the same source coordinate each
            // time). 75u > 64u closes that gap.
            const float keepDistSq = 75.0f * 75.0f;
            for (int pi = remainingPlayers.Count - 1; pi >= 0; pi--)
            {
                Vector pos = remainingPlayers[pi].PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                for (int si = 0; si < remainingSpawns.Count; si++)
                {
                    Vector sp = remainingSpawns[si].PlayerPosition;
                    float dx = sp.X - pos.X, dy = sp.Y - pos.Y, dz = sp.Z - pos.Z;
                    if (dx * dx + dy * dy + dz * dz <= keepDistSq)
                    {
                        // Already seated on this spawn - claim the pair, no teleport.
                        remainingPlayers.RemoveAt(pi);
                        remainingSpawns.RemoveAt(si);
                        break;
                    }
                }
            }

            // Diagnostics: whoever is still unmatched is about to be moved - log how far they were
            // from the nearest candidate so threshold/coverage gaps show up in one log line.
            if (debug)
            {
                foreach (var rp in remainingPlayers)
                {
                    Vector pos = rp.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                    float best = float.MaxValue;
                    foreach (var s in spawns)
                    {
                        float dx = s.PlayerPosition.X - pos.X, dy = s.PlayerPosition.Y - pos.Y, dz = s.PlayerPosition.Z - pos.Z;
                        best = Math.Min(best, dx * dx + dy * dy + dz * dz);
                    }
                    Log($"[CoachDebug] team {side}: {rp.PlayerName} unmatched - nearest candidate {(float)Math.Sqrt(best):0}u away ({spawns.Count} candidates)");
                }
            }

            // Remaining (displaced) players: bind the globally-closest (player, spawn) pairs. Beats a
            // spawn-centric greedy because the coach-bumped player snaps to the freed competitive slot
            // instead of staying at the far overflow spawn. N<=5 per side so O(N^3) is trivial.
            while (remainingPlayers.Count > 0 && remainingSpawns.Count > 0)
            {
                int bestP = -1,
                    bestS = -1;
                float bestDist = float.MaxValue;
                for (int pi = 0; pi < remainingPlayers.Count; pi++)
                {
                    Vector pos = remainingPlayers[pi].PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                    for (int si = 0; si < remainingSpawns.Count; si++)
                    {
                        Vector sp = remainingSpawns[si].PlayerPosition;
                        float dx = sp.X - pos.X;
                        float dy = sp.Y - pos.Y;
                        float dz = sp.Z - pos.Z;
                        float dist = dx * dx + dy * dy + dz * dz;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestP = pi;
                            bestS = si;
                        }
                    }
                }
                if (bestP < 0 || bestS < 0)
                    break;

                CCSPlayerController player = remainingPlayers[bestP];
                Position spawn = remainingSpawns[bestS];

                if (debug)
                {
                    Vector old = player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                    Log($"[CoachDebug] team {side}: {player.PlayerName} ({old.X:F0},{old.Y:F0},{old.Z:F0}) -> ({spawn.PlayerPosition.X:F0},{spawn.PlayerPosition.Y:F0},{spawn.PlayerPosition.Z:F0})");
                    PrintToAllChat($"{ChatColors.Yellow}[CoachDebug]{ChatColors.Default} reseated {ChatColors.Green}{player.PlayerName}{ChatColors.Default} (team {side})");
                }

                new Position(spawn).Teleport(player);
                remainingPlayers.RemoveAt(bestP);
                remainingSpawns.RemoveAt(bestS); // claim both so nothing is reused
            }
        }
    }

    /// <summary>
    /// True when a coach viewing-position file is currently loaded for both sides.
    /// </summary>
    private bool HasCoachSpawns()
    {
        return coachSpawns.Count > 0 && coachSpawns.TryGetValue((byte)CsTeam.CounterTerrorist, out List<Position>? ct) && ct.Count > 0 && coachSpawns.TryGetValue((byte)CsTeam.Terrorist, out List<Position>? t) && t.Count > 0;
    }

    /// <summary>
    /// Returns up to <paramref name="count"/> competitive spawns for a side, taken from the live
    /// map entities ordered by <c>Priority</c> ascending - i.e. the spawns the engine would assign
    /// to that many coachless players. Independent of the cached min-priority <c>spawnsData</c>.
    /// </summary>
    private List<Position> GetTopCompetitiveSpawns(byte side, int count)
    {
        string designerName = side == (byte)CsTeam.CounterTerrorist ? "info_player_counterterrorist" : "info_player_terrorist";
        // MATERIALIZE the native entity enumeration first and guard the whole scan: under the
        // AcceleratorCSS Harmony tracer, iterating the lazy enumerable inside a patched method throws
        // ArrayTypeMismatchException (same artifact the 0.8.57 .prac spawn fix addressed). On failure
        // return empty - callers fall back to spawnsData / the JSON coach spot.
        // ENTIRE body guarded: on dust2 the ArrayTypeMismatchException artifact (AcceleratorCSS
        // Harmony tracer) fired in the sort/pick section, which sat OUTSIDE the earlier scan-only
        // try and escaped into the reseat timer. Value-tuple list + List.Sort replaced with a plain
        // class list + manual insertion ordering, which the tracer tolerates.
        var candidates = new List<CoachSpawnCandidate>();
        try
        {
            var raw = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>(designerName).ToList();
            foreach (var s in raw)
            {
                if (s == null || !s.IsValid || !s.Enabled || s.CBodyComponent?.SceneNode == null)
                    continue;
                var cand = new CoachSpawnCandidate
                {
                    Priority = (int)s.Priority,
                    Index = s.Index,
                    Pos = new Position(s.CBodyComponent.SceneNode.AbsOrigin, s.CBodyComponent.SceneNode.AbsRotation),
                };
                // Insertion keeping (Priority, Index) order - deterministic, no List.Sort.
                int at = 0;
                while (at < candidates.Count
                       && (candidates[at].Priority < cand.Priority
                           || (candidates[at].Priority == cand.Priority && candidates[at].Index <= cand.Index)))
                    at++;
                candidates.Insert(at, cand);
            }
        }
        catch (Exception e)
        {
            Log($"[GetTopCompetitiveSpawns] scan failed (team {side}): {e.GetType().Name}: {e.Message}");
            return new List<Position>();
        }
        const float minSpawnGapSq = 64.0f * 64.0f;
        var picked = new List<Position>(count);
        try
        {
            foreach (var cand in candidates)
            {
                if (picked.Count >= count)
                    break;
                Position c = cand.Pos;
                bool tooClose = false;
                foreach (var p in picked)
                {
                    float dx = c.PlayerPosition.X - p.PlayerPosition.X;
                    float dy = c.PlayerPosition.Y - p.PlayerPosition.Y;
                    float dz = c.PlayerPosition.Z - p.PlayerPosition.Z;
                    if (dx * dx + dy * dy + dz * dz < minSpawnGapSq)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (!tooClose)
                    picked.Add(c);
            }
        }
        catch (Exception e)
        {
            Log($"[GetTopCompetitiveSpawns] pick failed (team {side}): {e.GetType().Name}: {e.Message}");
        }
        return picked;
    }

    private sealed class CoachSpawnCandidate
    {
        public int Priority;
        public uint Index;
        public Position Pos = null!;
    }

    /// <summary>
    /// Option B: compute a coach viewing position BEHIND the team's own spawns. Guarantees the coach
    /// never lands inside the 5 players' spawn cluster (so it can't bump/scramble their spawn points):
    /// it projects every team spawn onto the spawn-facing axis, takes the rear-most, and places the
    /// coach a margin further back + up, looking the way the team faces. coachIdx spreads multiple
    /// coaches sideways. Returns false if no team spawns were found (caller falls back to the JSON spot).
    /// </summary>
    private bool TryGetBehindTeamCoachSpawn(byte teamNum, int coachIdx, out Position result)
    {
        result = null!;
        try
        {
            return TryGetBehindTeamCoachSpawnCore(teamNum, coachIdx, out result);
        }
        catch (Exception e)
        {
            // Never let a placement computation escape into the spawn-event handler (an unguarded
            // ArrayTypeMismatch from the AcceleratorCSS tracer did exactly that). Fall back to the
            // JSON viewing spot.
            // Full stack: the tracer-induced ArrayTypeMismatch kept surviving section guards, and
            // Message-only logging made the throwing line unidentifiable.
            Log($"[TryGetBehindTeamCoachSpawn] failed (team {teamNum}): {e}");
            return false;
        }
    }

    private bool TryGetBehindTeamCoachSpawnCore(byte teamNum, int coachIdx, out Position result)
    {
        result = null!;
        // ALL enabled spawns (32-cap), not just 10: Ancient CT proved maps can enable more, and the
        // inside-cluster rejection below is only as good as the spawn list it checks against (a bot
        // seated on spawn #11 stood right in front of the coach).
        var spawns = GetTopCompetitiveSpawns(teamNum, 32);
        if (spawns.Count == 0)
            return false;

        float cx = 0, cy = 0, cz = 0, fx = 0, fy = 0;
        foreach (var s in spawns)
        {
            cx += s.PlayerPosition.X;
            cy += s.PlayerPosition.Y;
            cz += s.PlayerPosition.Z;
            double yaw = s.PlayerAngle.Y * Math.PI / 180.0;
            fx += (float)Math.Cos(yaw);
            fy += (float)Math.Sin(yaw);
        }
        int n = spawns.Count;
        cx /= n; cy /= n; cz /= n;

        // Average spawn-facing direction (the way the team looks out of spawn).
        float flen = (float)Math.Sqrt(fx * fx + fy * fy);
        if (flen < 0.0001f) { fx = 1; fy = 0; flen = 1; }
        fx /= flen; fy /= flen;

        // Rear-most spawn along that axis (most negative projection = furthest back).
        float minProj = 0;
        foreach (var s in spawns)
        {
            float proj = (s.PlayerPosition.X - cx) * fx + (s.PlayerPosition.Y - cy) * fy;
            if (proj < minProj) minProj = proj;
        }

        const float up = 90.0f;        // units above the floor for an overview
        const float pitch = 12.0f;     // downward look pitch (degrees)
        float yawDeg = (float)(Math.Atan2(fy, fx) * 180.0 / Math.PI);
        // Spread extra coaches sideways (perpendicular to the facing axis) so they don't stack.
        float rx = fy, ry = -fx;
        float spread = coachIdx * 55.0f;
        // Rear-most spawn point at eye height - the wall/void probe starts here.
        var rear = new Vector(cx + fx * minProj + rx * spread, cy + fy * minProj + ry * spread, cz + 64.0f);

#if HAS_CSS_TRACE
        // The naive "220u behind" can land OUTSIDE the world on maps whose spawn backs onto the map
        // edge (Mirage T: coach fell into the void with a black screen). Validate each candidate:
        // the path from the rear spawn to it must be clear (not through a wall), and there must be a
        // floor under it. Shrink the margin until a valid spot is found; give up -> caller falls back.
        try
        {
            var opts = new TraceOptions { InteractsWith = Masks.Solid };
            // Spawn-cluster eye point the coach must be able to SEE (LOS requirement below).
            var clusterEye = new Vector(cx, cy, cz + 64.0f);
            // Only meaningful stand-back distances: a 40-70u "behind" spot puts the coach nose-to-back
            // with the rear player (Ancient CT). If nothing >= 110u is clear, use the overhead camera.
            foreach (float margin in new[] { 220.0f, 160.0f, 110.0f })
            {
                var cand = new Vector(rear.X - fx * margin, rear.Y - fy * margin, rear.Z);
                // Wall probe: rear spawn -> candidate must not pass through solid.
                var wall = Trace.TraceEndShape(rear, cand, null, opts);
                if (wall.DidHit())
                    continue;
                // Floor probe: there must be ground beneath (void = no hit = outside the map).
                var floor = Trace.TraceEndShape(cand, new Vector(cand.X, cand.Y, cand.Z - 600.0f), null, opts);
                if (!floor.DidHit())
                    continue;
                // The floor behind can be a DIFFERENT level (Inferno: a terrace below the spawn with a
                // wall in between - the coach ended up staring at bricks). Require (a) the candidate
                // eye to be at least at the team's eye height (a spot on a LOWER level gives a view
                // through railings/over walls at best), and (b) clear line of sight back to the
                // cluster; otherwise try a shorter margin.
                var eyePos = new Vector(cand.X, cand.Y, floor.HitPoint.Z + up);
                if (eyePos.Z < clusterEye.Z - 16.0f)
                    continue;
                // Must stand CLEAR of the spawn cluster: on maps with radial spawn facings the
                // averaged "behind" direction can point back INTO the cluster (Ancient CT put the
                // coach at ground level nose-to-back with a bot). Require distance to the nearest
                // spawn; too close -> shorter margin won't help either, but the loop falls through
                // to the overhead fallback.
                bool insideCluster = false;
                foreach (var s in spawns)
                {
                    float ddx = cand.X - s.PlayerPosition.X, ddy = cand.Y - s.PlayerPosition.Y;
                    if (ddx * ddx + ddy * ddy < 90.0f * 90.0f)
                    {
                        insideCluster = true;
                        break;
                    }
                }
                if (insideCluster)
                    continue;
                var los = Trace.TraceEndShape(eyePos, clusterEye, null, opts);
                if (los.DidHit())
                    continue;
                if (coachDebugEnabled.Value)
                    Log($"[CoachPlace] team {teamNum}: BEHIND margin={margin:0} pos=({eyePos.X:0},{eyePos.Y:0},{eyePos.Z:0}) spawns={spawns.Count}");
                result = new Position(eyePos, new QAngle(pitch, yawDeg, 0.0f));
                return true;
            }

            // No behind-spot with a clear view: hover ABOVE the rear spawn looking down instead - the
            // rear spawn itself is guaranteed inside the world and has line of sight to the team.
            // A ceiling probe keeps the hover under covered spawns.
            float topZ = cz + 150.0f;
            var ceil = Trace.TraceEndShape(rear, new Vector(rear.X, rear.Y, cz + 200.0f), null, opts);
            if (ceil.DidHit())
                topZ = Math.Min(topZ, ceil.HitPoint.Z - 30.0f);
            var overheadPos = new Vector(rear.X, rear.Y, Math.Max(topZ, cz + 80.0f));
            if (coachDebugEnabled.Value)
                Log($"[CoachPlace] team {teamNum}: OVERHEAD pos=({overheadPos.X:0},{overheadPos.Y:0},{overheadPos.Z:0}) spawns={spawns.Count}");
            result = new Position(overheadPos, new QAngle(40.0f, yawDeg, 0.0f));
            return true;
        }
        catch
        {
            // Trace failure - fall through to the unvalidated fallback below.
        }
#endif
        // No trace API (or it failed): a short, safe hover just behind and above the rear spawn.
        result = new Position(new Vector(rear.X - fx * 40.0f, rear.Y - fy * 40.0f, cz + up), new QAngle(pitch, yawDeg, 0.0f));
        return true;
    }

    // ── .coachtest : solo debug of the coach placement ─────────────────────────────────────────
    // The real coach flow only runs on a SPAWN event (so .coach during warmup does nothing until a
    // respawn/round start, which makes solo testing awkward). .coachtest places YOU like a coach
    // right now: computes the behind-team spot for your current side, teleports + freezes +
    // invisible + undamageable - exactly the live placement path. Run again to release.
    private readonly HashSet<int> _coachTestActive = new();

    [ConsoleCommand("css_coachtest", "Debug: place yourself like a coach right now; run again to release")]
    public void OnCoachTestCommand(CCSPlayerController? player, CommandInfo? command)
    {
        if (!IsPlayerValid(player) || !player!.UserId.HasValue)
            return;
        if (!IsPlayerAdmin(player, "css_coachtest", "@css/map", "@custom/prac"))
        {
            SendPlayerNotAdminMessage(player);
            return;
        }
        int uid = player.UserId.Value;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null)
            return;

        if (_coachTestActive.Remove(uid))
        {
            // Release: visible, walkable, damageable again.
            SetPlayerVisible(player);
            pawn.MoveType = MoveType_t.MOVETYPE_WALK;
            pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
            pawn.TakesDamage = true;
            ReplyToUserCommand(player, "[CoachTest] released - you are a normal player again.");
            return;
        }

        if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
        {
            ReplyToUserCommand(player, "[CoachTest] join T or CT first.");
            return;
        }
        if (!TryGetBehindTeamCoachSpawn(player.TeamNum, 0, out Position spot))
        {
            ReplyToUserCommand(player, "[CoachTest] could not compute a behind-team spot (no team spawns found).");
            return;
        }
        MoveCoachToPosition(player, spot, "coachtest");
        _coachTestActive.Add(uid);
        ReplyToUserCommand(player, $"[CoachTest] placed at ({spot.PlayerPosition.X:0}, {spot.PlayerPosition.Y:0}, {spot.PlayerPosition.Z:0}) - run .coachtest again to release.");
    }

    private void MoveCoachToPosition(CCSPlayerController coach, Position position, string timing)
    {
        if (!IsPlayerValid(coach))
            return;
        if (!coach.PlayerPawn.IsValid || coach.PlayerPawn.Value == null)
            return;

        try
        {
            HandleCoachWeapons(coach);
            SetPlayerInvisible(player: coach, setWeaponsInvisible: false);

            // Lock movement
            coach.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
            coach.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            // Untouchable: teammates must not be able to damage/kill the invisible coach body.
            coach.PlayerPawn.Value.TakesDamage = false;

            // Teleport to viewing position
            coach.PlayerPawn.Value.Teleport(position.PlayerPosition, position.PlayerAngle, new Vector(0, 0, 0));

            // Reset velocity
            if (coach.PlayerPawn.Value.AbsVelocity != null)
            {
                coach.PlayerPawn.Value.AbsVelocity.X = 0;
                coach.PlayerPawn.Value.AbsVelocity.Y = 0;
                coach.PlayerPawn.Value.AbsVelocity.Z = 0;
            }
        }
        catch (Exception ex)
        {
            Log($"[MoveCoachToPosition] Error at {timing}: {ex.Message}");
        }
    }

    private void HandleCoachWeapons(CCSPlayerController coach)
    {
        if (!IsPlayerValid(coach))
            return;
        coach.RemoveWeapons();
    }

    /// <summary>
    /// Transfers bomb from coach to first available non-coach terrorist.
    /// </summary>
    public void TransferCoachBomb(CCSPlayerController coach)
    {
        if (coach.TeamNum != (int)CsTeam.Terrorist)
            return; // can't have bomb

        // find bomb and new target
        var bomb = coach.PlayerPawn.Value!.WeaponServices!.MyWeapons.Where(w => w != null && w.IsValid && w.Value!.DesignerName == "weapon_c4").FirstOrDefault();
        if (bomb == null || bomb.Value == null)
            return; // should never trigger

        var target = Utilities.GetPlayers().FirstOrDefault(p => IsPlayerValid(p) && !reverseTeamSides["TERRORIST"].coach.Contains(p) && p.TeamNum == (int)CsTeam.Terrorist && p.PawnIsAlive);
        if (!IsPlayerValid(target) || target == null)
            return; // should never trigger

        // transfer bomb
        bomb.Value!.Remove();
        target!.GiveNamedItem("weapon_c4");
    }

    public CsTeam GetCoachTeam(CCSPlayerController coach)
    {
        if (matchzyTeam1.coach.Contains(coach))
        {
            if (teamSides[matchzyTeam1] == "CT")
            {
                return CsTeam.CounterTerrorist;
            }
            else if (teamSides[matchzyTeam1] == "TERRORIST")
            {
                return CsTeam.Terrorist;
            }
        }
        if (matchzyTeam2.coach.Contains(coach))
        {
            if (teamSides[matchzyTeam2] == "CT")
            {
                return CsTeam.CounterTerrorist;
            }
            else if (teamSides[matchzyTeam2] == "TERRORIST")
            {
                return CsTeam.Terrorist;
            }
        }
        return CsTeam.Spectator;
    }

    private void HandleCoachTeam(CCSPlayerController playerController)
    {
        if (!IsPlayerValid(playerController))
            return;

        CsTeam oldTeam = GetCoachTeam(playerController);
        if (playerController.Team != oldTeam)
        {
            playerController.ChangeTeam(CsTeam.Spectator);
            AddTimer(
                0.01f,
                () =>
                {
                    // Re-validate player after timer - may have disconnected
                    if (!IsPlayerValid(playerController))
                        return;
                    playerController.ChangeTeam(oldTeam);
                }
            );
        }
        if (playerController.InGameMoneyServices != null)
            playerController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        // Debug mode keeps coaches alive for inspection - never suicide them.
        if (coachDebugEnabled.Value)
            return;
        if (isPaused || IsTacticalTimeoutActive())
            return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (coaches.Count == 0)
            return;
        // Capture the ConVar objects (not just their values) so we can mutate them
        // synchronously. Server.ExecuteCommand queues to the command buffer and runs at
        // frame-end, AFTER the CommitSuicide() calls below execute inline - so the old
        // ExecuteCommand("mp_suicide_penalty 0") never took effect before the suicides and
        // coaches still ate the suicide penalty. SetConvarValue writes the live cvar now.
        ConVar? suicidePenaltyCvar = ConVar.Find("mp_suicide_penalty");
        ConVar? specFreezeTimeCvar = ConVar.Find("spec_freeze_time");
        ConVar? specFreezeTimeLockCvar = ConVar.Find("spec_freeze_time_lock");
        ConVar? specFreezeDeathanimCvar = ConVar.Find("spec_freeze_deathanim_time");
        // Coach suicide makes the side momentarily shorthanded, so the engine hands the
        // opposing team "compensation" money ("An enemy player was awarded compensation for
        // the suicide of <coach>"). Zero the shorthanded bonuses across the suicides so the
        // coach removal never gifts the enemy economy, then restore them in finally.
        ConVar? shorthandedBonusCvar = ConVar.Find("cash_team_bonus_shorthanded");
        ConVar? shorthandedLoserBonusCvar = ConVar.Find("cash_team_loser_bonus_shorthanded");

        string suicidePenalty = GetConvarStringValue(suicidePenaltyCvar);
        string specFreezeTime = GetConvarStringValue(specFreezeTimeCvar);
        string specFreezeTimeLock = GetConvarStringValue(specFreezeTimeLockCvar);
        string specFreezeDeathanim = GetConvarStringValue(specFreezeDeathanimCvar);
        string shorthandedBonus = GetConvarStringValue(shorthandedBonusCvar);
        string shorthandedLoserBonus = GetConvarStringValue(shorthandedLoserBonusCvar);

        SetConvarValue(suicidePenaltyCvar, "0");
        SetConvarValue(specFreezeTimeCvar, "0");
        SetConvarValue(specFreezeTimeLockCvar, "0");
        SetConvarValue(specFreezeDeathanimCvar, "0");
        SetConvarValue(shorthandedBonusCvar, "0");
        SetConvarValue(shorthandedLoserBonusCvar, "0");

        try
        {
            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach))
                    continue;
                if (isPaused || IsTacticalTimeoutActive())
                    continue;

                // Additional safety check for pawn components
                if (!coach.PlayerPawn.IsValid || coach.PlayerPawn.Value == null || coach.PlayerPawn.Value.CBodyComponent?.SceneNode == null)
                    continue;

                Position coachPosition = new(coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin, coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsRotation);
                coach.PlayerPawn.Value.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
                coach.PlayerPawn.Value.CommitSuicide(explode: false, force: true);
            }
        }
        finally
        {
            // Restore originals synchronously, even if a suicide above threw.
            SetConvarValue(suicidePenaltyCvar, suicidePenalty);
            SetConvarValue(specFreezeTimeCvar, specFreezeTime);
            SetConvarValue(specFreezeTimeLockCvar, specFreezeTimeLock);
            SetConvarValue(specFreezeDeathanimCvar, specFreezeDeathanim);
            SetConvarValue(shorthandedBonusCvar, shorthandedBonus);
            SetConvarValue(shorthandedLoserBonusCvar, shorthandedLoserBonus);
        }
    }

    /// <summary>
    /// Admin tool: capture the caller's current position + view angle and append it as a coach
    /// viewing spot for the current map/side, then persist to spawns/coach/{map}.json. Lets server
    /// owners build the coach-spawn file in-game instead of editing JSON by hand. Side defaults to
    /// the caller's team; pass "t"/"ct" to override.
    /// </summary>
    [ConsoleCommand("css_savecoachspawn", "Save your current position as a coach viewing spawn for this map")]
    [CommandHelper(minArgs: 0, usage: "[t|ct]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSaveCoachSpawnCommand(CCSPlayerController? player, CommandInfo command)
    {
        HandleSaveCoachSpawnCommand(player, command.ArgCount > 1 ? command.GetArg(1) : "");
    }

    public void HandleSaveCoachSpawnCommand(CCSPlayerController? player, string sideArg)
    {
        if (!IsPlayerAdmin(player, "css_savecoachspawn", "@css/config"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command!");
            return;
        }
        if (!IsPlayerValid(player) || !player!.PlayerPawn.IsValid || player.PlayerPawn.Value?.CBodyComponent?.SceneNode == null)
        {
            ReplyToUserCommand(player, "You must be alive and in-game to save a coach spawn.");
            return;
        }

        // Resolve side: explicit arg wins, else caller's current team.
        string side = sideArg.Trim().ToLower();
        byte team;
        if (side == "t")
            team = (byte)CsTeam.Terrorist;
        else if (side == "ct")
            team = (byte)CsTeam.CounterTerrorist;
        else if (player.TeamNum == (byte)CsTeam.Terrorist || player.TeamNum == (byte)CsTeam.CounterTerrorist)
            team = player.TeamNum;
        else
        {
            ReplyToUserCommand(player, "Usage: .savecoachspawn t|ct (or join a team first)");
            return;
        }

        // Sync in-memory set with disk before editing (keeps the OTHER side's saved spot intact).
        if (!HasCoachSpawns())
            GetCoachSpawns();

        Vector origin = player.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin;
        // Save the VIEW angle (EyeAngles), not the body AbsRotation (pitch is always 0 on a standing
        // pawn) - so an elevated overview spot keeps its downward look when the coach is placed there.
        QAngle angle = player.PlayerPawn.Value.EyeAngles;

        // REPLACE this side's spot (one spot per side). Appending stacked duplicates: re-running to
        // adjust a spot left the old spot at index 0, which is exactly the one the coach then used.
        coachSpawns[team] = new List<Position> { new Position(origin, angle) };

        if (SaveCoachSpawnsFile())
        {
            string sideName = team == (byte)CsTeam.Terrorist ? "T" : "CT";
            ReplyToUserCommand(player, $"Saved {sideName} coach spot on {Server.MapName} ({origin.X:F0}, {origin.Y:F0}, {origin.Z:F0}). Verify with .showcoachspawns.");
            _coachSpawnsLoadedMap = "";   // force reload on next placement
        }
        else
        {
            ReplyToUserCommand(player, "Failed to write coach spawn file - check server logs.");
        }
    }

    /// <summary>
    /// Admin tool: delete every saved coach viewing spawn for the current map (both sides) and
    /// remove the on-disk file so you can rebuild from scratch.
    /// </summary>
    [ConsoleCommand("css_clearcoachspawns", "Clear all coach viewing spawns for this map")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnClearCoachSpawnsCommand(CCSPlayerController? player, CommandInfo command)
    {
        HandleClearCoachSpawnsCommand(player);
    }

    public void HandleClearCoachSpawnsCommand(CCSPlayerController? player)
    {
        if (!IsPlayerAdmin(player, "css_clearcoachspawns", "@css/config"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command!");
            return;
        }

        coachSpawns = GetEmptySpawnsData();
        _coachSpawnsLoadedMap = "";
        try
        {
            string path = Path.Combine(CoachSpawnsDir(), $"{Server.MapName}.json");
            if (File.Exists(path))
                File.Delete(path);
            ReplyToUserCommand(player, $"Cleared all coach spawns for {Server.MapName}.");
        }
        catch (Exception ex)
        {
            Log($"[OnClearCoachSpawnsCommand] Error deleting coach spawn file: {ex.Message}");
            ReplyToUserCommand(player, "Failed to delete coach spawn file - check server logs.");
        }
    }

    /// <summary>
    /// Admin tool: report how many coach viewing spawns are currently loaded for each side.
    /// </summary>
    [ConsoleCommand("css_listcoachspawns", "List loaded coach viewing spawns for this map")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnListCoachSpawnsCommand(CCSPlayerController? player, CommandInfo command)
    {
        HandleListCoachSpawnsCommand(player);
    }

    public void HandleListCoachSpawnsCommand(CCSPlayerController? player)
    {
        if (!IsPlayerAdmin(player, "css_listcoachspawns", "@css/config"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command!");
            return;
        }

        if (!HasCoachSpawns())
            GetCoachSpawns();

        int ct = coachSpawns.TryGetValue((byte)CsTeam.CounterTerrorist, out List<Position>? ctList) ? ctList.Count : 0;
        int t = coachSpawns.TryGetValue((byte)CsTeam.Terrorist, out List<Position>? tList) ? tList.Count : 0;
        ReplyToUserCommand(player, $"Coach spawns for {Server.MapName}: {ct} CT, {t} T.");
    }

    /// <summary>
    /// Serializes the in-memory <see cref="coachSpawns"/> set to spawns/coach/{map}.json in the
    /// same shape <see cref="GetCoachSpawns"/> reads back ({ teamNum: [ { Vector, QAngle } ] }).
    /// Returns true on success.
    /// </summary>
    private bool SaveCoachSpawnsFile()
    {
        try
        {
            string dir = CoachSpawnsDir();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{Server.MapName}.json");

            var outData = new Dictionary<string, List<Dictionary<string, string>>>();
            foreach (var entry in coachSpawns)
            {
                if (entry.Value == null || entry.Value.Count == 0)
                    continue;
                var positions = new List<Dictionary<string, string>>();
                foreach (Position pos in entry.Value)
                {
                    positions.Add(
                        new Dictionary<string, string>
                        {
                            ["Vector"] = string.Format(CultureInfo.InvariantCulture, "{0:F2} {1:F2} {2:F2}", pos.PlayerPosition.X, pos.PlayerPosition.Y, pos.PlayerPosition.Z),
                            ["QAngle"] = string.Format(CultureInfo.InvariantCulture, "{0:F2} {1:F2} {2:F2}", pos.PlayerAngle.X, pos.PlayerAngle.Y, pos.PlayerAngle.Z),
                        }
                    );
                }
                outData[entry.Key.ToString()] = positions;
            }

            string json = JsonSerializer.Serialize(outData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            Log($"[SaveCoachSpawnsFile] Wrote coach spawns for {Server.MapName} to {path}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[SaveCoachSpawnsFile] Error: {ex.Message}");
            return false;
        }
    }

    // The coach-spawns directory, resolved case-insensitively. ModuleDirectory is whatever casing the
    // plugin folder happens to have (MatchZy vs matchzy); on case-sensitive Linux a read written under
    // one casing would be invisible under the other. Prefer an EXISTING sibling dir that matches the
    // plugin-folder name case-insensitively (lowercase wins if both exist), so read + write always
    // agree. Cached per session.
    private string? _coachSpawnsDirCache;
    private string CoachSpawnsDir()
    {
        if (_coachSpawnsDirCache != null)
            return _coachSpawnsDirCache;
        string baseDir = ModuleDirectory;
        try
        {
            string parent = Path.GetDirectoryName(ModuleDirectory.TrimEnd('/', '\\')) ?? "";
            string leaf = Path.GetFileName(ModuleDirectory.TrimEnd('/', '\\'));
            if (Directory.Exists(parent))
            {
                var matches = Directory.GetDirectories(parent)
                    .Where(d => string.Equals(Path.GetFileName(d), leaf, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => Path.GetFileName(d))   // lowercase sorts after uppercase; take lowercase first below
                    .ToList();
                // Prefer an all-lowercase match ("matchzy") if present, else the DLL's own dir.
                var lower = matches.FirstOrDefault(d => Path.GetFileName(d) == leaf.ToLowerInvariant());
                baseDir = lower ?? (matches.Count > 0 ? matches[0] : ModuleDirectory);
            }
        }
        catch (Exception e)
        {
            Log($"[CoachSpawnsDir] resolve failed, using ModuleDirectory: {e.Message}");
        }
        _coachSpawnsDirCache = Path.Combine(baseDir, "spawns", "coach");
        return _coachSpawnsDirCache;
    }

    // Resolve the coach viewing spot for a side EXACTLY as OnCoachPlayerSpawn would (mode 1: JSON
    // override then computed; mode 2: always computed). Used by .showcoachspawns so what you see is
    // what the coach gets. Returns null if nothing resolves.
    private Position? ResolveCoachSpot(byte team)
    {
        // Always reload from disk (coachSpawns.Count is always 2 - both team keys are pre-created -
        // so a "Count == 0" guard never reloaded, and a hand-edited JSON never showed up). Cheap; this
        // is a debug/visualization path.
        GetCoachSpawns();
        if (coachingMode.Value != 2
            && coachSpawns.TryGetValue(team, out List<Position>? list) && list != null && list.Count > 0)
            return list[0];
        return TryGetBehindTeamCoachSpawn(team, 0, out Position p) ? p : null;
    }

    private readonly List<CBaseEntity> _coachSpawnViz = new();
    private bool _coachSpawnVizOn;
    // Map that coachSpawns was last loaded for (reload the JSON only on a map change). "" forces reload.
    private string _coachSpawnsLoadedMap = "";

    [ConsoleCommand("css_showcoachspawns", "Show the coach viewing spot for both sides in-world")]
    public void OnShowCoachSpawnsCommand(CCSPlayerController? player, CommandInfo? command)
    {
        if (!IsPlayerValid(player))
            return;
        if (!IsPlayerAdmin(player, "css_showcoachspawns", "@css/config"))
        {
            SendPlayerNotAdminMessage(player);
            return;
        }

        // Toggle off.
        if (_coachSpawnVizOn)
        {
            ClearCoachSpawnViz();
            ReplyToUserCommand(player, "Coach spawn markers hidden.");
            return;
        }

        int shown = 0;
        foreach (var (team, name, col) in new[]
        {
            ((byte)CsTeam.CounterTerrorist, "CT", System.Drawing.Color.DeepSkyBlue),
            ((byte)CsTeam.Terrorist, "T", System.Drawing.Color.Orange),
        })
        {
            Position? spot = ResolveCoachSpot(team);
            if (spot == null)
                continue;
            DrawCoachSpawnMarker(spot.PlayerPosition, spot.PlayerAngle, $"COACH {name}", col);
            shown++;
        }
        _coachSpawnVizOn = shown > 0;
        ReplyToUserCommand(player, shown > 0
            ? $"Showing {shown} coach spot(s). Run .showcoachspawns again to hide."
            : "No coach spot resolved for this map.");
    }

    private void DrawCoachSpawnMarker(Vector pos, QAngle ang, string label, System.Drawing.Color color)
    {
        // Tall vertical beam at the spot.
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam != null)
        {
            beam.LifeState = 1;
            beam.Width = 3.0f;
            beam.Render = color;
            beam.EndPos.X = pos.X;
            beam.EndPos.Y = pos.Y;
            beam.EndPos.Z = pos.Z + 72.0f;
            beam.Teleport(new Vector(pos.X, pos.Y, pos.Z), new QAngle(0, 0, 0), new Vector(0, 0, 0));
            beam.DispatchSpawn();
            _coachSpawnViz.Add(beam);
        }

        // Billboard label above it (same proven CPointWorldText setup as the grenade library markers).
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text != null)
        {
            text.MessageText = label;
            text.Color = color;
            text.FontName = "Arial Bold";
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bEnabled", true);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flFontSize", 60.0f);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flWorldUnitsPerPx", 0.25f);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bFullbright", true);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_bDrawBackground", true);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flBackgroundBorderWidth", 6.0f);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_flBackgroundBorderHeight", 4.0f);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyHorizontal", 1);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nJustifyVertical", 1);
            Schema.SetSchemaValue(text.Handle, "CPointWorldText", "m_nReorientMode", 1); // billboard
            text.Teleport(new Vector(pos.X, pos.Y, pos.Z + 90.0f), new QAngle(0, ang.Y, 90), new Vector(0, 0, 0));
            text.DispatchSpawn();
            _coachSpawnViz.Add(text);
        }
    }

    private void ClearCoachSpawnViz()
    {
        foreach (var e in _coachSpawnViz)
            if (e != null && e.IsValid)
                SafeRemoveEntity(e, "coachviz");
        _coachSpawnViz.Clear();
        _coachSpawnVizOn = false;
    }

    // On a map change the marker entities are gone but _coachSpawnVizOn was still true. If markers
    // were on, redraw them for the NEW map (after a delay so spawn entities exist); otherwise just
    // drop the stale handles.
    public void RefreshCoachSpawnVizOnMapStart()
    {
        _coachSpawnViz.Clear();   // handles from the old map are dead
        if (!_coachSpawnVizOn)
            return;
        _coachSpawnVizOn = false; // let the redraw re-set it
        AddTimer(2.0f, () =>
        {
            try
            {
                int shown = 0;
                foreach (var (team, name, col) in new[]
                {
                    ((byte)CsTeam.CounterTerrorist, "CT", System.Drawing.Color.DeepSkyBlue),
                    ((byte)CsTeam.Terrorist, "T", System.Drawing.Color.Orange),
                })
                {
                    Position? spot = ResolveCoachSpot(team);
                    if (spot == null)
                        continue;
                    DrawCoachSpawnMarker(spot.PlayerPosition, spot.PlayerAngle, $"COACH {name}", col);
                    shown++;
                }
                _coachSpawnVizOn = shown > 0;
            }
            catch (Exception e)
            {
                Log($"[CoachSpawnViz] map-start redraw: {e.Message}");
            }
        });
    }

    private void GetCoachSpawns()
    {
        coachSpawns = GetEmptySpawnsData();
        try
        {
            string spawnsConfigPath = Path.Combine(CoachSpawnsDir(), $"{Server.MapName}.json");

            if (!File.Exists(spawnsConfigPath))
            {
                Log($"[GetCoachSpawns] Coach spawn file not found: {spawnsConfigPath}");
                return;
            }

            string spawnsConfig = File.ReadAllText(spawnsConfigPath);

            var jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(spawnsConfig);
            if (jsonDictionary is null)
            {
                Log($"[GetCoachSpawns] Failed to deserialize coach spawns JSON for map {Server.MapName}");
                return;
            }

            foreach (var entry in jsonDictionary)
            {
                if (!byte.TryParse(entry.Key, out byte team))
                {
                    Log($"[GetCoachSpawns] Invalid team ID in JSON: {entry.Key}");
                    continue;
                }

                List<Position> positionList = new(64);

                foreach (var positionData in entry.Value)
                {
                    try
                    {
                        string[] vectorArray = positionData["Vector"].Split(' ');
                        string[] angleArray = positionData["QAngle"].Split(' ');

                        // Parse position and angle with Invariant culture to handle both "." and "," as decimal separators
                        // Also remove any remaining commas used as thousands separators
                        float x = float.Parse(vectorArray[0].Replace(",", ""), CultureInfo.InvariantCulture);
                        float y = float.Parse(vectorArray[1].Replace(",", ""), CultureInfo.InvariantCulture);
                        float z = float.Parse(vectorArray[2].Replace(",", ""), CultureInfo.InvariantCulture);

                        float pitch = float.Parse(angleArray[0].Replace(",", ""), CultureInfo.InvariantCulture);
                        float yaw = float.Parse(angleArray[1].Replace(",", ""), CultureInfo.InvariantCulture);
                        float roll = float.Parse(angleArray[2].Replace(",", ""), CultureInfo.InvariantCulture);

                        Vector vector = new(x, y, z);
                        QAngle qAngle = new(pitch, yaw, roll);

                        Position position = new(vector, qAngle);

                        positionList.Add(position);
                    }
                    catch (Exception ex)
                    {
                        Log($"[GetCoachSpawns] Error parsing position data for team {entry.Key}: {ex.Message}");
                    }
                }

                if (positionList.Count > 0)
                {
                    coachSpawns[team] = positionList;
                }
            }

            int totalSpawns = coachSpawns[(byte)CsTeam.CounterTerrorist].Count + coachSpawns[(byte)CsTeam.Terrorist].Count;
            if (totalSpawns > 0)
            {
                Log($"[GetCoachSpawns] Loaded {coachSpawns[(byte)CsTeam.CounterTerrorist].Count} CT and {coachSpawns[(byte)CsTeam.Terrorist].Count} T coach spawns for map {Server.MapName}");
            }
            else
            {
                Log($"[GetCoachSpawns] No valid coach spawns found in {Server.MapName}.json");
            }
        }
        catch (Exception ex)
        {
            Log($"[GetCoachSpawns] FATAL error loading coach spawns for map {Server.MapName}: {ex.Message}");
        }
    }
}
