using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Ready-up events (player_ready, player_unready, team_ready, all_players_ready), unpause_requested,
    // demo_recording_start. The .admin / .report tickets are in AdminTickets.cs. Field names follow the MatchZy Enhanced
    // shapes that tournament panels already read. Ready events are match events: only sent while a
    // match is loaded.
    public partial class MatchZy
    {
        // Sides that were ready at the last check, so team_ready / all_players_ready fire once per
        // ready phase (again only after the side dropped out of ready).
        private bool _readyEvtCt, _readyEvtT, _readyEvtAll;

        // "team1" / "team2" for a side of a loaded match, "spectator" otherwise.
        private string TeamKeyForSide(int side)
        {
            if (side != (int)CsTeam.CounterTerrorist && side != (int)CsTeam.Terrorist)
                return "spectator";
            if (!reverseTeamSides.TryGetValue(side == (int)CsTeam.CounterTerrorist ? "CT" : "TERRORIST", out Team? team) || team == null)
                return "spectator";
            return team == matchzyTeam1 ? "team1" : "team2";
        }

        private (int Team1, int Team2) ReadyCountsByTeam()
        {
            (_, int ctReady) = GetTeamPlayerCount((int)CsTeam.CounterTerrorist);
            (_, int tReady) = GetTeamPlayerCount((int)CsTeam.Terrorist);
            return TeamKeyForSide((int)CsTeam.CounterTerrorist) == "team1" ? (ctReady, tReady) : (tReady, ctReady);
        }

        private int ExpectedReadyTotal() =>
            ReadyNeededOnSide((int)CsTeam.CounterTerrorist) + ReadyNeededOnSide((int)CsTeam.Terrorist);

        // .ready / .unready of one player. Call after playerReadyStatus changed.
        private void PublishPlayerReadyChanged(CCSPlayerController player, bool ready)
        {
            if (!isMatchSetup || !player.IsValid)
                return;
            var (team1, team2) = ReadyCountsByTeam();
            string team = TeamKeyForSide(player.TeamNum);
            PublishEvent(new PlayerReadyChangedEvent(ready ? "player_ready" : "player_unready")
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                Player = new ReadyEventPlayer { SteamId = player.SteamID.ToString(), Name = player.PlayerName, Team = team },
                Team = team,
                ReadyCountTeam1 = team1,
                ReadyCountTeam2 = team2,
                TotalReady = team1 + team2,
                ExpectedTotal = ExpectedReadyTotal(),
            });
        }

        // team_ready when a side becomes ready, all_players_ready when both are. Runs every second
        // from the ready timer (catches join mode, .forceready and players leaving) and right before
        // the match start check, so a quick last .ready still sends them before going_live.
        private void TrackReadyEvents()
        {
            if (!isMatchSetup || !readyAvailable || matchStarted || isDryRun || !readyEnabled.Value)
            {
                _readyEvtCt = _readyEvtT = _readyEvtAll = false;
                return;
            }
            bool ct = IsTeamReady((int)CsTeam.CounterTerrorist);
            bool t = IsTeamReady((int)CsTeam.Terrorist);
            var (team1, team2) = ReadyCountsByTeam();
            int expected = ExpectedReadyTotal();
            foreach (var (side, now, was) in new[] { ((int)CsTeam.CounterTerrorist, ct, _readyEvtCt), ((int)CsTeam.Terrorist, t, _readyEvtT) })
            {
                if (!now || was || IsBotSide(side))
                    continue;
                string team = TeamKeyForSide(side);
                PublishEvent(new TeamReadyEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    Team = team,
                    ReadyCount = team == "team1" ? team1 : team2,
                    TotalReady = team1 + team2,
                    ExpectedTotal = expected,
                    Forced = IsTeamForcedReady((CsTeam)side),
                });
            }
            _readyEvtCt = ct;
            _readyEvtT = t;
            bool all = ct && t;
            if (all && !_readyEvtAll)
            {
                PublishEvent(new AllPlayersReadyEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    ReadyCountTeam1 = team1,
                    ReadyCountTeam2 = team2,
                    TotalReady = team1 + team2,
                    CountdownStarted = IsJoinReadyMode(),
                });
            }
            _readyEvtAll = all;
        }

        // One team typed .unpause and the other still has to.
        private void PublishUnpauseRequested(int side)
        {
            if (!isMatchSetup)
                return;
            PublishEvent(new UnpauseRequestedEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                Team = TeamKeyForSide(side),
                TeamsReady = 1,
                TeamsNeeded = 2,
                RoundNumber = GetRoundNumer(),
            });
        }

        private void PublishDemoRecordingStart(string demoFile)
        {
            if (!isMatchSetup)
                return;
            PublishEvent(new DemoRecordingStartEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                FileName = Path.GetFileName(demoFile),
            });
        }
    }
}
