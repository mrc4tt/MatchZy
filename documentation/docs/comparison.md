# Fork vs upstream

This fork started from [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak and keeps its core: match configs, veto, knife round, ready system, pauses, practice mode, backups, demos, database stats and Get5 panel support. Everything that works upstream still works here, with the same command names and config keys.

The table compares with **upstream 0.8.15** (October 2025). Fork version numbers are independent of upstream.

Legend: <span class="mz-yes">✔</span> available, <span class="mz-plus">＋</span> extended in the fork, <span class="mz-no">–</span> not available.

## At a glance

| Area | Upstream | Fork |
|---|:---:|:---:|
| Match configs, veto, BO1/BO3/BO5, knife | <span class="mz-yes">✔</span> | <span class="mz-plus">＋</span> |
| Scrim and hill modes | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Players vs bots matches | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Coaches in the match config | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Coach viewing spots on every map | 8 fixed maps | <span class="mz-plus">＋</span> computed + 11 tuned |
| In-game admin menu and match wizard | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| HTML ready-up panel | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Auto-pause on disconnect | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Round restore rolls back scoreboard | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Backup list and restore after a crash | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Live scorebot events (kills, damage, bomb, nades) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Demo recording checks and restarts | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Demo upload to S3 (presigned PUT) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| HLTV-style rating, KAST, clutches, opening duels | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Practice: grenade library with in-world markers | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Practice: global rethrow, arcs, land markers, auto-clear | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Practice: named bot spots, bot jiggle | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Workshop maps by name (`ws:<name>`) | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Automatic map change after a match | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Config updates without overwriting admin edits | <span class="mz-no">–</span> | <span class="mz-yes">✔</span> |
| Runs on stock CounterStrikeSharp | <span class="mz-yes">✔</span> | <span class="mz-yes">✔</span> |
| .NET runtime | .NET 8 | .NET 10 |

## In detail

### Match flow

- **Scrim** (`.scrim`) and **hill** (`.hill`) modes: every round is played, no knife; hill is full-buy.
- **Players vs bots**: `"players": "any"` on one team, `"bots": true` on the other. Bots hold their side through halftime, overtime and restores, and are recorded in stats.
- **Workshop maps** in match configs and `.map`: `workshop/<id>`, `ws/<id>`, and `ws:<name>` from the hosted collection.
- **Stop anywhere**: `.stopmatch` works in setup, veto, warmup, knife and live, and stopped matches get an end time in the database.
- **Auto changelevel** after a match (`matchzy_match_end_auto_changelevel`), off automatically for panel-loaded matches.
- **Surrender vote** (`!gg`) when 6+ rounds behind.

### Ready and warmup

- HTML ready-up panel with progress bar, team split and per-player status (`matchzy_ready_hint_style 1`).
- Ping to ready, `.readycheck`, `[READY]` clan tags, coaches excluded from the count.
- `.warmupbots` to warm up aim while waiting.
- `matchzy_warmup_enabled` to skip warmup entirely.

### Pauses and backups

- Auto-pause when a team drops below N players, auto-resume when full.
- `.restorelast`, `.restorecurrent`, `.backups` menu, `!loadbackup` from chat.
- Restore rolls back round history, K/D/A, damage, score and MVPs.
- Optional auto-unpause after restore with a countdown.
- A restore is only announced once it actually loaded.

### Coaching

- `coaches` list per team in the match config.
- Coach spot computed behind the team on any map (with line of sight and wall checks), or hand-tuned per map in game with `.savecoachspawn`.
- The coach never displaces a player from the competitive spawns, has no teammate color, is hidden from the kill feed, and never passes through Spectator.
- `.coach` only for your own team; `matchzy_coach_enabled` to turn it off.

### Practice

- Grenade library: colored in-world markers with labels, ++e++ to load, ++f++ to cycle, shared server pack (`.libadd`), `.nades` menu.
- `.grt` rethrows every player's last grenade at once.
- `.arc`, `.landmarker`, `.autoclear`, `.cleanup`, `.flashtest`, `.blind`, `.wipe`.
- Named positions (`.savepos <name>`), named bot spots (`.savebotpos`), `.botjiggle`, `.nobot`.
- Interactive spawn markers (aim + ++e++).
- Crash-safe team switching and spectator-to-team joins.
- Practice bots respect `-nobots` and never take the CSTV slot.

### Demos

- Recording verified by file growth, watched for the whole map, restarted if it stalls.
- Crash-safe demos (`tv_record_immediate`), dynamic CSTV disabled while recording.
- S3 presigned uploads (`matchzy_demo_upload_s3`).

### Events and stats

- Live events: `player_death`, `player_hurt`, `bomb_*`, `grenade_thrown`, `grenade_detonated`, `player_blinded`, `round_start`, `freezetime_end`, `match_paused`, `match_unpaused`, `match_cancelled`.
- A second auth header pair for the remote log.
- Advanced stats JSON per map: rating, KAST, ADR, opening duels, trades, clutches.
- Bot stats (`matchzy_stats_include_bots`).

### Admin and server

- `.ma` admin menu and `.matchsetup` wizard (optional CS2MenuManager).
- `.mhelp` shows each admin exactly the commands they can run.
- Coexists with CS2-SimpleAdmin and map plugins (`css_map` / `css_asay` yield, no double commands with a `.` chat trigger).
- `config.cfg` gets new settings appended on update, never overwritten.
- `cfg/MatchZy` or `cfg/matchzy`, detected automatically.

## Differences to be aware of

| Upstream feature | In this fork |
|---|---|
| `.rcon` / `css_rcon` | Removed. Use your panel or real RCON. |
| `matchzy_demo_recording_enabled` | Not present. Recording follows `tv_enable`. |
| `matchzy_show_credits_on_match_start` | Not present. |
| `matchzy_tech_pause_flag` | Not present. |
| `live_override.cfg` / `live_wingman_override.cfg` | Not used. Edit `live.cfg` / `live_wingman.cfg` directly. |
| 12 translations | English, Danish and Albanian. Other languages fall back to English. |

Upstream documentation: <https://shobhit-pathak.github.io/MatchZy/>
