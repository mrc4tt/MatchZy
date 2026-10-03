# Pauses and backups

## Pause types

| Type | Started by | Ends when |
|---|---|---|
| Normal | `.pause` | Both teams type `.unpause`. |
| Tactical | `.tac` (or `.pause` with `matchzy_use_pause_command_for_tactical_pause`) | The timeout runs out. |
| Technical | `.tech` | Both teams unpause, or `matchzy_tech_pause_duration` runs out (then it unpauses on its own). |
| Admin | `.forcepause` | An admin types `.forceunpause`. |
| Auto | A team drops below `matchzy_autopause_minplayers` | The team is full again; the game resumes after `matchzy_autopause_resume_delay` seconds. |

!!! note "Technical pauses"
    Each team gets `matchzy_max_tech_pauses_allowed` per map. With `matchzy_tech_pause_mode 1` (Get5 rules) a tech pause never ends on its own: once it has lasted `matchzy_tech_pause_duration` seconds in freeze time either team can `.unpause`, and the pausing team can cancel it with `.unpause` before freeze time without using it up.

**Overtime**: tactical timeouts (`.tac`) follow the engine settings in live.cfg: 4 per team in regulation (`mp_team_timeout_max`) and one more when overtime starts (`mp_team_timeout_ot_add_once 1`, up to `mp_team_timeout_ot_max 5`). In every overtime period each team may also use `.pause` `matchzy_overtime_pauses_per_team` times (1 by default). Admin pauses are not limited.

!!! tip "Adjust to your needs"
    These are defaults. Both `config.cfg` and `live.cfg` can be edited to your needs:

    - `matchzy_overtime_pauses_per_team` goes in `config.cfg`.
    - The `mp_team_timeout_*` lines go in `cfg/MatchZy/live.cfg` (it runs when the match goes live, so the same lines in `config.cfg` would be overridden), or per match in the match config's `cvars` block.

    `live.cfg` is part of the release zip, so extracting an update over the server replaces it. Keep a copy of your edits. `config.cfg` is never replaced.

Autopause replaces the game's own `sv_matchpause_auto_5v5` and ignores bot teams. Every pause and unpause is sent as a `match_paused` / `match_unpaused` [event](../reference/events.md).

## Round backups

MatchZy saves a backup at the start of every live round (Valve's backup system plus MatchZy's own state). Backups live in `csgo/MatchZyDataBackup/` and can also be POSTed to `matchzy_remote_backup_url`.

| Situation | Command |
|---|---|
| Both teams want to replay this round | `.stop` (both teams) |
| Admin replays this round | `.restorecurrent` / `!rr` |
| Go back one round | `.restorelast` |
| Go back to round N | `.restore <n>` |
| See what is available | `.backups` |
| Server crashed mid-match | Reconnect, `.backups` lists the newest files on disk with a ready-made restore command, then `!loadbackup <file>` |

### After a restore

- The match pauses (`matchzy_pause_after_restore`). Set `matchzy_restore_auto_unpause true` to resume on its own after `matchzy_restore_unpause_delay` seconds.
- The scoreboard is rolled back too: round history, kills, deaths, assists, damage, score and MVPs (`matchzy_restore_scoreboard_stats`).
- A restore is only announced once the server has actually loaded it; a failed load is reported in chat.
