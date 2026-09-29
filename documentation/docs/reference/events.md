# Events

Every event is sent as an HTTP `POST` with a JSON body to `matchzy_remote_log_url`. The `event` field names the event. See the [Live events guide](../guides/events.md) for setup.

:fontawesome-solid-code-fork: = not sent by upstream MatchZy.

## Common fields

- `event`: event name.
- `matchid`: on match events.
- `map_number`: 0-based map index in the series.
- `round_number`: on round and live events.
- Veto events use `team` = `"team1"` / `"team2"`. Live events use sides `"CT"` / `"T"`.

Player stats objects (`team1.players[].stats` on `round_end` and `map_result`) carry kills, deaths, assists, damage, utility damage, enemies flashed, headshot kills, rounds played, multi kills (2k-5k), 1v1 / 1v2 wins, score and MVPs. Advanced stats (KAST, rating, opening duels, trades) are written to the [stats JSON file](../guides/stats.md#advanced-stats), not to events.

## Match flow

| Event | When | Fields |
|---|---|---|
| `series_start` | A match config was loaded | `matchid`, `num_maps`, `team1{id,name}`, `team2{id,name}` |
| `map_vetoed` | A team bans a map | `matchid`, `team`, `map_name` |
| `map_picked` | A team picks a map | `matchid`, `team`, `map_name`, `map_number` |
| `side_picked` | A side is chosen for a picked map | `matchid`, `team`, `map_name`, `map_number`, `side` |
| `going_live` | The map goes live | `matchid`, `map_number` |
| `round_start` :fontawesome-solid-code-fork: | Every round start once the match has started | `matchid`, `map_number`, `round_number` |
| `freezetime_end` :fontawesome-solid-code-fork: | Freeze time ends | `round_number`, `ct_alive`, `t_alive`, `players[]` (`name`, `steamid`, `team`, `hp`, `armor`, `has_helmet`, `has_defuser`, `money`) |
| `round_end` | A live round ends | `round_number`, `reason`, `winner{side,team}`, `team1`, `team2` (scores and player stats) |
| `map_result` | The map ends | `map_number`, `winner`, `team1`, `team2`, `demo_filename` |
| `series_end` | The series ends | `winner`, `team1_series_score`, `team2_series_score`, `time_until_restore` |
| `match_cancelled` :fontawesome-solid-code-fork: | A running match is stopped, surrendered or restarted | `reason` (`ended_early`, `surrendered`, `restarted`), `demo_filename`, `team1`, `team2`, `team1_score`, `team2_score` |
| `match_paused` :fontawesome-solid-code-fork: | Any pause starts | `round_number`, `pause_type` (`pause`, `tech`, `admin`, `auto`), `team_name`, `max_duration` |
| `match_unpaused` :fontawesome-solid-code-fork: | Any pause ends | `round_number` |
| `demo_upload_ended` | A demo upload finished | `map_number`, `filename`, `success` |

## Live scorebot :fontawesome-solid-code-fork:

| Event | When | Fields |
|---|---|---|
| `player_death` | A player dies | `attacker_name`, `attacker_steamid`, `attacker_team`, `victim_name`, `victim_steamid`, `victim_team`, `assister_name`, `assister_steamid`, `weapon`, `headshot`, `penetrated`, `noscope`, `thrusmoke`, `attackerblind`, `is_suicide`, `ct_alive`, `t_alive` |
| `player_hurt` | A player takes damage (live) | `attacker_name`, `attacker_steamid`, `victim_name`, `victim_steamid`, `victim_team`, `hp_remaining`, `armor_remaining`, `damage_health`, `damage_armor`, `weapon`, `hitgroup` |
| `bomb_planted` | Bomb planted | `player_name`, `player_steamid`, `site` (`A`/`B`), `ct_alive`, `t_alive` |
| `bomb_defused` | Bomb defused | same as `bomb_planted` |
| `bomb_exploded` | Bomb exploded | `site`, `ct_alive`, `t_alive` |
| `bomb_pickup`, `bomb_dropped` | Bomb picked up / dropped | `player_name`, `player_steamid` |
| `grenade_thrown` | Grenade thrown | `player_name`, `player_steamid`, `player_team`, `grenade` (`smoke`, `flash`, `he`, `molotov`, `incendiary`, `decoy`) |
| `grenade_detonated` | Grenade detonated | `player_name`, `player_steamid`, `player_team`, `grenade`, `x`, `y`, `z` |
| `player_blinded` | A player is flashed | `attacker_*`, `victim_*`, `duration` (seconds), `team_flash` |

All live events also carry `matchid`, `map_number` and `round_number`.

## Example

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
