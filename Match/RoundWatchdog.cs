using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Live round watchdog (matchzy_round_watchdog). A live round that should be over but never
        // ends (every player of one side dead, or the round timer at 0:00 with no bomb planted)
        // means the engine is not evaluating the win conditions: mp_ignore_round_win_conditions 1
        // or a leaked m_bGameRestart left behind by the ready phase / pause HUD. The watchdog first
        // removes those overrides so the engine ends the round itself; if the round still hangs it
        // ends it with TerminateRound.
        private const float WatchdogClearAfter = 3f;
        private const float WatchdogTerminateAfter = 8f;
        // Round timer: allow this much past 0:00 before counting the round as stuck.
        private const float WatchdogTimeGrace = 2f;

        private CounterStrikeSharp.API.Modules.Timers.Timer? roundWatchdogTimer;
        private bool watchdogRoundOver;
        private float? watchdogStuckSince;
        private bool watchdogClearedThisRound;

        private void InitRoundWatchdog()
        {
            RegisterEventHandler<EventRoundStart>((@event, info) => { ResetRoundWatchdog(); return HookResult.Continue; }, HookMode.Post);
            RegisterEventHandler<EventRoundEnd>((@event, info) => { watchdogRoundOver = true; watchdogStuckSince = null; return HookResult.Continue; }, HookMode.Post);
            roundWatchdogTimer?.Kill();
            roundWatchdogTimer = AddTimer(1.0f, RoundWatchdogTick, TimerFlags.REPEAT);
        }

        private void ResetRoundWatchdog()
        {
            watchdogRoundOver = false;
            watchdogStuckSince = null;
            watchdogClearedThisRound = false;
        }

        private void RoundWatchdogTick()
        {
            try
            {
                if (!roundWatchdog.Value || !isMatchLive || !matchStarted || isPaused || isRoundRestoring
                    || isPractice || isDryRun || isKnifeRound || isSideSelectionPhase || watchdogRoundOver)
                {
                    watchdogStuckSince = null;
                    return;
                }
                var gr = GetGameRules();
                if (gr == null || gr.WarmupPeriod || gr.FreezePeriod || gr.GamePaused || gr.MatchWaitingForResume
                    || gr.CTTimeOutActive || gr.TerroristTimeOutActive || gr.BombPlanted)
                {
                    watchdogStuckSince = null;
                    return;
                }

                float now = Server.CurrentTime;
                bool timeUp = gr.RoundTime > 0 && now > gr.RoundStartTime + gr.RoundTime + WatchdogTimeGrace;

                int ctPlayers = 0, tPlayers = 0, ctAlive = 0, tAlive = 0;
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || p.IsHLTV || IsMatchCoach(p))
                        continue;
                    bool alive = p.PawnIsAlive;
                    if (p.TeamNum == (byte)CsTeam.CounterTerrorist)
                    {
                        ctPlayers++;
                        if (alive) ctAlive++;
                    }
                    else if (p.TeamNum == (byte)CsTeam.Terrorist)
                    {
                        tPlayers++;
                        if (alive) tAlive++;
                    }
                }
                bool eliminated = ctPlayers > 0 && tPlayers > 0 && (ctAlive == 0 || tAlive == 0);

                if (!timeUp && !eliminated)
                {
                    watchdogStuckSince = null;
                    return;
                }

                watchdogStuckSince ??= now;
                float stuckFor = now - watchdogStuckSince.Value;

                if (stuckFor >= WatchdogClearAfter && !watchdogClearedThisRound)
                {
                    watchdogClearedThisRound = true;
                    bool ignoreWin = CounterStrikeSharp.API.Modules.Cvars.ConVar.Find("mp_ignore_round_win_conditions")?.GetPrimitiveValue<bool>() ?? false;
                    Log($"[RoundWatchdog] Round looks stuck (timeUp={timeUp}, CT alive {ctAlive}/{ctPlayers}, T alive {tAlive}/{tPlayers}): "
                        + $"mp_ignore_round_win_conditions={(ignoreWin ? 1 : 0)}, m_bGameRestart={gr.GameRestart}, RestartRoundTime={gr.RestartRoundTime:F1}, now={now:F1}, RoundTime={gr.RoundTime}. Clearing round overrides.");
                    ClearLiveRoundBlockers("round watchdog");
                    return;
                }

                if (stuckFor < WatchdogTerminateAfter)
                    return;

                // Still hanging with the overrides gone: end it the way the engine would have.
                RoundEndReason reason;
                if (eliminated && ctAlive == 0 && tAlive > 0)
                    reason = RoundEndReason.TerroristsWin;
                else if (eliminated && tAlive == 0 && ctAlive > 0)
                    reason = RoundEndReason.CTsWin;
                else
                    reason = RoundEndReason.TargetSaved; // time ran out with no bomb planted
                watchdogRoundOver = true;
                watchdogStuckSince = null;
                Log($"[RoundWatchdog] Round still stuck after {stuckFor:F0}s, ending it: {reason} (CT alive {ctAlive}/{ctPlayers}, T alive {tAlive}/{tPlayers}, timeUp={timeUp}).");
                PrintLocalizedToAll("matchzy.watchdog.roundended");
                float delay = Math.Clamp(CounterStrikeSharp.API.Modules.Cvars.ConVar.Find("mp_round_restart_delay")?.GetPrimitiveValue<float>() ?? 5f, 1f, 15f);
                gr.TerminateRound(delay, reason);
            }
            catch (Exception e)
            {
                Log($"[RoundWatchdog] {e.Message}");
            }
        }
    }
}
