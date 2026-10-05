# Filer og mapper

`<cfg>` nedenfor er `csgo/cfg/MatchZy/` (eller `csgo/cfg/matchzy/`; MatchZy bruger den, der findes, og skriver sit valg i loggen ved opstart).

| Fil | Placering | Formål |
|---|---|---|
| `config.cfg` | `<cfg>` | Alle indstillinger. Laves første gang, og nye indstillinger tilføjes ved opdateringer. |
| `warmup.cfg`, `knife.cfg`, `live.cfg`, `live_wingman.cfg`, `scrim.cfg`, `hill.cfg`, `prac.cfg`, `dryrun.cfg`, `sleep.cfg` | `<cfg>` | Køres, når den tilhørende fase eller mode starter. Skrives, hvis de mangler, og overskrives aldrig. |
| `<mode>_override.cfg` (fx `warmup_override.cfg`, `live_override.cfg`) | `<cfg>` | Valgfri og oprettes af dig. Køres lige efter den tilhørende mode-fil, så dens værdier vinder. Se [Serverens egne indstillinger](#serverens-egne-indstillinger). |
| `defaults/*.cfg`, `defaults/database.json.example`, `defaults/README.txt` | `<cfg>` | Referencekopier af de nuværende standarder. Skrives forfra ved hver opstart og køres aldrig. Ret ikke i dem. |
| `matchzymaps.cfg` | `<cfg>` | Baneliste til automatiske baneskift og `.matchsetup`-guiden. Én bane pr. linje, `#` til kommentarer, `workshop/<id>` til workshop-baner. |
| `database.json` | `<cfg>` | Database. Se nedenfor. |
| `admins.json` | `<cfg>` | Ekstra admins efter SteamID64. Se nedenfor. |
| `whitelist.cfg` | `<cfg>` | Én SteamID64 pr. linje. Bruges, når whitelisten er slået til. |
| `savednades.json` | `<cfg>` | Gemte practice-lineups. |
| `grenadelibrary.json` | `<cfg>` | Fælles lineup-pakke (`.libadd`). |
| `botpositions.json` | `<cfg>` | Navngivne bot-positioner til practice (`.savebotpos`). |
| `spawns/coach/<bane>.json` | plugin-mappen | Håndplacerede udsigtspladser til coaches. Følger med til ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train og vertigo. |
| `matchzy.json` | `addons/counterstrikesharp/gamedata/` | Native signaturer. Se [Gamedata](../getting-started/gamedata.md). |
| `lang/*.json` | plugin-mappen | Oversættelser: engelsk, dansk, albansk. |
| `matchzy.db` | plugin-mappen | SQLite-database (standard). |
| `MatchZy_Stats/<matchid>/` | `csgo/` | CSV og avanceret statistik som JSON pr. bane. Se [Statistik](../guides/stats.md). |
| `MatchZyDataBackup/` | `csgo/` | Backups af runder. |
| Demoer | `csgo/demos/` | Styres af `matchzy_demo_path`. |

- **Mode-filer** (`warmup.cfg`, `live.cfg`, ...): skrives ud fra de indbyggede standarder, hvis de mangler.
- **`<mode>_override.cfg`**: indstillinger fra en loadet match config vinder stadig over både mode-filen og override-filen. MatchZy medbringer, opretter eller ændrer aldrig disse filer.
- **`defaults/`**: MatchZy skriver disse filer forfra ved hver opstart. Sammenlign dine egne filer med dem efter en opdatering.

## Serverens egne indstillinger

For at ændre nogle få indstillinger i en mode, fx ingen flashbangs i warmup til en turnering, skal du kun skrive de linjer i `<mode>_override.cfg` ved siden af mode-filen:

```cfg
// csgo/cfg/MatchZy/warmup_override.cfg
mp_respawn_immunitytime 5
ammo_grenade_limit_flashbang 0
```

Sæt ikke `mp_weapons_allow_typecount 0`: `0` blokerer alle køb (`-1` = ingen grænse, `5` = standarden i live).

Filnavnet er mode-filens navn med `_override` tilføjet: `warmup`, `knife`, `live`, `live_wingman`, `scrim`, `hill`, `prac`, `dryrun` eller `sleep`. MatchZy skriver aldrig disse filer, så de overlever alle opdateringer.

- **Kun de linjer, du ændrer, skal i override-filen.** Lad mode-filen (`warmup.cfg`) være identisk med standarden, eller slet den for at få den nuværende standard tilbage ved næste genstart. Kopier ikke hele mode-filen ind i override-filen: så låses alle værdier, og du går glip af nye standarder i senere releases.
- **Rækkefølge:** MatchZys indbyggede standard for moden køres først, derefter din mode-fil, så override-filen og til sidst `cvars` fra en loadet match config. Hvert trin vinder over det forrige. Den indbyggede standard udfylder kun indstillinger, din mode-fil ikke sætter, fx en nyere indstilling, der mangler i en ældre `live.cfg`. Den skrives aldrig til dine filer, og den rører ikke indstillinger, der hører til hele serveren (`sv_hibernate_when_empty`, `tv_relayvoice`, `sv_lan`, `sv_pure`, `sv_steamgroup_exclusive`, `sv_kick_ban_duration`, `sv_competitive_minspec`, `mp_logdetail`): sæt dem i din server.cfg. Practice mode (`prac.cfg`) har ikke det indbyggede trin.
- **Overførsel mellem modes:** en værdi sat i en override-fil bliver, indtil noget sætter den igen. De indbyggede standarder nulstiller alle indstillinger, som MatchZys egen fil til den næste mode sætter, også selvom din kopi af den fil ikke gør. En cvar, som den næste modes standard heller ikke sætter, følger stadig med over. Tjek med `grep <cvar> defaults/*.cfg`, og sæt den tilbage i den næste modes override-fil om nødvendigt. Sæt fx ikke `mp_ignore_round_win_conditions 1` i `warmup_override.cfg`: `knife.cfg` nulstiller den ikke, så knife-runden ville aldrig slutte.

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

`DatabaseType` er `SQLite` eller `MySQL`. Alt andet falder tilbage til SQLite. Tabellerne oprettes automatisk.

## admins.json

```json
{
  "76561198000000001": "",
  "76561198000000002": ""
}
```

Nøglerne skal være SteamID64'er. Alle på listen er fulde MatchZy-admins. Værdien er fri tekst (fx spillerens navn) og bruges ikke. Til admins med begrænsede rettigheder skal du bruge CounterStrikeSharps eget admin-system med flag. Spillere er også admins, når de har `@css/root` eller den rettighed, en kommando tjekker i CounterStrikeSharps admin-system, eller når `matchzy_everyone_is_admin` er slået til. `.mhelp` viser hver admin præcis, hvilke kommandoer de kan bruge.
