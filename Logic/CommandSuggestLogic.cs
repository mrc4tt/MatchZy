using System;
using System.Collections.Generic;

namespace MatchZy
{
    // "Did you mean .ready?" for a mistyped chat command.
    public static class CommandSuggestLogic
    {
        /// <summary>
        /// The known command closest to <paramref name="typed"/> (both with their leading dot), or
        /// null when nothing is close enough to be a typo of it. Deliberately strict, because a dot
        /// word MatchZy does not know is often another plugin's command or plain chat: the word
        /// needs at least 4 characters after the dot, one edit is allowed up to 5 characters and
        /// two from 6 (a swap of two neighbouring letters counts as one), and two candidates at the
        /// same distance give no suggestion at all rather than a guess.
        /// </summary>
        public static string? Closest(string typed, IEnumerable<string> known)
        {
            if (string.IsNullOrEmpty(typed) || typed[0] != '.')
                return null;
            string word = typed.ToLowerInvariant();
            int letters = word.Length - 1;
            if (letters < 4)
                return null;
            for (int i = 1; i < word.Length; i++)
            {
                char c = word[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                    return null;
            }

            int max = letters <= 5 ? 1 : 2;
            string? best = null;
            int bestDistance = int.MaxValue;
            bool tie = false;
            foreach (string candidate in known)
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;
                string cand = candidate.ToLowerInvariant();
                if (cand == word)
                    return null;
                if (Math.Abs(cand.Length - word.Length) > max)
                    continue;
                int d = Distance(word, cand);
                if (d > max)
                    continue;
                if (d < bestDistance)
                {
                    best = cand;
                    bestDistance = d;
                    tie = false;
                }
                else if (d == bestDistance && cand != best)
                {
                    tie = true;
                }
            }
            return tie ? null : best;
        }

        // Optimal string alignment distance: insert, delete, replace, or swap two neighbours.
        public static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++)
                d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        v = Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v;
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
