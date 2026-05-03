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

    public GrenadeThrownData(
        Vector nadePosition,
        QAngle nadeAngle,
        Vector nadeVelocity,
        Vector playerPosition,
        QAngle playerAngle,
        string grenadeType,
        DateTime thrownTime,
        UInt16 itemIndex,
        float duckAmount = 0.0f
    )
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
        var pawn = player.PlayerPawn.Value;
        pawn.Teleport(PlayerPosition, PlayerAngle, new Vector(0, 0, 0));

        // Issue #391: throw animation leaves the duck state stuck. DuckAmount
        // alone is insufficient — the engine re-derives it next tick from the
        // bDucked/bDucking/bDesiresDuck/bDuckOverride flags, so all of them
        // must be cleared together. DuckRootOffset and DuckViewOffset control
        // the eye/model height interpolation and can also be stuck mid-anim.
        if (pawn.MovementServices == null || pawn.MovementServices.Handle == IntPtr.Zero)
            return;
        var ms = new CCSPlayer_MovementServices(pawn.MovementServices.Handle);
        bool wantDucked = DuckAmount >= 0.5f;
        ms.DuckAmount = wantDucked ? 1.0f : 0.0f;
        ms.Ducked = wantDucked;
        ms.Ducking = false;
        ms.DesiresDuck = wantDucked;
        ms.DuckOverride = false;
        ms.DuckRootOffset = 0.0f;
        ms.DuckViewOffset = wantDucked ? 1.0f : 0.0f;
    }

    public void Throw(CCSPlayerController player)
    {
        // Validate player before accessing any properties
        if (
            player == null
            || !player.IsValid
            || player.Connected != PlayerConnectedState.Connected
            || !player.PlayerPawn.IsValid
            || player.PlayerPawn.Value == null
        )
            return;

        CBaseCSGrenadeProjectile? grenadeEntity = null;
        switch (Type)
        {
            case "smoke":
            {
                grenadeEntity = GrenadeFunctions.CSmokeGrenadeProjectile_CreateFunc.Invoke(
                    Position.Handle,
                    Angle.Handle,
                    Velocity.Handle,
                    Velocity.Handle,
                    IntPtr.Zero,
                    ItemIndex,
                    (int)player.Team
                );
                break;
            }
            case "molotov":
            {
                grenadeEntity = GrenadeFunctions.CMolotovProjectile_CreateFunc.Invoke(
                    Position.Handle,
                    Angle.Handle,
                    Velocity.Handle,
                    Velocity.Handle,
                    IntPtr.Zero,
                    ItemIndex
                );
                break;
            }
            case "hegrenade":
            {
                grenadeEntity = GrenadeFunctions.CHEGrenadeProjectile_CreateFunc.Invoke(
                    Position.Handle,
                    Angle.Handle,
                    Velocity.Handle,
                    Velocity.Handle,
                    IntPtr.Zero,
                    ItemIndex
                );
                break;
            }
            case "decoy":
            {
                grenadeEntity = GrenadeFunctions.CDecoyProjectile_CreateFunc.Invoke(
                    Position.Handle,
                    Angle.Handle,
                    Velocity.Handle,
                    Velocity.Handle,
                    IntPtr.Zero,
                    ItemIndex
                );
                break;
            }
            case "flash":
            {
                grenadeEntity = Utilities.CreateEntityByName<CFlashbangProjectile>(
                    "flashbang_projectile"
                );
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
