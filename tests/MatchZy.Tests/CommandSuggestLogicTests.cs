using System;
using MatchZy;
using Xunit;

namespace MatchZy.Tests
{
    public class CommandSuggestLogicTests
    {
        private static readonly string[] Known =
        {
            ".ready", ".unready", ".pause", ".unpause", ".delnade", ".savenade", ".loadnade",
            ".listnades", ".ct", ".t", ".rt", ".back", ".stay", ".switch", ".tech",
        };

        [Theory]
        [InlineData(".redy", ".ready")]
        [InlineData(".raedy", ".ready")]
        [InlineData(".unpuase", ".unpause")]
        [InlineData(".delnad", ".delnade")]
        [InlineData(".listnade", ".listnades")]
        [InlineData(".SWITHC", ".switch")]
        public void FindsTheTypo(string typed, string expected)
        {
            Assert.Equal(expected, CommandSuggestLogic.Closest(typed, Known));
        }

        [Theory]
        [InlineData(".ready")]      // a real command
        [InlineData(".rtv")]        // too short: likely another plugin's command
        [InlineData(".rank")]       // not close to anything
        [InlineData(".delsmoke")]   // three edits from .delnade
        [InlineData("...")]
        [InlineData(".lol!")]
        [InlineData("ready")]
        [InlineData("")]
        public void StaysSilent(string typed)
        {
            Assert.Null(CommandSuggestLogic.Closest(typed, Known));
        }

        [Fact]
        public void TwoEquallyCloseCommandsGiveNoGuess()
        {
            // One edit from both.
            Assert.Null(CommandSuggestLogic.Closest(".xavenade", new[] { ".savenade", ".havenade" }));
        }

        [Fact]
        public void NeighbourSwapIsOneEdit()
        {
            Assert.Equal(1, CommandSuggestLogic.Distance(".paues", ".pause"));
        }

        [Fact]
        public void NoKnownCommands()
        {
            Assert.Null(CommandSuggestLogic.Closest(".ready", Array.Empty<string>()));
        }
    }
}
