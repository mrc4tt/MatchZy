# Changelog

Customized fork of [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak, adapted for CS2 game-server hosting. On top of upstream it adds a remote log HTTP API, G5API compatibility, auto changelevel, advanced stats (HLTV 2.0 rating / KAST / clutch / opening duels), a coach system, a pause overhaul, and in-game admin and match-setup menus.

Fork version numbering is independent of upstream. Upstream changelog: <https://github.com/shobhit-pathak/MatchZy/blob/main/CHANGELOG.md>

# 0.8.60

#### July 19, 2026

- Fixed the coach falling out of the map with a black screen on maps where the team spawn backs onto the map edge (Mirage T): the behind-team spot is now validated with wall and floor probes and moves closer to the spawn until it is inside the world.
- Fixed players being re-teleported every round while a coach was on, even on the coachless side: anyone standing on any valid team spawn is now left alone (maps like Mirage enable more spawn points than the team size), and near-duplicate spawn points can no longer seat two players almost on top of each other.
- New `.coachtest` (admin): instantly places you like a coach on your current side and back again on the second run - lets a single admin verify coach placement on any map without bots or a match.
- Coach spawn computation is hardened against the AcceleratorCSS tracer: a failure now falls back to the fixed viewing spot instead of erroring in the spawn handler.

# 0.8.59

#### July 19, 2026

- Practice grenade library: `.shownades` toggles in-world markers for every saved lineup on the map (yours + the shared pack), `.hidenades` hides them, and `css_shownades` can be bound to a key. Markers and labels are colored by grenade type (smoke blue, flash yellow, HE red, molotov orange, decoy grey).
- Grenade library labels show the type, comment and throw style, are readable from every angle (never mirrored), and no longer clip into walls next to the lineup.
- Grenade library: press E aimed at a marker to teleport to the lineup with the right grenade equipped; lineups saved on the same spot share one marker and F cycles between them (a counter like 1/2 is shown).
- Grenade library: the marker you are standing on hides for you only (no beam blocking the throw) and reappears when you walk away - no need to run `.shownades` again.
- Grenade library shared pack: admins can promote lineups with `.libadd <name>`, remove with `.libremove <name>` and list with `.liblist` - visible to everyone on the server.
- `.savenade <name> [throwtype] <comment>` accepts an optional throw style (jump/run/walk/crouch) as the second word; `.listnades` numbers lineups and `.ln #N` loads by number. Saving without a grenade in hand is allowed (the label shows a blank type).
- New `.nades` menu: browse the grenade library by type and click a lineup to load it (requires the CS2MenuManager plugin).
- New `.warmupbots [count]` (admin): adds aim-warmup bots during the warmup/ready phase; they are removed automatically the moment the knife round or live match starts.
- Practice `.bot` fixes: the engine could pair-spawn a second bot on the other team (leaving a bot on each team) and could even hand you a bot on your OWN team; both are detected and kicked, so `.bot` adds exactly one bot on the opposing team again.
- Practice colored smoke no longer reverts to grey a few seconds after blooming, and rethrown smokes (`.rt` / `.throw`) are colored too.
- Fixed `matchzy_autostart_mode 2` being ignored after `css_plugins restart MatchZy`: the plugin read the convar before config.cfg had re-applied, so a practice server came back up in match mode.
- Dry Run no longer ends after a single round: play as many rounds as you like (with bots or friends) until an admin runs `.exitdry`, which now returns to match warmup instead of forcing practice mode (run `.prac` yourself if wanted).
- `.match` and `.scrim` now print a compact status line (Knife / DemoRec / Playout with colored Enabled/Disabled) plus a `.help` hint, and `.help` during the ready phase shows the same status block with the available commands.
- Coach overhaul: the coach now spawns behind their own team automatically on every map (no per-map file), can no longer be damaged or killed by teammates, and the players' competitive spawns are left untouched (previously everyone was re-teleported each round when a coach was on, which shuffled spawns).
- Fixed scrim.cfg never applying `mp_autoteambalance 0` (the setting was glued into a comment). Note: existing scrim.cfg files on disk keep the old text - delete the file to regenerate it.
- Fixed the first `.ma` / `.nades` menu open stalling the server for over a second (the menu library is now warmed up in the background at plugin load).
- `.shownades` failures now log the full error and a single bad marker no longer prevents the rest from drawing.
- `.matchsetup` wizard: added a Best of 2 (BO2) series option and a "Back to Admin Menu" entry at the top of the wizard.
- Removed the experimental `.predict` grenade predictor along with `matchzy_experimental_predictor` and the `matchzy_predict_*` convars.

# 0.8.58

#### July 18, 2026

- Fixed dot-prefix chat commands (`.ma`, `.match`, `.stopmatch`, ...) doing nothing during match setup: a spectator / unassigned admin is excluded from the internal player map while a match is being set up, which stopped the chat handler from resolving them, so only the `!` versions (e.g. `!ma`) worked. Dot commands now dispatch for those players too.
- Fixed `.stopmatch` and the "Stop Match" button in the `.ma` / `css_ma` admin menu not stopping a match that had been set up but was not yet live (setup / veto / warmup / knife). Stop now works in every pre-live and live state, not just once the match goes live.
- Added `.matchstop` as an alias for stopping/ending a match (alongside the existing `.stopmatch`, `.endmatch`, `.stopgame`, `.endgame`, `.forcestop`, `.forceend`, `.end`, `.exitscrim`).

# 0.8.57

#### July 17, 2026

- Fixed `.bot` spawning two bots at once: the redundant `bot_join_team` before `bot_add_t`/`bot_add_ct` spawned an extra bot, and `bot_quota_mode normal` then refilled any kicked extra. `.bot` now adds exactly one bot (quota is pinned to the tracked bot count).
- Practice now disables the round's team-damage penalties (autokick / team-damage warn/kick / TK punish), so grenade / molotov / friendly-fire testing no longer risks a kick.
- Fixed a lingering ghost body in practice: a player who disconnects mid-practice could leave an orphaned pawn (the round is kept alive so the engine does not always reap it). The disconnecting pawn is now removed on the next frame.
- The ready-status display is controlled by a single convar, `matchzy_ready_hint_style`: `0` = classic center text (default), `1` = HTML READY-UP panel with the native warmup suppressed (panel shows its own `WARMUP` badge and no native pill, at the cost of a frozen `1:00` round timer top-center). This folds in the old `matchzy_ready_hide_warmup_hud` toggle (retired) and the experimental `matchzy_ready_block_warmup_announce` (removed); both are auto-removed from an existing config.cfg on update. Note: hiding the native warmup is what forces CS2 to draw the round timer, and no server value can blank it, hence the frozen `1:00`.
- Hardened practice spawn loading (`.prac`): the spawn scan is now materialized and guarded so it cannot crash the command (fixes an `ArrayTypeMismatchException` seen when running under the AcceleratorCSS Harmony tracer).
- Fixed the ready phase after a live plugin reload (`css_plugins unload/load`): per-player ready state and the gamerules warmup state are now restored on hot reload, so the ready hint counts connected players (with the NotReady list) and displays correctly instead of reading 0/0 or staying invisible until a round restart.

# 0.8.56

#### July 16, 2026

- Practice named position slots: `.savepos <name>` saves a position under a name, `.loadpos <name>` teleports back to it, `.listpos` lists your saved names, and `.delpos <name>` removes one (up to 32 per player). `.savepos` / `.loadpos` with no name keep working as the single default slot.
- Practice flash test: `.flashtest` (or `.ft`) toggles a chat readout of your own blind duration each time you get flashed, for tuning pop-flashes and self-flashes.
- Practice self-flash: `.blind` throws a flashbang at your own face for pop-flash reaction reps (no teammate or client bind needed).
- Practice `.wipe` (or `.clearnades`) clears your grenade throw history (the source for `.last` / `.back` / `.rt` / `.throwindex`) without leaving and re-entering practice.
- Practice `.jt` (`.jumpthrow`) jumpthrows the grenade in your hand, gated behind the new convar `matchzy_experimental_jumpthrow` (default `false`). Experimental: it forces the jump and release server-side, which the engine may ignore on some CS2 builds, so it ships off by default.
- Practice `.cleanup` clears all utility currently on the map (smokes, mollies, infernos, live projectiles).
- Practice `.autoclear` toggles auto-clearing utility: when on, each time a grenade detonates the older utility is removed and only the just-detonated result is kept, for fast lineup iteration.
- Practice `.landmarker` (`.lm`) toggles a marker at each grenade's detonation point so you can see exactly where it landed.
- Practice `.arc` (`.traceline`) toggles drawing the flight path of thrown grenades as an in-world trajectory line.
- Practice saved grenade lineups are now capped at 500 per player, and `.mynades` shows how many you have saved.
- Fixed `.rt` / `.throw` (rethrow last grenade) silently doing nothing after a normal mouse1 throw: a freshly thrown grenade could be recorded with zero velocity (its velocity field lags a frame on current CS2 builds), which the rethrow's zero-velocity guard then dropped. The launch velocity is now recovered from the projectile's movement when the direct read comes back empty.
- Added an experimental grenade landing predictor: `.predict` draws the flight arc and a landing marker for the grenade in your hand, gated behind `matchzy_experimental_predictor` (default `false`). It forward-simulates the throw (with world-collision wall/floor bounces when built against a CounterStrikeSharp API that exposes the trace natives, otherwise a no-collision estimate), tunable live via `matchzy_predict_gravity` / `matchzy_predict_throwspeed` / `matchzy_predict_elasticity` / `matchzy_predict_friction`, with a `matchzy_predict_debug` readout of predicted-vs-actual landing distance for calibration.
- Fixed a countdown timer appearing during the ready phase when the HTML ready panel (`matchzy_ready_hint_style 1`) is used: hiding the native WARMUP banner also dropped `mp_warmup_pausetimer`, so the round timer counted down. The timer is now frozen during the ready phase, matching paused warmup.
- The HTML ready panel now shows a `WARMUP` badge at the top, since the native WARMUP banner is hidden while the panel is up.
- Fixed the HTML ready panel dropping lines (e.g. the NOT READY status) in languages with accented characters such as Danish and Albanian: accented text broke the center-HTML rendering, so it is now escaped to render correctly in every language.
- Practice `.delnade` can now delete multiple lineups at once: `.delnade <name1> <name2> ...` removes each, and `.delnade all` removes every lineup you saved on the current map. It reports which were deleted and which were not found.
- Fixed a rare `ArrayTypeMismatchException` when entering practice (`.prac` -> `GetSpawns`) on servers running a call-history crash tracer: the spawn lists are now pre-sized so the list-grow path that tripped it is never taken.
- Added `matchzy_ready_up_by_ping` (default `true`): set it `false` to stop pinging (middle-mouse / scroll button) from toggling your ready status, for players who ready up by accident.
- The ready panel (and classic ready hint) no longer shows during `.dryrun`, which has no ready gate.
- config.cfg now auto-removes retired convar lines on load, so an upgraded server no longer spams "Unknown command" when config.cfg execs.
- Fixed getting stuck in the spectator/observer camera for several seconds after picking a team during the ready phase (with the HTML ready panel): hiding the native warmup also disabled warmup's auto-respawn, so a fresh team-joiner was not spawned. Players are now respawned on join, with a periodic safety sweep that keeps every T/CT player spawned during the ready phase.

# 0.8.55

#### July 16, 2026

- Practice spawn markers are now interactive: with `.showspawns` active, aim at a spawn marker and press USE (E) to teleport to that spawn. `.hidespawns` (or leaving practice) disarms it.
- Practice spawn markers are now lifted slightly off the floor so they stay visible over shallow water (e.g. de_ancient) instead of sinking out of sight.
- Practice `.back` with no number now steps backward through your grenade history like CS:GO practice mode: the first `.back` jumps to your most recent nade, each further `.back` goes one older, and it stops at the oldest instead of printing a usage message. `.last` and `.back <number>` set the starting point, and the cursor resets when you throw a new nade.
- Fixed practice spawn teleports (`.spawn`, best/worst spawn) tilting the whole player model sideways at steep spawn angles after a recent CS2 update: they now keep the body upright (same fix already used for `.last` / `.back` nade lineups).
- Build now compiles against the fork's CounterStrikeSharp API DLL at `~/CounterStrikeSharp` (1.0.398) instead of the NuGet package (which tops out at 1.0.371), so the plugin matches the newer server runtime ABI. This fixes an `EntryPointNotFound` error triggered on 1.0.39x runtimes.

# 0.8.54

#### July 16, 2026

- Fixed practice rethrow (`.throw` / `.rt` / `.throwsmoke` etc.) only working for flashbangs: smoke / HE / molotov / decoy re-throws silently did nothing when their native `*_Create` signature failed to resolve from `gamedata/matchzy.json`. They now fall back to the managed entity API (like flash always has) so a rethrow always spawns, and log a clear warning when the signature was missing so a stale/undeployed gamedata file is diagnosable. Also added incendiary (CT molotov) rethrow support.
- Fixed re-thrown grenades spinning wrong: the projectile's angular velocity (spin) was being set to its linear launch velocity on rethrow. The real spin is now captured at throw time and replayed, so a rethrown nade tumbles like the original (cosmetic; landing spot was already correct).
- Added `.grt` (`.globalrethrow`, console `css_grt`) in practice: rethrows every player's last thrown grenade at once, for setting up full team executes in one command.
- Fixed `.listnades` / `.loadnade` / `.delnade` / `.importnade` throwing a `FileNotFoundException` (server error spam) on a fresh server before any lineup was saved: the missing `savednades.json` is now treated as empty instead of crashing the command.

# 0.8.53

#### July 15, 2026

- Reworked the "waiting for players" ready screen into a per-player HTML panel: title, progress bar, ready count, CT/T split, current mode (Match / Scrim / Hill / Match Setup), and each player's own READY / NOT READY status, shown in their own language. New convar `matchzy_ready_hint_style` (0 = classic center text, 1 = HTML panel, default `1`) and `matchzy_ready_hint_blink` (blink the NOT READY line to grab attention, style 1 only, default `false`).
- The native "WARMUP" HUD banner can be hidden during the ready phase (with the HTML ready panel) so it no longer overlaps the panel. A "fake warmup" keeps the pre-match ready phase playing like warmup (round never ends, respawn on death, no round-time expiry) while the banner is hidden, and the center panel no longer flashes.
- Fixed the ready panel showing the wrong mode: switching `.scrim` / `.hill` during warmup now updates the panel immediately, and `.hill` -> `.match` no longer leaves the server stuck in hill mode.
- Practice grenade spawns and `.breakrestore` now resolve their signatures by key from CounterStrikeSharp's gamedata instead of hardcoded byte patterns. MatchZy ships its own `gamedata/matchzy.json` (auto-loaded by CounterStrikeSharp, included in the release `.zip` at `addons/counterstrikesharp/gamedata/matchzy.json`), so it works on stock CounterStrikeSharp without editing the core `gamedata.json`. Missing/stale keys degrade gracefully instead of crashing.
- Added `matchzy_ready_clantag_enabled` (default `true`) to toggle the `[READY]` / `[UNREADY]` scoreboard clan tags shown during the ready phase.
- MatchZy now auto-yields the map command when a dedicated map plugin (CS2-SimpleAdmin / CS2MapChange) is installed alongside: it registers neither `css_map` nor handles the `.map` chat command, letting the other plugin own map changes. This avoids a `css_map` ConCommand conflict (which could block players from connecting) and a double map change (two plugins both firing a changelevel disconnected players). Map changes are also debounced, so a single `.map` never changes the map twice even on servers that add `.` as a chat trigger (where `.map` hits both the chat dispatch and `css_map`). `matchzy_map_console_command_enabled` (default `true`) gates the console command; set it `false` to never register `css_map`.

# 0.8.52

#### July 15, 2026

- Added optional `.map` / `css_map` admin command for changing the map. The `css_map` console command is gated by `matchzy_map_console_command_enabled` (default `true`) so it can defer to another plugin such as CS2-SimpleAdmin without a conflict; the `.map` chat command is always available. The map name is validated and resolved before the demo is stopped and bots are kicked (a typo no longer tears the server down and loses the recording), supports workshop ids via `host_workshop_map`, and resolves bare names (e.g. `mirage` -> `de_mirage`).
- Config folder is now case-agnostic: an existing `cfg/MatchZy/` or `cfg/matchzy/` is auto-detected and used for **every** MatchZy file (cfgs, `savednades.json`, `admins.json`, `whitelist.cfg`). Also fixes cfgs failing to exec on case-sensitive Linux when the folder was `MatchZy/` but paths were hardcoded lowercase. Keep only one of the two folders.
- `admins.json` is now actually loaded at startup (was never called before, so it had no effect); only valid SteamID64 entries grant admin, and a reload drops removed admins.
- Fixed practice smoke rethrow (`.rt` / `.throwsmoke`) dropping the smoke dead at the spawn origin. Smoke was excluded from the velocity-apply path.
- Fixed `.loadnade` / `.back` / `.last` auto-throwing the restored grenade (dead into a wall at tight lineups); the pose-clear now redeploys the nade without triggering a throw.
- Fixed `.savenade` storing the position 4 units above the real stance, which made loadnade lineups release from the wrong height.
- Removed the end-of-match summary panel (the center-HTML MVP / clutch / top-frag panel) and its `matchzy_match_summary_panel` and `matchzy_match_summary_panel_duration` convars.

# 0.8.51

#### July 13, 2026

- Grouped the source into concern folders (Core / Match / Practice / Stats / Pause / ...) with no namespace or build change.

# 0.8.50

#### July 11, 2026

- Bumped CounterStrikeSharp API to 1.0.371.
- Fixed a server crash and a phantom death when switching team with `.t` / `.ct` / `.spec` in practice.
- Kept the player model flat on `.last` / `.back` when the lineup was aimed at a steep pitch.
- Auto-clean the stale `publish/` folder on `dotnet publish`.

# 0.8.49

#### July 9, 2026

- Bumped for the Valve game update (build 14168).

# 0.8.48

#### July 2, 2026

- Added ClanTags support and small fixes.
- Release / CI housekeeping (untrack build artifacts, fix `release.yml`).

# 0.8.41 – 0.8.46

#### July 1, 2026

- Re-arm AutoStart on the first player connect so warmup execs after an empty-map load (0.8.46).
- Iterated on the warmup-timer pause so the HUD reliably shows a plain paused "WARMUP" (`mp_warmup_pausetimer` / `mp_warmup_online_enabled` handling) (0.8.41–0.8.45).

# 0.8.40

#### July 1, 2026

- Added the `matchzy_nade_pose_flicker_free` toggle for nade restore.

# 0.8.39

#### July 1, 2026

- Clear the stuck throw-pose on all grenade restores.

# 0.8.38

#### July 1, 2026

- Ship cfgs under `cfg/MatchZy/` in the release artifact.
- Don't create `matchzy/` when `MatchZy/` already exists.
- Added the in-game coach spawn builder.
