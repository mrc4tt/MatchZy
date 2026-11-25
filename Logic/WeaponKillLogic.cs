using System;
using System.Collections.Generic;

namespace MatchZy
{
    // Turns the weapon name of a player_death event into the key used for per-weapon kill stats.
    public static class WeaponKillLogic
    {
        public const string Knife = "knife";
        public const string Awp = "awp";

        /// <summary>
        /// Key for the per-weapon kill counter, or null when the kill has no weapon to count
        /// (empty name, world damage). The name is lower case without the "weapon_" prefix, as
        /// player_death sends it ("ak47", "m4a1_silencer", "hegrenade", "inferno" for fire). Every
        /// knife model ("knife_t", "bayonet", "knife_karambit", ...) counts as "knife".
        /// </summary>
        public static string? Key(string? weapon)
        {
            string name = (weapon ?? "").Trim().ToLowerInvariant();
            if (name.StartsWith("weapon_", StringComparison.Ordinal))
                name = name.Substring("weapon_".Length);
            if (name.Length == 0 || name == "world" || name == "worldspawn")
                return null;
            if (name.Contains("knife", StringComparison.Ordinal) || name.Contains("bayonet", StringComparison.Ordinal))
                return Knife;
            return name;
        }

        // Weapon types in the order they are reported. "other" takes the Zeus and any name not
        // listed below, so the types always add up to the per-weapon total.
        public static readonly string[] Types = { "pistol", "smg", "rifle", "sniper", "shotgun", "machinegun", "grenade", "knife", "other" };

        private static readonly Dictionary<string, string> TypeByKey = BuildTypeByKey();

        private static Dictionary<string, string> BuildTypeByKey()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string type, params string[] keys)
            {
                foreach (var key in keys)
                    map[key] = type;
            }
            Add("pistol", "glock", "hkp2000", "usp_silencer", "p250", "elite", "fiveseven", "tec9", "cz75a", "deagle", "revolver");
            Add("smg", "mac10", "mp9", "mp7", "mp5sd", "ump45", "p90", "bizon");
            Add("rifle", "ak47", "m4a1", "m4a1_silencer", "famas", "galilar", "aug", "sg556");
            Add("sniper", "awp", "ssg08", "scar20", "g3sg1");
            Add("shotgun", "nova", "xm1014", "mag7", "sawedoff");
            Add("machinegun", "m249", "negev");
            Add("grenade", "hegrenade", "inferno", "molotov", "incgrenade", "flashbang", "smokegrenade", "decoy");
            Add("knife", Knife);
            return map;
        }

        /// <summary>Weapon type of a per-weapon key from <see cref="Key"/>; "other" when not known.</summary>
        public static string TypeOf(string key) => TypeByKey.TryGetValue(key, out var type) ? type : "other";

        /// <summary>
        /// Kills per weapon type from the per-weapon counts. Every type of <see cref="Types"/> is
        /// present, 0 included, so a consumer can read a fixed set of fields.
        /// </summary>
        public static Dictionary<string, int> TypeTotals(IReadOnlyDictionary<string, int>? weaponKills)
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var type in Types)
                totals[type] = 0;
            if (weaponKills != null)
                foreach (var pair in weaponKills)
                    totals[TypeOf(pair.Key)] += pair.Value;
            return totals;
        }
    }
}
