using MatchZy;

namespace MatchZy.Tests;

public class SideSwapLogicTests
{
    // MR12 (mp_maxrounds 24) with MR3 overtime halves (mp_overtime_maxrounds 6).
    [Theory]
    [InlineData(0, false)]
    [InlineData(11, false)]
    [InlineData(12, true)]   // halftime
    [InlineData(13, false)]
    [InlineData(24, false)]  // end of regulation: OT starts on the same sides
    [InlineData(27, true)]   // OT1 half
    [InlineData(30, false)]  // OT1 -> OT2: no swap
    [InlineData(33, true)]   // OT2 half
    public void IsSwapAfterRound(int roundsPlayed, bool expected)
        => Assert.Equal(expected, SideSwapLogic.IsSwapAfterRound(roundsPlayed, 12, 3, true));

    [Theory]
    [InlineData(0, false)]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(20, true)]
    [InlineData(24, true)]
    [InlineData(26, true)]
    [InlineData(27, false)]
    [InlineData(30, false)]
    [InlineData(33, true)]
    public void AreSidesSwitched(int roundsPlayed, bool expected)
        => Assert.Equal(expected, SideSwapLogic.AreSidesSwitched(roundsPlayed, 12, 3, true));

    [Fact]
    public void NoHalftimeNeverSwitches()
        => Assert.False(SideSwapLogic.AreSidesSwitched(20, 12, 3, false));

    [Fact]
    public void ZeroOvertimeHalfDoesNotThrow()
        => Assert.True(SideSwapLogic.AreSidesSwitched(30, 12, 0, true));
}
