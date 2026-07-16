using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public class GrenadeThrownData
{
    public Vector Position { get; private set; }

    public QAngle Angle { get; private set; }

    public Vector Velocity { get; private set; }

    // Angular velocity (spin) captured from the projectile at spawn. Separate from
    // linear Velocity: applying linear velocity to AngVelocity spins the model wrong
    // (spin is rad/s, launch is ~600-1000 u/s). Cosmetic - trajectory is driven by
    // linear velocity + gravity - but replaying the real spin matches the original throw.
    public Vector AngularVelocity { get; private set; }

    public Vector PlayerPosition { get; private set; }

    public QAngle PlayerAngle { get; private set; }

    public string Type { get; private set; }

    public DateTime ThrownTime { get; private set; }

    public float Delay { get; set; }

    public UInt16 ItemIndex { get; set; }

    public float DuckAmount { get; private set; }

    public GrenadeThrownData(Vector nadePosition, QAngle nadeAngle, Vector nadeVelocity, Vector playerPosition, QAngle playerAngle, string grenadeType, DateTime thrownTime, UInt16 itemIndex, float duckAmount = 0.0f, Vector? nadeAngularVelocity = null)
    {
        Position = new Vector(nadePosition.X, nadePosition.Y, nadePosition.Z);
        Angle = new QAngle(nadeAngle.X, nadeAngle.Y, nadeAngle.Z);
        Velocity = new Vector(nadeVelocity.X, nadeVelocity.Y, nadeVelocity.Z);
        AngularVelocity = nadeAngularVelocity != null
            ? new Vector(nadeAngularVelocity.X, nadeAngularVelocity.Y, nadeAngularVelocity.Z)
            : new Vector(0.0f, 0.0f, 0.0f);
        PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
        PlayerAngle = new QAngle(playerAngle.X, playerAngle.Y, playerAngle.Z);
        Type = grenadeType;
        ThrownTime = thrownTime;
        Delay = 0;
        ItemIndex = itemIndex;
        // Snap to fully-standing or fully-ducked - anything in-between produces
        // the "MJ peak" half-crouch when restored (issue #391).
        DuckAmount = duckAmount >= 0.5f ? 1.0f : 0.0f;
    }

    public void LoadPosition(CCSPlayerController player)
    {
        if (player == null || player.PlayerPawn.Value == null)
            return;

        // molotov maps to weapon_incgrenade on CT, weapon_molotov on T.
        bool isCT = player.TeamNum == (byte)CsTeam.CounterTerrorist;
        string? nadeWeapon = Type switch
        {
            "smoke" => "weapon_smokegrenade",
            "hegrenade" => "weapon_hegrenade",
            "decoy" => "weapon_decoy",
            "flash" => "weapon_flashbang",
            "molotov" => isCT ? "weapon_incgrenade" : "weapon_molotov",
            _ => null,
        };
        // Issues #391/#393 (AG2): teleport back to the throw position and clear
        // the stuck throw pose. The pose only resets on a REAL weapon deploy, and
        // the only managed call that triggers one is GiveNamedItem - so giveDeploy
        // re-gives the (consumed) grenade by classname, which both restores it AND
        // plays the deploy that clears the pose, leaving the nade in hand at the
        // lineup. No respawn, so the rest of the inventory is untouched.
        MatchZy.TeleportAndClearPose(
            player,
            PlayerPosition,
            PlayerAngle,
            wantDucked: DuckAmount >= 0.5f,
            deployWeapon: nadeWeapon,
            giveDeploy: nadeWeapon != null
        );
    }

    // Managed fallback when a native *_Create sig didn't resolve from gamedata. Logs loudly so a
    // missing/stale gamedata entry is diagnosable instead of a silent dead rethrow.
    private static T? CreateGrenadeFallback<T>(string className, string sigKey) where T : CBaseCSGrenadeProjectile
    {
        Console.WriteLine($"[MatchZy] {sigKey} sig unresolved - using entity API for '{className}'. Verify gamedata/matchzy.json is deployed to addons/counterstrikesharp/gamedata/ and matches this CS2 build.");
        var ent = Utilities.CreateEntityByName<T>(className);
        ent?.DispatchSpawn();
        return ent;
    }

    public void Throw(CCSPlayerController player)
    {
        // Validate player before accessing any properties
        if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null)
            return;

        // Never re-throw a zero-velocity entry: a real thrown grenade always carries
        // launch velocity, so mag≈0 means a corrupt/garbage history record (e.g. a dead
        // nade that a pre-fix build re-recorded). Replaying it would spawn a stationary
        // grenade that drops into the floor/wall at the record's spawn point.
        if (Velocity.X * Velocity.X + Velocity.Y * Velocity.Y + Velocity.Z * Velocity.Z < 1.0f)
            return;

        // Native *_Create factories are resolved by sig from gamedata/matchzy.json. When a sig
        // is missing/stale (or the gamedata file isn't deployed) the factory is null and the old
        // code silently no-op'd - so smoke/he/molotov/decoy just never re-threw while flash (which
        // already used the managed entity API) kept working. Fall back to CreateEntityByName for
        // every type so a rethrow ALWAYS spawns a projectile; the common Teleport block below
        // imparts the recorded launch velocity regardless of how the entity was created.
        CBaseCSGrenadeProjectile? grenadeEntity = null;
        switch (Type)
        {
            case "smoke":
            {
                grenadeEntity = GrenadeFunctions.CSmokeGrenadeProjectile_CreateFunc?.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex, (int)player.Team);
                grenadeEntity ??= CreateGrenadeFallback<CSmokeGrenadeProjectile>("smokegrenade_projectile", "CSmokeGrenadeProjectile_Create");
                break;
            }
            case "molotov":
            case "incendiary":
            {
                grenadeEntity = GrenadeFunctions.CMolotovProjectile_CreateFunc?.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex);
                grenadeEntity ??= CreateGrenadeFallback<CMolotovProjectile>(Type == "incendiary" ? "incendiary_projectile" : "molotov_projectile", "CMolotovProjectile_Create");
                break;
            }
            case "hegrenade":
            {
                grenadeEntity = GrenadeFunctions.CHEGrenadeProjectile_CreateFunc?.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex);
                grenadeEntity ??= CreateGrenadeFallback<CHEGrenadeProjectile>("hegrenade_projectile", "CHEGrenadeProjectile_Create");
                break;
            }
            case "decoy":
            {
                grenadeEntity = GrenadeFunctions.CDecoyProjectile_CreateFunc?.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex);
                grenadeEntity ??= CreateGrenadeFallback<CDecoyProjectile>("decoy_projectile", "CDecoyProjectile_Create");
                break;
            }
            case "flash":
            {
                // Flash has no native factory - always the managed path.
                grenadeEntity = Utilities.CreateEntityByName<CFlashbangProjectile>("flashbang_projectile");
                grenadeEntity?.DispatchSpawn();
                break;
            }
            default:
                Console.WriteLine($"[MatchZy] Unknown Grenade: {Type}");
                break;
        }

        if (grenadeEntity == null)
            return;

        // Apply the recorded launch transform to EVERY grenade type - including smoke.
        // Smokes were previously excluded (DesignerName != "smokegrenade_projectile"),
        // relying on the native Create func to impart velocity; it does not, so a
        // re-thrown smoke dropped dead at the spawn origin with zero velocity. The
        // Teleport(pos, ang, vel) below is what actually launches the projectile.
        // Setting Globalname="custom" also marks the projectile so OnEntitySpawned
        // skips it - without it, dead re-thrown smokes were re-recorded into history
        // with zero velocity, poisoning .last / .rt.
        if (grenadeEntity != null)
        {
            grenadeEntity.InitialPosition.X = Position.X;
            grenadeEntity.InitialPosition.Y = Position.Y;
            grenadeEntity.InitialPosition.Z = Position.Z;

            grenadeEntity.InitialVelocity.X = Velocity.X;
            grenadeEntity.InitialVelocity.Y = Velocity.Y;
            grenadeEntity.InitialVelocity.Z = Velocity.Z;

            // Apply the recorded spin, NOT the linear velocity (old bug: AngVelocity was
            // set to Velocity, producing a wildly wrong model spin on rethrow).
            grenadeEntity.AngVelocity.X = AngularVelocity.X;
            grenadeEntity.AngVelocity.Y = AngularVelocity.Y;
            grenadeEntity.AngVelocity.Z = AngularVelocity.Z;

            grenadeEntity.Teleport(Position, Angle, Velocity);
            grenadeEntity.Globalname = "custom";
            grenadeEntity.TeamNum = player.TeamNum;
            grenadeEntity.Thrower.Raw = player.PlayerPawn.Raw;
            grenadeEntity.OriginalThrower.Raw = player.PlayerPawn.Raw;
            grenadeEntity.OwnerEntity.Raw = player.PlayerPawn.Raw;
        }
    }
}
