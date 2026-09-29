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
                        PrintToAllChat($"A team that is not ready in {ChatColors.Green}{secondsLeft}{ChatColors.Default} seconds forfeits the match.");
                        break;
                    }
                }
                return;
            }

            readyPhaseStartedAt = null;
            if (!team1Ready && !team2Ready)
            {
                Log("[Forfeit] Neither team was ready in time - cancelling the match.");
                PrintToAllChat("Neither team was ready in time. The match is cancelled.");
                ResetMatch(true, "no_show");
                return;
            }

            Team winner = team1Ready ? matchzyTeam1 : matchzyTeam2;
            Team loser = team1Ready ? matchzyTeam2 : matchzyTeam1;
            ForfeitSeries(winner, $"{loser.teamName} was not ready in time");
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
                    PrintToAllChat($"{ChatColors.Green}{team.teamName}{ChatColors.Default} has no players left. They forfeit in {ChatColors.Green}{timeout}{ChatColors.Default} seconds unless someone returns.");
                    continue;
                }

                if ((DateTime.UtcNow - since).TotalSeconds >= timeout)
                {
                    teamEmptySince.Clear();
                    Team winner = team == matchzyTeam1 ? matchzyTeam2 : matchzyTeam1;
                    ForfeitSeries(winner, $"{team.teamName} left the match");
                    return;
                }
            }
        }

        /// <summary>Ends the series with <paramref name="winner"/> as the winner (walkover).</summary>
        private void ForfeitSeries(Team winner, string reason)
        {
            Log($"[Forfeit] {reason}. {winner.teamName} wins by forfeit.");
            PrintToAllChat($"{reason}. {ChatColors.Green}{winner.teamName}{ChatColors.Default} wins by forfeit.");
            (int t1score, int t2score) = GetTeamsScore();
            if (isDemoRecording)
                StopDemoRecording(activeDemoFile, liveMatchId, matchConfig.CurrentMapNumber);
            winner.seriesScore = Math.Max(winner.seriesScore, (matchConfig.NumMaps / 2) + 1);
            // The series is over: nobody may ready up into a knife round or veto before the reset.
            readyAvailable = false;
            isPreVeto = false;
            readyPhaseStartedAt = null;
            EndSeries(winner.teamName, 5, t1score, t2score);
        }
    }
}
