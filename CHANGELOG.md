# Changelog

Customized fork of [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak, adapted for CS2 game-server hosting. On top of upstream it adds a remote log HTTP API, G5API compatibility, auto changelevel, advanced stats (HLTV 2.0 rating / KAST / clutch / opening duels), a coach system, a pause overhaul, in-game admin and match-setup menus, and in-game stats commands.

Fork version numbering is independent of upstream. Upstream changelog: <https://github.com/shobhit-pathak/MatchZy/blob/main/CHANGELOG.md>

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
