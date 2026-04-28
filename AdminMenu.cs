using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;

namespace MatchZy
{
    public partial class MatchZy
    {
        [ConsoleCommand("css_matchadmin", "Opens the MatchZy admin chat menu")]
        [ConsoleCommand("css_ma", "Opens the MatchZy admin chat menu")]
        public void OnMatchAdminCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
            {
                ReplyToUserCommand(player, "matchadmin must be run from a client (chat menu).");
                return;
            }
            if (!IsPlayerAdmin(player, "css_matchadmin", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            // Defer so menu render isn't clobbered when entered via .chat dispatch.
            Server.NextFrame(() => OpenMatchAdminMenu(player));
        }

        private void OpenMatchAdminMenu(CCSPlayerController player)
        {
            var menu = new WasdMenu($"Match Admin", this);
            menu.AddItem("New Match Setup", (p, _) => OnMatchSetupCommand(p, null));
            menu.AddItem("Match Control", (p, _) => OpenMatchControlMenu(p, menu));
            menu.AddItem("Pause / Unpause", (p, _) => OpenPauseMenu(p, menu));
            menu.AddItem("Modes", (p, _) => OpenModesMenu(p, menu));
            menu.Display(player, 0);
        }

        private void OpenMatchControlMenu(CCSPlayerController player, WasdMenu parent)
        {
            var menu = new WasdMenu($"{chatPrefix} Match Control", this) { PrevMenu = parent };
            menu.AddItem("Force Start", (p, _) => OnStartCommand(p, null));
            menu.AddItem("Knife Round", (p, _) => OnKnifeCommand(p, null));
            menu.AddItem("Restart Round", (p, _) => OnRestartRoundCommand(p, null));
            menu.AddItem("Restart Match", (p, _) => OnRestartMatchCommand(p, null));
            menu.AddItem("Force End Match", (p, _) => OnEndMatchCommand(p, null));
            menu.AddItem("Stop Match", (p, _) => OnStopMatchCommand(p, null));
            menu.Display(player, 0);
        }

        private void OpenPauseMenu(CCSPlayerController player, WasdMenu parent)
        {
            var menu = new WasdMenu($"{chatPrefix} Pause Control", this) { PrevMenu = parent };
            menu.AddItem("Pause", (p, _) => OnPauseCommand(p, null));
            menu.AddItem("Unpause", (p, _) => OnUnpauseCommand(p, null));
            menu.AddItem("Force Pause", (p, _) => OnForcePauseCommand(p, null));
            menu.AddItem("Force Unpause", (p, _) => OnForceUnpauseCommand(p, null));
            menu.AddItem("Tactical Timeout", (p, _) => OnTacCommand(p, null));
            menu.AddItem("Tech Pause", (p, _) => OnTechCommand(p, null));
            menu.Display(player, 0);
        }

        private void OpenModesMenu(CCSPlayerController player, WasdMenu parent)
        {
            var menu = new WasdMenu($"{chatPrefix} Modes", this) { PrevMenu = parent };
            menu.AddItem("Warmup", (p, _) => OnWarmupCommand(p, null));
            menu.AddItem("Match Setup", (p, _) => OnMatchCommand(p, null));
            menu.AddItem("Practice", (p, _) => OnPracCommand(p, null));
            menu.AddItem("Exit Practice", (p, _) => OnExitPracCommand(p, null));
            menu.AddItem("Dry Run", (p, _) => OnDryRunCommand(p, null));
            menu.AddItem("Exit Dry Run", (p, _) => OnExitDryCommand(p, null));
            menu.Display(player, 0);
        }
    }
}
