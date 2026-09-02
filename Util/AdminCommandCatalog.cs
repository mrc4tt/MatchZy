using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        /// <summary>
        /// One admin command as shown by .mhelp. <see cref="PermissionKey"/> and <see cref="Permissions"/>
        /// mirror exactly what the handler passes to IsPlayerAdmin, so the guide agrees with the gate,
        /// including per-player command_overrides.
        /// </summary>
        private readonly record struct AdminCommandInfo(string Category, string Display, string Description, string PermissionKey, string[] Permissions);

        private static readonly string[] PermConfig = { "@css/config" };
        private static readonly string[] PermMapOrPrac = { "@css/map", "@custom/prac" };
        private static readonly string[] PermMap = { "@css/map" };
        private static readonly string[] PermChat = { "@css/chat" };
        private static readonly string[] PermRootOnly = System.Array.Empty<string>(); // IsPlayerAdmin always adds @css/root

        // Keep in sync with the IsPlayerAdmin(...) call in each handler.
        private static readonly AdminCommandInfo[] AdminCommandCatalog =
        {
            // Modes
            new("Modes", ".match", "Match mode (knife, ready-up, MR12)", "css_match", PermMapOrPrac),
            new("Modes", ".scrim / .playout / .po", "Scrim/playout mode (no knife, all rounds)", "css_scrim", PermMapOrPrac),
            new("Modes", ".hill", "King of the hill mode", "css_hill", PermMapOrPrac),
            new("Modes", ".prac / .tactics", "Practice mode", "css_prac", PermMapOrPrac),
            new("Modes", ".dry / .dryrun", "Dryrun mode", "css_prac", PermMapOrPrac),
            new("Modes", ".exitprac / .noprac", "Leave practice mode", "css_exitprac", PermMapOrPrac),
            new("Modes", ".exitdry / .stopdry / .enddry", "Leave dryrun mode", "css_exitdry", PermMapOrPrac),
            new("Modes", ".warmup", "Back to warmup", "css_warmup", PermConfig),
            new("Modes", ".sleep", "Sleep mode", "css_sleep", PermMapOrPrac),
            new("Modes", ".warmupbots", "Toggle warmup bots", "css_warmupbots", PermMapOrPrac),

            // Setup
            new("Setup", ".ma / .matchadmin", "Admin menu", "css_matchadmin", PermConfig),
            new("Setup", ".matchsetup", "Match setup wizard", "css_matchsetup", PermConfig),
            new("Setup", ".map <name>", "Change map", "css_map", PermMap),
            new("Setup", ".teamsize <n> / .readyrequired", "Players required to ready", "css_readyrequired", PermConfig),
            new("Setup", ".knife / .kniferound", "Toggle knife round", "css_roundknife", PermConfig),
            new("Setup", ".settings / .configs", "Show match settings", "css_settings", PermConfig),
            new("Setup", ".skipveto", "Skip current veto phase", "css_skipveto", PermConfig),
            new("Setup", ".team <ct|t> <name>", "Set team name", "css_team", PermConfig),
            new("Setup", ".whitelist", "Toggle whitelist", "css_whitelist", PermConfig),
            new("Setup", ".globalnades", "Toggle global nade lineups", "css_save_nades_as_global", PermConfig),
            new("Setup", ".rmap", "Reload current map", "css_rmap", PermRootOnly),

            // Control
            new("Control", ".start / .force / .forcestart", "Force start match", "css_start", PermConfig),
            new("Control", ".restart / .abort", "Restart match", "css_restart", PermConfig),
            new("Control", ".endmatch / .end / .forceend / .exitscrim", "End and reset match", "css_endmatch", PermConfig),
            new("Control", ".surrender / .matchgg", "Surrender match", "css_endmatch", PermConfig),
            new("Control", ".restore <round>", "Restore a round backup", "css_restore", PermConfig),
            new("Control", ".restorelast / .rl", "Restore previous round", "css_restorelast", PermConfig),
            new("Control", ".restorecurrent / .rr", "Restart current round", "css_restorecurrent", PermConfig),
            new("Control", ".backupmenu / .backups / .backup", "Backup list with restore buttons", "css_backupmenu", PermConfig),
            new("Control", ".listbackups <matchid>", "List backups for a match", "css_restore", PermConfig),

            // Pause
            new("Pause", ".fp / .forcepause", "Force pause", "css_forcepause", PermConfig),
            new("Pause", ".fup / .forceunpause", "Force unpause", "css_forceunpause", PermConfig),
            new("Pause", ".autopause / css_autopause_*", "Auto-pause settings", "css_autopause", PermRootOnly),

            // Chat
            new("Chat", ".asay <msg>", "Say as admin", "css_asay", PermChat),

            // Coach / library
            new("Coach", ".coachtest", "Place yourself as coach", "css_coachtest", PermMapOrPrac),
            new("Coach", ".savecoachspawn / .clearcoachspawns / .listcoachspawns / .showcoachspawns", "Coach spawn spots", "css_savecoachspawn", PermConfig),
            new("Nades", ".libadd / .libremove", "Global grenade library", "css_libadd", PermConfig),
        };

        /// <summary>True when the player passes at least one catalog gate, i.e. is some kind of admin.</summary>
        private bool HasAnyAdminCommand(CCSPlayerController? player)
        {
            if (player == null) return true;
            foreach (var c in AdminCommandCatalog)
                if (IsPlayerAdmin(player, c.PermissionKey, c.Permissions)) return true;
            return false;
        }

        private static string DescribePerms(string[] perms)
            => perms.Length == 0 ? "@css/root" : string.Join(" or ", perms);

        /// <summary>
        /// .mhelp: chat gets the commands THIS player can run, grouped by category; console gets the
        /// full catalog with a check/cross and the permission each one needs.
        /// </summary>
        private void SendAdminCommandsGuide(CCSPlayerController? player)
        {
            if (!IsPlayerValid(player))
                return;

            var allowed = new Dictionary<string, List<string>>();
            var denied = new List<AdminCommandInfo>();
            int allowedCount = 0;
            foreach (var c in AdminCommandCatalog)
            {
                if (IsPlayerAdmin(player, c.PermissionKey, c.Permissions))
                {
                    if (!allowed.TryGetValue(c.Category, out var list)) allowed[c.Category] = list = new List<string>();
                    list.Add(c.Display.Split(" / ")[0]);
                    allowedCount++;
                }
                else
                {
                    denied.Add(c);
                }
            }

            // Chat: only what they can use.
            player!.PrintToChat($"{chatPrefix} {ChatColors.Gold}Admin commands you can use ({allowedCount}/{AdminCommandCatalog.Length}):");
            foreach (var (category, cmds) in allowed)
                player.PrintToChat($" {ChatColors.Green}{category}:{ChatColors.Default} {string.Join("  ", cmds)}");
            if (denied.Count > 0)
                player.PrintToChat($" {ChatColors.Grey}{denied.Count} more need extra flags. Full list with required flags in console.");

            // Console: everything, with the gate each one needs.
            player.PrintToConsole("\n" + new string('=', 60));
            player.PrintToConsole("MATCHZY ADMIN COMMANDS - [x] = you can use, [ ] = missing permission");
            player.PrintToConsole(new string('=', 60));
            string? lastCategory = null;
            foreach (var c in AdminCommandCatalog)
            {
                if (c.Category != lastCategory)
                {
                    player.PrintToConsole($"\n[{c.Category}]");
                    lastCategory = c.Category;
                }
                bool ok = IsPlayerAdmin(player, c.PermissionKey, c.Permissions);
                player.PrintToConsole($" [{(ok ? "x" : " ")}] {c.Display,-52} {c.Description}");
                if (!ok)
                    player.PrintToConsole($"      needs: {DescribePerms(c.Permissions)}");
            }
            player.PrintToConsole("\nFlag reference (SourceMod letters): g=@css/map (modes, prac, map), i=@css/config (match control), j=@css/chat (.asay), z=root (everything).");
            player.PrintToConsole("Player/practice commands: .help");
        }
    }
}
