using MatchZy;

namespace MatchZy.Tests;

public class MatchConfigWarningsTests
{
    private static string[] Ids(int start, int count)
        => Enumerable.Range(start, count).Select(i => (76561198000000000L + i).ToString()).ToArray();

    [Fact]
    public void CleanConfigHasNoWarnings()
    {
        var w = MatchConfigWarnings.Collect(new MatchConfigWarnings.Input
        {
            Team1Players = Ids(1, 5), Team2Players = Ids(11, 5), Spectators = Ids(21, 2),
            PlayersPerTeam = 5, PlayersPerTeamSet = true,
        });
        Assert.Empty(w);
    }

    [Fact]
    public void PlayerAlsoSpectatorAndOversizedRoster()
    {
        var team2 = Ids(11, 5).Append(Ids(21, 1)[0]).ToArray();
        var w = MatchConfigWarnings.Collect(new MatchConfigWarnings.Input
        {
            Team1Players = Ids(1, 5), Team2Players = team2, Spectators = Ids(21, 3),
            PlayersPerTeam = 5, PlayersPerTeamSet = true,
            MinPlayersToReady = 1, MinPlayersToReadySet = true, ReadyPerTeam = 1,
        });
        Assert.Contains(w, s => s.Contains("under spectators"));
        Assert.Contains(w, s => s.Contains("lists 6 players"));
        Assert.Contains(w, s => s.Contains("is ignored"));
        Assert.Equal(3, w.Count);
    }

    [Fact]
    public void PlayerOnBothTeams()
    {
        var w = MatchConfigWarnings.Collect(new MatchConfigWarnings.Input
        {
            Team1Players = Ids(1, 4), Team2Players = Ids(4, 4), PlayersPerTeam = 4, PlayersPerTeamSet = true,
        });
        Assert.Single(w);
        Assert.Contains("both", w[0]);
    }

    [Fact]
    public void DifferentTeamSizesAndLowMinReady()
    {
        var w = MatchConfigWarnings.Collect(new MatchConfigWarnings.Input
        {
            Team1Players = Ids(1, 4), Team2Players = Ids(11, 5), PlayersPerTeam = 5, PlayersPerTeamSet = true,
            MinPlayersToReady = 1, MinPlayersToReadySet = true, ReadyPerTeam = 0,
        });
        Assert.Contains(w, s => s.Contains("different number"));
        Assert.Contains(w, s => s.Contains("also while registered players are missing"));
    }

    [Fact]
    public void FromJsonMatchesTheLoader()
    {
        var json = Newtonsoft.Json.Linq.JObject.Parse("""
            {
              "team1": { "name": "A", "players": { "76561198000000001": "a", "76561198000000002": "b", "76561198000000003": "c", "76561198000000004": "d" } },
              "team2": { "name": "B", "players": ["76561198000000011", "76561198000000012", "76561198000000013", "76561198000000014", "76561198000000015"],
                         "coaches": ["76561198000000015"] },
              "spectators": { "players": { "76561198000000004": "d" } },
              "min_players_to_ready": 1,
              "cvars": { "matchzy_ready_per_team": "1" }
            }
            """);
        var input = MatchConfigWarnings.FromJson(json, 0);
        Assert.Equal(4, input.Team1Players.Count);
        Assert.Equal(4, input.Team2Players.Count);   // the coach is not a player
        Assert.Equal(4, input.PlayersPerTeam);       // not set: from the rosters
        Assert.False(input.PlayersPerTeamSet);
        Assert.Equal(1, input.ReadyPerTeam);         // the match's cvars win
        var w = MatchConfigWarnings.Collect(input);
        Assert.Contains(w, s => s.Contains("under spectators"));
        Assert.Contains(w, s => s.Contains("is ignored"));
    }

    [Fact]
    public void FromJsonOpenRosterAndWingmanDefault()
    {
        var json = Newtonsoft.Json.Linq.JObject.Parse("""
            { "wingman": true, "team1": { "name": "A", "players": "any" }, "team2": { "name": "B" } }
            """);
        var input = MatchConfigWarnings.FromJson(json, 0);
        Assert.Empty(input.Team1Players);
        Assert.Equal(2, input.PlayersPerTeam);
    }

    [Fact]
    public void OpenRosterOnlyChecksReadySettings()
    {
        var w = MatchConfigWarnings.Collect(new MatchConfigWarnings.Input
        {
            Team1Players = Ids(1, 5), PlayersPerTeam = 5, PlayersPerTeamSet = true,
        });
        Assert.Empty(w);
    }
}
