using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public class PlayerLocationData
{
    public Vector Position { get; set; }
    public QAngle Angle { get; set; }

    public PlayerLocationData(Vector position, QAngle angle)
    {
        this.Position = position;
        this.Angle = angle;
    }

    public void LoadPosition(CCSPlayerController player)
    {
        if (player == null || player.PlayerPawn.Value == null)
            return;
        // Issues #391/#393 (AG2): teleport + clear any stuck throw pose via a
        // weapon re-deploy. No specific grenade to restore here — re-deploy onto
        // whatever the player currently holds (captured by classname) so they end
        // up with the same weapon in hand; the knife-bounce inside the helper does
        // the actual pose reset.
        string? heldWeapon = player.PlayerPawn.Value.WeaponServices?.ActiveWeapon?.Value?.DesignerName;
        MatchZy.TeleportAndClearPose(player, Position, Angle, deployWeapon: heldWeapon);
    }
}
