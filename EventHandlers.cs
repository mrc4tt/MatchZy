using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public HookResult EventPlayerConnectFullHandler(
        EventPlayerConnectFull @event,
        GameEventInfo info
    )
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            // Early validation — must be a connected human player with a UserId
            if (!IsHumanPlayerValid(player) || !player!.UserId.HasValue)
                return HookResult.Continue;

            int userId = player.UserId.Value;

            // Whitelist/match validation
            if (isMatchSetup || matchModeOnly)
            {
                CsTeam team = GetPlayerTeam(player);
                if (team == CsTeam.None)
                {
                    return HookResult.Continue;
                }
            }

            playerData[userId] = player;
            connectedPlayers++;

            // Set ready status based on game state
            if (readyAvailable && !matchStarted)
            {
                playerReadyStatus[userId] = (matchConfig.MinPlayersToReady == -1);
            }
            else
            {
                playerReadyStatus[userId] = true;
            }
            _readyStatusDirty = true;

            // First player connection handling
            if (GetRealPlayersCount() == 1)
            {
                if (readyAvailable && !matchStarted)
                {
                    ExecUnpracCommands();
                    AutoStart();
                    isKnifeRequired = true;

                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.eh.warmup"));
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.eh.start"));
                    PrintToAdmins(Localizer.ForPlayer(player, "matchzy.eh.prac"));
                    PrintToAdmins(Localizer.ForPlayer(player, "matchzy.eh.knife"));
                }
                else if (isPractice && !readyAvailable)
                {
                    PrintToAdmins(Localizer.ForPlayer(player, "matchzy.eh.quitprac"));
                }
            }

            if (isMatchLive && autoPauseEnabled.Value)
            {
                AddTimer(1.0f, () => CheckAutoResumeOrAutoPause());
            }

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerConnectFull FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerDisconnectHandler(EventPlayerDisconnect @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            // Early validation
            if (!IsPlayerValid(player) || !player!.UserId.HasValue)
                return HookResult.Continue;

            int userId = player.UserId.Value;

            if (playerReadyStatus.Remove(userId))
            {
                connectedPlayers--;
            }
            _readyStatusDirty = true;

            playerData.Remove(userId);

            if (matchzyTeam1.coach.Remove(player) || matchzyTeam2.coach.Remove(player))
            {
                SetPlayerVisible(player);
                player.Clan = "";
            }

            // Cleanup practice mode data
            noFlashList.Remove(userId);
            lastGrenadesData.Remove(userId);
            nadeSpecificLastGrenadeData.Remove(userId);

            if (isMatchLive && autoPauseEnabled.Value)
            {
                AddTimer(1.0f, () => CheckAutoResumeOrAutoPause());
            }

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerDisconnect FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventCsWinPanelRoundHandler(EventCsWinPanelRound @event, GameEventInfo info)
    {
        // EventCsWinPanelRound has stopped firing after Arms Race update, hence we handle knife round winner in EventRoundEnd.

        // Log($"[EventCsWinPanelRound PRE] finalEvent: {@event.FinalEvent}");
        // if (isKnifeRound && matchStarted)
        // {
        //     HandleKnifeWinner(@event);
        // }
        return HookResult.Continue;
    }

    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        try
        {
            HandleMatchEnd();
            // isKnifeRequired is set explicitly by SetMapSides() / ResetMatch() — never toggle blindly
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventCsWinPanelMatch FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    private void OnMapEndHandler()
    {
        try
        {
            ResetMatch();
            // isKnifeRequired is set explicitly by ResetMatch() — never toggle blindly
        }
        catch (Exception e)
        {
            Log($"[OnMapEndHandler FATAL] An error occurred: {e.Message}");
        }
    }

    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
    {
        try
        {
            HandlePostRoundStartEvent(@event);
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundStart FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundFreezeEndHandler(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        try
        {
            if (!matchStarted)
                return HookResult.Continue;
            HashSet<CCSPlayerController> coaches = GetAllCoaches();

            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach))
                    continue;
                // If coaches are still left alive after freezetime ends, this code will force them to spectate their team again.
                if (coach.PlayerPawn.Value?.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    continue;

                // Safety check for coach pawn components
                if (coach.PlayerPawn.Value?.CBodyComponent?.SceneNode == null)
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
                AddTimer(
                    1.5f,
                    () =>
                    {
                        // Re-validate coach after timer delay - player may have disconnected
                        if (
                            !IsPlayerValid(coach)
                            || !coach.PlayerPawn.IsValid
                            || coach.PlayerPawn.Value == null
                        )
                            return;

                        coach.PlayerPawn.Value.Teleport(
                            new Vector(
                                coachPosition.PlayerPosition.X,
                                coachPosition.PlayerPosition.Y,
                                coachPosition.PlayerPosition.Z + 20.0f
                            ),
                            coachPosition.PlayerAngle,
                            new Vector(0, 0, 0)
                        );
                        CsTeam oldTeam = GetCoachTeam(coach);
                        coach.ChangeTeam(CsTeam.Spectator);
                        AddTimer(
                            0.01f,
                            () =>
                            {
                                // Re-validate for nested timer callback
                                if (!IsPlayerValid(coach))
                                    return;
                                coach.ChangeTeam(oldTeam);
                            }
                        );
                    }
                );
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundFreezeEnd FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerGivenC4(EventPlayerGivenC4 @event, GameEventInfo info)
    {
        try
        {
            if (!matchStarted)
                return HookResult.Continue;
            if (@event.Userid == null)
                return HookResult.Continue;
            var recv = @event.Userid;

            // check if coach
            var coaches = reverseTeamSides["TERRORIST"].coach;
            if (coaches.Contains(recv))
            {
                TransferCoachBomb(recv);
            }
        }
        catch (Exception e)
        {
            Log($"[EventPlayerGivenC4 FATAL] An error occured: {e.Message}");
        }
        return HookResult.Continue;
    }

    public void OnEntitySpawnedHandler(CEntityInstance entity)
    {
        try
        {
            if (!isPractice || entity == null || entity.Entity == null)
                return;
            if (!Constants.ProjectileTypeMap.ContainsKey(entity.Entity.DesignerName))
                return;

            Server.NextFrame(() =>
            {
                try
                {
                    // Verify entity is still valid before creating wrapper
                    if (entity == null || !entity.IsValid || entity.Handle == IntPtr.Zero)
                        return;

                    CBaseCSGrenadeProjectile projectile = new CBaseCSGrenadeProjectile(
                        entity.Handle
                    );

                    if (
                        !projectile.IsValid
                        || !projectile.Thrower.IsValid
                        || projectile.Thrower.Value == null
                        || projectile.Thrower.Value.Controller.Value == null
                        || projectile.Globalname == "custom"
                    )
                        return;

                    CCSPlayerController player = new(
                        projectile.Thrower.Value.Controller.Value.Handle
                    );
                    if (
                        !player.IsValid
                        || player.PlayerPawn.Value == null
                        || !player.PlayerPawn.IsValid
                    )
                        return;
                    var throwerSceneNode = player.PlayerPawn.Value.CBodyComponent?.SceneNode;
                    if (throwerSceneNode?.AbsOrigin == null)
                        return;
                    int client = player.UserId!.Value;

                    Vector position = new(
                        projectile.AbsOrigin!.X,
                        projectile.AbsOrigin.Y,
                        projectile.AbsOrigin.Z
                    );
                    QAngle angle = new(
                        projectile.AbsRotation!.X,
                        projectile.AbsRotation.Y,
                        projectile.AbsRotation.Z
                    );
                    Vector velocity = new(
                        projectile.AbsVelocity.X,
                        projectile.AbsVelocity.Y,
                        projectile.AbsVelocity.Z
                    );
                    string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];

                    if (!lastGrenadesData.ContainsKey(client))
                    {
                        lastGrenadesData[client] = new();
                    }

                    if (!nadeSpecificLastGrenadeData.ContainsKey(client))
                    {
                        nadeSpecificLastGrenadeData[client] = new() { };
                    }

                    GrenadeThrownData lastGrenadeThrown = new(
                        position,
                        angle,
                        velocity,
                        throwerSceneNode.AbsOrigin,
                        player.PlayerPawn.Value.EyeAngles,
                        nadeType,
                        DateTime.Now,
                        projectile.ItemIndex
                    );

                    nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
                    lastGrenadesData[client].Add(lastGrenadeThrown);

                    if (
                        maxLastGrenadesSavedLimit != 0
                        && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit
                    )
                    {
                        lastGrenadesData[client].RemoveAt(0);
                    }

                    lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
                    if (smokeColorEnabled.Value && nadeType == "smoke")
                    {
                        CSmokeGrenadeProjectile smokeProjectile = new(entity.Handle);
                        smokeProjectile.SmokeColor.X = GetPlayerTeammateColor(player).R;
                        smokeProjectile.SmokeColor.Y = GetPlayerTeammateColor(player).G;
                        smokeProjectile.SmokeColor.Z = GetPlayerTeammateColor(player).B;
                    }
                }
                catch (Exception)
                {
                    // Entity was destroyed between frames - silently ignore
                }
            });
        }
        catch (Exception e)
        {
            Log($"[OnEntitySpawnedHandler FATAL] An error occurred: {e.Message}");
        }
    }

    public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event, GameEventInfo info)
    {
        try
        {
            // We do not broadcast the suicide of the coach
            if (!matchStarted)
                return HookResult.Continue;

            if (@event.Attacker == @event.Userid)
            {
                if (
                    matchzyTeam1.coach.Contains(@event.Attacker!)
                    || matchzyTeam2.coach.Contains(@event.Attacker!)
                )
                {
                    info.DontBroadcast = true;
                }
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerDeathPreHandler FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventSmokegrenadeDetonateHandler(
        EventSmokegrenadeDetonate @event,
        GameEventInfo info
    )
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(
                player!,
                Localizer.ForPlayer(
                    player,
                    "matchzy.pracc.smoke",
                    player!.PlayerName,
                    $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"
                )
            );
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        return HookResult.Continue;
    }

    public HookResult EventFlashbangDetonateHandler(
        EventFlashbangDetonate @event,
        GameEventInfo info
    )
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(
                player!,
                Localizer.ForPlayer(
                    player,
                    "matchzy.pracc.flash",
                    player!.PlayerName,
                    $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"
                )
            );
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        return HookResult.Continue;
    }

    public HookResult EventHegrenadeDetonateHandler(
        EventHegrenadeDetonate @event,
        GameEventInfo info
    )
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(
                player!,
                Localizer.ForPlayer(
                    player,
                    "matchzy.pracc.grenade",
                    player!.PlayerName,
                    $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"
                )
            );
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        return HookResult.Continue;
    }

    public HookResult EventMolotovDetonateHandler(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Get<int>("entityid"), out var thrownTime))
        {
            PrintToPlayerChat(
                player!,
                Localizer.ForPlayer(
                    player,
                    "matchzy.pracc.molotov",
                    player!.PlayerName,
                    $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"
                )
            );
        }

        return HookResult.Continue;
    }

    public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(
                player!,
                Localizer.ForPlayer(
                    player,
                    "matchzy.pracc.decoy",
                    player!.PlayerName,
                    $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"
                )
            );
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        return HookResult.Continue;
    }

    public HookResult EventPlayerPingHandler(EventPlayerPing @event, GameEventInfo info)
    {
        try
        {
            // Only process pings during warmup when ready system is active
            if (!readyAvailable || matchStarted || !isWarmup)
                return HookResult.Continue;

            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player) || !player!.UserId.HasValue)
                return HookResult.Continue;

            int userId = player.UserId.Value;

            // Toggle ready status when player pings
            if (playerReadyStatus.TryGetValue(userId, out bool currentStatus))
            {
                // Toggle the ready status
                playerReadyStatus[userId] = !currentStatus;
                _readyStatusDirty = true;

                // Update the clan tag immediately
                HandleClanTags(forceUpdateSlot: player.Slot);

                // Show feedback to the player
                if (playerReadyStatus[userId])
                {
                    PrintToPlayerChat(
                        player,
                        Localizer.ForPlayer(player, "matchzy.ready.markedready")
                    );
                }
                else
                {
                    PrintToPlayerChat(
                        player,
                        Localizer.ForPlayer(player, "matchzy.ready.markedunready")
                    );
                }

                // Check if all players are ready to start the match
                AddTimer(afterReadyDelay, CheckLiveRequired);
            }

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerPingHandler FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }
}
