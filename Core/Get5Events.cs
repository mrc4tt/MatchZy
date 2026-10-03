using System.Text.Json.Serialization;

namespace MatchZy;

// Live events in the Get5 event format (matchzy_events_format "get5", the default). Field names and
// nesting follow Get5's published event schema (Get5Player, Get5Weapon, Get5AssisterObject, ...),
// so a Get5 consumer can parse them as is. Fields Get5 does not have (ct_alive, t_alive, ...) are
// extra properties; Get5 consumers ignore unknown fields. The legacy flat format lives in Events.cs.

public class Get5PlayerInfo
{
    // SteamID64, or "BOT-<user_id>" for a bot (as Get5 sends it).
    [JsonPropertyName("steamid")]
    public required string SteamId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("user_id")]
    public int UserId { get; init; }

    // "ct" | "t" | "spec", null when the player has no side.
    [JsonPropertyName("side")]
    public string? Side { get; init; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; init; }
}

public class Get5WeaponInfo
{
    // In-game weapon name without the "weapon_" prefix ("ak47", "hegrenade", "planted_c4", ...).
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    // SourceMod's CSWeaponID (ak47 = 27), as Get5 sends it; 0 when the weapon has none (world,
    // bomb, fire, MP5-SD).
    [JsonPropertyName("id")]
    public int Id { get; init; }
}

public class Get5AssistInfo
{
    [JsonPropertyName("player")]
    public required Get5PlayerInfo Player { get; init; }

    [JsonPropertyName("friendly_fire")]
    public bool FriendlyFire { get; init; }

    [JsonPropertyName("flash_assist")]
    public bool FlashAssist { get; init; }
}

public abstract class Get5PlayerTimedRoundEvent : MatchZyTimedRoundEvent
{
    [JsonPropertyName("player")]
    public required Get5PlayerInfo Player { get; init; }

    protected Get5PlayerTimedRoundEvent(string eventName)
        : base(eventName) { }
}

public class Get5PlayerDeathEvent : Get5PlayerTimedRoundEvent
{
    [JsonPropertyName("weapon")]
    public required Get5WeaponInfo Weapon { get; init; }

    [JsonPropertyName("bomb")]
    public bool Bomb { get; init; }

    [JsonPropertyName("headshot")]
    public bool Headshot { get; init; }

    [JsonPropertyName("thru_smoke")]
    public bool ThruSmoke { get; init; }

    // Number of objects (walls, players) the bullet went through.
    [JsonPropertyName("penetrated")]
    public int Penetrated { get; init; }

    [JsonPropertyName("attacker_blind")]
    public bool AttackerBlind { get; init; }

    [JsonPropertyName("no_scope")]
    public bool NoScope { get; init; }

    [JsonPropertyName("suicide")]
    public bool Suicide { get; init; }

    [JsonPropertyName("friendly_fire")]
    public bool FriendlyFire { get; init; }

    [JsonPropertyName("attacker")]
    public Get5PlayerInfo? Attacker { get; init; }

    [JsonPropertyName("assist")]
    public Get5AssistInfo? Assist { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    public Get5PlayerDeathEvent()
        : base("player_death") { }

    protected Get5PlayerDeathEvent(string eventName)
        : base(eventName) { }
}

// MatchZy-only: one per kill (no suicides or world deaths), same shape as player_death plus the
// killer-centric extras. player = victim, attacker = killer, as in player_death.
public class Get5PlayerKillEvent : Get5PlayerDeathEvent
{
    [JsonPropertyName("attacker_hp")]
    public int AttackerHp { get; init; }

    [JsonPropertyName("distance")]
    public double? Distance { get; init; }

    [JsonPropertyName("first_kill")]
    public bool FirstKill { get; init; }

    [JsonPropertyName("trade_kill")]
    public bool TradeKill { get; init; }

    [JsonPropertyName("attacker_round_kills")]
    public int AttackerRoundKills { get; init; }

    [JsonPropertyName("attacker_map_kills")]
    public int AttackerMapKills { get; init; }

    public Get5PlayerKillEvent()
        : base("player_kill") { }
}

public class Get5BombPlayerEvent : Get5PlayerTimedRoundEvent
{
    // "a" | "b", null when the site could not be read.
    [JsonPropertyName("site")]
    public string? Site { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    // bomb_planted | bomb_defused
    public Get5BombPlayerEvent(string eventName)
        : base(eventName) { }
}

public class Get5BombDefusedEvent : Get5BombPlayerEvent
{
    // Milliseconds left on the bomb timer when it was defused.
    [JsonPropertyName("bomb_time_remaining")]
    public int BombTimeRemaining { get; init; }

    public Get5BombDefusedEvent()
        : base("bomb_defused") { }
}

public class Get5BombExplodedEvent : MatchZyTimedRoundEvent
{
    [JsonPropertyName("site")]
    public string? Site { get; init; }

    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    public Get5BombExplodedEvent()
        : base("bomb_exploded") { }
}

public class Get5GrenadeThrownEvent : Get5PlayerTimedRoundEvent
{
    [JsonPropertyName("weapon")]
    public required Get5WeaponInfo Weapon { get; init; }

    public Get5GrenadeThrownEvent()
        : base("grenade_thrown") { }
}

// MatchZy-only: a grenade detonated (smoke, flash, HE, molotov/incendiary) and where. Get5's
// per-type detonation events with victim lists are not sent.
public class Get5GrenadeDetonatedEvent : MatchZyTimedRoundEvent
{
    // The thrower, null when they already left.
    [JsonPropertyName("player")]
    public Get5PlayerInfo? Player { get; init; }

    [JsonPropertyName("weapon")]
    public required Get5WeaponInfo Weapon { get; init; }

    [JsonPropertyName("x")]
    public float X { get; init; }

    [JsonPropertyName("y")]
    public float Y { get; init; }

    [JsonPropertyName("z")]
    public float Z { get; init; }

    public Get5GrenadeDetonatedEvent()
        : base("grenade_detonated") { }
}

// MatchZy-only: damage dealt to a player. player = victim.
public class Get5PlayerHurtEvent : Get5PlayerTimedRoundEvent
{
    [JsonPropertyName("attacker")]
    public Get5PlayerInfo? Attacker { get; init; }

    [JsonPropertyName("weapon")]
    public required Get5WeaponInfo Weapon { get; init; }

    [JsonPropertyName("damage")]
    public int Damage { get; init; }

    [JsonPropertyName("damage_armor")]
    public int DamageArmor { get; init; }

    [JsonPropertyName("health")]
    public int Health { get; init; }

    [JsonPropertyName("armor")]
    public int Armor { get; init; }

    [JsonPropertyName("hitgroup")]
    public int Hitgroup { get; init; }

    [JsonPropertyName("friendly_fire")]
    public bool FriendlyFire { get; init; }

    public Get5PlayerHurtEvent()
        : base("player_hurt") { }
}

// MatchZy-only: a player was flashed. player = victim, attacker = who threw the flash.
public class Get5PlayerBlindedEvent : Get5PlayerTimedRoundEvent
{
    [JsonPropertyName("attacker")]
    public Get5PlayerInfo? Attacker { get; init; }

    // Seconds.
    [JsonPropertyName("blind_duration")]
    public float BlindDuration { get; init; }

    [JsonPropertyName("friendly_fire")]
    public bool FriendlyFire { get; init; }

    public Get5PlayerBlindedEvent()
        : base("player_blinded") { }
}

// MatchZy-only: bomb_pickup | bomb_dropped.
public class Get5BombCarrierEvent : Get5PlayerTimedRoundEvent
{
    public Get5BombCarrierEvent(string eventName)
        : base(eventName) { }
}

public class Get5FreezetimePlayer : Get5PlayerInfo
{
    [JsonPropertyName("health")]
    public int Health { get; init; }

    [JsonPropertyName("armor")]
    public int Armor { get; init; }

    [JsonPropertyName("has_helmet")]
    public bool HasHelmet { get; init; }

    [JsonPropertyName("has_defuser")]
    public bool HasDefuser { get; init; }

    [JsonPropertyName("money")]
    public int Money { get; init; }
}

// MatchZy-only: freeze time ended, with every player's loadout state.
public class Get5FreezetimeEndEvent : MatchZyRoundEvent
{
    [JsonPropertyName("ct_alive")]
    public int CtAlive { get; init; }

    [JsonPropertyName("t_alive")]
    public int TAlive { get; init; }

    [JsonPropertyName("players")]
    public required List<Get5FreezetimePlayer> Players { get; init; }

    public Get5FreezetimeEndEvent()
        : base("freezetime_end") { }
}

public class Get5MatchPauseEvent : MatchZyMapEvent
{
    // "team1" | "team2", null for an admin or automatic pause.
    [JsonPropertyName("team")]
    public string? Team { get; init; }

    // "tactical" | "technical" | "admin" | "backup"
    [JsonPropertyName("pause_type")]
    public required string PauseType { get; init; }

    [JsonPropertyName("round_number")]
    public int RoundNumber { get; init; }

    // Seconds for a technical pause with a time limit, null otherwise (game_paused only).
    [JsonPropertyName("max_duration")]
    public int? MaxDuration { get; init; }

    // game_paused | game_unpaused
    public Get5MatchPauseEvent(string eventName)
        : base(eventName) { }
}

public class Get5PlayerDisconnectEvent : MatchZyMatchEvent
{
    [JsonPropertyName("player")]
    public required Get5PlayerInfo Player { get; init; }

    // Engine disconnect reason code (ENetworkDisconnectionReason). Not in Get5.
    [JsonPropertyName("reason")]
    public int Reason { get; init; }

    public Get5PlayerDisconnectEvent()
        : base("player_disconnect") { }
}

// A round backup was restored. round_number is the round restored to (rounds played at its start).
// Sent in both event formats; in the Get5 format it is followed by that round's round_start.
public class MatchZyBackupLoadedEvent : MatchZyRoundEvent
{
    [JsonPropertyName("filename")]
    public required string FileName { get; init; }

    public MatchZyBackupLoadedEvent()
        : base("backup_loaded") { }
}
