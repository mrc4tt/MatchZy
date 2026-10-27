# Troubleshooting

The most common problems, what causes them and how to fix them. Most answers start in the server console or log: MatchZy writes every warning there with a `[MatchZy]` prefix.

!!! tip "First check"
    Type `.version` in chat (or `css_matchzy_version` in the console). If nothing answers, the plugin is not loaded: start with the first section below.

## Install and loading

### MatchZy does not load

- Run `css_plugins list` in the console. MatchZy should be listed as loaded.
- `MissingMethodException`, `TypeLoadException` or `EntryPointNotFoundException` in the log: CounterStrikeSharp is too old or does not match. Update CounterStrikeSharp to the latest release and restart.
- Check that the files are in `game/csgo/addons/counterstrikesharp/plugins/MatchZy/` (not one folder deeper, which happens when a zip is extracted into its own folder).

### `.ma`, `.matchsetup`, `.nades` or `.strats` only reply with a message

"In-game menus require the CS2MenuManager plugin, which is not installed on this server." The menus need the optional [CS2MenuManager](installation.md#requirements) plugin (1.0.42+). Everything else works without it.

### My config changes are not applied

- A mode cfg (`live.cfg`, `prac.cfg`, ...) runs after `config.cfg` and wins for the same setting. Put game settings for a mode in `<mode>_override.cfg`, for example `live_override.cfg`. See [Keeping your own changes](updating.md#keeping-your-own-changes).
- There are both a `cfg/MatchZy/` and a `cfg/matchzy/` folder. MatchZy uses only one (it prints which at startup); delete the other.
- After an update your existing cfg files are never replaced. The current defaults are in `cfg/MatchZy/defaults/` to compare with. Delete a file to get the new default written.

## Admins and commands

### I am not recognized as admin

- Add your SteamID64 to `cfg/MatchZy/admins.json`, or give yourself a CounterStrikeSharp admin flag (`@css/config` for match control, `@css/root` for everything). See [admins.json](../reference/files.md#adminsjson).
- `.mhelp` lists the admin commands you can run and the permission you are missing for the others.

### `.map` does nothing

`.map` stands aside when CS2-SimpleAdmin or CS2MapChange is installed (use their map command), or when `matchzy_map_console_command_enabled` is off. It also only works before a match has started.

### A command says it is not available now

Most commands only work in one phase: warmup, live, practice and so on. `.help` lists the commands for the phase the server is in. See [Where commands work](../reference/commands.md#where-commands-work).

## Matches

### The match does not start when everyone is ready

- The ready count is `matchzy_minimum_ready_required` (or `.readyrequired <n>` for this match). With a match config, the team size comes from `players_per_team`. See [Ready system](../guides/ready-system.md).
- In 1v1, wingman and rush pugs the count follows the mode (2, 4 or 6) unless the admin set a lower value.

### Players are kicked when they join

`matchzy_kick_when_no_match_loaded` is on: players who are not on the loaded match's roster are kicked, and everyone is kicked when no match is loaded. Admins are exempt. Turn it off in `config.cfg` for a public or pug server.

## Round backups

### `.restore` fails or there are no backups

- The server must be started with `-allow_round_backup_files`. Without it the console says "Round backup files are disabled" and MatchZy refuses `.restore` with a chat message. See [Launch options](installation.md#launch-options).
- "Backup ... contains no usable round data, nothing was restored": the backup JSON exists, but CS2 wrote no round file for it, usually because the match was played without `-allow_round_backup_files` (or on MatchZy 1.0.9 or older, see below). Those backups cannot be restored; add the option and restart the server, and new rounds are backed up again.
- "Rejecting round backup file path" in the console: CS2 (since the update of October 15, 2026) rejects absolute backup paths. Update MatchZy to 1.0.10 or newer.
- The backup files are in `csgo/` or `csgo/addons/metamod/`, MatchZy's own state in `csgo/MatchZyDataBackup/`. See [Round backups](../guides/pauses-backups.md#round-backups).

## Demos

### No demo was recorded

- GOTV must be on: `tv_enable 1` in the launch options or a cfg.
- Demos are only recorded for live maps (match, scrim, hill), not in warmup or practice.
- Look for the demo lines in the server log: every start, retry and stop is logged. A cfg that kicks the CSTV bot (for example `bot_quota 0` in a mode cfg) stops the recording; MatchZy 1.0.9 and newer bring the CSTV bot back when that happens, if the server runs with `+tv_enable_dynamic 1`. Without it the log says "tv_enable_dynamic is 0 - GOTV comes back only after a map change". See [Demos](../guides/demos.md).

## Practice

### `.last` / `.back` / `.ln` do not put the grenade in your hand, or `.t` / `.ct` from spectator is "currently broken"

The gamedata no longer matches the CS2 build, usually right after a CS2 update. The log shows lines like `CCSPlayer_WeaponServices_SelectItem offset missing`. Update MatchZy when a new release is out; it ships an updated `gamedata/matchzy.json`. See [Gamedata](gamedata.md).

## Live events

### My panel or bot receives no events

- `matchzy_remote_log_url` must be set in `config.cfg`.
- Match events are only sent while a match is loaded or live. `server_ready`, `map_change` and `server_shutdown` are always sent.
- The server log shows each result event with the match id and the HTTP status your endpoint returned. See [Live events API](../guides/events.md).

## Still stuck?

Open an [issue](https://github.com/mrc4tt/MatchZy/issues/new) with the output of `css_matchzy_version`, your CounterStrikeSharp version and the relevant console lines (remove tokens and passwords).
