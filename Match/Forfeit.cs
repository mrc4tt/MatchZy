using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    public partial class MatchZy
    {
        // No-show / walkover handling for loaded matches (both off by default):
        //   matchzy_forfeit_ready_timeout - a team that is not ready this many seconds after the
        //       ready phase of a map began loses the series; if neither team is ready the series
        //       ends in a tie, as in Get5 (get5_time_to_start). The veto's ready-up can have its own
        //       limit (matchzy_forfeit_veto_ready_timeout, ReadyTime.cs).
        //   matchzy_forfeit_leave_timeout - a team with nobody left on its side for this many
        //       seconds during a live map loses the series.
        // A forfeit ends the series like "get5_endmatch team1|team2", so panels get series_end
        // with the winner.

        private const float ForfeitCheckInterval = 5.0f;
        private CounterStrikeSharp.API.Modules.Timers.Timer? forfeitMonitorTimer = null;
        private DateTime? readyPhaseStartedAt = null;
        private readonly Dictionary<Team, DateTime> teamEmptySince = new();
        private int forfeitWarnedSecondsLeft = -1;

        // matchzy_forfeit_join_grace_seconds: when a registered player reaches the server late in
        // the ready phase, the ready forfeit waits this long for them to pick a side and ready up.
        // A player who connected 30 seconds before the deadline used to lose the series for their
        // team while still choosing a side. Each player gets it once per ready phase (first seen
        // time is kept), so leaving and rejoining cannot stall the clock.
        private readonly Dictionary<ulong, (DateTime At, string Name)> readyPhaseArrivals = new();
        private readonly HashSet<ulong> joinGraceAnnounced = new();

        private void StartForfeitMonitor()
        {
            forfeitMonitorTimer?.Kill();
            forfeitMonitorTimer = AddTimer(ForfeitCheckInterval, CheckForfeits, TimerFlags.REPEAT);
        }

        // Called whenever a loaded match enters its ready phase (StartWarmup).
        private void MarkReadyPhaseStarted()
        {
            readyPhaseStartedAt = isMatchSetup ? DateTime.UtcNow : null;
            forfeitWarnedSecondsLeft = -1;
            // Logged so a forfeit can be checked against the limit that was actually in force (a
            // panel can set get5_time_to_start in the match config's cvars).
            if (readyPhaseStartedAt != null && CurrentReadyTimeout() > 0)
                Log($"[Forfeit] Ready phase started: a team that is not ready in {CurrentReadyTimeout()} s forfeits.");
            // Whoever is already here when the phase starts is not a late arrival.
            readyPhaseArrivals.Clear();
            joinGraceAnnounced.Clear();
            RecordReadyPhaseArrivals(DateTime.MinValue);
            // A force-ready belongs to one ready phase. It used to be cleared only by ResetMatch, so
            // a team forced ready before the veto or on map 1 stayed forced on the next map.
            foreach (var team in teamReadyOverride.Keys.ToList())
                teamReadyOverride[team] = false;
            // The ready data depends on isMatchSetup (roster-sized team requirements), which a
            // match load sets only after StartWarmup computed it.
            _readyStatusDirty = true;
        }

        private void CheckForfeits()
        {
            try
            {
                if (!isMatchSetup || isPractice || isDryRun || HasBotTeam())
                {
                    readyPhaseStartedAt = null;
                    teamEmptySince.Clear();
                    return;
                }

                CheckReadyForfeit();
                CheckLeaveForfeit();
            }
            catch (Exception e)
            {
                Log($"[Forfeit] {e.Message}");
            }
        }

        private int SideOfTeam(Team team) => teamSides.TryGetValue(team, out var side) && side == "CT" ? (int)CsTeam.CounterTerrorist : (int)CsTeam.Terrorist;

        private void CheckReadyForfeit()
        {
            // A started map ends the ready phase; the next one re-arms the clock in StartWarmup. Keeping
            // the old start time across the map change made a check between maps look long overdue.
            if (matchStarted)
                readyPhaseStartedAt = null;

            int timeout = CurrentReadyTimeout();
            if (timeout <= 0 || !readyAvailable || matchStarted || isVeto || readyPhaseStartedAt == null)
                return;
            // Before the restore check, so a player who joins during a restore gets the full grace.
            RecordReadyPhaseArrivals(DateTime.UtcNow);
            // As in Get5: the clock does not run while a round restore is waiting for the teams.
            if (isRoundRestorePending)
            {
                readyPhaseStartedAt = DateTime.UtcNow;
                return;
            }

            double elapsed = (DateTime.UtcNow - readyPhaseStartedAt.Value).TotalSeconds;
            int secondsLeft = (int)Math.Ceiling(timeout - elapsed);
            RecordReadyPhaseArrivals(DateTime.UtcNow);

            bool team1Ready = IsTeamReady(SideOfTeam(matchzyTeam1));
            bool team2Ready = IsTeamReady(SideOfTeam(matchzyTeam2));
            if (team1Ready && team2Ready)
                return;

            if (secondsLeft > 0)
            {
                // Get5's schedule: every minute, every 30 s in the last five minutes, and at 10 s.
                int? mark = DueReadyReminder(secondsLeft, ForfeitCheckInterval);
                if (mark != null && forfeitWarnedSecondsLeft != mark.Value)
                {
                    forfeitWarnedSecondsLeft = mark.Value;
                    PrintLocalizedToAll("matchzy.matchmsg.forfeitreadywarning", FormatReadyTime(secondsLeft));
                    // Name who is missing: a team that typed .ready while short-handed believed it
                    // was ready, and only learned otherwise from the forfeit.
                    if (!team1Ready)
                        PrintMissingPlayers(matchzyTeam1);
                    if (!team2Ready)
                        PrintMissingPlayers(matchzyTeam2);
                }
                return;
            }

            foreach (Team team in new[] { matchzyTeam1, matchzyTeam2 })
            {
                if ((team == matchzyTeam1 ? team1Ready : team2Ready) || JoinGraceFor(team) is not (ulong steamId, string name, int graceLeft))
                    continue;
                if (joinGraceAnnounced.Add(steamId))
                {
                    Log($"[Forfeit] Ready time is up, waiting up to {graceLeft} s for {name} ({team.teamName}) who joined late.");
                    PrintLocalizedToAll("matchzy.matchmsg.forfeitjoingrace", team.teamName, name, graceLeft);
                }
                return;
            }

            readyPhaseStartedAt = null;
            if (!team1Ready && !team2Ready)
            {
                // Get5: no winner, the series ends in a tie (series_end winner none) instead of a cancel,
                // so panels record it as a no-show draw.
                Log("[Forfeit] Neither team was ready in time - the series ends in a tie.");
                PrintLocalizedToAll("matchzy.matchmsg.forfeitnoneready");
                EndSeriesWithoutWinner();
                return;
            }

            Team winner = team1Ready ? matchzyTeam1 : matchzyTeam2;
            Team loser = team1Ready ? matchzyTeam2 : matchzyTeam1;
            ForfeitSeries(winner, $"{loser.teamName} was not ready in time", "matchzy.matchmsg.forfeitnotready", loser.teamName);
        }

        // Notes the first time each human is seen on the server (connecting counts) during this
        // ready phase. Nothing is overwritten, so a rejoin keeps the first time.
        private void RecordReadyPhaseArrivals(DateTime at)
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (p is not { IsValid: true, IsBot: false, IsHLTV: false } || p.SteamID == 0)
                    continue;
                if (p.Connected is not (PlayerConnectedState.Connected or PlayerConnectedState.Connecting or PlayerConnectedState.Reconnecting))
                    continue;
                readyPhaseArrivals.TryAdd(p.SteamID, (at, p.PlayerName));
            }
        }

        // A registered player of this team who joined late, is still on the server and is within
        // the join grace. The latest arrival wins. Null when the team has nobody to wait for.
        private (ulong SteamId, string Name, int SecondsLeft)? JoinGraceFor(Team team)
        {
            int grace = forfeitJoinGraceSeconds.Value;
            if (grace <= 0 || team.teamPlayers == null)
                return null;
            var onServer = Utilities.GetPlayers()
                .Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false } && p.Connected != PlayerConnectedState.Disconnected)
                .Select(p => p.SteamID)
                .ToHashSet();
            DateTime now = DateTime.UtcNow;
            (ulong, string, int)? latest = null;
            DateTime latestUntil = DateTime.MinValue;
            foreach (var (steamId, arrival) in readyPhaseArrivals)
            {
                DateTime until = arrival.At == DateTime.MinValue ? DateTime.MinValue : arrival.At.AddSeconds(grace);
                if (until <= now || until <= latestUntil || !onServer.Contains(steamId) || !LookupRosterEntry(team.teamPlayers, steamId))
                    continue;
                latestUntil = until;
                latest = (steamId, arrival.Name, (int)Math.Ceiling((until - now).TotalSeconds));
            }
            return latest;
        }

        // "Herning: 4/5 players on the server. Missing: Mio0p." for a team still waiting for
        // registered players. Names come from the roster; when it lists more players than the team
        // needs (substitutes), only the number missing is shown.
        private void PrintMissingPlayers(Team team)
        {
            int side = SideOfTeam(team);
            if (ShortHandedSide(side) is not (int present, int required))
                return;
            int missing = required - present;
            string who = missing.ToString();
            if (team.teamPlayers is JObject roster)
            {
                var onSide = Utilities.GetPlayers()
                    .Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false } && p.TeamNum == side)
                    .Select(p => p.SteamID)
                    .ToHashSet();
                var names = roster.Properties()
                    .Where(e => ulong.TryParse(e.Name, out ulong id) && !onSide.Contains(id) && !LookupRosterEntry(team.teamCoaches, id))
                    .Select(e => e.Value.Type == JTokenType.String && !string.IsNullOrWhiteSpace(e.Value.ToString()) ? e.Value.ToString() : e.Name)
                    .ToList();
                if (names.Count == missing)
                    who = string.Join(", ", names);
            }
            PrintLocalizedToAll("matchzy.matchmsg.forfeitmissingplayers", team.teamName, present, required, who);
        }

        // .tech / .pause / .tac before a loaded match is live: a pause does not exist yet and does not
        // stop the ready time. Used to do nothing at all, so a team kept typing .tech and lost on time.
        // True when it replied (the caller then stops).
        private bool ReplyPauseInReadyPhase(CCSPlayerController? player)
        {
            if (player == null || !isMatchSetup || !readyAvailable || matchStarted || isMatchLive)
                return false;
            int timeout = CurrentReadyTimeout();
            if (timeout > 0 && readyPhaseStartedAt != null && !isVeto)
            {
                int secondsLeft = (int)Math.Ceiling(timeout - (DateTime.UtcNow - readyPhaseStartedAt.Value).TotalSeconds);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pause.readyphasetime", FormatReadyTime(secondsLeft)));
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pause.readyphase"));
            }
            return true;
        }

        private void CheckLeaveForfeit()
        {
            int timeout = forfeitLeaveTimeout.Value;
            if (timeout <= 0 || !isMatchLive)
            {
                teamEmptySince.Clear();
                return;
            }

            // Both sides empty at once is not two teams leaving: at halftime the side mapping is
            // swapped at round end while the engine moves the players only at the restart, so for a
            // few seconds each team is looked up on the side it is about to leave.
            if (GetTeamPlayerCount(CsTeam.CounterTerrorist) == 0 && GetTeamPlayerCount(CsTeam.Terrorist) == 0)
            {
                teamEmptySince.Clear();
                return;
            }
            if (IsHalfTimePhase())
                return;

            foreach (Team team in new[] { matchzyTeam1, matchzyTeam2 })
            {
                int humans = GetTeamPlayerCount((CsTeam)SideOfTeam(team));
                if (humans > 0)
                {
                    teamEmptySince.Remove(team);
                    continue;
                }

                if (!teamEmptySince.TryGetValue(team, out DateTime since))
                {
                    teamEmptySince[team] = DateTime.UtcNow;
                    PrintLocalizedToAll("matchzy.matchmsg.forfeitteamempty", team.teamName, timeout);
                    continue;
                }

                if ((DateTime.UtcNow - since).TotalSeconds >= timeout)
                {
                    teamEmptySince.Clear();
                    Team winner = team == matchzyTeam1 ? matchzyTeam2 : matchzyTeam1;
                    ForfeitSeries(winner, $"{team.teamName} left the match", "matchzy.matchmsg.forfeitleft", team.teamName);
                    return;
                }
            }
        }

        /// <summary>Ends the series with <paramref name="winner"/> as the winner (walkover).</summary>
        private void ForfeitSeries(Team winner, string reason, string reasonKey, string loserName)
        {
            Log($"[Forfeit] {reason}. {winner.teamName} wins by forfeit.");
            PrintLocalizedToAll(reasonKey, loserName, winner.teamName);
            EndSeriesWithWinner(winner);
        }

        // Ends the series with no winner (a tie), from the ready phase: no map is in progress, so
        // there is no map_result, only series_end.
        private void EndSeriesWithoutWinner()
        {
            (int t1score, int t2score) = GetTeamsScore();
            if (isDemoRecording || previousDemoSegments.Count > 0)
                StopDemoRecording(activeDemoFile, liveMatchId, matchConfig.CurrentMapNumber);
            readyAvailable = false;
            isPreVeto = false;
            readyPhaseStartedAt = null;
            EndSeries(null, 5, t1score, t2score, noWinner: true);
        }

        /// <summary>
        /// Ends the series now with <paramref name="winner"/> as the winner: forfeit, .ffw, .gg and
        /// get5_endmatch team1|team2 all go through here so they report the same way. Stops the demo
        /// (so it is uploaded), sends map_result for a map in progress before series_end, gives the
        /// winner a series majority and writes the real map score (a hardcoded 16 used to be written,
        /// wrong under MR12). Between maps there is no map to report.
        /// </summary>
        // kickPlayers false: get5_endmatch team1|team2. As in Get5, a forcefully ended match does not
        // kick the players (matchzy_kick_when_no_match_loaded), only a series that ends by itself.
        private void EndSeriesWithWinner(Team winner, bool kickPlayers = true)
        {
            (int t1score, int t2score) = GetTeamsScore();
            // Only a demo stopped here belongs to this map; lastStoppedDemoFile can still hold the
            // previous map's file. previousDemoSegments: a failed watchdog restart leaves earlier
            // parts to finalise and upload even when isDemoRecording is false.
            bool demoWasRecording = isDemoRecording || previousDemoSegments.Count > 0;
            if (demoWasRecording)
                StopDemoRecording(activeDemoFile, liveMatchId, matchConfig.CurrentMapNumber);
            winner.seriesScore = Math.Max(winner.seriesScore, (matchConfig.NumMaps / 2) + 1);

            if (isMatchLive && !currentMapFinished)
            {
                var (playerStatsDictionary, statsTeam1, statsTeam2) = GetPlayerStatsDict();
                string? demoFilename = demoWasRecording && !string.IsNullOrEmpty(lastStoppedDemoFile) ? Path.GetFileName(lastStoppedDemoFile) : null;
                var mapResultEvent = new MapResultEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    Winner = BuildWinnerFor(winner),
                    StatsTeam1 = BuildStatsTeam(matchzyTeam1, t1score, statsTeam1),
                    StatsTeam2 = BuildStatsTeam(matchzyTeam2, t2score, statsTeam2),
                    DemoFilename = demoFilename,
                };
                // Queued on the game thread before EndSeries queues series_end, so the event queue
                // sends map_result first.
                lastMapResultTask = SendEventAsync(mapResultEvent);

                // A forfeit, .gg or get5_endmatch ends the map here, not in HandleMatchEnd, so the
                // final stats write, the CSV export and the stats JSON for this map were skipped:
                // the database kept the last round_end snapshot. Same block as HandleMatchEnd
                // (series end data is written by EndSeries). Natives captured on the game thread.
                long matchId = liveMatchId;
                int mapNumber = matchConfig.CurrentMapNumber;
                string statsPath = Server.GameDirectory + "/csgo/MatchZy_Stats/" + liveMatchId;
                MatchStatsJson? export = CollectMatchStatsForExport(demoFilename ?? string.Empty, t1score, t2score, statsTeam1, statsTeam2);
                Task.Run(async () =>
                {
                    if (playerStatsDictionary.Count > 0)
                        await database.UpdatePlayerStatsAsync(matchId, mapNumber, playerStatsDictionary.ToDictionary(kvp => (long)kvp.Key, kvp => kvp.Value));
                    await database.WritePlayerStatsToCsvAsync(statsPath, matchId, mapNumber);
                    if (export != null && demoFilename != null)
                        await WriteMatchStatsJsonAsync(export, demoFilename, statsPath);
                });
            }

            // The series is over: nobody may ready up into a knife round or veto before the reset.
            readyAvailable = false;
            isPreVeto = false;
            readyPhaseStartedAt = null;
            EndSeries(winner.teamName, 5, t1score, t2score, kickPlayers: kickPlayers);
        }
    }
}
