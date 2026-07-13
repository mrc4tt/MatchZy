using System.Text.Json.Serialization;

namespace MatchZy;

public class MatchZyEvent
{
    public MatchZyEvent(string eventName)
    {
        EventName = eventName;
    }

    [JsonPropertyName("event")]
    public string EventName { get; }
}

public class MatchZyMatchEvent : MatchZyEvent
{
    [JsonPropertyName("matchid")]
    public required long MatchId { get; init; }

    protected MatchZyMatchEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyMatchTeamEvent : MatchZyMatchEvent
{
    [JsonPropertyName("team")]
    public required string Team { get; init; }

    protected MatchZyMatchTeamEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyMapEvent : MatchZyMatchEvent
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }

    protected MatchZyMapEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyMapTeamEvent : MatchZyMapEvent
{
    [JsonPropertyName("team_int")]
    public required int TeamNumber { get; init; }

    protected MatchZyMapTeamEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyRoundEvent : MatchZyMapEvent
{
    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    protected MatchZyRoundEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyTimedRoundEvent : MatchZyRoundEvent
{
    [JsonPropertyName("round_time")]
    public required int RoundTime { get; init; }

    protected MatchZyTimedRoundEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyPlayerRoundEvent : MatchZyRoundEvent
{
    [JsonPropertyName("player")]
    public required int Player { get; init; }

    protected MatchZyPlayerRoundEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyPlayerTimedRoundEvent : MatchZyTimedRoundEvent
{
    [JsonPropertyName("player")]
    public required int Player { get; init; }

    protected MatchZyPlayerTimedRoundEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyPlayerDisconnectedEvent : MatchZyMatchEvent
{
    [JsonPropertyName("player")]
    public required int Player { get; init; }

    public MatchZyPlayerDisconnectedEvent()
        : base("player_disconnect") { }
}

public class MatchZySeriesStartedEvent : MatchZyMatchEvent
{
    [JsonPropertyName("team1")]
    public required MatchZyTeamWrapper Team1 { get; init; }

    [JsonPropertyName("team2")]
    public required MatchZyTeamWrapper Team2 { get; init; }

    [JsonPropertyName("num_maps")]
    public required int NumberOfMaps { get; init; }

    public MatchZySeriesStartedEvent()
        : base("series_start") { }
}

public class MatchZySeriesResultEvent : MatchZyMatchEvent
{
    [JsonPropertyName("time_until_restore")]
    public required int TimeUntilRestore { get; init; }

    [JsonPropertyName("winner")]
    public required Winner Winner { get; init; }

    [JsonPropertyName("team1_series_score")]
    public required int Team1SeriesScore { get; init; }

    [JsonPropertyName("team2_series_score")]
    public required int Team2SeriesScore { get; init; }

    public MatchZySeriesResultEvent()
        : base("series_end") { }
}

public class GoingLiveEvent : MatchZyMapEvent
{
    public GoingLiveEvent()
        : base("going_live") { }
}

public class MatchZyRoundEndedEvent : MatchZyTimedRoundEvent
{
    [JsonPropertyName("reason")]
    public required int Reason { get; init; }

    [JsonPropertyName("winner")]
    public required Winner Winner { get; init; }

    [JsonPropertyName("team1")]
    public required MatchZyStatsTeam StatsTeam1 { get; init; }

    [JsonPropertyName("team2")]
    public required MatchZyStatsTeam StatsTeam2 { get; init; }

    public MatchZyRoundEndedEvent()
        : base("round_end") { }
}

public class MapResultEvent : MatchZyMapEvent
{
    [JsonPropertyName("winner")]
    public required Winner Winner { get; init; }

    [JsonPropertyName("team1")]
    public required MatchZyStatsTeam StatsTeam1 { get; init; }

    [JsonPropertyName("team2")]
    public required MatchZyStatsTeam StatsTeam2 { get; init; }

    [JsonPropertyName("demo_filename")]
    public string? DemoFilename { get; init; }

    public MapResultEvent()
        : base("map_result") { }
}

public class MatchCancelledEvent : MatchZyMatchEvent
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("demo_filename")]
    public string? DemoFilename { get; init; }

    [JsonPropertyName("team1")]
    public required MatchZyTeamWrapper Team1 { get; init; }

    [JsonPropertyName("team2")]
    public required MatchZyTeamWrapper Team2 { get; init; }

    [JsonPropertyName("team1_score")]
    public int Team1Score { get; init; }

    [JsonPropertyName("team2_score")]
    public int Team2Score { get; init; }

    public MatchCancelledEvent()
        : base("match_cancelled") { }
}

public class MatchZyMapSelectionEvent : MatchZyMatchTeamEvent
{
    [JsonPropertyName("map_name")]
    public required string MapName { get; init; }

    protected MatchZyMapSelectionEvent(string eventName)
        : base(eventName) { }
}

public class MatchZyMapPickedEvent : MatchZyMapSelectionEvent
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }

    public MatchZyMapPickedEvent()
        : base("map_picked") { }
}

public class MatchZyMapVetoedEvent : MatchZyMapSelectionEvent
{
    public MatchZyMapVetoedEvent()
        : base("map_vetoed") { }
}

public class MatchZySidePickedEvent : MatchZyMapSelectionEvent
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }

    [JsonPropertyName("side")]
    public required string Side { get; init; }

    public MatchZySidePickedEvent()
        : base("side_picked") { }
}

public class MatchZyDemoUploadedEvent : MatchZyMatchEvent
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }

    [JsonPropertyName("filename")]
    public required string FileName { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    public MatchZyDemoUploadedEvent()
        : base("demo_upload_ended") { }
}

// ═══════════════════════════════════════════════════════════════════
// Live scorebot events (mid-round, for real-time HLTV-style updates)
// ═══════════════════════════════════════════════════════════════════

public class PlayerDeathLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("attacker_name")]
    public string? AttackerName { get; init; }

    [JsonPropertyName("attacker_steamid")]
    public string? AttackerSteamId { get; init; }

    [JsonPropertyName("attacker_team")]
    public string? AttackerTeam { get; init; }

    [JsonPropertyName("victim_name")]
    public required string VictimName { get; init; }

    [JsonPropertyName("victim_steamid")]
    public required string VictimSteamId { get; init; }

    [JsonPropertyName("victim_team")]
    public required string VictimTeam { get; init; }

    [JsonPropertyName("assister_name")]
    public string? AssisterName { get; init; }

    [JsonPropertyName("assister_steamid")]
    public string? AssisterSteamId { get; init; }

    [JsonPropertyName("weapon")]
    public string? Weapon { get; init; }

    [JsonPropertyName("headshot")]
    public bool Headshot { get; init; }

    [JsonPropertyName("penetrated")]
    public bool Penetrated { get; init; }

    [JsonPropertyName("noscope")]
    public bool Noscope { get; init; }

    [JsonPropertyName("thrusmoke")]
    public bool Thrusmoke { get; init; }

    [JsonPropertyName("attackerblind")]
    public bool Attackerblind { get; init; }

    [JsonPropertyName("is_suicide")]
    public bool IsSuicide { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    public PlayerDeathLiveEvent()
        : base("player_death") { }
}

public class BombPlantedLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    [JsonPropertyName("site")]
    public required string Site { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    public BombPlantedLiveEvent()
        : base("bomb_planted") { }
}

public class BombDefusedLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    [JsonPropertyName("site")]
    public required string Site { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    public BombDefusedLiveEvent()
        : base("bomb_defused") { }
}

public class RoundStartLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    public RoundStartLiveEvent()
        : base("round_start") { }
}

public class FreezetimeEndLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    [JsonPropertyName("players")]
    public List<LivePlayerInfo>? Players { get; init; }

    public FreezetimeEndLiveEvent()
        : base("freezetime_end") { }
}

public class LivePlayerInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("steamid")]
    public required string SteamId { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("hp")]
    public int Hp { get; init; }

    [JsonPropertyName("armor")]
    public int Armor { get; init; }

    [JsonPropertyName("has_helmet")]
    public bool HasHelmet { get; init; }

    [JsonPropertyName("has_defuser")]
    public bool HasDefuser { get; init; }

    [JsonPropertyName("money")]
    public int Money { get; init; }
}

public class PlayerHurtLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("attacker_name")]
    public string? AttackerName { get; init; }

    [JsonPropertyName("attacker_steamid")]
    public string? AttackerSteamId { get; init; }

    [JsonPropertyName("victim_name")]
    public required string VictimName { get; init; }

    [JsonPropertyName("victim_steamid")]
    public required string VictimSteamId { get; init; }

    [JsonPropertyName("victim_team")]
    public required string VictimTeam { get; init; }

    [JsonPropertyName("hp_remaining")]
    public int HpRemaining { get; init; }

    [JsonPropertyName("armor_remaining")]
    public int ArmorRemaining { get; init; }

    [JsonPropertyName("damage_health")]
    public int DamageHealth { get; init; }

    [JsonPropertyName("damage_armor")]
    public int DamageArmor { get; init; }

    [JsonPropertyName("weapon")]
    public string? Weapon { get; init; }

    [JsonPropertyName("hitgroup")]
    public int Hitgroup { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    public PlayerHurtLiveEvent()
        : base("player_hurt") { }
}

// ══════════════════════════════════════════════════════════════════════
// Pause/unpause events (for real-time scorebot)
// ══════════════════════════════════════════════════════════════════════

public class MatchPausedLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("pause_type")]
    public required string PauseType { get; init; } // "tech" | "admin"

    [JsonPropertyName("team_name")]
    public string? TeamName { get; init; }

    [JsonPropertyName("max_duration")]
    public int? MaxDuration { get; init; } // seconds, null = indefinite

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    public MatchPausedLiveEvent()
        : base("match_paused") { }
}

public class MatchUnpausedLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    public MatchUnpausedLiveEvent()
        : base("match_unpaused") { }
}
