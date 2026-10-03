using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy
{
    // matchzy_empty_shutdown_seconds: closes a server nobody uses. After the set number of seconds
    // without a human player (bots and CSTV do not count; players reconnecting during a map change
    // do), it sends server_shutdown with reason "empty" and quits. It waits while a demo or round
    // backup upload is still running. The count also starts at server start, so a server nobody
    // ever joins is closed as well.
    public partial class MatchZy
    {
        private const float EmptyShutdownCheckInterval = 10.0f;

        // Demo and round backup uploads in flight (Interlocked).
        private int _pendingUploads;

        private DateTime? _serverEmptySince;
        private bool _emptyShutdownWaitLogged;
        private bool _emptyShutdownStarted;

        private void StartEmptyShutdownMonitor()
        {
            // No STOP_ON_MAPCHANGE: the count runs across map changes.
            AddTimer(EmptyShutdownCheckInterval, CheckEmptyShutdown, TimerFlags.REPEAT);
        }

        private void CheckEmptyShutdown()
        {
            try
            {
                int limit = emptyShutdownSeconds.Value;
                if (limit <= 0 || _emptyShutdownStarted)
                {
                    _serverEmptySince = null;
                    return;
                }

                bool anyPlayer = Utilities.GetPlayers().Any(p =>
                    p != null && p.IsValid && !p.IsBot && !p.IsHLTV
                    && (p.Connected == PlayerConnectedState.Connected || p.Connected == PlayerConnectedState.Connecting));
                if (anyPlayer)
                {
                    _serverEmptySince = null;
                    _emptyShutdownWaitLogged = false;
                    return;
                }

                _serverEmptySince ??= DateTime.UtcNow;
                int emptyFor = (int)(DateTime.UtcNow - _serverEmptySince.Value).TotalSeconds;
                if (emptyFor < limit)
                    return;

                if (Volatile.Read(ref _pendingUploads) > 0)
                {
                    if (!_emptyShutdownWaitLogged)
                    {
                        _emptyShutdownWaitLogged = true;
                        Log($"[EmptyShutdown] Server empty for {emptyFor}s; waiting for uploads to finish before shutting down.");
                    }
                    return;
                }

                _emptyShutdownStarted = true;
                Log($"[EmptyShutdown] No players for {emptyFor}s (matchzy_empty_shutdown_seconds {limit}). Shutting down.");
                // Sent (and briefly waited for) before quit; the quit command listener then sees it
                // was already announced.
                AnnounceShutdown("empty", emptyFor);
                Server.ExecuteCommand("quit");
            }
            catch (Exception e)
            {
                Log($"[EmptyShutdown] {e.Message}");
            }
        }
    }
}
