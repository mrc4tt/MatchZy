using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    // .mute vote: when matchzy_allchat_mute_votes players on a team write .mute / !mute (or the
    // alias .nochat / !nochat), all chat is off and the teams can no longer write to each other. Team chat (say_team) keeps working,
    // admins are not affected and chat commands (lines starting with . ! or /) still go through.
    // Lasts until the map changes or the match is reset. Admins turn it on or off with
    // .allchat [on|off].
    //
    // Little chat: the vote lines themselves are swallowed, only the first vote is announced to
    // everyone (later voters see the count themselves), and a blocked player is told once per map.
    //
    // Both run in the "say" command listener (HookMode.Pre), before the engine's Host_Say
    // broadcasts the line (and before CounterStrikeSharp turns "!mute" into css_mute).
    //
    // CS2-SimpleAdmin owns css_mute (its admin voice mute, "!mute <player>"). While it is loaded,
    // a bare .mute / !mute does not vote: the player is told to use .nochat. A !mute with an
    // argument is never touched, so SimpleAdmin's command keeps working.
    public partial class MatchZy
    {
        private bool allChatMuted;
        private readonly HashSet<ulong> noChatVotes = new();
        private readonly HashSet<ulong> noChatBlockedTold = new();

        private static bool IsChatCommandLine(string line, string word) =>
            line.Length == word.Length + 1 && (line[0] == '.' || line[0] == '!' || line[0] == '/')
            && line.AsSpan(1).Equals(word, StringComparison.OrdinalIgnoreCase);

        // Loaded plugins live in their own AssemblyLoadContext; the assembly is named after the
        // project. Checked on use, so the plugin load order does not matter.
        private static bool IsSimpleAdminLoaded() =>
            System.Runtime.Loader.AssemblyLoadContext.All.Any(c =>
                c.Assemblies.Any(a => a.GetName().Name == "CS2-SimpleAdmin"));

        // What players are told to type to vote.
        private static string AllChatMuteVoteCommand() => IsSimpleAdminLoaded() ? ".nochat" : ".mute";

        private void ResetAllChatVote()
        {
            allChatMuted = false;
            noChatVotes.Clear();
            noChatBlockedTold.Clear();
        }

        // Called from the "say" / "say_team" listener. Handled = the line is not broadcast.
        private HookResult HandleAllChatSay(CCSPlayerController player, string command, string line)
        {
            bool muteLine = IsChatCommandLine(line, "mute");
            if (allChatMuteVotesNeeded.Value > 0 && muteLine && IsSimpleAdminLoaded())
            {
                var told = player;
                Server.NextFrame(() =>
                {
                    if (IsHumanPlayerValid(told))
                        PrintToPlayerChat(told, Localizer.ForPlayer(told, "matchzy.allchat.simpleadmin"));
                });
                return HookResult.Handled;
            }
            if (allChatMuteVotesNeeded.Value > 0 && (muteLine || IsChatCommandLine(line, "nochat")))
            {
                var voter = player;
                // Engine re-entry off the command dispatch: next frame (rule 4).
                Server.NextFrame(() => { if (IsHumanPlayerValid(voter)) OnNoChatCommand(voter, null); });
                return HookResult.Handled;
            }
            if (!allChatMuted || command != "say" || line.Length == 0)
                return HookResult.Continue;
            // Chat commands still reach their handlers (MatchZy dot commands are read from player_chat,
            // which the engine only fires for a line it broadcasts).
            if (line[0] == '.' || line[0] == '!' || line[0] == '/')
                return HookResult.Continue;
            if (IsPlayerAdmin(player, "css_allchat", "@css/config", "@css/chat"))
                return HookResult.Continue;

            // The line is dropped every time; the reason is told once per map.
            if (!noChatBlockedTold.Add(player.SteamID))
                return HookResult.Handled;
            var blocked = player;
            // Chat print off the command dispatch: next frame (rule 4).
            Server.NextFrame(() =>
            {
                if (IsHumanPlayerValid(blocked))
                    PrintToPlayerChat(blocked, Localizer.ForPlayer(blocked, "matchzy.allchat.blocked"));
            });
            return HookResult.Handled;
        }

        [ConsoleCommand("css_nochat", "Vote to turn off all chat until the map changes")]
        public void OnNoChatCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsHumanPlayerValid(player))
                return;
            // Off: the command does nothing (another plugin may use the name).
            if (allChatMuteVotesNeeded.Value <= 0)
                return;
            if (allChatMuted)
            {
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.allchat.alreadymuted"));
                return;
            }
            if (player!.TeamNum != 2 && player.TeamNum != 3)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.allchat.onlyplayers"));
                return;
            }

            // Only players still on a team count: someone who voted and then left or went to
            // spectator drops out of the tally.
            var onTeams = Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3))
                .Select(p => p.SteamID)
                .ToHashSet();
            noChatVotes.IntersectWith(onTeams);
            int needed = Math.Max(1, Math.Min(allChatMuteVotesNeeded.Value, onTeams.Count));

            if (!noChatVotes.Add(player.SteamID))
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.allchat.alreadyvoted", noChatVotes.Count, needed));
                return;
            }

            if (noChatVotes.Count >= needed)
            {
                allChatMuted = true;
                noChatVotes.Clear();
                noChatBlockedTold.Clear();
                PrintLocalizedToAll("matchzy.allchat.muted");
                return;
            }
            // Everyone hears about the first vote; later voters only see the count themselves.
            if (noChatVotes.Count == 1)
                PrintLocalizedToAll("matchzy.allchat.vote", player.PlayerName, noChatVotes.Count, needed, AllChatMuteVoteCommand());
            else
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.allchat.alreadyvoted", noChatVotes.Count, needed));
        }

        [ConsoleCommand("css_allchat", "Turns all chat on or off for players (admin)")]
        public void OnAllChatCommand(CCSPlayerController? player, CommandInfo? command)
        {
            HandleAllChatCommand(player, command?.ArgCount > 1 ? command.ArgByIndex(1) : "");
        }

        // No argument toggles; "on" / "off" set it.
        private void HandleAllChatCommand(CCSPlayerController? player, string arg)
        {
            if (!IsPlayerAdmin(player, "css_allchat", "@css/config", "@css/chat"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            bool mute;
            switch (arg.Trim().ToLowerInvariant())
            {
                case "": mute = !allChatMuted; break;
                case "on": case "1": case "true": mute = false; break;
                case "off": case "0": case "false": mute = true; break;
                default:
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.allchat.usage"));
                    return;
            }
            allChatMuted = mute;
            noChatVotes.Clear();
            noChatBlockedTold.Clear();
            PrintLocalizedToAll(mute ? "matchzy.allchat.adminmuted" : "matchzy.allchat.unmuted");
        }
    }
}
