using MatchZy;
using Xunit;

namespace MatchZy.Tests
{
    public class HelpTableLogicTests
    {
        [Fact]
        public void ParseBlockReadsTitleAndRows()
        {
            var s = HelpTableLogic.ParseBlock("\n【Bot Control】\n.bot  Add a bot\n.nobots  Remove all bots (alias: .nbs)\n");
            Assert.Equal("Bot Control", s.Title);
            Assert.Equal(2, s.Rows.Count);
            Assert.Equal((".bot", "Add a bot"), s.Rows[0]);
            Assert.Equal((".nobots", "Remove all bots (alias: .nbs)"), s.Rows[1]);
        }

        [Fact]
        public void ParseBlockKeepsArgumentsInTheCommand()
        {
            var s = HelpTableLogic.ParseBlock(".spawn <number>  Teleport to a spawn\n.clear");
            Assert.Equal("", s.Title);
            Assert.Equal((".spawn <number>", "Teleport to a spawn"), s.Rows[0]);
            Assert.Equal((".clear", ""), s.Rows[1]);
        }

        [Fact]
        public void FormatLinesUpTheDescriptions()
        {
            var a = new HelpTableLogic.Section("Core");
            a.Rows.Add((".tac", "Tactical pause"));
            a.Rows.Add((".coach <side>", "Coach a team"));
            var lines = HelpTableLogic.Format("MatchZy - Scrim", null, new[] { a });
            Assert.Equal("MatchZy - Scrim", lines[1]);
            Assert.Equal("COMMAND".PadRight(HelpTableLogic.CommandWidth) + "DESCRIPTION", lines[2]);
            int col = lines.Find(l => l.StartsWith(".tac"))!.IndexOf("Tactical", StringComparison.Ordinal);
            Assert.Equal(HelpTableLogic.CommandWidth, col);
            Assert.Equal(col, lines.Find(l => l.StartsWith(".coach"))!.IndexOf("Coach a team", StringComparison.Ordinal));
        }

        [Fact]
        public void FormatSkipsEmptySectionsAndKeepsLongCommands()
        {
            var empty = new HelpTableLogic.Section("Nothing");
            var b = new HelpTableLogic.Section("");
            string longCmd = ".savecoachspawn / .clearcoachspawns / .listcoachspawns";
            b.Rows.Add((longCmd, "Coach spawn spots"));
            var lines = HelpTableLogic.Format("T", "https://example.invalid", new[] { empty, b });
            Assert.DoesNotContain("Nothing", lines);
            Assert.Contains("https://example.invalid", lines);
            Assert.Contains(longCmd + "  Coach spawn spots", lines);
        }
    }
}
