---
title: "Gamedata"
sidebar:
  order: 3
---

MatchZy resolves a few native game functions by name from CounterStrikeSharp's gamedata. CounterStrikeSharp merges every `*.json` in its `gamedata/` directory, so the shipped `gamedata/matchzy.json` provides them without editing the core `gamedata.json`. This is what lets the plugin run on stock CounterStrikeSharp.

| Key | Type | Used by |
|---|---|---|
| `CSmokeGrenadeProjectile_Create` | signature | Practice grenade rethrow (`.rt`, `.last`, `.back`) |
| `CHEGrenadeProjectile_Create` | signature | Practice grenade rethrow |
| `CMolotovProjectile_Create` | signature | Practice grenade rethrow (molotov and incendiary) |
| `CDecoyProjectile_Create` | signature | Practice grenade rethrow |
| `CCSGameRules_PostCleanUp` | signature | `.breakrestore` (respawn breakable props) |
| `CBasePlayerController_HandleCommand_JoinTeam` | signature | `.t` / `.ct` from spectator in practice (the older name `CCSPlayerController_HandleCommandJoinTeam` is also accepted) |
| `CCSPlayer_WeaponServices_SelectItem` | vtable offset | Putting a restored grenade or weapon in hand (`.last`, `.back`, `.ln`) |

## When a key goes stale

Valve updates can move these functions. A stale or missing key degrades gracefully:

- Grenade rethrows fall back to a managed spawn path and log a warning naming the missing key.
- `.breakrestore`, the spectator team switch and the weapon selection turn into no-ops (the team switch falls back to the old behavior plus a team-menu hint).
- The log shows lines such as `CCSPlayer_WeaponServices_SelectItem offset missing`.

The fix is a fresh `matchzy.json` for the new CS2 build (Linux `libserver.so`, Windows `server.dll`). Update MatchZy, or regenerate the signatures yourself and replace the file.

## Automatic updates (Miksen's CounterStrikeSharp fork)

On the CounterStrikeSharp fork MatchZy is built against, the core fixes a stale `matchzy.json` by itself after a CS2 update, before a new MatchZy release is out. It is on by default (`PluginGamedataAutoUpdate` in `configs/core.json`) and uses the gamedata feed set in `AutoUpdateURL`.

- Only signatures that no longer resolve on this server are replaced, and only with a signature from the feed that resolves here. Working keys, offsets and keys the feed does not know stay as they are.
- The feed's manifest is signed and every file is checked against its sha256. Each change is backed up, and a change the server dies with is rolled back on the next start.
- `addons/counterstrikesharp/logs/gamedata-status.json` shows what resolves after the start.
- `PluginGamedataFeeds` in `core.json` names the feed file for a plugin file when the core cannot tell (for example a renamed copy).

On stock CounterStrikeSharp, update MatchZy (or replace `matchzy.json`) as described above.
