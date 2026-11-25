using System;
using MatchZy;
using Xunit;

namespace MatchZy.Tests
{
    public class LineupNameLogicTests
    {
        [Fact]
        public void FirstNameIsNumberOne()
        {
            Assert.Equal("smoke1", LineupNameLogic.NextFree("smoke", Array.Empty<string>()));
        }

        [Fact]
        public void SkipsNamesTakenByAnyOwner()
        {
            // smoke1 is another player's, smoke2 is in the shared pack.
            Assert.Equal("smoke3", LineupNameLogic.NextFree("smoke", new[] { "smoke1", "smoke2", "flash1" }));
        }

        [Fact]
        public void FillsTheFirstGap()
        {
            Assert.Equal("smoke2", LineupNameLogic.NextFree("smoke", new[] { "smoke1", "smoke3" }));
        }

        [Fact]
        public void ComparesWithoutCase()
        {
            Assert.Equal("smoke2", LineupNameLogic.NextFree("smoke", new[] { "Smoke1" }));
        }

        [Fact]
        public void UniqueKeepsAFreeName()
        {
            Assert.Equal("xbox", LineupNameLogic.Unique("xbox", new[] { "window" }));
        }

        [Fact]
        public void UniqueAddsASuffixToATakenName()
        {
            Assert.Equal("smoke1_2", LineupNameLogic.Unique("smoke1", new[] { "smoke1" }));
            Assert.Equal("smoke1_3", LineupNameLogic.Unique("smoke1", new[] { "SMOKE1", "smoke1_2" }));
        }
    }
}
