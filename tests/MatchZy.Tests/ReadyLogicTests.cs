using MatchZy;

namespace MatchZy.Tests;

public class ReadyLogicTests
{
    [Theory]
    [InlineData(5, -1, 0)]  // off
    [InlineData(5, 0, 5)]   // full team
    [InlineData(5, 1, 4)]
    [InlineData(2, 1, 1)]   // wingman
    [InlineData(1, 1, 1)]   // 1v1: never below one
    [InlineData(5, 9, 1)]
    [InlineData(0, 1, 0)]
    public void ReadyFloor(int perTeam, int maxMissing, int expected)
        => Assert.Equal(expected, ReadyLogic.ReadyFloor(perTeam, maxMissing));

    [Theory]
    [InlineData(5, 0, 4, 5)]  // open / bot roster keeps players_per_team
    [InlineData(5, 5, 4, 5)]
    [InlineData(5, 7, 4, 5)]  // roster with subs: players_per_team is the most
    [InlineData(5, 4, 0, 4)]  // no floor: a roster of 4 readies as 4
    [InlineData(5, 3, 0, 3)]
    [InlineData(5, 3, 4, 4)]  // floor 4: a roster of 3 can no longer ready up alone
    [InlineData(5, 3, 5, 5)]
    public void RequiredOnSide(int perTeam, int roster, int floor, int expected)
        => Assert.Equal(expected, ReadyLogic.RequiredOnSide(perTeam, roster, floor));

    [Theory]
    [InlineData(false, 1, 5, 0, 0)]  // not in the match config
    [InlineData(true, 0, 5, 0, 0)]
    [InlineData(true, 1, 5, 0, 1)]   // panel default 1: one ready player is enough
    [InlineData(true, 1, 5, 4, 4)]   // floor 4 lifts it
    [InlineData(true, 3, 5, 4, 4)]
    [InlineData(true, 5, 5, 4, 0)]   // not lower than required: everyone readies
    [InlineData(true, 1, 4, 4, 0)]   // floor reaches required: everyone readies
    public void ConfigMinimum(bool set, int configMin, int required, int floor, int expected)
        => Assert.Equal(expected, ReadyLogic.ConfigMinimum(set, configMin, required, floor));

    [Theory]
    [InlineData(5, 1, 1, 0, 4)]   // forceready_max_missing 1
    [InlineData(5, 1, -1, 0, 1)]  // Get5 rule
    [InlineData(5, 1, -1, 4, 4)]  // ready floor still applies
    [InlineData(5, 1, 2, 4, 4)]   // looser forceready limit cannot undercut the ready floor
    [InlineData(5, 5, 1, 0, 5)]   // match config stricter
    public void ForceReadyMinimum(int perTeam, int configMin, int forceMaxMissing, int floor, int expected)
        => Assert.Equal(expected, ReadyLogic.ForceReadyMinimum(perTeam, configMin, forceMaxMissing, floor));
}
