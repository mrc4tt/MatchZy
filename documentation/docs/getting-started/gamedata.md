# Gamedata

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
