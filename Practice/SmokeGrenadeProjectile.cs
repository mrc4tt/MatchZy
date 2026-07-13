using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public class SmokeGrenadeProjectile
{
    public static string smokeGrenadeProjectileWindowsSig = @"48 8B C4 48 89 58 ? 48 89 68 ? 48 89 70 ? 57 41 56 41 57 48 81 EC ? ? ? ? 48 8B B4 24 ? ? ? ? 4D 8B F8";

    public static string smokeGrenadeProjectileLinuxSig = @"55 4C 89 C1 48 89 E5 41 57 45 89 CF 41 56 49 89 FE";

    public static string smokeGrenadeProjectileSig = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? smokeGrenadeProjectileLinuxSig : smokeGrenadeProjectileWindowsSig;

    // Parameters: position, angle, velocity, angVelocity, owner, lifetime(float), teamNum(int)
    // Return: IntPtr (pointer to created smoke grenade)
    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, float, int, IntPtr> CSmokeGrenadeProjectile_CreateFunc = new(smokeGrenadeProjectileSig);

    public static nint Create(Vector position, QAngle angle, Vector velocity, CCSPlayerController player)
    {
        if (position == null || angle == null || velocity == null || player == null)
            return IntPtr.Zero;

        return CSmokeGrenadeProjectile_CreateFunc.Invoke(
            position.Handle, // Vector* position
            angle.Handle, // QAngle* angle
            velocity.Handle, // Vector* velocity
            velocity.Handle, // Vector* angVelocity (reusing velocity)
            IntPtr.Zero, // CBaseEntity* owner (null)
            45.0f, // float lifetime (45 seconds)
            (int)player.TeamNum // int team number
        );
    }
}
