using System.Collections.Generic;

namespace MatchZy
{
    // Lays a command list out as a console table: a command column and a description column,
    // grouped in sections with a rule between them. The console font is monospaced, so padding
    // the command column lines the descriptions up.
    public static class HelpTableLogic
    {
        public const int CommandWidth = 34;
        private const int RuleWidth = 44;

        public sealed class Section
        {
            public string Title { get; }
            public List<(string Command, string Description)> Rows { get; } = new();
            public Section(string title) { Title = title ?? ""; }
        }

        /// <summary>
        /// The table as console lines: title, optional url, the column header, then every section
        /// that has rows. A command wider than the column keeps two spaces before its description.
        /// </summary>
        public static List<string> Format(string title, string? url, IEnumerable<Section> sections)
        {
            string rule = new string('-', RuleWidth);
            var lines = new List<string> { rule, title };
            if (!string.IsNullOrWhiteSpace(url))
                lines.Add(url!);
            lines.Add("COMMAND".PadRight(CommandWidth) + "DESCRIPTION");
            lines.Add(rule);
            foreach (var section in sections)
            {
                if (section.Rows.Count == 0)
                    continue;
                if (section.Title.Length > 0)
                {
                    lines.Add(section.Title);
                    lines.Add(rule);
                }
                foreach (var (command, description) in section.Rows)
                {
                    if (string.IsNullOrEmpty(description))
                        lines.Add(command);
                    else if (command.Length >= CommandWidth)
                        lines.Add(command + "  " + description);
                    else
                        lines.Add(command.PadRight(CommandWidth) + description);
                }
                lines.Add(rule);
            }
            return lines;
        }

        /// <summary>
        /// Reads one help block: an optional first line "【Title】" and then one command per line,
        /// the command and its description separated by two or more spaces.
        /// </summary>
        public static Section ParseBlock(string block)
        {
            string title = "";
            var rows = new List<(string, string)>();
            foreach (var raw in (block ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (rows.Count == 0 && title.Length == 0 && line.StartsWith('【') && line.EndsWith('】'))
                {
                    title = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int gap = line.IndexOf("  ", StringComparison.Ordinal);
                if (gap < 0)
                    rows.Add((line, ""));
                else
                    rows.Add((line.Substring(0, gap).Trim(), line.Substring(gap).Trim()));
            }
            var section = new Section(title);
            section.Rows.AddRange(rows);
            return section;
        }
    }
}
