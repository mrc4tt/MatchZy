using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

namespace MatchZy;

public static class GrenadeFunctions
{
    // Grenade projectile Create factories, resolved by key from the fork's gamedata.json (single
    // source of truth - byte signatures live only in gamedata.json, never in this source, so they
    // self-heal on a CS2 update by regenerating the entry with no MatchZy rebuild). Guard() keeps
    // resolution off the crash path: a throw in a static field initializer surfaces as a
    // TypeInitializationException before Load() and makes CSS skip the whole plugin, so a missing
    // key degrades a factory to null (the caller skips the rethrow) instead of taking MatchZy down.
    private static TFunc? Guard<TFunc>(Func<TFunc> make) where TFunc : class
    {
        try { return make(); }
        catch { return null; }
    }

    public static readonly MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>? CSmokeGrenadeProjectile_CreateFunc =
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>(GameData.GetSignature("CSmokeGrenadeProjectile_Create")));

    public static readonly MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>? CHEGrenadeProjectile_CreateFunc =
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>(GameData.GetSignature("CHEGrenadeProjectile_Create")));

    public static readonly MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>? CMolotovProjectile_CreateFunc =
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>(GameData.GetSignature("CMolotovProjectile_Create")));

    public static readonly MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>? CDecoyProjectile_CreateFunc =
        Guard(() => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>(GameData.GetSignature("CDecoyProjectile_Create")));
}
