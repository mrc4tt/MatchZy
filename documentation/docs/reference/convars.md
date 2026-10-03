# Configuration (convars)

All settings live in `cfg/MatchZy/config.cfg`, which MatchZy executes on every load. The file is written with comments on first start and new settings are appended to it on updates (see [Updating](../getting-started/updating.md)). You can also set any value from the server console or another cfg.

!!! note "Boolean values"
    Every on/off setting accepts `true` / `false` and `1` / `0`.

!!! danger "Keep secrets out of config.cfg"
    Settings marked :material-lock: hold URLs or tokens (remote log, remote backup, demo upload). `config.cfg` is readable by anyone with file access to the server, so set these from a private cfg (for example one exec'd from `server.cfg`) or from your panel's startup commands.

Legend: **cfg** = present in the generated `config.cfg`.

## General and admin

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_everyone_is_admin` | `false` | Y | Every player gets MatchZy admin rights. For private practice servers. |
| `matchzy_autostart_mode` | `1` | Y | Mode on map load: `0` none (sleep), `1` match/warmup, `2` practice. Mode commands (`css_prac`, `css_match`, ...) also write it, so a chosen mode survives a map change. |
| `matchzy_whitelist_enabled_default` | `false` | Y | Enforce `whitelist.cfg` on connect. Admins can toggle with `.whitelist`. |
| `matchzy_kick_when_no_match_loaded` | `false` | Y | Kick players who are not on the loaded match's roster (everyone if no match is loaded). Admins are exempt. |
| `matchzy_hostname_format` | `""` | Y | Hostname template, e.g. `MatchZy \| {TEAM1} vs {TEAM2}`. Empty keeps your hostname. |
| `matchzy_map_console_command_enabled` | `true` | Y | Register `css_map` / `!map`. Set `false` when another plugin owns it. MatchZy also yields automatically when CS2-SimpleAdmin or CS2MapChange is installed. `.map` always works. |
| `matchzy_asay_console_enabled` | `true` | Y | Register `css_asay` / `!asay`. Set `false` when another plugin owns it. |
| `matchzy_dot_trigger_dedupe` | `true` | Y | When `.` is a CounterStrikeSharp chat trigger in `core.json`, let CounterStrikeSharp handle dot commands so they do not run twice. |

## Ready and warmup

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_warmup_enabled` | `true` | Y | Use warmup (execute `warmup.cfg`) before a match. |
| `matchzy_minimum_ready_required` | `10` | Y | Ready players needed to start. `0` = every connected player. Also `.readyrequired <n>`. |
| `matchzy_ready_per_team` | `0` | Y | Per-team ready-up. `N` = a team is ready once `N` of its players are ready; the match starts when CT and T are both ready (`1` = one player per team). Works with and without a loaded match. `0` = off. |
| `matchzy_allow_force_ready` | `true` | Y | Allow `.forceready` (match setup only). Alias `get5_allow_force_ready`. |
| `matchzy_ready_hint_style` | `0` | Y | `0` classic center text, `1` HTML ready-up panel (progress bar, team split, own status; hides the native warmup banner), `2` no center text: a chat reminder lists the players who are not ready every `matchzy_chat_messages_timer_delay` seconds; also hides the native warmup banner. |
| `matchzy_ready_hint_blink` | `false` | Y | Blink the "NOT READY" line on the HTML panel. |
| `matchzy_loaded_match_hide_mode_hints` | `true` | Y | In a match loaded from a match config, joining players only see the ready-up hint (no `.scrim` / `.prac` / `.knife` hints or admin help line). |
| `matchzy_ready_clantag_enabled` | `true` | Y | Show `[READY]` / `[UNREADY]` scoreboard clan tags. |
| `matchzy_ready_up_by_ping` | `true` | Y | Pinging (middle mouse) toggles ready. |

## Knife, sides and team names

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_knife_enabled_default` | `true` | Y | Knife round on by default. Admins toggle with `.knife`. |
| `matchzy_ct_name` / `matchzy_t_name` | `""` | Y | Fixed scrim team names for the starting CT / T side. Empty = automatic `team_<player>` names. |
| `matchzy_auto_team_names_enabled` | `true` | Y | Rename teams in scrim mode at all. `false` keeps the game's names. Match config names always apply. |

## Pauses

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_allow_pause` | `true` | Y | Enable `.pause`. |
| `matchzy_allow_unpause` | `true` | Y | Enable `.unpause`. Admin force unpause is not affected. |
| `matchzy_use_pause_command_for_tactical_pause` | `false` | Y | `.pause` starts a tactical timeout instead of a normal pause. |
| `matchzy_enable_tech_pause` | `true` | Y | Enable `.tech`. Also settable as `get5_allow_technical_pause`. |
| `matchzy_tech_pause_duration` | `300` | Y | Tech pause length in seconds; the match unpauses on its own when it runs out. `-1` = unlimited. With `matchzy_tech_pause_mode 1`: seconds in freeze time before either team can unpause (`0` = both teams always have to). Also settable as `get5_tech_pause_time` / `matchzy_tech_pause_time`, which switch to `matchzy_tech_pause_mode 1`. |
| `matchzy_max_tech_pauses_allowed` | `2` | Y | Tech pauses per team per map. With `matchzy_tech_pause_mode 1`, `0` = unlimited. Also settable as `get5_max_tech_pauses` / `matchzy_max_tech_pauses`, which switch to `matchzy_tech_pause_mode 1`. |
| `matchzy_tech_pause_mode` | `0` | Y | `0` = a tech pause ends on its own when its time runs out. `1` = Get5 rules: the pause counts once it takes effect in freeze time, the pausing team can cancel it before then, and once its time is up either team can `.unpause`. Setting `get5_max_tech_pauses` or `get5_tech_pause_time` switches to `1`. |
| `matchzy_overtime_pauses_per_team` | `1` | Y | `.pause` uses per team in each overtime period. `0` = no limit. Tactical timeouts in overtime are set in live.cfg (`mp_team_timeout_ot_add_once`, `mp_team_timeout_ot_add_each`, `mp_team_timeout_ot_max`). |
| `matchzy_autopause_enabled` | `true` | Y | Pause automatically when a team drops below the minimum player count. |
| `matchzy_autopause_minplayers` | `5` | Y | Players per team below which autopause triggers. Autopause is active once the map has had at least twice this many players. |
| `matchzy_autopause_resume_delay` | `3` | Y | Seconds before resuming once teams are full again. |

## Match and series

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_playout_enabled_default` | `false` | Y | Play all max rounds (no early win). Toggle with `.playout`. |
| `matchzy_reset_cvars_on_series_end` | `true` | Y | Restore cvars changed by a match config's `cvars` block when the series ends. |
| `matchzy_match_start_message` | `""` | Y | Chat message at match start. `$$$` = new line. Supports `{TIME}`, `{MATCH_ID}`, `{MAP}`, `{MAPNUMBER}`, `{TEAM1}`, `{TEAM2}` and color tags. |
| `matchzy_match_end_auto_changelevel` | `true` | Y | Change map automatically after a match ends. Disable for panel-driven servers (G5API). |
| `matchzy_empty_shutdown_seconds` | `0` | Y | Close the server (`quit`) after it has had no players for this many seconds (bots and CSTV do not count; also counted from server start). Sends `server_shutdown` with `reason` `empty` first and waits for demo and backup uploads. Can be set per match in the match config's `cvars`. `0` = off. |

## Tournament timeouts

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_forfeit_ready_timeout` | `0` | Y | Loaded matches: seconds after a map's ready phase begins before a team that is not ready forfeits the series. Neither team ready: the series ends in a tie. `0` = off. Also settable as `matchzy_time_to_start` / `get5_time_to_start`. |
| `matchzy_forfeit_veto_ready_timeout` | `-1` | Y | The same for the ready-up before the map veto. `-1` = same as `matchzy_forfeit_ready_timeout`, `0` = no limit. Also settable as `matchzy_time_to_start_veto` / `get5_time_to_start_veto`. |
| `matchzy_ready_mode` | `0` | Y | Loaded matches: `0` = players type `.ready`. `1` = join mode: a team is ready once `min_players_to_ready` of its players are on its side, and the match starts `matchzy_join_start_delay` seconds later. |
| `matchzy_join_start_delay` | `10` | Y | Join mode: seconds between everyone having joined and the match starting. |
| `matchzy_forfeit_leave_timeout` | `0` | Y | Loaded matches: seconds a team may have nobody on its side during a live map before it forfeits the series. `0` = off. |
| `matchzy_veto_step_timeout` | `0` | Y | Seconds a veto captain has per ban, pick or side choice; then it is made at random (side: CT). `0` = off. |

## Demos (GOTV)

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_demo_path` | `demos/` | Y | Demo folder relative to `csgo/`. Must end with `/`. |
| `matchzy_demo_name_format` | `{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}` | Y | Demo file name. Also `{MAPNUMBER}`. |
| `matchzy_demo_upload_url` :material-lock: | `""` | N | Upload the demo here when the map ends. Alias `get5_demo_upload_url`. |
| `matchzy_demo_upload_s3` | `false` | Y | Upload with HTTP `PUT` and the raw `.dem` as body, for S3 presigned URLs (sign with `Content-Type: application/octet-stream`). Alias `get5_demo_upload_s3`. |
| `matchzy_demo_upload_header_key` | `""` | N | Custom header name on the upload. Alias `get5_demo_upload_header_key`. |
| `matchzy_demo_upload_header_value` :material-lock: | `""` | N | Custom header value. Alias `get5_demo_upload_header_value`. |

## Remote log (events)

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_remote_log_url` :material-lock: | `""` | N | Every event is POSTed here as JSON. Alias `get5_remote_log_url`. |
| `matchzy_remote_log_header_key` | `""` | N | Custom header name. Alias `get5_remote_log_header_key`. |
| `matchzy_remote_log_header_value` :material-lock: | `""` | N | Custom header value. Alias `get5_remote_log_header_value`. |
| `matchzy_remote_log_auth_key` | `""` | N | Name of an extra authentication header. |
| `matchzy_remote_log_auth_value` :material-lock: | `""` | N | Value of the extra authentication header. |

## Backups and restore

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_stop_command_available` | `true` | Y | Enable `.stop` (both teams vote to replay the current round). |
| `matchzy_stop_command_no_damage` | `false` | Y | `.stop` is refused once an opponent has been damaged that round. |
| `matchzy_pause_after_restore` | `true` | Y | Pause the match after a round restore. |
| `matchzy_restore_auto_unpause` | `false` | Y | Unpause on its own after a restore instead of waiting for both teams. |
| `matchzy_restore_unpause_delay` | `5` | Y | Countdown in seconds for the automatic unpause. |
| `matchzy_restore_scoreboard_stats` | `true` | Y | A restore also rolls back the round history and player K/D/A, damage, score and MVPs. |
| `matchzy_remote_backup_url` :material-lock: | `""` | N | Round backups are POSTed here. Alias `get5_remote_backup_url`. |
| `matchzy_remote_backup_header_key` | `""` | N | Custom header name. Alias `get5_remote_backup_header_key`. |
| `matchzy_remote_backup_header_value` :material-lock: | `""` | N | Custom header value. Alias `get5_remote_backup_header_value`. |

## Stats

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_stats_include_bots` | `false` | Y | Record bots in the database, CSV and stats events (stable id per bot name). |
| `matchzy_events_format` | `get5` | Y | Format of the live events: `get5` (Get5 event schema, nested player/weapon objects) or `legacy` (flat format of 0.8.94 and older). See [Events](events.md#live-scorebot). |
| `matchzy_enable_damage_report` | `true` | Y | Per-round damage report in chat. |

Database connection settings are in [`database.json`](files.md#databasejson), not convars.

## Coaching

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_coach_enabled` | `true` | Y | Allow `.coach`. |
| `matchzy_coach_listed_only` | `false` | Y | In a loaded match only the SteamIDs in a team's `coaches` list may coach it; a team without a list has no coach. |
| `matchzy_coaching_mode` | `1` | Y | Coach viewing spot: `1` use `spawns/coach/<map>.json` when present, otherwise compute one; `2` always compute. |

## Practice

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_save_nades_as_global_enabled` | `false` | Y | Saved lineups are visible to everyone instead of private. |
| `matchzy_max_saved_last_grenades` | `512` | Y | Grenade history per player per map. `0` disables. |
| `matchzy_smoke_color_enabled` | `false` | Y | Player-colored smokes. |
| `matchzy_botjiggle_range` | `30` | N | Strafe width for `.botjiggle`. |
| `matchzy_grenadelibrary_labels` | `true` | N | Text labels above `.shownades` markers. |
| `matchzy_grenadelibrary_label_scale` | `0.2` | N | Size of those labels. |
| `matchzy_prac_disable_magazine_drop` | `true` | N | Keep leftover ammo on reload in practice. |

## Chat

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_chat_prefix` | `{Green}[MatchZy]{Default}` | Y | Prefix for MatchZy chat messages. Color tags supported. |
| `matchzy_admin_chat_prefix` | `[{Red}ADMIN{Default}]` | Y | Prefix for `.asay` messages. |
| `matchzy_chat_messages_timer_delay` | `21` | Y | Seconds between reminder messages (unready, paused, ...). |

## Get5 aliases

For panel compatibility these `get5_` names map to the `matchzy_` settings above: `get5_allow_force_ready`, `get5_demo_upload_url`, `get5_demo_upload_s3`, `get5_demo_upload_header_key`, `get5_demo_upload_header_value`, `get5_remote_log_url`, `get5_remote_log_header_key`, `get5_remote_log_header_value`, `get5_remote_backup_url`, `get5_remote_backup_header_key`, `get5_remote_backup_header_value`.
