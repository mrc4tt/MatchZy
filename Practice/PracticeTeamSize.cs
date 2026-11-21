using CounterStrikeSharp.API.Core;
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
    }
}
