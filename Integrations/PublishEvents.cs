using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Single HttpClient instance - avoids socket exhaustion from per-request allocation.
        // HttpClient is thread-safe and designed to be long-lived. 60 s: a slow receiver (e.g. a
        // panel writing a big map_result) timed out at 10 s and the event was lost.
        private static readonly HttpClient _sharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

        // Where an event goes, taken when the event is created. Events sent after the match was
        // reset (series_end, match_cancelled) must still reach that match's URL, not the next one's.
        public sealed record RemoteLogTarget(string Url, string HeaderKey, string HeaderValue, string AuthKey, string AuthValue);

        public RemoteLogTarget CurrentRemoteLogTarget() => new(
            matchConfig.RemoteLogURL, matchConfig.RemoteLogHeaderKey, matchConfig.RemoteLogHeaderValue,
            matchConfig.RemoteLogAuthKey, matchConfig.RemoteLogAuthValue);

        // Every event goes through ONE queue with ONE sender, so a receiver gets them in the order
        // they happened (each event used to be its own Task.Run, and round_end / map_result or
        // paused / unpaused could arrive swapped). Events are queued when they are created, which is
        // on the game thread for everything except the few that need a database or upload result
        // first (series_start, demo_upload_ended).
        private sealed record QueuedEvent(MatchZyEvent Event, RemoteLogTarget Target, long MatchId, TaskCompletionSource Sent);

        private readonly System.Threading.Channels.Channel<QueuedEvent> _eventQueue =
            System.Threading.Channels.Channel.CreateUnbounded<QueuedEvent>(new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        private int _eventSenderStarted;

        private Task EnqueueEvent(MatchZyEvent @event, RemoteLogTarget target, long matchId)
        {
            var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (System.Threading.Interlocked.Exchange(ref _eventSenderStarted, 1) == 0)
                Task.Run(RunEventSenderAsync);
            if (!_eventQueue.Writer.TryWrite(new QueuedEvent(@event, target, matchId, sent)))
                sent.TrySetResult(); // queue closed (plugin unloading): drop
            return sent.Task;
        }

        // Cancelled a few seconds after Unload so a reload never leaves the old instance draining a
        // long backlog (and holding the old plugin assembly).
        private readonly CancellationTokenSource _eventSenderCts = new();

        // Receiver health. While it keeps failing, the high-volume live scorebot events (kills,
        // damage, grenades, ...) are dropped for a while instead of each waiting out its timeout,
        // so the queue cannot pile up and match events (round_end, map_result, series_end, ...) are
        // not delayed behind them.
        private int _eventFailuresInARow;
        private DateTime _dropLiveEventsUntil = DateTime.MinValue;
        private int _droppedLiveEvents;
        private const int EventFailuresBeforeBackoff = 3;
        private static readonly TimeSpan LiveEventBackoff = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LiveEventTimeout = TimeSpan.FromSeconds(10);
        // server_shutdown is sent while the process is about to exit; keep the lifecycle events short.
        private static readonly TimeSpan LifecycleEventTimeout = TimeSpan.FromSeconds(3);

        // The scorebot's per-action events (legacy classes ending in LiveEvent, and their Get5-format
        // counterparts starting with Get5) are many per round and only useful in real time;
        // everything else, pauses included, is a match event that must arrive.
        private static bool IsHighVolumeLiveEvent(MatchZyEvent e)
        {
            string name = e.GetType().Name;
            bool live = name.EndsWith("LiveEvent", StringComparison.Ordinal) || name.StartsWith("Get5", StringComparison.Ordinal);
            // Pauses, disconnects and round starts (G5API cleans up a restore on round_start) must arrive.
            return live && e is not MatchPausedLiveEvent && e is not MatchUnpausedLiveEvent && e is not Get5MatchPauseEvent
                && e is not Get5PlayerDisconnectEvent && e is not RoundStartLiveEvent;
        }

        private async Task RunEventSenderAsync()
        {
            var token = _eventSenderCts.Token;
            try
            {
                await foreach (var item in _eventQueue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        bool live = IsHighVolumeLiveEvent(item.Event);
                        if (live && DateTime.UtcNow < _dropLiveEventsUntil)
                        {
                            _droppedLiveEvents++;
                            continue;
                        }
                        TimeSpan? timeout = live ? LiveEventTimeout
                            : item.Event is ServerLifecycleEvent ? LifecycleEventTimeout : null;
                        bool ok = await SendEventCoreAsync(item.Event, item.Target, item.MatchId, timeout, token).ConfigureAwait(false);
                        if (ok)
                        {
                            if (_droppedLiveEvents > 0)
                                Log($"[SendEventAsync] Remote log reachable again; {_droppedLiveEvents} live event(s) were dropped while it was not.");
                            _eventFailuresInARow = 0;
                            _droppedLiveEvents = 0;
                        }
                        else if (++_eventFailuresInARow >= EventFailuresBeforeBackoff)
                        {
                            _dropLiveEventsUntil = DateTime.UtcNow + LiveEventBackoff;
                            Log($"[SendEventAsync] {_eventFailuresInARow} failed sends in a row; dropping live events for {LiveEventBackoff.TotalSeconds:0} s (match events are still sent).");
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw; // unloading: leave the loop
                    }
                    catch (Exception ex)
                    {
                        // Never let one event stop the sender: everything after it would be lost.
                        try { Log($"[SendEventAsync] Sender error: {ex.Message}"); } catch { }
                    }
                    finally
                    {
                        item.Sent.TrySetResult();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Plugin unloading.
            }
        }

        // Called from Unload: stop accepting events, let the sender finish what is queued for a few
        // seconds, then stop it.
        private void CloseEventQueue()
        {
            _eventQueue.Writer.TryComplete();
            try { _eventSenderCts.CancelAfter(TimeSpan.FromSeconds(5)); } catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Fire-and-forget send. Call it on the game thread: the target URL and match id are read
        /// here, and the event is queued in the order it happened.
        /// </summary>
        public void PublishEvent(MatchZyEvent @event)
        {
            if (UseGet5Events)
                @event = ToGet5Event(@event);
            EnqueueEvent(@event, CurrentRemoteLogTarget(), liveMatchId);
        }

        /// <summary>Queues an event; the task completes once it has been sent (or has failed).</summary>
        public Task SendEventAsync(MatchZyEvent @event, RemoteLogTarget? target = null)
        {
            return EnqueueEvent(@event, target ?? CurrentRemoteLogTarget(), liveMatchId);
        }

        // True when the event was delivered or there was nothing to send; false when the receiver
        // failed or timed out (feeds the live-event backoff in RunEventSenderAsync).
        private async Task<bool> SendEventCoreAsync(MatchZyEvent @event, RemoteLogTarget target, long currentMatchId, TimeSpan? timeout, CancellationToken shutdown)
        {
            try
            {
                if (string.IsNullOrEmpty(target.Url))
                    return true;

                long eventMatchId = @event is MatchZyMatchEvent matchEvent ? matchEvent.MatchId : currentMatchId;

                // Never send match events with an invalid matchId. Server lifecycle events
                // (server_ready, map_change, server_shutdown) are not about a match.
                if (eventMatchId == -1 && @event is not ServerLifecycleEvent)
                    return true;

                string json = JsonSerializer.Serialize(@event, @event.GetType());
                using var request = new HttpRequestMessage(HttpMethod.Post, target.Url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(target.HeaderKey) && !string.IsNullOrEmpty(target.HeaderValue))
                {
                    request.Headers.TryAddWithoutValidation(target.HeaderKey, target.HeaderValue);
                }

                if (!string.IsNullOrEmpty(target.AuthKey) && !string.IsNullOrEmpty(target.AuthValue))
                {
                    request.Headers.TryAddWithoutValidation(target.AuthKey, target.AuthValue);
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                if (timeout.HasValue)
                    cts.CancelAfter(timeout.Value);
                using var response = await _sharedHttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[SendEventAsync] {@event.EventName} to {RedactUrl(target.Url)} failed: {response.StatusCode}");
                    return false;
                }
                return true;
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                Log($"[SendEventAsync] Request timed out for {@event.EventName}");
                return false;
            }
            catch (Exception e)
            {
                Log($"[SendEventAsync FATAL] An error occurred: {e.Message}");
                return false;
            }
        }
    }
}
