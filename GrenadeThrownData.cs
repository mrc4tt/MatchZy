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

    public Vector PlayerPosition { get; private set; }

    public QAngle PlayerAngle { get; private set; }

    public string Type { get; private set; }

    public DateTime ThrownTime { get; private set; }

    public float Delay { get; set; }

    public UInt16 ItemIndex { get; set; }

    public float DuckAmount { get; private set; }

    public GrenadeThrownData(Vector nadePosition, QAngle nadeAngle, Vector nadeVelocity, Vector playerPosition, QAngle playerAngle, string grenadeType, DateTime thrownTime, UInt16 itemIndex, float duckAmount = 0.0f)
    {
        Position = new Vector(nadePosition.X, nadePosition.Y, nadePosition.Z);
        Angle = new QAngle(nadeAngle.X, nadeAngle.Y, nadeAngle.Z);
        Velocity = new Vector(nadeVelocity.X, nadeVelocity.Y, nadeVelocity.Z);
        PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
        PlayerAngle = new QAngle(playerAngle.X, playerAngle.Y, playerAngle.Z);
        Type = grenadeType;
        ThrownTime = thrownTime;
        Delay = 0;
        ItemIndex = itemIndex;
        // Snap to fully-standing or fully-ducked — anything in-between produces
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
        // Inventory slot for the thrown grenade type, used to re-deploy it after
        // the teleport so it's in hand at the lineup position.
        string? nadeSlot = Type switch
        {
            "hegrenade" => "slot6",
            "flash" => "slot7",
            "smoke" => "slot8",
            "decoy" => "slot9",
            "molotov" => "slot10",
            _ => null,
        };

        // Issues #391/#393 (AG2): teleport back to the throw position and clear
        // the stuck throw pose via a weapon re-deploy (no respawn, so the rest
        // of the inventory is untouched). The thrown grenade itself was consumed,
        // so re-give it in afterRestore before the re-deploy kicks in.
        MatchZy.TeleportAndClearPose(
            player,
            PlayerPosition,
            PlayerAngle,
            wantDucked: DuckAmount >= 0.5f,
            switchSlot: nadeSlot,
            afterRestore: nadeWeapon == null
                ? null
                : () =>
                {
                    var pawn = player.PlayerPawn.Value;
                    bool alreadyHas = pawn?.WeaponServices?.MyWeapons.Any(h => h.Value != null && h.Value.IsValid && h.Value.DesignerName == nadeWeapon) ?? false;
                    if (!alreadyHas)
                        player.GiveNamedItem(nadeWeapon);
                }
        );
    }

    public void Throw(CCSPlayerController player)
    {
        // Validate player before accessing any properties
        if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null)
            return;

        CBaseCSGrenadeProjectile? grenadeEntity = null;
        switch (Type)
        {
            case "smoke":
            {
                grenadeEntity = GrenadeFunctions.CSmokeGrenadeProjectile_CreateFunc.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex, (int)player.Team);
                break;
            }
            case "molotov":
            {
                grenadeEntity = GrenadeFunctions.CMolotovProjectile_CreateFunc.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex);
                break;
            }
            case "hegrenade":
            {
                grenadeEntity = GrenadeFunctions.CHEGrenadeProjectile_CreateFunc.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex);
                break;
            }
            case "decoy":
            {
                grenadeEntity = GrenadeFunctions.CDecoyProjectile_CreateFunc.Invoke(Position.Handle, Angle.Handle, Velocity.Handle, Velocity.Handle, IntPtr.Zero, ItemIndex);
                break;
            }
            case "flash":
            {
                grenadeEntity = Utilities.CreateEntityByName<CFlashbangProjectile>("flashbang_projectile");
                if (grenadeEntity == null)
                    return;
                grenadeEntity.DispatchSpawn();
                break;
            }
            default:
                Console.WriteLine($"[MatchZy] Unknown Grenade: {Type}");
                break;
        }

        if (grenadeEntity != null && grenadeEntity.DesignerName != "smokegrenade_projectile")
        {
            grenadeEntity.InitialPosition.X = Position.X;
            grenadeEntity.InitialPosition.Y = Position.Y;
            grenadeEntity.InitialPosition.Z = Position.Z;

            grenadeEntity.InitialVelocity.X = Velocity.X;
            grenadeEntity.InitialVelocity.Y = Velocity.Y;
            grenadeEntity.InitialVelocity.Z = Velocity.Z;

            grenadeEntity.AngVelocity.X = Velocity.X;
            grenadeEntity.AngVelocity.Y = Velocity.Y;
            grenadeEntity.AngVelocity.Z = Velocity.Z;

            grenadeEntity.Teleport(Position, Angle, Velocity);
            grenadeEntity.Globalname = "custom";
            grenadeEntity.TeamNum = player.TeamNum;
            grenadeEntity.Thrower.Raw = player.PlayerPawn.Raw;
            grenadeEntity.OriginalThrower.Raw = player.PlayerPawn.Raw;
            grenadeEntity.OwnerEntity.Raw = player.PlayerPawn.Raw;
        }
    }
}
