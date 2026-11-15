using System.Collections.Generic;

namespace MatchZy
{
    // Splits a help line's command list so every chat message fits on screen. CS2 cuts a long chat
    // message instead of wrapping it, so the last commands of a long list were never shown.
    public static class HelpLineLogic
    {
        // Visible characters of one help chat line, label included. The chat window cut a line
        // after about 75 characters at 1080p ("Bots: .bot ... .armor ."), so stay well below.
        public const int MaxLineChars = 68;
        private const int MinChunkChars = 30;

        /// <summary>
        /// Groups the space-separated words of <paramref name="text"/> into chunks of at most
        /// <paramref name="maxChars"/> characters (at least 30). A single word longer than that is
        /// kept whole. Empty or blank text gives one empty chunk, so the caller still prints its line.
        /// </summary>
        public static List<string> Split(string text, int maxChars)
        {
            int limit = Math.Max(maxChars, MinChunkChars);
            var chunks = new List<string>();
            var current = "";
            foreach (var word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length == 0)
                    current = word;
                else if (current.Length + 1 + word.Length <= limit)
                    current += " " + word;
                else
                {
                    chunks.Add(current);
                    current = word;
                }
            }
            chunks.Add(current);
            return chunks;
        }
    }
}
