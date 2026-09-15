using System.Runtime.CompilerServices;
using System.Text;
#if HAS_CSS_COMMANDLINE
using CounterStrikeSharp.API;
#endif
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

        private static bool? hasCssCommandLineApi;

        // True when the running CounterStrikeSharp build ships the fork's CommandLine helper
        // (CounterStrikeSharp.API.CommandLine -> the engine's own ICommandLine). Probed once;
        // false on stock builds.
        public static bool HasCssCommandLineApi
        {
            get
            {
                if (hasCssCommandLineApi == null)
                {
#if HAS_CSS_COMMANDLINE
                    try
                    {
                        ProbeCssCommandLineApi();
                        hasCssCommandLineApi = true;
                    }
                    catch
                    {
                        hasCssCommandLineApi = false;
                    }
#else
                    hasCssCommandLineApi = false;
#endif
                }
                return hasCssCommandLineApi.Value;
            }
        }

#if HAS_CSS_COMMANDLINE
        // Same NoInlining + returned-Type trick as ProbeCssTraceApi: keep the fork-only typeref
        // out of any method a stock server will JIT.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Type ProbeCssCommandLineApi()
        {
            return typeof(CommandLine);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasParamViaFork(string flag)
        {
            return CommandLine.HasParam(flag);
        }
#endif

        // Bare launch-option probe (e.g. "-nohltv", "-nobots").
        //
        // Environment.GetCommandLineArgs() is NOT reliable here: the managed runtime is hosted
        // inside the game process rather than being its entry point, so it can report just the
        // assembly path and never the server's real argv. Every launch-option check then reads
        // false, which is how a server started with -nobots still ran .bot and produced the
        // modelless, invisible bot shells the engine hands out when bots are disabled.
        //
        // Order: the fork's CommandLine helper (the engine's own ICommandLine - authoritative),
        // then /proc/self/cmdline (the real NUL-separated argv on Linux), then the Environment
        // args as a last resort.
        private static bool HasLaunchOption(string flag)
        {
#if HAS_CSS_COMMANDLINE
            if (HasCssCommandLineApi)
            {
                try
                {
                    return HasParamViaFork(flag);
                }
                catch
                {
                    // Fall through to the process command line.
                }
            }
#endif

            try
            {
                if (File.Exists("/proc/self/cmdline"))
                {
                    string raw = Encoding.UTF8.GetString(File.ReadAllBytes("/proc/self/cmdline"));
                    foreach (string arg in raw.Split('\0'))
                    {
                        if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;
                }
            }
            catch
            {
                // Fall through to the Environment args.
            }

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
