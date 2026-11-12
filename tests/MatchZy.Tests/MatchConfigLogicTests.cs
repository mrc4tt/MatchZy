using MatchZy;
using Newtonsoft.Json.Linq;

namespace MatchZy.Tests;

public class MatchConfigLogicTests
{
    private const string ValidConfig = """
        {
          "matchid": 42,
          "num_maps": 1,
          "maplist": ["de_dust2", "de_mirage", "de_inferno"],
          "team1": { "name": "Alpha", "players": { "76561198000000001": "a" } },
          "team2": { "name": "Bravo", "players": ["76561198000000002"] }
        }
        """;

    private static string Validate(string json) => MatchConfigLogic.ValidateMatchJson(JObject.Parse(json));

    // ValidConfig with one top-level key set (or removed when value is null).
    private static string ValidateWith(string key, JToken? value)
    {
        var obj = JObject.Parse(ValidConfig);
        if (value == null)
            obj.Remove(key);
        else
            obj[key] = value;
        return MatchConfigLogic.ValidateMatchJson(obj);
    }

    [Fact]
    public void ValidConfigPasses() => Assert.Equal("", Validate(ValidConfig));

    [Theory]
    [InlineData("maplist")]
    [InlineData("team1")]
    [InlineData("team2")]
    [InlineData("num_maps")]
    public void MissingMandatoryFieldFails(string field) =>
        Assert.Equal($"Missing mandatory field: {field}", ValidateWith(field, null));

    [Theory]
    [InlineData("\"123\"", true)]
    [InlineData("123", true)]
    [InlineData("\"abc\"", false)]
    public void MatchIdMayBeANumberOrANumericString(string token, bool valid) =>
        Assert.Equal(valid, ValidateWith("matchid", JToken.Parse(token)) == "");

    [Fact]
    public void NumMapsBelowOneFails() =>
        Assert.Equal("num_maps should be at least 1!", ValidateWith("num_maps", 0));

    [Fact]
    public void NumMapsLargerThanTheMapListFails() =>
        Assert.Equal("num_maps should not be larger than the number of maps in maplist!", ValidateWith("num_maps", 5));

    [Fact]
    public void NonIntegerPlayersPerTeamFails() =>
        Assert.Equal("players_per_team should be an integer!", ValidateWith("players_per_team", "five"));

    [Fact]
    public void TeamWithoutNameFails() =>
        Assert.Equal("team1 should have a 'name'!", ValidateWith("team1", JObject.Parse("""{ "players": [] }""")));

    [Theory]
    [InlineData("""{ "name": "A" }""", false)]
    [InlineData("""{ "name": "A", "players": "nobody" }""", false)]
    [InlineData("""{ "name": "A", "players": "any" }""", true)]
    [InlineData("""{ "name": "A", "players": "*" }""", true)]
    [InlineData("""{ "name": "A", "bots": true }""", true)]
    [InlineData("""{ "name": "A", "players": [] }""", true)]
    public void TeamNeedsARosterAnOpenRosterOrBots(string team, bool valid) =>
        Assert.Equal(valid, ValidateWith("team2", JObject.Parse(team)) == "");

    [Fact]
    public void SpectatorsNeedNoName() =>
        Assert.Equal("", ValidateWith("spectators", JObject.Parse("""{ "players": [] }""")));

    [Fact]
    public void EmptyMapListFails()
    {
        // Rejected either way: by the num_maps check (checked first here) or by the maplist check.
        Assert.NotEqual("", ValidateWith("maplist", new JArray()));
        var noNumMapsFirst = JObject.Parse("""{ "maplist": [], "num_maps": 1, "team1": {}, "team2": {} }""");
        Assert.Equal("maplist should contain atleast 1 map!", MatchConfigLogic.ValidateMatchJson(noNumMapsFirst));
    }

    [Theory]
    [InlineData("de_dust2;quit")]
    [InlineData("de_dust2\"")]
    [InlineData("de dust2")]
    public void UnsafeMapNameInMapListFails(string map) =>
        Assert.StartsWith("maplist contains an invalid map name", ValidateWith("maplist", new JArray(map, "de_mirage", "de_nuke")));

    [Fact]
    public void MapSidesWithUnknownValueFails() =>
        Assert.Equal("map_sides should be \"team1_ct\", \"team1_t\", \"team2_ct\", \"team2_t\" or \"knife\"!", ValidateWith("map_sides", new JArray("team1_ct", "ct")));

    [Fact]
    public void MapSidesShorterThanNumMapsFails()
    {
        var obj = JObject.Parse(ValidConfig);
        obj["num_maps"] = 3;
        obj["map_sides"] = new JArray("knife", "knife");
        Assert.Equal("map_sides should be equal to or greater than num_maps!", MatchConfigLogic.ValidateMatchJson(obj));
    }

    [Theory]
    [InlineData("competitive", true)]
    [InlineData(" Wingman ", true)]
    [InlineData("1v1", true)]
    [InlineData("deathmatch", false)]
    public void GameModeMustBeKnown(string mode, bool valid) =>
        Assert.Equal(valid, ValidateWith("game_mode", mode) == "");

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", true)]
    [InlineData("1", true)]
    [InlineData("0", true)]
    [InlineData("\"yes\"", false)]
    public void SkipVetoTakesTheSameBooleansAsTheLoader(string token, bool valid) =>
        Assert.Equal(valid, ValidateWith("skip_veto", JToken.Parse(token)) == "");

    [Fact]
    public void CvarsMustBeAnObject() =>
        Assert.Equal("cvars should be a JSON structure!", ValidateWith("cvars", new JArray("mp_maxrounds 24")));

    [Theory]
    [InlineData("de_dust2", true)]
    [InlineData("workshop/3070212801/de_cache", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("de_dust2; quit", false)]
    [InlineData("de_dust2'", false)]
    public void SafeMapNames(string? map, bool safe) => Assert.Equal(safe, MatchConfigLogic.IsSafeMapName(map));

    [Fact]
    public void OverlongMapNameIsUnsafe() => Assert.False(MatchConfigLogic.IsSafeMapName(new string('a', 129)));

    [Theory]
    [InlineData("""{ "bots": true }""", true)]
    [InlineData("""{ "bots": "true" }""", true)]
    [InlineData("""{ "bots": 1 }""", true)]
    [InlineData("""{ "bots": "1" }""", true)]
    [InlineData("""{ "bots": false }""", false)]
    [InlineData("""{ "bots": 0 }""", false)]
    [InlineData("""{ "bots": "no" }""", false)]
    [InlineData("""{ }""", false)]
    public void BotTeamKey(string team, bool isBotTeam) =>
        Assert.Equal(isBotTeam, MatchConfigLogic.IsBotTeamToken(JObject.Parse(team)));

    [Theory]
    [InlineData("""{ "bot_difficulty": 1 }""", 1)]
    [InlineData("""{ "bot_difficulty": 9 }""", 3)]
    [InlineData("""{ "bot_difficulty": -2 }""", 0)]
    [InlineData("""{ "bot_difficulty": "hard" }""", 2)]
    [InlineData("""{ }""", 2)]
    public void BotDifficultyIsClampedAndDefaultsToTwo(string team, int expected) =>
        Assert.Equal(expected, MatchConfigLogic.BotDifficultyFrom(JObject.Parse(team)));

    [Theory]
    [InlineData("\"any\"", true)]
    [InlineData("\" ANY \"", true)]
    [InlineData("\"*\"", true)]
    [InlineData("\"all\"", false)]
    [InlineData("[]", false)]
    public void OpenRosterToken(string players, bool open) =>
        Assert.Equal(open, MatchConfigLogic.IsOpenRosterToken(JToken.Parse(players)));

    [Fact]
    public void Bo1BanOrderForSevenMapsIsValid()
    {
        var order = new[] { "team1_ban", "team2_ban", "team1_ban", "team2_ban", "team1_ban", "team2_ban" };
        Assert.Equal("", MatchConfigLogic.ValidateMapBanOrder(order, poolCount: 7, numMaps: 1));
    }

    [Fact]
    public void ValidateMapBanOrder_RejectsUnknownOption()
    {
        var order = new List<string> { "team1_ban", "team2_veto", "team1_ban", "team2_ban", "team1_ban", "team2_ban" };
        Assert.StartsWith("Unknown veto option \"team2_veto\"", MatchConfigLogic.ValidateMapBanOrder(order, 7, 1));
    }

    [Fact]
    public void Bo3BanOrderForSevenMapsIsValid()
    {
        var order = new[] { "team1_ban", "team2_ban", "team1_pick", "team2_pick", "team1_ban", "team2_ban" };
        Assert.Equal("", MatchConfigLogic.ValidateMapBanOrder(order, poolCount: 7, numMaps: 3));
    }

    [Fact]
    public void Bo3WithOnlyOnePickFails()
    {
        var order = new[] { "team1_ban", "team2_ban", "team1_pick", "team2_ban", "team1_ban", "team2_ban" };
        Assert.StartsWith("In a series of 3 maps, at least 2 veto options must be picks", MatchConfigLogic.ValidateMapBanOrder(order, 7, 3));
    }

    [Fact]
    public void BanOrderOfTheWrongLengthFails()
    {
        var order = new[] { "team1_ban", "team2_ban", "team1_ban" };
        Assert.StartsWith("The number of maps in the pool 7 must be one larger", MatchConfigLogic.ValidateMapBanOrder(order, 7, 1));
    }

    [Fact]
    public void ShortOrderIsValidWhenEveryMapIsPicked()
    {
        var order = new[] { "team1_ban", "team2_ban", "team1_pick", "team2_pick", "team1_pick" };
        Assert.Equal("", MatchConfigLogic.ValidateMapBanOrder(order, poolCount: 7, numMaps: 3));
    }
}
