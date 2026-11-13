---
title: "Installation"
sidebar:
  order: 1
---

## Requirements

| Component | Required | Notes |
|---|---|---|
| CS2 dedicated server | Yes | Linux or Windows. |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) | Yes | Stock CounterStrikeSharp works. The fork is developed against a .NET 10 CounterStrikeSharp build. |
| [CS2MenuManager](https://github.com/mrc4tt/CS2MenuManager/releases) 1.0.42+ | Optional | Only needed for the in-game menus (`.ma`, `.matchsetup`, `.nades`, `.strats`); without it those commands reply with a notice. |
| MySQL / MariaDB | Optional | SQLite is used by default and needs no setup. |

## Install from the release zip

[Download MatchZy @@MATCHZY_VERSION@@ (.zip)](https://github.com/mrc4tt/MatchZy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip)
[Download](https://github.com/mrc4tt/MatchZy/releases/latest) [Changelog](https://github.com/mrc4tt/MatchZy/blob/@@MATCHZY_VERSION@@/CHANGELOG.md)

1. Install **CounterStrikeSharp** into `game/csgo/addons/counterstrikesharp/`.
2. Download the latest `MatchZy-<version>.zip` (button above, or the [releases page](https://github.com/mrc4tt/MatchZy/releases)).
3. Extract the zip into `game/csgo/`. It contains:

    ```text
    addons/counterstrikesharp/plugins/MatchZy/            # plugin and its dependencies
    addons/counterstrikesharp/gamedata/matchzy.json       # signatures used by practice mode
    ```

    The zip contains nothing under `cfg/`. MatchZy writes its cfg files itself on first load, plus reference copies of the defaults in `cfg/MatchZy/defaults/`.

4. *(Optional)* Install **CS2MenuManager** as its own plugin under `addons/counterstrikesharp/plugins/CS2MenuManager/` if you want the in-game menus.
5. Restart the server, or run `css_plugins load MatchZy`.

On first load MatchZy writes `cfg/MatchZy/config.cfg` (all settings, with comments), the mode configs (`live.cfg`, `scrim.cfg`, `prac.cfg`, ...), `admins.json` and `database.json` (SQLite). Edit `config.cfg` to tune the server; see [Configuration](/reference/convars/).

To update later, see [Updating](/getting-started/updating/).

:::tip[Admins]
MatchZy accepts CounterStrikeSharp admin flags (for example `@css/config`, `@css/map`). You can also list SteamID64s in `cfg/MatchZy/admins.json`; every player listed there is a full MatchZy admin. See [Files and folders](/reference/files/#adminsjson).
:::

## Launch options

Add these to the server's start command (or the launch options field in your panel):

```text
-dedicated +map de_dust2 +game_type 0 +game_mode 1 -allow_round_backup_files +tv_enable 1 +tv_enable_dynamic 1
```

| Option | Why |
|---|---|
| `-allow_round_backup_files` | **Required for round backups.** Without it CS2 writes no backup files and `.restore`, `.ma` → Restore Round and crash recovery do not work. |
| `+tv_enable 1` | Turns on GOTV so MatchZy can record demos. Can also go in a cfg. See [Demos](/guides/demos/). |
| `+tv_enable_dynamic 1` | Recommended: a `tv_enable` change takes effect right away instead of on the next map, so MatchZy can bring back a kicked CSTV bot mid-map. |
| `+game_type 0 +game_mode 1` | Competitive. For wingman, rush and 1v1, MatchZy switches the mode itself. See [Game modes](/guides/game-modes/#team-sizes). |
| `+host_workshop_map <id>` / `+host_workshop_collection <id>` | Optional: start on a workshop map, or make a collection's maps available to `.map ws:<name>`. |

## Build from source

```bash
git clone https://github.com/mrc4tt/MatchZy.git
cd MatchZy
dotnet build -c Release
# output: bin/Release/net10.0/MatchZy.dll (+ dependencies)
```

Copy the build output to `addons/counterstrikesharp/plugins/MatchZy/` and `gamedata/matchzy.json` to `addons/counterstrikesharp/gamedata/`.

The project compiles against a local CounterStrikeSharp API DLL. Point it at your own build with `-p:CssApiDir=/path/to/CounterStrikeSharp.API/bin/Release/net10.0`. Build against the same CounterStrikeSharp version your server runs; a mismatch shows up as `MissingMethodException`, `TypeLoadException` or `EntryPointNotFoundException` at load.

## Config folder name

MatchZy works with either `cfg/MatchZy/` or `cfg/matchzy/`. It detects which one exists (an exact lowercase `matchzy` wins) and uses it for every file, and prints the folder it picked in the server log at startup. Keep only one of the two.
