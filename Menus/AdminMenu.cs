using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Menu;

namespace MatchZy
{
    public partial class MatchZy
    {
        // CS2MenuManager is an OPTIONAL runtime dependency. Its assembly is only
        // JIT-resolved on the first touch of a menu type (WasdMenu), which happens
        // inside the OpenX menu-builder methods, never at Load(). If the shared
        // plugin isn't installed, that first touch throws FileNotFound/TypeLoad on
        // the NextFrame callback. Route every menu open through here so a missing
        // optional dep degrades to a chat message instead of crashing the frame.
        // `open` must be a method group whose body is the first WasdMenu reference -
        // this wrapper itself references no menu type, so it always JIT-resolves.
        private void OpenMenuGuarded(CCSPlayerController player, Action<CCSPlayerController> open)
        {
            try
            {
                open(player);
                if (player.IsValid)
                    _centerMenuHolders.Add(player.Slot);
            }
            catch (Exception e) when (e is FileNotFoundException || e is TypeLoadException || e is FileLoadException)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.menu.menumanagermissing"));
                Log($"[Menu] CS2MenuManager not available: {e.Message}");
            }
        }

        // Center-panel menu gate. A WasdMenu repaints the center HTML panel every tick, and the ready
        // panel, classic ready hint, tech pause box, .timer and spawn-marker hover wrote over it, so
        // the menu flickered (.matchadmin / .matchsetup during the ready-up, .nades with .timer on).
        // Those writers skip a player whose menu is open. Slots are added in OpenMenuGuarded, so the
        // menu library is only asked (MenuStillOpen) after it has loaded.
        private readonly HashSet<int> _centerMenuHolders = new();

        private bool IsCenterMenuOpen(CCSPlayerController player)
        {
            if (_centerMenuHolders.Count == 0 || !_centerMenuHolders.Contains(player.Slot))
                return false;
            bool open;
            try
            {
                open = MenuStillOpen(player);
            }
            catch (Exception)
            {
                open = false;
            }
            if (!open)
                _centerMenuHolders.Remove(player.Slot);
            return open;
        }

        // Own method so the menu type is only JIT-resolved here, never in the always-run callers.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool MenuStillOpen(CCSPlayerController player)
        {
            return MenuManager.GetActiveMenu(player) != null;
        }

        // Pre-JIT the CS2MenuManager assembly on a BACKGROUND thread at Load. Without this the whole
        // menu dependency graph is assembly-loaded + JIT'd on the game thread at the FIRST menu open
        // (observed: .nades -> 1772ms frame stall -> recv queue overflow for every client). Doing the
        // first touch inside Task.Run moves that cost off the game thread; the later real open finds
        // everything warm. The optional-dependency invariant is preserved: this method itself contains
        // no menu type (so it always JIT-resolves), the touch happens inside the task, and a missing
        // plugin just lands in the catch - plugin keeps running, menus degrade as before.
        private static bool _menuWarmupStarted;
        public void WarmupMenuAssembly()
        {
            if (_menuWarmupStarted)
                return;
            _menuWarmupStarted = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    TouchMenuTypes();
                }
                catch (Exception e) when (e is System.IO.FileNotFoundException or System.IO.FileLoadException or TypeLoadException)
                {
                    // Optional dependency not installed: say what that means instead of the exception name.
                    Log("CS2MenuManager is not installed: the in-game menus (.matchadmin, .matchsetup, ...) are off. Everything else works.");
                }
                catch (Exception e)
                {
                    Log($"[Menu] CS2MenuManager failed to load: {e.GetType().Name}: {e.Message}");
                }
            });
        }

        // First menu-type touch - kept in its own method so its JIT (and the assembly load it
        // triggers) happens only when invoked from the warmup task, never on the Load path.
        private static void TouchMenuTypes()
        {
            _ = typeof(WasdMenu).Assembly.FullName;
        }

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
            // Re-validated: the player can leave before the next frame.
            Server.NextFrame(() => { if (IsPlayerValid(player)) OpenMenuGuarded(player, OpenMatchAdminMenu); });
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
            var menu = new WasdMenu($"Match Control", this) { PrevMenu = parent };
            menu.AddItem("Force Start", (p, _) => OnStartCommand(p, null));
            menu.AddItem("Knife Round", (p, _) => OnKnifeCommand(p, null));
            // Restore the current round from its backup. OnRestartRoundCommand is the practice-only
            // .rs and did nothing in a match.
            menu.AddItem("Restart Round", (p, _) => OnRestoreCurrentRoundCommand(p, null));
            menu.AddItem("Restore Round...", (p, _) => OpenRestoreRoundMenu(p, menu));
            menu.AddItem("Restart Match", (p, _) => OnRestartMatchCommand(p, null));
            menu.AddItem("Force End Match", (p, _) => OnEndMatchCommand(p, null));
            menu.AddItem("Stop Match", (p, _) => OnStopMatchCommand(p, null));
            menu.Display(player, 0);
        }

        // One restorable backup in the restore menu.
        private sealed record BackupMenuEntry(string FileName, string Label);

        // .ma -> Match Control -> Restore Round: pick a round from a list instead of typing
        // !restore <round> / !loadbackup <file>. In a live match the list is this map's round
        // backups (newest round first); with no live match (after a crash or restart) it is the
        // newest backup files on disk across all matches, like .backup. Every pick asks for a
        // confirmation, and the restore itself is the same RestoreRoundBackup the commands use.
        private void OpenRestoreRoundMenu(CCSPlayerController player, WasdMenu parent)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            bool live = isMatchLive;
            List<BackupMenuEntry> entries = live ? LiveBackupMenuEntries(player) : DiskBackupMenuEntries(player);
            if (entries.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, live ? "matchzy.backupmsg.nobackupsformatch" : "matchzy.backupmsg.nobackups"));
                return;
            }

            var menu = new WasdMenu(live ? "Restore Round" : "Restore Backup", this) { PrevMenu = parent };
            if (live && !string.IsNullOrEmpty(lastMatchZyBackupFileName))
                menu.AddItem("Previous round", (p, _) => OpenRestoreConfirmMenu(p, menu, new BackupMenuEntry(lastMatchZyBackupFileName, "previous round"), true));
            foreach (var entry in entries)
                menu.AddItem(entry.Label, (p, _) => OpenRestoreConfirmMenu(p, menu, entry, live));
            menu.Display(player, 0);
        }

        private void OpenRestoreConfirmMenu(CCSPlayerController player, WasdMenu parent, BackupMenuEntry entry, bool live)
        {
            var menu = new WasdMenu($"Restore {entry.Label}?", this) { PrevMenu = parent };
            menu.AddItem($"{ChatColors.Green}Yes, restore", (p, o) =>
            {
                o.PostSelectAction = CS2MenuManager.API.Enum.PostSelectAction.Close;
                // The menu can sit open while the match moves on: re-check before restoring.
                if (!IsPlayerAdmin(p, "css_restore", "@css/config"))
                {
                    SendPlayerNotAdminMessage(p);
                    return;
                }
                if (live && !isMatchLive)
                {
                    ReplyToUserCommand(p, Localizer.ForPlayer(p, "matchzy.backupmsg.matchnotlive"));
                    return;
                }
                Log($"[RestoreMenu] {p.PlayerName} restores {entry.FileName}");
                RestoreRoundBackup(p, entry.FileName);
            });
            menu.AddItem("No", (p, _) => parent.Display(p, 0));
            menu.Display(player, 0);
        }

        // This map's round backups of the live match, newest round first.
        private List<BackupMenuEntry> LiveBackupMenuEntries(CCSPlayerController player)
        {
            var entries = new List<BackupMenuEntry>();
            string prefix = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round";
            var rounds = GetBackups(liveMatchId.ToString())
                .Select(path => Path.GetFileName(path))
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(name => (name, ok: int.TryParse(name[prefix.Length..^5], out int round), round))
                .Where(x => x.ok)
                .OrderByDescending(x => x.round)
                .Take(15);
            string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");
            foreach (var (name, _, round) in rounds)
            {
                var data = ParseBackupFile(Path.Combine(backupDir, name));
                string score = data == null ? "" : $" | {data.GetValueOrDefault("team1_score", "0")}-{data.GetValueOrDefault("team2_score", "0")}";
                string ago = data != null && DateTime.TryParse(data.GetValueOrDefault("timestamp", ""), out var t) ? $" | {BackupTimeAgo(player, DateTime.Now - t, false)}" : "";
                entries.Add(new BackupMenuEntry(name, $"Round {round}{score}{ago}"));
            }
            return entries;
        }

        // No live match: the newest backup files on disk, across all matches (crash recovery).
        private List<BackupMenuEntry> DiskBackupMenuEntries(CCSPlayerController player)
        {
            var entries = new List<BackupMenuEntry>();
            string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");
            if (!Directory.Exists(backupDir))
                return entries;
            var files = new DirectoryInfo(backupDir).GetFiles("matchzy_*.json")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(10);
            foreach (var file in files)
            {
                var data = ParseBackupFile(file.FullName);
                if (data == null)
                    continue;
                string ago = DateTime.TryParse(data.GetValueOrDefault("timestamp", ""), out var t) ? $" | {BackupTimeAgo(player, DateTime.Now - t, true)}" : "";
                entries.Add(new BackupMenuEntry(file.Name,
                    $"#{data.GetValueOrDefault("matchid", "?")} {data.GetValueOrDefault("map_name", "?")} R{data.GetValueOrDefault("round", "?")}" +
                    $" | {data.GetValueOrDefault("team1_score", "0")}-{data.GetValueOrDefault("team2_score", "0")}{ago}"));
            }
            return entries;
        }

        private void OpenPauseMenu(CCSPlayerController player, WasdMenu parent)
        {
            var menu = new WasdMenu($"Pause Control", this) { PrevMenu = parent };
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
            var menu = new WasdMenu($"Modes", this) { PrevMenu = parent };
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
