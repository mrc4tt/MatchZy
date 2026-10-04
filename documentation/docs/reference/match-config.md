# Match config JSON

A match is described by a JSON file (Get5 style). Load it from the server console, RCON or a panel:

```text
matchzy_loadmatch cfg/MatchZy/match.json                       # path relative to csgo/
matchzy_loadmatch_url "https://example.com/match.json"          # HTTP GET
matchzy_loadmatch_url "https://example.com/match.json" "Authorization" "Bearer <token>"
```

`get5_loadmatch_url` is an alias of `matchzy_loadmatch_url`. A match loaded by URL is treated as a panel match (no automatic map change at series end). If the first map is not the current map, MatchZy changes map and loads the match again on the new map.

Prefer an in-game flow? Admins can build the same config with the [`.matchsetup` wizard](../guides/menus.md).

## Full example

```json
{
  "matchid": 1042,
  "num_maps": 3,
  "maplist": ["de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_dust2", "workshop/3070284539"],
  "skip_veto": false,
  "veto_mode": ["team1_ban", "team2_ban", "team1_pick", "team2_pick", "team1_ban", "team2_ban"],
  "map_sides": ["knife", "team1_ct", "knife"],
  "players_per_team": 5,
  "min_players_to_ready": 10,
  "clinch_series": true,
  "team1": {
    "id": "t1",
    "name": "Alpha",
    "players": {
      "76561198000000001": "alpha1",
      "76561198000000002": "alpha2",
      "76561198000000003": "alpha3",
      "76561198000000004": "alpha4",
      "76561198000000005": "alpha5"
    },
    "coaches": { "76561198000000009": "alphacoach" }
  },
  "team2": {
    "id": "t2",
    "name": "Bravo",
    "players": {
      "76561198000000011": "bravo1",
      "76561198000000012": "bravo2",
      "76561198000000013": "bravo3",
      "76561198000000014": "bravo4",
      "76561198000000015": "bravo5"
    },
    "coaches": ["76561198000000019"]
  },
  "spectators": {
    "players": { "76561198000000099": "caster" }
  },
  "cvars": {
    "mp_overtime_enable": "1"
  }
}
```

## Top-level fields

| Field | Type | Default | Description |
|---|---|---|---|
| `matchid` | integer | auto | Match id. If omitted (or `0`), a new id is taken from the database. |
| `num_maps` | integer | **required** | Maps in the series (BO1, BO3, ...). Must not exceed the length of `maplist`. |
| `maplist` | string[] | **required** | Map pool. See [map formats](#map-formats). |
| `team1`, `team2` | object | **required** | See [team fields](#team-fields). |
| `skip_veto` | bool | `false` | Play the first `num_maps` maps in order instead of a veto. Forced on when `maplist` has exactly `num_maps` maps. |
| `side_type` | string | `standard` | `standard` (knife unless `map_sides` says otherwise; side pick in the veto), `always_knife`, `never_knife` (team1 starts CT), `random`. |
| `veto_first` | string | - | `team1`, `team2` or `random`: who starts a generated veto. |
| `veto_mode` | string[] | generated | Veto order using `team1_ban`, `team2_ban`, `team1_pick`, `team2_pick`. Generated if omitted. |
| `map_sides` | string[] | `knife` | Per map: `team1_ct`, `team1_t`, `team2_ct`, `team2_t` or `knife`. A fixed side means no knife round on that map. |
| `players_per_team` | integer | `5` | Players per side at a time. Extra roster players (substitutes) wait on Spectator while their side is full. Also the number of bots on a bot team. |
| `min_players_to_ready` | integer | server setting | Ready players needed to start. |
| `min_spectators_to_ready` | integer | `0` | Ready spectators needed. |
| `spectators` | object | `{}` | `{ "players": { "<steamid64>": "<name>" } }`. |
| `clinch_series` | bool | `true` | End the series once a team has won the majority of maps. |
| `wingman` | bool | `false` | Wingman game mode, uses `live_wingman.cfg`. |
| `cvars` | object | - | Cvars applied when the match loads and before each map goes live, e.g. `{ "mp_overtime_enable": "1" }`. See below. |

!!! note "`cvars`"
    Only existing convars and MatchZy/Get5 settings are accepted, with plain values (no quotes or `;`). Action commands (`matchzy_loadmatch_url`, `matchzy_loadbackup`, `get5_endmatch`, ...), `rcon_password` and `matchzy_everyone_is_admin` are ignored and logged. The values are restored at series end when `matchzy_reset_cvars_on_series_end` is on.

## Team fields

| Field | Type | Default | Description |
|---|---|---|---|
| `name` | string | **required** | Team name, shown on the scoreboard. |
| `tag` | string | `""` | Short team tag, shown as the players' clan tag while live when `matchzy_team_clantag_enabled` is on. Letters, digits, space, `_` and `-`. |
| `id` | string | `""` | Your own id for the team, echoed in events. |
| `players` | object, array or `"any"` | **required** | Roster as `{ "<steamid64>": "<name>" }` or `["<steamid64>", ...]`, or `"any"` (see below). |
| `coaches` | object or string[] | - | SteamID64s that coach this team. They become coach automatically. See [Coaching](../guides/coaching.md). |
| `bots` | bool | `false` | The team is played by bots, filled to `players_per_team`. |
| `bot_difficulty` | integer 0-3 | `2` | Bot difficulty for a bot team. |

- **`players`**: `"any"` opens the team to every human who is not on the other roster. Only one team can be `"any"`.
- **`coaches`**: listed coaches may join and are not kicked by `matchzy_kick_when_no_match_loaded`. With a list, only those SteamIDs can `.coach` this team.

## Players vs bots

```json
{
  "matchid": 0,
  "num_maps": 1,
  "maplist": ["de_dust2"],
  "players_per_team": 5,
  "team1": { "name": "Players", "players": "any" },
  "team2": { "name": "Bots", "bots": true, "bot_difficulty": 2 }
}
```

- Bots cannot veto, knife or type commands, so a bot match needs a fixed map list (exactly `num_maps` maps, or `"skip_veto": true`). Otherwise the load is refused.
- A `knife` side is drawn at random instead of played.
- The bot side always counts as ready and agrees to unpause, and autopause ignores it.
- Bots stay on their side through halftime, overtime and round restores. Humans cannot join the bot team.
- Bot stats are recorded automatically. `.warmupbots` is disabled in these matches.

## Map formats

| Entry | Map change |
|---|---|
| `de_mirage` | `changelevel de_mirage` |
| `3070284539` | Workshop map by id (`host_workshop_map`) |
| `workshop/3070284539` or `workshop/3070284539/cs_alpine` | Workshop map by id |
| `ws/3070284539` | Workshop map by id |
| `ws:cs_alpine` | Workshop map by name from the server's hosted collection (`ds_workshop_changelevel`) |

## Roster commands

Run from the server console or RCON while a match is loaded:

| Command | Description |
|---|---|
| `matchzy_addplayer <steamid64> <team1\|team2\|spec> "<name>"` | Add a player to a roster. Alias `get5_addplayer`. |
| `matchzy_removeplayer <steamid64>` | Remove a player. Alias `get5_removeplayer`. |
