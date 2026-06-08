using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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
        if (!matchStarted)
            return HookResult.Continue;

        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;

        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (player == null || !coaches.Contains(player))
            return HookResult.Continue;

        // This player is a coach - immediately move them to their viewing position
        // This happens DURING the spawn event, preventing them from occupying a competitive spawn

        if (
            coachSpawns.Count == 0
            || !coachSpawns.ContainsKey((byte)CsTeam.CounterTerrorist)
            || coachSpawns[(byte)CsTeam.CounterTerrorist].Count == 0
            || !coachSpawns.ContainsKey((byte)CsTeam.Terrorist)
            || coachSpawns[(byte)CsTeam.Terrorist].Count == 0
        )
        {
            GetCoachSpawns();
        }

        if (
            coachSpawns.Count > 0
            && coachSpawns.TryGetValue(player.TeamNum, out List<Position>? coachTeamSpawns)
            && coachTeamSpawns != null
            && coachTeamSpawns.Count > 0
        )
        {
            // Deterministic per-coach assignment so multiple coaches on the same side never
            // collide on the same viewing position. The previous `new Random()` was seeded by
            // wall-clock time, so coaches spawning on the same tick drew identical indices and
            // stacked on top of each other.
            List<CCSPlayerController> sideCoaches = coaches
                .Where(c => IsPlayerValid(c) && c.TeamNum == player.TeamNum)
                .OrderBy(c => c.Slot)
                .ToList();
            int coachIdx = sideCoaches.IndexOf(player);
            if (coachIdx < 0)
                coachIdx = 0;

            // If a side has more coaches than configured viewing positions, wrap around the
            // position list and nudge each extra coach by an offset so they don't perfectly
            // overlap. Handles 1-5+ coaches per side without needing per-map JSON edits.
            Position basePosition = coachTeamSpawns[coachIdx % coachTeamSpawns.Count];
            int overflow = coachIdx / coachTeamSpawns.Count;
            Position newPosition = new(
                new Vector(
                    basePosition.PlayerPosition.X + overflow * 40.0f,
                    basePosition.PlayerPosition.Y,
                    basePosition.PlayerPosition.Z + overflow * 8.0f
                ),
                basePosition.PlayerAngle
            );

            // Immediate teleport during spawn event
            AddTimer(
                0.01f,
                () =>
                {
                    if (
                        !IsPlayerValid(player)
                        || !player.PlayerPawn.IsValid
                        || player.PlayerPawn.Value == null
                    )
                        return;

                    player.PlayerPawn.Value.Teleport(
                        newPosition.PlayerPosition,
                        newPosition.PlayerAngle,
                        new Vector(0, 0, 0)
                    );

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
                            if (
                                !IsPlayerValid(player)
                                || !player.PlayerPawn.IsValid
                                || player.PlayerPawn.Value == null
                            )
                                return;
                            player.PlayerPawn.Value.Teleport(
                                newPosition.PlayerPosition,
                                newPosition.PlayerAngle,
                                new Vector(0, 0, 0)
                            );
                        }
                    );

                    Log(
                        $"[OnCoachPlayerSpawn] Moved coach {player.PlayerName} to viewing position during spawn"
                    );
                }
            );
        }
        else
        {
            Log(
                $"[OnCoachPlayerSpawn] WARNING: No valid coach spawn found for coach {player.PlayerName} (Team: {player.TeamNum}). Coach will spawn at default location!"
            );
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
                ReplyToUserCommand(
                    player,
                    "Usage: .coach t or .coach ct (or join a team first and use .coach)"
                );
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
        ReplyToUserCommand(
            player,
            $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching"
        );
        PrintToAllChat(
            $"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!"
        );
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
        if (
            coachSpawns.Count == 0
            || coachSpawns[(byte)CsTeam.CounterTerrorist].Count == 0
            || coachSpawns[(byte)CsTeam.Terrorist].Count == 0
        )
        {
            Log($"[HandleCoaches] No coach spawns found, player positions will not be swapped!");
            return;
        }

        int freezeTime = ConVar.Find("mp_freezetime")!.GetPrimitiveValue<int>();
        freezeTime = freezeTime > 2 ? freezeTime : 2;
        coachKillTimer ??= AddTimer(freezeTime - 1f, KillCoaches);

        foreach (CCSPlayerController coach in coaches)
        {
            if (!IsPlayerValid(coach))
                continue;
            AddTimer(0.5f, () => HandleCoachTeam(coach));
        }

        // After coaches are relocated to their viewing spots, force the real (non-coach)
        // players onto the canonical competitive spawns. The game allocates spawn points to
        // ALL bodies on a team, coaches included, so coaches can grab the good min-priority
        // spawns and bump real players to far/wrong ones. Relocating coaches alone does not
        // fix the already-bumped players — this does, making coach count (1-5+) irrelevant.
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

        foreach (byte side in new[] { (byte)CsTeam.CounterTerrorist, (byte)CsTeam.Terrorist })
        {
            List<CCSPlayerController> realPlayers = Utilities
                .GetPlayers()
                .Where(p =>
                    IsPlayerValid(p)
                    && p.TeamNum == side
                    && !coaches.Contains(p)
                    && p.PawnIsAlive
                    && p.PlayerPawn.Value?.CBodyComponent?.SceneNode != null
                )
                .ToList();
            if (realPlayers.Count == 0)
                continue;

            List<Position> spawns = spawnsData[side];
            if (spawns.Count < realPlayers.Count)
            {
                // Overflow (more players than competitive spawns). We still seat as many as we
                // have spawns; leftover players stay put. Log it so it's visible during testing.
                Log(
                    $"[EnforceCompetitiveSpawns] Team {side}: {realPlayers.Count} players > {spawns.Count} spawns, seating nearest {spawns.Count}"
                );
            }

            // Each competitive spawn pulls in its nearest remaining player.
            foreach (Position spawn in spawns)
            {
                if (realPlayers.Count == 0)
                    break;

                Vector sp = spawn.PlayerPosition;
                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < realPlayers.Count; i++)
                {
                    Vector pos = realPlayers[i].PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                    float dx = sp.X - pos.X;
                    float dy = sp.Y - pos.Y;
                    float dz = sp.Z - pos.Z;
                    float dist = dx * dx + dy * dy + dz * dz;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = i;
                    }
                }
                if (best < 0)
                    break;

                new Position(spawn).Teleport(realPlayers[best]);
                realPlayers.RemoveAt(best); // claim the player so no spawn reuses it
            }
        }
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
            coach.PlayerPawn.Value.Teleport(
                position.PlayerPosition,
                position.PlayerAngle,
                new Vector(0, 0, 0)
            );

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
        var bomb = coach
            .PlayerPawn.Value!.WeaponServices!.MyWeapons.Where(w =>
                w != null && w.IsValid && w.Value!.DesignerName == "weapon_c4"
            )
            .FirstOrDefault();
        if (bomb == null || bomb.Value == null)
            return; // should never trigger

        var target = Utilities
            .GetPlayers()
            .FirstOrDefault(p =>
                IsPlayerValid(p)
                && !reverseTeamSides["TERRORIST"].coach.Contains(p)
                && p.TeamNum == (int)CsTeam.Terrorist
                && p.PawnIsAlive
            );
        if (!IsPlayerValid(target))
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
        if (isPaused || IsTacticalTimeoutActive())
            return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (coaches.Count == 0)
            return;
        // Capture the ConVar objects (not just their values) so we can mutate them
        // synchronously. Server.ExecuteCommand queues to the command buffer and runs at
        // frame-end, AFTER the CommitSuicide() calls below execute inline — so the old
        // ExecuteCommand("mp_suicide_penalty 0") never took effect before the suicides and
        // coaches still ate the suicide penalty. SetConvarValue writes the live cvar now.
        ConVar? suicidePenaltyCvar = ConVar.Find("mp_suicide_penalty");
        ConVar? specFreezeTimeCvar = ConVar.Find("spec_freeze_time");
        ConVar? specFreezeTimeLockCvar = ConVar.Find("spec_freeze_time_lock");
        ConVar? specFreezeDeathanimCvar = ConVar.Find("spec_freeze_deathanim_time");

        string suicidePenalty = GetConvarStringValue(suicidePenaltyCvar);
        string specFreezeTime = GetConvarStringValue(specFreezeTimeCvar);
        string specFreezeTimeLock = GetConvarStringValue(specFreezeTimeLockCvar);
        string specFreezeDeathanim = GetConvarStringValue(specFreezeDeathanimCvar);

        SetConvarValue(suicidePenaltyCvar, "0");
        SetConvarValue(specFreezeTimeCvar, "0");
        SetConvarValue(specFreezeTimeLockCvar, "0");
        SetConvarValue(specFreezeDeathanimCvar, "0");

        try
        {
            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach))
                    continue;
                if (isPaused || IsTacticalTimeoutActive())
                    continue;

                // Additional safety check for pawn components
                if (
                    !coach.PlayerPawn.IsValid
                    || coach.PlayerPawn.Value == null
                    || coach.PlayerPawn.Value.CBodyComponent?.SceneNode == null
                )
                    continue;

                Position coachPosition = new(
                    coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin,
                    coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsRotation
                );
                coach.PlayerPawn.Value.Teleport(
                    new Vector(
                        coachPosition.PlayerPosition.X,
                        coachPosition.PlayerPosition.Y,
                        coachPosition.PlayerPosition.Z + 20.0f
                    ),
                    coachPosition.PlayerAngle,
                    new Vector(0, 0, 0)
                );
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
        }
    }

    private void GetCoachSpawns()
    {
        coachSpawns = GetEmptySpawnsData();
        try
        {
            string spawnsConfigPath = Path.Combine(
                ModuleDirectory,
                "spawns",
                "coach",
                $"{Server.MapName}.json"
            );

            if (!File.Exists(spawnsConfigPath))
            {
                Log($"[GetCoachSpawns] Coach spawn file not found: {spawnsConfigPath}");
                return;
            }

            string spawnsConfig = File.ReadAllText(spawnsConfigPath);

            var jsonDictionary = JsonSerializer.Deserialize<
                Dictionary<string, List<Dictionary<string, string>>>
            >(spawnsConfig);
            if (jsonDictionary is null)
            {
                Log(
                    $"[GetCoachSpawns] Failed to deserialize coach spawns JSON for map {Server.MapName}"
                );
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
                        float x = float.Parse(
                            vectorArray[0].Replace(",", ""),
                            CultureInfo.InvariantCulture
                        );
                        float y = float.Parse(
                            vectorArray[1].Replace(",", ""),
                            CultureInfo.InvariantCulture
                        );
                        float z = float.Parse(
                            vectorArray[2].Replace(",", ""),
                            CultureInfo.InvariantCulture
                        );

                        float pitch = float.Parse(
                            angleArray[0].Replace(",", ""),
                            CultureInfo.InvariantCulture
                        );
                        float yaw = float.Parse(
                            angleArray[1].Replace(",", ""),
                            CultureInfo.InvariantCulture
                        );
                        float roll = float.Parse(
                            angleArray[2].Replace(",", ""),
                            CultureInfo.InvariantCulture
                        );

                        Vector vector = new(x, y, z);
                        QAngle qAngle = new(pitch, yaw, roll);

                        Position position = new(vector, qAngle);

                        positionList.Add(position);
                    }
                    catch (Exception ex)
                    {
                        Log(
                            $"[GetCoachSpawns] Error parsing position data for team {entry.Key}: {ex.Message}"
                        );
                    }
                }

                if (positionList.Count > 0)
                {
                    coachSpawns[team] = positionList;
                }
            }

            int totalSpawns =
                coachSpawns[(byte)CsTeam.CounterTerrorist].Count
                + coachSpawns[(byte)CsTeam.Terrorist].Count;
            if (totalSpawns > 0)
            {
                Log(
                    $"[GetCoachSpawns] Loaded {coachSpawns[(byte)CsTeam.CounterTerrorist].Count} CT and {coachSpawns[(byte)CsTeam.Terrorist].Count} T coach spawns for map {Server.MapName}"
                );
            }
            else
            {
                Log($"[GetCoachSpawns] No valid coach spawns found in {Server.MapName}.json");
            }
        }
        catch (Exception ex)
        {
            Log(
                $"[GetCoachSpawns] FATAL error loading coach spawns for map {Server.MapName}: {ex.Message}"
            );
        }
    }
}
