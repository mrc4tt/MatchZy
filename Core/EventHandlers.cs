using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // A match from a match config is loaded, or is being loaded: a load whose first map differs
    // from the current one changes map first and only finishes (isMatchSetup) about a second after
    // the new map starts. A player joining in that window counts as joining a loaded match too.
    private bool IsMatchLoadedOrPending() => isMatchSetup || pendingMatchLoadJson != null;

    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            // Early validation - must be a connected human player with a UserId
            if (!IsHumanPlayerValid(player) || !player!.UserId.HasValue)
                return HookResult.Continue;

            int userId = player.UserId.Value;
            string steamId = player.SteamID.ToString();

            // Whitelist gate (css_whitelist): non-whitelisted players are kicked.
            if (HandlePlayerWhitelist(player, steamId))
            {
                KickPlayerDeferred(player);
                return HookResult.Continue;
            }

            // Match roster validation. matchModeOnly (matchzy_kick_when_no_match_loaded) kicks
            // anyone who is not on a loaded match's roster - with no match loaded that is
            // everyone, matching the convar's description. Admins are exempt so they can join
            // to manage the server. During plain match setup (convar off) a non-roster player
            // is left connected but untracked; team locking is enforced by the jointeam
            // listener and the EventPlayerTeam handler.
            if (isMatchSetup || matchModeOnly)
            {
                CsTeam team = GetPlayerTeam(player);
                if (team == CsTeam.None)
                {
                    if (matchModeOnly && !IsPlayerAdmin(player, "", "@css/config"))
                    {
                        Log($"[EventPlayerConnectFull] KICKING PLAYER STEAMID: {steamId}, Name: {player.PlayerName} (NOT ALLOWED!)");
                        PrintLocalizedToAll("matchzy.cmd.kicknotinmatch", player.PlayerName);
                        KickPlayerDeferred(player);
                        return HookResult.Continue;
                    }
                    SendPlayerConnectEvent(player);
                    return HookResult.Continue;
                }
            }

            SendPlayerConnectEvent(player);

            playerData[userId] = player;
            connectedPlayers++;

            if (isMatchSetup)
                AssignRosteredCoach(player);


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

                    // Do NOT touch isKnifeRequired here. It is set by SetMapSides() (match config
                    // map_sides), the mode commands (.match/.scrim/.hill), .knife and ResetMatch().
                    // Forcing it to true for every player who connected during warmup overrode a
                    // configured side (team1_ct/team2_ct/...) and started a knife round anyway, and
                    // did the same to scrim/hill and to an admin's .knife off.
                    // In a match loaded from a match config the mode hints (.scrim / .prac / .knife)
                    // do not apply; matchzy_loaded_match_hide_mode_hints keeps only the ready hint.
                    bool hideModeHints = IsMatchLoadedOrPending() && loadedMatchHideModeHints.Value;
                    if (!hideModeHints)
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.eh.warmup"));
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.eh.start"));
                    if (!hideModeHints)
                    {
                        PrintLocalizedToAdmins("matchzy.eh.prac");
                        PrintLocalizedToAdmins("matchzy.eh.knife");
                    }
                }
                else if (isPractice && !readyAvailable)
                {
                    PrintLocalizedToAdmins("matchzy.eh.quitprac");
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
                if (IsMatchLoadedOrPending() && loadedMatchHideModeHints.Value)
                    return;
                if (IsPlayerAdmin(player, "", "@css/config", "@css/map", "@custom/prac"))
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.adminjoinhint"));
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

            // Controller-level validation only. IsPlayerValid also requires
            // Connected == Connected and a valid pawn, which a leaving player (state
            // Disconnecting, or a spectator without a pawn) can fail - that skipped all the
            // cleanup below and left stale ready/playerData/coach entries behind.
            if (player == null || !player.IsValid || !player.UserId.HasValue)
                return HookResult.Continue;

            int userId = player.UserId.Value;

            if ((isMatchSetup || matchStarted) && !player.IsBot && !player.IsHLTV && UseGet5Events)
            {
                PublishEvent(new Get5PlayerDisconnectEvent
                {
                    MatchId = liveMatchId,
                    Player = Get5Player(player),
                    Reason = @event.Reason,
                });
            }
            else if ((isMatchSetup || matchStarted) && !player.IsBot && !player.IsHLTV)
            {
                var disconnectEvent = new MatchZyPlayerDisconnectedEvent
                {
                    MatchId = liveMatchId,
                    Player = userId,
                    PlayerSteamId = player.SteamID.ToString(),
                    PlayerName = player.PlayerName,
                    PlayerTeam = player.TeamNum switch { 2 => "T", 3 => "CT", 1 => "SPEC", _ => "none" },
                    Reason = @event.Reason,
                };
                PublishEvent(disconnectEvent);
            }

            // Practice orphaned-pawn cleanup: in practice the round is kept alive (buddha /
            // ignore_round_win_conditions), so a disconnecting human's pawn can linger as a
            // ghost collision body instead of being reaped. Capture its handle now and Kill
            // it next frame, but only if the handle still resolves to that same pawn (guards
            // against the engine having already freed it -> Remove on a dead entity crashes).
            if (isPractice && !player.IsBot && !player.IsHLTV && player.PlayerPawn?.IsValid == true && player.PlayerPawn.Value != null)
            {
                CCSPlayerPawn orphanPawn = player.PlayerPawn.Value;
                uint orphanRaw = orphanPawn.EntityHandle.Raw;
                Server.NextFrame(() =>
                {
                    // Entity-IO Kill, not Remove(): the pawn can still own networked weapons, and
                    // freeing those mid-tick is the WriteEnterPVS crash class.
                    if (orphanPawn.IsValid && orphanPawn.EntityHandle.Raw == orphanRaw)
                        orphanPawn.AcceptInput("Kill");
                });
            }

            if (playerReadyStatus.Remove(userId))
            {
                connectedPlayers--;
            }
            _readyStatusDirty = true;

            playerData.Remove(userId);
            infernoStartTimes.Remove(userId);

            // A veto captain left: hand the captaincy to a teammate and re-prompt the current step,
            // or abort the veto when the team has nobody left. The veto used to wait forever
            // (paused) for the missing captain.
            if (isVeto && !player.IsBot)
                HandleVetoCaptainLeft(userId);

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
            lastNadeMarkerUseTime.Remove(userId);
            // A practice bot kicked from outside (console, admin plugin) must not keep pinning
            // bot_quota; the engine would refill the slot with an untracked bot over and over.
            pracUsedBots.Remove(userId);
            _botsBeingProcessed.Remove(userId);
            lastNadeToggleTime.Remove(userId);
            _lastPanelHtml.Remove(userId);
            stopCommandCooldowns.Remove(player.SteamID);

            // Leak fix: a .timer repeating timer (0.2s REPEAT) keeps firing
            // DisplayPracticeTimerCenter(userId) forever if the player disconnects
            // mid-timer. Kill it + drop the dict entry. savedPlayerLocationData is
            // add-only otherwise → unbounded growth across churning players.
            if (playerTimers.Remove(userId, out var practiceTimer))
                practiceTimer.KillTimer();
            savedPlayerLocationData.Remove(userId);
            namedPlayerPositions.Remove(userId);
            flashTestList.Remove(userId);

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

    // CounterStrikeSharp throws "Global Variables not initialized yet" from Server.* properties
    // until the engine has its globals (before the first map is loaded).
    private static bool AreServerGlobalsReady()
    {
        try
        {
            _ = Server.MaxPlayers;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnMapEndHandler()
    {
        // OnMapEnd also fires while the server is still starting up (the boot map is shut down before
        // the first real map), when the engine's global variables do not exist yet. ResetMatch reads
        // players and Server.* and failed there with "Global Variables not initialized yet". Nothing
        // is loaded at that point, and OnMapStart / AutoStart set the state up on the real map.
        if (!AreServerGlobalsReady())
            return;

        // Before anything resets: receivers learn the map is changing (the map command's listener
        // already sent it when the change was requested by command).
        try { AnnounceMapEndIfUnannounced(); } catch (Exception e) { Log($"[map_change] {e.Message}"); }

        try
        {
            // If this changelevel was triggered by a match load whose map differs from the current
            // one (pendingMatchLoadJson set in LoadMatchFromJSON), capture the caller-set flags before
            // ResetMatch clears them, so the OnMapStart resume can restore them on the target map.
            if (pendingMatchLoadJson != null)
            {
                pendingMatchLoadIsG5 = isG5ApiMatch;
                pendingMatchLoadConfigFile = loadedConfigFile;
            }

            // A loaded match that is between maps must survive the changelevel: the next map of a
            // series (HandleMatchEnd) and the map picked in the veto (FinishVeto) both clear
            // matchStarted/isMatchLive and then change map, and OnMapStart resumes the match in
            // warmup. Resetting here wiped the series, the veto result and the team rosters, so map
            // 2 of a BO3 came up as an empty server. The same goes for a round restore that needs a
            // different map. A map change in the middle of a live map (another plugin, a plain
            // changelevel) still resets, since the match cannot continue from there.
            // isRoundRestorePending on its own too: a .match game (no match config, isMatchSetup
            // false) that restores a backup from another map was wiped here, pending restore included.
            if ((isMatchSetup && !matchStarted) || isRoundRestorePending)
            {
                Log($"[OnMapEndHandler] Keeping the loaded match across the map change (map {matchConfig.CurrentMapNumber + 1}/{matchConfig.NumMaps}, restorePending: {isRoundRestorePending})");
                KillPhaseTimers();
                // A match start that was in progress (ready-up done, database row being created) is
                // abandoned by the map change; the players ready up again on the new map. Leaving
                // the flag set would make HandleMatchStart return at once forever.
                matchStartInProgress = false;
                if (matchStarted)
                {
                    // Cross-map restore: the queued backup is applied by HandleMatchStart once the
                    // players ready up on the new map, which needs the match back in warmup.
                    matchStarted = false;
                    matchStartInProgress = false;
                    isMatchLive = false;
                    isKnifeRound = false;
                    isSideSelectionPhase = false;
                    isPaused = false;
                    isWarmup = true;
                    readyAvailable = true;
                }
                return;
            }

            // A map change in the middle of a loaded or started match (another plugin, a plain
            // changelevel) ends it: report it as cancelled so the panel and the database close it
            // instead of leaving it open forever. Not after a finished series: a panel may change
            // map before the post-series reset runs.
            ResetMatch(true, (matchStarted || isMatchSetup) && !seriesEnded ? "map_changed" : null);
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
            // A new round has no planted bomb: the cached site must not leak into this round.
            plantedBombSite = null;
            // Re-assert the bot team every round: follows halftime/overtime side swaps and
            // round restores, and refills bots the engine dropped.
            if (isMatchSetup)
                ApplyBotTeam();
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

            // Fallback: a coach still alive when freezetime ends means the scheduled freezetime
            // kill did not run (it is suppressed during a pause/tactical-timeout window). Kill
            // them now instead. The old upstream fallback bounced the coach through Spectator
            // (ChangeTeam Spectator -> back) - never do that: it is a ghosting window and a
            // visible team flicker.
            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach))
                    continue;
                if (coach.PlayerPawn.Value?.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                {
                    KillCoaches();
                    break;
                }
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
                    // Prefer m_vInitialVelocity: the engine sets it when it creates the projectile
                    // (the launch velocity), while AbsVelocity can still read ~0 one frame after spawn.
                    Vector velocity = new(projectile.InitialVelocity.X, projectile.InitialVelocity.Y, projectile.InitialVelocity.Z);
                    if (velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z < 2500f)
                        velocity = new(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z);
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
                    // Molotov/incendiary: track throw time + thrower per PROJECTILE (see OnEntityDeletedHandler).
                    if (nadeType == "molotov" || nadeType == "incendiary")
                        molotovProjectileThrows[(int)projIndex] = (DateTime.Now, client);
                    RegisterArcTrace(projIndex);
                    if (smokeColorEnabled.Value && nadeType == "smoke")
                    {
                        // Set the smoke tint ONE FRAME after spawn. Setting m_nSmokeColor synchronously
                        // in OnEntitySpawned ran before the projectile finished initializing, so the
                        // engine re-defaulted the color and the tint reverted to grey once the smoke
                        // bloomed (the "goes back to normal after a few seconds" bug). Deferring to
                        // NextFrame - as working colored-smoke plugins do - makes it stick for the
                        // smoke's whole lifetime.
                        var smokeCol = GetPlayerTeammateColor(player);
                        nint smokeHandle = entity.Handle;
                        Server.NextFrame(() =>
                        {
                            var sp = new CSmokeGrenadeProjectile(smokeHandle);
                            if (sp.Handle == IntPtr.Zero || !sp.IsValid)
                                return;
                            sp.SmokeColor.X = smokeCol.R;
                            sp.SmokeColor.Y = smokeCol.G;
                            sp.SmokeColor.Z = smokeCol.B;
                        });
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
                                {
                                    Log($"[GrenadeRecord] {nadeType} from userid {client} not recorded: the projectile was gone one frame after the throw.");
                                    return;
                                }
                                var o = ent2.AbsOrigin;
                                // (p1 - p0) per tick -> units/sec (CS2 default 64 tick).
                                Vector recovered = new((o.X - p0.X) * 64f, (o.Y - p0.Y) * 64f, (o.Z - p0.Z) * 64f);
                                // A zero-velocity record is useless: Throw() refuses it, so .rt would
                                // silently spawn nothing. Leave the previous history entry in place.
                                if (recovered.X * recovered.X + recovered.Y * recovered.Y + recovered.Z * recovered.Z < 2500f)
                                {
                                    Log($"[GrenadeRecord] {nadeType} from userid {client} not recorded: no launch velocity (dropped, not thrown).");
                                    return;
                                }
                                RecordThrownNade(client, nadeType, p0, angle, playerOrigin, eyeAngles, itemIndex, duckAmount, recovered, angularVelocity);
                            }
                            catch (Exception ex)
                            {
                                // Projectile detonated/freed between frames.
                                Log($"[GrenadeRecord] {nadeType} from userid {client} not recorded: {ex.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Entity was destroyed between frames.
                    Log($"[GrenadeRecord] A thrown grenade was not recorded: {ex.Message}");
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
            // Never broadcast a coach death to clients (no kill-feed entry in the corner).
            // Match on the VICTIM being a coach, not Attacker == Userid: the forced freezetime
            // suicide reports the world (null) as attacker, so the old suicide-only guard let
            // the death notice through.
            if (!matchStarted)
                return HookResult.Continue;

            CCSPlayerController? victim = @event.Userid;
            if (victim != null && (matchzyTeam1.coach.Contains(victim) || matchzyTeam2.coach.Contains(victim)))
            {
                info.DontBroadcast = true;
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
        // Detonation-time message is printed from EventInfernoStartburn (this event has no usable
        // entityid and fires even on a mid-air burst). Keep this handler only for the autoclear /
        // land-marker position.
        OnUtilityDetonated(@event.Get<float>("x"), @event.Get<float>("y"), @event.Get<float>("z"));
        return HookResult.Continue;
    }

    // Fire started on the ground: remember WHEN and by WHOM, and whether the fire came from an
    // incendiary (SourceItemDefIndex 48 - the TeamNum guess lied for picked-up nades). The print
    // itself happens in OnEntityDeletedHandler, which pairs this record with the projectile whose
    // deletion accompanies the burn - that is what makes overlapping double-molly times correct.
    public HookResult EventInfernoStartburnHandler(EventInfernoStartburn @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun)
            return HookResult.Continue;
        try
        {
            var inferno = Utilities.GetEntityFromIndex<CInferno>(@event.Entityid);
            var owner = inferno?.OwnerEntity?.Value;
            if (owner == null)
                return HookResult.Continue;
            var player = new CCSPlayerPawn(owner.Handle).OriginalController?.Value;
            if (!IsHumanPlayerValid(player) || !player!.UserId.HasValue)
                return HookResult.Continue;
            infernoStartTimes[player.UserId.Value] = (DateTime.Now, inferno!.SourceItemDefIndex == 48);
        }
        catch (Exception e)
        {
            Log($"[InfernoStartburn] {e.Message}");
        }
        return HookResult.Continue;
    }

    // A molotov/incendiary projectile entity dies the moment its fire starts (or when it fizzles in
    // the air). If its thrower had an inferno start within the last 0.2s, that fire belongs to this
    // projectile: print the flight time recorded at the throw. The check is deferred one frame
    // because the deletion can land before the startburn event of the same tick is processed.
    public void OnEntityDeletedHandler(CEntityInstance entity)
    {
        if (!isPractice || isDryRun)
            return;
        try
        {
            if (entity == null || !entity.IsValid)
                return;
            string designerName = entity.DesignerName;
            if (designerName != "molotov_projectile" && designerName != "incendiary_projectile")
                return;
            if (!molotovProjectileThrows.TryGetValue((int)entity.Index, out var thrown))
                return;
            molotovProjectileThrows.Remove((int)entity.Index);

            Server.NextFrame(() =>
            {
                var player = Utilities.GetPlayerFromUserid(thrown.Client);
                if (!IsHumanPlayerValid(player))
                    return;
                if (infernoStartTimes.TryGetValue(thrown.Client, out var burn) && (DateTime.Now - burn.Time).TotalSeconds < 0.2)
                {
                    string key = burn.IsIncendiary ? "matchzy.pracc.incendiary" : "matchzy.pracc.molotov";
                    PrintToPlayerChat(player!, Localizer.ForPlayer(player, key, player!.PlayerName, $"{(DateTime.Now - thrown.Time).TotalSeconds:0.00}"));
                }
            });
        }
        catch (Exception e)
        {
            Log($"[OnEntityDeleted] {e.Message}");
        }
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
                AddTimer(afterReadyDelay, () => CheckLiveRequired());
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
