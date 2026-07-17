using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            // Early validation - must be a connected human player with a UserId
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
                    if (!isMatchSetup)
                    {
                        // OnMapStart's AutoStart may have run while the server was empty/
                        // hibernating, so the warmup exec never stuck. Re-arm the latch so
                        // AutoStart actually runs now that the first real player is in.
                        autoStartLatched = false;
                        AutoStart();
                    }

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

            // Discoverability: point admins at the help commands shortly after they join,
            // so they don't have to already know a command exists (recurring support ticket).
            // Admins only. Delayed so it lands after the join/connect chat spam; re-validate.
            AddTimer(4.0f, () =>
            {
                if (!IsHumanPlayerValid(player))
                    return;
                if (IsPlayerAdmin(player, "", "@css/config", "@css/map", "@custom/prac"))
                {
                    PrintToPlayerChat(player, $"{ChatColors.Gold}Admin:{ChatColors.Default} type {ChatColors.Green}.help{ChatColors.Default} for the current mode's commands, {ChatColors.Green}.mhelp{ChatColors.Default} for the full admin guide, {ChatColors.Green}.ma{ChatColors.Default} for the admin menu.");
                }
            });

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
            lastGrenadeBackCursor.Remove(userId);
            lastSpawnMarkerUseTime.Remove(userId);
            nadeSpecificLastGrenadeData.Remove(userId);

            // Leak fix: a .timer repeating timer (0.2s REPEAT) keeps firing
            // DisplayPracticeTimerCenter(userId) forever if the player disconnects
            // mid-timer. Kill it + drop the dict entry. savedPlayerLocationData is
            // add-only otherwise → unbounded growth across churning players.
            if (playerTimers.Remove(userId, out var practiceTimer))
                practiceTimer.KillTimer();
            savedPlayerLocationData.Remove(userId);
            namedPlayerPositions.Remove(userId);
            flashTestList.Remove(userId);
            lastPredictedLanding.Remove(userId);

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
            // isKnifeRequired is set explicitly by SetMapSides() / ResetMatch() - never toggle blindly
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
            // isKnifeRequired is set explicitly by ResetMatch() - never toggle blindly
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

                Position coachPosition = new(coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin, coach.PlayerPawn.Value.CBodyComponent.SceneNode.AbsRotation);
                coach.PlayerPawn.Value.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
                AddTimer(
                    1.5f,
                    () =>
                    {
                        // Re-validate coach after timer delay - player may have disconnected
                        if (!IsPlayerValid(coach) || !coach.PlayerPawn.IsValid || coach.PlayerPawn.Value == null)
                            return;

                        coach.PlayerPawn.Value.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
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

                    CBaseCSGrenadeProjectile projectile = new CBaseCSGrenadeProjectile(entity.Handle);

                    if (!projectile.IsValid || !projectile.Thrower.IsValid || projectile.Thrower.Value == null || projectile.Thrower.Value.Controller.Value == null || projectile.Globalname == "custom")
                        return;

                    CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                    if (!player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid)
                        return;
                    var throwerSceneNode = player.PlayerPawn.Value.CBodyComponent?.SceneNode;
                    if (throwerSceneNode?.AbsOrigin == null)
                        return;
                    int client = player.UserId!.Value;

                    Vector position = new(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z);
                    QAngle angle = new(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z);
                    Vector velocity = new(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z);
                    Vector angularVelocity = new(projectile.AngVelocity.X, projectile.AngVelocity.Y, projectile.AngVelocity.Z);
                    string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];

                    float duckAmount = 0.0f;
                    if (player.PlayerPawn.Value.MovementServices != null && player.PlayerPawn.Value.MovementServices.Handle != IntPtr.Zero)
                    {
                        duckAmount = new CCSPlayer_MovementServices(player.PlayerPawn.Value.MovementServices.Handle).DuckAmount;
                    }

                    Vector playerOrigin = new(throwerSceneNode.AbsOrigin.X, throwerSceneNode.AbsOrigin.Y, throwerSceneNode.AbsOrigin.Z);
                    QAngle eyeAngles = player.PlayerPawn.Value.EyeAngles;
                    ushort itemIndex = projectile.ItemIndex;
                    uint projIndex = projectile.Index;

                    lastGrenadeThrownTime[(int)projIndex] = DateTime.Now;
                    RegisterArcTrace(projIndex);
                    if (smokeColorEnabled.Value && nadeType == "smoke")
                    {
                        CSmokeGrenadeProjectile smokeProjectile = new(entity.Handle);
                        smokeProjectile.SmokeColor.X = GetPlayerTeammateColor(player).R;
                        smokeProjectile.SmokeColor.Y = GetPlayerTeammateColor(player).G;
                        smokeProjectile.SmokeColor.Z = GetPlayerTeammateColor(player).B;
                    }

                    // Capture the launch velocity. On current CS2 builds a freshly-spawned
                    // grenade's AbsVelocity can read ~0 (physics moves AbsOrigin but the velocity
                    // field lags a frame), so a normal mouse1 throw got stored with velocity 0 and
                    // Throw()'s zero-velocity guard silently dropped .rt / .throw. Trust AbsVelocity
                    // when it's clearly alive (>= 50 u/s); otherwise recover it from the projectile's
                    // position delta over the next frame (AbsOrigin is reliably live).
                    float velMagSq = velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z;
                    if (velMagSq >= 2500f)
                    {
                        RecordThrownNade(client, nadeType, position, angle, playerOrigin, eyeAngles, itemIndex, duckAmount, velocity, angularVelocity);
                    }
                    else
                    {
                        Vector p0 = new(position.X, position.Y, position.Z);
                        Server.NextFrame(() =>
                        {
                            try
                            {
                                var ent2 = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)projIndex);
                                if (ent2 == null || !ent2.IsValid || ent2.AbsOrigin == null)
                                    return;
                                var o = ent2.AbsOrigin;
                                // (p1 - p0) per tick -> units/sec (CS2 default 64 tick).
                                Vector recovered = new((o.X - p0.X) * 64f, (o.Y - p0.Y) * 64f, (o.Z - p0.Z) * 64f);
                                if (coachDebugEnabled.Value)
                                    Log($"[NadeRecord] AbsVelocity low ({System.Math.Sqrt(velMagSq):0}); recovered {recovered.X:0}/{recovered.Y:0}/{recovered.Z:0} from position delta");
                                RecordThrownNade(client, nadeType, p0, angle, playerOrigin, eyeAngles, itemIndex, duckAmount, recovered, angularVelocity);
                            }
                            catch (Exception)
                            {
                                // Projectile detonated/freed between frames - ignore.
                            }
                        });
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
                if (matchzyTeam1.coach.Contains(@event.Attacker!) || matchzyTeam2.coach.Contains(@event.Attacker!))
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

    public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pracc.smoke", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"));
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        if (player!.UserId.HasValue)
            CalibratePrediction(player.UserId.Value, @event.X, @event.Y, @event.Z);
        OnUtilityDetonated(@event.X, @event.Y, @event.Z);
        return HookResult.Continue;
    }

    public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsHumanPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pracc.flash", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"));
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        OnUtilityDetonated(@event.X, @event.Y, @event.Z);
        return HookResult.Continue;
    }

    public HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsHumanPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pracc.grenade", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"));
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        OnUtilityDetonated(@event.X, @event.Y, @event.Z);
        return HookResult.Continue;
    }

    public HookResult EventMolotovDetonateHandler(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsHumanPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Get<int>("entityid"), out var thrownTime))
        {
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pracc.molotov", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"));
        }

        OnUtilityDetonated(@event.Get<float>("x"), @event.Get<float>("y"), @event.Get<float>("z"));
        return HookResult.Continue;
    }

    public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsHumanPlayerValid(player))
            return HookResult.Continue;
        if (lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime))
        {
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pracc.decoy", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"));
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }

        OnUtilityDetonated(@event.Get<float>("x"), @event.Get<float>("y"), @event.Get<float>("z"));
        return HookResult.Continue;
    }

    public HookResult EventPlayerPingHandler(EventPlayerPing @event, GameEventInfo info)
    {
        try
        {
            // Only process pings during warmup when ready system is active
            if (!readyAvailable || matchStarted || !isWarmup)
                return HookResult.Continue;

            // Opt-out: some players ready up by accident when pinging. matchzy_ready_up_by_ping
            // false disables the ping->ready toggle (they use .ready / the ready panel instead).
            if (!readyUpByPing.Value)
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

                // Update the clan tag next frame (same-tick m_szClan set lags scoreboard)
                int slot = player.Slot;
                Server.NextFrame(() => HandleClanTags(forceUpdateSlot: slot));

                // Show feedback to the player
                if (playerReadyStatus[userId])
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.markedready"));
                }
                else
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.markedunready"));
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
