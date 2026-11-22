using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

namespace MatchZy;

public static class GrenadeFunctions
{
    // Resolve only the requested factory. Previously touching one field initialized
    // all four factories, including signature scans for unrelated grenade types.
    // These accesses still belong on the game thread; Lazy does not make engine calls async.
    // Grenade projectile Create factories, resolved by key from the plugin's own
    // gamedata/matchzy.json (single source of truth - byte signatures live only in gamedata, never
    // in this source, so they self-heal on a CS2 update by regenerating the entry with no MatchZy
    // rebuild; works on fork and stock upstream CounterStrikeSharp alike). Guard() keeps
    // resolution off the crash path: a throw in a static field initializer surfaces as a
    // TypeInitializationException before Load() and makes CSS skip the whole plugin, so a missing
    // key degrades a factory to null (the caller skips the rethrow) instead of taking MatchZy down.
    // Why each unresolved factory failed, by gamedata key. Printed once by Prewarm so a server
    // owner can report the cause (key missing, signature not found in this server binary) in one
    // console line instead of only seeing rethrown grenades that bounce and never detonate.
    private static readonly Dictionary<string, string> UnresolvedReasons = new();

    private static TFunc? Guard<TFunc>(string key, Func<string, TFunc> make) where TFunc : class
    {
        try
        {
            TFunc func = make(MzGameData.GetSignature(key));
            // A stale signature can resolve to a zero address without throwing; invoking that fails
            // with "Invalid function pointer". Treat it as unresolved so the caller uses its fallback.
            if (func is NativeObject native && native.Handle == IntPtr.Zero)
            {
                UnresolvedReasons[key] = "signature not found in the server binary (gamedata older or newer than this CS2 build)";
                return null;
            }
            return func;
        }
        catch (Exception e)
        {
            UnresolvedReasons[key] = e.GetBaseException().Message;
            return null;
        }
    }

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>?> CSmokeGrenadeProjectile_CreateFuncLazy = new(() =>
        Guard("CSmokeGrenadeProjectile_Create", sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>(sig)));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>? CSmokeGrenadeProjectile_CreateFunc => CSmokeGrenadeProjectile_CreateFuncLazy.Value;

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>?> CHEGrenadeProjectile_CreateFuncLazy = new(() =>
        Guard("CHEGrenadeProjectile_Create", sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>(sig)));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>? CHEGrenadeProjectile_CreateFunc => CHEGrenadeProjectile_CreateFuncLazy.Value;

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>?> CMolotovProjectile_CreateFuncLazy = new(() =>
        Guard("CMolotovProjectile_Create", sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>(sig)));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>? CMolotovProjectile_CreateFunc => CMolotovProjectile_CreateFuncLazy.Value;

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>?> CDecoyProjectile_CreateFuncLazy = new(() =>
        Guard("CDecoyProjectile_Create", sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>(sig)));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>? CDecoyProjectile_CreateFunc => CDecoyProjectile_CreateFuncLazy.Value;

    // Resolve every factory up front. Each Lazy runs a signature scan over the server binary on
    // first access, and that first access used to be the first .rt / .throw of each grenade type:
    // a long game-thread stall in the middle of practice. The scan must stay on the game thread
    // (StartupWarmup cannot do it), so the caller runs this when practice starts. Returns how many
    // factories resolved; the rest use the managed fallback in GrenadeThrownData.Throw.
    private static bool prewarmReported;

    public static int Prewarm()
    {
        int resolved = 0;
        if (CSmokeGrenadeProjectile_CreateFunc != null) resolved++;
        if (CHEGrenadeProjectile_CreateFunc != null) resolved++;
        if (CMolotovProjectile_CreateFunc != null) resolved++;
        if (CDecoyProjectile_CreateFunc != null) resolved++;

        // Silent when all four resolved: only a failure is worth a console line.
        if (!prewarmReported && resolved < 4)
        {
            prewarmReported = true;
            string platform = OperatingSystem.IsWindows() ? "windows" : "linux";
            Console.WriteLine($"[MatchZy] Grenade rethrow factories resolved: {resolved}/4 ({platform}).");
            foreach (var (key, reason) in UnresolvedReasons)
                Console.WriteLine($"[MatchZy] {key} unresolved ({platform}): {reason}. Rethrown grenades of this type will not detonate until gamedata/matchzy.json matches the server build.");
        }
        return resolved;
    }
}
