using System;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    // .warmupbots [n] - admin toggle that adds n aim-warmup bots (normal AI: they move and shoot)
    // during the warmup/ready phase, deathmatch style, so players can warm their aim before the
    // match. Auto-removed the moment the match leaves warmup (knife round or live) - the bots can
    // never leak into the real match. Warmup only: refuses in practice (practice has its own frozen
    // .bot system) and once the match has started.
    public partial class MatchZy
    {
        private bool warmupBotsActive;

        [ConsoleCommand("css_warmupbots", "Toggle aim-warmup bots during warmup: .warmupbots [count]")]
        public void OnWarmupBotsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            HandleWarmupBotsCommand(player, command?.ArgString ?? "");
        }

        public void HandleWarmupBotsCommand(CCSPlayerController? player, string arg)
        {
            if (!IsPlayerValid(player))
                return;
            if (!IsPlayerAdmin(player, "css_warmupbots", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (warmupBotsActive)
            {
                KillWarmupBots();
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.wb.off"));
                return;
            }
            // Warmup/ready phase only - never during practice (own bot system) or a started match.
            if (isPractice || isDryRun || matchStarted || !isWarmup)
            {
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.wb.warmuponly"));
                return;
            }

            int count = 4;
            arg = (arg ?? "").Trim();
            if (int.TryParse(arg, out int n))
                count = Math.Clamp(n, 1, 10);

            // Normal bot AI (they buy, move, shoot) split over both teams; balanced fill.
            Server.ExecuteCommand(
                $"bot_kick; bot_difficulty 2; bot_dont_shoot 0; bot_stop 0; bot_zombie 0; " +
                $"mp_autoteambalance 1; bot_quota_mode normal; bot_quota {count}");
            warmupBotsActive = true;
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.wb.on", $"{count}"));
        }

        // Remove the warmup bots. Called on toggle-off and from every warmup exit (knife/live/practice)
        // so they can never survive into the real match.
        public void KillWarmupBots()
        {
            if (!warmupBotsActive)
                return;
            warmupBotsActive = false;
            Server.ExecuteCommand("bot_kick; bot_quota 0");
            Log("[WarmupBots] removed (match leaving warmup)");
        }
    }
}
