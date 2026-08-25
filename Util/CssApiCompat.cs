using System.Runtime.CompilerServices;
#if HAS_CSS_TRACE
using CounterStrikeSharp.API.Modules.Utils;
#endif

namespace MatchZy
{
    public partial class MatchZy
    {
        // Compatibility helpers for running the same MatchZy.dll on both the forked
        // CounterStrikeSharp build (~1.0.39x) and stock upstream CounterStrikeSharp
        // (roflmuffin/CounterStrikeSharp, <= 1.0.37x). The fork adds API surface the stock
        // build does not have (CommandLine, the Trace API). Referencing a missing type makes
        // the JIT throw TypeLoadException when it compiles the method containing the
        // reference, so every fork-only touch must live in its own NoInlining method behind
        // a runtime probe. Never reference a fork-only type from a method that stock servers
        // will JIT.

        private static bool? hasCssTraceApi;

        // True when the running CounterStrikeSharp build ships the fork's Trace API
        // (CounterStrikeSharp.API.Modules.Utils.Trace). Probed once; false on stock builds.
        public static bool HasCssTraceApi
        {
            get
            {
                if (hasCssTraceApi == null)
                {
#if HAS_CSS_TRACE
                    try
                    {
                        ProbeCssTraceApi();
                        hasCssTraceApi = true;
                    }
                    catch
                    {
                        hasCssTraceApi = false;
                    }
#else
                    hasCssTraceApi = false;
#endif
                }
                return hasCssTraceApi.Value;
            }
        }

#if HAS_CSS_TRACE
        // Returning the Type forces the JIT to resolve the Trace typeref (a discarded
        // "_ = typeof(Trace)" gets elided by Release codegen and never throws); on a stock
        // build the JIT throws TypeLoadException, caught by the probe above. NoInlining
        // keeps the reference out of the caller.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Type ProbeCssTraceApi()
        {
            return typeof(Trace);
        }
#endif

        // Bare launch-option probe (e.g. "-nohltv") via the raw process command line. Used
        // instead of the fork-only CounterStrikeSharp.API.CommandLine helper so the check
        // works on stock builds too.
        private static bool HasLaunchOption(string flag)
        {
            try
            {
                return Environment.GetCommandLineArgs().Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}
