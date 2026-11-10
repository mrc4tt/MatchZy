using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.UserMessages;

namespace MatchZy
{
    // TEMPORARY debug probe (not for release): logs the text user messages the server sends, to find
    // which one draws CS2's red "The match is set to pause during freeze time" box after
    // mp_pause_match / timeout_ct_start / timeout_terrorist_start. Server console / RCON only:
    //   matchzy_debug_usermessages [seconds]   (default 60, 0 = stop)
    // then .pause / .tac / .tech mid-round and read the "[UserMessageProbe]" lines in the log.
    public partial class MatchZy
    {
        private static readonly int[] ProbedUserMessages = { 111, 113, 124, 304, 307, 323, 324 };
        private bool userMessageProbeHooked;
        private DateTime userMessageProbeUntil = DateTime.MinValue;

        [ConsoleCommand("matchzy_debug_usermessages", "Debug: log text user messages for N seconds (default 60, 0 = stop)")]
        [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
        public void OnDebugUserMessagesCommand(CCSPlayerController? player, CommandInfo command)
        {
            int seconds = 60;
            if (command.ArgCount > 1 && int.TryParse(command.ArgByIndex(1), out int s))
                seconds = Math.Max(0, s);
            if (!userMessageProbeHooked)
            {
                foreach (int id in ProbedUserMessages)
                    HookUserMessage(id, UserMessageProbe, HookMode.Pre);
                userMessageProbeHooked = true;
            }
            userMessageProbeUntil = DateTime.UtcNow.AddSeconds(seconds);
            command.ReplyToCommand($"[UserMessageProbe] logging text user messages for {seconds} s");
            Log($"[UserMessageProbe] logging for {seconds} s");
        }

        private HookResult UserMessageProbe(UserMessage um)
        {
            try
            {
                if (DateTime.UtcNow > userMessageProbeUntil)
                    return HookResult.Continue;
                string dbg = um.DebugString.Replace("\n", " | ");
                if (dbg.Length > 600)
                    dbg = dbg[..600] + "...";
                Log($"[UserMessageProbe] t={Server.CurrentTime:F1} id={um.Id} name={um.Name} type={um.Type} recipients={um.Recipients.Count} {dbg}");
            }
            catch (Exception e)
            {
                Log($"[UserMessageProbe] {e.Message}");
            }
            return HookResult.Continue;
        }
    }
}
