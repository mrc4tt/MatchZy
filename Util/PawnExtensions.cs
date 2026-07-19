using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public static class PawnExtensions
{
    // Resets MOVETYPE_NOCLIP to MOVETYPE_WALK so that plugin-initiated
    // teleports (e.g. .last / .spawn) don't leave the player noclipping
    // at the destination. Leaves other move types (e.g. MOVETYPE_NONE
    // for coaches / freeze frames) untouched.
    public static void ResetNoclipToWalk(this CBasePlayerPawn? pawn)
    {
        if (pawn == null) return;
        if (pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP) return;
        pawn.MoveType = MoveType_t.MOVETYPE_WALK;
        pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }
}
