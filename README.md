# MikZy (MatchZy)

[![Latest release](https://img.shields.io/badge/release-latest-2ea44f)](https://git.miksen.me/mikkel/matchzy/releases/latest)
[![CS2](https://img.shields.io/badge/game-CS2-orange)](https://www.counter-strike.net/)
[![CounterStrikeSharp](https://img.shields.io/badge/CounterStrikeSharp-.NET%2010-512bd4)](https://github.com/roflmuffin/CounterStrikeSharp)
[![Docs](https://img.shields.io/badge/docs-matchzy.miksen.me-blue)](https://matchzy.miksen.me/)

MikZy is a CounterStrikeSharp plugin for running CS2 matches, scrims, pugs and practice on your own server. It is a customized fork of [MatchZy](https://github.com/shobhit-pathak/MatchZy) built for game-server hosting. The plugin still loads as `MatchZy`, so file names, folders, commands and settings (`MatchZy.dll`, `cfg/MatchZy/`, `matchzy_*`) are unchanged.

> **Looking for the official MatchZy?** Use [MatchZy by WD-](https://github.com/shobhit-pathak/MatchZy). This fork is an optional alternative for those who want its extra features, for example for tournaments, leagues or community servers. Feature requests and issues are welcome, but there is no guarantee they will be picked up or implemented.

**Documentation:** <https://matchzy.miksen.me/>

## What the fork adds

- **Live events API:** every round, kill, pause and map result posted as JSON to `matchzy_remote_log_url`, for panels, bots and scoreboards.
- **Get5 / G5API compatibility:** load matches from a URL, `get5_status`, `get5_endmatch` and the Get5 match config format.
- **Advanced stats:** HLTV 2.0 rating, KAST, clutches and opening duels, saved to SQLite or MySQL and exported as CSV and JSON.
- **Coaches:** per-team coach lists in the match config, invisible coach slots and hand-tuned viewing spots per map.
- **Pauses:** tactical and technical pauses with per-team budgets, overtime timeouts and automatic pausing when a player disconnects.
- **In-game menus:** `.ma` admin menu and a `.matchsetup` wizard that builds a match without writing JSON.
- **Practice:** grenade history with rethrow, saved and shared lineups, bots, spawns, timers and fast forward.
- **Players vs bots:** a match config can put a bot team against an open team of humans.

## Install

1. Install [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) on the server.
2. Download the latest `MatchZy-<version>.zip` from the [releases page](https://git.miksen.me/mikkel/matchzy/releases/latest).
3. Extract it into `game/csgo/` and restart the server.

MatchZy writes its config files to `csgo/cfg/MatchZy/` on first load. Edit `config.cfg` there to tune the server.

*Optional:* install [CS2MenuManager](https://git.miksen.me/mikkel/CS2MenuManager/releases) (1.0.42 or newer) for the in-game menus. Everything else works without it.

Full guide: [Installation](https://matchzy.miksen.me/getting-started/installation/)

## Update

Extract the new zip over the old one and restart. The zip contains nothing under `cfg/`, so your config files, admins, database settings and saved lineups are never overwritten. New settings are added to the bottom of your `config.cfg` automatically, and reference copies of the current default cfgs are written to `cfg/MatchZy/defaults/` on every load.

Details: [Updating](https://matchzy.miksen.me/getting-started/updating/)

## Requirements

| Component | Required | Notes |
|---|---|---|
| CS2 dedicated server | Yes | Linux or Windows. |
| CounterStrikeSharp | Yes | Stock CounterStrikeSharp works. Developed against a .NET 10 build. |
| CS2MenuManager 1.0.42+ | No | Only for `.ma`, `.matchsetup` and `.nades`. |
| MySQL / MariaDB | No | SQLite is used by default. |

## Documentation

- [Commands](https://matchzy.miksen.me/reference/commands/)
- [Settings (convars)](https://matchzy.miksen.me/reference/convars/)
- [Match config](https://matchzy.miksen.me/reference/match-config/)
- [Events](https://matchzy.miksen.me/reference/events/)
- [Coaching](https://matchzy.miksen.me/guides/coaching/)
- [Practice mode](https://matchzy.miksen.me/guides/practice/)
- [What's new](https://matchzy.miksen.me/whats-new/) and the [changelog](CHANGELOG.md)

## Build from source

```bash
git clone https://git.miksen.me/mikkel/matchzy.git
cd matchzy
dotnet build -c Release
```

The output is `bin/Release/net10.0/MatchZy.dll` with its dependencies. The project compiles against a local CounterStrikeSharp API DLL; point it at yours with `-p:CssApiDir=/path/to/CounterStrikeSharp.API/bin/Release/net10.0`, and build against the same CounterStrikeSharp version the server runs.

## Credits

Based on [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak (WD-). This fork is developed independently and no longer tracks the upstream changelog.
