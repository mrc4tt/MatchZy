using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        // No-show / walkover handling for loaded matches (both off by default):
        //   matchzy_forfeit_ready_timeout - a team that is not ready this many seconds after the
        //       ready phase of a map began loses the series; if neither team is ready the match is
        //       cancelled.
        //   matchzy_forfeit_leave_timeout - a team with nobody left on its side for this many
        //       seconds during a live map loses the series.
        // A forfeit ends the series like "get5_endmatch team1|team2", so panels get series_end
        // with the winner.

        private const float ForfeitCheckInterval = 5.0f;
        private CounterStrikeSharp.API.Modules.Timers.Timer? forfeitMonitorTimer = null;
        private DateTime? readyPhaseStartedAt = null;
        private readonly Dictionary<Team, DateTime> teamEmptySince = new();
        private int forfeitWarnedSecondsLeft = -1;

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
            // A force-ready belongs to one ready phase. It used to be cleared only by ResetMatch, so
            // a team forced ready before the veto or on map 1 stayed forced on the next map.
            foreach (var team in teamReadyOverride.Keys.ToList())
                teamReadyOverride[team] = false;
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

            int timeout = forfeitReadyTimeout.Value;
            if (timeout <= 0 || !readyAvailable || matchStarted || isVeto || readyPhaseStartedAt == null)
                return;

            double elapsed = (DateTime.UtcNow - readyPhaseStartedAt.Value).TotalSeconds;
            int secondsLeft = (int)Math.Ceiling(timeout - elapsed);

            bool team1Ready = IsTeamReady(SideOfTeam(matchzyTeam1));
            bool team2Ready = IsTeamReady(SideOfTeam(matchzyTeam2));
            if (team1Ready && team2Ready)
                return;

            if (secondsLeft > 0)
            {
                // A reminder at 5, 2 and 1 minute(s) and at 30 seconds.
                foreach (int mark in new[] { 300, 120, 60, 30 })
                {
                    if (secondsLeft <= mark && secondsLeft > mark - ForfeitCheckInterval && forfeitWarnedSecondsLeft != mark)
                    {
                        forfeitWarnedSecondsLeft = mark;
                        PrintLocalizedToAll("matchzy.matchmsg.forfeitreadywarning", secondsLeft);
                        break;
                    }
                }
                return;
            }

            readyPhaseStartedAt = null;
            if (!team1Ready && !team2Ready)
            {
                Log("[Forfeit] Neither team was ready in time - cancelling the match.");
                PrintLocalizedToAll("matchzy.matchmsg.forfeitnoneready");
                ResetMatch(true, "no_show");
                return;
            }

            Team winner = team1Ready ? matchzyTeam1 : matchzyTeam2;
            Team loser = team1Ready ? matchzyTeam2 : matchzyTeam1;
            ForfeitSeries(winner, $"{loser.teamName} was not ready in time", "matchzy.matchmsg.forfeitnotready", loser.teamName);
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

        /// <summary>
        /// Ends the series now with <paramref name="winner"/> as the winner: forfeit, .ffw, .gg and
        /// get5_endmatch team1|team2 all go through here so they report the same way. Stops the demo
        /// (so it is uploaded), sends map_result for a map in progress before series_end, gives the
        /// winner a series majority and writes the real map score (a hardcoded 16 used to be written,
        /// wrong under MR12). Between maps there is no map to report.
        /// </summary>
        private void EndSeriesWithWinner(Team winner)
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
                var (_, statsTeam1, statsTeam2) = GetPlayerStatsDict();
                string? demoFilename = demoWasRecording && !string.IsNullOrEmpty(lastStoppedDemoFile) ? Path.GetFileName(lastStoppedDemoFile) : null;
                var mapResultEvent = new MapResultEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    Winner = BuildWinnerFor(winner),
                    StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, matchzyTeam1.seriesScore, t1score, 0, 0, statsTeam1),
                    StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, matchzyTeam2.seriesScore, t2score, 0, 0, statsTeam2),
                    DemoFilename = demoFilename,
                };
                // Queued on the game thread before EndSeries queues series_end, so the event queue
                // sends map_result first.
                lastMapResultTask = SendEventAsync(mapResultEvent);
            }

            // The series is over: nobody may ready up into a knife round or veto before the reset.
            readyAvailable = false;
            isPreVeto = false;
            readyPhaseStartedAt = null;
            EndSeries(winner.teamName, 5, t1score, t2score);
        }
    }
}
