using CounterStrikeSharp.API.Core;

namespace MatchZy
{
    // .help in the console: the commands of the current mode as a table (HelpTableLogic).
    // Console text is English, like .mhelp; the chat summary is the translated part.
    public partial class MatchZy
    {
        private static readonly (string Command, string Description)[] HelpPlayerRowsGeneral =
        {
            (".help", "This list for the current mode"),
            (".mhelp", "Admin commands and the permission each one needs"),
            ("!lang <code>", "Your chat language (en, da, de, es, fr, nb, pl, pt-BR, ru, sq, sv, tr)"),
            (".version", "MatchZy version"),
            (".admin / .calladmin", "Call an admin"),
        };

        private static readonly (string Command, string Description)[] HelpPlayerRowsMatch =
        {
            (".ready / .r", "Ready up"),
            (".unready / .ur", "Not ready"),
            (".coach <t|ct>", "Coach a side"),
            (".uncoach", "Stop coaching"),
            (".stay / .switch", "After winning the knife round: keep or swap sides"),
            (".ct / .t", "After winning the knife round: pick a side"),
            (".tac", "Tactical timeout"),
            (".tech / .pause / .p", "Technical pause"),
            (".unpause / .up", "Ask to unpause (both teams must agree)"),
            (".stop", "Restore the round (both teams must agree, when enabled)"),
        };

        private string HelpModeName()
        {
            if (isPractice) return "Practice";
            if (isDryRun) return "Dry run";
            if (isVeto || isPreVeto) return "Veto";
            if (matchStarted || readyAvailable) return isMatchModeEnabled ? "Match" : "Scrim";
            return "Warmup";
        }

        private static void PrintHelpTable(CCSPlayerController player, string mode, IEnumerable<HelpTableLogic.Section> sections)
        {
            foreach (string line in HelpTableLogic.Format($"MatchZy - {mode}", null, sections))
                player.PrintToConsole(line);
        }

        // The admin commands this player may run, one section per catalog category.
        private List<HelpTableLogic.Section> HelpAdminSections(CCSPlayerController player)
        {
            var sections = new List<HelpTableLogic.Section>();
            HelpTableLogic.Section? current = null;
            foreach (var c in AdminCommandCatalog)
            {
                if (!IsPlayerAdmin(player, c.PermissionKey, c.Permissions))
                    continue;
                if (current == null || current.Title != $"Admin: {c.Category}")
                    sections.Add(current = new HelpTableLogic.Section($"Admin: {c.Category}"));
                current.Rows.Add((c.Display, c.Description));
            }
            return sections;
        }

        // Match, scrim, warmup, veto and dry run.
        private void PrintMatchHelpTable(CCSPlayerController player)
        {
            var sections = new List<HelpTableLogic.Section>();
            var general = new HelpTableLogic.Section("");
            general.Rows.AddRange(HelpPlayerRowsGeneral);
            sections.Add(general);
            var players = new HelpTableLogic.Section("Player commands");
            players.Rows.AddRange(HelpPlayerRowsMatch);
            sections.Add(players);
            sections.AddRange(HelpAdminSections(player));
            PrintHelpTable(player, HelpModeName(), sections);
        }

        // Practice: the blocks keep their "command  description" lines and become one table.
        private void PrintPracticeHelpTable(CCSPlayerController player, List<string> blocks)
        {
            var sections = new List<HelpTableLogic.Section>();
            foreach (string block in blocks)
                sections.Add(HelpTableLogic.ParseBlock(block));
            PrintHelpTable(player, HelpModeName(), sections);
        }
    }
}
