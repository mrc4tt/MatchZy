# Configuration (convars)

All settings live in `cfg/MatchZy/config.cfg`, which MatchZy executes on every load. The file is written with comments on first start and new settings are appended to it on updates (see [Updating](../getting-started/updating.md)). You can also set any value from the server console or another cfg.

!!! note "Boolean values"
    Every on/off setting accepts `true` / `false` and `1` / `0`.

!!! danger "Keep secrets out of config.cfg"
    Settings marked :material-lock: hold URLs or tokens (remote log, remote backup, demo upload). `config.cfg` is readable by anyone with file access to the server, so set these from a private cfg (for example one exec'd from `server.cfg`) or from your panel's startup commands.

Legend: **cfg** = present in the generated `config.cfg`. :fontawesome-solid-code-fork: = added by this fork (not in upstream MatchZy 0.8.15).

## General and admin

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_everyone_is_admin` | `false` | Y | Every player gets MatchZy admin rights. For private practice servers. |
| `matchzy_autostart_mode` | `1` | Y | Mode on map load: `0` none (sleep), `1` match/warmup, `2` practice. Mode commands (`css_prac`, `css_match`, ...) also write it, so a chosen mode survives a map change. |
| `matchzy_whitelist_enabled_default` | `false` | Y | Enforce `whitelist.cfg` on connect. Admins can toggle with `.whitelist`. |
| `matchzy_kick_when_no_match_loaded` | `false` | Y | Kick players who are not on the loaded match's roster (everyone if no match is loaded). Admins are exempt. |
| `matchzy_hostname_format` | `""` | Y | Hostname template, e.g. `MatchZy \| {TEAM1} vs {TEAM2}`. Empty keeps your hostname. |
| `matchzy_map_console_command_enabled` :fontawesome-solid-code-fork: | `true` | Y | Register `css_map` / `!map`. Set `false` when another plugin owns it. MatchZy also yields automatically when CS2-SimpleAdmin or CS2MapChange is installed. `.map` always works. |
| `matchzy_asay_console_enabled` :fontawesome-solid-code-fork: | `true` | N | Register `css_asay` / `!asay`. Set `false` when another plugin owns it. |
| `matchzy_dot_trigger_dedupe` :fontawesome-solid-code-fork: | `true` | Y | When `.` is a CounterStrikeSharp chat trigger in `core.json`, let CounterStrikeSharp handle dot commands so they do not run twice. |

## Ready and warmup

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_warmup_enabled` :fontawesome-solid-code-fork: | `true` | Y | Use warmup (execute `warmup.cfg`) before a match. |
| `matchzy_minimum_ready_required` | `2` | Y | Ready players needed to start. `0` = every connected player. Also `.readyrequired <n>`. |
| `matchzy_allow_force_ready` | `true` | Y | Allow `.forceready` (match setup only). Alias `get5_allow_force_ready`. |
| `matchzy_ready_hint_style` :fontawesome-solid-code-fork: | `0` | Y | `0` classic center text, `1` HTML ready-up panel (progress bar, team split, own status; hides the native warmup banner). |
| `matchzy_ready_hint_blink` :fontawesome-solid-code-fork: | `false` | Y | Blink the "NOT READY" line on the HTML panel. |
| `matchzy_ready_clantag_enabled` :fontawesome-solid-code-fork: | `true` | Y | Show `[READY]` / `[UNREADY]` scoreboard clan tags. |
| `matchzy_ready_up_by_ping` :fontawesome-solid-code-fork: | `true` | Y | Pinging (middle mouse) toggles ready. |

## Knife, sides and team names

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_knife_enabled_default` | `true` | Y | Knife round on by default. Admins toggle with `.knife`. |
| `matchzy_ct_name` / `matchzy_t_name` :fontawesome-solid-code-fork: | `""` | Y | Fixed scrim team names for the starting CT / T side. Empty = automatic `team_<player>` names. |
| `matchzy_auto_team_names_enabled` :fontawesome-solid-code-fork: | `true` | Y | Rename teams in scrim mode at all. `false` keeps the game's names. Match config names always apply. |

## Pauses

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_allow_pause` :fontawesome-solid-code-fork: | `true` | N | Enable `.pause`. |
| `matchzy_allow_unpause` :fontawesome-solid-code-fork: | `true` | N | Enable `.unpause`. |
| `matchzy_use_pause_command_for_tactical_pause` | `false` | Y | `.pause` starts a tactical timeout instead of a normal pause. |
| `matchzy_enable_tech_pause` | `true` | Y | Enable `.tech`. |
| `matchzy_tech_pause_duration` | `300` | Y | Tech pause length in seconds. `-1` = unlimited. |
| `matchzy_max_tech_pauses_allowed` | `2` | Y | Tech pauses per team. |
| `matchzy_autopause_enabled` :fontawesome-solid-code-fork: | `true` | Y | Pause automatically when a team drops below the minimum player count. |
| `matchzy_autopause_minplayers` :fontawesome-solid-code-fork: | `5` | Y | Players per team below which autopause triggers. |
| `matchzy_autopause_resume_delay` :fontawesome-solid-code-fork: | `3` | Y | Seconds before resuming once teams are full again. |

## Match and series

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_playout_enabled_default` | `false` | Y | Play all max rounds (no early win). Toggle with `.playout`. |
| `matchzy_reset_cvars_on_series_end` | `true` | Y | Restore cvars changed by a match config's `cvars` block when the series ends. |
| `matchzy_match_start_message` | `""` | Y | Chat message at match start. `$$$` = new line. Supports `{TIME}`, `{MATCH_ID}`, `{MAP}`, `{MAPNUMBER}`, `{TEAM1}`, `{TEAM2}` and color tags. |
| `matchzy_match_end_auto_changelevel` :fontawesome-solid-code-fork: | `true` | Y | Change map automatically after a match ends. Disable for panel-driven servers (G5API). |

## Demos (GOTV)

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_demo_path` | `demos/` | Y | Demo folder relative to `csgo/`. Must end with `/`. |
| `matchzy_demo_name_format` | `{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}` | Y | Demo file name. Also `{MAPNUMBER}`. |
| `matchzy_demo_upload_url` :material-lock: | `""` | N | Upload the demo here when the map ends. Alias `get5_demo_upload_url`. |
| `matchzy_demo_upload_s3` :fontawesome-solid-code-fork: | `false` | Y | Upload with HTTP `PUT` and the raw `.dem` as body, for S3 presigned URLs (sign with `Content-Type: application/octet-stream`). Alias `get5_demo_upload_s3`. |
| `matchzy_demo_upload_header_key` | `""` | N | Custom header name on the upload. Alias `get5_demo_upload_header_key`. |
| `matchzy_demo_upload_header_value` :material-lock: | `""` | N | Custom header value. Alias `get5_demo_upload_header_value`. |

## Remote log (events)

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_remote_log_url` :material-lock: | `""` | N | Every event is POSTed here as JSON. Alias `get5_remote_log_url`. |
| `matchzy_remote_log_header_key` | `""` | N | Custom header name. Alias `get5_remote_log_header_key`. |
| `matchzy_remote_log_header_value` :material-lock: | `""` | N | Custom header value. Alias `get5_remote_log_header_value`. |
| `matchzy_remote_log_auth_key` :fontawesome-solid-code-fork: | `""` | N | Name of an extra authentication header. |
| `matchzy_remote_log_auth_value` :fontawesome-solid-code-fork: :material-lock: | `""` | N | Value of the extra authentication header. |

## Backups and restore

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_stop_command_available` | `true` | Y | Enable `.stop` (both teams vote to replay the current round). |
| `matchzy_stop_command_no_damage` | `false` | N | `.stop` is refused once an opponent has been damaged that round. |
| `matchzy_pause_after_restore` | `true` | Y | Pause the match after a round restore. |
| `matchzy_restore_auto_unpause` :fontawesome-solid-code-fork: | `false` | Y | Unpause on its own after a restore instead of waiting for both teams. |
| `matchzy_restore_unpause_delay` :fontawesome-solid-code-fork: | `5` | Y | Countdown in seconds for the automatic unpause. |
| `matchzy_restore_scoreboard_stats` :fontawesome-solid-code-fork: | `true` | Y | A restore also rolls back the round history and player K/D/A, damage, score and MVPs. |
| `matchzy_remote_backup_url` :material-lock: | `""` | N | Round backups are POSTed here. Alias `get5_remote_backup_url`. |
| `matchzy_remote_backup_header_key` | `""` | N | Custom header name. Alias `get5_remote_backup_header_key`. |
| `matchzy_remote_backup_header_value` :material-lock: | `""` | N | Custom header value. Alias `get5_remote_backup_header_value`. |

## Stats

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_stats_include_bots` :fontawesome-solid-code-fork: | `false` | Y | Record bots in the database, CSV and stats events (stable id per bot name). |
| `matchzy_enable_damage_report` | `true` | Y | Per-round damage report in chat. |

Database connection settings are in [`database.json`](files.md#databasejson), not convars.

## Coaching

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_coach_enabled` :fontawesome-solid-code-fork: | `true` | Y | Allow `.coach`. |
| `matchzy_coaching_mode` :fontawesome-solid-code-fork: | `1` | Y | Coach viewing spot: `1` use `spawns/coach/<map>.json` when present, otherwise compute one; `2` always compute. |

## Practice

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_save_nades_as_global_enabled` | `false` | Y | Saved lineups are visible to everyone instead of private. |
| `matchzy_max_saved_last_grenades` | `512` | Y | Grenade history per player per map. `0` disables. |
| `matchzy_smoke_color_enabled` | `false` | Y | Player-colored smokes. |
| `matchzy_botjiggle_range` :fontawesome-solid-code-fork: | `30` | N | Strafe width for `.botjiggle`. |
| `matchzy_grenadelibrary_labels` :fontawesome-solid-code-fork: | `true` | N | Text labels above `.shownades` markers. |
| `matchzy_grenadelibrary_label_scale` :fontawesome-solid-code-fork: | `0.2` | N | Size of those labels. |
| `matchzy_prac_disable_magazine_drop` :fontawesome-solid-code-fork: | `true` | N | Keep leftover ammo on reload in practice. |

## Chat

| Setting | Default | cfg | Description |
|---|---|---|---|
| `matchzy_chat_prefix` | `{Green}[MatchZy]{Default}` | Y | Prefix for MatchZy chat messages. Color tags supported. |
| `matchzy_admin_chat_prefix` | `[{Red}ADMIN{Default}]` | Y | Prefix for `.asay` messages. |
| `matchzy_chat_messages_timer_delay` | `21` | Y | Seconds between reminder messages (unready, paused, ...). |

## Get5 aliases

For panel compatibility these `get5_` names map to the `matchzy_` settings above: `get5_allow_force_ready`, `get5_demo_upload_url`, `get5_demo_upload_s3`, `get5_demo_upload_header_key`, `get5_demo_upload_header_value`, `get5_remote_log_url`, `get5_remote_log_header_key`, `get5_remote_log_header_value`, `get5_remote_backup_url`, `get5_remote_backup_header_key`, `get5_remote_backup_header_value`.
