# Installation

## Requirements

| Component | Required | Notes |
|---|---|---|
| CS2 dedicated server | Yes | Linux or Windows. |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) | Yes | Stock CounterStrikeSharp works. The fork is developed against a .NET 10 CounterStrikeSharp build. |
| [CS2MenuManager](https://git.miksen.me/mikkel/CS2MenuManager/releases) 1.0.42+ | Optional | Only needed for the in-game menus (`.ma`, `.matchsetup`, `.nades`). Without it MatchZy runs normally and those commands reply with a notice. |
| MySQL / MariaDB | Optional | SQLite is used by default and needs no setup. |

## Install from the release zip

[:material-download: Download MatchZy @@MATCHZY_VERSION@@ (.zip)](https://git.miksen.me/mikkel/matchzy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip){ .md-button .md-button--primary }
[Release notes](https://git.miksen.me/mikkel/matchzy/releases/latest){ .md-button }

1. Install **CounterStrikeSharp** into `game/csgo/addons/counterstrikesharp/`.
2. Download the latest `MatchZy-<version>.zip` (button above, or the [releases page](https://git.miksen.me/mikkel/matchzy/releases)).
3. Extract the zip into `game/csgo/`. It contains:

    ```text
    addons/counterstrikesharp/plugins/MatchZy/            # plugin and its dependencies
    addons/counterstrikesharp/plugins/MatchZy/defaults/   # reference copies of the default cfgs and database.json
    addons/counterstrikesharp/gamedata/matchzy.json       # signatures used by practice mode
    ```

    The zip contains nothing under `cfg/`. MatchZy writes its cfg files itself on first load.

4. *(Optional)* Install **CS2MenuManager** as its own plugin under `addons/counterstrikesharp/plugins/CS2MenuManager/` if you want the in-game menus.
5. Restart the server, or run `css_plugins load MatchZy`.

On first load MatchZy writes `cfg/MatchZy/config.cfg` (all settings, with comments), the mode configs (`live.cfg`, `scrim.cfg`, `prac.cfg`, ...), `admins.json` and `database.json` (SQLite). Edit `config.cfg` to tune the server; see [Configuration](../reference/convars.md).

To update later, see [Updating](updating.md).

!!! tip "Admins"
    MatchZy accepts CounterStrikeSharp admin flags (for example `@css/config`, `@css/map`). You can also list SteamID64s in `cfg/MatchZy/admins.json`; every player listed there is a full MatchZy admin. See [Files and folders](../reference/files.md#adminsjson).

## Build from source

```bash
git clone https://git.miksen.me/mikkel/matchzy.git
cd matchzy
dotnet build -c Release
# output: bin/Release/net10.0/MatchZy.dll (+ dependencies)
```

Copy the build output to `addons/counterstrikesharp/plugins/MatchZy/` and `gamedata/matchzy.json` to `addons/counterstrikesharp/gamedata/`.

The project compiles against a local CounterStrikeSharp API DLL. Point it at your own build with `-p:CssApiDir=/path/to/CounterStrikeSharp.API/bin/Release/net10.0`. Build against the same CounterStrikeSharp version your server runs; a mismatch shows up as `MissingMethodException`, `TypeLoadException` or `EntryPointNotFoundException` at load.

## Config folder name

MatchZy works with either `cfg/MatchZy/` or `cfg/matchzy/`. It detects which one exists (an exact lowercase `matchzy` wins) and uses it for every file, and prints the folder it picked in the server log at startup. Keep only one of the two.
