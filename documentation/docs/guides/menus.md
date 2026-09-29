# Admin menus :fontawesome-solid-code-fork:

The menus need the optional [CS2MenuManager](https://git.miksen.me/mikkel/CS2MenuManager/releases) plugin (1.0.42+). Without it, MatchZy runs normally and the menu commands reply with a notice.

Navigate with ++w++ / ++s++, select with ++e++, go back with the back entry.

## `.ma` admin menu

Permission: `@css/config`.

| Section | Entries |
|---|---|
| New Match Setup | Opens the setup wizard below. |
| Match Control | Force start, toggle knife, restart round, restart match, force end, stop match. |
| Pause / Unpause | Pause, unpause, force pause, force unpause, tactical, tech. |
| Modes | Warmup, match, practice, exit practice, dryrun, exit dryrun. |

## `.matchsetup` wizard

Permission: `@css/config`. Builds a match config in game and loads it, the same as `matchzy_loadmatch`.

1. **Series**: BO1, BO2, BO3 or BO5.
2. **Maps**: pre-pick the maps (no veto) from `matchzymaps.cfg`, or veto from the full pool.
3. **Sides**: knife round decides, or skip knife with team 1 starting CT.
4. **Confirm**: review everything, set team names with `.team1 <name>` / `.team2 <name>` (the menu refreshes), then **START MATCH**.

The match then goes to veto or warmup like any loaded config. Rosters are not locked (anyone can join either team).

## `.nades` grenade library

Any player in practice mode. Browse lineups for the current map by type (All, Smoke, Flash, HE, Molotov, Decoy) and select one to load it.
