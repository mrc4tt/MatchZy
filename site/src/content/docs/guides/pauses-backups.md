---
title: "Pauses and backups"
sidebar:
  order: 4
---

## Pause types

| Type | Started by | Ends when |
|---|---|---|
| Normal | `.pause` | Both teams type `.unpause`. |
| Tactical | `.tac` (or `.pause` with `matchzy_use_pause_command_for_tactical_pause`) | The timeout runs out. |
| Technical | `.tech` | Both teams unpause, or `matchzy_tech_pause_duration` runs out (then it unpauses on its own). |
| Admin | `.forcepause` | An admin types `.forceunpause`. |
| Auto | A team drops below `matchzy_autopause_minplayers` | The team is full again; the game resumes after `matchzy_autopause_resume_delay` seconds. |

:::note[Technical pauses]
Each team gets `matchzy_max_tech_pauses_allowed` per map. With `matchzy_tech_pause_mode 1` (Get5 rules) a tech pause never ends on its own: once it has lasted `matchzy_tech_pause_duration` seconds in freeze time either team can `.unpause`, and the pausing team can cancel it with `.unpause` before freeze time without using it up.

While a technical pause lasts, every player sees a box in the middle of the screen: the team and player who called it, the time left (or who can unpause), the technical pauses left and which teams have typed `.unpause`. A regular `.pause` shows the same box (without a time limit or a pause count), and a tactical timeout (`.tac`) shows the team, the time left and the timeouts left. `matchzy_pause_hud 1` shows a single line of text instead, `0` turns it off.
:::

**Overtime**: tactical timeouts (`.tac`) follow the engine settings in live.cfg: 4 per team in regulation (`mp_team_timeout_max`) and one more when overtime starts (`mp_team_timeout_ot_add_once 1`, up to `mp_team_timeout_ot_max 5`). Wingman and rush (`live_wingman.cfg`, `live_rush.cfg`) give 1 timeout of 60 seconds per team and 1 more in each overtime. In every overtime period each team may also use `.pause` `matchzy_overtime_pauses_per_team` times (1 by default). Admin pauses are not limited.

:::tip[Adjust to your needs]
These are defaults. Both `config.cfg` and `live.cfg` can be edited to your needs:

- `matchzy_overtime_pauses_per_team` goes in `config.cfg`.
- The `mp_team_timeout_*` lines go in `cfg/MatchZy/live_override.cfg` (it runs right after `live.cfg` when the match goes live, so the same lines in `config.cfg` would be overridden), or per match in the match config's `cvars` block.

Updates never replace `config.cfg`, `live.cfg` or an override file. See [Updating](/getting-started/updating/#keeping-your-own-changes).
:::

Autopause replaces the game's own `sv_matchpause_auto_5v5` and ignores bot teams. Every pause and unpause is sent as a `match_paused` / `match_unpaused` [event](/reference/events/).

## Round backups

MatchZy saves a backup at the start of every live round (Valve's backup system plus MatchZy's own state). Backups live in `csgo/MatchZyDataBackup/` and can also be POSTed to `matchzy_remote_backup_url`.

:::caution[Launch option required]
CS2 only writes and loads round backups when the server is started with `-allow_round_backup_files`. Without it, the console says "Round backup files are disabled" and every restore fails. When the option is missing, MatchZy warns admins in chat (and in the console) when a match is loaded and when it goes live, and refuses `.restore` with a chat message.
:::

| Situation | Command |
|---|---|
| Both teams want to replay this round | `.stop` (both teams) |
| Admin replays this round | `.restorecurrent` / `!rr` |
| Go back one round | `.restorelast` |
| Go back to round N | `.restore <n>` |
| See what is available | `.backups` |
| Pick a round from a list | `.ma` → Match Control → Restore Round (asks for confirmation) |
| Server crashed mid-match | Nothing: the match recovers on its own (see [Crash recovery](#crash-recovery)). By hand: `.ma` → Restore Round, or `.backups` and then `matchzy_loadbackup <file>.json` |

### After a restore

- The match pauses (`matchzy_pause_after_restore`). Set `matchzy_restore_auto_unpause true` to resume on its own after `matchzy_restore_unpause_delay` seconds.
- The scoreboard is rolled back too: round history, kills, deaths, assists, damage, score and MVPs (`matchzy_restore_scoreboard_stats`).
- A restore is only announced once the server has actually loaded it; a failed load is reported in chat.

## Crash recovery

A loaded match is kept in `csgo/MatchZyDataBackup/active_match.json` until the series ends or the match is cancelled. When the server crashes, restarts or MatchZy is reloaded in the middle of a match:

1. **Automatic recovery** (`matchzy_auto_recover`, default on): 10 seconds after the server is ready, with no match loaded, MatchZy restores the newest round backup of that match. The match config, teams, match id and score come back, and the round is played once both teams are ready. A match interrupted before its first round backup is loaded again from its match config. Matches last active more than `matchzy_auto_recover_max_age` minutes ago (default 180) are left alone. A panel that loads a match within those 10 seconds wins. The panel is told with a `match_recovered` event.
2. **Players are not locked out**: with `matchzy_kick_when_no_match_loaded`, a player on the interrupted match's roster is let in while no match is loaded, and admins are told in chat.
3. **Valve's raw restore goes through MatchZy**: `mp_backup_restore_load_file <file>.txt` only brings the score back, not the rosters, team names or pause state. With `matchzy_raw_backup_restore redirect` (default) it is run as MatchZy's restore of `<file>.json` (a `.json` name works the same). When only the `.txt` exists, MatchZy rebuilds the `.json` from another backup of the same match (or the loaded or interrupted match) and works out each team's side for that round; admins are asked to check the sides. `block` refuses it, `allow` runs it as is.
4. `.restore`, `.restorelast` and `.restorecurrent` without a live match list the newest backups on disk and the command to load one.

### Runbook for tournament admins

| Problem | Do this |
|---|---|
| Players are kicked on connect | Read the kick reason in the server log (`[ConnectGate]`, `[Disconnect]`) or the panel's `player_kicked` event. `no_match_loaded`: the match is not loaded, load it again (or `matchzy_loadbackup`). `not_in_match`: the SteamID is missing from the roster. |
| The server crashed or restarted | Wait 10 seconds for the automatic recovery, then both teams `.ready`. Without it: `.recover` (or `matchzy_recover` over RCON). |
| Wrong score or a round must be replayed | `.restore <round>`. Never `mp_backup_restore_load_file`. |
| A round does not end (everyone dead, timer at 0:00) | MatchZy ends it after a few seconds (`matchzy_round_watchdog`). |
| Check the connection to the panel | `matchzy_test_event` |
