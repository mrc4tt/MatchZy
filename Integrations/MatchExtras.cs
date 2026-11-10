using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Small match additions: phase events for panels, matchzy_test_event, round backup clean-up,
    // clearing an engine pause nobody asked for at go-live.
    public partial class MatchZy
    {
        // ---------------------------------------------------------------- phase events

        private void PublishPhaseEvent(string name, int roundNumber, int? overtime = null, string? winner = null, string? team1Side = null)
        {
            if (!isMatchSetup || liveMatchId <= 0)
                return;
            PublishEvent(new MatchPhaseEvent(name)
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                RoundNumber = roundNumber,
                OvertimeNumber = overtime,
                Winner = winner,
                Team1Side = team1Side,
            });
        }

        private void PublishKnifeRoundStarted() => PublishPhaseEvent("knife_round_started", 0);

        private void PublishKnifeRoundEnded(int winnerSide) =>
            PublishPhaseEvent("knife_round_ended", 0, winner: TeamKeyForSide(winnerSide));

        // After a live round end: side_swap + halftime_started when the sides were swapped (halftime or
        // an overtime half), overtime_started when an overtime begins. Uses the same overtime rule as
        // the overtime pause budget (IsOvertimeStartingNext), with the round end's scores.
        private void PublishPostRoundPhaseEvents(bool swapped, int t1score, int t2score)
        {
            if (!isMatchSetup || liveMatchId <= 0)
                return;
            int played = t1score + t2score;
            if (swapped)
            {
                string team1Side = teamSides.TryGetValue(matchzyTeam1, out var side) && side == "CT" ? "ct" : "t";
                PublishPhaseEvent("halftime_started", played);
                PublishPhaseEvent("side_swap", played, team1Side: team1Side);
            }
            if (IsOvertimeStartingNext(t1score, t2score))
            {
                int maxRounds = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 0;
                int otMaxRounds = ConVar.Find("mp_overtime_maxrounds")?.GetPrimitiveValue<int>() ?? 0;
                int overtime = played <= maxRounds || otMaxRounds <= 0 ? 1 : (played - maxRounds) / otMaxRounds + 1;
                PublishPhaseEvent("overtime_started", played, overtime: overtime);
            }
        }

        // ---------------------------------------------------------------- matchzy_test_event

        [ConsoleCommand("matchzy_test_event", "Sends a test_event to the remote log URL(s) to check that a panel can be reached")]
        public void OnTestEventCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && !IsPlayerAdmin(player, "matchzy_test_event", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            var sent = PublishLifecycleEvent(new TestEvent
            {
                MatchId = isMatchSetup ? liveMatchId : null,
                MapName = SafeMapName(),
                PluginVersion = ModuleVersion,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            ReplyToUserCommand(player, sent.Length == 0
                ? "[TestEvent] No remote log URL is set (matchzy_remote_log_url or the match config's remote_log_url)."
                : $"[TestEvent] test_event queued for {sent.Length} URL(s). A failed send is logged as [SendEventAsync].");
        }

        // ---------------------------------------------------------------- backup clean-up

        private static readonly Regex RoundBackupFile = new(@"^matchzy_(\d+)_\d+_round\d+\.(json|txt)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // matchzy_backup_keep_days: deletes MatchZy's round backups (MatchZyDataBackup/*.json and the
        // engine's matchzy_*_round*.txt) older than that many days, never the running match's.
        // Runs at load and at series end, off the game thread.
        private void CleanupOldRoundBackups()
        {
            int days = backupKeepDays.Value;
            if (days <= 0)
                return;
            // The engine's .txt files land where EngineRoundBackupDirs says (csgo/, addons/metamod/
            // on Metamod servers, csgo/backups/); MatchZy's own .json in MatchZyDataBackup.
            string dataDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");
            string[] dirs = EngineRoundBackupDirs().Append(dataDir).Distinct().ToArray();
            long keepMatch = liveMatchId;
            DateTime cutoff = DateTime.UtcNow.AddDays(-days);
            Task.Run(() =>
            {
                int deleted = 0;
                foreach (string dir in dirs)
                {
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (string file in Directory.EnumerateFiles(dir, "matchzy_*_round*.*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var m = RoundBackupFile.Match(Path.GetFileName(file));
                            if (!m.Success || (keepMatch > 0 && m.Groups[1].Value == keepMatch.ToString()))
                                continue;
                            if (File.GetLastWriteTimeUtc(file) >= cutoff)
                                continue;
                            File.Delete(file);
                            deleted++;
                        }
                        catch (Exception) { }
                    }
                }
                if (deleted > 0)
                    Log($"[BackupCleanup] Deleted {deleted} round backup file(s) older than {days} day(s).");
            });
        }

        // ---------------------------------------------------------------- stale engine pause at go-live

        // A few seconds after going live: the engine is paused, but MatchZy has no pause (an engine
        // pause from the map load or a cfg, e.g. mp_backup_restore_load_autopause). .unpause would do
        // nothing then, so lift it. A MatchZy pause, a pause carried to live or a round restore keep it.
        private void ScheduleStalePauseCheck()
        {
            AddTimer(8.0f, () =>
            {
                try
                {
                    if (!isMatchLive || isPaused || isRoundRestoring || isRoundRestorePending || pauseCarriedToLive != null
                        || pausedStateTimer != null)
                        return;
                    var gameRules = GetGameRules();
                    if (gameRules == null || !gameRules.MatchWaitingForResume)
                        return;
                    if (gameRules.CTTimeOutActive || gameRules.TerroristTimeOutActive)
                        return;
                    // Also a raw mp_pause_match from a panel's RCON in these 8 s: logged so it can be told apart.
                    Log("[GoLive] The engine was paused without a MatchZy pause 8 s after going live (map load / cfg autopause, or a raw mp_pause_match over RCON) - unpausing.");
                    Server.ExecuteCommand("mp_unpause_match");
                }
                catch (Exception e)
                {
                    Log($"[GoLive] Stale pause check failed: {e.Message}");
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        // A tactical timeout is the game's own timeout (timeout_ct_start / timeout_terrorist_start),
        // not mp_pause_match, so it never goes through SetMatchPausedFlags. Report it as a pause of
        // type "tactical" (Get5: game_paused) when it is called, and as an unpause once neither
        // team's timeout is active any more (checked every second). The same tick refreshes the
        // on-screen box (TacticalTimeoutHudTick in Pause/TechPauseHud.cs).
        private CounterStrikeSharp.API.Modules.Timers.Timer? tacticalTimeoutWatch;

        private void PublishTacticalTimeoutStarted(CCSPlayerController player)
        {
            PublishEvent(new MatchPausedLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                PauseType = "tactical",
                TeamName = GetTacTeamName(player),
                MaxDuration = ReadIntCvar("mp_team_timeout_time"),
                RoundNumber = GetRoundNumer(),
            });

            StartTacticalTimeoutHud(player);
            tacticalTimeoutWatch?.Kill();
            tacticalTimeoutWatch = AddTimer(1.0f, TacticalTimeoutWatchTick, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            Server.NextFrame(() =>
            {
                var gameRules = GetGameRules();
                if (tacticalTimeoutWatch != null && isMatchLive && gameRules != null)
                    TacticalTimeoutHudTick(gameRules);
            });
        }

        private void TacticalTimeoutWatchTick()
        {
            try
            {
                var gameRules = GetGameRules();
                if (isMatchLive && gameRules != null && (gameRules.CTTimeOutActive || gameRules.TerroristTimeOutActive))
                {
                    TacticalTimeoutHudTick(gameRules);
                    return;
                }
                tacticalTimeoutWatch?.Kill();
                tacticalTimeoutWatch = null;
                StopTacticalTimeoutHud();
                if (!isMatchLive)
                    return;
                PublishEvent(new MatchUnpausedLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = GetRoundNumer(),
                });
            }
            catch (Exception e)
            {
                Log($"[TacticalTimeout] Watch failed: {e.Message}");
                tacticalTimeoutWatch?.Kill();
                tacticalTimeoutWatch = null;
                StopTacticalTimeoutHud();
            }
        }
    }
}
