using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Text.Json;
using System.Globalization;

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
        if (!matchStarted) return HookResult.Continue;

        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;

        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (player == null || !coaches.Contains(player)) return HookResult.Continue;

        // This player is a coach - immediately move them to their viewing position
        // This happens DURING the spawn event, preventing them from occupying a competitive spawn

        if (coachSpawns.Count == 0 ||
            !coachSpawns.ContainsKey((byte)CsTeam.CounterTerrorist) || coachSpawns[(byte)CsTeam.CounterTerrorist].Count == 0 ||
            !coachSpawns.ContainsKey((byte)CsTeam.Terrorist) || coachSpawns[(byte)CsTeam.Terrorist].Count == 0)
        {
            GetCoachSpawns();
        }

        if (coachSpawns.Count > 0 &&
            coachSpawns.TryGetValue(player.TeamNum, out List<Position>? coachTeamSpawns) &&
            coachTeamSpawns != null && coachTeamSpawns.Count > 0)
        {
            Random random = new();
            Position newPosition = coachTeamSpawns[random.Next(0, coachTeamSpawns.Count)];

            // Immediate teleport during spawn event
            AddTimer(0.01f, () =>
            {
                if (!IsPlayerValid(player) || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null) return;

                player.PlayerPawn.Value.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new Vector(0, 0, 0));

                // Setup coach properties
                SetPlayerInvisible(player: player, setWeaponsInvisible: false);
                player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
                player.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_NONE;

                HandleCoachWeapons(player);
                player.InGameMoneyServices!.Account = 0;

                // Ensure they stay there
                AddTimer(0.05f, () =>
                {
                    if (!IsPlayerValid(player) || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null) return;
                    player.PlayerPawn.Value.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new Vector(0, 0, 0));
                });

                Log($"[OnCoachPlayerSpawn] Moved coach {player.PlayerName} to viewing position during spawn");
            });
        }
        else
        {
            Log($"[OnCoachPlayerSpawn] WARNING: No valid coach spawn found for coach {player.PlayerName} (Team: {player.TeamNum}). Coach will spawn at default location!");
        }

        return HookResult.Continue;
    }

    public void HandleCoachCommand(CCSPlayerController? player, string side)
    {
        if (!IsPlayerValid(player)) return;
        if (isPractice)
        {
            ReplyToUserCommand(player, "Coach command can only be used in match mode!");
            return;
        }
        if (IsWingmanMode())
        {
            ReplyToUserCommand(player, "Coach command cannot be used in wingman!");
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
        if (player.InGameMoneyServices != null) player.InGameMoneyServices.Account = 0;
        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");
    }

    public void HandleCoaches()
    {
        coachKillTimer?.Kill();
        coachKillTimer = null;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;

        int freezeTime = ConVar.Find("mp_freezetime")!.GetPrimitiveValue<int>();
        freezeTime = freezeTime > 2 ? freezeTime : 2;
        coachKillTimer ??= AddTimer(freezeTime - 1f, KillCoaches);

        // Stats are now reset in the spawn event handler
        // Position is now set in the spawn event handler
        // Just handle team validation
        foreach (CCSPlayerController coach in coaches)
        {
            if (!IsPlayerValid(coach)) continue;
            AddTimer(0.5f, () => HandleCoachTeam(coach));
        }

        Log($"[HandleCoaches] Handled {coaches.Count} coach(es)");
    }

    private void MoveCoachToPosition(CCSPlayerController coach, Position position, string timing)
    {
        if (!IsPlayerValid(coach)) return;
        if (!coach.PlayerPawn.IsValid || coach.PlayerPawn.Value == null) return;

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
        if (!IsPlayerValid(coach)) return;
        coach.RemoveWeapons();
    }

    /// <summary>
    /// Transfers bomb from coach to first available non-coach terrorist.
    /// </summary> 
    public void TransferCoachBomb(CCSPlayerController coach)
    {
        if (coach.TeamNum != (int)CsTeam.Terrorist) return; // can't have bomb

        // find bomb and new target
        var bomb = coach.PlayerPawn.Value!.WeaponServices!.MyWeapons
            .Where(w => w != null && w.IsValid && w.Value!.DesignerName == "weapon_c4")
            .FirstOrDefault();
        if (bomb == null || bomb.Value == null) return; // should never trigger

        var target = Utilities.GetPlayers()
            .FirstOrDefault(
                p => IsPlayerValid(p)
                && !reverseTeamSides["TERRORIST"].coach.Contains(p)
                && p.TeamNum == (int)CsTeam.Terrorist
                && p.PawnIsAlive
            );
        if (!IsPlayerValid(target)) return; // should never trigger

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
        if (!IsPlayerValid(playerController)) return;
        
        CsTeam oldTeam = GetCoachTeam(playerController);
        if (playerController.Team != oldTeam)
        {
            playerController.ChangeTeam(CsTeam.Spectator);
            AddTimer(0.01f, () => 
            {
                // Re-validate player after timer - may have disconnected
                if (!IsPlayerValid(playerController)) return;
                playerController.ChangeTeam(oldTeam);
            });
        }
        if (playerController.InGameMoneyServices != null) playerController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        string suicidePenalty = GetConvarStringValue(ConVar.Find("mp_suicide_penalty"));
        string specFreezeTime = GetConvarStringValue(ConVar.Find("spec_freeze_time"));
        string specFreezeTimeLock = GetConvarStringValue(ConVar.Find("spec_freeze_time_lock"));
        string specFreezeDeathanim = GetConvarStringValue(ConVar.Find("spec_freeze_deathanim_time"));
        Server.ExecuteCommand("mp_suicide_penalty 0;spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        foreach (var coach in coaches)
        {
            if (!IsPlayerValid(coach)) continue;
            if (isPaused || IsTacticalTimeoutActive()) continue;
            
            // Additional safety check for pawn components
            if (!coach.PlayerPawn.IsValid || coach.PlayerPawn.Value == null ||
                coach.PlayerPawn.Value.CBodyComponent?.SceneNode == null)
                continue;

            Position coachPosition = new(coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin, coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsRotation);
            coach.PlayerPawn.Value.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
            coach.PlayerPawn.Value.CommitSuicide(explode: false, force: true);
        }
        Server.ExecuteCommand($"mp_suicide_penalty {suicidePenalty}; spec_freeze_time {specFreezeTime}; spec_freeze_time_lock {specFreezeTimeLock}; spec_freeze_deathanim_time {specFreezeDeathanim};");
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
