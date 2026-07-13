## A forked MatchZy plugin - customized

Customized [MatchZy](https://github.com/shobhit-pathak/MatchZy) fork for CS2 competitive servers. Adds a remote log API, G5API compatibility, auto changelevel, advanced stats, a coach system, pause overhauls, in-game admin and match-setup menus, and in-game stats commands.

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
5. Restart the server, or run `css_plugins reload MatchZy`.

> **Note on CS2MenuManager:** it is a build-time NuGet reference but only a runtime dependency of the menu commands. The menu assembly is resolved lazily on first use, so the plugin loads without it and only `.matchadmin` / `.matchsetup` are affected. If you install it, place it before MatchZy in load order.
