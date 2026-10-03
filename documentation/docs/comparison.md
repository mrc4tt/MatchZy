# Fork vs upstream

This fork started from [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak and keeps its core: match configs, veto, knife round, ready system, pauses, practice mode, backups, demos, database stats and Get5 panel support. Most command names and config keys are the same as upstream; the exceptions are listed under [Differences to be aware of](#differences-to-be-aware-of).

The table compares with **upstream 0.9.1** (October 2026). Fork version numbers are independent of upstream.

Legend: <span class="mz-yes">✔</span> available, <span class="mz-plus">＋</span> extended in the fork, <span class="mz-no">–</span> not available.

## At a glance

| Area | Upstream | Fork |
|---|:---:|:---:|
| Match configs, veto, BO1/BO3/BO5, knife | <span class="mz-yes">✔</span> | <span class="mz-plus">＋</span> |
| Get5 match config fields (`veto_first`, `side_type`, `players` as a list) | <span class="mz-yes">✔</span> | <span class="mz-yes">✔</span> |
| Scrim and hill modes | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Players vs bots matches | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Coaches in the match config | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Coach viewing spots on every map | 8 fixed maps | <span class="mz-plus">＋</span> computed + 11 tuned |
| In-game admin menu and match wizard | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| HTML ready-up panel | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Ready-up time limit with forfeit (`get5_time_to_start`, `.addreadytime`) | <span class="mz-yes">✔</span> | <span class="mz-yes">✔</span> |
| Join ready mode (`matchzy_ready_mode 1`) | <span class="mz-yes">✔</span> | <span class="mz-yes">✔</span> |
| Per-team ready count (`matchzy_ready_per_team`) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Get5 technical pause rules | <span class="mz-yes">✔</span> | <span class="mz-yes">✔</span> |
| Overtime pause limit per team | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Auto-pause on disconnect | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Forfeit when a team leaves during a live map | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Time limit per veto step | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Surrender vote | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Round restore rolls back scoreboard | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Backup list and restore | console only | <span class="mz-plus">＋</span> in-game menu, `.restorelast` |
| Live events in Get5 format (kills, bomb, pauses, round start, backup loaded) | <span class="mz-yes">✔</span> | <span class="mz-plus">＋</span> |
| Extra scorebot events (damage, grenades, flashes, bomb carrier, freeze time) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Server lifecycle events (`server_ready`, `map_change`, `server_shutdown`) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Demo recording checks and restarts | warning only | <span class="mz-yes">✔</span> |
| Demo upload to S3 (presigned PUT) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| HLTV-style rating, KAST, clutches, opening duels | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Practice: grenade library with in-world markers | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Practice: global rethrow, arcs, land markers, auto-clear | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Practice: named bot spots, bot jiggle | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Workshop maps by name (`ws:<name>`) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Automatic map change after a match | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Per-mode `_override.cfg` files | live and wingman | <span class="mz-plus">＋</span> every mode |
| Updates never overwrite your cfg files | admin files only | <span class="mz-plus">＋</span> |
| Translations | 12 languages | English, Danish, Albanian |
| Unit tests | <span class="mz-yes">✔</span> | <span class="mz-no">–</span> |
| Runs on stock CounterStrikeSharp | <span class="mz-yes">✔</span> | <span class="mz-yes">✔</span> |
| .NET runtime | .NET 10 | .NET 10 |

## In detail

### Match flow

- **Scrim** (`.scrim`) and **hill** (`.hill`) modes: every round is played, no knife; hill is full-buy.
- **Players vs bots**: `"players": "any"` on one team, `"bots": true` on the other. Bots hold their side through halftime, overtime and restores, and are recorded in stats.
- **Workshop maps** in match configs and `.map`: `workshop/<id>`, `ws/<id>`, and `ws:<name>` from the hosted collection. Upstream accepts a plain workshop id.
- **Stop anywhere**: `.stopmatch` works in setup, veto, warmup, knife and live.
- **Auto changelevel** after a match (`matchzy_match_end_auto_changelevel`), off automatically for panel-loaded matches.
- **Surrender vote** (`!gg`) when 6+ rounds behind.
- **Forfeit on leave**: `matchzy_forfeit_leave_timeout` ends the series for a team that has nobody on its side for that many seconds of a live map.
- **Veto step timeout**: `matchzy_veto_step_timeout` makes a ban, pick or side choice at random when a captain takes too long.

### Ready and warmup

- HTML ready-up panel with progress bar, team split and per-player status (`matchzy_ready_hint_style 1`), or upstream's chat reminder (`matchzy_ready_hint_style 2`).
- Per-team ready count: `matchzy_ready_per_team N` makes a team ready once N of its players are ready.
- Ping to ready, `.readycheck`, `[READY]` clan tags, coaches excluded from the count.
- `.warmupbots` to warm up aim while waiting.
- `matchzy_warmup_enabled` to skip warmup entirely.
- Both versions have the Get5 ready-up time limit and join ready mode. The fork also accepts the limit as `matchzy_forfeit_ready_timeout`, and `matchzy_forfeit_veto_ready_timeout` defaults to `-1` (same limit as the map) where upstream's `matchzy_time_to_start_veto` defaults to no limit.

### Pauses and backups

- Auto-pause when a team drops below N players, auto-resume when full.
- `matchzy_overtime_pauses_per_team` limits `.pause` in each overtime period.
- Technical pauses: both versions support Get5 rules (count once in freeze time, cancel before then, either team can unpause after the time). In the fork they are enabled with `matchzy_tech_pause_mode 1` or by setting `get5_max_tech_pauses` / `get5_tech_pause_time`; the default mode ends a technical pause on its own after `matchzy_tech_pause_duration`.
- `.restorelast`, `.restorecurrent`, `.backups` menu, `!loadbackup` from chat.
- Restore rolls back round history, K/D/A, damage, score and MVPs.
- Optional auto-unpause after restore with a countdown.

### Coaching

- `coaches` list per team in the match config.
- Coach spot computed behind the team on any map (with line of sight and wall checks), or hand-tuned per map in game with `.savecoachspawn`.
- The coach never displaces a player from the competitive spawns, has no teammate color, is hidden from the kill feed, and never passes through Spectator.
- `.coach` only for your own team and only in warmup or freeze time; `matchzy_coach_enabled` to turn it off, `matchzy_coach_listed_only` to allow only listed coaches.

### Practice

- Grenade library: colored in-world markers with labels, ++e++ to load, ++f++ to cycle, shared server pack (`.libadd`), `.nades` menu.
- `.grt` rethrows every player's last grenade at once.
- `.arc`, `.landmarker`, `.autoclear`, `.cleanup`, `.flashtest`, `.blind`, `.wipe`.
- Named positions (`.savepos <name>`), named bot spots (`.savebotpos`), `.botjiggle`, `.nobot`.
- Interactive spawn markers (aim + ++e++).
- Joining a team from Spectator with `.t` / `.ct` (upstream still needs the team menu).

### Demos

- Recording verified by file growth, watched for the whole map, restarted if it stalls. Upstream logs a warning when no demo file is created.
- Dynamic CSTV disabled while recording.
- S3 presigned uploads (`matchzy_demo_upload_s3`).

### Events and stats

- Both versions send `player_death`, `bomb_planted`, `bomb_defused`, `round_start`, `game_paused`, `game_unpaused`, `backup_loaded` and `player_disconnect` in Get5's format.
- The fork adds `player_kill`, `player_hurt`, `bomb_exploded`, `bomb_pickup`, `bomb_dropped`, `grenade_thrown`, `grenade_detonated`, `player_blinded`, `freezetime_end`, `knife_won` and `match_cancelled`, plus `server_ready`, `map_change` and `server_shutdown` without a loaded match.
- `matchzy_events_format legacy` keeps the flat event format of fork 0.8.94 and older.
- A second auth header pair for the remote log.
- Advanced stats JSON per map: rating, KAST, ADR, opening duels, trades, clutches.
- Bot stats (`matchzy_stats_include_bots`).

### Admin and server

- `.ma` admin menu and `.matchsetup` wizard (optional CS2MenuManager).
- `.mhelp` shows each admin exactly the commands they can run.
- Coexists with CS2-SimpleAdmin and map plugins (`css_map` / `css_asay` yield, no double commands with a `.` chat trigger).
- The release zip contains nothing under `cfg/`. Missing cfgs are written on load, reference copies of the defaults go to `cfg/MatchZy/defaults/`, and new settings are appended to `config.cfg` without touching your edits. Upstream's zip still ships `config.cfg` and the mode cfgs.
- Optional `<mode>_override.cfg` for every mode (warmup, knife, live, live_wingman, scrim, hill, prac, dryrun, sleep). Upstream has `live_override.cfg` and `live_wingman_override.cfg`.
- `cfg/MatchZy` or `cfg/matchzy`, detected automatically.

## Upstream features not in this fork

| Upstream feature | In this fork |
|---|---|
| `.rcon` / `css_rcon` | Removed. Use your panel or real RCON. |
| `matchzy_demo_recording_enabled` | Not present. Recording follows `tv_enable`. |
| `matchzy_show_credits_on_match_start` | Not present. |
| `matchzy_tech_pause_flag` | Not present. |
| `matchzy_loadbackup_url` / `get5_loadbackup_url` | Not present. Load backups from a file with `matchzy_loadbackup`. |
| `.reload_admins` | Not present. `admins.json` is read when the plugin loads. |
| Admin flags per entry in `admins.json` | Every listed SteamID64 is a full admin. Use CounterStrikeSharp's admin system for limited admins. |
| 12 translations | English, Danish and Albanian. Other languages fall back to English. |
| Unit test suite | Not present. |
| Release zips bundled with CounterStrikeSharp | Plugin-only zip. Install CounterStrikeSharp separately. |

## Differences to be aware of

| Setting or behavior | Upstream 0.9.1 | This fork |
|---|---|---|
| Technical pause limits | `matchzy_max_tech_pauses` and `matchzy_tech_pause_time` (default 0, Get5 rules); `matchzy_max_tech_pauses_allowed` and `matchzy_tech_pause_duration` were removed | `matchzy_max_tech_pauses_allowed` (default 2) and `matchzy_tech_pause_duration` (default 300, the pause ends on its own). Upstream's `matchzy_max_tech_pauses` / `matchzy_tech_pause_time` and the `get5_` names are accepted too and switch to Get5 rules (`matchzy_tech_pause_mode 1`). |
| Ready-up time limit for the veto | `matchzy_time_to_start_veto`, default 0 (no limit) | Same command, stored as `matchzy_forfeit_veto_ready_timeout`, default `-1` (same as the map limit). |

Upstream documentation: <https://shobhit-pathak.github.io/MatchZy/>
