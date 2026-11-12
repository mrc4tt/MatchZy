using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

namespace MatchZy
{
    // CS2's bot quota kicks SourceTV. CCSBotManager::MaintainBotQuota, when there are more bots than
    // the quota wants (bot_quota lowered, bot_quota_mode fill and a human joining a team), first calls
    // KickBotFromTeam(0): "kick a fake client on team Unassigned". That picks the first controller on
    // team 0 with FL_FAKECLIENT whose pawn is missing or dead, which is always the SourceTV controller
    // (team 0, fake client, no pawn), and kicks it with IVEngineServer2::KickClient directly - no
    // console command, so a kickid listener never sees it (RE of build 14196). GOTV stops, the running
    // demo is closed and MatchZy has to restart GOTV, which splits the demo into two files. The bot
    // count is pawn based and never includes SourceTV, so the quota still kicks the real bot on its
    // next pass anyway.
    // Fix: hook KickBotFromTeam and make the team-0 call return false without running. The quota then
    // goes straight to the CT/T bot kick; its math is unchanged. Side effect: a non-HLTV bot stuck on
    // team 0 is not trimmed by the quota (bots are put on a team as they are added, so in practice
    // only SourceTV is ever there). matchzy_cstv_kick_guard false lifts the guard.
    public partial class MatchZy
    {
        private MemoryFunctionWithReturn<int, bool>? _kickBotFromTeam;
        private Func<DynamicHook, HookResult>? _kickBotFromTeamHook;
        private long _cstvKickGuardLogMs;

        private void RegisterCstvKickGuard()
        {
            try
            {
                string? sig = null;
                try { sig = MzGameData.GetSignature("KickBotFromTeam"); } catch { }
                if (string.IsNullOrEmpty(sig))
                {
                    Log("[CstvKickGuard] gamedata key KickBotFromTeam missing: the bot quota can still kick the CSTV client (demo split).");
                    return;
                }
                _kickBotFromTeam = new MemoryFunctionWithReturn<int, bool>(sig);
                _kickBotFromTeamHook = OnKickBotFromTeam;
                _kickBotFromTeam.Hook(_kickBotFromTeamHook, HookMode.Pre);
                Log("[CstvKickGuard] KickBotFromTeam hooked: the bot quota no longer kicks the CSTV client.");
            }
            catch (Exception e)
            {
                _kickBotFromTeam = null;
                _kickBotFromTeamHook = null;
                Log($"[CstvKickGuard] hooking KickBotFromTeam failed: {e.Message}");
            }
        }

        private HookResult OnKickBotFromTeam(DynamicHook hook)
        {
            try
            {
                int team = hook.GetParam<int>(0);
                // Practice manages its bots itself (it pins bot_quota and kicks extras by userid).
                // While a .bot is being added the quota can briefly count one bot too many; that
                // trim used to land on SourceTV (team 0) and now, with team 0 skipped, it would
                // kick the practice bot just placed. So in practice the quota kicks nobody.
                if (isPractice)
                {
                    hook.SetReturn(false);
                    return HookResult.Stop;
                }
                if (!cstvKickGuard.Value || team != 0)
                    return HookResult.Continue;
                long now = Environment.TickCount64;
                if (now - _cstvKickGuardLogMs > 30000)
                {
                    _cstvKickGuardLogMs = now;
                    Log("[CstvKickGuard] Bot quota tried to kick a fake client on team Unassigned (SourceTV); skipped, a real bot goes instead.");
                }
                hook.SetReturn(false);
                return HookResult.Stop;
            }
            catch
            {
                return HookResult.Continue;
            }
        }

        // Unload: the detour must not outlive the plugin's managed callback.
        private void UnregisterCstvKickGuard()
        {
            try
            {
                if (_kickBotFromTeam != null && _kickBotFromTeamHook != null)
                    _kickBotFromTeam.Unhook(_kickBotFromTeamHook, HookMode.Pre);
            }
            catch { }
            _kickBotFromTeam = null;
            _kickBotFromTeamHook = null;
        }
    }
}
