using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;

namespace MatchZy;

// CS2 sends "#SFUI_Notice_Match_Will_Pause" ("The match is set to pause during freeze time", a red
// center box) as a TextMsg when mp_pause_match or timeout_ct_start / timeout_terrorist_start is
// called mid-round (the key is in libserver.so). While MatchZy's own pause box is up
// (matchzy_pause_hud 2) it says the same ("Starts at freeze time"), and the two stacked boxes
// pushed ours down. Drop the engine's notice only then; with matchzy_pause_hud 0 / 1, or a pause
// without our box (admin .forcepause, a raw mp_pause_match), it is shown as before.
public partial class MatchZy
{
    private const int UmTextMsg = 124; // EBaseUserMessages.UM_TextMsg
    private const string WillPauseNotice = "#SFUI_Notice_Match_Will_Pause";

    // .tac runs timeout_*_start before its box state is set; this covers that gap.
    private DateTime pauseNoticeGraceUntil = DateTime.MinValue;

    private void RegisterPauseNoticeFilter()
    {
        try
        {
            HookUserMessage(UmTextMsg, PauseNoticeFilter, HookMode.Pre);
        }
        catch (Exception e)
        {
            Log($"[PauseNoticeFilter] Hook failed, the engine's pause notice stays: {e.Message}");
        }
    }

    private HookResult PauseNoticeFilter(UserMessage um)
    {
        try
        {
            if (pauseHud.Value != 2)
                return HookResult.Continue;
            bool ourBox = techPauseHudTeam != null || tacHudSide != 0 || DateTime.UtcNow < pauseNoticeGraceUntil;
            if (!ourBox)
                return HookResult.Continue;
            int n = um.GetRepeatedFieldCount("param");
            for (int i = 0; i < n; i++)
            {
                string? p = um.ReadString("param", i);
                if (p != null && p.StartsWith(WillPauseNotice, StringComparison.Ordinal))
                    return HookResult.Stop;
            }
        }
        catch (Exception e)
        {
            Log($"[PauseNoticeFilter] {e.Message}");
        }
        return HookResult.Continue;
    }
}
