# Changelog

Customized fork of [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak, adapted for CS2 game-server hosting. On top of upstream it adds a remote log HTTP API, G5API compatibility, auto changelevel, advanced stats (HLTV 2.0 rating / KAST / clutch / opening duels), a coach system, a pause overhaul, and in-game admin and match-setup menus.

Fork version numbering is independent of upstream. Upstream changelog: <https://github.com/shobhit-pathak/MatchZy/blob/main/CHANGELOG.md>

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
- Added an experimental grenade landing predictor: `.predict` draws the flight arc and a landing marker for the grenade in your hand, gated behind `matchzy_experimental_predictor` (default `false`). It forward-simulates the throw with world collision (wall/floor bounces), tunable live via `matchzy_predict_gravity` / `matchzy_predict_throwspeed` / `matchzy_predict_elasticity` / `matchzy_predict_friction`, with a `matchzy_predict_debug` readout of predicted-vs-actual landing distance for calibration.
- Fixed a countdown timer appearing during the ready phase when the HTML ready panel (`matchzy_ready_hint_style 1`) is used: hiding the native WARMUP banner also dropped `mp_warmup_pausetimer`, so the round timer counted down. The timer is now frozen during the ready phase, matching paused warmup.
- The HTML ready panel now shows a `WARMUP` badge at the top, since the native WARMUP banner is hidden while the panel is up.
- Practice `.delnade` can now delete multiple lineups at once: `.delnade <name1> <name2> ...` removes each, and `.delnade all` removes every lineup you saved on the current map. It reports which were deleted and which were not found.
- Fixed a rare `ArrayTypeMismatchException` when entering practice (`.prac` -> `GetSpawns`) on servers running a call-history crash tracer: the spawn lists are now pre-sized so the list-grow path that tripped it is never taken.
- Added `matchzy_ready_up_by_ping` (default `true`): set it `false` to stop pinging (middle-mouse / scroll button) from toggling your ready status, for players who ready up by accident.

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
- The native "WARMUP" HUD banner is now hidden during the ready phase (convar `matchzy_ready_hide_warmup_hud`, default `true`) so it no longer overlaps the ready panel. A "fake warmup" keeps the pre-match ready phase playing like warmup (round never ends, respawn on death, no round-time expiry) while the banner is hidden, and the center panel no longer flashes.
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
