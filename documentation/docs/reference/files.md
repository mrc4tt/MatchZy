# Files and folders

`<cfg>` below is `csgo/cfg/MatchZy/` (or `csgo/cfg/matchzy/`; MatchZy uses whichever exists and logs its choice at startup).

| File | Location | Purpose |
|---|---|---|
| `config.cfg` | `<cfg>` | All settings. Generated on first load, new settings appended on updates. |
| `warmup.cfg`, `knife.cfg`, `live.cfg`, `live_wingman.cfg`, `scrim.cfg`, `hill.cfg`, `prac.cfg`, `dryrun.cfg`, `sleep.cfg` | `<cfg>` | Executed when the matching phase or mode starts. Regenerated from built-in templates if missing. |
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

Keys must be SteamID64s. Players are also admins when they hold `@css/root` or the permission a command checks in CounterStrikeSharp's own admin system, or when `matchzy_everyone_is_admin` is on. `.mhelp` shows each admin exactly which commands they can run.
