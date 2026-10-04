# What's new

A short summary of each release. The [full changelog](changelog.md) has the details and background for every change.

## 1.0.1 <small>October 4, 2026</small>

- **New:** `matchzy_team_clantag_enabled` shows each team's `tag` from the match config as the clan tag while live. Off by default.
- **Fixed:** No `[UNREADY]` clan tags in join ready mode.

## 1.0.0 <small>October 3, 2026</small>

- **New:** `player_connect` and `player_say` events (Get5 and legacy format). `player_say` covers chat commands too.
- **Changed:** `{TEAM1}` / `{TEAM2}` in the hostname and the match start message keep spaces; demo file names still use `_`.
- **Fixed:** Coaches are no longer listed as not ready.

## 0.8.99 <small>October 3, 2026</small>

- **New:** `.friendlyfire` / `.ff` (outside practice) turns friendly fire on or off before a match, scrim or hill starts, like `.knife`. In practice `.ff` is still fast-forward.

## 0.8.98 <small>October 3, 2026</small>

- **New:** `matchzy_empty_shutdown_seconds` closes a server that has had no players for the set time, after sending `server_shutdown` with `reason` `empty`. It waits for demo and backup uploads. Off by default.

## 0.8.97 <small>October 3, 2026</small>

- **New:** `matchzy_max_tech_pauses`, `matchzy_tech_pause_time` and `get5_allow_technical_pause` as alternative names for the technical pause settings.

## 0.8.96 <small>October 3, 2026</small>

- **New:** `.addreadytime <seconds>`, `get5_time_to_start` / `get5_time_to_start_veto` aliases and a separate veto ready-up limit (`matchzy_forfeit_veto_ready_timeout`).
- **New:** Join ready mode (`matchzy_ready_mode 1`): teams are ready once enough players have joined; the match starts after `matchzy_join_start_delay` seconds.
- **New:** Get5 technical pause rules (`matchzy_tech_pause_mode 1`, or `get5_max_tech_pauses` / `get5_tech_pause_time`): cancel before freeze time, either team can unpause once the time is up.
- **Changed:** Neither team ready in time ends the series in a tie instead of cancelling it.
- **Changed:** Technical pauses used are saved in round backups.

## 0.8.95 <small>October 3, 2026</small>

- **Changed:** Live events use Get5's event format by default (`matchzy_events_format get5`): nested player and weapon objects, `round_time`, `game_paused` / `game_unpaused`. Set `matchzy_events_format legacy` if your receiver reads the old flat fields. See [Events](reference/events.md#live-scorebot).
- **New:** Optional `<mode>_override.cfg` files (e.g. `warmup_override.cfg`) run right after their mode cfg. MatchZy never writes them, so your own settings survive every update. See [Server-specific settings](reference/files.md#server-specific-settings).
- **New:** `mikzy_version` / `css_mikzy_version` aliases for `matchzy_version`.
- **Changed:** Default warmup.cfg is back to normal warmup values (no spawn protection, no purchase limit, flashbangs allowed). Existing files are not rewritten; put the 0.8.94 values in `warmup_override.cfg` to keep them.
- **Fixed:** Coach scoreboard tags were replaced by `[UNREADY]` or cleared.
- **Changed:** Practice commands outside practice now say "Practice mode is not active!" instead of nothing.
- **Changed:** Less per-tick work during the ready phase and while practicing.
- **Fixed:** `.forceready` with `matchzy_ready_per_team` readies a team with fewer players than required.
- **Fixed:** `.readycheck` and the `matchzy_ready_hint_style 2` chat reminder respect `matchzy_ready_per_team`.
- **Fixed:** Dry run and practice started from warmup no longer keep the warmup purchase limit and spawn protection (update your own dryrun.cfg / prac.cfg, see the changelog).
- **Fixed:** Round timer hidden in live play when a live, scrim or hill cfg file was missing.
- **Fixed:** A `map_change` event with an empty map name was sent at every server start.
- **Fixed:** Stall on the first `.rt` / `.throw` after a server start.

## 0.8.94 <small>October 2, 2026</small>

- **New:** `matchzy_ready_per_team`: a team is ready once that many of its players are ready (`1` = one player per team). Works for pugs and loaded matches. See [Ready system](guides/ready-system.md).
- **Changed:** `matchzy_ready_hint_style 2` removes the native "Warmup" label, like style 1, without the HTML panel.
- **Changed:** warmup.cfg: spawn protection (`mp_respawn_immunitytime 5`), no purchase limit per weapon, no flashbangs, top timer hidden. The live configs reset all of it. Existing cfg files are not rewritten: compare with `defaults/`.
- **Changed:** `matchzy_minimum_ready_required` defaults to 10 again.
- **Changed:** Reference copies of the default cfgs moved to `cfg/MatchZy/defaults/`, written by MatchZy on every load, with a README. See [Updating](getting-started/updating.md).
- **Fixed:** Server stall on the first `.rt` / `.throw` of each grenade type in practice.
- **Fixed:** `Global Variables not initialized yet` error and repeated `[AutoStart] skipped` lines at server start; shorter stall when config.cfg loads.

## 0.8.93 <small>October 1, 2026</small>

- **New:** Remote log events `server_ready`, `map_change` and `server_shutdown`, also without a loaded match. See [Events](reference/events.md).
- **Security:** Match config `cvars` only accept real convars and MatchZy/Get5 settings; action commands and `matchzy_everyone_is_admin` are ignored.
- **Security:** Team names from round backups are cleaned.
- **Fixed:** `1` / `0` for `skip_veto`, `clinch_series` and `wingman` in match configs.
- **Fixed:** `get5_endmatch` after a series ended sent a second `series_end` and overwrote the winner.
- **Fixed:** Kicking bots could also kick the CSTV bot.
- **Fixed:** A refused round restore changed the running match.
- **Fixed:** Chat commands with arguments match the exact word (`.mapx` no longer runs `.map`).
- **Fixed:** Drawn maps counted when deciding whether a series is clinched.
- **Fixed:** `.forceend team1|team2` sends `map_result` for the live map.
- **Fixed:** Veto hung when a captain changed team.
- **Fixed:** MatchZy settings from match config `cvars` restored at series end; convars restored as `1`/`0`.
- **Fixed:** Practice lineups on servers with a `,` decimal separator.
- **Fixed:** `.last` / `.loadpos` / `.loadnade` moved dead players and spectators; `.fas` added deaths; ending practice during `.ff`.
- **Fixed:** Team names with spaces cut off on the scoreboard.
- **Fixed:** Demo and round backup paths on Windows servers.
- **Changed:** SQLite uses WAL mode (back up `matchzy.db` with its `-wal` / `-shm` files).
- **Fixed:** A restore queued in warmup announced itself as loaded.
- **Changed:** The release zip contains nothing under `cfg/`; updates never touch your config files. Default cfgs for reference are in `plugins/MatchZy/defaults/`. See [Updating](getting-started/updating.md).
- **Changed:** `admins.json` entries are always full admins; the value is no longer read as flags.
- **Fixed:** A remote log URL from a match config's `cvars` stayed active for later matches.
- **Fixed:** Reusing a `matchid` left the match marked as finished in the database.
- **Fixed:** Several server-crash risks around team switches, spectators and removing dropped weapons.
- **Fixed:** `.unpause` could not end an auto-pause while a team was short.
- **Fixed:** Restores queued in warmup recorded no demo and turned scrims into matches.
- **Changed:** A map change from outside MatchZy during a match sends `match_cancelled` (`map_changed`).
- **Fixed:** Forfeit, `.ffw` and `.gg` now stop the demo, send `map_result` and write the real score.
- **Fixed:** `.ln` and the nades menu loaded the wrong lineup; `.delbotpos` deleted the wrong position after a typo.
- **Performance:** Less work per round in coach matches, per grenade with `.autoclear`, and on bomb events.
- **Added:** `matchzy_ready_hint_style 2`: no center hint, a chat reminder of who is not ready.
- **Changed:** `.coach` only in warmup and freeze time.
- **Changed:** `.rs`, `.grt` and `.fas` need admin.
- **Fixed:** Live events could arrive out of order.
- **Fixed:** Advanced stats counted restored rounds twice.
- **Changed:** About 250 more chat messages translated (Danish, Albanian); players get their own language.

## 0.8.92 <small>October 1, 2026</small>

- **Fixed:** Round backup files written to `csgo/addons/metamod/` on Metamod servers, leaving `.stop` and `.restore` with nothing to load.

## 0.8.91 <small>September 29, 2026</small>

- **Added:** `matchzy_coach_listed_only` (default false): only coaches listed in the match config may coach.

## 0.8.90 <small>September 29, 2026</small>

- **Fixed:** Server crash right after a player connected to a loaded match (automatic team placement on connect removed; players pick their team from the menu again, locked to their roster side).
- **Added:** `matchzy_loaded_match_hide_mode_hints` (default true): only the ready-up hint is shown to joining players in a loaded match.

## 0.8.89 <small>September 29, 2026</small>

- **Fixed:** Teams get a tactical timeout in overtime (live.cfg: `mp_team_timeout_ot_add_once 1`, `mp_team_timeout_ot_max 5`; `mp_team_timeout_max 4`).
- **Added:** `matchzy_overtime_pauses_per_team` (default 1): `.pause` uses per team per overtime period.
- **Fixed:** `.pause` refused when tech pauses were disabled.

## 0.8.88 <small>September 29, 2026</small>

- **Added:** Documentation site.
- **Changed:** New config.cfg defaults: `matchzy_minimum_ready_required 2`, `matchzy_chat_messages_timer_delay 21`. Existing config.cfg files keep their values.
- **Changed:** On/off settings accept `1` / `0` as well as `true` / `false`.
- **Fixed:** `.t` / `.ct` from spectator in practice (gamedata key name).
- **Fixed:** Team `"players"` as an array of SteamID64s in match configs.
- **Fixed:** Remote log URL from config.cfg lost when a match was loaded.
- **Fixed:** Dot forms `.skipveto`, `.rmap`, `.surrender`, `.configs`, `.kniferound`, `.autopause`, `.listbackups`, and `.version` in chat.
- **Fixed:** Empty `matchzy_admin_chat_prefix` resetting the wrong prefix.
- **Fixed:** A loaded match (series map 2, the map picked in the veto) being wiped by its own map change.
- **Fixed:** Warmup never ending after a veto.
- **Fixed:** Finished series announced as a tie; wrong winner side/team in `map_result`, `series_end` and `round_end`.
- **Fixed:** Auto-pause not triggering when a player leaves; tech pause limit and duration not enforced; `matchzy_allow_unpause` ignored.
- **Fixed:** Coaches showing up in stats, damage reports and events, and taking the opening duel of every round.
- **Added:** `player_kill` event for every kill: killer, victim, weapon, headshot, wallbang, distance, flash assist, opening kill, trade kill, and the killer's kills this round and map.
- **Added:** Stats events fill KAST, trades, first kills/deaths per side, flash assists, team kills, knife kills, bomb plants/defuses. New `player_disconnect` event.
- **Fixed:** Demo recordings: no overwrite on restart (`_part2`), all parts uploaded, failed demos not reported.
- **Fixed:** `.rr` / admin menu "Restart Round" in a match, `.forceend` vs `!forceend`, `.matchsetup` lock, `.stop` in round 1 of scrim/hill.
- **Fixed:** Knife round played although `map_sides` fixed the side.
- **Added:** Players are placed on their team on connect; non-roster players watch; `players_per_team` limits the players per side.
- **Changed:** `get5_endmatch` follows Get5 (cancel, or `team1`/`team2` wins). `skip_veto` defaults to false; `side_type` and `veto_first` are read.
- **Fixed:** Coaching another team, veto hanging when a captain leaves, remote backup upload, `!gg` threshold, event order and `get5_status` fields.
- **Added:** Tournament timeouts: `matchzy_forfeit_ready_timeout`, `matchzy_forfeit_leave_timeout`, `matchzy_veto_step_timeout` (all off by default). `knife_won` event, `1k` stat.
- **Fixed:** MatchZy settings from a match config's `cvars` are restored after the series.
- **Security:** Match config `cvars` and map names can no longer run console commands; admins.json flags are honored; secrets are kept out of the log.
- **Fixed:** Several practice issues (`.ff`, `.timer`, `.back`, spawn markers, corrupt JSON files, `.delay`, bot kicks).

## 0.8.87 <small>September 28, 2026</small>

- **Added:** `"coaches"` list per team in the match config. Listed coaches can join, are not kicked, and become coach automatically.
- **Changed:** Coaches no longer count toward `players_per_team` and do not need to `.ready`.

## 0.8.86 <small>September 28, 2026</small>

- **Added:** `matchzy_coach_enabled` to turn coaching off.
- **Added:** Live events `grenade_thrown`, `grenade_detonated`, `player_blinded`, `bomb_pickup`, `bomb_dropped`, `bomb_exploded`.
- **Changed:** `.coach` only works for the team you are on.
- **Fixed:** `bomb_planted` / `bomb_defused` report the correct site.

## 0.8.85 <small>September 27, 2026</small>

- **Added:** Players vs bots matches (`"players": "any"` and `"bots": true` in the match config).
- **Added:** `matchzy_stats_include_bots` to record bot stats.
- **Removed:** `matchzy_coach_debug`.
- **Fixed:** `.rethrow` spawning nothing after some throws.

## 0.8.83 <small>September 16, 2026</small>

- **Changed:** Less work on the game thread at round start and during practice rethrows (fewer hitches).
- **Changed:** Round backups are written in the background.

## 0.8.82 <small>September 15, 2026</small>

- **Fixed:** Practice bot commands and `.warmupbots` respect `-nobots` (no more invisible bots).
- **Fixed:** `.map` ignored after a map change.
- **Fixed:** `-nohltv` detection.

## 0.8.80 <small>September 4, 2026</small>

- **Changed:** Mode commands (`css_prac`, `css_match`, ...) remember the mode across map changes.
- **Changed:** Practice started on a fresh map starts right away, without a warmup countdown.
- **Changed:** Bullet impacts are on by default in practice.
- **Fixed:** Warmup starting on top of practice when loaded from a mode-switch script.

## 0.8.79 <small>September 2, 2026</small>

- **Changed:** `.mhelp` shows each admin the commands they can run, and the permission for the rest. Any admin can open it.

## 0.8.78 <small>September 1, 2026</small>

- **Added:** Workshop maps by name with `ws:<name>`.
- **Fixed:** `workshop/<id>` map formats in match configs and veto.

## 0.8.77 <small>August 25, 2026</small>

- **Changed:** Runs on stock CounterStrikeSharp as well as the fork.
- **Changed:** `gamedata/matchzy.json` includes the weapon-select offset.

## 0.8.76 <small>August 17, 2026</small>

- **Fixed:** `.bot` refusing to spawn when CSTV is disabled.

## 0.8.75 <small>August 15, 2026</small>

- **Fixed:** `matchzy_kick_when_no_match_loaded` and the whitelist are enforced again.
- **Fixed:** A server crash when a player outside a set-up match joined a team.

## 0.8.74 <small>August 11, 2026</small>

- **Added:** `matchzy_auto_team_names_enabled`.
- **Changed:** Demo recording is verified by file growth and restarted if it stalls.
- **Changed:** `tv_enable_dynamic 0` while recording; practice bots never take the CSTV slot.
- **Fixed:** Molotov detonation times per grenade.

## 0.8.73 <small>August 6, 2026</small>

- **Changed:** `config.cfg` is no longer in the release zip, so updates never overwrite it. `database.json.example` is shipped instead.
- **Fixed:** Demos not recorded on `.match`, `.scrim` and `.hill`, or after another plugin changed map.
- **Fixed:** Several database issues: MySQL player stats, reused match ids, concurrent writes, multi-kill columns.
- **Fixed:** Stopped matches get an end time in the database.
- **Fixed:** `.restore` failing on a half-written round file.
- **Fixed:** Knife maps using the previous match's sides.

## 0.8.72 <small>August 5, 2026</small>

- **Added:** `matchzy_restore_auto_unpause`, `matchzy_restore_unpause_delay`, `matchzy_restore_scoreboard_stats`, `matchzy_dot_trigger_dedupe`.
- **Changed:** A restore rolls back the scoreboard and is only announced once it loaded.
- **Changed:** `.restorecurrent` / `!rr` no longer ask for confirmation.
- **Fixed:** Match stuck paused after a restore.
- **Fixed:** Commands running twice when `.` is a CounterStrikeSharp chat trigger.
- **Fixed:** Config files written to two folders (`MatchZy` and `matchzy`).

## 0.8.71 <small>August 1, 2026</small>

- **Added:** Demo upload after each map, S3 presigned upload (`matchzy_demo_upload_s3`), upload headers, `demo_upload_ended` event.

## 0.8.70 <small>July 31, 2026</small>

- **Fixed:** Match end data written twice at series end.

## 0.8.69 <small>July 30, 2026</small>

- **Fixed:** `.t` / `.ct` from spectator in practice kicking the player or leaving them dead.

## 0.8.68 <small>July 29, 2026</small>

- **Added:** `.backups` without a live match lists the newest backup files (restore after a crash). `!loadbackup` from chat.
- **Fixed:** `.botjiggle` hiding new bots; `.bot` sometimes spawning nothing.

## 0.8.67 <small>July 28, 2026</small>

- **Removed:** `matchzy_random_spawns`.
- **Changed:** Coaches never displace players from competitive spawns, have no teammate color and are hidden from the kill feed.
- **Changed:** `bot_quota 0` and `mp_randomspawn 0` in the mode cfgs.

## 0.8.66 <small>July 27, 2026</small>

- **Fixed:** Demos not recording on `.scrim` and `.hill`.

## 0.8.65 <small>July 27, 2026</small>

- **Fixed:** Coach killed after the round went live; `.watchme` / `.spec` failing to move players.

## 0.8.64 <small>July 24, 2026</small>

- **Removed:** Experimental `.jt` / `.jumpthrow`.
- **Fixed:** Crash when switching to the side you are already on; `.loadbotpos` tilted bots.

## 0.8.63 <small>July 24, 2026</small>

- **Fixed:** A match loaded by URL being lost on the map change to its first map.

## 0.8.62 <small>July 24, 2026</small>

- **Added:** Named bot positions (`.savebotpos`, `.loadbotpos`, `.listbotpos`, `.delbotpos`, `.showbotpos`) and `.botjiggle`.
- **Added:** Molotov burn time in chat.

## 0.8.61 <small>July 20, 2026</small>

- **Added:** `matchzy_coaching_mode`, `.showcoachspawns`, `.coachtest`.
- **Changed:** Coach viewing spots reworked for the whole map pool.
- **Fixed:** Crash from `.watchme` / `.fas`.

## 0.8.60 <small>July 19, 2026</small>

- **Fixed:** Coach falling out of the map on some maps.

## 0.8.59 <small>July 19, 2026</small>

- **Added:** Grenade library: `.shownades`, `.hidenades`, shared pack (`.libadd`, `.libremove`, `.liblist`), `.nades` menu, throw styles on `.savenade`, `.ln #N`.
- **Added:** `.warmupbots`, BO2 in `.matchsetup`.
- **Changed:** Dryrun keeps playing until `.exitdry`.
- **Removed:** Experimental `.predict`.

## 0.8.58 <small>July 18, 2026</small>

- **Added:** `.matchstop` alias.
- **Fixed:** Dot commands during match setup; `.stopmatch` before the match is live.

## 0.8.57 <small>July 17, 2026</small>

- **Changed:** Practice turns off team-damage kicks.
- **Changed:** `matchzy_ready_hint_style` controls the whole ready display.
- **Fixed:** `.bot` spawning two bots; ghost bodies after a disconnect in practice.

## 0.8.56 <small>July 16, 2026</small>

- **Added:** Named positions, `.flashtest`, `.blind`, `.wipe`, `.cleanup`, `.autoclear`, `.landmarker`, `.arc`, `.mynades`, `matchzy_ready_up_by_ping`.
- **Changed:** `.delnade` deletes several lineups or `all`.
- **Changed:** Retired settings are removed from `config.cfg` on load.

## 0.8.55 <small>July 16, 2026</small>

- **Added:** Interactive spawn markers (aim + E to teleport).
- **Changed:** `.back` steps through history like CS:GO.

## 0.8.54 <small>July 16, 2026</small>

- **Added:** `.grt` global rethrow; incendiary rethrow.
- **Fixed:** Rethrow for smoke, HE, molotov and decoy; grenade spin on rethrow.

## 0.8.53 <small>July 15, 2026</small>

- **Added:** HTML ready-up panel, `matchzy_ready_clantag_enabled`.
- **Changed:** MatchZy ships its own `gamedata/matchzy.json`.
- **Changed:** Yields `css_map` to CS2-SimpleAdmin / CS2MapChange.

## 0.8.52 <small>July 15, 2026</small>

- **Added:** `.map` command with name resolution and workshop ids.
- **Changed:** Config folder name is detected (`MatchZy` or `matchzy`); `admins.json` is loaded.
- **Removed:** End-of-match summary panel.

Older releases: see the [full changelog](changelog.md).
