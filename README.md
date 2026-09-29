## A forked MatchZy plugin - customized

Customized [MatchZy](https://github.com/shobhit-pathak/MatchZy) fork for CS2 competitive servers. Adds a remote log API, G5API compatibility, auto changelevel, advanced stats, a coach system, pause overhauls, and in-game admin and match-setup menus.

## In-game commands

Type in chat with a dot prefix (the `!` / `css_` prefixes work too, e.g. `!ready` / `css_ready`).

**Help & admin (admins only):**

- `.help` - commands available in the current phase.
- `.mhelp` - summary of admin commands.
- `.ma` / `.matchadmin` - in-game admin menu (needs CS2MenuManager).
- `.matchsetup` - in-game match-setup wizard (needs CS2MenuManager).
- `.map <name/id>` - change map (name or workshop id; auto-yields to a dedicated map plugin if one is installed).
- Match flow: `.match`, `.scrim`, `.prac`, `.dry`, `.warmup`, and the ready commands (`.ready` / `.r`, `.forceready`).

## Players vs bots matches

A match config can make one team open to any human and the other a bot team:

```json
{
  "matchid": 0,
  "num_maps": 1,
  "maplist": ["de_dust2"],
  "players_per_team": 5,
  "team1": { "name": "Players", "players": "any" },
  "team2": { "name": "Bots", "bots": true, "bot_difficulty": 2 }
}
```

- `"players": "any"` - any human who is not on another roster (or the spectators) joins this team. No SteamIDs needed. Named rosters keep working on the other team.
- `"bots": true` - the team is played by bots, filled up to `players_per_team`. `bot_difficulty` 0-3, default 2. Humans cannot join it; the bots follow their side through halftime and overtime.
- Bots cannot veto, knife or type commands, so a bot match needs a fixed map list (exactly `num_maps` maps, or `"skip_veto": true`) and the load is refused otherwise. A `knife` side is drawn at random instead. The bot side always counts as ready and agrees to unpause, and autopause never counts it short.
- Player stats include the bots automatically (see `matchzy_stats_include_bots`). `.warmupbots` is disabled in these matches.

## Coaches

A team in the match config can list its coaches next to its players. Same shape as `"players"`: an object keyed by SteamID64, or an array of SteamID64s.

```json
"team1": {
  "name": "Team A",
  "players": { "76561198000000001": "Player1", "76561198000000002": "Player2" },
  "coaches": { "76561198000000009": "CoachA" }
}
```

- A listed coach may join that team (and is not kicked by `matchzy_kick_when_no_match_loaded`) and becomes its coach automatically. They cannot `.uncoach` into a player.
- When a team has a `coaches` list, only the SteamIDs on it can `.coach` that team. Without a list, anyone on the team can `.coach`, as before.
- `.coach` only works for the team you are on. `matchzy_coach_enabled false` turns coaching off entirely, including the listed coaches.
- Coaches do not count toward `players_per_team` in the ready check and do not need to `.ready`.

## Requirements

- **CS2 dedicated server** (Windows/Linux)
- **[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)**: the plugin framework. This fork targets API 1.0.369 on .NET 10.0.
- **[CS2MenuManager](https://git.miksen.me/mikkel/CS2MenuManager/releases) (1.0.42+)**: OPTIONAL, required only for the in-game menus. The `.matchadmin` / `.ma` admin menu and the `.matchsetup` wizard use its `WasdMenu` UI. MatchZy loads and runs normally without it; only those two menu commands are unavailable and will reply with a notice instead. Install it if you want the in-game menus.

## Installation

1. Install **CounterStrikeSharp** into the server at `game/csgo/addons/counterstrikesharp/`.
2. (Optional, for the in-game menus) Install **CS2MenuManager** as a separate shared plugin at `game/csgo/addons/counterstrikesharp/plugins/CS2MenuManager/`. Download the latest release from its [releases page](https://git.miksen.me/mikkel/CS2MenuManager/releases). Skip this if you do not use `.matchadmin` / `.matchsetup`.
3. Build this plugin:
   ```bash
   dotnet build -c Release
   ```
4. Copy the build output (`bin/Release/net10.0/MatchZy.dll` and its dependencies) to:
   ```
   game/csgo/addons/counterstrikesharp/plugins/MatchZy/
   ```
5. Copy `gamedata/matchzy.json` to the CounterStrikeSharp gamedata directory:
   ```
   game/csgo/addons/counterstrikesharp/gamedata/matchzy.json
   ```
   (The release `.zip` already includes it at this path.)
6. Restart the server, or run `css_plugins reload MatchZy`.

## Gamedata

MatchZy resolves a few game functions by key from CounterStrikeSharp's gamedata (CounterStrikeSharp merges every `*.json` in its `gamedata/` directory). The shipped `gamedata/matchzy.json` provides them, so nothing needs to be added to the core `gamedata.json`, and the plugin works on both stock upstream CounterStrikeSharp and the fork. The required keys are:

- `CCSGameRules_PostCleanUp` - `.breakrestore` (respawn breakable props in practice).
- `CSmokeGrenadeProjectile_Create`, `CHEGrenadeProjectile_Create`, `CMolotovProjectile_Create`, `CDecoyProjectile_Create` - practice grenade rethrow (`.rt` / `.last` / `.back`).
- `CBasePlayerController_HandleCommand_JoinTeam` - spectator-to-team switch in practice (`.t` / `.ct` from spec). The older key name `CCSPlayerController_HandleCommandJoinTeam` is also accepted.
- `CCSPlayer_WeaponServices_SelectItem` (vtable offset) - putting a restored grenade or weapon in hand (`.last` / `.back` / `.ln`).

Signatures shift when Valve updates CS2. If a rethrow or `.breakrestore` stops working after a game update, regenerate the signatures for the new `libserver.so` (Linux) / `server.dll` (Windows) and update `gamedata/matchzy.json`. Missing or stale keys degrade gracefully (the feature no-ops), they do not crash the plugin.

> **Note on CS2MenuManager:** it is a build-time NuGet reference but only a runtime dependency of the menu commands. The menu assembly is resolved lazily on first use, so the plugin loads without it and only `.matchadmin` / `.matchsetup` are affected. If you install it, place it before MatchZy in the load order.
