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
    private static TFunc? Guard<TFunc>(Func<TFunc> make) where TFunc : class
    {
        try
        {
            TFunc func = make();
            // A stale signature can resolve to a zero address without throwing; invoking that fails
            // with "Invalid function pointer". Treat it as unresolved so the caller uses its fallback.
            if (func is NativeObject native && native.Handle == IntPtr.Zero)
                return null;
            return func;
        }
        catch { return null; }
    }

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>?> CSmokeGrenadeProjectile_CreateFuncLazy = new(() =>
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>(GameData.GetSignature("CSmokeGrenadeProjectile_Create"))));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>? CSmokeGrenadeProjectile_CreateFunc => CSmokeGrenadeProjectile_CreateFuncLazy.Value;

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>?> CHEGrenadeProjectile_CreateFuncLazy = new(() =>
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>(GameData.GetSignature("CHEGrenadeProjectile_Create"))));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>? CHEGrenadeProjectile_CreateFunc => CHEGrenadeProjectile_CreateFuncLazy.Value;

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>?> CMolotovProjectile_CreateFuncLazy = new(() =>
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>(GameData.GetSignature("CMolotovProjectile_Create"))));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>? CMolotovProjectile_CreateFunc => CMolotovProjectile_CreateFuncLazy.Value;

    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>?> CDecoyProjectile_CreateFuncLazy = new(() =>
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>(GameData.GetSignature("CDecoyProjectile_Create"))));

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>? CDecoyProjectile_CreateFunc => CDecoyProjectile_CreateFuncLazy.Value;

    // Resolve every factory up front. Each Lazy runs a signature scan over the server binary on
    // first access, and that first access used to be the first .rt / .throw of each grenade type:
    // a long game-thread stall in the middle of practice. The scan must stay on the game thread
    // (StartupWarmup cannot do it), so the caller runs this when practice starts. Returns how many
    // factories resolved; the rest use the managed fallback in GrenadeThrownData.Throw.
    public static int Prewarm()
    {
        int resolved = 0;
        if (CSmokeGrenadeProjectile_CreateFunc != null) resolved++;
        if (CHEGrenadeProjectile_CreateFunc != null) resolved++;
        if (CMolotovProjectile_CreateFunc != null) resolved++;
        if (CDecoyProjectile_CreateFunc != null) resolved++;
        return resolved;
    }
}
