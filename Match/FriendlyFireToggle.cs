using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    // .friendlyfire / .ffire: like .knife, an admin decides before the match (or scrim / hill) goes
    // live whether friendly fire is on. Applied after the live, scrim or hill cfg and the match
    // config's cvars on every map of the series; cleared when the match is reset.
    public partial class MatchZy
    {
        // null = the mode cfg decides (mp_friendlyfire 1 in the shipped cfgs).
        private bool? friendlyFireOverride;

        [ConsoleCommand("css_friendlyfire", "Toggles friendly fire for the match before it goes live")]
        [ConsoleCommand("css_ffire", "Toggles friendly fire for the match before it goes live")]
        public void OnFriendlyFireCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_friendlyfire", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (isPractice)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.friendlyfirepractice"));
                return;
            }
            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.friendlyfirestarted"));
                return;
            }

            bool enabled = !(friendlyFireOverride ?? CfgFriendlyFireDefault());
            friendlyFireOverride = enabled;
            string status = Localizer.ForPlayer(player, enabled ? "matchzy.cc.enabled" : "matchzy.cc.disabled");
            PrintLocalizedToAll("matchzy.cc.friendlyfire", status);
        }

        // What the mode cfg the match will use sets, so the first toggle flips the real value.
        private bool CfgFriendlyFireDefault()
        {
            try
            {
                string cfg = isPlayOutEnabled2 ? hillCfgPath
                    : isPlayOutEnabled ? scrimCfgPath
                    : GetGameMode() == 2 ? liveWingmanCfgPath
                    : liveCfgPath;
                string? value = GetConvarValueFromCFGFile(Path.Join(Server.GameDirectory + "/csgo/cfg", cfg), "mp_friendlyfire");
                return value == null || (value.Trim().Trim('"') != "0" && !value.Trim().Trim('"').Equals("false", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
        }

        // Called after the live / scrim / hill cfg has been executed.
        private void ApplyFriendlyFireOverride()
        {
            if (friendlyFireOverride is bool enabled)
                Server.ExecuteCommand($"mp_friendlyfire {(enabled ? 1 : 0)}");
        }
    }
}
