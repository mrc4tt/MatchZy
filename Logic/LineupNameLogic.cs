using System;
using System.Collections.Generic;

namespace MatchZy
{
    // Names for saved grenade lineups that nobody typed a name for.
    public static class LineupNameLogic
    {
        /// <summary>
        /// The first "{prefix}{n}" (n from 1) that is not in <paramref name="taken"/>, compared
        /// without case. The caller passes every name the new one could be mixed up with: all
        /// owners' lineups and the shared pack, not only the saving player's own. Numbering per
        /// player made two players both get "smoke1", and adding both to the shared pack then
        /// overwrote one with the other.
        /// </summary>
        public static string NextFree(string prefix, IEnumerable<string> taken)
        {
            var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
            int n = 1;
            while (used.Contains(prefix + n))
                n++;
            return prefix + n;
        }

        /// <summary>
        /// <paramref name="name"/> when it is free, else the first "{name}_{n}" (n from 2) that is.
        /// </summary>
        public static string Unique(string name, IEnumerable<string> taken)
        {
            var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(name))
                return name;
            int n = 2;
            while (used.Contains($"{name}_{n}"))
                n++;
            return $"{name}_{n}";
        }
    }
}
