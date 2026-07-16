> **Mirror notice:** This GitHub repo is a read-only mirror. Development happens on
> [git.miksen.me](https://git.miksen.me/mikkel/matchzy). Please open issues and pull requests there.
> Issues opened here are synced automatically, but the Forgejo repo is canonical.

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

## Requirements

- **CS2 dedicated server** (Linux)
- **[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)**: the plugin framework. This fork targets API 1.0.369 on .NET 10.0.
- **[CS2MenuManager](https://github.com/schwarper/CS2MenuManager) (1.0.42+)**: OPTIONAL, required only for the in-game menus. The `.matchadmin` / `.ma` admin menu and the `.matchsetup` wizard use its `WasdMenu` UI. MatchZy loads and runs normally without it; only those two menu commands are unavailable and will reply with a notice instead. Install it if you want the in-game menus.

## Installation

1. Install **CounterStrikeSharp** into the server at `game/csgo/addons/counterstrikesharp/`.
2. (Optional, for the in-game menus) Install **CS2MenuManager** as a separate shared plugin at `game/csgo/addons/counterstrikesharp/plugins/CS2MenuManager/`. Download the latest release from its [releases page](https://github.com/schwarper/CS2MenuManager/releases). Skip this if you do not use `.matchadmin` / `.matchsetup`.
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

MatchZy resolves a few game functions by key from CounterStrikeSharp's gamedata (CounterStrikeSharp merges every `*.json` in its `gamedata/` directory). The shipped `gamedata/matchzy.json` provides them, so nothing needs to be added to the core `gamedata.json`. The required keys (linux + windows signatures) are:

- `CCSGameRules_PostCleanUp` - `.breakrestore` (respawn breakable props in practice).
- `CSmokeGrenadeProjectile_Create`, `CHEGrenadeProjectile_Create`, `CMolotovProjectile_Create`, `CDecoyProjectile_Create` - practice grenade rethrow (`.rt` / `.last` / `.back`).

Signatures shift when Valve updates CS2. If a rethrow or `.breakrestore` stops working after a game update, regenerate the signatures for the new `libserver.so` (Linux) / `server.dll` (Windows) and update `gamedata/matchzy.json`. Missing or stale keys degrade gracefully (the feature no-ops), they do not crash the plugin.

> **Note on CS2MenuManager:** it is a build-time NuGet reference but only a runtime dependency of the menu commands. The menu assembly is resolved lazily on first use, so the plugin loads without it and only `.matchadmin` / `.matchsetup` are affected. If you install it, place it before MatchZy in load order.
