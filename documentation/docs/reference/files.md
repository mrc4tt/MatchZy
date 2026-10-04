# Files and folders

`<cfg>` below is `csgo/cfg/MatchZy/` (or `csgo/cfg/matchzy/`; MatchZy uses whichever exists and logs its choice at startup).

| File | Location | Purpose |
|---|---|---|
| `config.cfg` | `<cfg>` | All settings. Generated on first load, new settings appended on updates. |
| `warmup.cfg`, `knife.cfg`, `live.cfg`, `live_wingman.cfg`, `scrim.cfg`, `hill.cfg`, `prac.cfg`, `dryrun.cfg`, `sleep.cfg` | `<cfg>` | Executed when the matching phase or mode starts. Written when missing, never overwritten. |
| `<mode>_override.cfg` (e.g. `warmup_override.cfg`, `live_override.cfg`) | `<cfg>` | Optional, created by you. Executed right after the matching mode cfg, so its values win. See [Server-specific settings](#server-specific-settings). |
| `defaults/*.cfg`, `defaults/database.json.example`, `defaults/README.txt` | `<cfg>` | Reference copies of the current defaults, rewritten on every load and never executed. Do not edit them. |
| `matchzymaps.cfg` | `<cfg>` | Map list for automatic map changes and the `.matchsetup` wizard. One map per line, `#` for comments, `workshop/<id>` for workshop maps. |
| `database.json` | `<cfg>` | Database backend. See below. |
| `admins.json` | `<cfg>` | Extra admins by SteamID64. See below. |
| `whitelist.cfg` | `<cfg>` | One SteamID64 per line. Enforced when the whitelist is on. |
| `savednades.json` | `<cfg>` | Saved practice lineups. |
| `grenadelibrary.json` | `<cfg>` | Shared lineup pack (`.libadd`). |
| `botpositions.json` | `<cfg>` | Named practice bot positions (`.savebotpos`). |
| `spawns/coach/<map>.json` | plugin folder | Hand-tuned coach viewing spots. Shipped for ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train and vertigo. |
| `matchzy.json` | `addons/counterstrikesharp/gamedata/` | Native signatures. See [Gamedata](../getting-started/gamedata.md). |
| `lang/*.json` | plugin folder | Translations: English, Danish, Albanian. |
| `matchzy.db` | plugin folder | SQLite database (default backend). |
| `MatchZy_Stats/<matchid>/` | `csgo/` | CSV and advanced stats JSON per map. See [Stats](../guides/stats.md). |
| `MatchZyDataBackup/` | `csgo/` | Round backups. |
| Demos | `csgo/demos/` | Controlled by `matchzy_demo_path`. |

- **Mode cfgs** (`warmup.cfg`, `live.cfg`, ...): written from the built-in defaults when missing.
- **`<mode>_override.cfg`**: settings from a loaded match config still win over both the mode cfg and the override. MatchZy never ships, creates or changes these files.
- **`defaults/`**: MatchZy rewrites these files on every load. Compare your own files with them after an update.

## Server-specific settings

To change a few settings of a mode, for example no flashbangs in warmup for a tournament, put only those lines in `<mode>_override.cfg` next to the mode cfg:

```cfg
// csgo/cfg/MatchZy/warmup_override.cfg
mp_respawn_immunitytime 5
ammo_grenade_limit_flashbang 0
```

Do not set `mp_weapons_allow_typecount 0`: `0` blocks all purchases (`-1` = no limit, `5` = the live default).

The file name is the mode cfg's name with `_override` added: `warmup`, `knife`, `live`, `live_wingman`, `scrim`, `hill`, `prac`, `dryrun` or `sleep`. MatchZy never writes these files, so they survive every update.

- **Only the lines you change go in the override.** Leave the mode cfg (`warmup.cfg`) identical to the default, or delete it to get the current default back on the next restart. Do not copy the whole mode cfg into the override: that pins every value, and you would miss new defaults from later releases.
- **Order:** MatchZy's built-in default for the mode runs first, then your mode cfg, then its override, then the `cvars` of a loaded match config. Each step wins over the one before. The built-in default only fills settings your mode cfg leaves out, for example a newer setting missing from an older `live.cfg`. It is never written to your files, and it leaves server-level settings alone (`sv_hibernate_when_empty`, `tv_relayvoice`, `sv_lan`, `sv_pure`, `sv_steamgroup_exclusive`, `sv_kick_ban_duration`, `sv_competitive_minspec`, `mp_logdetail`): set those in your server.cfg. Practice mode (`prac.cfg`) has no built-in step.
- **Carry-over:** a value set in an override stays until something sets it again. The built-in defaults reset every setting MatchZy's own cfg for the next mode sets, even when your copy of that cfg does not. A cvar that the next mode's default does not set either still carries over. Check with `grep <cvar> defaults/*.cfg` and set it back in the next mode's override if needed. For example, do not put `mp_ignore_round_win_conditions 1` in `warmup_override.cfg`: `knife.cfg` does not reset it, so the knife round would never end.

## database.json

```json
{
  "DatabaseType": "SQLite",
  "MySqlHost": "your_mysql_host",
  "MySqlDatabase": "your_mysql_database",
  "MySqlUsername": "your_mysql_username",
  "MySqlPassword": "your_mysql_password",
  "MySqlPort": 3306
}
```

`DatabaseType` is `SQLite` or `MySQL`. Anything else falls back to SQLite. Tables are created automatically.

## admins.json

```json
{
  "76561198000000001": "",
  "76561198000000002": ""
}
```

Keys must be SteamID64s. Every listed player is a full MatchZy admin. The value is free text (for example the player's name) and is not used. For admins with limited rights, use CounterStrikeSharp's own admin system with flags. Players are also admins when they hold `@css/root` or the permission a command checks in CounterStrikeSharp's own admin system, or when `matchzy_everyone_is_admin` is on. `.mhelp` shows each admin exactly which commands they can run.
