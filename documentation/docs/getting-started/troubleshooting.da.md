# Fejlfinding

De mest almindelige problemer, hvad der giver dem, og hvordan du løser dem. De fleste svar starter i serverens konsol eller log: MatchZy skriver alle advarsler der med `[MatchZy]` foran.

!!! tip "Tjek først"
    Skriv `.version` i chatten (eller `css_matchzy_version` i konsollen). Svarer intet, er pluginet ikke loadet: start med første afsnit nedenfor.

## Installation og loading { #install-and-loading }

### MatchZy loader ikke { #matchzy-does-not-load }

- Kør `css_plugins list` i konsollen. MatchZy skal stå som loadet.
- `MissingMethodException`, `TypeLoadException` eller `EntryPointNotFoundException` i loggen: CounterStrikeSharp er for gammel eller passer ikke. Opdater CounterStrikeSharp til den nyeste release, og genstart.
- Tjek, at filerne ligger i `game/csgo/addons/counterstrikesharp/plugins/MatchZy/` (ikke én mappe dybere, som sker, når en zip pakkes ud i sin egen mappe).

### `.ma`, `.matchsetup`, `.nades` eller `.strats` svarer kun med en besked { #menus-only-reply-with-a-message }

"In-game menus require the CS2MenuManager plugin, which is not installed on this server." Menuerne kræver det valgfri plugin [CS2MenuManager](installation.md#requirements) (1.0.42+). Alt andet virker uden.

### Mine config-ændringer slår ikke igennem { #my-config-changes-are-not-applied }

- En mode-cfg (`live.cfg`, `prac.cfg`, ...) kører efter `config.cfg` og vinder for samme indstilling. Læg spilindstillinger for en mode i `<mode>_override.cfg`, fx `live_override.cfg`. Se [Behold dine egne ændringer](updating.md#keeping-your-own-changes).
- Der findes både en `cfg/MatchZy/`- og en `cfg/matchzy/`-mappe. MatchZy bruger kun den ene (den skrives ved opstart); slet den anden.
- Efter en opdatering bliver dine cfg-filer aldrig erstattet. De nuværende standarder ligger i `cfg/MatchZy/defaults/`, så du kan sammenligne. Slet en fil for at få den nye standard skrevet.

## Admins og kommandoer { #admins-and-commands }

### Jeg bliver ikke genkendt som admin { #i-am-not-recognized-as-admin }

- Skriv dit SteamID64 i `cfg/MatchZy/admins.json`, eller giv dig selv et CounterStrikeSharp-admin-flag (`@css/config` til kampstyring, `@css/root` til alt). Se [admins.json](../reference/files.md#adminsjson).
- `.mhelp` viser de admin-kommandoer, du kan køre, og hvilken rettighed du mangler til resten.

### `.map` gør ingenting { #map-does-nothing }

`.map` træder til side, når CS2-SimpleAdmin eller CS2MapChange er installeret (brug deres map-kommando), eller når `matchzy_map_console_command_enabled` er slået fra. Den virker også kun, før en kamp er startet.

### En kommando siger, at den ikke er tilgængelig nu { #a-command-says-it-is-not-available-now }

De fleste kommandoer virker kun i én fase: warmup, live, practice og så videre. `.help` viser kommandoerne for den fase, serveren er i. Se [Hvor kommandoerne virker](../reference/commands.md#where-commands-work).

## Kampe { #matches }

### Kampen starter ikke, når alle er ready { #the-match-does-not-start-when-everyone-is-ready }

- Ready-antallet er `matchzy_minimum_ready_required` (eller `.readyrequired <n>` for denne kamp). Med en match config kommer holdstørrelsen fra `players_per_team`. Se [Ready-systemet](../guides/ready-system.md).
- I 1v1-, wingman- og rush-pugs følger antallet spiltypen (2, 4 eller 6), medmindre en admin har sat en lavere værdi.

### Spillere bliver kicket, når de joiner { #players-are-kicked-when-they-join }

`matchzy_kick_when_no_match_loaded` er slået til: spillere, der ikke står på den loadede kamps hold, bliver kicket, og alle bliver kicket, når ingen kamp er loadet. Admins er undtaget. Slå den fra i `config.cfg` på en offentlig server eller en pug-server.

## Round-backups { #round-backups }

### `.restore` fejler, eller der er ingen backups { #restore-fails-or-there-are-no-backups }

- Serveren skal startes med `-allow_round_backup_files`. Uden den skriver konsollen "Round backup files are disabled", og MatchZy afviser `.restore` med en besked i chatten. Se [Startparametre](installation.md#launch-options).
- "Backup ... contains no usable round data, nothing was restored": backup-JSON'en findes, men CS2 skrev ingen rundefil til den, typisk fordi kampen blev spillet uden `-allow_round_backup_files` (eller på MatchZy 1.0.9 eller ældre, se nedenfor). De backups kan ikke gendannes; tilføj parameteren og genstart serveren, så tages der backup af nye runder igen.
- "Rejecting round backup file path" i konsollen: CS2 (siden opdateringen 15. oktober 2026) afviser absolutte backup-stier. Opdater MatchZy til 1.0.10 eller nyere.
- Backup-filerne ligger i `csgo/` eller `csgo/addons/metamod/`, MatchZys egen tilstand i `csgo/MatchZyDataBackup/`. Se [Backups af runder](../guides/pauses-backups.md#round-backups).

## Demoer { #demos }

### Der blev ikke optaget en demo { #no-demo-was-recorded }

- GOTV skal være slået til: `tv_enable 1` i startparametrene eller en cfg.
- Demoer optages kun på live-baner (match, scrim, hill), ikke i warmup eller practice.
- Se efter demo-linjerne i serverloggen: hver start, hvert nyt forsøg og hvert stop logges. En cfg, der kicker CSTV-botten (fx `bot_quota 0` i en mode-cfg), stopper optagelsen; MatchZy 1.0.9 og nyere henter CSTV-botten tilbage, når det sker, hvis serveren kører med `+tv_enable_dynamic 1`. Uden den skriver loggen "tv_enable_dynamic is 0 - GOTV comes back only after a map change". Se [Demoer](../guides/demos.md).

## Practice { #practice }

### `.last` / `.back` / `.ln` giver dig ikke granaten i hånden, eller `.t` / `.ct` fra spectator er "currently broken" { #grenade-not-in-hand-or-spectator-switch-broken }

Gamedataen passer ikke længere til CS2-buildet, typisk lige efter en CS2-opdatering. Loggen viser linjer som `CCSPlayer_WeaponServices_SelectItem offset missing`. Opdater MatchZy, når en ny release er ude; den har en opdateret `gamedata/matchzy.json` med. Se [Gamedata](gamedata.md).

## Live events { #live-events }

### Mit panel eller min bot modtager ingen events { #my-panel-or-bot-receives-no-events }

- `matchzy_remote_log_url` skal være sat i `config.cfg`.
- Kamp-events sendes kun, mens en kamp er loadet eller live. `server_ready`, `map_change` og `server_shutdown` sendes altid.
- Serverloggen viser hvert resultat-event med kamp-id og den HTTP-status, dit endpoint svarede. Se [Live events API](../guides/events.md).

## Sidder du stadig fast? { #still-stuck }

Opret et [issue](https://github.com/mrc4tt/MatchZy/issues/new) med outputtet af `css_matchzy_version`, din CounterStrikeSharp-version og de relevante konsollinjer (fjern tokens og adgangskoder).
