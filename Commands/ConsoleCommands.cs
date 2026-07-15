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
                int totalPlayers = minimumReadyRequired;

                int readyPlayers = playerReadyStatus.Values.Count(status => status);
                int notReadyPlayers = totalPlayers - readyPlayers;

                if (notReadyPlayers < 0)
                    notReadyPlayers = 0; // safety check

                player?.PrintToChat($" {ChatColors.Default}Ready Players: {ChatColors.Green}{readyPlayers}/{totalPlayers}");
                player?.PrintToChat($" {ChatColors.Default}Waiting For: {ChatColors.Red}{notReadyPlayers} Players");
            }
            else
            {
                player?.PrintToChat($"{ChatColors.Default}Ready check is not available right now.");
            }
        }

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
                            PrintToAllChat($"{ChatColors.Green}{teamName} is ready!");
                        }
                    }

                    AddTimer(afterReadyDelay, CheckLiveRequired);
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
                    PrintToAllChat(Localizer["matchzy.knife.decidedtostay", knifeWinnerName]);
                    StartLive();
                }
                else
                {
                    // They're on T side and want CT, so switch
                    Server.ExecuteCommand("mp_swapteams;");
                    SwapSidesInTeamData(true);
                    PrintToAllChat(Localizer["matchzy.knife.chosect", knifeWinnerName]);
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
                    PrintToAllChat(Localizer["matchzy.knife.decidedtostay", knifeWinnerName]);
                    StartLive();
                }
                else
                {
                    // They're on CT side and want T, so switch
                    Server.ExecuteCommand("mp_swapteams;");
                    SwapSidesInTeamData(true);
                    PrintToAllChat(Localizer["matchzy.knife.choset", knifeWinnerName]);
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
                PrintToAllChat(Localizer["matchzy.knife.decidedtostay", knifeWinnerName]);
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
                PrintToAllChat(Localizer["matchzy.knife.decidedtoswitch", knifeWinnerName]);
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

                            entity.Remove();
                        }
                        catch
                        {
                            // Ignorer fejl på enkelte entities
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
                player?.PrintToChat($"{chatPrefix} ⛔ Pause command is disabled.");
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
                player.PrintToChat($"{chatPrefix} Match is live and not paused. Use {ChatColors.Green}!pause{ChatColors.Default} to pause or {ChatColors.Green}!unready{ChatColors.Default} if you need to go unready.");
            }
            else if (!readyAvailable)
            {
                player.PrintToChat($"{chatPrefix} Ready system is not available right now.");
            }
            else
            {
                player.PrintToChat($"{chatPrefix} {ChatColors.Red}!r{ChatColors.Default} command is not available in the current match state.");
            }
        }

        [ConsoleCommand("css_up", "Unpause the match")]
        [ConsoleCommand("css_unpause", "Unpause the match")]
        public void OnUnpauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if ((isMatchLive || isKnifeRound) && isPaused)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin" && player != null)
                {
                    PrintToPlayerChat(player, Localizer["matchzy.pause.onlyadmincanunpause"]);
                    return;
                }

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
                else
                {
                    return;
                }

                if ((bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamsunpausedthematch"]);
                    Server.ExecuteCommand("mp_unpause_match;");
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
                        Task.Run(async () =>
                        {
                            await SendEventAsync(unpauseEvent);
                        });
                    }
                }
                else if (unpauseTeamName == "Admin")
                {
                    PrintToAllChat(Localizer["matchzy.pause.adminunpausedthematch"]);
                    Server.ExecuteCommand("mp_unpause_match;");
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
                        Task.Run(async () =>
                        {
                            await SendEventAsync(unpauseEvent);
                        });
                    }
                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", unpauseTeamName, remainingUnpauseTeam]);
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
                    ReplyToUserCommand(player, "Match is already paused, cannot start a tactical timeout!");
                    return;
                }

                var gameRules = GetGameRules();
                if (gameRules == null)
                {
                    ReplyToUserCommand(player, "Failed to get game rules.");
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
                        ReplyToUserCommand(player, "You do not have any tactical timeouts left!");
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
                        ReplyToUserCommand(player, "You do not have any tactical timeouts left!");
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
                    ReplyToUserCommand(player, $"Current Ready Required: {minimumReadyRequiredFormatted} .Usage: !readyrequired <number_of_ready_players_required>");
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

                player.PrintToChat($"{chatPrefix} Current Settings:");
                player.PrintToChat($"{chatPrefix} Knife Round: {ChatColors.Green}{knifeStatus}{ChatColors.Default}");
                player.PrintToChat($"{chatPrefix} Match Mode: {ChatColors.Green}{matchModeStatus}{ChatColors.Default}");
                player.PrintToChat($"{chatPrefix} Scrim/Full30 Mode (All Rounds): {ChatColors.Green}{playoutStatus}{ChatColors.Default}");
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("get5_endmatch", "Ends resets the current match")]
        public void OnEndMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "get5_endmatch", "@css/config"))
            {
                if (!isPractice)
                {
                    Server.PrintToChatAll($"{chatPrefix} An admin force-ended the match.");
                    HandleMatchEnd();
                }
                else
                {
                    ReplyToUserCommand(player, $"{ChatColors.Green}Practice mode is active, cannot end the match. Make sure to use !exitprac OR !match to load match mode.");
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
        [ConsoleCommand("css_forceend", "Ends and resets match")]
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

                if (matchStarted && isMatchLive)
                {
                    Server.PrintToChatAll($"{chatPrefix} An admin force-ended the match.");
                    ResetMatch(true, "ended_early");
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
                    Server.PrintToChatAll($"{chatPrefix} Match surrendered. GG!");
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
            Server.ExecuteCommand("bot_kick");

            Server.NextFrame(() =>
            {
                if (long.TryParse(currentMapName, out _))
                {
                    Server.ExecuteCommand($"host_workshop_map \"{currentMapName}\"");
                }
                else if (Server.IsMapValid(currentMapName))
                {
                    Server.ExecuteCommand($"changelevel \"{currentMapName}\"");
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
            ReplyToUserCommand(player, "You can’t start a match while Dry Run is active. Type .exitdry first.");
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
                    ReplyToUserCommand(player, $"{ChatColors.Green}You cannot start a match while we are in practice mode.");
                    ReplyToUserCommand(player, $"{ChatColors.Green}Please use the .exitprac or .match commands to enter match mode.");
                    return;
                }

                if (GuardAgainstDryRun(player))
                    return;

                if (matchStarted)
                {
                    ReplyToUserCommand(player, "The Start command cannot be used if the match has already started! If you want to unpause, please use .unpause");
                }
                else
                {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has started the game!");
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
                    ReplyToUserCommand(player, "Warmup command cannot be used if match is already started! If you want to stop match, please use .endmatch");
                }
                else if (!warmupEnabled.Value)
                {
                    ReplyToUserCommand(player, "Warmup mode is disabled via matchzy_warmup_enabled. Set it to true to use this command.");
                }
                else
                {
                    var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg" + "/matchzy");
                    ExecUnpracCommands();
                    CleanupAllCollisionTimers();
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has started the warmup round!");
                    Server.ExecuteCommand($"exec {warmupCfgPath};mp_freezetime 0");
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

        [ConsoleCommand("css_map", "Changes the map (map name or workshop id)")]
        public void OnMapCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (command == null)
                return;
            // Another plugin (e.g. CS2-SimpleAdmin) may own css_map. When disabled,
            // MatchZy's console handler no-ops so !map does not trigger a second map
            // change / conflict. The .map chat command stays available regardless
            // (see HandleMapChangeCommand dispatch in EventPlayerChat).
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

            isKnifeRound = false;
            isKnifeRequired = false;
            isPlayOutEnabled = true;
            isPlayOutEnabled2 = false;
            isMatchModeEnabled = false;

            if (matchStarted)
            {
                ReplyToUserCommand(player, "MatchZy is already in Scrim/Full30 Mode!");
                return;
            }

            StartScrimMode();
            // Apply clinch=0/overtime=0 NOW during warmup so client trophy UI slot
            // is allocated correctly at the upcoming warmup→live phase transition.
            // Mid-live convar flips can't move trophy after the slot is allocated.
            HandlePlayoutConfig();
            ReplyToUserCommand(player, "Scrim/Full30 Mode has been loaded.");
            ReplyToUserCommand(player, "Knife Round is disabled for this mode.");
            ReplyToUserCommand(player, "Wait until all players have !ready OR an Admin can !forcestart.");
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

            isKnifeRequired = false;
            isKnifeRound = false;

            ReplyToUserCommand(player, "Hill mode has been loaded.");
            ReplyToUserCommand(player, "Knife Round is disabled for this mode.");

            if (matchStarted)
            {
                ReplyToUserCommand(player, "MatchZy is already in hill mode!");
                return;
            }

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

            isKnifeRequired = true;
            string knifeStatus = isKnifeRequired ? Localizer.ForPlayer(player, "matchzy.cc.enabled") : Localizer.ForPlayer(player, "matchzy.cc.disabled");
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.match.mode.loaded"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.knifestatus", knifeStatus));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.knifecmd"));
            isMatchModeEnabled = true;

            if (matchStarted)
            {
                ReplyToUserCommand(player, "MatchZy is already in match mode!");
                return;
            }

            isPlayOutEnabled = false;
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
                ReplyToUserCommand(player, "Practice mode is not active!");
                return;
            }

            // Reset all player practice settings when exiting
            ResetAllPlayerPracticeSettings(enteringPractice: false);

            CleanupAllCollisionTimers();

            if (GuardAgainstDryRun(player))
                return;

            if (matchStarted)
            {
                ReplyToUserCommand(player, "MatchZy is already in match mode!");
                return;
            }

            StartMatchMode();

            ReplyToUserCommand(player, "Exiting practice mode, starting match mode!");
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
            if (IsPlayerAdmin(player, "css_adminhelp", "@css/config"))
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
        [ConsoleCommand("css_version", "Displays the current MatchZy version")]
        [ConsoleCommand("version", "Returns server version")]
        public void OnMatchZyVersionCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (command == null)
                return;

            string steamInfFilePath = Path.Combine(Server.GameDirectory, "csgo", "steam.inf");

            if (!File.Exists(steamInfFilePath))
            {
                command.ReplyToCommand("Unable to locate steam.inf file!");
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
                command.ReplyToCommand($"Protocol version: [{patchVersion}/{serverVersion}]");
                command.ReplyToCommand($"MatchZy version: {ModuleVersion}");
            }
            else
            {
                command.ReplyToCommand("Unable to get server version");
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
                player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_WALK;
                player.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
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
