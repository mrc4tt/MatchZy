# Pauses and backups

## Pause types

| Type | Started by | Ends when |
|---|---|---|
| Normal | `.pause` | Both teams type `.unpause`. |
| Tactical | `.tac` (or `.pause` with `matchzy_use_pause_command_for_tactical_pause`) | The timeout runs out. |
| Technical | `.tech` | Both teams unpause, or `matchzy_tech_pause_duration` runs out (then it unpauses on its own). Each team gets `matchzy_max_tech_pauses_allowed` per map. |
| Admin | `.forcepause` | An admin types `.forceunpause`. |
| Auto :fontawesome-solid-code-fork: | A team drops below `matchzy_autopause_minplayers` | The team is full again; the game resumes after `matchzy_autopause_resume_delay` seconds. |

Autopause replaces the game's own `sv_matchpause_auto_5v5` and ignores bot teams. Every pause and unpause is sent as a `match_paused` / `match_unpaused` [event](../reference/events.md). :fontawesome-solid-code-fork:

## Round backups

MatchZy saves a backup at the start of every live round (Valve's backup system plus MatchZy's own state). Backups live in `csgo/MatchZyDataBackup/` and can also be POSTed to `matchzy_remote_backup_url`.

| Situation | Command |
|---|---|
| Both teams want to replay this round | `.stop` (both teams) |
| Admin replays this round | `.restorecurrent` / `!rr` :fontawesome-solid-code-fork: |
| Go back one round | `.restorelast` :fontawesome-solid-code-fork: |
| Go back to round N | `.restore <n>` |
| See what is available | `.backups` :fontawesome-solid-code-fork: |
| Server crashed mid-match | Reconnect, `.backups` lists the newest files on disk with a ready-made restore command, then `!loadbackup <file>` :fontawesome-solid-code-fork: |

### After a restore

- The match pauses (`matchzy_pause_after_restore`). Set `matchzy_restore_auto_unpause true` to resume on its own after `matchzy_restore_unpause_delay` seconds. :fontawesome-solid-code-fork:
- The scoreboard is rolled back too: round history, kills, deaths, assists, damage, score and MVPs (`matchzy_restore_scoreboard_stats`). :fontawesome-solid-code-fork:
- A restore is only announced once the server has actually loaded it; a failed load is reported in chat. :fontawesome-solid-code-fork:
