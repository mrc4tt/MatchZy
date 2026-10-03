# Stats and database

## Database

SQLite works out of the box (`matchzy.db` in the plugin folder). For MySQL / MariaDB edit `cfg/MatchZy/database.json`:

```json
{
  "DatabaseType": "MySQL",
  "MySqlHost": "127.0.0.1",
  "MySqlDatabase": "matchzy",
  "MySqlUsername": "matchzy",
  "MySqlPassword": "secret",
  "MySqlPort": 3306
}
```

Tables are created automatically:

| Table | Contents |
|---|---|
| `matchzy_stats_matches` | One row per match: start and end time, winner, series type (`BO3`), team names and series score, server address. |
| `matchzy_stats_maps` | One row per map: map name, start and end time, winner, score. |
| `matchzy_stats_players` | One row per player per map: kills, deaths, assists, damage, multi kills, utility and flash stats, 1v1 / 1v2, entries, headshots, money. Updated every round. |

A stopped match has an end time and an empty winner.

Set `matchzy_stats_include_bots true` to record bots as well (each bot gets a stable id from its name).

## CSV export

After every map: `csgo/MatchZy_Stats/<matchid>/match_data_map<N>_<matchid>.csv`, the player table for that map.

## Advanced stats

When the map ends, MatchZy also writes an HLTV-style scoreboard to `csgo/MatchZy_Stats/<matchid>/<demo name>_stats.json` (written when a demo was recorded). Per player:

| Stat | Meaning |
|---|---|
| `rating` | Rating 2.0 style (simplified): KAST, kills, deaths, impact and ADR. |
| `kast` | % of rounds with a kill, assist, survival or being traded. |
| `adr`, `hs_percent` | Damage per round, headshot percentage. |
| `opening_kills`, `opening_deaths` | First kill / first death of a round. |
| `trade_kills` | Kills on an enemy within 5 seconds of them killing a teammate. |
| `1v1`, `1v2` | Clutches as `wins/attempts`. |
| `5k`, `4k`, `3k`, `multi_kills` | Multi-kill rounds. |

Players are sorted by rating. The file also holds the map, date, round count, winner and team scores.

## Damage report

After each round players get a chat summary of damage given and taken (`matchzy_enable_damage_report`).
