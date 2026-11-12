using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    // Match config rules (structure, team keys, map names, veto order), kept free of
    // CounterStrikeSharp so they can be unit tested (tests/MatchZy.Tests). The plugin calls these
    // through thin wrappers on the MatchZy partial class.
    public static class MatchConfigLogic
    {
        public static readonly string[] GameModes = { "competitive", "wingman", "rush", "1v1" };

        private static readonly Regex SafeMapNameRegex = new(@"^[A-Za-z0-9_\-\./:]+$");

        /// <summary>
        /// Map names end up in changelevel / host_workshop_map / ds_workshop_changelevel console
        /// commands. Only plain map tokens (letters, digits, _ - . / :) are allowed, so a name from a
        /// match config, a veto, a backup or chat cannot carry a quote or ';' into the console.
        /// </summary>
        public static bool IsSafeMapName(string? mapName)
        {
            return !string.IsNullOrWhiteSpace(mapName) && mapName.Length <= 128 && SafeMapNameRegex.IsMatch(mapName);
        }

        public static bool IsOpenRosterToken(JToken? players)
        {
            if (players is not JValue { Type: JTokenType.String } value)
                return false;
            string text = ((string?)value ?? "").Trim();
            return text.Equals("any", StringComparison.OrdinalIgnoreCase) || text == "*";
        }

        public static bool IsBotTeamToken(JToken? team)
        {
            JToken? bots = team?["bots"];
            if (bots == null)
                return false;
            return bots.Type switch
            {
                JTokenType.Boolean => bots.Value<bool>(),
                JTokenType.Integer => bots.Value<long>() > 0,
                JTokenType.String => bool.TryParse(bots.ToString(), out bool b) ? b : int.TryParse(bots.ToString(), out int n) && n > 0,
                _ => false,
            };
        }

        public static int BotDifficultyFrom(JToken? team)
        {
            JToken? token = team?["bot_difficulty"];
            if (token != null && int.TryParse(token.ToString(), out int difficulty))
                return Math.Clamp(difficulty, 0, 3);
            return 2;
        }

        /// <summary>
        /// Checks a match config's structure and value types. Returns "" when valid, otherwise the
        /// first problem found (logged by the loader).
        /// </summary>
        public static string ValidateMatchJson(JObject jsonData)
        {
            string[] requiredFields = { "maplist", "team1", "team2", "num_maps" };

            // Check if any required field is missing
            foreach (string field in requiredFields)
            {
                if (jsonData[field] == null)
                {
                    return $"Missing mandatory field: {field}";
                }
            }

            foreach (var property in jsonData.Properties())
            {
                string field = property.Name;

                switch (field)
                {
                    case "matchid":
                        // Get5 allows the id as a number or a numeric string.
                        if (!long.TryParse(jsonData[field]!.ToString(), out _))
                        {
                            return $"{field} should be a number!";
                        }
                        break;
                    case "players_per_team":
                    case "min_players_to_ready":
                    case "min_spectators_to_ready":
                    case "num_maps":
                        int numMaps;
                        if (!int.TryParse(jsonData[field]!.ToString(), out numMaps))
                        {
                            return $"{field} should be an integer!";
                        }

                        if (field == "num_maps" && numMaps < 1)
                        {
                            return $"{field} should be at least 1!";
                        }

                        if (field == "num_maps" && numMaps > jsonData["maplist"]!.ToObject<List<string>>()!.Count)
                        {
                            return $"{field} should not be larger than the number of maps in maplist!";
                        }

                        break;

                    case "cvars":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }

                        break;

                    case "team1":
                    case "team2":
                    case "spectators":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }

                        if (field != "spectators" && (jsonData[field]!["name"] == null || string.IsNullOrWhiteSpace(jsonData[field]!["name"]!.ToString())))
                        {
                            return $"{field} should have a 'name'!";
                        }

                        // A bot team needs no roster, and "players": "any" opens the team to any human.
                        if ((field != "spectators") && !IsBotTeamToken(jsonData[field]) && !IsOpenRosterToken(jsonData[field]!["players"])
                            && (jsonData[field]!["players"] == null
                                || (jsonData[field]!["players"]!.Type != JTokenType.Object && jsonData[field]!["players"]!.Type != JTokenType.Array)))
                        {
                            return $"{field} should have 'players' (an object keyed by SteamID64 or an array of SteamID64s), \"players\": \"any\", or \"bots\": true!";
                        }

                        break;

                    case "veto_mode":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }

                        break;

                    case "maplist":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }

                        if (!jsonData[field]!.Any())
                        {
                            return $"{field} should contain atleast 1 map!";
                        }

                        foreach (var mapEntry in jsonData[field]!)
                        {
                            if (!IsSafeMapName(mapEntry.ToString()))
                                return $"{field} contains an invalid map name: {mapEntry}";
                        }

                        break;
                    case "map_sides":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }

                        string[] allowedValues = { "team1_ct", "team1_t", "team2_ct", "team2_t", "knife" };
                        bool allElementsValid = jsonData[field]!.All(element => allowedValues.Contains(element.ToString()));

                        if (!allElementsValid)
                        {
                            return $"{field} should be \"team1_ct\", \"team1_t\", \"team2_ct\", \"team2_t\" or \"knife\"!";
                        }

                        if (int.TryParse(jsonData["num_maps"]?.ToString(), out int sideMaps) && jsonData[field]!.ToObject<List<string>>()!.Count < sideMaps)
                        {
                            return $"{field} should be equal to or greater than num_maps!";
                        }

                        break;

                    case "game_mode":
                        if (!GameModes.Contains(jsonData[field]!.ToString().Trim().ToLowerInvariant()))
                        {
                            return $"{field} should be \"competitive\", \"wingman\", \"rush\" or \"1v1\"!";
                        }

                        break;

                    case "skip_veto":
                    case "clinch_series":
                    case "wingman":
                        // Same forms the loader accepts (ParseCvarBool): true/false and 1/0. bool.TryParse
                        // alone refused 1/0, so a config the loader could read was rejected here.
                        string boolText = jsonData[field]!.ToString().Trim();
                        if (!bool.TryParse(boolText, out _) && boolText != "1" && boolText != "0")
                        {
                            return $"{field} should be a boolean (true/false or 1/0)!";
                        }

                        break;
                }
            }

            return "";
        }

        /// <summary>
        /// Checks a custom veto order (map_ban_order / veto_mode) against the map pool and series
        /// length. Returns "" when it is playable, otherwise why not.
        /// </summary>
        public static string ValidateMapBanOrder(IReadOnlyList<string> banOrder, int poolCount, int numMaps)
        {
            // Anything else (a typo such as "team1_veto") stalled the veto: no captain is asked and
            // both .ban and .pick refuse the step.
            foreach (string option in banOrder)
            {
                if (option is not ("team1_ban" or "team2_ban" or "team1_pick" or "team2_pick"))
                    return $"Unknown veto option \"{option}\": use team1_ban, team2_ban, team1_pick or team2_pick.";
            }

            int numberOfPicks = 0;
            for (int i = 0; i < banOrder.Count; i++)
            {
                string option = banOrder[i];
                if (option == "team1_pick" || option == "team2_pick")
                {
                    numberOfPicks++;
                }

                if (numberOfPicks == numMaps || i == poolCount - 2)
                {
                    break;
                }
            }

            // Example: In a Bo3, at least 2 of the options must be picks to avoid randomly selecting map order of remaining maps.
            if (numMaps > 1 && numberOfPicks < numMaps - 1)
            {
                return $"In a series of {numMaps} maps, at least {numMaps - 1} veto options must be picks. Found {numberOfPicks} pick(s).";
            }

            if (poolCount - 1 != banOrder.Count && numberOfPicks != numMaps)
            {
                // Example: Map pool of 7 requires 6 picks/bans *unless* we have picks for all maps.
                return $"The number of maps in the pool {poolCount} must be one larger than the number of map picks/bans {banOrder.Count}, unless the number of picks {numberOfPicks} matches the series length {numMaps}.";
            }

            return "";
        }
    }
}
