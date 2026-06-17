# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **Response style:** use `/caveman ultra` — terse, abbreviated prose (DB/auth/config/req/res/fn/impl), arrows for causality, drop articles/filler. Code, commits, PRs, and security warnings stay normal prose.

# MatchZy -- Miksen Fork

> Customized MatchZy fork for CS2 competitive servers

## Project Overview

MatchZy is a CounterStrikeSharp plugin for CS2 match management — warmup, knife rounds, ready system, live matches, scrims, practice mode, backup/restore, map veto, demo recording, and live scorebot events. This fork adds custom integrations for game server hosting: remote log API, G5API compat, auto changelevel, advanced stats, coach system, pause overhauls, in-game admin/match-setup menus, and in-game stats commands.

## Tech Stack

- **Framework**: CounterStrikeSharp API 1.0.369 (.NET 10.0, C# 14)
- **Database**: SQLite (default) or MySQL via Dapper
- **Serialization**: Newtonsoft.Json (match configs), System.Text.Json (events/stats)
- **In-game menus**: CS2MenuManager 1.0.42 (`WasdMenu`)
- **CSV Export**: CsvHelper 33.1.0
- **Target**: CS2 dedicated servers (Linux)

## Architecture

This is a **single partial class** (`MatchZy : BasePlugin`) split across ~40 files. There are no folders/namespaces for sub-concerns — all `.cs` files live flat in the repo root and share state via fields on the partial class. Every file is `namespace MatchZy { public partial class MatchZy { ... } }`.

```
matchzy/                       # Repo root (== project root, no src/ subfolder)
├── MatchZy.cs                 # Entry point: Load(), event registrations, commandActions map, inline event handlers
├── MatchZy.csproj             # .NET 10.0 project file
├── EventHandlers.cs           # Named event handler methods (connect, disconnect, round start/end, entity spawn, etc.)
├── MatchManagement.cs         # Match setup: LoadMatch JSON/URL, team management, series end, map change
├── MatchSetupWizard.cs        # In-game .matchsetup wizard — WasdMenu flow that builds match JSON, calls LoadMatchFromJSON
├── Utility.cs                 # 3500+ lines: StartWarmup, StartLive, ResetMatch, ChangeMap, HandleMatchEnd, AutoStart, cfg builders, helpers
├── ConsoleCommands.cs         # css_* console commands + admin commands
├── StatsCommands.cs           # .lastmatch / .stats in-game commands — async DB reads, prints scoreboards to chat
├── AdminMenu.cs               # .matchadmin (.ma) WasdMenu — match control / pause / modes
├── MatchSummaryPanel.cs       # End-of-match center-HTML summary panel (MVP, clutch king, top frags)
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
├── GGSystem.cs                # Surrender / GG-vote system — COMPILED & ACTIVE (css_gg). See note below.
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
├── SynchronizationContextManagement.cs # SourceSynchronizationContext / SyncContextScope helpers — compiled but unused (no callers)
├── cfg/                       # Server config files (warmup, knife, live, live_wingman, scrim, hill, prac, dryrun, sleep, config, matchzymaps)
├── lang/                      # Localization (en.json, da.json, sq.json)
└── spawns/coach/              # Coach spawn position JSONs per map (10 maps: ancient, ancient_night, anubis, dust2, inferno, mirage, nuke, overpass, train, vertigo)
```

> **Build inclusion:** the `.csproj` no longer has any `<Compile Remove>` entries — every `.cs` file in the repo root is compiled. `GGSystem.cs` and `SynchronizationContextManagement.cs` were excluded in older revisions; that exclusion is gone. `GGSystem.cs` now compiles and its `css_gg` console command is registered (the `.gg` chat alias is still commented out in `commandActions`). `SynchronizationContextManagement.cs` compiles but has no callers.

## Key State Machine

The plugin operates as a state machine driven by boolean flags on the partial class:

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
// IsPlayerValid checks: not-null, IsValid, Connected == PlayerConnectedState.Connected, PlayerPawn valid + non-null
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
string gameDir = Server.GameDirectory;   // even ModuleDirectory / GameDirectory must be captured first
Task.Run(async () =>
{
    await SendEventAsync(someEvent);           // HTTP I/O — safe
    await database.SomeQueryAsync(matchId);    // DB I/O — safe
    // NEVER: Server.ExecuteCommand() or Utilities.GetPlayers() here — CRASH
    // To touch the game again, marshal back: Server.NextFrame(() => { ... });
});
```

### Command Registration
```csharp
// Console commands (server + client). CSS auto-registers [ConsoleCommand] on plugin methods.
[ConsoleCommand("css_ready", "Mark yourself as ready")]
[CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
public void OnReadyCommand(CCSPlayerController? player, CommandInfo command) { }

// Chat commands via dictionary lookup in EventPlayerChat handler (MatchZy.cs ~line 361)
commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>
{
    { ".ready", OnPlayerReady },
    { ".r", OnRCommand },
    // ...
};
```

### In-game Menus — CS2MenuManager `WasdMenu`
```csharp
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;

var menu = new WasdMenu("Title", this);
menu.AddItem("Label", (p, option) => { /* handler */ });
menu.Display(player, 0);

// Submenu — set PrevMenu to get a built-in back button:
var sub = new WasdMenu("Sub", this) { PrevMenu = menu };
```
When opening a menu from a **chat-command dispatch** (a `.command` routed via `commandActions`), defer the open with `Server.NextFrame(() => OpenMenu(player))` — otherwise the menu render is clobbered by the chat line still broadcasting on the same tick. See `AdminMenu.cs` / `MatchSetupWizard.cs`.

## Dependencies & NuGet

| Package | Version | Purpose |
|---------|---------|---------|
| CounterStrikeSharp.API | 1.0.369 | Core CSS framework |
| Newtonsoft.Json | 13.0.3 | Match config JSON parsing (JObject/JToken) |
| Dapper | 2.1.72 | Lightweight DB ORM |
| Microsoft.Data.Sqlite | 8.0.0 | SQLite provider |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.6 | Native SQLite binaries |
| MySqlConnector | 2.4.0 | MySQL provider |
| CsvHelper | 33.1.0 | Player stats CSV export |
| CS2MenuManager | 1.0.42 | In-game `WasdMenu` UI (admin menu, match setup wizard) |

## Build & Deploy

```bash
dotnet build -c Release
# Output: bin/Release/net10.0/MatchZy.dll + dependencies
# Deploy to: /game/csgo/addons/counterstrikesharp/plugins/MatchZy/
```

There is **no test suite or lint tooling** — `dotnet build -c Release` is the only automated correctness check. The plugin only runs inside a live CS2 dedicated server with CounterStrikeSharp installed; it cannot be run or exercised locally.

## Working with Claude Code Skills

This is a **CounterStrikeSharp** plugin. Skills useful here, and when to reach for them:

| Skill | Use it for |
|-------|------------|
| `code-review` | Review the current uncommitted diff for correctness bugs before committing. Good after any non-trivial change to event handlers / async / state flags. |
| `security-review` | Audit branch changes for vulnerabilities — relevant for the remote-log HTTP API, DB queries (SQL injection), and any external input parsing (match JSON/URL loading). |
| `review` | Review a GitHub PR. |
| `verify` / `run` | Limited value here — the plugin needs a live CS2 server. Cannot launch the app locally; do not claim a change is "verified" without server testing. State that explicitly. |
| `caveman-commit` | Generate a compressed Conventional-Commits message when committing. |
| `init` | (Re)generate this CLAUDE.md. |

**Do NOT use the `swiftlys2` skill for this repo.** SwiftlyS2 is a *different* CS2 server-mod framework. This project targets **CounterStrikeSharp** (API 1.0.369, .NET 10.0) — both frameworks now run on .NET 10, so do not distinguish them by runtime version; distinguish by API surface (`CounterStrikeSharp.API.*` here vs `SwiftlyS2.CS2.*`). The two APIs are not interchangeable. The `swiftlys2` skill only applies under `/home/mikkel/Hentet/swiftlys2-cs2-plugins/` or when explicitly porting to SwiftlyS2.

> **Runtime note:** the server runs a **forked CounterStrikeSharp built for .NET 10** at `~/CounterStrikeSharp` (stock CSS 1.0.x is .NET 8). MatchZy references NuGet `CounterStrikeSharp.API 1.0.369`; if a *consistent* `MissingMethod`/`TypeLoad` load failure appears, the NuGet API has drifted from the fork's runtime ABI — rebuild against the fork's own API DLLs rather than NuGet.

For CounterStrikeSharp API questions, consult the docs link below rather than guessing — the API surface (especially enum names like `PlayerConnectedState`) has churned between minor versions.

---

## References

- [CounterStrikeSharp Docs](https://docs.cssharp.dev/)
- [MatchZy Upstream Docs](https://shobhit-pathak.github.io/MatchZy/)
- [CS2 Server ConVar Reference](https://developer.valvesoftware.com/wiki/List_of_Counter-Strike_2_console_commands_and_variables)
