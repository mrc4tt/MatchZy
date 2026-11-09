namespace MatchZy
{
    // Halftime / overtime side swaps (referred from Get5), kept free of CounterStrikeSharp so it can
    // be unit tested (tests/MatchZy.Tests). The plugin feeds it mp_maxrounds / 2,
    // mp_overtime_maxrounds / 2 and mp_halftime.
    public static class SideSwapLogic
    {
        // Whether the teams switch sides once `roundsPlayed` rounds have been played: at halftime
        // and at every overtime half (OT rounds 3, 9, ... with MR3 overtime halves).
        public static bool IsSwapAfterRound(int roundsPlayed, int roundsPerHalf, int roundsPerOTHalf, bool halftimeEnabled)
        {
            if (!halftimeEnabled || roundsPerHalf <= 0 || roundsPlayed <= 0)
                return false;
            if (roundsPlayed == roundsPerHalf)
                return true;
            if (roundsPlayed >= 2 * roundsPerHalf && roundsPerOTHalf > 0)
            {
                int otRound = roundsPlayed - 2 * roundsPerHalf; // round 33 -> round 3, etc.
                return (otRound + roundsPerOTHalf) % (2 * roundsPerOTHalf) == 0;
            }
            return false;
        }

        // Whether the team that started the map as CT is playing T after `roundsPlayed` rounds:
        // an odd number of swaps so far. The engine ties mp_teamname_1 to the starting CT team and
        // swaps what it displays at each of these, so this is also the scoreboard name mapping.
        public static bool AreSidesSwitched(int roundsPlayed, int roundsPerHalf, int roundsPerOTHalf, bool halftimeEnabled)
        {
            bool switched = false;
            for (int round = 1; round <= roundsPlayed; round++)
            {
                if (IsSwapAfterRound(round, roundsPerHalf, roundsPerOTHalf, halftimeEnabled))
                    switched = !switched;
            }
            return switched;
        }
    }
}
