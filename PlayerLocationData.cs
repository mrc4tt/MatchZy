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
        // weapon re-deploy. No grenade slot to restore here, so re-deploy onto
        // the primary (slot1).
        MatchZy.TeleportAndClearPose(player, Position, Angle, switchSlot: "slot1");
    }
}
