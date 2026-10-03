using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Server lifecycle events for remote log receivers: server_ready (plugins loaded and
        // config.cfg applied), map_change (before the map changes) and server_shutdown (quit /
        // restart / fatal). They go to the server's remote log URL and, while a match with its own
        // URL is loaded, to that one too.

        private static readonly string[] MapChangeCommands =
            { "changelevel", "map", "ds_workshop_changelevel", "host_workshop_map" };

        private static readonly Dictionary<string, string> ShutdownCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            { "quit", "quit" },
            { "exit", "quit" },
            { "_restart", "restart" },
        };

        // How long the game thread waits for server_shutdown to be delivered before letting the
        // process go on. The send has its own timeout of the same length.
        private static readonly TimeSpan ShutdownEventWait = TimeSpan.FromSeconds(3);

        private DateTime _mapChangeAnnouncedAt = DateTime.MinValue;
        private bool _shutdownAnnounced;

        private void RegisterServerLifecycleHooks()
        {
            foreach (var command in MapChangeCommands)
                AddCommandListener(command, OnMapChangeCommand, HookMode.Pre);
            foreach (var command in ShutdownCommands.Keys)
                AddCommandListener(command, OnShutdownCommand, HookMode.Pre);
            RegisterListener<Listeners.OnServerPreFatalShutdown>(() => AnnounceShutdown("fatal"));
        }

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            base.OnAllPluginsLoaded(hotReload);
            // config.cfg is exec'd from Load and applied on a later frame; the remote log URL it sets
            // must be in place before server_ready is sent. On a boot this timer runs once the first
            // map ticks.
            AddTimer(2.0f, () =>
            {
                try
                {
                    PublishLifecycleEvent(new ServerReadyEvent
                    {
                        MatchId = LifecycleMatchId(),
                        MapName = Server.MapName,
                        PluginVersion = ModuleVersion,
                        HotReload = hotReload,
                    });
                }
                catch (Exception e)
                {
                    Log($"[server_ready] {e.Message}");
                }
            });
        }

        private HookResult OnMapChangeCommand(CCSPlayerController? player, CommandInfo info)
        {
            // The boot map's own command (+map on the command line) also lands here, before the
            // engine globals exist: there is no map to change from, and Server.MapName is null.
            if (player != null || !AreServerGlobalsReady())
                return HookResult.Continue;
            try
            {
                string next = info.ArgCount > 1 ? info.ArgByIndex(1).Trim() : "";
                if (next == "")
                    return HookResult.Continue; // usage error, no change follows
                AnnounceMapChange(next, info.GetArg(0).ToLowerInvariant());
            }
            catch (Exception e)
            {
                Log($"[map_change] {e.Message}");
            }
            return HookResult.Continue;
        }

        // Called from OnMapEnd: the map changed without a map command we saw (mapcycle, nextlevel,
        // another plugin calling the engine directly).
        private void AnnounceMapEndIfUnannounced()
        {
            if (DateTime.UtcNow - _mapChangeAnnouncedAt < TimeSpan.FromSeconds(30))
                return;
            AnnounceMapChange(null, "map_end");
        }

        private void AnnounceMapChange(string? nextMap, string trigger)
        {
            _mapChangeAnnouncedAt = DateTime.UtcNow;
            PublishLifecycleEvent(new MapChangeEvent
            {
                MatchId = LifecycleMatchId(),
                MapName = Server.MapName,
                PluginVersion = ModuleVersion,
                NextMap = nextMap,
                Trigger = trigger,
            });
        }

        private HookResult OnShutdownCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player != null)
                return HookResult.Continue;
            string command = info.GetArg(0);
            AnnounceShutdown(ShutdownCommands.TryGetValue(command, out var reason) ? reason : "quit");
            return HookResult.Continue;
        }

        // Queues server_shutdown and blocks briefly so it is on the wire before the process exits.
        // Once per process: quit runs the command listener and then unloads the plugin.
        private void AnnounceShutdown(string reason)
        {
            if (_shutdownAnnounced)
                return;
            _shutdownAnnounced = true;
            try
            {
                var sent = PublishLifecycleEvent(new ServerShutdownEvent
                {
                    MatchId = LifecycleMatchId(),
                    MapName = SafeMapName(),
                    PluginVersion = ModuleVersion,
                    Reason = reason,
                });
                if (sent.Length > 0)
                    Task.WaitAll(sent, ShutdownEventWait);
            }
            catch (Exception e)
            {
                try { Log($"[server_shutdown] {e.Message}"); } catch { }
            }
        }

        private long? LifecycleMatchId() => liveMatchId > 0 ? liveMatchId : null;

        // Server.MapName reads a native global; during a fatal shutdown it may already be gone.
        private static string SafeMapName()
        {
            try { return Server.MapName; } catch { return ""; }
        }

        // Sends to the server's own remote log URL and, if a loaded match uses a different one, to
        // that too. Returns the delivery tasks (empty when no URL is configured).
        private Task[] PublishLifecycleEvent(ServerLifecycleEvent @event)
        {
            var targets = new List<RemoteLogTarget>();
            var server = new RemoteLogTarget(defaultRemoteLogURL, defaultRemoteLogHeaderKey,
                defaultRemoteLogHeaderValue, defaultRemoteLogAuthKey, defaultRemoteLogAuthValue);
            if (!string.IsNullOrEmpty(server.Url))
                targets.Add(server);
            var match = CurrentRemoteLogTarget();
            if (!string.IsNullOrEmpty(match.Url) && !string.Equals(match.Url, server.Url, StringComparison.OrdinalIgnoreCase))
                targets.Add(match);
            return targets.Select(t => EnqueueEvent(@event, t, liveMatchId)).ToArray();
        }
    }
}
