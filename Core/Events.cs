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
    // Engine user id of the player that left.
    [JsonPropertyName("player")]
    public required int Player { get; init; }

    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    // "CT" / "T" / "SPEC" / "none"
    [JsonPropertyName("player_team")]
    public required string PlayerTeam { get; init; }

    // Engine disconnect reason code (ENetworkDisconnectionReason).
    [JsonPropertyName("reason")]
    public required int Reason { get; init; }

    public MatchZyPlayerDisconnectedEvent()
        : base("player_disconnect") { }
}

public class MatchZyPlayerConnectedEvent : MatchZyMatchEvent
{
    // Engine user id of the player that joined.
    [JsonPropertyName("player")]
    public required int Player { get; init; }

    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    // "CT" / "T" / "SPEC" / "none"
    [JsonPropertyName("player_team")]
    public required string PlayerTeam { get; init; }

    // Without the port.
    [JsonPropertyName("ip_address")]
    public required string IpAddress { get; init; }

    public MatchZyPlayerConnectedEvent()
        : base("player_connect") { }
}

// A chat line (chat commands included). round_number is -1 while the match is not live.
public class PlayerSayLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    // "CT" / "T" / "SPEC" / "none"
    [JsonPropertyName("player_team")]
    public required string PlayerTeam { get; init; }

    // "say" | "say_team"
    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    public PlayerSayLiveEvent()
        : base("player_say") { }
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

/// <summary>Get5 knife_won: the knife round winner and the side it chose.</summary>
public class MatchZyKnifeWonEvent : MatchZyMapEvent
{
    [JsonPropertyName("team")]
    public required string Team { get; init; }

    // "ct" or "t": the side the winner starts the map on.
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    // True when the winner switched sides after the knife round.
    [JsonPropertyName("swapped")]
    public bool Swapped { get; init; }

    public MatchZyKnifeWonEvent()
        : base("knife_won") { }
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

/// <summary>
/// One per kill of an enemy (or a teammate, flagged team_kill); suicides and world deaths are not
/// kills and only appear as player_death. Killer-centric, for kill feeds and scorebots.
/// </summary>
public class PlayerKillLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    [JsonPropertyName("killer_name")]
    public required string KillerName { get; init; }

    [JsonPropertyName("killer_steamid")]
    public required string KillerSteamId { get; init; }

    [JsonPropertyName("killer_team")]
    public required string KillerTeam { get; init; }

    [JsonPropertyName("killer_hp")]
    public int KillerHp { get; init; }

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

    [JsonPropertyName("flash_assist")]
    public bool FlashAssist { get; init; }

    [JsonPropertyName("weapon")]
    public required string Weapon { get; init; }

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

    // Units between killer and victim, null if a position was not available.
    [JsonPropertyName("distance")]
    public double? Distance { get; init; }

    [JsonPropertyName("team_kill")]
    public bool TeamKill { get; init; }

    // First enemy kill of the round (opening kill).
    [JsonPropertyName("first_kill")]
    public bool FirstKill { get; init; }

    // Killed the enemy who killed a teammate within the last 5 seconds (human players).
    [JsonPropertyName("trade_kill")]
    public bool TradeKill { get; init; }

    // Enemy kills by the killer in this round, including this one (2 = double kill, ...).
    [JsonPropertyName("killer_round_kills")]
    public int KillerRoundKills { get; init; }

    // Enemy kills by the killer on this map, including this one.
    [JsonPropertyName("killer_map_kills")]
    public int KillerMapKills { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    public PlayerKillLiveEvent()
        : base("player_kill") { }
}

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
    public required string PauseType { get; init; } // "tech" | "pause" | "auto" | "admin" | "backup" | "tactical"

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

// ══════════════════════════════════════════════════════════════════════
// Grenade / flash / bomb carrier events (for real-time scorebot)
// ══════════════════════════════════════════════════════════════════════

public class GrenadeThrownLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    [JsonPropertyName("player_team")]
    public required string PlayerTeam { get; init; }

    [JsonPropertyName("grenade")]
    public required string Grenade { get; init; } // "smoke" | "flash" | "he" | "molotov" | "incendiary" | "decoy"

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    public GrenadeThrownLiveEvent()
        : base("grenade_thrown") { }
}

public class GrenadeDetonatedLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("player_name")]
    public string? PlayerName { get; init; }

    [JsonPropertyName("player_steamid")]
    public string? PlayerSteamId { get; init; }

    [JsonPropertyName("player_team")]
    public string? PlayerTeam { get; init; }

    [JsonPropertyName("grenade")]
    public required string Grenade { get; init; } // "smoke" | "flash" | "he" | "molotov"

    [JsonPropertyName("x")]
    public float X { get; init; }

    [JsonPropertyName("y")]
    public float Y { get; init; }

    [JsonPropertyName("z")]
    public float Z { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    public GrenadeDetonatedLiveEvent()
        : base("grenade_detonated") { }
}

public class PlayerBlindedLiveEvent : MatchZyMapEvent
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

    [JsonPropertyName("duration")]
    public float Duration { get; init; } // seconds

    [JsonPropertyName("team_flash")]
    public bool TeamFlash { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    public PlayerBlindedLiveEvent()
        : base("player_blinded") { }
}

public class BombCarrierLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    // "bomb_pickup" | "bomb_dropped"
    public BombCarrierLiveEvent(string eventName)
        : base(eventName) { }
}

public class BombExplodedLiveEvent : MatchZyMapEvent
{
    [JsonPropertyName("site")]
    public required string Site { get; init; }

    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    public BombExplodedLiveEvent()
        : base("bomb_exploded") { }
}

// Server lifecycle events (server_ready, map_change, server_shutdown). Not tied to a match: they
// are sent with or without a loaded match, and matchid is null when there is none.
public abstract class ServerLifecycleEvent : MatchZyEvent
{
    [JsonPropertyName("matchid")]
    public long? MatchId { get; init; }

    [JsonPropertyName("map_name")]
    public required string MapName { get; init; }

    [JsonPropertyName("plugin_version")]
    public required string PluginVersion { get; init; }

    protected ServerLifecycleEvent(string eventName)
        : base(eventName) { }
}

public class ServerReadyEvent : ServerLifecycleEvent
{
    // true when only the plugin was reloaded, false on a server boot.
    [JsonPropertyName("hot_reload")]
    public bool HotReload { get; init; }

    public ServerReadyEvent()
        : base("server_ready") { }
}

public class MapChangeEvent : ServerLifecycleEvent
{
    // The requested map (map name or workshop id), or null when the change did not come from a
    // map command (mapcycle, nextlevel, another plugin calling the engine directly).
    [JsonPropertyName("next_map")]
    public string? NextMap { get; init; }

    // The command that requested the change ("changelevel", "map", "ds_workshop_changelevel",
    // "host_workshop_map"), or "map_end" when only the map end was seen.
    [JsonPropertyName("trigger")]
    public required string Trigger { get; init; }

    public MapChangeEvent()
        : base("map_change") { }
}

public class ServerShutdownEvent : ServerLifecycleEvent
{
    // "quit", "restart", "fatal", "plugin_unload", "empty" (matchzy_empty_shutdown_seconds) or
    // "series_end" (matchzy_series_end_shutdown_seconds).
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    // reason "empty": seconds the server had no players. Null otherwise.
    [JsonPropertyName("empty_seconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EmptySeconds { get; init; }

    public ServerShutdownEvent()
        : base("server_shutdown") { }
}

// ══════════════════════════════════════════════════════════════════════
// Ready-up, unpause request, demo start and admin call events. Field names follow the
// MatchZy Enhanced shapes that tournament panels (e.g. auto-tournament) already read.
// ══════════════════════════════════════════════════════════════════════

public class ReadyEventPlayer
{
    [JsonPropertyName("steamid")]
    public required string SteamId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    // "team1" / "team2" / "spectator"
    [JsonPropertyName("team")]
    public required string Team { get; init; }
}

public class PlayerReadyChangedEvent : MatchZyMapEvent
{
    [JsonPropertyName("player")]
    public required ReadyEventPlayer Player { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("ready_count_team1")]
    public int ReadyCountTeam1 { get; init; }

    [JsonPropertyName("ready_count_team2")]
    public int ReadyCountTeam2 { get; init; }

    [JsonPropertyName("total_ready")]
    public int TotalReady { get; init; }

    [JsonPropertyName("expected_total")]
    public int ExpectedTotal { get; init; }

    // "player_ready" or "player_unready"
    public PlayerReadyChangedEvent(string eventName)
        : base(eventName) { }
}

public class TeamReadyEvent : MatchZyMapEvent
{
    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("ready_count")]
    public int ReadyCount { get; init; }

    [JsonPropertyName("total_ready")]
    public int TotalReady { get; init; }

    [JsonPropertyName("expected_total")]
    public int ExpectedTotal { get; init; }

    // true when the team used .forceready
    [JsonPropertyName("forced")]
    public bool Forced { get; init; }

    public TeamReadyEvent()
        : base("team_ready") { }
}

public class AllPlayersReadyEvent : MatchZyMapEvent
{
    [JsonPropertyName("ready_count_team1")]
    public int ReadyCountTeam1 { get; init; }

    [JsonPropertyName("ready_count_team2")]
    public int ReadyCountTeam2 { get; init; }

    [JsonPropertyName("total_ready")]
    public int TotalReady { get; init; }

    // true in join ready mode, where the match starts after matchzy_join_start_delay
    [JsonPropertyName("countdown_started")]
    public bool CountdownStarted { get; init; }

    public AllPlayersReadyEvent()
        : base("all_players_ready") { }
}

public class UnpauseRequestedEvent : MatchZyMapEvent
{
    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("teams_ready")]
    public int TeamsReady { get; init; }

    [JsonPropertyName("teams_needed")]
    public int TeamsNeeded { get; init; }

    [JsonPropertyName("round_number")]
    public int RoundNumber { get; init; }

    public UnpauseRequestedEvent()
        : base("unpause_requested") { }
}

public class DemoRecordingStartEvent : MatchZyMapEvent
{
    [JsonPropertyName("filename")]
    public required string FileName { get; init; }

    public DemoRecordingStartEvent()
        : base("demo_recording_start") { }
}

public class AdminCallPlayer
{
    [JsonPropertyName("steamid64")]
    public required string SteamId64 { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    // "team1" / "team2" / "spectator", null without a loaded match
    [JsonPropertyName("team")]
    public string? Team { get; init; }

    // "ct" / "t", null for spectators
    [JsonPropertyName("side")]
    public string? Side { get; init; }
}

public class AdminTicketServer
{
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }

    // ip:port as the server binds it (0.0.0.0 unless started with -ip; the port tells servers apart).
    [JsonPropertyName("address")]
    public required string Address { get; init; }
}

// The .admin / .report ticket events. Sent with or without a loaded match, like the lifecycle
// events (to the server's and the match's remote log URL).
public abstract class AdminTicketEvent : ServerLifecycleEvent
{
    [JsonPropertyName("map_number")]
    public int? MapNumber { get; init; }

    // One conversation: the match's ticket (.admin) or one player's report (.report).
    [JsonPropertyName("thread_id")]
    public required string ThreadId { get; init; }

    // "admin" (seen by every player in the match) or "report" (only the reporter and the admins).
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("server")]
    public required AdminTicketServer Server { get; init; }

    protected AdminTicketEvent(string eventName)
        : base(eventName) { }
}

public class AdminCalledEvent : AdminTicketEvent
{
    // Unique per message, so a panel can store a retried delivery once.
    [JsonPropertyName("call_id")]
    public required string CallId { get; init; }

    // true for the first message of a thread (create the ticket), false for the ones after it.
    [JsonPropertyName("new_thread")]
    public bool NewThread { get; init; }

    [JsonPropertyName("player")]
    public required AdminCallPlayer Player { get; init; }

    // At most 200 characters, may be empty.
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    // ISO 8601, UTC.
    [JsonPropertyName("called_at")]
    public required string CalledAt { get; init; }

    public AdminCalledEvent()
        : base("admin_called") { }
}

// An admin answered (matchzy_admin_reply), so the reply is in the panel's history too.
public class AdminReplyEvent : AdminTicketEvent
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("sent_at")]
    public required string SentAt { get; init; }

    public AdminReplyEvent()
        : base("admin_reply") { }
}

public class AdminThreadClosedEvent : AdminTicketEvent
{
    // "admin" (matchzy_admin_close), "match_end" or "timeout" (no message for matchzy_admin_ticket_timeout minutes)
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("closed_at")]
    public required string ClosedAt { get; init; }

    public AdminThreadClosedEvent()
        : base("admin_thread_closed") { }
}

// knife_round_started, knife_round_ended, halftime_started, overtime_started, side_swap.
public class MatchPhaseEvent : MatchZyMapEvent
{
    // Rounds played on this map when the phase changed.
    [JsonPropertyName("round_number")]
    public int RoundNumber { get; init; }

    // overtime_started: 1 for the first overtime, 2 for the next, ...
    [JsonPropertyName("overtime_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OvertimeNumber { get; init; }

    // knife_round_ended: "team1" / "team2" (the knife winner, who now picks a side)
    [JsonPropertyName("winner")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Winner { get; init; }

    // side_swap: team1's side after the swap ("ct" / "t")
    [JsonPropertyName("team1_side")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Team1Side { get; init; }

    public MatchPhaseEvent(string eventName)
        : base(eventName) { }
}

// matchzy_auto_recover brought back a match that a crash, restart or plugin reload interrupted.
public class MatchRecoveredEvent : ServerLifecycleEvent
{
    // round_backup (newest round backup restored or queued), match_config (no round backup yet:
    // the match config was loaded again) or failed.
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("backup_file")]
    public string? BackupFile { get; init; }

    public MatchRecoveredEvent()
        : base("match_recovered") { }
}

// A player the connect gate (or the series-end sweep) kicked: why, and what the server had
// loaded at the time, so a panel can show why a player could not get in.
public class PlayerKickedEvent : ServerLifecycleEvent
{
    [JsonPropertyName("player_steamid")]
    public required string PlayerSteamId { get; init; }

    [JsonPropertyName("player_name")]
    public required string PlayerName { get; init; }

    // not_whitelisted, no_match_loaded, not_in_match, series_over
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    // Whether a match config was loaded when the player was kicked.
    [JsonPropertyName("match_loaded")]
    public bool MatchLoaded { get; init; }

    public PlayerKickedEvent()
        : base("player_kicked") { }
}

// matchzy_test_event: a connectivity probe for a panel's endpoint.
public class TestEvent : ServerLifecycleEvent
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    public TestEvent()
        : base("test_event") { }
}
