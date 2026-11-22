using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;

namespace MatchZy
{
    // .bot stopped at five per side with "the team is full": the competitive game mode allows
    // five players on a team, and the server refuses a bot for a full team. The limit is kept in
    // the game rules (m_iMaxNumTerrorists / m_iMaxNumCTs, server side only), so practice raises it
    // there and puts the map's own values back when it ends. The number of spawn points of the map
    // and the server's player slots (-maxplayers) still apply.
    public partial class MatchZy
    {
        private const int PracticeTeamSizeLimit = 32;
        private int _teamSizeBeforePracticeT = -1;
        private int _teamSizeBeforePracticeCt = -1;

        // Called at practice start and before every bot add: a round restart can write the game
        // mode's values back.
        private void RaisePracticeTeamSize()
        {
            try
            {
                var rules = GetGameRules();
                if (rules == null)
                    return;
                if (rules.MaxNumTerrorists < PracticeTeamSizeLimit)
                {
                    if (_teamSizeBeforePracticeT < 0)
                        _teamSizeBeforePracticeT = rules.MaxNumTerrorists;
                    Schema.SetSchemaValue(rules.Handle, "CCSGameRules", "m_iMaxNumTerrorists", PracticeTeamSizeLimit);
                }
                if (rules.MaxNumCTs < PracticeTeamSizeLimit)
                {
                    if (_teamSizeBeforePracticeCt < 0)
                        _teamSizeBeforePracticeCt = rules.MaxNumCTs;
                    Schema.SetSchemaValue(rules.Handle, "CCSGameRules", "m_iMaxNumCTs", PracticeTeamSizeLimit);
                }
            }
            catch (Exception e)
            {
                Log($"[PracticeTeamSize] {e.Message}");
            }
            SyncTeamPlayerCounts();
        }

        // The "team is full" check of bot_add compares the game rules' own head count
        // (m_iNumTerrorist / m_iNumCT) with the limit, and that count is only refreshed at certain
        // points (round restart, team change). After .nobots kicked a full side it still held the
        // old number, and the next .bot was refused with "Could not add bot to the game: Team is
        // full" on an empty team. Written fresh before every bot add.
        private void SyncTeamPlayerCounts()
        {
            try
            {
                var rules = GetGameRules();
                if (rules == null)
                    return;
                int t = 0, ct = 0;
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p?.IsValid != true || p.IsHLTV || p.Connected != PlayerConnectedState.Connected)
                        continue;
                    if (p.TeamNum == (byte)CsTeam.Terrorist) t++;
                    else if (p.TeamNum == (byte)CsTeam.CounterTerrorist) ct++;
                }
                if (rules.NumTerrorist != t)
                    Schema.SetSchemaValue(rules.Handle, "CCSGameRules", "m_iNumTerrorist", t);
                if (rules.NumCT != ct)
                    Schema.SetSchemaValue(rules.Handle, "CCSGameRules", "m_iNumCT", ct);
            }
            catch (Exception e)
            {
                Log($"[PracticeTeamSize] {e.Message}");
            }
        }

        // A new map has new game rules with the mode's own team sizes: the values remembered on
        // the old map must not be written into them (wingman 2 into a competitive map).
        private void ForgetPracticeTeamSize()
        {
            _teamSizeBeforePracticeT = -1;
            _teamSizeBeforePracticeCt = -1;
        }

        private void RestorePracticeTeamSize()
        {
            try
            {
                var rules = GetGameRules();
                if (rules != null)
                {
                    if (_teamSizeBeforePracticeT >= 0)
                        Schema.SetSchemaValue(rules.Handle, "CCSGameRules", "m_iMaxNumTerrorists", _teamSizeBeforePracticeT);
                    if (_teamSizeBeforePracticeCt >= 0)
                        Schema.SetSchemaValue(rules.Handle, "CCSGameRules", "m_iMaxNumCTs", _teamSizeBeforePracticeCt);
                }
            }
            catch (Exception e)
            {
                Log($"[PracticeTeamSize] {e.Message}");
            }
            _teamSizeBeforePracticeT = -1;
            _teamSizeBeforePracticeCt = -1;
        }

        // Practice bots are teleported and respawned all the time, and each move clears the
        // bot's lag compensation history. A shot at such a bot within the next few ticks makes
        // the engine print a 15 line "No valid positions in history for BacktrackPlayer" report
        // on its LagCompensation logging channel. Harmless there, so the channel is not echoed
        // while practice is on. Matches keep the engine default.
        private bool _lagCompLogQuiet;
        private bool _lagCompChannelMissingLogged;

        private void QuietLagCompensationLog(bool quiet)
        {
            if (_lagCompLogQuiet == quiet)
                return;
            if (Tier0Logging.SetChannelFlag("LagCompensation", Tier0Logging.LCF_DO_NOT_ECHO, quiet))
            {
                _lagCompLogQuiet = quiet;
            }
            else if (!_lagCompChannelMissingLogged)
            {
                _lagCompChannelMissingLogged = true;
                Log("[Practice] LagCompensation logging channel not found; its console lines stay visible.");
            }
        }
    }
}
