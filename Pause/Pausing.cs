using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // Ends a tech pause after matchzy_tech_pause_duration. The token ties the timer to the pause
    // that started it, so a later pause is never lifted by an old timer.
    private CounterStrikeSharp.API.Modules.Timers.Timer? techPauseTimer = null;
    private int techPauseToken = 0;

    // Any unpause, and any new pause that is not a tech pause, ends the tech pause window. Without
    // this the timer of an earlier, already-lifted tech pause could end a later regular pause of
    // the same team when it fired.
    private void CancelTechPauseTimer()
    {
        techPauseTimer?.Kill();
        techPauseTimer = null;
        techPauseToken++;
    }

    // Auto-pause tracking
    private bool isAutoPaused = false;

    // Set when both teams (or an admin) lift an auto-pause while a team is still short: they agreed
    // to play on. The 10 s check used to pause again straight away, so .unpause could never end an
    // auto-pause. Holds the player counts they agreed to; only a further drop pauses again, and it
    // clears once both teams are full again.
    private bool autoPauseShortAccepted = false;
    // Per match team, not per side: the counts must follow the teams through halftime.
    private readonly Dictionary<Team, int> autoPauseAcceptedCounts = new();

    /// <summary>
    /// Called when an auto-pause is lifted by the teams or an admin: remember the short-handed
    /// counts they accepted so the auto-pause check does not pause again for the same shortage.
    /// </summary>
    private void AcceptShortHandedAfterAutoPause()
    {
        if (!IsCurrentPauseAuto())
            return;
        int minPlayers = autoPauseMinPlayers.Value;
        int ct = IsBotSide(3) ? minPlayers : GetTeamPlayerCount(CsTeam.CounterTerrorist);
        int t = IsBotSide(2) ? minPlayers : GetTeamPlayerCount(CsTeam.Terrorist);
        autoPauseAcceptedCounts.Clear();
        if (reverseTeamSides.TryGetValue("CT", out var ctTeam))
            autoPauseAcceptedCounts[ctTeam] = ct;
        if (reverseTeamSides.TryGetValue("TERRORIST", out var tTeam))
            autoPauseAcceptedCounts[tTeam] = t;
        autoPauseShortAccepted = true;
        isAutoPaused = false;
        autoPauseReason = null;
        unpauseData["pauseTeam"] = "";
        Log($"[AutoPause] Lifted by agreement; playing on with CT {ct}, T {t}.");
    }
    private string? autoPauseReason = null;
    // Arguments for the localized auto-pause reason (team side, player count, minimum players).
    private object[] autoPauseReasonArgs = Array.Empty<object>();
    private CounterStrikeSharp.API.Modules.Timers.Timer? autoPauseCheckTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
#pragma warning disable CS0162 // Unreachable code detected
        if (!isMatchLive)
            return;

        // Handle console usage
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pause.ispaused"));
            return;
        }

        if (IsHalfTimePhase())
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pause.duringhalftime"));
            return;
        }

        if (IsPostGamePhase())
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pause.matchended"));
            return;
        }

        if (IsTacticalTimeoutActive())
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pause.tacticaltimeout"));
            return;
        }

        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
            return;

        if (!techPauseEnabled.Value && player != null)
        {
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pause.techpausenotenabled"));
            return;
        }

        if (maxTechPausesAllowed.Value <= 0)
            return;

        // Initialize team if it doesn't exist yet in the dictionary
        if (!reverseTeamSides.ContainsKey("CT") || !reverseTeamSides.ContainsKey("TERRORIST"))
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.sidesnotinitialized"));
            return;
        }

        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];

        // Ensure this team is in our tracking dictionary
        if (!technicalPauseUsed.ContainsKey(playerTeam))
        {
            technicalPauseUsed[playerTeam] = 0;
        }

        // matchzy_max_tech_pauses_allowed was never enforced: the counter existed but was
        // never compared or incremented.
        if (technicalPauseUsed[playerTeam] >= maxTechPausesAllowed.Value)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.techpauseslimitreached", maxTechPausesAllowed.Value));
            return;
        }
        technicalPauseUsed[playerTeam]++;

        // The team on the player's side. mp_teamname_1/2 swap meaning after halftime, so reading
        // them by side named the opponent in the second half.
        string teamName = playerTeam.teamName;

        // Use default names if convars are empty
        if (string.IsNullOrEmpty(teamName))
        {
            teamName = player.Team == CsTeam.CounterTerrorist ? "Counter-Terrorists" : "Terrorists";
        }

        // Execute pause command
        Server.ExecuteCommand("mp_pause_match");
        isPaused = true;

        // Reset unpause data to allow normal unpause functionality
        unpauseData["ct"] = false;
        unpauseData["t"] = false;
        unpauseData["pauseTeam"] = playerTeam.teamName;

        // Mark as manual pause (not auto-pause)
        isAutoPaused = false;
        autoPauseReason = null;

        // Announce the technical pause
        int techPausesLeft = maxTechPausesAllowed.Value - technicalPauseUsed[playerTeam];
        PrintLocalizedToAll("matchzy.pause.techpause.started", teamName, techPausesLeft, maxTechPausesAllowed.Value);

        // matchzy_tech_pause_duration (-1 = no limit) ends the pause on its own.
        techPauseTimer?.Kill();
        techPauseTimer = null;
        int duration = techPauseDuration.Value;
        if (duration > 0)
        {
            int token = ++techPauseToken;
            string pauseTeamName = playerTeam.teamName;
            techPauseTimer = AddTimer(duration, () =>
            {
                techPauseTimer = null;
                if (token != techPauseToken || !isPaused)
                    return;
                if (!unpauseData.TryGetValue("pauseTeam", out var current) || current is not string currentTeam || currentTeam != pauseTeamName)
                    return;
                PrintLocalizedToAll("matchzy.pausemsg.techpauseexpired", pauseTeamName, duration);
                UnpauseMatch();
                unpauseData["pauseTeam"] = "";
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        // Send webhook for live scorebot
        if (!string.IsNullOrEmpty(matchConfig.RemoteLogURL))
        {
            var pauseEvent = new MatchPausedLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                PauseType = "tech",
                TeamName = playerTeam.teamName,
                MaxDuration = techPauseDuration.Value,
                RoundNumber = GetRoundNumer(),
            };

            PublishEvent(pauseEvent);
        }
    }
#pragma warning restore CS0162 // Unreachable code detected

    /// <summary>
    /// Check if autopause should be active for current player count
    /// Autopause only works for 5v5 (10 total players)
    /// For smaller player counts (1v1, 2v2, 3v3, 4v4, 4v5), autopause is disabled
    /// </summary>
    // Most humans seen on the server since auto-pause monitoring started for this map.
    private int autoPausePeakHumans = 0;

    public bool IsAutoPauseActive()
    {
        // Same population as GetTeamPlayerCount (humans on T/CT, no coaches): spectators, casters
        // and coaches must not arm auto-pause for a smaller match.
        int totalPlayers = GetTeamPlayerCount(CsTeam.CounterTerrorist) + GetTeamPlayerCount(CsTeam.Terrorist);

        // Judge by the peak, not the current count: the check runs right after a disconnect, when
        // a 5v5 is down to 9 players, so requiring 10 connected players meant auto-pause could
        // never trigger in the exact situation it exists for.
        if (totalPlayers > autoPausePeakHumans)
            autoPausePeakHumans = totalPlayers;

        return autoPausePeakHumans >= 2 * Math.Max(1, autoPauseMinPlayers.Value);
    }

    // True only while the CURRENT pause is an auto-pause. isAutoPaused alone can be left over from an
    // earlier auto-pause that the teams lifted with .unpause, which made a later manual or admin
    // pause auto-resume as soon as both teams were full.
    private bool IsCurrentPauseAuto()
    {
        return isPaused && isAutoPaused && unpauseData.TryGetValue("pauseTeam", out var pauseTeam) && pauseTeam is string team && team == "AUTO";
    }

    /// <summary>
    /// MatchZy's auto-pause system - automatically pauses when a team has fewer than 5 players
    /// This replaces the need for sv_matchpause_auto_5v5
    /// Note: Autopause is only active for 5v5 matches (10 total players)
    /// </summary>
    public void CheckAutoResumeOrAutoPause()
    {
        if (!isMatchLive)
            return;
        if (!autoPauseEnabled.Value)
            return; // Check if auto-pause is enabled via ConVar
        if (!IsAutoPauseActive())
            return; // Only autopause for 5v5 (10 players)
        if (IsHalfTimePhase() || IsPostGamePhase())
            return;

        int minPlayers = autoPauseMinPlayers.Value;
        // The bot side has no humans by design; never count it short.
        int ctPlayerCount = IsBotSide(3) ? minPlayers : GetTeamPlayerCount(CsTeam.CounterTerrorist);
        int tPlayerCount = IsBotSide(2) ? minPlayers : GetTeamPlayerCount(CsTeam.Terrorist);

        if (autoPauseShortAccepted)
        {
            if (ctPlayerCount >= minPlayers && tPlayerCount >= minPlayers)
                autoPauseShortAccepted = false; // full again: back to normal auto-pausing
            else
            {
                // Map the accepted counts (kept per team) to the sides the teams are on now.
                int acceptedCt = reverseTeamSides.TryGetValue("CT", out var ctTeam) && autoPauseAcceptedCounts.TryGetValue(ctTeam, out int a1) ? a1 : minPlayers;
                int acceptedT = reverseTeamSides.TryGetValue("TERRORIST", out var tTeam) && autoPauseAcceptedCounts.TryGetValue(tTeam, out int a2) ? a2 : minPlayers;
                if (ctPlayerCount >= acceptedCt && tPlayerCount >= acceptedT)
                    return; // still the shortage the teams agreed to play with
            }
        }

        // Check if we need to auto-pause (team has < min players)
        if (!isPaused && (ctPlayerCount < minPlayers || tPlayerCount < minPlayers))
        {
            string teamWithIssue = ctPlayerCount < minPlayers ? "CT" : "T";
            int playerCount = ctPlayerCount < minPlayers ? ctPlayerCount : tPlayerCount;

            Log($"[AutoPause] Triggering auto-pause - {teamWithIssue} team has {playerCount}/{minPlayers} players");

            Server.ExecuteCommand("mp_pause_match");
            isPaused = true;
            isAutoPaused = true;
            autoPauseReason = $"{teamWithIssue} team has only {playerCount}/{minPlayers} players";
            autoPauseReasonArgs = new object[] { teamWithIssue, playerCount, minPlayers };

            // Reset unpause data
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            unpauseData["pauseTeam"] = "AUTO";

            PrintLocalizedToAll("matchzy.pausemsg.autopaused", teamWithIssue, playerCount, minPlayers);
            PrintLocalizedToAll("matchzy.pausemsg.autoresumehint", minPlayers);

            // Send webhook for live scorebot
            if (!string.IsNullOrEmpty(matchConfig.RemoteLogURL))
            {
                var pauseEvent = new MatchPausedLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    PauseType = "auto",
                    TeamName = teamWithIssue,
                    MaxDuration = null,
                    RoundNumber = GetRoundNumer(),
                };
                PublishEvent(pauseEvent);
            }
        }
        // Check if we can auto-resume (both teams back to min players)
        else if (IsCurrentPauseAuto() && ctPlayerCount >= minPlayers && tPlayerCount >= minPlayers)
        {
            Log($"[AutoPause] Auto-resuming - both teams now have {minPlayers} players (CT: {ctPlayerCount}, T: {tPlayerCount})");

            int resumeDelay = autoResumeDelay.Value;
            PrintLocalizedToAll("matchzy.pausemsg.autoresuming", minPlayers, resumeDelay);

            AddTimer(
                (float)resumeDelay,
                () =>
                {
                    if (!IsCurrentPauseAuto())
                        return; // Already unpaused, or replaced by a manual/admin pause

                    Server.ExecuteCommand("mp_unpause_match");
                    CancelTechPauseTimer();
                    isPaused = false;
                    isAutoPaused = false;
                    autoPauseReason = null;

                    // Reset unpause data
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;
                    unpauseData["pauseTeam"] = "";

                    PrintLocalizedToAll("matchzy.pausemsg.autoresumed");

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
            );
        }
    }

    /// <summary>
    /// Get the count of non-coach players on a team
    /// </summary>
    private int GetTeamPlayerCount(CsTeam team)
    {
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        int count = 0;

        foreach (var p in playerData.Values)
        {
            if (IsHumanPlayerValid(p) && p.Team == team && !coaches.Contains(p))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Enhanced unpause handler that works with both manual pauses and auto-pauses
    /// </summary>
    public bool HandleUnpause(CCSPlayerController player)
    {
        if (!isPaused)
        {
            return false;
        }

        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
        {
            return false;
        }

        // If it's an auto-pause due to missing players AND autopause is active for this player count
        if (IsCurrentPauseAuto() && IsAutoPauseActive())
        {
            int minPlayers = autoPauseMinPlayers.Value;
            int ctCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
            int tCount = GetTeamPlayerCount(CsTeam.Terrorist);

            if (ctCount < minPlayers || tCount < minPlayers)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.cannotunpauseunbalanced", ctCount, minPlayers, tCount, minPlayers));
                return false;
            }
        }
        // For small player counts (4v4, 3v3, etc), allow unpause even if teams unbalanced
        else if (IsCurrentPauseAuto() && !IsAutoPauseActive())
        {
            // Autopause inactive for small player counts - allow unpause
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.unpauseallowedunbalanced"));
        }

        string teamKey = player.Team == CsTeam.CounterTerrorist ? "ct" : "t";
        // Side -> match team (mp_teamname_1/2 swap meaning after halftime).
        string teamName = player.Team == CsTeam.CounterTerrorist ? reverseTeamSides["CT"].teamName : reverseTeamSides["TERRORIST"].teamName;

        if (string.IsNullOrEmpty(teamName))
        {
            teamName = player.Team == CsTeam.CounterTerrorist ? "Counter-Terrorists" : "Terrorists";
        }

        // Mark this team as ready to unpause
        unpauseData[teamKey] = true;

        // Check if both teams are ready to unpause
        bool ctReady = (bool)unpauseData["ct"];
        bool tReady = (bool)unpauseData["t"];

        if (ctReady && tReady)
        {
            // Both teams ready, unpause the match
            Server.ExecuteCommand("mp_unpause_match");
            CancelTechPauseTimer();
            isPaused = false;
            isAutoPaused = false;
            autoPauseReason = null;

            PrintLocalizedToAll("matchzy.pausemsg.unpausedbothready");

            // Reset unpause data
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            unpauseData["pauseTeam"] = "";

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

            return true;
        }
        else
        {
            // Waiting for other team
            string waitingFor = ctReady ? "Terrorists" : "Counter-Terrorists";
            PrintLocalizedToAll("matchzy.pausemsg.teamreadytounpause", teamName, waitingFor);
            return false;
        }
    }

    /// <summary>
    /// Start the auto-pause monitoring timer
    /// Call this when match goes live
    /// </summary>
    public void StartAutoPauseCheck()
    {
        // Called when a map goes live (match, scrim, hill): every team starts with its full
        // tech pause budget. ResetTechPauses was never called before.
        ResetTechPauses();

        if (!autoPauseEnabled.Value)
        {
            Log("[AutoPause] Auto-pause is disabled - not starting monitoring timer");
            return;
        }

        // Kill existing timer if any
        autoPauseCheckTimer?.Kill();
        autoPausePeakHumans = 0;
        // Sample now: the first timer tick is 10s away, and a player who drops before it would
        // otherwise leave the peak below a full match.
        IsAutoPauseActive();

        // Check every 10 seconds for player count changes
        autoPauseCheckTimer = AddTimer(
            10.0f,
            () =>
            {
                if (isMatchLive)
                {
                    CheckAutoResumeOrAutoPause();
                }
            },
            TimerFlags.REPEAT
        );

        Log("[AutoPause] Started auto-pause monitoring with 10s interval"); // Changed this line
    }

    /// <summary>
    /// Stop the auto-pause monitoring timer
    /// Call this when match ends
    /// </summary>
    public void StopAutoPauseCheck()
    {
        autoPauseCheckTimer?.Kill();
        autoPauseCheckTimer = null;
        autoPausePeakHumans = 0;
        techPauseTimer?.Kill();
        techPauseTimer = null;
        techPauseToken++;

        isAutoPaused = false;
        autoPauseReason = null;
        autoPauseShortAccepted = false;
    }

    public void ResetTechPauses()
    {
        techPauseTimer?.Kill();
        techPauseTimer = null;
        techPauseToken++;
        technicalPauseUsed.Clear();
        foreach (var team in reverseTeamSides.Values)
        {
            technicalPauseUsed[team] = 0;
        }

        lastTechPauseDuration = 0;

        // Reset auto-pause state
        isAutoPaused = false;
        autoPauseReason = null;
        autoPauseShortAccepted = false;

        if (pausedStateTimer != null)
        {
            pausedStateTimer.Kill();
            pausedStateTimer = null;
        }
    }
}
