using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Get5 compatibility for the ready-up time limit and join ready mode.
    //
    // The limit itself is matchzy_forfeit_ready_timeout (Forfeit.cs). Get5 panels send
    // get5_time_to_start / get5_time_to_start_veto, which set the same values here, and
    // .addreadytime gives the teams more time, as get5_add_ready_time does.
    //
    // Join ready mode (matchzy_ready_mode 1): in a loaded match a team counts as ready once
    // min_players_to_ready of its players are on its side, nobody has to type .ready, and the match
    // starts matchzy_join_start_delay seconds after everyone has joined.
    public partial class MatchZy
    {
        [ConsoleCommand("matchzy_time_to_start", "Seconds teams have to ready up before a team that is not ready forfeits (same as matchzy_forfeit_ready_timeout). 0 = no limit.")]
        [ConsoleCommand("get5_time_to_start", "Seconds teams have to ready up before a team that is not ready forfeits (same as matchzy_forfeit_ready_timeout). 0 = no limit.")]
        public void OnTimeToStartCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (int.TryParse(command.ArgByIndex(1).Trim('"'), out int seconds) && seconds >= 0)
                forfeitReadyTimeout.Value = seconds;
        }

        [ConsoleCommand("matchzy_time_to_start_veto", "Seconds teams have to ready up for the map veto (same as matchzy_forfeit_veto_ready_timeout). 0 = no limit.")]
        [ConsoleCommand("get5_time_to_start_veto", "Seconds teams have to ready up for the map veto (same as matchzy_forfeit_veto_ready_timeout). 0 = no limit.")]
        public void OnTimeToStartVetoCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (int.TryParse(command.ArgByIndex(1).Trim('"'), out int seconds) && seconds >= 0)
                forfeitVetoReadyTimeout.Value = seconds;
        }

        // The ready-up time limit of the phase in progress: the veto's own limit before the map veto
        // (-1 = the same as the map's), else matchzy_forfeit_ready_timeout. 0 = no limit.
        private int CurrentReadyTimeout()
        {
            if (isPreVeto && forfeitVetoReadyTimeout.Value >= 0)
                return forfeitVetoReadyTimeout.Value;
            return forfeitReadyTimeout.Value;
        }

        [ConsoleCommand("css_addreadytime", "Gives the teams more time to ready up. Usage: .addreadytime <seconds>")]
        [ConsoleCommand("matchzy_add_ready_time", "Gives the teams more time to ready up. Usage: matchzy_add_ready_time <seconds>")]
        [ConsoleCommand("get5_add_ready_time", "Gives the teams more time to ready up. Usage: get5_add_ready_time <seconds>")]
        public void OnAddReadyTimeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            HandleAddReadyTimeCommand(player, command != null && command.ArgCount > 1 ? command.ArgByIndex(1) : "");
        }

        public void HandleAddReadyTimeCommand(CCSPlayerController? player, string argument)
        {
            if (!IsPlayerAdmin(player, "css_addreadytime", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (!int.TryParse(argument.Trim('"'), out int seconds) || seconds <= 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.ready.addreadytimeusage"));
                return;
            }
            int timeout = CurrentReadyTimeout();
            if (timeout <= 0 || readyPhaseStartedAt == null || !isMatchSetup || matchStarted || !readyAvailable)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.ready.nolimit"));
                return;
            }
            // As in Get5: the seconds come off the time already used, so at most the full time is left.
            DateTime now = DateTime.UtcNow;
            DateTime shifted = readyPhaseStartedAt.Value.AddSeconds(seconds);
            readyPhaseStartedAt = shifted > now ? now : shifted;
            forfeitWarnedSecondsLeft = -1;
            int secondsLeft = (int)Math.Ceiling(timeout - (now - readyPhaseStartedAt.Value).TotalSeconds);
            PrintLocalizedToAll("matchzy.ready.addreadytime", seconds, FormatReadyTime(secondsLeft));
        }

        private static string FormatReadyTime(int seconds)
        {
            seconds = Math.Max(0, seconds);
            return seconds >= 60 ? $"{seconds / 60}:{seconds % 60:00}" : $"{seconds}s";
        }

        // Get5's reminder schedule: every minute, every 30 seconds in the last five minutes, and at
        // 10 seconds. checkInterval is how often the caller runs; returns the mark reached in this
        // interval, or null.
        private static int? DueReadyReminder(int secondsLeft, float checkInterval)
        {
            int window = (int)Math.Ceiling(checkInterval);
            int mark = secondsLeft <= 10 ? 10
                : secondsLeft <= 300 ? (int)Math.Ceiling(secondsLeft / 30.0) * 30
                : (int)Math.Ceiling(secondsLeft / 60.0) * 60;
            return mark < secondsLeft + window ? mark : null;
        }

        // ── Join ready mode ──────────────────────────────────────────────────────────────────

        private bool IsJoinReadyMode() => isMatchSetup && readyMode.Value == 1;

        // When the join-mode start countdown ends; null while it does not run. A deadline rather than
        // a per-call counter: the caller can run more than once a second.
        private DateTime? joinStartAt;
        private int joinStartLastAnnounced = -1;

        // Join mode: a side is ready once min_players_to_ready of its players are on it (spectators:
        // min_spectators_to_ready). Null when join mode does not apply.
        private bool? IsTeamJoinedReady(int team)
        {
            if (!IsJoinReadyMode())
                return null;
            (int players, _) = GetTeamPlayerCount(team, false);
            if (team == (int)CsTeam.Spectator)
                return players >= matchConfig.MinSpectatorsToReady;
            if (team != (int)CsTeam.CounterTerrorist && team != (int)CsTeam.Terrorist)
                return null;
            // Never ready with nobody on the side, forced or not (as IsTeamReady's own rule).
            int needed = matchConfig.MinPlayersToReadySet ? matchConfig.MinPlayersToReady : matchConfig.PlayersPerTeam;
            return players > 0 && (players >= Math.Max(1, needed) || IsTeamForcedReady((CsTeam)team));
        }

        // Called every second during the ready phase (SendReadyStatusHintMessage). Starts the match
        // matchzy_join_start_delay seconds after everyone has joined; someone leaving stops it.
        private void HandleJoinStartCountdown()
        {
            if (!IsJoinReadyMode() || matchStarted || !readyAvailable || matchStartInProgress || mapChangePending)
            {
                joinStartAt = null;
                return;
            }
            SyncJoinReadyFlags();
            bool everyoneJoined = IsTeamsReady() && IsSpectatorsReady();
            if (!everyoneJoined)
            {
                if (joinStartAt != null)
                {
                    joinStartAt = null;
                    PrintLocalizedToAll("matchzy.ready.joinstartstopped");
                }
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (joinStartAt == null)
            {
                int delay = Math.Max(0, joinStartDelay.Value);
                joinStartAt = now.AddSeconds(delay);
                joinStartLastAnnounced = delay;
                if (delay > 0)
                {
                    PrintLocalizedToAll("matchzy.ready.joinstartcountdown", delay);
                    return;
                }
            }
            int left = (int)Math.Ceiling((joinStartAt.Value - now).TotalSeconds);
            if (left > 0)
            {
                if (left != joinStartLastAnnounced && (left <= 5 || left % 10 == 0))
                {
                    joinStartLastAnnounced = left;
                    PrintLocalizedToAll("matchzy.ready.joinstartin", left);
                }
                return;
            }
            joinStartAt = null;
            Log("[JoinReady] Everyone has joined, starting the match.");
            CheckLiveRequired(fromJoinCountdown: true);
        }

        // Join mode: a player on T/CT counts as ready, so the per-player readers (ready panel, chat
        // reminder, [READY] tags, .readycheck) agree with the side readiness above.
        private void SyncJoinReadyFlags()
        {
            foreach (var (userId, player) in playerData)
            {
                if (player == null || !player.IsValid || player.IsBot || IsMatchCoach(player))
                    continue;
                bool onSide = player.TeamNum == (int)CsTeam.CounterTerrorist || player.TeamNum == (int)CsTeam.Terrorist;
                if (!playerReadyStatus.TryGetValue(userId, out bool current) || current != onSide)
                {
                    playerReadyStatus[userId] = onSide;
                    _readyStatusDirty = true;
                }
            }
        }
    }
}
