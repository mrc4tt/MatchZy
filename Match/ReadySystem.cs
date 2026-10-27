using Newtonsoft.Json.Linq;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<CsTeam, bool> teamReadyOverride = new()
    {
        { CsTeam.Terrorist, false },
        { CsTeam.CounterTerrorist, false },
        { CsTeam.Spectator, false },
    };

    public bool allowForceReady = true;

    public bool IsTeamsReady()
    {
        return IsTeamReady((int)CsTeam.CounterTerrorist) && IsTeamReady((int)CsTeam.Terrorist);
    }

    public bool IsSpectatorsReady()
    {
        return IsTeamReady((int)CsTeam.Spectator);
    }

    public bool IsTeamReady(int team)
    {
        // if (matchStarted) return true;

        // The bot team cannot type .ready.
        if (IsBotSide(team))
            return true;

        // matchzy_ready_mode 1: joining the side is enough, nobody types .ready.
        bool? joined = IsTeamJoinedReady(team);
        if (joined.HasValue)
            return joined.Value;

        // matchzy_ready_per_team: N ready players make the whole side ready (e.g. one per team),
        // instead of every player on the roster.
        int perTeam = EffectiveReadyPerTeam();
        if (perTeam > 0 && (team == (int)CsTeam.CounterTerrorist || team == (int)CsTeam.Terrorist))
        {
            (int sidePlayers, int sideReady) = GetTeamPlayerCount(team, false);
            // .forceready readies everyone on the side, so honor it even when the side has fewer
            // than N players (otherwise a short-handed team could never start the match).
            if (sidePlayers > 0 && IsTeamForcedReady((CsTeam)team))
                return true;
            // Loaded match: N ready players only confirm for the team; its registered players
            // (roster size, at most players_per_team) must still all be on the side, so the match
            // cannot start while one of them is missing. A pug has no roster and only needs N.
            if (isMatchSetup && sidePlayers < RequiredPlayersOnSide(team))
                return false;
            return sidePlayers > 0 && sideReady >= perTeam;
        }

        int minPlayers = GetPlayersPerTeam(team);
        int minReady = ForceReadyMinimum(team);
        (int playerCount, int readyCount) = GetTeamPlayerCount(team, false);

        if (team == (int)CsTeam.Spectator && minReady == 0)
        {
            return true;
        }

        if (readyAvailable && playerCount == 0)
        {
            // We cannot ready for veto with no players, regardless of force status or min_players_to_ready.
            return false;
        }

        if (playerCount == readyCount && playerCount >= minPlayers)
        {
            return true;
        }

        // The match config's min_players_to_ready: that many ready players make the side ready,
        // without .forceready and without waiting for the rest of the roster.
        int configMin = ConfigReadyMinimum(team);
        if (configMin > 0 && readyCount >= configMin)
        {
            return true;
        }

        if (IsTeamForcedReady((CsTeam)team) && readyCount >= minReady)
        {
            return true;
        }

        return false;
    }

    public int GetPlayersPerTeam(int team)
    {
        if (team == (int)CsTeam.CounterTerrorist || team == (int)CsTeam.Terrorist)
            return RequiredPlayersOnSide(team);
        if (team == (int)CsTeam.Spectator)
            return matchConfig.MinSpectatorsToReady;
        return 0;
    }

    // Players a side needs before .ready alone makes it ready: players_per_team, or fewer when the
    // team on that side has fewer players in its match config roster. A team registered with 4
    // players in a 5v5 then readies up as 4 without .forceready; a team of 5 still needs all 5.
    // players_per_team stays the most a side can have, matchzy_ready_max_missing the fewest.
    // Coaches listed under "players" do not count, and open ("any") and bot rosters keep
    // players_per_team.
    private int RequiredPlayersOnSide(int side)
    {
        int perTeam = matchConfig.PlayersPerTeam;
        if (!isMatchSetup || (side != (int)CsTeam.CounterTerrorist && side != (int)CsTeam.Terrorist))
            return perTeam;
        if (!reverseTeamSides.TryGetValue(side == (int)CsTeam.CounterTerrorist ? "CT" : "TERRORIST", out Team? team))
            return perTeam;
        return ReadyLogic.RequiredOnSide(perTeam, RosterPlayerCount(team), ReadyFloor());
    }

    // matchzy_ready_max_missing: the fewest players a side of a loaded match can ready up with
    // (roster size, min_players_to_ready, join mode and .forceready all stop there). 0 = off.
    private int ReadyFloor()
    {
        return isMatchSetup ? ReadyLogic.ReadyFloor(matchConfig.PlayersPerTeam, readyMaxMissing.Value) : 0;
    }

    // Players a side needs ready when the match config sets min_players_to_ready below what the
    // side would otherwise need (players_per_team or the roster size), never below
    // matchzy_ready_max_missing. 0 = not set, or not lower: then every player on the side has to be
    // ready. Panels often send min_players_to_ready 1, which let one .ready per team start a 5v5.
    private int ConfigReadyMinimum(int side)
    {
        if (!isMatchSetup || (side != (int)CsTeam.CounterTerrorist && side != (int)CsTeam.Terrorist))
            return 0;
        return ReadyLogic.ConfigMinimum(matchConfig.MinPlayersToReadySet, matchConfig.MinPlayersToReady, RequiredPlayersOnSide(side), ReadyFloor());
    }

    // Players a side must have ready before it counts as ready by .ready alone.
    private int ReadyNeededOnSide(int side)
    {
        int configMin = ConfigReadyMinimum(side);
        return configMin > 0 ? configMin : RequiredPlayersOnSide(side);
    }

    private static int RosterPlayerCount(Team team)
    {
        if (team.openRoster || team.botTeam || team.teamPlayers == null)
            return 0;
        IEnumerable<string> steamIds = team.teamPlayers switch
        {
            JObject rosterObject => rosterObject.Properties().Select(p => p.Name),
            JArray rosterArray => rosterArray.Select(e => e.ToString()),
            _ => Enumerable.Empty<string>(),
        };
        return steamIds.Count(id => !ulong.TryParse(id, out ulong steamId) || !LookupRosterEntry(team.teamCoaches, steamId));
    }

    // A side that is still waiting for registered players who have not joined. Null otherwise
    // (complete, ready, nobody on it). Not used in join ready mode, which has its own countdown.
    // Only informs: the hint does not push .forceready, which stays available to teams that know it.
    private (int Present, int Required)? ShortHandedSide(int side)
    {
        if (!isMatchSetup || !readyAvailable || matchStarted || IsJoinReadyMode())
            return null;
        if (side != (int)CsTeam.CounterTerrorist && side != (int)CsTeam.Terrorist)
            return null;
        int required = ReadyNeededOnSide(side);
        (int present, _) = GetTeamPlayerCount(side, false);
        if (present <= 0 || present >= required || IsTeamReady(side))
            return null;
        return (present, required);
    }

    // Players a side needs for .forceready: min_players_to_ready, but never fewer than
    // players_per_team - matchzy_forceready_max_missing (default 1: at most one player short, so
    // 4 in a 5v5). A match config value can only make it stricter. Panels often send
    // min_players_to_ready 1 without meaning it, which let a single player force-start a 5v5.
    // -1 = only min_players_to_ready (Get5's rule). matchzy_ready_max_missing still applies.
    // Spectators keep min_spectators_to_ready.
    private int ForceReadyMinimum(int side)
    {
        int minReady = GetTeamMinReady(side);
        if (side != (int)CsTeam.CounterTerrorist && side != (int)CsTeam.Terrorist)
            return minReady;
        return ReadyLogic.ForceReadyMinimum(matchConfig.PlayersPerTeam, minReady, forceReadyMaxMissing.Value, ReadyFloor());
    }

    public int GetTeamMinReady(int team)
    {
        if (team == (int)CsTeam.CounterTerrorist || team == (int)CsTeam.Terrorist)
            return matchConfig.MinPlayersToReady;
        if (team == (int)CsTeam.Spectator)
            return matchConfig.MinSpectatorsToReady;
        return 0;
    }

    public (int, int) GetTeamPlayerCount(int team, bool includeCoaches = false)
    {
        int playerCount = 0;
        int readyCount = 0;
        foreach (var key in playerData.Keys)
        {
            if (!playerData[key].IsValid)
                continue;
            // Coaches do not play, so they neither fill a player slot nor need to ready up.
            if (!includeCoaches && (matchzyTeam1.coach.Contains(playerData[key]) || matchzyTeam2.coach.Contains(playerData[key])))
                continue;
            if (playerData[key].TeamNum == team)
            {
                playerCount++;
                if (playerReadyStatus[key] == true)
                    readyCount++;
            }
        }

        return (playerCount, readyCount);
    }

    // Ready players a side contributes toward matchzy_ready_per_team (capped at N; a ready side
    // counts as full, so .forceready and the bot side show as complete).
    public int CountedReadyForSide(CsTeam side, int perTeam)
    {
        if (IsTeamReady((int)side))
            return perTeam;
        (_, int sideReady) = GetTeamPlayerCount((int)side, false);
        return Math.Min(sideReady, perTeam);
    }

    // matchzy_ready_enabled false: players cannot ready up, only an admin starts the match (.start).
    // Tells the player so. True when the ready-up is off (the caller then stops).
    private bool ReplyReadyDisabled(CCSPlayerController? player)
    {
        if (readyEnabled.Value)
            return false;
        if (player != null && player.IsValid)
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.disabled"));
        return true;
    }

    public bool IsTeamForcedReady(CsTeam team)
    {
        return teamReadyOverride[team];
    }

    [ConsoleCommand("css_forceready", "Force-readies the team")]
    public void OnForceReadyCommandCommand(CCSPlayerController? player, CommandInfo? command)
    {
        Log($"{readyAvailable} {isMatchSetup} {allowForceReady} {IsPlayerValid(player)}");
        if (!readyAvailable || !isMatchSetup || !allowForceReady || !IsPlayerValid(player))
            return;
        if (matchStarted || ReplyReadyDisabled(player))
            return;

        int minReady = ForceReadyMinimum(player!.TeamNum);
        (int playerCount, int readyCount) = GetTeamPlayerCount(player!.TeamNum, false);

        if (playerCount < minReady)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rs.minreadyplayers", minReady));
            return;
        }

        foreach (var key in playerData.Keys)
        {
            if (!playerData[key].IsValid)
                continue;
            if (playerData[key].TeamNum == player.TeamNum)
            {
                playerReadyStatus[key] = true;
                ReplyToUserCommand(playerData[key], Localizer.ForPlayer(player, "matchzy.rs.forcereadiedby", player.PlayerName));
            }
        }

        teamReadyOverride[(CsTeam)player.TeamNum] = true;
        _readyStatusDirty = true;
        CheckLiveRequired();
    }
}
