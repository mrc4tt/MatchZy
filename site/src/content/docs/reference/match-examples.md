---
title: "Match config examples"
sidebar:
  order: 6
---

Ready-made match configs for common series formats. Every field is described in [Match config JSON](/reference/match-config/). Replace the SteamID64s, team names and maps with your own.

A whole series is one match config: MatchZy plays map 1, changes to map 2 by itself after 15 seconds, and keeps the series score, the rosters and the match id. Each map gets its own demo, its own `map_result` event and its own rows in the stats database (`matchid` + `mapnumber`).

## BO1 with in-game veto

Seven maps, the teams ban in turn until one is left. The decider is played with a knife round.

```json
{
  "matchid": 2001,
  "num_maps": 1,
  "maplist": ["de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_dust2", "de_train"],
  "veto_first": "random",
  "side_type": "standard",
  "players_per_team": 5,
  "team1": {
    "name": "Alpha",
    "tag": "ALP",
    "players": {
      "76561198000000001": "alpha1",
      "76561198000000002": "alpha2",
      "76561198000000003": "alpha3",
      "76561198000000004": "alpha4",
      "76561198000000005": "alpha5"
    }
  },
  "team2": {
    "name": "Bravo",
    "tag": "BRV",
    "players": {
      "76561198000000011": "bravo1",
      "76561198000000012": "bravo2",
      "76561198000000013": "bravo3",
      "76561198000000014": "bravo4",
      "76561198000000015": "bravo5"
    }
  }
}
```

## BO2 with the veto done on a website

The website has already run the veto, so the config only carries the result: the two maps in order and who starts on which side. `skip_veto` plays the first `num_maps` maps of `maplist`. `map_sides` takes `team1_ct`, `team1_t`, `team2_ct`, `team2_t` or `knife`, one entry per map.

```json
{
  "matchid": 2002,
  "num_maps": 2,
  "maplist": ["de_nuke", "de_ancient"],
  "skip_veto": true,
  "map_sides": ["team2_ct", "team1_t"],
  "players_per_team": 5,
  "team1": {
    "id": "league-team-17",
    "name": "Alpha",
    "players": {
      "76561198000000001": "alpha1",
      "76561198000000002": "alpha2",
      "76561198000000003": "alpha3",
      "76561198000000004": "alpha4",
      "76561198000000005": "alpha5"
    },
    "coaches": ["76561198000000009"]
  },
  "team2": {
    "id": "league-team-42",
    "name": "Bravo",
    "players": {
      "76561198000000011": "bravo1",
      "76561198000000012": "bravo2",
      "76561198000000013": "bravo3",
      "76561198000000014": "bravo4",
      "76561198000000015": "bravo5"
    }
  }
}
```

- A BO2 can end 1-1. The series then ends as a draw: `series_end` has no winner.
- The `id` of each team is echoed in the events, so a website can match the result to its own teams.

## BO2 with in-game veto

Each team bans two maps, then each team picks one. After a pick the other team chooses its side in-game with `.ct` or `.t`.

```json
{
  "matchid": 2003,
  "num_maps": 2,
  "maplist": ["de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_dust2", "de_train"],
  "veto_first": "team1",
  "veto_mode": ["team1_ban", "team2_ban", "team1_ban", "team2_ban", "team1_pick", "team2_pick"],
  "side_type": "standard",
  "players_per_team": 5,
  "team1": { "name": "Alpha", "players": { "76561198000000001": "alpha1" } },
  "team2": { "name": "Bravo", "players": { "76561198000000011": "bravo1" } }
}
```

The rosters are shortened here; list all five players per team as in the examples above.

## BO3 with in-game veto

Ban, ban, pick, pick, ban, ban; the last map is the decider with a knife round. `clinch_series` ends the series at 2-0.

```json
{
  "matchid": 2004,
  "num_maps": 3,
  "maplist": ["de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_dust2", "de_train"],
  "veto_first": "random",
  "veto_mode": ["team1_ban", "team2_ban", "team1_pick", "team2_pick", "team1_ban", "team2_ban"],
  "side_type": "standard",
  "clinch_series": true,
  "players_per_team": 5,
  "team1": { "name": "Alpha", "players": { "76561198000000001": "alpha1" } },
  "team2": { "name": "Bravo", "players": { "76561198000000011": "bravo1" } },
  "spectators": { "players": { "76561198000000099": "caster" } },
  "cvars": {
    "mp_overtime_enable": "1"
  }
}
```

## BO5 with fixed maps

A final with the maps decided beforehand. Sides are decided by a knife round on every map.

```json
{
  "matchid": 2005,
  "num_maps": 5,
  "maplist": ["de_mirage", "de_nuke", "de_ancient", "de_inferno", "de_anubis"],
  "skip_veto": true,
  "side_type": "always_knife",
  "clinch_series": true,
  "players_per_team": 5,
  "team1": { "name": "Alpha", "players": { "76561198000000001": "alpha1" } },
  "team2": { "name": "Bravo", "players": { "76561198000000011": "bravo1" } }
}
```

## Test a BO2 alone against bots

Plays a full BO2 against a bot team so you can check the map change, the demos and the scores without ten players. `mp_maxrounds 4` keeps each map short; it is put back when the series ends.

```json
{
  "matchid": 0,
  "num_maps": 2,
  "maplist": ["de_dust2", "de_mirage"],
  "skip_veto": true,
  "map_sides": ["team1_ct", "team1_t"],
  "players_per_team": 5,
  "min_players_to_ready": 1,
  "team1": { "name": "Testers", "players": "any" },
  "team2": { "name": "Bots", "bots": true, "bot_difficulty": 1 },
  "cvars": {
    "mp_maxrounds": "4",
    "mp_overtime_enable": "0"
  }
}
```

Save it as `csgo/cfg/MatchZy/test_bo2.json` and run `matchzy_loadmatch cfg/MatchZy/test_bo2.json`. After both maps, check:

- two `.dem` files, one per map (the default name contains the map and the time),
- two rows for the match in `matchzy_stats_maps`, with map 1 and map 2's own scores,
- a `map_result` event after each map and one `series_end` at the end, if `matchzy_remote_log_url` is set.

## G5API / G5V

G5API sends its own match config and sets the remote log, demo upload and backup URLs itself, so no extra setup is needed in MatchZy. It loads the match with:

```text
get5_loadmatch_url "https://g5api.example.com/v1/matches/123/config" "Authorization" "Bearer <api key>"
```

- The same series formats work (BO1 to BO5, veto or fixed maps). MatchZy changes the map between the maps of the series itself.
- When the series ends, MatchZy leaves the server on the last map so G5API decides what happens next (`matchzy_match_end_auto_changelevel` is ignored for these matches).
- The events (`map_result`, `series_end`, ...) are sent in Get5's format (`matchzy_events_format get5`, the default), which G5API reads.

## Your own website

To feed MatchZy from your own site, serve one of the configs above from a URL and load it with:

```text
matchzy_loadmatch_url "https://example.com/api/match/2002.json" "Authorization" "Bearer <token>"
```

Set `matchzy_remote_log_url` (in `config.cfg` or through the config's `cvars`) to receive the live [events](/reference/events/) back: `map_result` after each map with its score, and `series_end` with the series score.

:::note[Time between maps]
The next map loads 15 seconds after a map ends (`mp_match_restart_delay` is 25 in the shipped cfgs). A lower `mp_match_restart_delay` in your `cvars` makes the map change come earlier, 3 seconds before the game's own restart.
:::
