using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Cvars;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // Auto-pause tracking
    private bool isAutoPaused = false;
    private string? autoPauseReason = null;
    private CounterStrikeSharp.API.Modules.Timers.Timer? autoPauseCheckTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
#pragma warning disable CS0162 // Unreachable code detected
        if (!isMatchLive) return;

        // Handle console usage
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }

        if (IsHalfTimePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]);
            return;
        }

        if (IsPostGamePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return;
        }

        if (IsTacticalTimeoutActive())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.tacticaltimeout"]);
            return;
        }

        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None) return;

        if (!techPauseEnabled.Value && player != null)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        if (maxTechPausesAllowed.Value <= 0) return;

        // Initialize team if it doesn't exist yet in the dictionary
        if (!reverseTeamSides.ContainsKey("CT") || !reverseTeamSides.ContainsKey("TERRORIST"))
        {
            ReplyToUserCommand(player, "Team sides not properly initialized. Cannot pause.");
            return;
        }

        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];

        // Ensure this team is in our tracking dictionary
        if (!technicalPauseUsed.ContainsKey(playerTeam))
        {
            technicalPauseUsed[playerTeam] = 0;
        }

        // Get proper team names from convars
        string ctTeamName = ConVar.Find("mp_teamname_1")!.StringValue;
        string tTeamName = ConVar.Find("mp_teamname_2")!.StringValue;

        // Get team name from player
        string teamName = player.Team == CsTeam.CounterTerrorist ? ctTeamName : tTeamName;

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
        PrintToAllChat($"{teamName} called for a technical pause.");

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

            Task.Run(async () =>
            {
                await SendEventAsync(pauseEvent);
            });
        }
    }
#pragma warning restore CS0162 // Unreachable code detected

    /// <summary>
    /// Check if autopause should be active for current player count
    /// Autopause only works for 5v5 (10 total players)
    /// For smaller player counts (1v1, 2v2, 3v3, 4v4, 4v5), autopause is disabled
    /// </summary>
public bool IsAutoPauseActive()
{
    int totalPlayers = 0;
    
    foreach (var p in playerData.Values)
    {
        if (IsHumanPlayerValid(p))
        {
            totalPlayers++;
            if (totalPlayers >= 10) return true; // Early exit
        }
    }
    
    return totalPlayers >= 10;
}

    /// <summary>
    /// MatchZy's auto-pause system - automatically pauses when a team has fewer than 5 players
    /// This replaces the need for sv_matchpause_auto_5v5
    /// Note: Autopause is only active for 5v5 matches (10 total players)
    /// </summary>
    public void CheckAutoResumeOrAutoPause()
    {
        if (!isMatchLive) return;
        if (!autoPauseEnabled.Value) return; // Check if auto-pause is enabled via ConVar
        if (!IsAutoPauseActive()) return; // Only autopause for 5v5 (10 players)
        if (IsHalfTimePhase() || IsPostGamePhase()) return;

        int minPlayers = autoPauseMinPlayers.Value;
        int ctPlayerCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
        int tPlayerCount = GetTeamPlayerCount(CsTeam.Terrorist);

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

            // Reset unpause data
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            unpauseData["pauseTeam"] = "AUTO";

            PrintToAllChat($"{ChatColors.Gold}[AUTO-PAUSE]{ChatColors.Default} Match paused - {autoPauseReason}");
            PrintToAllChat($"{ChatColors.Grey}Match will auto-resume when both teams have {minPlayers} players, or use {ChatColors.Green}.unpause{ChatColors.Default}");

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
                Task.Run(async () => { await SendEventAsync(pauseEvent); });
            }
        }
        // Check if we can auto-resume (both teams back to min players)
        else if (isPaused && isAutoPaused && ctPlayerCount >= minPlayers && tPlayerCount >= minPlayers)
        {
            Log($"[AutoPause] Auto-resuming - both teams now have {minPlayers} players (CT: {ctPlayerCount}, T: {tPlayerCount})");

            int resumeDelay = autoResumeDelay.Value;
            PrintToAllChat($"{ChatColors.Green}[AUTO-RESUME]{ChatColors.Default} Both teams now have {minPlayers} players. Match resuming in {resumeDelay} seconds...");

            AddTimer((float)resumeDelay, () =>
            {
                if (!isPaused) return; // Already unpaused manually

                Server.ExecuteCommand("mp_unpause_match");
                isPaused = false;
                isAutoPaused = false;
                autoPauseReason = null;

                // Reset unpause data
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                unpauseData["pauseTeam"] = "";

                PrintToAllChat($"{ChatColors.Green}Match resumed!{ChatColors.Default}");

                // Send webhook for live scorebot
                if (!string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                {
                    var unpauseEvent = new MatchUnpausedLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                    };
                    Task.Run(async () => { await SendEventAsync(unpauseEvent); });
                }
            });
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
        if (IsHumanPlayerValid(p)
            && p.Team == team && !coaches.Contains(p))
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
        if (isAutoPaused && IsAutoPauseActive())
        {
            int minPlayers = autoPauseMinPlayers.Value;
            int ctCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
            int tCount = GetTeamPlayerCount(CsTeam.Terrorist);

            if (ctCount < minPlayers || tCount < minPlayers)
            {
                ReplyToUserCommand(player, $"Cannot unpause - teams still unbalanced (CT: {ctCount}/{minPlayers}, T: {tCount}/{minPlayers})");
                return false;
            }
        }
        // For small player counts (4v4, 3v3, etc), allow unpause even if teams unbalanced
        else if (isAutoPaused && !IsAutoPauseActive())
        {
            // Autopause inactive for small player counts - allow unpause
            ReplyToUserCommand(player, $"Match paused. You may now use .unpause to continue (players may be unbalanced).");
        }

        string teamKey = player.Team == CsTeam.CounterTerrorist ? "ct" : "t";
        string teamName = player.Team == CsTeam.CounterTerrorist
            ? ConVar.Find("mp_teamname_1")!.StringValue
            : ConVar.Find("mp_teamname_2")!.StringValue;

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
            isPaused = false;
            isAutoPaused = false;
            autoPauseReason = null;

            PrintToAllChat($"{ChatColors.Green}Match unpaused!{ChatColors.Default} Both teams ready.");

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
                Task.Run(async () => { await SendEventAsync(unpauseEvent); });
            }

            return true;
        }
        else
        {
            // Waiting for other team
            string waitingFor = ctReady ? "Terrorists" : "Counter-Terrorists";
            PrintToAllChat($"{teamName} is ready to unpause. Waiting for {waitingFor}...");
            return false;
        }
    }

    /// <summary>
    /// Start the auto-pause monitoring timer
    /// Call this when match goes live
    /// </summary>
public void StartAutoPauseCheck()
{
    if (!autoPauseEnabled.Value)
    {
        Log("[AutoPause] Auto-pause is disabled - not starting monitoring timer");
        return;
    }

    // Kill existing timer if any
    autoPauseCheckTimer?.Kill();

    // Check every 10 seconds for player count changes
    autoPauseCheckTimer = AddTimer(10.0f, () =>
    {
        if (isMatchLive)
        {
            CheckAutoResumeOrAutoPause();
        }
    }, TimerFlags.REPEAT);

    Log("[AutoPause] Started auto-pause monitoring with 10s interval");  // Changed this line
}

    /// <summary>
    /// Stop the auto-pause monitoring timer
    /// Call this when match ends
    /// </summary>
    public void StopAutoPauseCheck()
    {
        autoPauseCheckTimer?.Kill();
        autoPauseCheckTimer = null;

        isAutoPaused = false;
        autoPauseReason = null;
    }

    public void ResetTechPauses()
    {
        technicalPauseUsed.Clear();
        foreach (var team in reverseTeamSides.Values)
        {
            technicalPauseUsed[team] = 0;
        }

        lastTechPauseDuration = 0;

        // Reset auto-pause state
        isAutoPaused = false;
        autoPauseReason = null;

        if (pausedStateTimer != null)
        {
            pausedStateTimer.Kill();
            pausedStateTimer = null;
        }
    }
}
