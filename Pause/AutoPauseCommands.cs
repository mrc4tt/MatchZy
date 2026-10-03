using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
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
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.dontpermission"));
            return;
        }

        // Toggle the setting
        autoPauseEnabled.Value = !autoPauseEnabled.Value;

        // Apply it now, not from the next map: start the check on a live map, or stop it and lift an
        // auto-pause in progress (with the check off it would never resume on its own).
        if (autoPauseEnabled.Value)
        {
            if (isMatchLive)
                StartAutoPauseMonitor();
        }
        else
        {
            autoPauseCheckTimer?.Kill();
            autoPauseCheckTimer = null;
            autoResumeTimer?.Kill();
            autoResumeTimer = null;
            if (IsCurrentPauseAuto())
            {
                isAutoPaused = false;
                autoPauseReason = null;
                UnpauseMatch();
                unpauseData["pauseTeam"] = "";
            }
        }

        string status = Localizer.ForPlayer(player, autoPauseEnabled.Value ? "matchzy.pausemsg.enabled" : "matchzy.pausemsg.disabled");
        string minPlayers = autoPauseMinPlayers.Value.ToString();

        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.autopausenow", status));

        if (autoPauseEnabled.Value)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.autopausethreshold", minPlayers));
        }

        // Announce to all players
        PrintLocalizedToAll(autoPauseEnabled.Value ? "matchzy.pausemsg.adminenabledautopause" : "matchzy.pausemsg.admindisabledautopause", player!.PlayerName);

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
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.dontpermission"));
            return;
        }

        if (command.ArgCount < 2)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.currentminplayers", autoPauseMinPlayers.Value));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", "!autopause_minplayers <number>"));
            return;
        }

        if (!int.TryParse(command.ArgByIndex(1), out int minPlayers) || minPlayers < 1 || minPlayers > 5)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.invalidminplayers"));
            return;
        }

        int oldValue = autoPauseMinPlayers.Value;
        autoPauseMinPlayers.Value = minPlayers;

        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.minplayerschanged", oldValue, minPlayers));

        // Announce to all players
        PrintLocalizedToAll("matchzy.pausemsg.adminsetminplayers", minPlayers, player!.PlayerName);

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
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.dontpermission"));
            return;
        }

        if (command.ArgCount < 2)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.currentresumedelay", autoResumeDelay.Value));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", "!autopause_delay <seconds>"));
            return;
        }

        if (!int.TryParse(command.ArgByIndex(1), out int delay) || delay < 0 || delay > 30)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.invalidresumedelay"));
            return;
        }

        int oldValue = autoResumeDelay.Value;
        autoResumeDelay.Value = delay;

        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.resumedelaychanged", oldValue, delay));

        // Announce to all players
        PrintLocalizedToAll("matchzy.pausemsg.adminsetresumedelay", delay, player!.PlayerName);

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
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.dontpermission"));
            return;
        }

        string enabled = Localizer.ForPlayer(player, autoPauseEnabled.Value ? "matchzy.pausemsg.enabled" : "matchzy.pausemsg.disabled");
        string minPlayers = autoPauseMinPlayers.Value.ToString();
        string delay = autoResumeDelay.Value.ToString();

        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.settingsheader"));
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.settingsstatus", enabled));
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.settingsminplayers", minPlayers));
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.settingsresumedelay", delay));

        if (isAutoPaused)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.currentlyautopaused", autoPauseReasonArgs));
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
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.dontpermission"));
            return;
        }

        if (!isMatchLive)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.checknotlive"));
            return;
        }

        int ctCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
        int tCount = GetTeamPlayerCount(CsTeam.Terrorist);
        int minPlayers = autoPauseMinPlayers.Value;

        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.checkheader"));
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.checkctplayers", ctCount, minPlayers));
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.checktplayers", tCount, minPlayers));
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.checkautopause", Localizer.ForPlayer(player, autoPauseEnabled.Value ? "matchzy.pausemsg.enabled" : "matchzy.pausemsg.disabled")));

        if (ctCount < minPlayers || tCount < minPlayers)
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.checkunbalanced"));
        }
        else
        {
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pausemsg.checkbalanced"));
        }

        // Force check
        CheckAutoResumeOrAutoPause();

        Log($"[AutoPause] {player!.PlayerName} manually triggered auto-pause check");
    }
}
