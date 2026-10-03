using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

// matchzy_tech_pause_mode 1: technical pauses with Get5's rules (pausing.sp).
//   - A pause takes effect in freeze time; from then on it counts as used and its time runs.
//   - Until it takes effect, the pausing team can cancel it with .unpause without using it up.
//   - Once it has lasted matchzy_tech_pause_duration seconds, either team can .unpause on its own
//     (0 = never, both teams always have to). It never ends by itself.
//   - matchzy_max_tech_pauses_allowed 0 = unlimited.
// Get5 panels send get5_max_tech_pauses / get5_tech_pause_time, which set the same values and
// switch to this mode.
public partial class MatchZy
{
    private bool Get5TechPauseMode => techPauseMode.Value == 1;

    // Technical pauses used from a backup of another match or map, applied when its round is loaded.
    private (int Team1, int Team2)? pendingRestoreTechPauses;

    // The Get5-mode tech pause in progress: the team that called it, and the seconds it has been in
    // effect (-1 until freeze time).
    private Team? get5TechPauseTeam;
    private int get5TechPauseElapsed = -1;
    private CounterStrikeSharp.API.Modules.Timers.Timer? get5TechPauseTimer;

    [ConsoleCommand("get5_max_tech_pauses", "Technical pauses a team can call per map, Get5 rules (matchzy_tech_pause_mode 1). 0 = unlimited.")]
    public void OnGet5MaxTechPausesCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null)
            return;
        if (int.TryParse(command.ArgByIndex(1).Trim('"'), out int value))
            SetGet5MaxTechPauses(value);
    }

    [ConsoleCommand("get5_tech_pause_time", "Seconds a technical pause lasts before either team can unpause it, Get5 rules (matchzy_tech_pause_mode 1). 0 = both teams always have to.")]
    public void OnGet5TechPauseTimeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null)
            return;
        if (int.TryParse(command.ArgByIndex(1).Trim('"'), out int value))
            SetGet5TechPauseTime(value);
    }

    private void SetGet5MaxTechPauses(int value)
    {
        maxTechPausesAllowed.Value = Math.Max(0, value);
        techPauseMode.Value = 1;
    }

    private void SetGet5TechPauseTime(int value)
    {
        techPauseDuration.Value = Math.Max(0, value);
        techPauseMode.Value = 1;
    }

    // Called by TechPause once the pause is called (not yet in effect).
    private void StartGet5TechPause(Team team)
    {
        StopGet5TechPause();
        get5TechPauseTeam = team;
        get5TechPauseElapsed = -1;
        get5TechPauseTimer = AddTimer(1.0f, Get5TechPauseTick, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    // Any unpause ends it (called from CancelTechPauseTimer).
    private void StopGet5TechPause()
    {
        get5TechPauseTimer?.Kill();
        get5TechPauseTimer = null;
        get5TechPauseTeam = null;
        get5TechPauseElapsed = -1;
    }

    private bool IsGet5TechPauseActive() =>
        get5TechPauseTeam != null && isPaused
        && unpauseData.TryGetValue("pauseTeam", out var current) && current is string name && name == get5TechPauseTeam.teamName;

    private void Get5TechPauseTick()
    {
        try
        {
            if (!IsGet5TechPauseActive())
            {
                StopGet5TechPause();
                return;
            }
            Team team = get5TechPauseTeam!;
            bool inFreezeTime = GetGameRules()?.FreezePeriod == true;
            if (inFreezeTime)
            {
                get5TechPauseElapsed++;
                // It counts as used once it is in effect.
                if (get5TechPauseElapsed == 0)
                    technicalPauseUsed[team] = technicalPauseUsed.GetValueOrDefault(team) + 1;
                int limit = techPauseDuration.Value;
                if (limit > 0 && get5TechPauseElapsed == limit)
                    PrintLocalizedToAll("matchzy.pause.anyonecanunpause");
            }

            foreach (var p in playerData.Values)
            {
                if (p == null || !p.IsValid || p.IsBot)
                    continue;
                p.PrintToCenter(Get5TechPauseHint(p, team.teamName));
            }
        }
        catch (Exception e)
        {
            Log($"[Get5TechPauseTick] {e.Message}");
        }
    }

    private string Get5TechPauseHint(CCSPlayerController player, string teamName)
    {
        if (get5TechPauseElapsed < 0)
            return Localizer.ForPlayer(player, "matchzy.pause.techhintpending", teamName);
        int limit = techPauseDuration.Value;
        if (limit <= 0)
            return Localizer.ForPlayer(player, "matchzy.pause.techhintawaiting", teamName);
        int left = limit - get5TechPauseElapsed;
        return left > 0
            ? Localizer.ForPlayer(player, "matchzy.pause.techhinttime", teamName, FormatReadyTime(left))
            : Localizer.ForPlayer(player, "matchzy.pause.techhintanyone", teamName);
    }

    // .unpause during a Get5-mode tech pause, before the both-teams rule. True when handled.
    private bool HandleGet5TechPauseUnpause(CCSPlayerController player)
    {
        if (!Get5TechPauseMode || !IsGet5TechPauseActive())
            return false;
        Team? playerTeam = player.TeamNum == 3 && reverseTeamSides.TryGetValue("CT", out var ct) ? ct
            : player.TeamNum == 2 && reverseTeamSides.TryGetValue("TERRORIST", out var t) ? t
            : null;
        if (playerTeam == null)
            return false;
        Team pausingTeam = get5TechPauseTeam!;

        if (playerTeam == pausingTeam && get5TechPauseElapsed < 0)
        {
            // Not in effect yet: the pausing team cancels it, which does not use it up.
            PrintLocalizedToAll("matchzy.pause.pauserequestcanceled", pausingTeam.teamName);
            UnpauseMatch();
            unpauseData["pauseTeam"] = "";
            return true;
        }
        int limit = techPauseDuration.Value;
        if (limit > 0 && get5TechPauseElapsed >= limit)
        {
            PrintLocalizedToAll("matchzy.pause.unpausedby", playerTeam.teamName);
            UnpauseMatch();
            unpauseData["pauseTeam"] = "";
            return true;
        }
        return false;
    }
}
