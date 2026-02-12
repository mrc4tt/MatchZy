using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    /// <summary>
    /// Toggle auto-pause on/off
    /// Usage: !autopause or .autopause
    /// </summary>
    [ConsoleCommand("css_autopause", "Toggle auto-pause feature on/off")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAutoPauseCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsPlayerAdmin(player, "css_autopause"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command.");
            return;
        }

        // Toggle the setting
        autoPauseEnabled.Value = !autoPauseEnabled.Value;

        string status = autoPauseEnabled.Value ? $"{ChatColors.Green}ENABLED{ChatColors.Default}" : $"{ChatColors.Red}DISABLED{ChatColors.Default}";
        string minPlayers = autoPauseMinPlayers.Value.ToString();

        ReplyToUserCommand(player, $"Auto-pause is now {status}");

        if (autoPauseEnabled.Value)
        {
            ReplyToUserCommand(player, $"Match will auto-pause when teams have fewer than {ChatColors.Green}{minPlayers}{ChatColors.Default} players");
        }

        // Announce to all players
        PrintToAllChat($"{ChatColors.Gold}[ADMIN]{ChatColors.Default} Auto-pause has been {status} by {player!.PlayerName}");

        Log($"[AutoPause] {player.PlayerName} toggled auto-pause: {autoPauseEnabled.Value}");
    }

    /// <summary>
    /// Set minimum players for auto-pause
    /// Usage: !autopause_minplayers <number> or .autopause_minplayers <number>
    /// </summary>
    [ConsoleCommand("css_autopause_minplayers", "Set minimum players required before auto-pause triggers")]
    [CommandHelper(minArgs: 1, usage: "<number>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAutoPauseMinPlayersCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsPlayerAdmin(player, "css_autopause_minplayers"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command.");
            return;
        }

        if (command.ArgCount < 2)
        {
            ReplyToUserCommand(player, $"Current minimum players: {ChatColors.Green}{autoPauseMinPlayers.Value}{ChatColors.Default}");
            ReplyToUserCommand(player, $"Usage: !autopause_minplayers <number>");
            return;
        }

        if (!int.TryParse(command.ArgByIndex(1), out int minPlayers) || minPlayers < 1 || minPlayers > 5)
        {
            ReplyToUserCommand(player, "Invalid number. Please use a value between 1 and 5.");
            return;
        }

        int oldValue = autoPauseMinPlayers.Value;
        autoPauseMinPlayers.Value = minPlayers;

        ReplyToUserCommand(player, $"Minimum players changed from {ChatColors.Yellow}{oldValue}{ChatColors.Default} to {ChatColors.Green}{minPlayers}{ChatColors.Default}");

        // Announce to all players
        PrintToAllChat($"{ChatColors.Gold}[ADMIN]{ChatColors.Default} Auto-pause threshold set to {ChatColors.Green}{minPlayers}{ChatColors.Default} players by {player!.PlayerName}");

        Log($"[AutoPause] {player.PlayerName} changed min players from {oldValue} to {minPlayers}");
    }

    /// <summary>
    /// Set auto-resume delay
    /// Usage: !autopause_delay <seconds> or .autopause_delay <seconds>
    /// </summary>
    [ConsoleCommand("css_autopause_delay", "Set delay before auto-resuming when teams are balanced")]
    [CommandHelper(minArgs: 1, usage: "<seconds>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAutoPauseDelayCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsPlayerAdmin(player, "css_autopause_delay"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command.");
            return;
        }

        if (command.ArgCount < 2)
        {
            ReplyToUserCommand(player, $"Current auto-resume delay: {ChatColors.Green}{autoResumeDelay.Value}{ChatColors.Default} seconds");
            ReplyToUserCommand(player, $"Usage: !autopause_delay <seconds>");
            return;
        }

        if (!int.TryParse(command.ArgByIndex(1), out int delay) || delay < 0 || delay > 30)
        {
            ReplyToUserCommand(player, "Invalid number. Please use a value between 0 and 30 seconds.");
            return;
        }

        int oldValue = autoResumeDelay.Value;
        autoResumeDelay.Value = delay;

        ReplyToUserCommand(player, $"Auto-resume delay changed from {ChatColors.Yellow}{oldValue}s{ChatColors.Default} to {ChatColors.Green}{delay}s{ChatColors.Default}");

        // Announce to all players
        PrintToAllChat($"{ChatColors.Gold}[ADMIN]{ChatColors.Default} Auto-resume delay set to {ChatColors.Green}{delay}{ChatColors.Default} seconds by {player!.PlayerName}");

        Log($"[AutoPause] {player.PlayerName} changed auto-resume delay from {oldValue}s to {delay}s");
    }

    /// <summary>
    /// Show current auto-pause settings
    /// Usage: !autopause_status or .autopause_status
    /// </summary>
    [ConsoleCommand("css_autopause_status", "Show current auto-pause settings")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAutoPauseStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsPlayerAdmin(player, "css_autopause_status"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command.");
            return;
        }

        string enabled = autoPauseEnabled.Value ? $"{ChatColors.Green}ENABLED{ChatColors.Default}" : $"{ChatColors.Red}DISABLED{ChatColors.Default}";
        string minPlayers = autoPauseMinPlayers.Value.ToString();
        string delay = autoResumeDelay.Value.ToString();

        ReplyToUserCommand(player, $"{ChatColors.Gold}=== Auto-Pause Settings ==={ChatColors.Default}");
        ReplyToUserCommand(player, $"Status: {enabled}");
        ReplyToUserCommand(player, $"Min Players: {ChatColors.Green}{minPlayers}{ChatColors.Default}");
        ReplyToUserCommand(player, $"Resume Delay: {ChatColors.Green}{delay}{ChatColors.Default} seconds");

        if (isAutoPaused)
        {
            ReplyToUserCommand(player, $"{ChatColors.Yellow}Currently auto-paused:{ChatColors.Default} {autoPauseReason}");
        }
    }

    /// <summary>
    /// Force trigger auto-pause check (for testing)
    /// Usage: !autopause_check or .autopause_check
    /// </summary>
    [ConsoleCommand("css_autopause_check", "Manually trigger auto-pause check (for testing)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAutoPauseCheckCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsPlayerAdmin(player, "css_autopause_check"))
        {
            ReplyToUserCommand(player, "You do not have permission to use this command.");
            return;
        }

        if (!isMatchLive)
        {
            ReplyToUserCommand(player, "Match is not live. Auto-pause only works during live matches.");
            return;
        }

        int ctCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
        int tCount = GetTeamPlayerCount(CsTeam.Terrorist);
        int minPlayers = autoPauseMinPlayers.Value;

        ReplyToUserCommand(player, $"{ChatColors.Gold}=== Auto-Pause Check ==={ChatColors.Default}");
        ReplyToUserCommand(player, $"CT Players: {ChatColors.Green}{ctCount}{ChatColors.Default}/{minPlayers}");
        ReplyToUserCommand(player, $"T Players: {ChatColors.Green}{tCount}{ChatColors.Default}/{minPlayers}");
        ReplyToUserCommand(player, $"Auto-pause: {(autoPauseEnabled.Value ? $"{ChatColors.Green}ENABLED{ChatColors.Default}" : $"{ChatColors.Red}DISABLED{ChatColors.Default}")}");

        if (ctCount < minPlayers || tCount < minPlayers)
        {
            ReplyToUserCommand(player, $"{ChatColors.Yellow}Teams are unbalanced - auto-pause should trigger{ChatColors.Default}");
        }
        else
        {
            ReplyToUserCommand(player, $"{ChatColors.Green}Teams are balanced - no auto-pause needed{ChatColors.Default}");
        }

        // Force check
        CheckAutoResumeOrAutoPause();

        Log($"[AutoPause] {player!.PlayerName} manually triggered auto-pause check");
    }
}
