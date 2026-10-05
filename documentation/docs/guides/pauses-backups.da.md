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

**Overtid**: taktiske timeouts (`.tac`) følger engine-indstillingerne i live.cfg: 4 pr. hold i ordinær tid (`mp_team_timeout_max`) og én mere, når overtiden starter (`mp_team_timeout_ot_add_once 1`, op til `mp_team_timeout_ot_max 5`). I hver overtidsperiode må hvert hold også bruge `.pause` `matchzy_overtime_pauses_per_team` gange (1 som standard). Admin-pauser er ikke begrænset.

!!! tip "Tilpas efter behov"
    Det er standardværdier. Begge kan tilpasses:

    - `matchzy_overtime_pauses_per_team` skrives i `config.cfg`.
    - Linjerne `mp_team_timeout_*` skrives i `cfg/MatchZy/live_override.cfg` (den køres lige efter `live.cfg`, når kampen går live, så de samme linjer i `config.cfg` ville blive overskrevet), eller pr. kamp i match configens `cvars`-blok.

    Opdateringer erstatter aldrig `config.cfg`, `live.cfg` eller en override-fil. Se [Opdatering](../getting-started/updating.md#keeping-your-own-changes).

Autopause erstatter spillets egen `sv_matchpause_auto_5v5` og ignorerer bot-hold. Hver pause og genoptagelse sendes som et `match_paused` / `match_unpaused` [event](../reference/events.md).

## Backups af runder { #round-backups }

MatchZy gemmer en backup ved starten af hver live-runde (Valves backup-system plus MatchZys egen tilstand). Backups ligger i `csgo/MatchZyDataBackup/` og kan også sendes med POST til `matchzy_remote_backup_url`.

| Situation | Kommando |
|---|---|
| Begge hold vil spille runden om | `.stop` (begge hold) |
| En admin spiller runden om | `.restorecurrent` / `!rr` |
| Gå én runde tilbage | `.restorelast` |
| Gå tilbage til runde N | `.restore <n>` |
| Se, hvad der findes | `.backups` |
| Serveren crashede midt i kampen | Connect igen; `.backups` viser de nyeste filer på disken med en færdig gendannelseskommando, og så `!loadbackup <fil>` |

### Efter en gendannelse { #after-a-restore }

- Kampen pauses (`matchzy_pause_after_restore`). Sæt `matchzy_restore_auto_unpause true` for at genoptage af sig selv efter `matchzy_restore_unpause_delay` sekunder.
- Scoreboardet rulles også tilbage: rundehistorik, kills, deaths, assists, skade, score og MVP'er (`matchzy_restore_scoreboard_stats`).
- En gendannelse meldes først ud, når serveren rent faktisk har loadet den; en fejlet load meldes i chatten.
