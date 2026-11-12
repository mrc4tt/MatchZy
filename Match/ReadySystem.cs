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

    // Rostered players who coach (.coach / css_coach without a "coaches" list in the match config)
    // do not play, so they are not counted either: a roster of 5 with one coaching readies up as 4.
    private int RosterPlayerCount(Team team)
    {
        HashSet<ulong> coaching = new();
        foreach (var coach in matchzyTeam1.coach.Concat(matchzyTeam2.coach))
            if (coach != null && coach.IsValid)
                coaching.Add(coach.SteamID);
        return RosterPlayerIds(team).Count(id => !ulong.TryParse(id, out ulong steamId) || !coaching.Contains(steamId));
    }

    // Players listed in the team's match config roster, without the ones also listed as coaches.
    // 0 for an open ("any") or bot team or no roster.
    private static int ListedRosterSize(Team team) => RosterPlayerIds(team).Count();

    private static IEnumerable<string> RosterPlayerIds(Team team)
    {
        if (team.openRoster || team.botTeam || team.teamPlayers == null)
            return Enumerable.Empty<string>();
        IEnumerable<string> steamIds = team.teamPlayers switch
        {
            JObject rosterObject => rosterObject.Properties().Select(p => p.Name),
            JArray rosterArray => rosterArray.Select(e => e.ToString()),
            _ => Enumerable.Empty<string>(),
        };
        return steamIds.Where(id => !ulong.TryParse(id, out ulong steamId) || !LookupRosterEntry(team.teamCoaches, steamId));
    }

    // .readycheck in a loaded match: one block per team. The team's players on its side against what
    // it needs, how many are ready against what it needs, and by name the registered players who are
    // not on the side (still connecting, on Spectator or not on the server) and the players on the
    // side who have not typed .ready. A side that is ready gets a single line.
    private List<string> ReadyCheckTeamLines(CCSPlayerController? viewer)
    {
        var lines = new List<string>();
        int perTeam = EffectiveReadyPerTeam();
        foreach (string sideKey in new[] { "CT", "TERRORIST" })
        {
            if (!reverseTeamSides.TryGetValue(sideKey, out Team? team) || team == null)
                continue;
            int side = sideKey == "CT" ? (int)CsTeam.CounterTerrorist : (int)CsTeam.Terrorist;
            string sideName = sideKey == "CT" ? "CT" : "T";
            if (IsTeamReady(side))
            {
                lines.Add(Localizer.ForPlayer(viewer, "matchzy.cmd.readycheck.teamready", team.teamName, sideName));
                continue;
            }
            if (IsBotSide(side))
                continue;

            (int present, int ready) = GetTeamPlayerCount(side, false);
            int required = RequiredPlayersOnSide(side);
            int readyNeeded = perTeam > 0 ? perTeam : ReadyNeededOnSide(side);
            lines.Add(Localizer.ForPlayer(viewer, "matchzy.cmd.readycheck.team", team.teamName, sideName,
                present, required, Math.Min(ready, readyNeeded), readyNeeded));

            var missing = MissingRosterNames(side, viewer, markSpectators: true);
            if (missing.Count > 0 && present < required)
                lines.Add(Localizer.ForPlayer(viewer, "matchzy.cmd.readycheck.missing", ShortNameList(missing)));

            // Players on the side who have not typed .ready, while the side still needs ready players.
            if (ready < readyNeeded)
            {
                var notReady = playerData
                    .Where(kv => IsReadyGatePlayer(kv.Value) && kv.Value.TeamNum == side
                        && !(playerReadyStatus.TryGetValue(kv.Key, out bool r) && r))
                    .Select(kv => kv.Value.PlayerName)
                    .ToList();
                if (notReady.Count > 0)
                    lines.Add(Localizer.ForPlayer(viewer, "matchzy.cmd.readycheck.notready", ShortNameList(notReady)));
            }
        }
        return lines;
    }

    // Registered players of the team on `side` who are not on that side (still connecting, on
    // Spectator, on the other side or not on the server), by name: the in-game name when connected,
    // else the roster's name, else the SteamID64. Coaches are skipped. Empty for an open ("any") or
    // bot team, which has no roster. markSpectators adds "(Spectator)" for the viewer's language.
    private List<string> MissingRosterNames(int side, CCSPlayerController? viewer, bool markSpectators)
    {
        var missing = new List<string>();
        string sideKey = side == (int)CsTeam.CounterTerrorist ? "CT" : "TERRORIST";
        if (!isMatchSetup || !reverseTeamSides.TryGetValue(sideKey, out Team? team) || team == null)
            return missing;
        JObject? rosterNames = team.teamPlayers as JObject;
        foreach (string id in RosterPlayerIds(team))
        {
            if (!ulong.TryParse(id, out ulong steamId))
                continue;
            CCSPlayerController? connected = playerData.Values.FirstOrDefault(p => p != null && p.IsValid && p.SteamID == steamId);
            if (connected != null && (IsMatchCoach(connected) || connected.TeamNum == side))
                continue;
            string name = connected?.PlayerName ?? rosterNames?[id]?.ToString() ?? id;
            if (string.IsNullOrWhiteSpace(name))
                name = id;
            missing.Add(markSpectators && connected != null && connected.TeamNum == (int)CsTeam.Spectator
                ? Localizer.ForPlayer(viewer, "matchzy.cmd.readycheck.onspectator", name)
                : name);
        }
        return missing;
    }

    // Names for a chat line: at most `max`, then "+N".
    private static string ShortNameList(List<string> names, int max = 6)
    {
        string list = string.Join(", ", names.Take(max));
        return names.Count > max ? $"{list} +{names.Count - max}" : list;
    }

    // The short-handed line for the ready panel / classic center text: with the names of the
    // missing registered players (at most 2, kept short for the panel's size cap) when the roster
    // knows them, else the bare count.
    private string ShortHandedLine(CCSPlayerController viewer, int side, int present, int required)
    {
        var names = MissingRosterNames(side, viewer, markSpectators: false);
        if (names.Count == 0)
            return Localizer.ForPlayer(viewer, "matchzy.ready.shorthanded", present, required, required - present);
        string list = ShortNameList(names, 2);
        if (list.Length > 48)
            list = list.Substring(0, 48) + "...";
        return Localizer.ForPlayer(viewer, "matchzy.ready.shorthandednames", present, required, list);
    }

    // Players that take part in the ready-up: on T or CT and not coaching. Spectators and coaches
    // cannot ready up and never count towards it.
    private bool IsReadyGatePlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid
            && (player.TeamNum == (int)CsTeam.CounterTerrorist || player.TeamNum == (int)CsTeam.Terrorist)
            && !IsMatchCoach(player);
    }

    // Ready-up players (see IsReadyGatePlayer) and how many of them are ready.
    private (int Players, int Ready) CountReadyGatePlayers()
    {
        int players = 0, ready = 0;
        foreach (var (userId, p) in playerData)
        {
            if (!IsReadyGatePlayer(p))
                continue;
            players++;
            if (playerReadyStatus.TryGetValue(userId, out bool r) && r)
                ready++;
        }
        return (players, ready);
    }

    // .ready / .unready / ping from a spectator or a coach: tell them and ignore it.
    private bool RejectNonPlayerReady(CCSPlayerController player)
    {
        if (IsReadyGatePlayer(player))
            return false;
        // A loaded match with min_spectators_to_ready waits for that many ready spectators
        // (IsSpectatorsReady): rejecting their .ready made the match impossible to start.
        if (isMatchSetup && matchConfig.MinSpectatorsToReady > 0 && player.IsValid
            && player.TeamNum == (int)CsTeam.Spectator && !IsMatchCoach(player))
            return false;
        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.ready.playersonly"));
        return true;
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

    // How long the short-handed line stays hidden after a player joins a side (or the side first
    // gets a player): players are often still loading in, and "2 missing" while they do reads as
    // if something is wrong.
    private const int ShortHandedHintGraceSeconds = 30;
    private readonly Dictionary<int, (int Present, DateTime At)> shortHandedLastJoin = new();

    // ShortHandedSide for the ready hints (panel, center text, chat), hidden for
    // ShortHandedHintGraceSeconds after the side last gained a player. Forfeit messages and other
    // logic keep using ShortHandedSide directly.
    private (int Present, int Required)? ShortHandedHintSide(int side)
    {
        var shortHanded = ShortHandedSide(side);
        (int present, _) = GetTeamPlayerCount(side, false);
        DateTime now = DateTime.UtcNow;
        if (!shortHandedLastJoin.TryGetValue(side, out var last) || present > last.Present)
            shortHandedLastJoin[side] = (present, now);
        else if (present < last.Present)
            shortHandedLastJoin[side] = (present, last.At);
        if (shortHanded == null)
            return null;
        return now - shortHandedLastJoin[side].At < TimeSpan.FromSeconds(ShortHandedHintGraceSeconds) ? null : shortHanded;
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
