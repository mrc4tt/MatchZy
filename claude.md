# MatchZy

> A customized fork of MatchZy for CS2 competitive servers.

## Project Overview

MatchZy is a CounterStrikeSharp plugin based on [MatchZy](https://github.com/shobhit-pathak/MatchZy) - a match management plugin for Counter-Strike 2 competitive servers. This fork includes customizations and integrations specific to game server hosting.

## Tech Stack

- **Framework**: [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) (.NET 8.0)
- **Language**: C# 12
- **Target**: Counter-Strike 2 dedicated servers (Linux)
- **API Version**: CounterStrikeSharp API 1.0.360+

## Project Structure

```
MatchZy/
├── src/
│   ├── MatchZy.cs          # Main plugin entry point
│   ├── Config/                 # Configuration classes
│   ├── Commands/               # Console/chat commands
│   ├── Events/                 # Game event handlers
│   ├── Services/               # Business logic services
│   └── Utils/                  # Helper utilities
├── lang/                       # Localization files (JSON)
├── cfg/                        # Server config files
└── MatchZy.csproj          # Project file
```

## Code Conventions

### Plugin Structure

```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public class MatchZy : BasePlugin
{
    public override string ModuleName => "MatchZy";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Miksen";
    
    public override void Load(bool hotReload)
    {
        // Plugin initialization
    }
}
```

### Command Registration

```csharp
[ConsoleCommand("css_ready", "Mark yourself as ready")]
[CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
public void OnReadyCommand(CCSPlayerController? player, CommandInfo command)
{
    if (player == null || !player.IsValid) return;
    // Command logic
}
```

### Event Handlers

```csharp
[GameEventHandler]
public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
{
    var player = @event.Userid;
    if (player == null || !player.IsValid) return HookResult.Continue;
    
    // Handle player connection
    return HookResult.Continue;
}
```

### Configuration Pattern

```csharp
public class MatchConfig : BasePluginConfig
{
    [JsonPropertyName("knife_round")]
    public bool KnifeRound { get; set; } = true;
    
    [JsonPropertyName("ready_required_players")]
    public int ReadyRequiredPlayers { get; set; } = 5;
}
```

## Key APIs

### Player Management

```csharp
// Get all players
Utilities.GetPlayers()

// Check player validity
player.IsValid && player.Connected == PlayerConnectedState.PlayerConnected

// Get player team
player.Team // CsTeam.Terrorist, CsTeam.CounterTerrorist, CsTeam.Spectator

// Player actions
player.PrintToChat("Message");
player.PrintToCenter("Center message");
Server.ExecuteCommand($"kickid {player.UserId}");
```

### Server Commands

```csharp
Server.ExecuteCommand("mp_restartgame 1");
Server.NextFrame(() => { /* Deferred execution */ });
```

### Timer Management

```csharp
AddTimer(5.0f, () => {
    // Execute after 5 seconds
}, TimerFlags.STOP_ON_MAPCHANGE);
```

## Common Patterns

### Null Safety
Always validate player controllers before use:
```csharp
if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
    return;
```

### Team Iteration
```csharp
foreach (var player in Utilities.GetPlayers()
    .Where(p => p.IsValid && p.Team == CsTeam.Terrorist))
{
    // Process terrorist players
}
```

### Localization
```csharp
player.PrintToChat(Localizer["match.started"]);
```

## Build & Deploy

```bash
# Build the plugin
dotnet build -c Release

# Output location
bin/Release/net8.0/MatchZy.dll
```

## Testing

Deploy to a test CS2 server:
```
/game/csgo/addons/counterstrikesharp/plugins/MatchZy/
├── MatchZy.dll
├── lang/
│   └── en.json
└── cfg/
    └── config.json
```

## Dependencies

- CounterStrikeSharp.API (NuGet)
- Metamod:Source (server requirement)

## References

- [CounterStrikeSharp Docs](https://docs.cssharp.dev/)
- [MatchZy Documentation](https://shobhit-pathak.github.io/MatchZy/)
