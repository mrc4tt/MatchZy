# MatchZy — Miksen Fork

> Customized MatchZy fork for CS2 competitive servers

## Project Overview

MatchZy is a CounterStrikeSharp plugin for CS2 match management — warmup, knife rounds, ready system, live matches, scrims, practice mode, backup/restore, map veto, demo recording, and live scorebot events. This fork adds custom integrations for game server hosting (remote log API, G5API compat, auto changelevel, advanced stats, coach system, pause overhauls, etc.).

## Tech Stack

- **Framework**: CounterStrikeSharp API 1.0.363 (.NET 8.0, C# 12)
- **Database**: SQLite (default) or MySQL via Dapper
- **Serialization**: Newtonsoft.Json (match configs), System.Text.Json (events/stats)
- **CSV Export**: CsvHelper 33.1.0
- **Target**: CS2 dedicated servers (Linux), deployed via Pterodactyl Panel + Wings

## Architecture

This is a **single partial class** (`MatchZy : BasePlugin`) split across ~20 files. There are no folders/namespaces for sub-concerns — all `.cs` files live flat in the repo root and share state via fields on the partial class.

```
matchzy/                       # Repo root (== project root, no src/ subfolder)
├── MatchZy.cs                 # Entry point: Load(), event registrations, command map, inline event handlers
├── MatchZy.csproj             # .NET 8.0 project file
├── EventHandlers.cs           # Named event handler methods (connect, disconnect, round start/end, entity spawn, etc.)
├── MatchManagement.cs         # Match setup: LoadMatch JSON/URL, team management, series end, map change
├── Utility.cs                 # 3500+ lines: StartWarmup, StartLive, ResetMatch, ChangeMap, HandleMatchEnd, AutoStart, cfg builders, helpers
├── ConsoleCommands.cs         # css_* console commands + admin commands
├── ConfigConvars.cs           # FakeConVar definitions for plugin settings
├── ConfigManager.cs           # Hardcoded CFG string builders for all game modes (live/scrim/hill/warmup/prac/sleep)
├── ConfigFiles.cs             # Static path constants for cfg files
├── PracticeMode.cs            # 3800+ lines: full practice mode (bots, nades, spawns, timers, rethrow, dryrun)
├── BackupManagement.cs        # Round backup save/restore system
├── Pausing.cs                 # Tactical + technical pause system
├── AutoPauseCommands.cs       # Auto-pause on player disconnect
├── Coach.cs                   # Coach slot management (invisible players, team assignment)
├── DamageInfo.cs              # Per-round damage tracking + display
├── DatabaseStats.cs           # SQLite/MySQL schema, match init, player stats, CSV export
├── DemoManagement.cs          # GOTV demo recording start/stop
├── Events.cs                  # Event data classes (MatchZyEvent hierarchy + live scorebot DTOs)
├── PublishEvents.cs           # SendEventAsync — HTTP POST events to remote log URL
├── AdvancedStats.cs           # HLTV 2.0 rating, KAST, clutch tracking, opening duels
├── FFWSystem.cs               # Forfeit/walkover system
├── GGSystem.cs                # Surrender/vote system — DISABLED (excluded from build via <Compile Remove> in csproj)
├── MapVeto.cs                 # Map ban/pick veto flow
├── ReadySystem.cs             # Ready check logic
├── Teams.cs                   # Team class definition
├── MatchConfig.cs             # MatchConfig class
├── MatchData.cs               # MatchData / StatsPlayer classes
├── RemoteLogConfig.cs         # Remote log URL/header config
├── G5API.cs                   # Get5 API compatibility layer
├── SleepMode.cs               # Idle/sleep server mode
├── GrenadeProjectiles.cs      # Grenade projectile wrapper
├── GrenadeThrownData.cs       # Grenade throw data class
├── SmokeGrenadeProjectile.cs  # Smoke projectile wrapper
├── PlayerLocationData.cs      # Position data class
├── PlayerPracticeTimer.cs     # Practice timer class
├── Constants.cs               # Projectile type mappings
├── SynchronizationContextManagement.cs # Dead code — excluded from build via <Compile Remove> in csproj
├── cfg/                       # Server config files (warmup, knife, live, live_wingman, scrim, hill, prac, dryrun, sleep, config, matchzymaps)
├── lang/                      # Localization (en.json, da.json, sq.json)
└── spawns/coach/              # Coach spawn position JSONs per map
```

## Key State Machine

The plugin operates as a state machine driven by boolean flags:

```
SLEEP (isSleep=true)
  ↓ .match / autostart
WARMUP (isWarmup=true, readyAvailable=true)
  ↓ all players ready
KNIFE ROUND (isKnifeRound=true, matchStarted=true)
  ↓ round ends → knife winner decided
SIDE SELECTION (isSideSelectionPhase=true)
  ↓ .stay / .switch / timeout
LIVE (isMatchLive=true, matchStarted=true)
  ↓ match ends (EventCsWinPanelMatch or all rounds played)
MATCH END → EndSeries() or next map in series
  ↓
WARMUP (reset) or MAP CHANGE
```

Parallel modes: `isPractice`, `isDryRun`, `isPlayOutEnabled`, `isPlayOutEnabled2`, `isMatchSetup`, `isVeto`, `isPreVeto`

## Code Conventions

### Null Safety — Required Pattern
```csharp
if (!IsPlayerValid(player)) return HookResult.Continue;
// IsPlayerValid checks: not-null, IsValid, Connected == PlayerConnected, PlayerPawn valid + non-null
// (Does NOT check UserId.HasValue — check that separately when you need player.UserId.Value.)

// When you need a connected HUMAN player (excludes bots + HLTV), use:
if (!IsHumanPlayerValid(player)) return HookResult.Continue;
// Equivalent to: IsPlayerValid(player) && !player.IsBot && !player.IsHLTV
```

### Event Handler Pattern
```csharp
RegisterEventHandler<EventSomething>((@event, info) =>
{
    try
    {
        // Early-exit guard
        if (!matchStarted) return HookResult.Continue;
        // Logic here
        return HookResult.Continue;
    }
    catch (Exception e)
    {
        Log($"[EventSomething FATAL] {e.Message}");
        return HookResult.Continue;
    }
}, HookMode.Post); // or HookMode.Pre
```

### Timer Pattern
```csharp
// One-shot timer (auto-disposed)
AddTimer(5.0f, () => { /* ... */ });

// Repeating timer (MUST be killed manually or use STOP_ON_MAPCHANGE)
myTimer = AddTimer(1.0f, MyCallback, TimerFlags.REPEAT);
// Kill before reassigning:
myTimer?.Kill();
myTimer = null;
```

### Async Pattern — Task.Run for I/O, NEVER touch Server.* inside
```csharp
// CORRECT: Capture all native data BEFORE Task.Run
long matchId = liveMatchId;
string mapName = Server.MapName;
Task.Run(async () =>
{
    await SendEventAsync(someEvent);           // HTTP I/O — safe
    await database.SomeQueryAsync(matchId);    // DB I/O — safe
    // NEVER: Server.ExecuteCommand() or Utilities.GetPlayers() here — CRASH
});
```

### Command Registration
```csharp
// Console commands (server + client)
[ConsoleCommand("css_ready", "Mark yourself as ready")]
[CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
public void OnReadyCommand(CCSPlayerController? player, CommandInfo command) { }

// Chat commands via dictionary lookup in EventPlayerChat handler
commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>
{
    { ".ready", OnPlayerReady },
    { ".r", OnRCommand },
    // ...
};
```

## Dependencies & NuGet

| Package | Version | Purpose |
|---------|---------|---------|
| CounterStrikeSharp.API | 1.0.364 | Core CSS framework |
| Newtonsoft.Json | 13.0.3 | Match config JSON parsing (JObject/JToken) |
| Dapper | 2.1.72 | Lightweight DB ORM |
| Microsoft.Data.Sqlite | 8.0.0 | SQLite provider |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.6 | Native SQLite binaries |
| MySqlConnector | 2.4.0 | MySQL provider |
| CsvHelper | 33.1.0 | Player stats CSV export |

## Build & Deploy

```bash
dotnet build -c Release
# Output: bin/Release/net8.0/MatchZy.dll + dependencies
# Deploy to: /game/csgo/addons/counterstrikesharp/plugins/MatchZy/
```

---

## References

- [CounterStrikeSharp Docs](https://docs.cssharp.dev/)
- [MatchZy Upstream Docs](https://shobhit-pathak.github.io/MatchZy/)
- [CS2 Server ConVar Reference](https://developer.valvesoftware.com/wiki/List_of_Counter-Strike_2_console_commands_and_variables)
