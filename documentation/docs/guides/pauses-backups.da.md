# Pauser og backups

## Typer af pauser { #pause-types }

| Type | Startes med | Slutter når |
|---|---|---|
| Normal | `.pause` | Begge hold skriver `.unpause`. |
| Taktisk | `.tac` (eller `.pause` med `matchzy_use_pause_command_for_tactical_pause`) | Timeouten løber ud. |
| Teknisk | `.tech` | Begge hold genoptager, eller `matchzy_tech_pause_duration` løber ud (så genoptages der af sig selv). |
| Admin | `.forcepause` | En admin skriver `.forceunpause`. |
| Auto | Et hold kommer under `matchzy_autopause_minplayers` | Holdet er fuldt igen; spillet genoptages efter `matchzy_autopause_resume_delay` sekunder. |

!!! note "Tekniske pauser"
    Hvert hold får `matchzy_max_tech_pauses_allowed` pr. bane. Med `matchzy_tech_pause_mode 1` (Get5-regler) slutter en teknisk pause aldrig af sig selv: når den har varet `matchzy_tech_pause_duration` sekunder i freeze time, kan hvert hold skrive `.unpause`, og holdet, der pausede, kan annullere den med `.unpause` før freeze time uden at bruge den op.

    Så længe en teknisk pause varer, ser alle spillere en boks midt på skærmen: holdet og spilleren, der kaldte den, tiden der er tilbage (eller hvem der kan unpause), de tekniske pauser der er tilbage, og hvilke hold der har skrevet `.unpause`. En almindelig `.pause` viser samme boks (uden tidsgrænse og pausetæller), og en taktisk timeout (`.tac`) viser holdet, tiden der er tilbage, og timeouts der er tilbage. `matchzy_pause_hud 1` viser i stedet en enkelt linje tekst, `0` slår den fra.

**Overtid**: taktiske timeouts (`.tac`) følger engine-indstillingerne i live.cfg: 4 pr. hold i ordinær tid (`mp_team_timeout_max`) og én mere, når overtiden starter (`mp_team_timeout_ot_add_once 1`, op til `mp_team_timeout_ot_max 5`). Wingman og rush (`live_wingman.cfg`, `live_rush.cfg`) giver 1 timeout på 60 sekunder pr. hold og 1 mere i hver overtid. I hver overtidsperiode må hvert hold også bruge `.pause` `matchzy_overtime_pauses_per_team` gange (1 som standard). Admin-pauser er ikke begrænset.

!!! tip "Tilpas efter behov"
    Det er standardværdier. Begge kan tilpasses:

    - `matchzy_overtime_pauses_per_team` skrives i `config.cfg`.
    - Linjerne `mp_team_timeout_*` skrives i `cfg/MatchZy/live_override.cfg` (den køres lige efter `live.cfg`, når kampen går live, så de samme linjer i `config.cfg` ville blive overskrevet), eller pr. kamp i match configens `cvars`-blok.

    Opdateringer erstatter aldrig `config.cfg`, `live.cfg` eller en override-fil. Se [Opdatering](../getting-started/updating.md#keeping-your-own-changes).

Autopause erstatter spillets egen `sv_matchpause_auto_5v5` og ignorerer bot-hold. Hver pause og genoptagelse sendes som et `match_paused` / `match_unpaused` [event](../reference/events.md).

## Backups af runder { #round-backups }

MatchZy gemmer en backup ved starten af hver live-runde (Valves backup-system plus MatchZys egen tilstand). Backups ligger i `csgo/MatchZyDataBackup/` og kan også sendes med POST til `matchzy_remote_backup_url`.

!!! warning "Startparameter påkrævet"
    CS2 skriver og indlæser kun round-backups, når serveren er startet med `-allow_round_backup_files`. Uden den skriver konsollen "Round backup files are disabled", og alle restores fejler. Når parameteren mangler, advarer MatchZy admins i chatten (og i konsollen), når en kamp indlæses, og når den går live, og afviser `.restore` med en besked i chatten.

| Situation | Kommando |
|---|---|
| Begge hold vil spille runden om | `.stop` (begge hold) |
| En admin spiller runden om | `.restorecurrent` / `!rr` |
| Gå én runde tilbage | `.restorelast` |
| Gå tilbage til runde N | `.restore <n>` |
| Se, hvad der findes | `.backups` |
| Vælg en runde fra en liste | `.ma` → Match Control → Restore Round (beder om bekræftelse) |
| Serveren crashede midt i kampen | Ingenting: kampen gendannes af sig selv (se [Gendannelse efter crash](#crash-recovery)). Manuelt: `.ma` → Restore Round, eller `.backups` og derefter `matchzy_loadbackup <fil>.json` |

### Efter en gendannelse { #after-a-restore }

- Kampen pauses (`matchzy_pause_after_restore`). Sæt `matchzy_restore_auto_unpause true` for at genoptage af sig selv efter `matchzy_restore_unpause_delay` sekunder.
- Scoreboardet rulles også tilbage: rundehistorik, kills, deaths, assists, skade, score og MVP'er (`matchzy_restore_scoreboard_stats`).
- En gendannelse meldes først ud, når serveren rent faktisk har loadet den; en fejlet load meldes i chatten.

## Gendannelse efter crash { #crash-recovery }

En loadet kamp gemmes i `csgo/MatchZyDataBackup/active_match.json`, indtil serien slutter eller kampen bliver annulleret. Når serveren crasher, genstarter eller MatchZy bliver genindlæst midt i en kamp:

1. **Automatisk gendannelse** (`matchzy_auto_recover`, slået til som standard): 10 sekunder efter at serveren er klar, uden en loadet kamp, gendanner MatchZy kampens nyeste rundebackup. Kampens config, hold, match-id og score kommer tilbage, og runden spilles, når begge hold er klar. En kamp, der blev afbrudt før første rundebackup, loades igen fra sin match-config. Kampe, der sidst var aktive for mere end `matchzy_auto_recover_max_age` minutter siden (standard 180), bliver ikke rørt. Et panel, der loader en kamp inden for de 10 sekunder, vinder. Panelet får besked med eventet `match_recovered`.
2. **Spillerne bliver ikke låst ude**: med `matchzy_kick_when_no_match_loaded` bliver en spiller på den afbrudte kamps holdliste lukket ind, mens ingen kamp er loadet, og admins får besked i chatten.
3. **Valves rå restore går gennem MatchZy**: `mp_backup_restore_load_file <fil>.txt` henter kun scoren tilbage, ikke holdlister, holdnavne eller pausestatus. Med `matchzy_raw_backup_restore redirect` (standard) køres den som MatchZys restore af `<fil>.json` (et `.json`-navn virker på samme måde). Findes kun `.txt`-filen, genopbygger MatchZy `.json`-filen ud fra en anden backup af samme kamp (eller den loadede eller afbrudte kamp) og regner holdenes side ud for runden; admins bliver bedt om at tjekke siderne. `block` afviser den, `allow` kører den uændret.
4. `.restore`, `.restorelast` og `.restorecurrent` uden en live kamp viser de nyeste backups på disken og kommandoen til at loade en.

### Runbook for turneringsadmins { #runbook }

| Problem | Gør dette |
|---|---|
| Spillere bliver kicket, når de connecter | Læs kick-grunden i serverloggen (`[ConnectGate]`, `[Disconnect]`) eller panelets `player_kicked`-event. `no_match_loaded`: kampen er ikke loadet, load den igen (eller `matchzy_loadbackup`). `not_in_match`: SteamID'et mangler på holdlisten. |
| Serveren crashede eller genstartede | Vent 10 sekunder på den automatiske gendannelse, og derefter `.ready` fra begge hold. Uden den: `.recover` (eller `matchzy_recover` over RCON). |
| Forkert score, eller en runde skal spilles om | `.restore <runde>`. Aldrig `mp_backup_restore_load_file`. |
| En runde slutter ikke (alle døde, timer på 0:00) | MatchZy afslutter den efter et par sekunder (`matchzy_round_watchdog`). |
| Tjek forbindelsen til panelet | `matchzy_test_event` |

