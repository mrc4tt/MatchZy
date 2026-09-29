# Running a match

There are three ways to run a match, from quick pug to fully scripted tournament.

=== "Pug (no config)"

    1. Admin types `.match`.
    2. Players join any team and type `.ready`.
    3. When `matchzy_minimum_ready_required` players are ready, the knife round starts.
    4. Knife winners type `.stay` or `.switch`, and the match goes live.

    Teams are not locked; anyone can join either side.

=== "In-game wizard"

    Admin types `.matchsetup` (needs [CS2MenuManager](../getting-started/installation.md#requirements)).
    The wizard walks through series length (BO1, BO2, BO3, BO5), maps, veto and teams, then loads
    the match exactly like a match config. See [Admin menus](menus.md).

=== "Match config (panel / tournament)"

    Write a [match config JSON](../reference/match-config.md) and load it from the console, RCON or a panel:

    ```text
    matchzy_loadmatch cfg/MatchZy/match.json
    matchzy_loadmatch_url "https://example.com/match.json" "Authorization" "Bearer <token>"
    ```

    Teams are locked to their rosters, the veto runs if configured, and events go to your
    [remote log URL](events.md).

## Series and veto

- `num_maps` sets the series length. `maplist` is the map pool.
- With `"skip_veto": true` (default) the first `num_maps` maps are played in order.
- With `"skip_veto": false` teams `.ban` and `.pick` in chat. Set the order with `veto_mode`, or let MatchZy generate one.
- `map_sides` sets the starting side per map (`team1_ct`, `team2_t`, `knife`, ...).
- `clinch_series` ends a BO3 at 2-0.

Workshop maps work everywhere a map name does: `workshop/<id>`, `ws/<id>` or `ws:<name>` (hosted collection). :fontawesome-solid-code-fork:

## Locking the server to the match

| Setting | Effect |
|---|---|
| `matchzy_kick_when_no_match_loaded true` | Players not on the loaded roster are kicked on join. Everyone is kicked when no match is loaded. Admins are exempt. |
| `matchzy_whitelist_enabled_default true` | Only SteamIDs in `whitelist.cfg` may join. |

## Players vs bots :fontawesome-solid-code-fork:

One team can be `"players": "any"` (any human) and the other `"bots": true`. Bots are filled to `players_per_team`, stay on their side through halftime and overtime, always count as ready, and are recorded in stats. The map list must be fixed. Example in the [match config reference](../reference/match-config.md#players-vs-bots).

## After the match

- Stats are written to the database and a CSV. See [Stats](stats.md).
- The demo stops and is uploaded if configured. See [Demos](demos.md).
- With `matchzy_match_end_auto_changelevel` :fontawesome-solid-code-fork: the server changes to the next map by itself. Panel-loaded matches (`matchzy_loadmatch_url`) leave the map change to the panel.
- Cvars from the match config's `cvars` block are restored (`matchzy_reset_cvars_on_series_end`).

## Stopping a match

`.stopmatch` (or the Stop Match button in `.ma`) stops the match in any state, from setup to live, and resets to warmup. The database gets an end time and no winner, so stopped matches are easy to tell apart. :fontawesome-solid-code-fork:
