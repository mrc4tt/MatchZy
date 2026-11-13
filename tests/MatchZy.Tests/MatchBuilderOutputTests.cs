using MatchZy;
using Newtonsoft.Json.Linq;

namespace MatchZy.Tests;

// Configs in the shape the docs' match config builder on matchzy.miksen.me
// writes. If the loader's validation changes, the builder has to follow.
public class MatchBuilderOutputTests
{
    [Theory]
    [InlineData("""
        {
          "matchid": 0, "num_maps": 3,
          "maplist": ["de_ancient", "de_anubis", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_train"],
          "team1": { "name": "Alpha", "players": { "76561198000000001": "alpha1", "76561198000000002": "alpha2" } },
          "team2": { "name": "Bravo", "players": { "76561198000000011": "bravo1", "76561198000000012": "bravo2" } }
        }
        """)]
    [InlineData("""
        {
          "matchid": 7, "num_maps": 1, "maplist": ["de_dust2"], "skip_veto": true,
          "team1": { "name": "Players", "players": "any" },
          "team2": { "name": "Bots", "bots": true, "bot_difficulty": 3 }
        }
        """)]
    [InlineData("""
        {
          "matchid": 12, "num_maps": 2, "maplist": ["de_mirage", "de_inferno", "workshop/3070284539"],
          "game_mode": "wingman", "veto_first": "random", "side_type": "always_knife",
          "players_per_team": 3, "min_players_to_ready": 2, "clinch_series": false, "friendly_fire": "grenades",
          "team1": { "name": "Alpha", "tag": "ALP", "id": "a1", "players": { "76561198000000001": "alpha1" },
                     "coaches": { "76561198000000009": "coach" } },
          "team2": { "name": "Bravo", "players": { "76561198000000011": "bravo1" } },
          "spectators": { "players": { "76561198000000099": "caster" } },
          "cvars": { "mp_overtime_enable": "1" }
        }
        """)]
    [InlineData("""
        {
          "matchid": 39509, "num_maps": 1, "maplist": ["de_inferno"], "skip_veto": true, "map_sides": ["team1_ct"],
          "players_per_team": 4, "min_spectators_to_ready": 1,
          "team1": { "name": "Alpha", "players": { "76561198000000001": "alpha1" } },
          "team2": { "name": "Bravo", "players": { "76561198000000011": "bravo1" } },
          "spectators": { "players": { "76561198000000099": "caster" } }
        }
        """)]
    public void BuilderOutputLoads(string json) => Assert.Equal("", MatchConfigLogic.ValidateMatchJson(JObject.Parse(json)));
}
