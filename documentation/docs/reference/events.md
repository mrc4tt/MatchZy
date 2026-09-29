# Events

Every event is sent as an HTTP `POST` with a JSON body to `matchzy_remote_log_url`. The `event` field names the event. See the [Live events guide](../guides/events.md) for setup.

:fontawesome-solid-code-fork: = not sent by upstream MatchZy.

## Common fields

- `event`: event name.
- `matchid`: on match events.
- `map_number`: 0-based map index in the series.
- `round_number`: on round and live events.
- Veto events use `team` = `"team1"` / `"team2"`. Live events use sides `"CT"` / `"T"`.

Player stats objects (`team1.players[].stats` on `round_end` and `map_result`) use the Get5 field names: `kills`, `deaths`, `assists`, `damage`, `utility_damage`, `enemies_flashed`, `friendlies_flashed`, `flash_assists`, `headshot_kills`, `knife_kills`, `team_kills`, `suicides`, `bomb_plants`, `bomb_defuses`, `rounds_played`, `1k`-`5k`, `1v1`-`1v5` (clutches won), `first_kills_t`, `first_kills_ct`, `first_deaths_t`, `first_deaths_ct`, `trade_kills`, `kast` (rounds with a kill, assist, survival or trade), `score` and `mvp`. Fields beyond the engine's own scoreboard stats are filled for human players; bots report 0 for them. Rating and ADR are in the [stats JSON file](../guides/stats.md#advanced-stats).

## Match flow

| Event | When | Fields |
|---|---|---|
| `series_start` | A match config was loaded | `matchid`, `num_maps`, `team1{id,name}`, `team2{id,name}` |
| `map_vetoed` | A team bans a map | `matchid`, `team`, `map_name` |
| `map_picked` | A team picks a map | `matchid`, `team`, `map_name`, `map_number` |
| `side_picked` | A side is chosen for a picked map | `matchid`, `team`, `map_name`, `map_number`, `side` |
| `knife_won` :fontawesome-solid-code-fork: | The knife winner has chosen a side | `matchid`, `map_number`, `team`, `side` (`ct`/`t`), `swapped` |
| `going_live` | The map goes live | `matchid`, `map_number` |
| `round_start` :fontawesome-solid-code-fork: | Every round start once the match has started | `matchid`, `map_number`, `round_number` |
| `freezetime_end` :fontawesome-solid-code-fork: | Freeze time ends | `round_number`, `ct_alive`, `t_alive`, `players[]` (`name`, `steamid`, `team`, `hp`, `armor`, `has_helmet`, `has_defuser`, `money`) |
| `round_end` | A live round ends | `round_number`, `reason`, `winner{side,team}` (the team that won this round), `team1`, `team2` (scores and player stats) |
| `map_result` | The map ends | `map_number`, `winner`, `team1`, `team2`, `demo_filename` |
| `series_end` | The series ends | `winner`, `team1_series_score`, `team2_series_score`, `time_until_restore` |
| `match_cancelled` :fontawesome-solid-code-fork: | A loaded or running match is stopped, surrendered, restarted or cancelled for a no-show | `reason` (`ended_early`, `surrendered`, `restarted`, `no_show`), `demo_filename`, `team1`, `team2`, `team1_score`, `team2_score` |
| `match_paused` :fontawesome-solid-code-fork: | Any pause starts | `round_number`, `pause_type` (`pause`, `tech`, `admin`, `auto`), `team_name`, `max_duration` |
| `match_unpaused` :fontawesome-solid-code-fork: | Any pause ends | `round_number` |
| `demo_upload_ended` | A demo upload finished | `map_number`, `filename`, `success` |
| `player_disconnect` | A player leaves while a match is loaded | `player` (user id), `player_steamid`, `player_name`, `player_team`, `reason` |

## Live scorebot :fontawesome-solid-code-fork:

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

All live events also carry `matchid`, `map_number` and `round_number`.

`player_kill` and `player_death` both fire for a kill. Use `player_kill` for a kill feed or multi-kill tracking (`killer_round_kills` 2 = double kill, 5 = ace) and `player_death` when you also need suicides and deaths by the world.

## Examples

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
