# Events

Every event is sent as an HTTP `POST` with a JSON body to `matchzy_remote_log_url`. The `event` field names the event. See the [Live events guide](../guides/events.md) for setup.

## Common fields

- `event`: event name.
- `matchid`: on match events.
- `map_number`: 0-based map index in the series.
- `round_number`: on round and live events.
- Veto events use `team` = `"team1"` / `"team2"`. Live events in the legacy format use sides `"CT"` / `"T"`; the Get5 format uses `"ct"` / `"t"` inside player objects.

Player stats objects (`team1.players[].stats` on `round_end` and `map_result`) use the Get5 field names: `kills`, `deaths`, `assists`, `damage`, `utility_damage`, `enemies_flashed`, `friendlies_flashed`, `flash_assists`, `headshot_kills`, `knife_kills`, `team_kills`, `suicides`, `bomb_plants`, `bomb_defuses`, `rounds_played`, `1k`-`5k`, `1v1`-`1v5` (clutches won), `first_kills_t`, `first_kills_ct`, `first_deaths_t`, `first_deaths_ct`, `trade_kills`, `kast` (rounds with a kill, assist, survival or trade), `score` and `mvp`. Fields beyond the engine's own scoreboard stats are filled for human players; bots report 0 for them. Rating and ADR are in the [stats JSON file](../guides/stats.md#advanced-stats).

## Match flow

| Event | When | Fields |
|---|---|---|
| `series_start` | A match config was loaded | `matchid`, `num_maps`, `team1{id,name}`, `team2{id,name}` |
| `map_vetoed` | A team bans a map | `matchid`, `team`, `map_name` |
| `map_picked` | A team picks a map | `matchid`, `team`, `map_name`, `map_number` |
| `side_picked` | A side is chosen for a picked map | `matchid`, `team`, `map_name`, `map_number`, `side` |
| `knife_won` | The knife winner has chosen a side | `matchid`, `map_number`, `team`, `side` (`ct`/`t`), `swapped` |
| `going_live` | The map goes live | `matchid`, `map_number` |
| `round_start` | Every round start once the match has started | `matchid`, `map_number`, `round_number` |
| `freezetime_end` | Freeze time ends | `round_number`, `ct_alive`, `t_alive`, `players[]` (`name`, `steamid`, `team`, `hp`, `armor`, `has_helmet`, `has_defuser`, `money`) |
| `round_end` | A live round ends | `round_number`, `reason`, `winner{side,team}` (the team that won this round), `team1`, `team2` (scores and player stats) |
| `map_result` | The map ends | `map_number`, `winner`, `team1`, `team2`, `demo_filename` |
| `series_end` | The series ends | `winner`, `team1_series_score`, `team2_series_score`, `time_until_restore` |
| `match_cancelled` | A loaded or running match is stopped, surrendered, restarted, cancelled for a no-show, or ended by a map change from outside MatchZy | `reason` (`ended_early`, `surrendered`, `restarted`, `no_show`, `map_changed`), `demo_filename`, `team1`, `team2`, `team1_score`, `team2_score` |
| `match_paused` | Any pause starts | `round_number`, `pause_type` (`pause`, `tech`, `admin`, `auto`), `team_name`, `max_duration` |
| `match_unpaused` | Any pause ends | `round_number` |
| `demo_upload_ended` | A demo upload finished | `map_number`, `filename`, `success` |
| `player_disconnect` | A player leaves while a match is loaded | `player` (user id), `player_steamid`, `player_name`, `player_team`, `reason` |

## Server lifecycle

Sent with or without a loaded match, to `matchzy_remote_log_url` from `config.cfg`. While a loaded match uses its own remote log URL, they go there as well. All three carry `matchid` (`null` without a match), `map_name` (the current map) and `plugin_version`.

| Event | When | Fields |
|---|---|---|
| `server_ready` | All plugins are loaded and `config.cfg` is applied (about 2 seconds after load; on a boot, once the first map runs) | `hot_reload` (`true` when only MatchZy was reloaded) |
| `map_change` | Before the map changes. Sent when a map command runs (`changelevel`, `map`, `ds_workshop_changelevel`, `host_workshop_map`), otherwise when the map ends | `next_map` (map name or workshop id, `null` when not known), `trigger` (the command, or `map_end`) |
| `server_shutdown` | The server is about to stop: `quit` / `exit`, `_restart`, a fatal error, or MatchZy being unloaded | `reason` (`quit`, `restart`, `fatal`, `plugin_unload`) |

`server_shutdown` is sent while the server waits up to 3 seconds for it. A server that is killed (crash, `SIGKILL`, a panel's force stop) sends nothing; use `server_ready` on the next start to notice it.

## Live scorebot

`matchzy_events_format` picks the format of these events: `get5` (default) or `legacy` (the flat format of 0.8.94 and older, see [below](#legacy-format)).

### Get5 format

Events that Get5 also has (`player_death`, `bomb_planted`, `bomb_defused`, `bomb_exploded`, `grenade_thrown`, `round_start`, `game_paused`, `game_unpaused`, `player_disconnect`, `backup_loaded`) use [Get5's event schema](https://splewis.github.io/get5/latest/events.html), so Get5 tooling can read them as is. MatchZy's own events use the same building blocks. Fields Get5 does not have (marked *extra*) are added on top; a Get5 consumer ignores them.

Building blocks:

- **Player object**: `{"steamid", "name", "user_id", "side", "is_bot"}`. `side` is `ct`, `t`, `spec` or `null`; a bot's `steamid` is `BOT-<user_id>`.
- **Weapon object**: `{"name", "id"}`. `name` without the `weapon_` prefix (`ak47`, `hegrenade`, `planted_c4`). `id` is SourceMod's weapon ID as Get5 sends it (`ak47` = 27), `0` for weapons without one (MP5-SD), the bomb, fire and the world.
- **`round_time`**: milliseconds since freeze time ended, `0` during freeze time.
- **`round_number`**: 0-based, the number of rounds already played.

| Event | When | Fields |
|---|---|---|
| `player_death` | A player dies (including suicides, world and bomb deaths) | `player` (victim), `attacker` (or `null`), `assist` (`{player, friendly_fire, flash_assist}` or `null`), `weapon`, `bomb`, `headshot`, `thru_smoke`, `penetrated` (objects passed through), `attacker_blind`, `no_scope`, `suicide`, `friendly_fire`, `round_time`; *extra* `ct_alive`, `t_alive` |
| `player_kill` | A player kills another player (not suicides or world deaths) | everything in `player_death`, plus `attacker_hp`, `distance`, `first_kill`, `trade_kill`, `attacker_round_kills`, `attacker_map_kills` |
| `player_hurt` | A player takes damage | `player` (victim), `attacker` (or `null`), `weapon`, `damage`, `damage_armor`, `health`, `armor` (left after the hit), `hitgroup`, `friendly_fire`, `round_time` |
| `bomb_planted` | Bomb planted | `player`, `site` (`a`/`b`), `round_time`; *extra* `ct_alive`, `t_alive` |
| `bomb_defused` | Bomb defused | same as `bomb_planted`, plus `bomb_time_remaining` (ms) |
| `bomb_exploded` | Bomb exploded | `site`, `round_time`; *extra* `ct_alive`, `t_alive` |
| `bomb_pickup`, `bomb_dropped` | Bomb picked up / dropped | `player`, `round_time` |
| `grenade_thrown` | Grenade thrown | `player`, `weapon`, `round_time` |
| `grenade_detonated` | Smoke, flash, HE or molotov detonated | `player` (thrower, or `null`), `weapon`, `x`, `y`, `z`, `round_time` |
| `player_blinded` | A player is flashed | `player` (victim), `attacker` (or `null`), `blind_duration` (seconds), `friendly_fire`, `round_time` |
| `freezetime_end` | Freeze time ended | `players` (player objects with `health`, `armor`, `has_helmet`, `has_defuser`, `money`), `ct_alive`, `t_alive` |
| `round_start` | A live round starts | `round_number` |
| `game_paused`, `game_unpaused` | Match paused / unpaused | `team` (`team1`, `team2`, or `null` for admin, automatic and restore pauses), `pause_type` (`tactical`, `technical`, `admin`, `backup` after a round restore); *extra* `round_number`, `max_duration` (seconds of a timed technical pause) |
| `player_disconnect` | A player leaves while a match is loaded | `player`; *extra* `reason` (engine disconnect code) |
| `backup_loaded` | A round backup was restored | `round_number` (the round restored to), `filename`. Followed by that round's `round_start`; the engine's own round start after the load is not sent again. |

All live events carry `matchid`, `map_number` and `round_number`. In this format `round_end`'s `round_number` is also Get5's: the rounds played when the round started (the legacy format sends the rounds played after it). `matchid` is a number, as in every other MatchZy event (Get5 sends a string).

Get5's per-grenade detonation events with victim lists (`hegrenade_detonated`, `flashbang_detonated`, ...) are not sent; use `grenade_detonated`, `player_hurt` and `player_blinded`.

`player_kill` and `player_death` both fire for a kill. Use `player_kill` for a kill feed or multi-kill tracking (`attacker_round_kills` 2 = double kill, 5 = ace) and `player_death` when you also need suicides and deaths by the world.

`player_death`:

```json
{
  "event": "player_death",
  "matchid": 1042,
  "map_number": 0,
  "round_number": 7,
  "round_time": 51434,
  "player": { "steamid": "76561198000000013", "name": "bravo3", "user_id": 9, "side": "t", "is_bot": false },
  "weapon": { "name": "ak47", "id": 7 },
  "bomb": false,
  "headshot": true,
  "thru_smoke": false,
  "penetrated": 0,
  "attacker_blind": false,
  "no_scope": false,
  "suicide": false,
  "friendly_fire": false,
  "attacker": { "steamid": "76561198000000001", "name": "alpha1", "user_id": 3, "side": "ct", "is_bot": false },
  "assist": {
    "player": { "steamid": "76561198000000002", "name": "alpha2", "user_id": 4, "side": "ct", "is_bot": false },
    "friendly_fire": false,
    "flash_assist": true
  },
  "ct_alive": 5,
  "t_alive": 3
}
```

`bomb_planted`:

```json
{
  "event": "bomb_planted",
  "matchid": 1042,
  "map_number": 0,
  "round_number": 7,
  "round_time": 38120,
  "player": { "steamid": "76561198000000013", "name": "bravo3", "user_id": 9, "side": "t", "is_bot": false },
  "site": "a",
  "ct_alive": 3,
  "t_alive": 4
}
```

### Legacy format

`matchzy_events_format legacy` sends the flat format below, unchanged from 0.8.94 and older. Pauses are sent as `match_paused` (`pause_type` `tech`, `pause`, `admin` or `auto`, `team_name`, `max_duration`) and `match_unpaused`.


| Event | When | Fields |
|---|---|---|
| `player_kill` | A player kills another player (not suicides or world deaths) | `killer_name`, `killer_steamid`, `killer_team`, `killer_hp`, `victim_name`, `victim_steamid`, `victim_team`, `assister_name`, `assister_steamid`, `flash_assist`, `weapon`, `headshot`, `penetrated`, `noscope`, `thrusmoke`, `attackerblind`, `distance`, `team_kill`, `first_kill`, `trade_kill`, `killer_round_kills`, `killer_map_kills`, `ct_alive`, `t_alive` |
| `player_death` | A player dies (including suicides and world deaths) | `attacker_name`, `attacker_steamid`, `attacker_team`, `victim_name`, `victim_steamid`, `victim_team`, `assister_name`, `assister_steamid`, `weapon`, `headshot`, `penetrated`, `noscope`, `thrusmoke`, `attackerblind`, `is_suicide`, `ct_alive`, `t_alive` |
| `player_hurt` | A player takes damage (live) | `attacker_name`, `attacker_steamid`, `victim_name`, `victim_steamid`, `victim_team`, `hp_remaining`, `armor_remaining`, `damage_health`, `damage_armor`, `weapon`, `hitgroup` |
| `bomb_planted` | Bomb planted | `player_name`, `player_steamid`, `site` (`A`/`B`), `ct_alive`, `t_alive` |
| `bomb_defused` | Bomb defused | same as `bomb_planted` |
| `bomb_exploded` | Bomb exploded | `site`, `ct_alive`, `t_alive` |
| `bomb_pickup`, `bomb_dropped` | Bomb picked up / dropped | `player_name`, `player_steamid` |
| `grenade_thrown` | Grenade thrown | `player_name`, `player_steamid`, `player_team`, `grenade` (`smoke`, `flash`, `he`, `molotov`, `incendiary`, `decoy`) |
| `grenade_detonated` | Grenade detonated | `player_name`, `player_steamid`, `player_team`, `grenade`, `x`, `y`, `z` |
| `player_blinded` | A player is flashed | `attacker_*`, `victim_*`, `duration` (seconds), `team_flash` |

All legacy live events also carry `matchid`, `map_number` and `round_number`.

`player_kill` and `player_death` both fire for a kill. Use `player_kill` for a kill feed or multi-kill tracking (`killer_round_kills` 2 = double kill, 5 = ace) and `player_death` when you also need suicides and deaths by the world.

#### Legacy examples

`player_kill`:

```json
{
  "event": "player_kill",
  "matchid": 1042,
  "map_number": 0,
  "round_number": 7,
  "killer_name": "alpha1",
  "killer_steamid": "76561198000000001",
  "killer_team": "CT",
  "killer_hp": 64,
  "victim_name": "bravo3",
  "victim_steamid": "76561198000000013",
  "victim_team": "T",
  "assister_name": "alpha2",
  "assister_steamid": "76561198000000002",
  "flash_assist": true,
  "weapon": "ak47",
  "headshot": true,
  "penetrated": false,
  "noscope": false,
  "thrusmoke": false,
  "attackerblind": false,
  "distance": 1243.7,
  "team_kill": false,
  "first_kill": false,
  "trade_kill": true,
  "killer_round_kills": 2,
  "killer_map_kills": 14,
  "ct_alive": 5,
  "t_alive": 3
}
```

`player_death`:

```json
{
  "event": "player_death",
  "matchid": 1042,
  "map_number": 0,
  "round_number": 7,
  "attacker_name": "alpha1",
  "attacker_steamid": "76561198000000001",
  "attacker_team": "CT",
  "victim_name": "bravo3",
  "victim_steamid": "76561198000000013",
  "victim_team": "T",
  "assister_name": null,
  "assister_steamid": null,
  "weapon": "ak47",
  "headshot": true,
  "penetrated": false,
  "noscope": false,
  "thrusmoke": false,
  "attackerblind": false,
  "is_suicide": false,
  "ct_alive": 5,
  "t_alive": 3
}
```
