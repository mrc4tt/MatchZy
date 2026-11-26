using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MatchZy.Tests
{
    // The "did you mean" reply (SuggestChatCommand) treats a dot word as unknown unless it is a
    // commandActions key, a css_ console command or listed in DotOnlyArgCommands /
    // PracticeOnlyArgCommands. A word the chat dispatch handles but none of those name would get a
    // suggestion for a neighbouring command while its own handler runs. These tests read the
    // plugin source and fail when such a word is added without listing it.
    public class ChatCommandCoverageTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MatchZy.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "MatchZy.csproj not found above the test output folder");
            return dir!.FullName;
        }

        private static IEnumerable<string> PluginSources(string root)
        {
            string[] skip = { "tests", "obj", "bin", "site", ".claude", ".git", "packages" };
            foreach (string sub in Directory.GetDirectories(root))
            {
                if (skip.Contains(Path.GetFileName(sub)))
                    continue;
                foreach (string file in Directory.GetFiles(sub, "*.cs", SearchOption.AllDirectories))
                    yield return file;
            }
        }

        private static string Between(string text, string from, string to)
        {
            int a = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(a >= 0, $"marker not found: {from}");
            int b = text.IndexOf(to, a, StringComparison.Ordinal);
            Assert.True(b > a, $"marker not found: {to}");
            return text.Substring(a, b - a);
        }

        private static HashSet<string> DotWords(string text) =>
            Regex.Matches(text, "\"(\\.[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        [Fact]
        public void EveryDispatchedDotWordIsKnown()
        {
            string root = RepoRoot();
            string core = File.ReadAllText(Path.Combine(root, "Core", "MatchZy.cs"));

            var known = new HashSet<string>();
            foreach (string file in PluginSources(root))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "ConsoleCommand\\(\"css_([a-z0-9_]+)\""))
                    known.Add("." + m.Groups[1].Value);
            }
            Assert.True(known.Count > 100, "console command scan found too little; the scan is broken");

            string actions = Between(core, "commandActions = new Dictionary", "RegisterEventHandler<EventPlayerChat>");
            foreach (Match m in Regex.Matches(actions, "\\{\\s*\"(\\.[^\" ]+)"))
                known.Add(m.Groups[1].Value.ToLowerInvariant());

            known.UnionWith(DotWords(Between(core, "PracticeOnlyArgCommands = new", "};")));
            known.UnionWith(DotWords(Between(core, "DotOnlyArgCommands =", "};")));

            var dispatched = DotWords(Between(core, "string cmdWord = messageCommand", "SuggestChatCommand(player, cmdWord);"));
            string extras = File.ReadAllText(Path.Combine(root, "Practice", "PracticeExtras.cs"));
            foreach (Match m in Regex.Matches(extras, "case \"(\\.[a-z0-9_]+)\""))
                dispatched.Add(m.Groups[1].Value);
            Assert.True(dispatched.Count > 50, "dispatch scan found too little; the scan is broken");

            var missing = dispatched.Where(w => !known.Contains(w)).OrderBy(w => w).ToList();
            Assert.True(missing.Count == 0,
                "Chat-dispatch words missing from DotOnlyArgCommands in Core/MatchZy.cs: " + string.Join(" ", missing));
        }
    }
}
