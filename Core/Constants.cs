namespace MatchZy;

class Constants
{
    public static readonly Dictionary<string, string> ProjectileTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "smokegrenade_projectile", "smoke" },
        { "flashbang_projectile", "flash" },
        { "hegrenade_projectile", "hegrenade" },
        { "decoy_projectile", "decoy" },
        { "molotov_projectile", "molotov" },
        { "incendiary_projectile", "incendiary" },
    };

    public static readonly Dictionary<string, string> NadeProjectileMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "smoke", "smokegrenade_projectile" },
        { "flash", "flashbang_projectile" },
        { "hegrenade", "hegrenade_projectile" },
        { "decoy", "decoy_projectile" },
        { "molotov", "molotov_projectile" },
        { "incendiary", "incendiary_projectile" },
    };
}
