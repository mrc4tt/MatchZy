using MatchZy;
using Xunit;

namespace MatchZy.Tests
{
    public class HelpLineLogicTests
    {
        [Fact]
        public void ShortListStaysOneLine()
        {
            Assert.Equal(new[] { ".ct .t .spec" }, HelpLineLogic.Split(".ct .t .spec", 90));
        }

        [Fact]
        public void LongListSplitsAtWordsWithinLimit()
        {
            var commands = ".bot .cbot .boost .runboost .nobot .dismiss .dismissall .kevlar .armor .botgun .botshoot .botrespawn .startround .rec .stoprecord .replay .saverec .loadrec .stratrec .strat .strats .createstrat .stratslot";
            var chunks = HelpLineLogic.Split(commands, 90);
            Assert.True(chunks.Count > 1);
            Assert.All(chunks, c => Assert.True(c.Length <= 90));
            Assert.Equal(commands, string.Join(" ", chunks));
        }

        [Fact]
        public void TinyLimitUsesMinimumAndKeepsLongWordWhole()
        {
            var chunks = HelpLineLogic.Split("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa b", 5);
            Assert.Equal(new[] { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "b" }, chunks);
        }

        [Fact]
        public void EmptyTextGivesOneEmptyChunk()
        {
            Assert.Equal(new[] { "" }, HelpLineLogic.Split("", 90));
        }
    }
}
