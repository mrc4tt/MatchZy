using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Admin tickets: players talk to the admins from the game, a panel / ticket system answers over RCON.
    //
    //   .admin [message]   the match's ticket. One open thread per match (per server without a match):
    //                      every player's messages go into it, and every player in the match sees them
    //                      and the admins' answers (so both teams see "we restart round 14").
    //   .report [message]  a private thread per player: only the reporter and the admins see it.
    //
    // Every message is an admin_called event (new_thread true on the first one), answers are
    // matchzy_admin_reply (-> admin_reply event), and a thread ends with matchzy_admin_close, the
    // match ending, or matchzy_admin_ticket_timeout minutes without a message (-> admin_thread_closed).
    public partial class MatchZy
    {
        private const int AdminCallMessageMax = 200;

        private sealed class AdminThread
        {
            public required string Id { get; init; }
            public required string Kind { get; init; }        // "admin" | "report"
            public required string Key { get; init; }
            public ulong ReportOwner { get; init; }
            public long MatchId { get; init; }
            public DateTime LastActivity { get; set; }
        }

        // Open threads by key ("match:<id>", "server", "report:<steamid>") and by thread id.
        private readonly Dictionary<string, AdminThread> adminThreadsByKey = new();
        private readonly Dictionary<string, AdminThread> adminThreadsById = new();
        // Message id -> thread id, so a reply can name the message it answers.
        private readonly Dictionary<string, string> adminCallThread = new();
        private readonly Dictionary<ulong, DateTime> adminCallLastAt = new();

        [ConsoleCommand("css_calladmin", "Writes to the match's admin ticket. Usage: .admin [message]")]
        public void OnCallAdminCommand(CCSPlayerController? player, CommandInfo? command)
        {
            HandleAdminCall(player, command?.ArgString ?? "", report: false);
        }

        private AdminTicketServer AdminTicketServerInfo() => new()
        {
            Hostname = ConVar.Find("hostname")?.StringValue ?? "",
            Address = GetServerIpForStats(),
        };

        private static string UtcNowIso() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        private static bool IsHuman(CCSPlayerController? p) => p != null && p.IsValid && !p.IsBot && !p.IsHLTV;

        private bool IsTicketAdmin(CCSPlayerController p) => IsPlayerAdmin(p, "css_calladmin", "@css/generic");

        // Who sees a thread's messages: everyone for the match ticket, the reporter and the admins for a report.
        private IEnumerable<CCSPlayerController> AdminThreadAudience(AdminThread thread)
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (!IsHuman(p))
                    continue;
                if (thread.Kind == "admin" || p.SteamID == thread.ReportOwner || IsTicketAdmin(p))
                    yield return p;
            }
        }

        private void PublishAdminTicketEvent(AdminTicketEvent @event) => PublishLifecycleEvent(@event);

        private void CloseAdminThread(AdminThread thread, string reason, bool tellPlayers)
        {
            adminThreadsByKey.Remove(thread.Key);
            adminThreadsById.Remove(thread.Id);
            foreach (var call in adminCallThread.Where(kv => kv.Value == thread.Id).Select(kv => kv.Key).ToList())
                adminCallThread.Remove(call);
            if (tellPlayers)
            {
                foreach (var p in AdminThreadAudience(thread))
                    PrintToPlayerChat(p, Localizer.ForPlayer(p, thread.Kind == "report" ? "matchzy.admincall.reportclosed" : "matchzy.admincall.closed"));
            }
            PublishAdminTicketEvent(new AdminThreadClosedEvent
            {
                MatchId = thread.MatchId > 0 ? thread.MatchId : null,
                MapName = SafeMapName(),
                PluginVersion = ModuleVersion,
                MapNumber = thread.MatchId > 0 && isMatchSetup ? matchConfig.CurrentMapNumber : null,
                ThreadId = thread.Id,
                Kind = thread.Kind,
                Server = AdminTicketServerInfo(),
                Reason = reason,
                ClosedAt = UtcNowIso(),
            });
        }

        // The match is over (series end, stop, cancel): its tickets end with it. Called from ResetMatch,
        // before the match's remote log settings are reset, so the events reach the match's URL.
        private void CloseMatchAdminThreads()
        {
            if (liveMatchId <= 0)
                return;
            foreach (var thread in adminThreadsById.Values.Where(t => t.MatchId == liveMatchId).ToList())
                CloseAdminThread(thread, "match_end", tellPlayers: false);
        }

        // .admin / .report from a player.
        public void HandleAdminCall(CCSPlayerController? player, string message, bool report)
        {
            if (!IsHuman(player))
                return;
            // Off by default. Silent then: .admin / .report may be another plugin's commands.
            if (!adminCallEnabled.Value)
                return;
            ulong steamId = player!.SteamID;
            int cooldown = Math.Max(0, adminCallCooldown.Value);
            if (adminCallLastAt.TryGetValue(steamId, out DateTime last) && (DateTime.UtcNow - last).TotalSeconds < cooldown)
            {
                int left = (int)Math.Ceiling(cooldown - (DateTime.UtcNow - last).TotalSeconds);
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.admincall.cooldown", left));
                return;
            }
            adminCallLastAt[steamId] = DateTime.UtcNow;

            // No {colour} tags from players: the localizer turns them into chat colours, so a player
            // could style or fake ticket lines.
            message = (message ?? "").Replace("{", "").Replace("}", "").Trim().Trim('"').Trim();
            if (message.Length > AdminCallMessageMax)
                message = message.Substring(0, AdminCallMessageMax);

            long matchId = isMatchSetup ? liveMatchId : 0;
            string key = report ? $"report:{steamId}" : matchId > 0 ? $"match:{matchId}" : "server";
            if (adminThreadsByKey.TryGetValue(key, out AdminThread? thread)
                && adminTicketTimeout.Value > 0
                && (DateTime.UtcNow - thread.LastActivity).TotalMinutes >= adminTicketTimeout.Value)
            {
                CloseAdminThread(thread, "timeout", tellPlayers: false);
                thread = null;
            }
            bool newThread = thread == null;
            if (thread == null)
            {
                thread = new AdminThread
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = report ? "report" : "admin",
                    Key = key,
                    ReportOwner = report ? steamId : 0,
                    MatchId = matchId,
                };
                adminThreadsByKey[key] = thread;
                adminThreadsById[thread.Id] = thread;
            }
            thread.LastActivity = DateTime.UtcNow;
            string callId = Guid.NewGuid().ToString("N");
            adminCallThread[callId] = thread.Id;

            // In game: the match ticket is shown to everyone, a report to the admins only.
            foreach (var p in AdminThreadAudience(thread))
            {
                if (report)
                {
                    if (p == player)
                        continue;
                    PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.admincall.toadminreport", player.PlayerName, message));
                }
                else
                {
                    PrintToPlayerChat(p, message.Length > 0
                        ? Localizer.ForPlayer(p, "matchzy.admincall.public", player.PlayerName, message)
                        : Localizer.ForPlayer(p, "matchzy.admincall.publicempty", player.PlayerName));
                }
            }

            string side = player.TeamNum == (int)CsTeam.CounterTerrorist ? "ct" : player.TeamNum == (int)CsTeam.Terrorist ? "t" : "";
            var targets = PublishLifecycleEvent(new AdminCalledEvent
            {
                MatchId = matchId > 0 ? matchId : null,
                MapName = SafeMapName(),
                PluginVersion = ModuleVersion,
                MapNumber = matchId > 0 ? matchConfig.CurrentMapNumber : null,
                ThreadId = thread.Id,
                Kind = thread.Kind,
                Server = AdminTicketServerInfo(),
                CallId = callId,
                NewThread = newThread,
                Player = new AdminCallPlayer
                {
                    SteamId64 = steamId.ToString(),
                    Name = player.PlayerName,
                    Team = matchId > 0 ? TeamKeyForSide(player.TeamNum) : null,
                    Side = side.Length > 0 ? side : null,
                },
                Message = message,
                CalledAt = UtcNowIso(),
            });

            bool adminOnline = Utilities.GetPlayers().Any(p => IsHuman(p) && p != player && IsTicketAdmin(p));
            Log($"[AdminTicket] {thread.Kind} {thread.Id}{(newThread ? " (new)" : "")} {player.PlayerName} ({steamId}): \"{message}\" - admin online: {adminOnline}, {targets.Length} remote log target(s).");
            if (newThread || report)
            {
                string replyKey = !adminOnline && targets.Length == 0 ? "matchzy.admincall.noadmin"
                    : report ? "matchzy.admincall.reportsent" : "matchzy.admincall.sent";
                PrintToPlayerChat(player, Localizer.ForPlayer(player, replyKey));
            }
        }

        // A thread named by its id, a message id, or a SteamID64 (that player's open report, else the
        // match ticket). Null when nothing open matches.
        private AdminThread? FindAdminThread(string target)
        {
            if (adminThreadsById.TryGetValue(target, out var byId))
                return byId;
            if (adminCallThread.TryGetValue(target, out string? threadId) && adminThreadsById.TryGetValue(threadId, out var byCall))
                return byCall;
            if (ulong.TryParse(target, out ulong steamId))
            {
                if (adminThreadsByKey.TryGetValue($"report:{steamId}", out var reportThread))
                    return reportThread;
                long matchId = isMatchSetup ? liveMatchId : 0;
                if (adminThreadsByKey.TryGetValue(matchId > 0 ? $"match:{matchId}" : "server", out var matchThread))
                    return matchThread;
            }
            return null;
        }

        // Splits "<target> <message...>" from a console command (RCON quotes optional).
        private static (string Target, string Text) SplitTargetAndText(CommandInfo command)
        {
            string target = command.ArgCount > 1 ? command.ArgByIndex(1).Trim('"') : "";
            string all = command.ArgString.Trim();
            int at = all.IndexOf(command.ArgCount > 1 ? command.ArgByIndex(1) : "", StringComparison.Ordinal);
            string rest = at >= 0 && command.ArgCount > 1 ? all.Substring(at + command.ArgByIndex(1).Length) : "";
            return (target, rest.Trim().Trim('"').Trim());
        }

        // matchzy_admin_reply <thread_id | call_id | steamid64> <message>: a panel / ticket system (RCON)
        // or an admin answers. The match ticket's answers are shown to every player, a report's only to
        // the reporter.
        [ConsoleCommand("matchzy_admin_reply", "Answers an admin ticket. Usage: matchzy_admin_reply <thread_id|call_id|steamid64> <message>")]
        public void OnAdminReplyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && !IsTicketAdmin(player))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            var (target, message) = SplitTargetAndText(command);
            if (target.Length == 0 || message.Length == 0)
            {
                ReplyToUserCommand(player, "Usage: matchzy_admin_reply <thread_id|call_id|steamid64> <message>");
                return;
            }
            if (message.Length > AdminCallMessageMax)
                message = message.Substring(0, AdminCallMessageMax);
            var thread = FindAdminThread(target);
            if (thread == null)
            {
                ReplyToUserCommand(player, $"[AdminTicket] No open ticket for {target}.");
                return;
            }
            thread.LastActivity = DateTime.UtcNow;
            string from = player?.PlayerName ?? "Admin";
            int shown = 0;
            foreach (var p in AdminThreadAudience(thread))
            {
                PrintToPlayerChat(p, Localizer.ForPlayer(p, thread.Kind == "report" ? "matchzy.admincall.replyreport" : "matchzy.admincall.reply", from, message));
                if (thread.Kind == "report" && p.SteamID == thread.ReportOwner)
                    p.PrintToCenter(Localizer.ForPlayer(p, "matchzy.admincall.replycenter", from));
                shown++;
            }
            PublishAdminTicketEvent(new AdminReplyEvent
            {
                MatchId = thread.MatchId > 0 ? thread.MatchId : null,
                MapName = SafeMapName(),
                PluginVersion = ModuleVersion,
                MapNumber = thread.MatchId > 0 && isMatchSetup ? matchConfig.CurrentMapNumber : null,
                ThreadId = thread.Id,
                Kind = thread.Kind,
                Server = AdminTicketServerInfo(),
                From = from,
                Message = message,
                SentAt = UtcNowIso(),
            });
            Log($"[AdminTicket] reply {thread.Kind} {thread.Id} from {from} to {shown} player(s): \"{message}\"");
            ReplyToUserCommand(player, $"[AdminTicket] Sent to {shown} player(s).");
        }

        // matchzy_admin_close <thread_id | call_id | steamid64> [message]: ends a ticket. An optional last
        // message is sent as a reply first.
        [ConsoleCommand("matchzy_admin_close", "Closes an admin ticket. Usage: matchzy_admin_close <thread_id|call_id|steamid64> [message]")]
        public void OnAdminCloseCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && !IsTicketAdmin(player))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            var (target, message) = SplitTargetAndText(command);
            var thread = target.Length > 0 ? FindAdminThread(target) : null;
            if (thread == null)
            {
                ReplyToUserCommand(player, target.Length == 0
                    ? "Usage: matchzy_admin_close <thread_id|call_id|steamid64> [message]"
                    : $"[AdminTicket] No open ticket for {target}.");
                return;
            }
            if (message.Length > 0)
                OnAdminReplyCommand(player, command);
            CloseAdminThread(thread, "admin", tellPlayers: true);
            Log($"[AdminTicket] closed {thread.Kind} {thread.Id} by {player?.PlayerName ?? "console"}");
            ReplyToUserCommand(player, $"[AdminTicket] Closed {thread.Id}.");
        }
    }
}
