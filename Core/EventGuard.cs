using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Every event handler this plugin registers goes through here (this hides BasePlugin's
        // method for calls inside the class), so a throw in any handler is logged as
        // "[EventX FATAL]" with the stack and the event goes on with HookResult.Continue. Handlers
        // registered as inline lambdas had no try/catch, and a throw skipped the rest of their
        // state changes (for example the knife round end leaving the side choice never started).
        public new void RegisterEventHandler<T>(GameEventHandler<T> handler, HookMode hookMode = HookMode.Post) where T : GameEvent
        {
            string name = typeof(T).Name;
            base.RegisterEventHandler<T>((@event, info) =>
            {
                try
                {
                    return handler(@event, info);
                }
                catch (Exception e)
                {
                    Log($"[{name} FATAL] {e}");
                    return HookResult.Continue;
                }
            }, hookMode);
        }
    }
}
