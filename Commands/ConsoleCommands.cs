using System.Reflection;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        private bool isMatchModeEnabled = false;

        [ConsoleCommand("css_whitelist", "Toggles Whitelisting of players")]
        [ConsoleCommand("css_wl", "Toggles Whitelisting of players")]
        public void OnWLCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_whitelist", "@css/config"))
            {
                isWhitelistRequired = !isWhitelistRequired;
                string WLStatus = isWhitelistRequired ? Localizer.ForPlayer(player, "matchzy.cc.enabled") : Localizer.ForPlayer(player, "matchzy.cc.disabled");
                if (player == null)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.wl", WLStatus));
                }
                else
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.wl", WLStatus));
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_save_nades_as_global", "Toggles Global Lineups for players")]
        [ConsoleCommand("css_globalnades", "Toggles Global Lineups for players")]
        public void OnSaveNadesAsGlobalCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_save_nades_as_global", "@css/config"))
            {
                isSaveNadesAsGlobalEnabled = !isSaveNadesAsGlobalEnabled;
                string GlobalNadesStatus = isSaveNadesAsGlobalEnabled ? Localizer.ForPlayer(player, "matchzy.cc.enabled") : Localizer.ForPlayer(player, "matchzy.cc.disabled");
                if (player == null)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.globalnades", GlobalNadesStatus));
                }
                else
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.globalnades", GlobalNadesStatus));
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_rc", "Check how many players are ready")]
        [ConsoleCommand("css_rcheck", "Check how many players are ready")]
        [ConsoleCommand("css_readycheck", "Check how many players are ready")]
        public void OnReadyCheckCommand(CCSPlayerController? player, CommandInfo? commandInfo)
        {
            if (readyAvailable && !matchStarted)
            {
                int perTeam = readyPerTeam.Value;
                int totalPlayers = perTeam > 0 ? perTeam * 2 : minimumReadyRequired;

                // Per-team mode: only up to N ready players per side count toward the start, and
                // spectators or extra ready players on one side must not hide a side that is missing.
                int readyPlayers = perTeam > 0
                    ? CountedReadyForSide(CsTeam.CounterTerrorist, perTeam) + CountedReadyForSide(CsTeam.Terrorist, perTeam)
                    : playerReadyStatus.Values.Count(status => status);
                int notReadyPlayers = totalPlayers - readyPlayers;

                if (notReadyPlayers < 0)
                    notReadyPlayers = 0; // safety check

                if (player != null)
                {
                    player.PrintToChat($" {Localizer.ForPlayer(player, "matchzy.cmd.readycheck.readyplayers", readyPlayers, totalPlayers)}");
                    player.PrintToChat($" {Localizer.ForPlayer(player, "matchzy.cmd.readycheck.waitingfor", notReadyPlayers)}");
                }
            }
            else
            {
                if (player != null)
                    player.PrintToChat($" {Localizer.ForPlayer(player, "matchzy.cmd.readycheck.unavailable")}");
            }
        }

        // Last short-handed hint per side after .ready, so a team readying up one by one is told once.
        private readonly Dictionary<int, DateTime> shortHandedHintAt = new();

        [ConsoleCommand("css_gaben", "Marks the player ready")]
        [ConsoleCommand("css_ready", "Marks the player ready")]
        public void OnPlayerReady(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;

            if (readyAvailable && !matchStarted)
            {
                if (player.UserId.HasValue)
                {
                    int userId = player.UserId.Value;
                    int team = player.TeamNum; // 2 = T, 3 = CT

                    if (!playerReadyStatus.ContainsKey(userId))
                        playerReadyStatus[userId] = false;

                    if (playerReadyStatus[userId])
                    {
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.markedready"));
                    }
                    else
                    {
                        playerReadyStatus[userId] = true;
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.markedready"));

                        // NEW: Check if this team is now ready
                        bool isTeamReady = IsTeamReady(team);
                        if (isTeamReady)
                        {
                            string teamName = team == 3 ? "CT" : "Terrorists";
                            PrintLocalizedToAll("matchzy.cmd.teamready", teamName);
                        }
                        else if (ShortHandedSide(team) != null
                            && (!shortHandedHintAt.TryGetValue(team, out DateTime last) || DateTime.UtcNow - last > TimeSpan.FromSeconds(15)))
                        {
                            // Everyone here may be ready while a rostered player is missing: say so.
                            shortHandedHintAt[team] = DateTime.UtcNow;
                            SendShortHandedHints(team);
                        }
                    }

                    AddTimer(afterReadyDelay, () => CheckLiveRequired());
                    _readyStatusDirty = true;
                    // Defer tag update: setting m_szClan on the same tick as the chat
                    // command dispatch loses a network race, so the scoreboard tag lags.
                    int slot = player.Slot;
                    Server.NextFrame(() => HandleClanTags(forceUpdateSlot: slot));
                    UnreadyHintMessageStart();
                }
            }
        }

        [ConsoleCommand("css_ur", "Marks the player unready")]
        [ConsoleCommand("css_unready", "Marks the player unready")]
        [ConsoleCommand("css_notready", "Marks the player unready")]
        public void OnPlayerUnReady(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;

            if (readyAvailable && !matchStarted)
            {
                if (player.UserId.HasValue)
                {
                    if (!playerReadyStatus.ContainsKey(player.UserId.Value))
                    {
                        playerReadyStatus[player.UserId.Value] = false;
                    }

                    if (!playerReadyStatus[player.UserId.Value])
                    {
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.markedunready"));
                    }
                    else
                    {
                        playerReadyStatus[player.UserId.Value] = false;
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.markedunready"));
                    }

                    _readyStatusDirty = true;
                    int slot = player.Slot;
                    Server.NextFrame(() => HandleClanTags(forceUpdateSlot: slot));
                    UnreadyHintMessageStart();
                }
            }
        }

        [ConsoleCommand("css_ct", "Choose CT side after knife round")]
        [ConsoleCommand(".ct", "Choose CT side after knife round")]
        public void OnChooseCT(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !isSideSelectionPhase)
                return;

            if (player.TeamNum == knifeWinner)
            {
                // Check if knife winner is already on CT side (CT is team 3 in CS2)
                if (knifeWinner == 3) // 3 = CT
                {
                    // They're already CT, so just stay
                    PrintLocalizedToAll("matchzy.knife.decidedtostay", knifeWinnerName);
                    StartLive();
                }
                else
                {
                    // They're on T side and want CT, so switch
                    Server.ExecuteCommand("mp_swapteams;");
                    SwapSidesInTeamData(true);
                    PrintLocalizedToAll("matchzy.knife.chosect", knifeWinnerName);
                    // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has chosen to play CT side!");
                    StartLive();
                }
            }
        }

        [ConsoleCommand("css_t", "Choose T side after knife round")]
        [ConsoleCommand(".t", "Choose T side after knife round")]
        public void OnChooseT(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !isSideSelectionPhase)
                return;

            if (player.TeamNum == knifeWinner)
            {
                // Check if knife winner is already on T side (T is team 2 in CS2)
                if (knifeWinner == 2) // 2 = T
                {
                    // They're already T, so just stay
                    PrintLocalizedToAll("matchzy.knife.decidedtostay", knifeWinnerName);
                    StartLive();
                }
                else
                {
                    // They're on CT side and want T, so switch
                    Server.ExecuteCommand("mp_swapteams;");
                    SwapSidesInTeamData(true);
                    PrintLocalizedToAll("matchzy.knife.choset", knifeWinnerName);
                    // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has chosen to play T side!");
                    StartLive();
                }
            }
        }

        [ConsoleCommand("css_stay", "Stays after knife round")]
        public void OnTeamStay(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !isSideSelectionPhase)
                return;

            if (player.TeamNum == knifeWinner)
            {
                SideSelectionTimer?.Kill();
                SideSelectionTimer = null;
                PrintLocalizedToAll("matchzy.knife.decidedtostay", knifeWinnerName);
                // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has decided to stay!");
                StartLive();
            }
        }

        [ConsoleCommand("css_switch", "Switch after knife round")]
        [ConsoleCommand("css_swap", "Switch after knife round")]
        public void OnTeamSwitch(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !isSideSelectionPhase)
                return;

            if (player.TeamNum == knifeWinner)
            {
                SideSelectionTimer?.Kill();
                SideSelectionTimer = null;
                Server.ExecuteCommand("mp_swapteams;");
                SwapSidesInTeamData(true);
                PrintLocalizedToAll("matchzy.knife.decidedtoswitch", knifeWinnerName);
                StartLive();
            }
        }

        [ConsoleCommand("css_t", "Switches team to Terrorist")]
        public void OnTCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || player.UserId == null)
                return;
            if (isVeto)
            {
                HandleSideChoice(CsTeam.Terrorist, player.UserId.Value);
                return;
            }

            if (isSideSelectionPhase && player.TeamNum == knifeWinner)
            {
                if (player.Team == CsTeam.Terrorist)
                {
                    OnTeamStay(player, command);
                }
                else
                {
                    OnTeamSwitch(player, command);
                }

                SideSelectionTimer?.Kill();
                SideSelectionTimer = null;
                return;
            }

            if (!isPractice)
                return;

            SideSwitchCommand(player, CsTeam.Terrorist);

            // Fjern droppede granater med delay
            AddTimer(
                0.2f,
                () =>
                {
                    CleanupDroppedGrenades();
                }
            );
        }

        [ConsoleCommand("css_ct", "Switches team to Counter-Terrorist")]
        public void OnCTCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || player.UserId == null)
                return;
            if (isVeto)
            {
                HandleSideChoice(CsTeam.CounterTerrorist, player.UserId.Value);
                return;
            }

            if (isSideSelectionPhase && player.TeamNum == knifeWinner)
            {
                if (player.Team == CsTeam.CounterTerrorist)
                {
                    OnTeamStay(player, command);
                }
                else
                {
                    OnTeamSwitch(player, command);
                }

                SideSelectionTimer?.Kill();
                SideSelectionTimer = null;
                return;
            }

            if (!isPractice)
                return;

            SideSwitchCommand(player, CsTeam.CounterTerrorist);

            // Fjern droppede granater med delay
            AddTimer(
                0.2f,
                () =>
                {
                    CleanupDroppedGrenades();
                }
            );
        }

        private void CleanupDroppedGrenades()
        {
            try
            {
                var grenadeNames = new[] { "weapon_flashbang", "weapon_hegrenade", "weapon_smokegrenade", "weapon_molotov", "weapon_incgrenade", "weapon_decoy" };

                foreach (var grenadeName in grenadeNames)
                {
                    var entities = Utilities.FindAllEntitiesByDesignerName<CBasePlayerWeapon>(grenadeName);

                    foreach (var entity in entities)
                    {
                        try
                        {
                            if (entity == null || !entity.IsValid)
                                continue;

                            // Tjek om entityen har en owner (ikke droppet)
                            var ownerEntity = entity.OwnerEntity;
                            if (ownerEntity != null && ownerEntity.IsValid && ownerEntity.Value != null)
                                continue;

                            // Entity-IO Kill, never Remove(): a dropped weapon is still networked, and
                            // freeing it mid-tick crashes the server ("WriteEnterPVS: GetEntServerClass
                            // failed"). Kill defers the delete to the engine's own safe point.
                            entity.AcceptInput("Kill");
                        }
                        catch
                        {
                            // Ignore errors on single entities
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[CleanupDroppedGrenades] Error: {ex.Message}");
            }
        }

        [ConsoleCommand("css_tech", "Pause the match")]
        public void OnTechCommand(CCSPlayerController? player, CommandInfo? command)
        {
            TechPause(player, command);
        }

        [ConsoleCommand("css_pause", "Pause the match")]
        [ConsoleCommand("css_p", "Pause the match")]
        public void OnPauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!allowPauseCommand.Value)
            {
                if (player != null)
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.pausedisabled"));
                return;
            }

            if (isPauseCommandForTactical)
            {
                OnTacCommand(player, command);
            }
            else
            {
                PauseMatch(player, command);
            }
        }

        [ConsoleCommand("css_fp", "Pause the match an admin")]
        [ConsoleCommand("css_forcepause", "Pause the match as an admin")]
        public void OnForcePauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            ForcePauseMatch(player, command);
        }

        [ConsoleCommand("css_fup", "Unpause the match an admin")]
        [ConsoleCommand("css_forceunpause", "Unpause the match as an admin")]
        public void OnForceUnpauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            ForceUnpauseMatch(player, command);
        }

        [ConsoleCommand("css_r", "Ready up before match or unpause during match")]
        public void OnRCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;

            // If match is live and paused, treat as unpause command
            if ((isMatchLive || isKnifeRound) && isPaused)
            {
                OnUnpauseCommand(player, command);
                return;
            }

            // If ready system is available and match hasn't started, treat as ready command
            if (readyAvailable && !matchStarted)
            {
                OnPlayerReady(player, command);
                return;
            }

            // If none of the above conditions are met, provide helpful feedback
            if (matchStarted && isMatchLive && !isPaused)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.r.livenotpaused"));
            }
            else if (!readyAvailable)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.r.readyunavailable"));
            }
            else
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.r.unavailable"));
            }
        }

        [ConsoleCommand("css_up", "Unpause the match")]
        [ConsoleCommand("css_unpause", "Unpause the match")]
        public void OnUnpauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            // matchzy_allow_unpause was registered but never checked. Only gate players: the
            // server console and internal callers (player == null) must still be able to unpause.
            if (player != null && !allowUnpauseCommand.Value)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.unpausedisabled"));
                return;
            }

            if ((isMatchLive || isKnifeRound) && isPaused)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin" && player != null)
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pause.onlyadmincanunpause"));
                    return;
                }

                // Get5 tech pause rules: the pausing team can cancel before it takes effect, and
                // either team can end it once its time is up.
                if (player != null && HandleGet5TechPauseUnpause(player))
                    return;

                string unpauseTeamName = "Admin";
                string remainingUnpauseTeam = "Admin";
                if (player?.TeamNum == 2)
                {
                    unpauseTeamName = reverseTeamSides["TERRORIST"].teamName;
                    remainingUnpauseTeam = reverseTeamSides["CT"].teamName;
                    if (!(bool)unpauseData["t"])
                    {
                        unpauseData["t"] = true;
                    }
                }
                else if (player?.TeamNum == 3)
                {
                    unpauseTeamName = reverseTeamSides["CT"].teamName;
                    remainingUnpauseTeam = reverseTeamSides["TERRORIST"].teamName;
                    if (!(bool)unpauseData["ct"])
                    {
                        unpauseData["ct"] = true;
                    }
                }
                else if (player != null)
                {
                    // A spectator or unassigned player cannot unpause. The server console (null)
                    // falls through as "Admin" and unpauses, as the comment above says.
                    return;
                }

                // The bot team cannot type .unpause; it always agrees.
                if (IsBotSide(2))
                    unpauseData["t"] = true;
                if (IsBotSide(3))
                    unpauseData["ct"] = true;

                if ((bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    // Both teams agreed: an auto-pause stays lifted even while a team is short.
                    AcceptShortHandedAfterAutoPause();
                    PrintLocalizedToAll("matchzy.pause.teamsunpausedthematch");
                    Server.ExecuteCommand("mp_unpause_match;");
                    CancelTechPauseTimer();
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;

                    // Send webhook for live scorebot
                    if (!string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                    {
                        var unpauseEvent = new MatchUnpausedLiveEvent
                        {
                            MatchId = liveMatchId,
                            MapNumber = matchConfig.CurrentMapNumber,
                            RoundNumber = GetRoundNumer(),
                        };
                        PublishEvent(unpauseEvent);
                    }
                }
                else if (unpauseTeamName == "Admin")
                {
                    AcceptShortHandedAfterAutoPause();
                    PrintLocalizedToAll("matchzy.pause.adminunpausedthematch");
                    Server.ExecuteCommand("mp_unpause_match;");
                    CancelTechPauseTimer();
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;

                    // Send webhook for live scorebot
                    if (!string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                    {
                        var unpauseEvent = new MatchUnpausedLiveEvent
                        {
                            MatchId = liveMatchId,
                            MapNumber = matchConfig.CurrentMapNumber,
                            RoundNumber = GetRoundNumer(),
                        };
                        PublishEvent(unpauseEvent);
                    }
                }
                else
                {
                    PrintLocalizedToAll("matchzy.pause.teamwantstounpause", unpauseTeamName, remainingUnpauseTeam);
                }

                if (!isPaused && pausedStateTimer != null)
                {
                    pausedStateTimer.Kill();
                    pausedStateTimer = null;
                }
            }
        }

        [ConsoleCommand("css_tac", "Starts a tactical timeout for the requested team")]
        public void OnTacCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;

            if (matchStarted && isMatchLive)
            {
                if (isPaused)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.matchpaused"));
                    return;
                }

                var gameRules = GetGameRules();
                if (gameRules == null)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.gamerulesfailed"));
                    return;
                }
                if (player.TeamNum == 2)
                {
                    if (gameRules.TerroristTimeOuts > 0)
                    {
                        Server.ExecuteCommand("timeout_terrorist_start");
                    }
                    else
                    {
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.nomorepauses"));
                    }
                }
                else if (player.TeamNum == 3)
                {
                    if (gameRules.CTTimeOuts > 0)
                    {
                        Server.ExecuteCommand("timeout_ct_start");
                    }
                    else
                    {
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.nomorepauses"));
                    }
                }
            }
        }

        [ConsoleCommand("css_skipveto", "Skips the current veto phase")]
        [ConsoleCommand("css_sv", "Skips the current veto phase")]
        public void OnSkipVetoCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_skipveto", "@css/config"))
            {
                if (matchStarted)
                {
                    if (player == null)
                    {
                        // ReplyToUserCommand(player, $"Skip veto command cannot be used if match has already started!");
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.skipvetomatchstarted"));
                    }
                    else
                    {
                        // player.PrintToChat($"{chatPrefix} Skip veto command cannot be used if match has already started!");
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.skipvetomatchstarted"));
                    }
                }
                else
                {
                    SkipVeto();
                    if (player == null)
                    {
                        // ReplyToUserCommand(player, $"Veto phase has been cancelled!");
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.skipveto"));
                    }
                    else
                    {
                        // player.PrintToChat($"{chatPrefix} Veto phase has been cancelled!");
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.skipveto"));
                    }
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_roundknife", "Toggles knife round for the match")]
        [ConsoleCommand("css_rk", "Toggles knife round for the match")]
        [ConsoleCommand("css_kr", "Toggles knife round for the match")]
        [ConsoleCommand("css_kniferound", "Toggles knife round for the match")]
        public void OnKnifeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;

            if (IsPlayerAdmin(player, "css_roundknife", "@css/config"))
            {
                isKnifeRequired = !isKnifeRequired;
                string knifeStatus = isKnifeRequired ? Localizer.ForPlayer(player, "matchzy.cc.enabled") : Localizer.ForPlayer(player, "matchzy.cc.disabled");
                if (player == null)
                {
                    // ReplyToUserCommand(player, $"Knife round is now {knifeStatus}!");
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.roundknife", knifeStatus));
                }
                else
                {
                    // player.PrintToChat($"{chatPrefix} Knife round is now {ChatColors.Green}{knifeStatus}{ChatColors.Default}!");
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.roundknife", knifeStatus));
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_teamsize", "Sets number of ready players required to start the match")]
        [ConsoleCommand("css_readyrequired", "Sets number of ready players required to start the match")]
        public void OnReadyRequiredCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (IsPlayerAdmin(player, "css_readyrequired", "@css/config"))
            {
                if (command.ArgCount >= 2)
                {
                    string commandArg = command.ArgByIndex(1);
                    HandleReadyRequiredCommand(player, commandArg);
                }
                else
                {
                    string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.readyrequired.current", minimumReadyRequiredFormatted));
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_options", "Shows the current match configuration/settings")]
        [ConsoleCommand("css_settings", "Shows the current match configuration/settings")]
        [ConsoleCommand("css_configs", "Show match configuration/settings")]
        public void OnMatchSettingsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;

            if (IsPlayerAdmin(player, "css_settings", "@css/config"))
            {
                string knifeStatus = isKnifeRequired ? Localizer.ForPlayer(player, "matchzy.cc.enabled") : Localizer.ForPlayer(player, "matchzy.cc.disabled");
                string playoutStatus = isPlayOutEnabled ? Localizer.ForPlayer(player, "matchzy.cc.enabled") : Localizer.ForPlayer(player, "matchzy.cc.disabled");
                string matchModeStatus = isMatchModeEnabled ? Localizer.ForPlayer(player, "matchzy.cc.enabled") : Localizer.ForPlayer(player, "matchzy.cc.disabled");

                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.currentsettings"));
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.knifestatus", knifeStatus));
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.settings.matchmode", matchModeStatus));
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cmd.settings.scrimmode", playoutStatus));
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("get5_endmatch", "Ends resets the current match")]
        [ConsoleCommand("css_forceend", "Ends the match through the normal match-end path (same as .forceend and get5_endmatch)")]
        public void OnEndMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "get5_endmatch", "@css/config"))
            {
                if (!isPractice)
                {
                    // Get5 semantics (what G5API's "cancel match" / forfeit sends):
                    //   get5_endmatch          -> cancel the match, no winner
                    //   get5_endmatch team1|2  -> end the series with that team as the winner
                    // It used to run the normal map-end path, which did nothing outside a live map
                    // (the server stayed locked on the old match) and, mid-map, scored the map for
                    // whichever team was ahead and moved a series on to the next map.
                    string winnerArg = command != null && command.ArgCount >= 2 ? command.ArgByIndex(1).Trim().ToLowerInvariant() : "";
                    bool matchActive = matchStarted || isMatchSetup || isVeto || isPreVeto;
                    if (!matchActive)
                    {
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.nomatchloaded"));
                        return;
                    }
                    if (seriesEnded)
                    {
                        // Ending or cancelling again would send a second series_end / match_cancelled
                        // and overwrite the winner the series was decided with.
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.seriesended"));
                        return;
                    }

                    if (winnerArg == "team1" || winnerArg == "team2")
                    {
                        Team winTeam = winnerArg == "team1" ? matchzyTeam1 : matchzyTeam2;
                        PrintLocalizedToAll("matchzy.cmd.adminendedmatchwinner", winTeam.teamName);
                        EndSeriesWithWinner(winTeam);
                    }
                    else
                    {
                        PrintLocalizedToAll("matchzy.cmd.admincancelledmatch");
                        ResetMatch(true, "ended_early");
                    }
                }
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.endmatchinpractice"));
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_stopgame", "Ends and resets match")]
        [ConsoleCommand("css_stopmatch", "Ends and resets match")]
        [ConsoleCommand("css_endgame", "Ends and resets match")]
        [ConsoleCommand("css_forcestop", "Ends and resets match")]
        [ConsoleCommand("css_endmatch", "Ends and resets match")]
        [ConsoleCommand("css_end", "Ends and resets match")]
        [ConsoleCommand("css_exitscrim", "Ends and resets match")]
        public void OnStopMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_endmatch", "@css/config"))
            {
                // Block end/reset during endscreen/post-game to avoid instability
                if (IsPostGamePhase())
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchended"));
                    return;
                }

                // Stop must work in EVERY pre-live and live state, not just isMatchLive. A match set up
                // via .match / .matchsetup sits in setup / veto / warmup / knife (matchStarted but not
                // yet isMatchLive) - the old `matchStarted && isMatchLive` gate made .stopmatch and the
                // css_ma "Stop Match" button a silent no-op there ("cannot stop once started").
                if (matchStarted || isMatchSetup || isVeto || isPreVeto)
                {
                    PrintLocalizedToAll("matchzy.cmd.adminstoppedmatch");
                    ResetMatch(true, "ended_early");
                }
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchended"));
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_matchgg", "Surrender the match")]
        [ConsoleCommand("css_surrender", "Surrender the match")]
        public void OnSurrenderCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_endmatch", "@css/config"))
            {
                // Block during endscreen/post-game
                if (IsPostGamePhase())
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchended"));
                    return;
                }

                if (matchStarted && isMatchLive)
                {
                    PrintLocalizedToAll("matchzy.cmd.matchsurrendered");
                    ResetMatch(true, "surrendered");
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_restart", "Restarts the match")]
        [ConsoleCommand("css_abort", "Restarts the match")]
        public void OnRestartMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_restart", "@css/config"))
            {
                // Block restart during endscreen/post-game to avoid CSTV/server issues
                if (IsPostGamePhase())
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchended"));
                    return;
                }

                if (GuardAgainstDryRun(player))
                    return;
                if (!isPractice)
                {
                    ResetMatch(true, "restarted");
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_rmap", "Reloads the current map")]
        private void OnMapReloadCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            // Block map reload during endscreen/post-game to avoid CSTV/player issues
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchended"));
                return;
            }
            string currentMapName = Server.MapName;

            // Stop demo recording before map change to prevent GOTV crash
            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
            }

            KickAllBotsProtectCSTV();

            Server.NextFrame(() =>
            {
                // A workshop map reloads through its workshop id / collection, a stock map via changelevel.
                string? command = BuildMapChangeCommand(currentMapName);
                if (command != null)
                {
                    Server.ExecuteCommand(command);
                }
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.invalidmap"));
                }
            });
        }

        private bool GuardAgainstDryRun(CCSPlayerController? player)
        {
            if (!isDryRun)
                return false;

            // Localized message if you have it, otherwise plain text:
            // ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nostartindry"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.nostartindryrun"));
            return true; // means: blocked
        }

        [ConsoleCommand("css_start", "Force starts the match")]
        [ConsoleCommand("css_force", "Force starts the match")]
        [ConsoleCommand("css_forcestart", "Force starts the match")]
        public void OnStartCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_start", "@css/config"))
            {
                if (isPractice)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.startinpractice"));
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.startinpracticehint"));
                    return;
                }

                if (GuardAgainstDryRun(player))
                    return;

                if (matchStarted)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.startmatchstarted"));
                }
                else
                {
                    PrintLocalizedToAll("matchzy.cmd.adminstartedgame");
                    HandleMatchStart();
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_warmup", "Force starts the match")]
        public void OnWarmupCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_warmup", "@css/config"))
            {
                if (GuardAgainstDryRun(player))
                    return;
                if (matchStarted)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.warmupmatchstarted"));
                }
                else if (!warmupEnabled.Value)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.warmupdisabled"));
                }
                else
                {
                    ExecUnpracCommands();
                    CleanupAllCollisionTimers();
                    PrintLocalizedToAll("matchzy.cmd.adminstartedwarmup");
                    ExecModeCfg(warmupCfgPath, ";mp_freezetime 0");
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_asay", "Say as an admin (all chat)")]
        public void OnAdminSay(CCSPlayerController? player, CommandInfo? command)
        {
            if (command == null)
                return;
            // Another plugin (e.g. CS2-SimpleAdmin, whose css_asay is team-only) may own css_asay.
            // When disabled, MatchZy's console handler no-ops so !asay does not double-print.
            // The .asay chat command stays available regardless (see HandleAdminSayCommand).
            if (!asayConsoleEnabled.Value)
                return;
            if (player == null)
            {
                Server.PrintToChatAll($"{adminChatPrefix} {command.ArgString}");
                return;
            }
            string message = "";
            for (int i = 1; i < command.ArgCount; i++)
            {
                message += command.ArgByIndex(i) + " ";
            }
            HandleAdminSayCommand(player, message);
        }

        // Shared by console css_asay and the .asay chat prefix command. Broadcasts to all chat.
        public void HandleAdminSayCommand(CCSPlayerController? player, string message)
        {
            if (!IsPlayerAdmin(player, "css_asay", "@css/chat"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            message = message.Trim();
            if (string.IsNullOrEmpty(message))
                return;
            Server.PrintToChatAll($"{adminChatPrefix} {message}");
        }

        // Detect a sibling plugin that owns css_map (CS2-SimpleAdmin / CS2MapChange and variants)
        // by scanning the plugins directory (MatchZy's own folder's parent). Returns the folder
        // name, or null if none. Lets MatchZy auto-yield css_map so two plugins never register the
        // same ConCommand (which can block players from connecting).
        private string? DetectConflictingMapPlugin()
        {
            try
            {
                var pluginsDir = Path.GetDirectoryName(ModuleDirectory);
                if (string.IsNullOrEmpty(pluginsDir) || !Directory.Exists(pluginsDir))
                    return null;
                foreach (var dir in Directory.GetDirectories(pluginsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name.Contains("SimpleAdmin", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("MapChange", StringComparison.OrdinalIgnoreCase))
                        return name;
                }
            }
            catch { /* best effort - fall through to registering css_map */ }
            return null;
        }

        // Detect whether the server owner added "." to CounterStrikeSharp's chat triggers in
        // addons/counterstrikesharp/configs/core.json, e.g.
        //     "PublicChatTrigger": [ "!", "." ],
        // and collect every console command MatchZy itself registers.
        //
        // Why this matters: CSS's Host_Say detour (chat_manager.cpp) dispatches "css_<cmd>" for a
        // triggered message FIRST and fires the player_chat game event AFTERWARDS. MatchZy's own dot
        // dispatch lives on that event, so with "." as a trigger a single ".ready" runs the console
        // handler and then the chat handler - the command executes twice and every reply prints
        // twice (".map junk" answered "Invalid map name!" twice, while "!map junk" answered once).
        // ~170 of MatchZy's dot commands have a css_ twin, so this affects nearly all of them.
        //
        // The dot commands MatchZy has no console twin for (.rdy, .nr, .knife, .training, ...) are
        // unaffected: CSS finds no css_ command for those and does nothing, so the chat dispatch
        // stays the only handler. See the guard in the EventPlayerChat handler (Core/MatchZy.cs).
        private void DetectDotChatTrigger()
        {
            try
            {
                _ownConsoleCommands.Clear();
                foreach (var method in GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    foreach (var attr in method.GetCustomAttributes<ConsoleCommandAttribute>(false))
                    {
                        if (!string.IsNullOrEmpty(attr.Command))
                            _ownConsoleCommands.Add(attr.Command);
                    }
                }

                _dotIsCssChatTrigger = CoreConfig.PublicChatTrigger.Concat(CoreConfig.SilentChatTrigger)
                    .Any(trigger => trigger == ".");

                if (_dotIsCssChatTrigger)
                {
                    Log($"[ChatTrigger] '.' is configured as a CounterStrikeSharp chat trigger in core.json. "
                        + $"CSS already runs css_<command> for a dot message, so MatchZy will skip its own chat dispatch "
                        + $"for the {_ownConsoleCommands.Count} commands it registers as console commands (prevents duplicate output). "
                        + $"Set matchzy_dot_trigger_dedupe 0 to disable this.");
                }
            }
            catch (Exception ex)
            {
                // Best effort: on failure leave the flag false so behaviour matches the pre-fix
                // plugin (duplicate output on a '.'-trigger server, but nothing else breaks).
                _dotIsCssChatTrigger = false;
                Log($"[ChatTrigger] Could not read CounterStrikeSharp chat triggers: {ex.Message}");
            }
        }

        // NOT a [ConsoleCommand] - registered dynamically in Load() only when
        // matchzy_map_console_command_enabled is true AND no dedicated map plugin is detected
        // (see the AddCommand in Load). This avoids a ConCommand-registration conflict with a
        // dedicated map plugin (CS2MapChange / CS2-SimpleAdmin) that would block connections. The
        // convar check below is kept as a defence in case the command is ever registered anyway.
        public void OnMapCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (command == null)
                return;
            if (!mapConsoleCommandEnabled.Value)
                return;
            if (command.ArgCount < 2)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", "css_map <map name/id>"));
                return;
            }
            HandleMapChangeCommand(player, command.GetArg(1));
        }

        [ConsoleCommand("css_scrim", "Starts scrim mode")]
        [ConsoleCommand("css_playout", "Starts scrim mode")]
        [ConsoleCommand("css_po", "Starts scrim mode")]
        public void OnScrimCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_scrim", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (GuardAgainstDryRun(player))
                return;

            // Check before touching any flag: flipping isKnifeRound/playout during a running knife
            // round or live match left the match stuck (knife round never resolved) or changed
            // overtime/clinch mid-match.
            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.scrim"));
                return;
            }

            isKnifeRound = false;
            isKnifeRequired = false;
            isPlayOutEnabled = true;
            isPlayOutEnabled2 = false;
            isMatchModeEnabled = false;

            SetExplicitMode(1);
            StartScrimMode();
            // Apply clinch=0/overtime=0 NOW during warmup so client trophy UI slot
            // is allocated correctly at the upcoming warmup→live phase transition.
            // Mid-live convar flips can't move trophy after the slot is allocated.
            HandlePlayoutConfig();
            // Same compact status readout as .match, reflecting scrim's flags (knife off, playout on).
            string on = Localizer.ForPlayer(player, "matchzy.cc.statuson");
            string off = Localizer.ForPlayer(player, "matchzy.cc.statusoff");
            string knife = isKnifeRequired ? on : off;
            string demorec = IsGOTVEnabled() ? on : off;
            string playout = isPlayOutEnabled ? on : off;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.scrimloaded"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.matchsettings", knife, demorec, playout));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.matchhelphint"));
        }

        [ConsoleCommand("css_hill", "Starts scrim mode")]
        public void OnHillCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_hill", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (GuardAgainstDryRun(player))
                return;

            // Check before touching any flag (see OnScrimCommand).
            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.alreadyhill"));
                return;
            }

            isKnifeRequired = false;
            isKnifeRound = false;

            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.hillloaded"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.hillnoknife"));

            isPlayOutEnabled = false;
            isPlayOutEnabled2 = true;
            isKnifeRequired = false;
            isMatchModeEnabled = false;
            StartHillMode();
            // Apply clinch=0/overtime=0 in warmup → see OnScrimCommand for rationale.
            HandlePlayoutConfig();
        }

        [ConsoleCommand("css_match", "Starts match mode")]
        public void OnMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_match", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (GuardAgainstDryRun(player))
                return;

            // Check before touching any flag (see OnScrimCommand).
            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.match"));
                return;
            }

            isKnifeRequired = true;
            // Compact match-status readout: show every toggle at a glance (knife / demo recording /
            // playout) with colored Enabled/Disabled, plus a .help hint.
            string on = Localizer.ForPlayer(player, "matchzy.cc.statuson");
            string off = Localizer.ForPlayer(player, "matchzy.cc.statusoff");
            string knife = isKnifeRequired ? on : off;
            string demorec = IsGOTVEnabled() ? on : off;
            string playout = isPlayOutEnabled ? on : off;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.matchloaded"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.matchsettings", knife, demorec, playout));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.matchhelphint"));
            isMatchModeEnabled = true;

            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.match"));
                return;
            }

            isPlayOutEnabled = false;
            isPlayOutEnabled2 = false; // clear hill flag too, else .hill -> .match leaks hill mode
            SetExplicitMode(1);
            StartMatchMode();
            // Apply clinch=1/overtime=1 from live.cfg NOW during warmup so client
            // trophy UI slot is allocated at warmup→knife/live phase transition.
            // Required when transitioning back from scrim/hill where clinch=0 leaked.
            HandlePlayoutConfig();
        }

        [ConsoleCommand("css_exitprac", "Starts match mode")]
        [ConsoleCommand("css_noprac", "Starts match mode")]
        public void OnExitPracCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_exitprac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!isPractice)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.pracnotactive"));
                return;
            }

            // Reset all player practice settings when exiting
            ResetAllPlayerPracticeSettings(enteringPractice: false);
            SetExplicitMode(1);

            CleanupAllCollisionTimers();

            if (GuardAgainstDryRun(player))
                return;

            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.exitprac"));
                return;
            }

            StartMatchMode();

            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.exitingprac"));
        }

        [ConsoleCommand("css_matchhelp", "Triggers provided command on the server")]
        [ConsoleCommand("css_matchzyhelp", "Triggers provided command on the server")]
        public void OnHelpCommand(CCSPlayerController? player, CommandInfo? command)
        {
            SendAvailableCommandsMessage(player);
        }

        [ConsoleCommand("css_mhelp", "Shows all available commands for each mode (admin only)")]
        public void OnAdminHelpCommand(CCSPlayerController? player, CommandInfo? command)
        {
            // Any admin may open the guide; it only lists what that player can actually run.
            if (HasAnyAdminCommand(player))
            {
                SendAdminCommandsGuide(player);
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        //[ConsoleCommand("css_playout", "Toggles playout (Playing of max rounds)")]
        //public void OnPlayoutCommand(CCSPlayerController? player, CommandInfo? command) {
        //    if (IsPlayerAdmin(player, "css_playout", "@css/config")) {
        //        isPlayOutEnabled = !isPlayOutEnabled;
        //        if(isPlayOutEnabled) isKnifeRequired = false;
        //        string playoutStatus = isPlayOutEnabled? "Enabled" : "Disabled";
        //        if (player == null) {
        //           // ReplyToUserCommand(player, $"Playout is now {playoutStatus}!");
        //            ReplyToUserCommand(player, Localizer["matchzy.cc.playout", playoutStatus]);
        //        } else {
        //            // player.PrintToChat($"{chatPrefix} Playout is now {ChatColors.Green}{playoutStatus}{ChatColors.Default}!");
        //            PrintToPlayerChat(player, Localizer["matchzy.cc.playout", playoutStatus]);
        //        }
        //
        //        HandlePlayoutConfig();
        //
        //            } else {
        //                SendPlayerNotAdminMessage(player);
        //            }
        //        }

        [ConsoleCommand("matchzy_version", "Displays the current MatchZy version")]
        [ConsoleCommand("css_matchzy_version", "Displays the current MatchZy version")]
        [ConsoleCommand("mikzy_version", "Displays the current MikZy version")]
        [ConsoleCommand("css_mikzy_version", "Displays the current MikZy version")]
        [ConsoleCommand("css_version", "Displays the current MatchZy version")]
        [ConsoleCommand("version", "Returns server version")]
        public void OnMatchZyVersionCommand(CCSPlayerController? player, CommandInfo? command)
        {
            // The .version chat alias is dispatched without a CommandInfo, so reply through the
            // player in that case instead of returning silently.
            Action<string> reply = command != null
                ? message => command.ReplyToCommand(message)
                : message => ReplyToUserCommand(player, message);

            string steamInfFilePath = Path.Combine(Server.GameDirectory, "csgo", "steam.inf");

            if (!File.Exists(steamInfFilePath))
            {
                reply("Unable to locate steam.inf file!");
                return;
            }

            var steamInfContent = File.ReadAllText(steamInfFilePath);

            // Extract PatchVersion (e.g., 1.41.2.5)
            Regex patchRegex = new(@"PatchVersion=(.+)");
            Match patchMatch = patchRegex.Match(steamInfContent);
            string? patchVersion = patchMatch.Success ? patchMatch.Groups[1].Value.Trim() : null;

            // Extract ServerVersion (e.g., 14125)
            Regex serverRegex = new(@"ServerVersion=(\d+)");
            Match serverMatch = serverRegex.Match(steamInfContent);
            string? serverVersion = serverMatch.Success ? serverMatch.Groups[1].Value : null;

            // Build the response similar to CS2 status command format
            if (patchVersion != null && serverVersion != null)
            {
                reply($"Protocol version: [{patchVersion}/{serverVersion}]");
                reply($"MatchZy version: {ModuleVersion}");
            }
            else
            {
                reply("Unable to get server version");
            }
        }

        // Overrides noclip console command. Perform the changes on server side.
        public HookResult OnConsoleNoClip(CCSPlayerController? player, CommandInfo cmd)
        {
            if (player == null || !player.PawnIsAlive || player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
                return HookResult.Stop;

            // Additional safety check for PlayerPawn
            if (!player.PlayerPawn.IsValid || player.PlayerPawn.Value == null)
                return HookResult.Stop;

            // inspired by cs2-noclip
            if (player.PlayerPawn.Value.MoveType == MoveType_t.MOVETYPE_NOCLIP)
            {
                player.PlayerPawn.Value!.ResetNoclipToWalk();
            }
            else
            {
                player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NOCLIP;
                player.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_NOCLIP;
                Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
            }

            return HookResult.Stop;
        }
    }
}
