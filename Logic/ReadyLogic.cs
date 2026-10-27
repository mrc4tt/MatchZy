using System;

namespace MatchZy
{
    // How many players a side of a loaded match needs before it counts as ready, kept free of
    // CounterStrikeSharp so it can be unit tested (tests/MatchZy.Tests). The plugin feeds it
    // players_per_team, the side's roster size and the convar / match config values.
    public static class ReadyLogic
    {
        // matchzy_ready_max_missing: the fewest players a side can ready up with, whatever the
        // roster or min_players_to_ready say. 0 = no floor (maxMissing -1).
        public static int ReadyFloor(int playersPerTeam, int maxMissing)
        {
            if (maxMissing < 0 || playersPerTeam <= 0)
                return 0;
            return Math.Clamp(playersPerTeam - maxMissing, 1, playersPerTeam);
        }

        // Players a side needs before .ready alone makes it ready: players_per_team, or the roster
        // size when smaller (0 = no roster: open or bot team), never below the floor.
        public static int RequiredOnSide(int playersPerTeam, int rosterSize, int floor)
        {
            if (rosterSize <= 0)
                return playersPerTeam;
            return Math.Max(Math.Min(playersPerTeam, rosterSize), Math.Min(floor, playersPerTeam));
        }

        // The match config's min_players_to_ready when it lets the side ready up with fewer than
        // `required` ready players, raised to the floor. 0 = not set, or not lower than required:
        // then every player the side needs has to be ready.
        public static int ConfigMinimum(bool configSet, int configMin, int required, int floor)
        {
            if (!configSet || configMin < 1)
                return 0;
            int minimum = Math.Max(configMin, floor);
            return minimum < required ? minimum : 0;
        }

        // Players a side needs for .forceready: min_players_to_ready, but never fewer than
        // players_per_team - matchzy_forceready_max_missing (-1 = no such limit) or the ready floor.
        public static int ForceReadyMinimum(int playersPerTeam, int configMin, int forceMaxMissing, int floor)
        {
            int minimum = Math.Max(configMin, floor);
            if (forceMaxMissing < 0)
                return minimum;
            return Math.Max(minimum, Math.Max(1, playersPerTeam - forceMaxMissing));
        }
    }
}
