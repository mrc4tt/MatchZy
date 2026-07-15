using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
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

        if (coachSpawns.Count == 0 || !coachSpawns.ContainsKey((byte)CsTeam.CounterTerrorist) || coachSpawns[(byte)CsTeam.CounterTerrorist].Count == 0 || !coachSpawns.ContainsKey((byte)CsTeam.Terrorist) || coachSpawns[(byte)CsTeam.Terrorist].Count == 0)
        {
            GetCoachSpawns();
        }

        if (coachSpawns.Count > 0 && coachSpawns.TryGetValue(player.TeamNum, out List<Position>? coachTeamSpawns) && coachTeamSpawns != null && coachTeamSpawns.Count > 0)
        {
            // Deterministic per-coach assignment so multiple coaches on the same side never
            // collide on the same viewing position. The previous `new Random()` was seeded by
            // wall-clock time, so coaches spawning on the same tick drew identical indices and
            // stacked on top of each other.
            List<CCSPlayerController> sideCoaches = coaches.Where(c => IsPlayerValid(c) && c.TeamNum == player.TeamNum).OrderBy(c => c.Slot).ToList();
            int coachIdx = sideCoaches.IndexOf(player);
            if (coachIdx < 0)
                coachIdx = 0;

            // If a side has more coaches than configured viewing positions, wrap around the
            // position list and nudge each extra coach by an offset so they don't perfectly
            // overlap. Handles 1-5+ coaches per side without needing per-map JSON edits.
            Position basePosition = coachTeamSpawns[coachIdx % coachTeamSpawns.Count];
            int overflow = coachIdx / coachTeamSpawns.Count;
            Position newPosition = new(new Vector(basePosition.PlayerPosition.X + overflow * 40.0f, basePosition.PlayerPosition.Y, basePosition.PlayerPosition.Z + overflow * 8.0f), basePosition.PlayerAngle);

            // Immediate teleport during spawn event
            AddTimer(
                0.01f,
                () =>
                {
                    if (!IsPlayerValid(player) || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null)
                        return;

                    player.PlayerPawn.Value.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new Vector(0, 0, 0));

                    // Setup coach properties
                    SetPlayerInvisible(player: player, setWeaponsInvisible: false);
                    player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
                    player.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_NONE;

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
            List<Position> spawns = GetTopCompetitiveSpawns(side, realPlayers.Count);
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

            // Optimal one-to-one assignment: repeatedly bind the globally-closest remaining
            // (player, spawn) pair. Beats the old spawn-centric greedy because the coach-bumped
            // player snaps back to the freed competitive slot instead of staying at the far
            // overflow spawn. N<=5 per side so the O(N^3) loop is trivial.
            List<CCSPlayerController> remainingPlayers = new(realPlayers);
            List<Position> remainingSpawns = new(spawns);
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
        return Utilities.FindAllEntitiesByDesignerName<SpawnPoint>(designerName).Where(s => s.IsValid && s.Enabled && s.CBodyComponent?.SceneNode != null).OrderBy(s => s.Priority).Take(count).Select(s => new Position(s.CBodyComponent!.SceneNode!.AbsOrigin, s.CBodyComponent.SceneNode.AbsRotation)).ToList();
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

        // Make sure the in-memory set reflects what's currently on disk before we append.
        if (!HasCoachSpawns())
            GetCoachSpawns();

        Vector origin = player.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin;
        QAngle angle = player.PlayerPawn.Value.CBodyComponent.SceneNode.AbsRotation;

        if (!coachSpawns.TryGetValue(team, out List<Position>? list) || list == null)
        {
            list = new List<Position>();
            coachSpawns[team] = list;
        }
        list.Add(new Position(origin, angle));

        if (SaveCoachSpawnsFile())
        {
            string sideName = team == (byte)CsTeam.Terrorist ? "T" : "CT";
            ReplyToUserCommand(player, $"Saved coach spawn #{list.Count} for {sideName} on {Server.MapName}.");
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
        try
        {
            string path = Path.Combine(ModuleDirectory, "spawns", "coach", $"{Server.MapName}.json");
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
            string dir = Path.Combine(ModuleDirectory, "spawns", "coach");
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

    private void GetCoachSpawns()
    {
        coachSpawns = GetEmptySpawnsData();
        try
        {
            string spawnsConfigPath = Path.Combine(ModuleDirectory, "spawns", "coach", $"{Server.MapName}.json");

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

                List<Position> positionList = new();

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
