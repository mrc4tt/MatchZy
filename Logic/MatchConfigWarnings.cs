using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    // Warnings about a loaded match config that is valid but will not behave as the panel probably
    // meant: a match that never starts by itself because a roster has more players than the team
    // size, a player listed for both teams or also as a spectator, ready settings that cancel each
    // other out. Kept free of CounterStrikeSharp so it can be unit tested (tests/MatchZy.Tests).
    public static class MatchConfigWarnings
    {
        public sealed class Input
        {
            public string Team1Name = "team1";
            public string Team2Name = "team2";
            // SteamID64s under each team's "players", without its listed coaches. Empty for an
            // open ("any") or bot team.
            public IReadOnlyList<string> Team1Players = Array.Empty<string>();
            public IReadOnlyList<string> Team2Players = Array.Empty<string>();
            public IReadOnlyList<string> Spectators = Array.Empty<string>();
            public int PlayersPerTeam = 5;
            public bool PlayersPerTeamSet;
            public int MinPlayersToReady;
            public bool MinPlayersToReadySet;
            // matchzy_ready_per_team (0 = off).
            public int ReadyPerTeam;
        }

        // The warning input straight from a match config, without loading it
        // (matchzy_validate_match). Mirrors the loader: coaches listed under "coaches" are not
        // players, an open ("any") or bot team has no roster, players_per_team left out is taken from
        // the rosters (at most the mode's default), and a matchzy_ready_per_team in "cvars" wins
        // over the server's value.
        public static Input FromJson(JObject json, int serverReadyPerTeam)
        {
            static List<string> Ids(JToken? token) => token switch
            {
                JObject o => o.Properties().Select(p => p.Name).ToList(),
                JArray a => a.Select(e => e.ToString()).ToList(),
                _ => new List<string>(),
            };
            List<string> Roster(JToken? team)
            {
                if (team == null || MatchConfigLogic.IsBotTeamToken(team) || MatchConfigLogic.IsOpenRosterToken(team["players"]))
                    return new List<string>();
                var coaches = new HashSet<string>(Ids(team["coaches"]));
                return Ids(team["players"]).Where(id => !coaches.Contains(id)).ToList();
            }
            string Name(string key) => $"{key} ({json[key]?["name"]?.ToString() ?? "?"})";

            var team1 = Roster(json["team1"]);
            var team2 = Roster(json["team2"]);
            bool pptSet = json["players_per_team"] != null && int.TryParse(json["players_per_team"]!.ToString(), out _);
            int ppt = pptSet ? int.Parse(json["players_per_team"]!.ToString())
                : ReadyLogic.PlayersPerTeamFromRosters(team1.Count, team2.Count, ModeDefaultPlayersPerTeam(json));
            bool minSet = json["min_players_to_ready"] != null && int.TryParse(json["min_players_to_ready"]!.ToString(), out _);
            int perTeam = serverReadyPerTeam;
            if (json["cvars"]?["matchzy_ready_per_team"] is JToken t && int.TryParse(t.ToString(), out int cvarPerTeam))
                perTeam = cvarPerTeam;
            return new Input
            {
                Team1Name = Name("team1"),
                Team2Name = Name("team2"),
                Team1Players = team1,
                Team2Players = team2,
                Spectators = Ids(json["spectators"]?["players"]),
                PlayersPerTeam = ppt,
                PlayersPerTeamSet = pptSet,
                MinPlayersToReady = minSet ? int.Parse(json["min_players_to_ready"]!.ToString()) : 0,
                MinPlayersToReadySet = minSet,
                ReadyPerTeam = perTeam,
            };
        }

        // players_per_team's default for the config's game mode: 5 competitive, 2 wingman, 3 rush, 1
        // for 1v1 (game_mode, else "wingman": true).
        private static int ModeDefaultPlayersPerTeam(JObject json)
        {
            string mode = json["game_mode"]?.ToString().Trim().ToLowerInvariant() ?? "";
            if (mode == "" && json["wingman"] != null && json["wingman"]!.ToString().Trim().ToLowerInvariant() is "true" or "1")
                mode = "wingman";
            return mode switch { "1v1" => 1, "wingman" => 2, "rush" => 3, _ => 5 };
        }

        public static List<string> Collect(Input c)
        {
            var warnings = new List<string>();
            var team1 = new HashSet<string>(c.Team1Players);
            var team2 = new HashSet<string>(c.Team2Players);
            var specs = new HashSet<string>(c.Spectators);

            foreach (string id in team1.Intersect(team2))
                warnings.Add($"SteamID {id} is listed under the players of both {c.Team1Name} and {c.Team2Name}. It can only play for one of them, and the other team waits for it.");

            foreach (var (name, roster) in new[] { (c.Team1Name, team1), (c.Team2Name, team2) })
            {
                foreach (string id in roster.Intersect(specs))
                    warnings.Add($"SteamID {id} is listed under the players of {name} and under spectators. It counts as a {name} player, so while it sits on Spectator the team is one player short.");
            }

            foreach (var (name, roster) in new[] { (c.Team1Name, team1), (c.Team2Name, team2) })
            {
                if (c.PlayersPerTeam > 0 && roster.Count > c.PlayersPerTeam)
                    warnings.Add($"{name} lists {roster.Count} players with players_per_team {c.PlayersPerTeam}{(c.PlayersPerTeamSet ? "" : " (not set: the most a roster has, at most the mode's default)")}. The team needs {c.PlayersPerTeam} of them on the server; the rest are substitutes. If the team plays with fewer, send players_per_team with the real team size.");
            }

            int size1 = team1.Count > 0 ? Math.Min(team1.Count, c.PlayersPerTeam) : c.PlayersPerTeam;
            int size2 = team2.Count > 0 ? Math.Min(team2.Count, c.PlayersPerTeam) : c.PlayersPerTeam;
            if (team1.Count > 0 && team2.Count > 0 && size1 != size2)
                warnings.Add($"The teams need a different number of players: {c.Team1Name} {size1}, {c.Team2Name} {size2} (each roster, at most players_per_team {c.PlayersPerTeam}).");

            if (c.MinPlayersToReadySet)
            {
                if (c.ReadyPerTeam > 0)
                    warnings.Add($"min_players_to_ready {c.MinPlayersToReady} is ignored: matchzy_ready_per_team is {c.ReadyPerTeam}, so {c.ReadyPerTeam} ready player(s) per team start the match once the team is complete.");
                else if (c.MinPlayersToReady >= 1 && c.MinPlayersToReady < Math.Max(size1, size2))
                    warnings.Add($"min_players_to_ready {c.MinPlayersToReady}: that many ready players make a team ready, also while registered players are missing. Leave it out for a full ready-up, or set matchzy_ready_max_missing to limit how short a team can start.");
            }

            return warnings;
        }
    }
}
