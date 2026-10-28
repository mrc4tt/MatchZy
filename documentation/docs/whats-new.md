# What's new

A short summary of each release. The [full changelog](changelog.md) has the details and background for every change.

## 1.0.21 <small>October 27, 2026</small>

- **New:** `matchzy_warmup_instant_respawn true` gives a warmup with instant respawn at the team spawn, 2 seconds of spawn protection, no bomb, no friendly fire, buying all warmup and full money on every respawn. Off by default.
- **New:** `matchzy_ready_max_missing` sets the fewest players a team can ready up with in a loaded match (`1` = at least 4 in a 5v5), however it readies up. Off by default.
- **New:** Chat translations in German, Spanish, French, Norwegian, Polish, Russian and Swedish (`!lang <code>`).
- **Changed:** A server started without `-allow_round_backup_files` warns admins in chat when a match is loaded and when it goes live, so missing round backups are noticed before a `.restore` is needed.
- **Fixed:** With `matchzy_ready_hint_style 1` or `2`, players who died in warmup sometimes only respawned when the round ended (after a match was loaded during warmup, a veto or a map reload).
- **Fixed:** Ready reminders no longer suggest every player has to type `.ready` when the match needs fewer: ready teams drop out of the waiting list, and the counts show what the start needs.
- **Fixed:** Warmup, the knife round, scrims and live matches always turn off knife dropping (`mp_drop_knife_enable 0`), and warmup and the knife round no longer hand out free defuse kits (`mp_defuser_allocation 0`). A value set by `server.cfg` or another plugin no longer carries into these modes.
- **Fixed:** A pause called in the final round of a series (also an auto-pause after a disconnect) is cleared when the map ends instead of staying queued through the intermission.

## 1.0.20 <small>October 24, 2026</small>

- **Fixed:** A match config loaded during a `.start` pug no longer reuses the pug's match id (stats, events, demo name and backups went to the pug).
- **Changed:** `.showspawns`: E on a spawn marker picks the marker you aim at more precisely, also at a distance and while crouched.

## 1.0.19 <small>October 24, 2026</small>

- **Fixed:** GOTV demos are recorded again on Windows servers. The current CS2 build rejected the demo path MatchZy sent (`couldn't open file ... for writing`), so no demo was saved.

## 1.0.18 <small>October 24, 2026</small>

- **New:** `matchzy_ready_enabled false` turns off the player ready-up (`.ready`, `.forceready`, ping, join ready mode). Only an admin starts the match with `.start`, and the ready time limit does not run.
- **New:** `matchzy_hide_failed_connect_messages true` hides the chat line for players who leave before they were fully connected ("failed to connect").

## 1.0.17 <small>October 24, 2026</small>

- **Fixed:** `matchzy_remote_log_url ""` clears the remote log URL and stops sending events (also the header and auth settings). Before, the old webhook kept receiving events.
- **Changed:** A remote log setting run without a value prints what it is set to (header and auth values masked).
- **Changed:** `matchzy_loadmatch_url` tries the match config download up to 3 times on timeouts, network errors and HTTP 5xx/408/429.

## Older releases

??? note "1.0.16 - October 24, 2026"

    - **New:** `.ffire nades` and `"friendly_fire": "grenades"` in the match config: only HE and molotov/incendiary damage teammates. `.ffire on` / `.ffire off` and `"friendly_fire": true` / `false` also work.
    - **Fixed:** Demo uploads no longer fail for team names with letters outside ASCII (for example `Ærø`).
    - **Fixed:** A pause asked for during the knife round or side selection starts in the first live round.
    - **Fixed:** In a series, a `mp_match_restart_delay` below 18 in the match config no longer lets the game load another map before the next map of the series.
    - **Fixed:** The hostname set by `matchzy_hostname_format` is put back to the one from before the match when the series ends or the match is stopped (with `matchzy_reset_cvars_on_series_end`).

??? note "1.0.15 - October 23, 2026"

    - **Fixed:** The ready panel no longer shows more ready players on a side than it needs (for example `CT 3/1` with `"min_players_to_ready": 1`).

??? note "1.0.14 - October 23, 2026"

    !!! warning "Action needed"
        - Check the `min_players_to_ready` your match panel sends. Many send `1`, which now starts a match as soon as one player on each team is ready. Send `players_per_team` (for example `5`) or leave it out to keep the full ready-up.

    - **Changed:** `min_players_to_ready` in the match config sets how many players of each team must type `.ready`. With `1`, one ready player on CT and one on T start the match. Before, it only applied to `.forceready`.

??? note "1.0.13 - October 23, 2026"

    !!! warning "Action needed"
        - Existing servers keep their old `live_wingman.cfg`, `dryrun.cfg` and `sleep.cfg`. Delete them from `cfg/MatchZy/` (or put your values in the matching `_override.cfg`) to get the new overtime, short-handed money and CSTV voice defaults. The new wingman and rush timeouts apply without this.

    - **Changed:** Wingman and rush give each team 1 tactical timeout of 60 seconds and 1 more in each overtime. Before, they kept the timeouts of the mode that ran before.
    - **Changed:** Wingman overtime has 6 rounds (was 4), there is no short-handed money bonus, and team voice is recorded in the CSTV demo (`tv_relayvoice 1`).
    - **Fixed:** When `live.cfg` or `live_wingman.cfg` is missing on disk, the match config's `cvars` (for example `mp_team_timeout_max`) are applied before the game restarts, so they count from round 1.

??? note "1.0.12 - October 21, 2026"

    - **Fixed:** An observer or caster who joined before the players no longer takes a player spawn. They were put on a team for a moment, and the spawn they used stayed blocked after they moved to Spectator, so a player spawned on someone else or not at all. Also applies to `.match` and `.scrim` warmups.

??? note "1.0.11 - October 17, 2026"

    - **Fixed:** a match config with `game_mode` set to `rush`, `1v1` or `wingman` always goes live with that mode's cfg (`live_rush.cfg`, `live_1v1.cfg`, `live_wingman.cfg`), even when the server's `game_mode` had not changed yet. Before, such matches could go live with `live.cfg`.
    - **Changed:** a config with `"wingman": true` and `"players_per_team": 1` and no `game_mode` is played as 1v1.

??? note "1.0.10 - October 17, 2026"

    !!! warning "Action needed"
        - Existing servers keep their old `live_1v1.cfg`. Delete `cfg/MatchZy/live_1v1.cfg` (or put your values in `live_1v1_override.cfg`) to get the new 1v1 defaults.

    - **New:** `.1v1`, `.wingman` and `.5v5` switch the server's game mode and reload the map (or `.wingman <map>`). 1v1 now runs in the wingman game mode.
    - **New:** `.shownades` lineups glow through walls; `.shownades smoke` (or `flash`, `mine`, a name...) filters them.
    - **New:** Strategy recorder: `.stratrec` records your team's execute, `.strat` plays it back with bots (`me <role>` to play one role yourself). `.createstrat` + `.stratslot` build one alone, slot by slot, and `.strats` is the menu for it. `.record` / `.play` with a 3 second countdown; `.stoprecord`, a double right click or Shift + E stops a recording.
    - **New:** Practice: `.kevlar`, `.armor`, `.botgun`, `.dismiss` and `.runboost` on the bot at your aim, `.dismissall`, `.xray`, `.expire` (smokes and molotovs never run out), `.coloredsmokes`, and `.observeme` for `.fas`.
    - **Changed:** `live_1v1.cfg` uses the wingman format: MR8 (first to 9), $8000 max money, 1:30 rounds, overtime MR2.
    - **Changed:** A pug in 1v1, wingman or rush starts with 2, 4 or 6 ready players without `.readyrequired`.
    - **Fixed:** Round backups and `.restore` work again after the CS2 update of October 15, 2026 (CS2 rejected the backup path MatchZy used). `-allow_round_backup_files` is still required.

??? note "1.0.9 - October 16, 2026"

    !!! warning "Action needed"
        - CS2 needs the launch option `-allow_round_backup_files` for round backups. Without it nothing is saved and every restore fails; MatchZy now warns about it in the log and in chat.

    - **New:** `.ma` → Match Control → Restore Round: pick a round backup from a list, with confirmation. Without a live match it lists the newest backups on disk for crash recovery.
    - **New:** Rush (3v3) and 1v1 matches. Set `"game_mode": "rush"` or `"1v1"` in the match config, or pick the mode in `.matchsetup`. Rush uses the new `live_rush.cfg` (Valve's rush rules), 1v1 the new `live_1v1.cfg`.
    - **New:** A match config with only rush maps (`rush_001`, `aim_rush`, ...) plays as rush automatically.
    - **New:** `matchzy_series_end_shutdown_seconds` closes the server a set time after a series ends (also after a forfeit), after sending `server_shutdown` with `reason` `series_end`. Players can no longer keep playing an unreported match after a forfeit. Off by default.
    - **New:** `matchzy_forfeit_join_grace_seconds` (default 60) gives a registered player who joins right before the ready time runs out a chance to ready up before their team forfeits.
    - **New:** `.botshoot` makes practice bots shoot back from their spot after `.botreactiontime`; smokes block their view. `.botrespawn` and `.botlifereg` control respawning and healing of bots.
    - **New:** `.startround` starts a fresh practice rep: utility cleared, random spawns for everyone, bots reset, 5 second freeze.
    - **New:** Bot spawn pools: `.botspawn <name>` collects spots, `.placebot <number> <name>` places bots on random ones. `.showbotspawn` shows them (stand in one + ++e++ deletes it).
    - **New:** `.liferegon` / `.allliferegon` to turn health regeneration off and actually die in practice, `.slp` / `.tlp` / `.dlp` for a saved location you also respawn at, `.kicklastbot`, `.botjigglerange`, `.botjigglerandom`, and `matchzy_spawn_markers_enabled_default`.
    - **New:** `.rec` records your movement and grenade throws in practice, `.replay` lets a bot walk it and throw the same grenades. The bot crouches where you crouched and holds the grenade before each throw. `.replay loop` repeats the path, `.saverec` / `.loadrec` / `.listrec` / `.delrec` keep paths per map. `.showrec` draws your path and throw spots on the map, only for you.
    - **Changed:** Without `players_per_team`, the team size follows the mode (1v1: 1, wingman: 2, rush: 3, competitive: 5).
    - **Changed:** With `matchzy_kick_when_no_match_loaded` on, the players left on the server after a series ends (also after a forfeit) are kicked, unless a new match is loaded within the reset delay. Admins stay. `get5_endmatch` does not kick, as in Get5.
    - **Changed:** The ready time reminders name the players a team is still missing, and `.tech` / `.pause` / `.tac` in the ready phase say that they do not stop the ready time.
    - **Changed:** Delivery of the result events (`series_end`, `map_result`, ...) is logged with match id and HTTP status.
    - **Changed:** `.showspawns` draws a numbered box on the floor at each spawn. Aim at a box (it turns green) or stand inside one and press ++e++ to teleport exactly onto that spawn. Bind a spawn to a key with `bind F1 "css_spawn 1"`.
    - **Changed:** The `.tac` hint at LIVE shows the server's real timeout settings (`mp_team_timeout_max` x `mp_team_timeout_time`) instead of a fixed "4 x 30 seconds", and `.tac` tells the chat how many timeouts the team has left.
    - **Changed:** `.help` before going live lists the player commands (`.ready`, `.unready`, `.coach`, `.tac`, `.tech`) and shows admin commands (`.start`, `.knife`, `.scrim` / `.playout`, `.endmatch`) only to admins. Live `.help` shows your team's tactical timeouts left.
    - **Changed:** The CSTV demo starts about a second after LIVE instead of about 8 seconds later. Chat confirms the recording sooner, and says when it is finished, with the demo file name.
    - **Changed:** `.clear` / `.cleanup` in practice also remove blood and bullet holes.
    - **Changed:** On load, MatchZy removes settings it no longer has from your `config.cfg`, so they stop printing "Unknown command". Only the setting line goes; comments and commented-out settings stay.
    - **Changed:** `.arc` uses far fewer beams (max 32 per arc, 256 in total), which is lighter on every player's FPS during practice.
    - **Changed:** `.shownades` is per player: only players who turn it on see the markers, and labels show only when you are near.
    - **Changed:** The ready-phase timer (`matchzy_ready_hint_style` 1) updates the game rules about twice a second instead of every tick.
    - **Changed:** Practice turns off cash awards, so a kicked practice bot no longer prints "+$300 ... compensation for the suicide of".
    - **Fixed:** `css_knife` (also `css_rk`, `css_kr`, `css_kniferound`, `css_roundknife`) now works from the server and panel console. Each call toggles the knife round.
    - **Fixed:** Going live with bots on the server no longer kicks the CSTV bot, so the demo is recorded. With `tv_enable 1`, MatchZy checks that the CSTV bot is on the server before `tv_record` and restarts it if it was kicked (needs `tv_enable_dynamic 1`); a kick during the match starts a new demo file.
    - **Fixed:** Possible server crash on the map change between series maps when the server's cfgs restored `bot_quota` during the level load. MatchZy now sets `bot_quota 0` in `csgo/cfg/gamemode_competitive_server.cfg` (and the wingman `gamemode_competitive2v2_server.cfg`).
    - **Fixed:** The coach kill no longer carries over to the next map.
    - **Fixed:** No player stats in a game started without a loaded match while `matchzy_kick_when_no_match_loaded` was on.
    - **Fixed:** Dryrun rounds use the normal competitive spawns, like a match round, instead of random spawn points across the map.
    - **Fixed:** `.nobot` in practice no longer starts an endless bot join/kick loop.
    - **Fixed:** `.throw` / `.rethrow` (`.rt`) now land a grenade thrown while running or jumping on the same spot as the original throw.
    - **Fixed:** Demos and round backups are written again on Windows servers (the engine rejected the full `D:/...` path: `invalid path`).
    - **Fixed:** The bomb no longer drops outside the map when a Terrorist coach is given it. It is passed to a player before the coach is killed at the end of freeze time.
    - **Fixed:** A coach who picks up the bomb (dropped by a teammate in freeze time) passes it straight to a player, and a bomb a coach drops is given to a live Terrorist instead of lying at the coach spot.
    - **Fixed:** Loading MatchZy mid-map (`css_plugins load`) counts the players already on the server, so the ready-up works without reconnecting.
    - **Fixed:** Unloading MatchZy resets practice settings and removes its bots and markers, so the next mode starts clean.
    - **Fixed:** Unloading during the ready phase no longer leaves a paused warmup behind, and no longer freezes the server for up to 3 seconds.
    - **Fixed:** Round backups on servers where the game folder has another path outside the engine (for example in Docker): CS2 rejected the backup path and wrote no round files.
    - **Fixed:** `score_ct` / `score_t` on the teams of `round_end` and `map_result` were always 0. They now hold the rounds won on each side, and the teams also carry `side` and `starting_side`.

??? note "1.0.8 - October 6, 2026"

    - **Fixed:** The automatic coach kill at the end of freeze time no longer gives money to the other team.

??? note "1.0.7 - October 5, 2026"

    - **Changed:** `matchzy_ready_per_team` in a loaded match waits until each team's registered players are all in; one `.ready` no longer starts the match with players missing. `.forceready` still starts without a missing player.
    - **Changed:** The missing-player hint only says how many are missing; it no longer suggests `.forceready`.

??? note "1.0.6 - October 5, 2026"

    - **Changed:** No `.scrim` / `.prac` / `.knife` hints with `matchzy_kick_when_no_match_loaded` on.
    - **Changed:** Shorter console output on load, with the MatchZy version; a clear message when CS2MenuManager is missing.

??? note "1.0.5 - October 5, 2026"

    - **Changed:** The ready panel tells a short-handed team it is waiting for the missing player, or can `.forceready`; `.forceready` is optional.

??? note "1.0.4 - October 5, 2026"

    - **New:** 4v5: a team registered with fewer players than `players_per_team` readies up with that many, no `.forceready` needed. A short-handed team is told in chat and on the ready panel that `.forceready` starts without the missing player.
    - **New:** `matchzy_forceready_max_missing` (default `1`): `.forceready` works with at most one player short (4 in a 5v5), even when the match config says `min_players_to_ready 1`.
    - **Fixed:** A setting missing from an older mode cfg no longer keeps the previous mode's value. Each mode starts from MatchZy's built-in default; your cfgs, overrides and match config still win. Fixes no buying in the match after `mp_weapons_allow_typecount 0` in `warmup_override.cfg`.
    - **Fixed:** The ready panel in a loaded match counts towards what the teams need instead of `matchzy_minimum_ready_required`.

??? note "1.0.3 - October 4, 2026"

    - **Fixed:** A short-handed team (4v5) can start with `.forceready` when the match config has no `min_players_to_ready`. The default is now 1, as in Get5.

??? note "1.0.2 - October 4, 2026"

    - **Changed:** A `changelevel` / `map` for a workshop map from the console or another plugin is replaced with the workshop command, or blocked when the map cannot be loaded.
    - **Fixed:** Workshop servers (started with `+host_workshop_map` / `+host_workshop_collection`) no longer crash at match end. Every map change now uses the workshop commands for workshop maps.

??? note "1.0.1 - October 4, 2026"

    - **New:** `matchzy_team_clantag_enabled` shows each team's `tag` from the match config as the clan tag while live. Off by default.
    - **Fixed:** No `[UNREADY]` clan tags in join ready mode.

??? note "1.0.0 - October 3, 2026"

    - **New:** `player_connect` and `player_say` events (Get5 and legacy format). `player_say` covers chat commands too.
    - **Changed:** `{TEAM1}` / `{TEAM2}` in the hostname and the match start message keep spaces; demo file names still use `_`.
    - **Fixed:** Coaches are no longer listed as not ready.

??? note "0.8.99 - October 3, 2026"

    - **New:** `.friendlyfire` / `.ff` (outside practice) turns friendly fire on or off before a match, scrim or hill starts, like `.knife`. In practice `.ff` is still fast-forward.

??? note "0.8.98 - October 3, 2026"

    - **New:** `matchzy_empty_shutdown_seconds` closes a server that has had no players for the set time, after sending `server_shutdown` with `reason` `empty`. It waits for demo and backup uploads. Off by default.

??? note "0.8.97 - October 3, 2026"

    - **New:** `matchzy_max_tech_pauses`, `matchzy_tech_pause_time` and `get5_allow_technical_pause` as alternative names for the technical pause settings.

??? note "0.8.96 - October 3, 2026"

    - **New:** `.addreadytime <seconds>`, `get5_time_to_start` / `get5_time_to_start_veto` aliases and a separate veto ready-up limit (`matchzy_forfeit_veto_ready_timeout`).
    - **New:** Join ready mode (`matchzy_ready_mode 1`): teams are ready once enough players have joined; the match starts after `matchzy_join_start_delay` seconds.
    - **New:** Get5 technical pause rules (`matchzy_tech_pause_mode 1`, or `get5_max_tech_pauses` / `get5_tech_pause_time`): cancel before freeze time, either team can unpause once the time is up.
    - **Changed:** Neither team ready in time ends the series in a tie instead of cancelling it.
    - **Changed:** Technical pauses used are saved in round backups.

??? note "0.8.95 - October 3, 2026"

    - **New:** Optional `<mode>_override.cfg` files (e.g. `warmup_override.cfg`) run right after their mode cfg. MatchZy never writes them, so your own settings survive every update. See [Server-specific settings](reference/files.md#server-specific-settings).
    - **New:** `mikzy_version` / `css_mikzy_version` aliases for `matchzy_version`.
    - **Changed:** Live events use Get5's event format by default (`matchzy_events_format get5`): nested player and weapon objects, `round_time`, `game_paused` / `game_unpaused`. Set `matchzy_events_format legacy` if your receiver reads the old flat fields. See [Events](reference/events.md#live-scorebot).
    - **Changed:** Default warmup.cfg is back to normal warmup values (no spawn protection, no purchase limit, flashbangs allowed). Existing files are not rewritten; put the 0.8.94 values in `warmup_override.cfg` to keep them.
    - **Changed:** Practice commands outside practice now say "Practice mode is not active!" instead of nothing.
    - **Changed:** Less per-tick work during the ready phase and while practicing.
    - **Fixed:** Coach scoreboard tags were replaced by `[UNREADY]` or cleared.
    - **Fixed:** `.forceready` with `matchzy_ready_per_team` readies a team with fewer players than required.
    - **Fixed:** `.readycheck` and the `matchzy_ready_hint_style 2` chat reminder respect `matchzy_ready_per_team`.
    - **Fixed:** Dry run and practice started from warmup no longer keep the warmup purchase limit and spawn protection (update your own dryrun.cfg / prac.cfg, see the changelog).
    - **Fixed:** Round timer hidden in live play when a live, scrim or hill cfg file was missing.
    - **Fixed:** A `map_change` event with an empty map name was sent at every server start.
    - **Fixed:** Stall on the first `.rt` / `.throw` after a server start.

??? note "0.8.94 - October 2, 2026"

    - **New:** `matchzy_ready_per_team`: a team is ready once that many of its players are ready (`1` = one player per team). Works for pugs and loaded matches. See [Ready system](guides/ready-system.md).
    - **Changed:** `matchzy_ready_hint_style 2` removes the native "Warmup" label, like style 1, without the HTML panel.
    - **Changed:** Warmup.cfg: spawn protection (`mp_respawn_immunitytime 5`), `mp_weapons_allow_typecount 0` (no purchases, reverted in 0.8.95), no flashbangs, top timer hidden. The live configs reset all of it. Existing cfg files are not rewritten: compare with `defaults/`.
    - **Changed:** `matchzy_minimum_ready_required` defaults to 10 again.
    - **Changed:** Reference copies of the default cfgs moved to `cfg/MatchZy/defaults/`, written by MatchZy on every load, with a README. See [Updating](getting-started/updating.md).
    - **Fixed:** Server stall on the first `.rt` / `.throw` of each grenade type in practice.
    - **Fixed:** `Global Variables not initialized yet` error and repeated `[AutoStart] skipped` lines at server start; shorter stall when config.cfg loads.

??? note "0.8.93 - October 1, 2026"

    - **New:** Remote log events `server_ready`, `map_change` and `server_shutdown`, also without a loaded match. See [Events](reference/events.md).
    - **New:** `matchzy_ready_hint_style 2`: no center hint, a chat reminder of who is not ready.
    - **Changed:** SQLite uses WAL mode (back up `matchzy.db` with its `-wal` / `-shm` files).
    - **Changed:** The release zip contains nothing under `cfg/`; updates never touch your config files. Default cfgs for reference are in `plugins/MatchZy/defaults/`. See [Updating](getting-started/updating.md).
    - **Changed:** `admins.json` entries are always full admins; the value is no longer read as flags.
    - **Changed:** A map change from outside MatchZy during a match sends `match_cancelled` (`map_changed`).
    - **Changed:** Less work per round in coach matches, per grenade with `.autoclear`, and on bomb events.
    - **Changed:** `.coach` only in warmup and freeze time.
    - **Changed:** `.rs`, `.grt` and `.fas` need admin.
    - **Changed:** About 250 more chat messages translated (Danish, Albanian); players get their own language.
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
    - **Fixed:** A restore queued in warmup announced itself as loaded.
    - **Fixed:** A remote log URL from a match config's `cvars` stayed active for later matches.
    - **Fixed:** Reusing a `matchid` left the match marked as finished in the database.
    - **Fixed:** Several server-crash risks around team switches, spectators and removing dropped weapons.
    - **Fixed:** `.unpause` could not end an auto-pause while a team was short.
    - **Fixed:** Restores queued in warmup recorded no demo and turned scrims into matches.
    - **Fixed:** Forfeit, `.ffw` and `.gg` now stop the demo, send `map_result` and write the real score.
    - **Fixed:** `.ln` and the nades menu loaded the wrong lineup; `.delbotpos` deleted the wrong position after a typo.
    - **Fixed:** Live events could arrive out of order.
    - **Fixed:** Advanced stats counted restored rounds twice.
    - **Security:** Match config `cvars` only accept real convars and MatchZy/Get5 settings; action commands and `matchzy_everyone_is_admin` are ignored.
    - **Security:** Team names from round backups are cleaned.

??? note "0.8.92 - October 1, 2026"

    - **Fixed:** Round backup files written to `csgo/addons/metamod/` on Metamod servers, leaving `.stop` and `.restore` with nothing to load.

??? note "0.8.91 - September 29, 2026"

    - **New:** `matchzy_coach_listed_only` (default false): only coaches listed in the match config may coach.

??? note "0.8.90 - September 29, 2026"

    - **New:** `matchzy_loaded_match_hide_mode_hints` (default true): only the ready-up hint is shown to joining players in a loaded match.
    - **Fixed:** Server crash right after a player connected to a loaded match (automatic team placement on connect removed; players pick their team from the menu again, locked to their roster side).

??? note "0.8.89 - September 29, 2026"

    - **New:** `matchzy_overtime_pauses_per_team` (default 1): `.pause` uses per team per overtime period.
    - **Fixed:** Teams get a tactical timeout in overtime (live.cfg: `mp_team_timeout_ot_add_once 1`, `mp_team_timeout_ot_max 5`; `mp_team_timeout_max 4`).
    - **Fixed:** `.pause` refused when tech pauses were disabled.

??? note "0.8.88 - September 29, 2026"

    - **New:** Documentation site.
    - **New:** `player_kill` event for every kill: killer, victim, weapon, headshot, wallbang, distance, flash assist, opening kill, trade kill, and the killer's kills this round and map.
    - **New:** Stats events fill KAST, trades, first kills/deaths per side, flash assists, team kills, knife kills, bomb plants/defuses. New `player_disconnect` event.
    - **New:** Players are placed on their team on connect; non-roster players watch; `players_per_team` limits the players per side.
    - **New:** Tournament timeouts: `matchzy_forfeit_ready_timeout`, `matchzy_forfeit_leave_timeout`, `matchzy_veto_step_timeout` (all off by default). `knife_won` event, `1k` stat.
    - **Changed:** New config.cfg defaults: `matchzy_minimum_ready_required 2`, `matchzy_chat_messages_timer_delay 21`. Existing config.cfg files keep their values.
    - **Changed:** On/off settings accept `1` / `0` as well as `true` / `false`.
    - **Changed:** `get5_endmatch` follows Get5 (cancel, or `team1`/`team2` wins). `skip_veto` defaults to false; `side_type` and `veto_first` are read.
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
    - **Fixed:** Demo recordings: no overwrite on restart (`_part2`), all parts uploaded, failed demos not reported.
    - **Fixed:** `.rr` / admin menu "Restart Round" in a match, `.forceend` vs `!forceend`, `.matchsetup` lock, `.stop` in round 1 of scrim/hill.
    - **Fixed:** Knife round played although `map_sides` fixed the side.
    - **Fixed:** Coaching another team, veto hanging when a captain leaves, remote backup upload, `!gg` threshold, event order and `get5_status` fields.
    - **Fixed:** MatchZy settings from a match config's `cvars` are restored after the series.
    - **Fixed:** Several practice issues (`.ff`, `.timer`, `.back`, spawn markers, corrupt JSON files, `.delay`, bot kicks).
    - **Security:** Match config `cvars` and map names can no longer run console commands; admins.json flags are honored; secrets are kept out of the log.

??? note "0.8.87 - September 28, 2026"

    - **New:** `"coaches"` list per team in the match config. Listed coaches can join, are not kicked, and become coach automatically.
    - **Changed:** Coaches no longer count toward `players_per_team` and do not need to `.ready`.

??? note "0.8.86 - September 28, 2026"

    - **New:** `matchzy_coach_enabled` to turn coaching off.
    - **New:** Live events `grenade_thrown`, `grenade_detonated`, `player_blinded`, `bomb_pickup`, `bomb_dropped`, `bomb_exploded`.
    - **Changed:** `.coach` only works for the team you are on.
    - **Fixed:** `bomb_planted` / `bomb_defused` report the correct site.

??? note "0.8.85 - September 27, 2026"

    - **New:** Players vs bots matches (`"players": "any"` and `"bots": true` in the match config).
    - **New:** `matchzy_stats_include_bots` to record bot stats.
    - **Fixed:** `.rethrow` spawning nothing after some throws.
    - **Removed:** `matchzy_coach_debug`.

??? note "0.8.83 - September 16, 2026"

    - **Changed:** Less work on the game thread at round start and during practice rethrows (fewer hitches).
    - **Changed:** Round backups are written in the background.

??? note "0.8.82 - September 15, 2026"

    - **Fixed:** Practice bot commands and `.warmupbots` respect `-nobots` (no more invisible bots).
    - **Fixed:** `.map` ignored after a map change.
    - **Fixed:** `-nohltv` detection.

??? note "0.8.80 - September 4, 2026"

    - **Changed:** Mode commands (`css_prac`, `css_match`, ...) remember the mode across map changes.
    - **Changed:** Practice started on a fresh map starts right away, without a warmup countdown.
    - **Changed:** Bullet impacts are on by default in practice.
    - **Fixed:** Warmup starting on top of practice when loaded from a mode-switch script.

??? note "0.8.79 - September 2, 2026"

    - **Changed:** `.mhelp` shows each admin the commands they can run, and the permission for the rest. Any admin can open it.

??? note "0.8.78 - September 1, 2026"

    - **New:** Workshop maps by name with `ws:<name>`.
    - **Fixed:** `workshop/<id>` map formats in match configs and veto.

??? note "0.8.77 - August 25, 2026"

    - **Changed:** Runs on stock CounterStrikeSharp as well as the fork.
    - **Changed:** `gamedata/matchzy.json` includes the weapon-select offset.

??? note "0.8.76 - August 17, 2026"

    - **Fixed:** `.bot` refusing to spawn when CSTV is disabled.

??? note "0.8.75 - August 15, 2026"

    - **Fixed:** `matchzy_kick_when_no_match_loaded` and the whitelist are enforced again.
    - **Fixed:** A server crash when a player outside a set-up match joined a team.

??? note "0.8.74 - August 11, 2026"

    - **New:** `matchzy_auto_team_names_enabled`.
    - **Changed:** Demo recording is verified by file growth and restarted if it stalls.
    - **Changed:** `tv_enable_dynamic 0` while recording; practice bots never take the CSTV slot.
    - **Fixed:** Molotov detonation times per grenade.

??? note "0.8.73 - August 6, 2026"

    - **Changed:** `config.cfg` is no longer in the release zip, so updates never overwrite it. `database.json.example` is shipped instead.
    - **Fixed:** Demos not recorded on `.match`, `.scrim` and `.hill`, or after another plugin changed map.
    - **Fixed:** Several database issues: MySQL player stats, reused match ids, concurrent writes, multi-kill columns.
    - **Fixed:** Stopped matches get an end time in the database.
    - **Fixed:** `.restore` failing on a half-written round file.
    - **Fixed:** Knife maps using the previous match's sides.

??? note "0.8.72 - August 5, 2026"

    - **New:** `matchzy_restore_auto_unpause`, `matchzy_restore_unpause_delay`, `matchzy_restore_scoreboard_stats`, `matchzy_dot_trigger_dedupe`.
    - **Changed:** A restore rolls back the scoreboard and is only announced once it loaded.
    - **Changed:** `.restorecurrent` / `!rr` no longer ask for confirmation.
    - **Fixed:** Match stuck paused after a restore.
    - **Fixed:** Commands running twice when `.` is a CounterStrikeSharp chat trigger.
    - **Fixed:** Config files written to two folders (`MatchZy` and `matchzy`).

??? note "0.8.71 - August 1, 2026"

    - **New:** Demo upload after each map, S3 presigned upload (`matchzy_demo_upload_s3`), upload headers, `demo_upload_ended` event.

??? note "0.8.70 - July 31, 2026"

    - **Fixed:** Match end data written twice at series end.

??? note "0.8.69 - July 30, 2026"

    - **Fixed:** `.t` / `.ct` from spectator in practice kicking the player or leaving them dead.

??? note "0.8.68 - July 29, 2026"

    - **New:** `.backups` without a live match lists the newest backup files (restore after a crash). `!loadbackup` from chat.
    - **Fixed:** `.botjiggle` hiding new bots; `.bot` sometimes spawning nothing.

??? note "0.8.67 - July 28, 2026"

    - **Changed:** Coaches never displace players from competitive spawns, have no teammate color and are hidden from the kill feed.
    - **Changed:** `bot_quota 0` and `mp_randomspawn 0` in the mode cfgs.
    - **Removed:** `matchzy_random_spawns`.

??? note "0.8.66 - July 27, 2026"

    - **Fixed:** Demos not recording on `.scrim` and `.hill`.

??? note "0.8.65 - July 27, 2026"

    - **Fixed:** Coach killed after the round went live; `.watchme` / `.spec` failing to move players.

??? note "0.8.64 - July 24, 2026"

    - **Fixed:** Crash when switching to the side you are already on; `.loadbotpos` tilted bots.
    - **Removed:** Experimental `.jt` / `.jumpthrow`.

??? note "0.8.63 - July 24, 2026"

    - **Fixed:** A match loaded by URL being lost on the map change to its first map.

??? note "0.8.62 - July 24, 2026"

    - **New:** Named bot positions (`.savebotpos`, `.loadbotpos`, `.listbotpos`, `.delbotpos`, `.showbotpos`) and `.botjiggle`.
    - **New:** Molotov burn time in chat.

??? note "0.8.61 - July 20, 2026"

    - **New:** `matchzy_coaching_mode`, `.showcoachspawns`, `.coachtest`.
    - **Changed:** Coach viewing spots reworked for the whole map pool.
    - **Fixed:** Crash from `.watchme` / `.fas`.

??? note "0.8.60 - July 19, 2026"

    - **Fixed:** Coach falling out of the map on some maps.

??? note "0.8.59 - July 19, 2026"

    - **New:** Grenade library: `.shownades`, `.hidenades`, shared pack (`.libadd`, `.libremove`, `.liblist`), `.nades` menu, throw styles on `.savenade`, `.ln #N`.
    - **New:** `.warmupbots`, BO2 in `.matchsetup`.
    - **Changed:** Dryrun keeps playing until `.exitdry`.
    - **Removed:** Experimental `.predict`.

??? note "0.8.58 - July 18, 2026"

    - **New:** `.matchstop` alias.
    - **Fixed:** Dot commands during match setup; `.stopmatch` before the match is live.

??? note "0.8.57 - July 17, 2026"

    - **Changed:** Practice turns off team-damage kicks.
    - **Changed:** `matchzy_ready_hint_style` controls the whole ready display.
    - **Fixed:** `.bot` spawning two bots; ghost bodies after a disconnect in practice.

??? note "0.8.56 - July 16, 2026"

    - **New:** Named positions, `.flashtest`, `.blind`, `.wipe`, `.cleanup`, `.autoclear`, `.landmarker`, `.arc`, `.mynades`, `matchzy_ready_up_by_ping`.
    - **Changed:** `.delnade` deletes several lineups or `all`.
    - **Changed:** Retired settings are removed from `config.cfg` on load.

??? note "0.8.55 - July 16, 2026"

    - **New:** Interactive spawn markers (aim + E to teleport).
    - **Changed:** `.back` steps through history like CS:GO.

??? note "0.8.54 - July 16, 2026"

    - **New:** `.grt` global rethrow; incendiary rethrow.
    - **Fixed:** Rethrow for smoke, HE, molotov and decoy; grenade spin on rethrow.

??? note "0.8.53 - July 15, 2026"

    - **New:** HTML ready-up panel, `matchzy_ready_clantag_enabled`.
    - **Changed:** MatchZy ships its own `gamedata/matchzy.json`.
    - **Changed:** Yields `css_map` to CS2-SimpleAdmin / CS2MapChange.

??? note "0.8.52 - July 15, 2026"

    - **New:** `.map` command with name resolution and workshop ids.
    - **Changed:** Config folder name is detected (`MatchZy` or `matchzy`); `admins.json` is loaded.
    - **Removed:** End-of-match summary panel.

Older releases: see the [full changelog](changelog.md).
