using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

// On-screen box for a technical pause (.tech) or a regular pause (.pause), refreshed every second
// while the pause lasts: who called it, how long is left / who can unpause, technical pauses left
// and which teams have typed .unpause. A tactical timeout (.tac) has its own box (bottom of this
// file): team, time left and timeouts left. matchzy_pause_hud: 0 = off, 1 = small center hint,
// 2 = center HTML box. Works for both matchzy_tech_pause_mode values; mode 1 keeps its timing in
// TechPauseGet5.cs.
public partial class MatchZy
{
    private Team? techPauseHudTeam;
    private string techPauseHudCaller = "";
    private CounterStrikeSharp.API.Modules.Timers.Timer? techPauseHudTimer;
    private bool techPauseHudShown;
    // false: a regular .pause (both teams unpause, no time limit, no tech pause count).
    private bool techPauseHudIsTech = true;

    // Called by TechPause once the pause is called (it takes effect at freeze time).
    private void StartTechPauseHud(Team team, string callerName, bool isTech = true)
    {
        StopTechPauseHud();
        techPauseHudIsTech = isTech;
        techPauseHudTeam = team;
        techPauseHudCaller = callerName;
        techPauseHudTimer = AddTimer(1.0f, TechPauseHudTick, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        // First frame right away instead of after a second.
        Server.NextFrame(TechPauseHudTick);
    }

    // Anti-flash while the HTML box is up: the engine's own "MATCH PAUSED" center text takes the
    // center HTML panel over and the two alternate. Same fix as the ready panel (RenderReadyPanel,
    // technique from Ghost23161/FlashingHtmlHudFix): keep m_bGameRestart synced to
    // (RestartRoundTime < now) every tick. A leaked m_bGameRestart makes the engine ignore
    // mp_restartgame / restores, so it is put back to false as soon as the box goes away
    // (StopTechPauseHud runs before any queued unpause / restore command executes).
    private CCSGameRulesProxy? techPauseHudProxy;
    private bool techPauseAntiFlashActive;

    // OnTick listener.
    private void TechPauseHudAntiFlashTick()
    {
        try
        {
            bool pauseBox = techPauseHudTeam != null && techPauseHudShown && isPaused;
            if ((!pauseBox && !tacHudShown) || pauseHud.Value != 2)
            {
                if (techPauseAntiFlashActive)
                    ResetTechPauseAntiFlash();
                return;
            }
            if (techPauseHudProxy == null || !techPauseHudProxy.IsValid)
                techPauseHudProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            var gr = techPauseHudProxy?.GameRules;
            if (gr == null)
                return;
            // The tactical timeout watch stops on a map change without clearing tacHudShown: only
            // keep the fix up while a timeout is really active.
            if (!pauseBox && !(gr.CTTimeOutActive || gr.TerroristTimeOutActive))
            {
                tacHudShown = false;
                tacHudSide = 0;
                if (techPauseAntiFlashActive)
                    ResetTechPauseAntiFlash();
                return;
            }
            techPauseAntiFlashActive = true;
            bool expectedRestart = gr.RestartRoundTime < Server.CurrentTime;
            if (gr.GameRestart != expectedRestart)
            {
                gr.GameRestart = expectedRestart;
                Utilities.SetStateChanged(techPauseHudProxy!, "CCSGameRulesProxy", "m_pGameRules");
            }
        }
        catch (Exception e)
        {
            Log($"[TechPauseHudAntiFlashTick] {e.Message}");
        }
    }

    private void ResetTechPauseAntiFlash()
    {
        if (!techPauseAntiFlashActive)
            return;
        techPauseAntiFlashActive = false;
        try
        {
            if (techPauseHudProxy == null || !techPauseHudProxy.IsValid)
                techPauseHudProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            var gr = techPauseHudProxy?.GameRules;
            if (gr == null || !gr.GameRestart)
                return;
            gr.GameRestart = false;
            Utilities.SetStateChanged(techPauseHudProxy!, "CCSGameRulesProxy", "m_pGameRules");
        }
        catch (Exception e)
        {
            Log($"[ResetTechPauseAntiFlash] {e.Message}");
        }
    }

    // Any unpause ends it (called from CancelTechPauseTimer).
    private void StopTechPauseHud()
    {
        techPauseHudTimer?.Kill();
        techPauseHudTimer = null;
        techPauseHudTeam = null;
        techPauseHudCaller = "";
        ResetTechPauseAntiFlash();
        if (techPauseHudShown)
        {
            techPauseHudShown = false;
            // Clear the HTML box now instead of letting it linger for its display time.
            if (pauseHud.Value == 2)
            {
                foreach (var p in playerData.Values)
                {
                    if (p != null && p.IsValid && !p.IsBot)
                        p.PrintToCenterHtml(" ", 1);
                }
            }
        }
    }

    private void TechPauseHudTick()
    {
        try
        {
            Team? team = techPauseHudTeam;
            if (team == null || !isPaused
                || !unpauseData.TryGetValue("pauseTeam", out var current) || current is not string name || name != team.teamName)
            {
                if (techPauseHudTimer != null)
                    StopTechPauseHud();
                return;
            }
            int hud = pauseHud.Value;
            if (hud <= 0)
                return;

            foreach (var p in playerData.Values)
            {
                if (p == null || !p.IsValid || p.IsBot || IsCenterMenuOpen(p))
                    continue;
                if (hud == 1)
                    p.PrintToCenter(TechPauseHint(p, team));
                else
                    p.PrintToCenterHtml(TechPauseHtml(p, team), 2);
            }
            techPauseHudShown = true;
        }
        catch (Exception e)
        {
            Log($"[TechPauseHudTick] {e.Message}");
        }
    }

    // True until the pause is in effect (freeze time).
    private bool IsTechPausePending()
    {
        if (Get5TechPauseMode && techPauseHudIsTech)
            return get5TechPauseElapsed < 0;
        return GetGameRules()?.FreezePeriod != true;
    }

    // Mode 0: seconds until the pause ends on its own (counted while it is in effect, see
    // techPauseElapsed), or null when it has no time limit.
    private int? TechPauseSecondsLeft()
    {
        int duration = techPauseDuration.Value;
        if (duration <= 0)
            return null;
        return Math.Max(0, duration - techPauseElapsed);
    }

    private string TechPauseHint(CCSPlayerController player, Team team)
    {
        if (!techPauseHudIsTech)
            return IsTechPausePending()
                ? Localizer.ForPlayer(player, "matchzy.pause.pausehintpending", team.teamName)
                : Localizer.ForPlayer(player, "matchzy.pause.pausehint", team.teamName);
        if (Get5TechPauseMode)
            return Get5TechPauseHint(player, team.teamName);
        if (IsTechPausePending())
            return Localizer.ForPlayer(player, "matchzy.pause.techhintpendingauto", team.teamName);
        return TechPauseSecondsLeft() is int left
            ? Localizer.ForPlayer(player, "matchzy.pause.techhintauto", team.teamName, FormatReadyTime(left))
            : Localizer.ForPlayer(player, "matchzy.pause.techhintawaiting", team.teamName);
    }

    // The status line under the team name: when it starts, when it ends / who can unpause.
    private (string Text, string Color) TechPauseStatus(CCSPlayerController player)
    {
        const string yellow = "#ffcf3f";
        const string green = "#37ff8b";
        const string white = "#ffffff";
        bool pending = IsTechPausePending();
        if (!techPauseHudIsTech)
            return pending
                ? (Localizer.ForPlayer(player, "matchzy.pausehud.pending"), yellow)
                : (Localizer.ForPlayer(player, "matchzy.pausehud.bothteams"), white);
        if (Get5TechPauseMode)
        {
            if (pending)
                return (Localizer.ForPlayer(player, "matchzy.pausehud.pendingcancel"), yellow);
            int limit = techPauseDuration.Value;
            if (limit <= 0)
                return (Localizer.ForPlayer(player, "matchzy.pausehud.bothteams"), white);
            int left = limit - get5TechPauseElapsed;
            return left > 0
                ? (Localizer.ForPlayer(player, "matchzy.pausehud.unpausein", FormatReadyTime(left)), white)
                : (Localizer.ForPlayer(player, "matchzy.pausehud.anyteam"), green);
        }
        if (TechPauseSecondsLeft() is int secondsLeft)
        {
            string ends = Localizer.ForPlayer(player, "matchzy.pausehud.autoend", FormatReadyTime(secondsLeft));
            return pending
                ? ($"{Localizer.ForPlayer(player, "matchzy.pausehud.pending")}. {ends}", yellow)
                : (ends, white);
        }
        return pending
            ? (Localizer.ForPlayer(player, "matchzy.pausehud.pending"), yellow)
            : (Localizer.ForPlayer(player, "matchzy.pausehud.bothteams"), white);
    }

    private string TechPauseHtml(CCSPlayerController player, Team team)
    {
        var sb = new StringBuilder(512);
        sb.Append($"<font class='fontSize-l' color='#ffcf3f'>{PanelSafe(Localizer.ForPlayer(player, techPauseHudIsTech ? "matchzy.pausehud.techtitle" : "matchzy.pausehud.pausetitle"))}</font>");

        sb.Append($"<br><font class='fontSize-m' color='#ffffff'>{PanelSafe(team.teamName)}</font>");
        if (!string.IsNullOrEmpty(techPauseHudCaller))
            // Own line: after the team name it wrapped mid-name in the panel's fixed width.
            sb.Append($"<br><font class='fontSize-sm' color='#9a9a9a'>{PanelSafe(Localizer.ForPlayer(player, "matchzy.pausehud.calledby", techPauseHudCaller))}</font>");

        var (status, color) = TechPauseStatus(player);
        sb.Append($"<br><font class='fontSize-m' color='{color}'>{PanelSafe(status)}</font>");

        int max = maxTechPausesAllowed.Value;
        if (max > 0 && techPauseHudIsTech)
        {
            // Under Get5 rules the pause only counts once it is in effect; show it as used already.
            int used = technicalPauseUsed.GetValueOrDefault(team) + (Get5TechPauseMode && get5TechPauseElapsed < 0 ? 1 : 0);
            sb.Append($"<br><font class='fontSize-sm' color='#9a9a9a'>{PanelSafe(Localizer.ForPlayer(player, "matchzy.pausehud.pausesleft", Math.Max(0, max - used), max))}</font>");
        }

        // Which side has typed .unpause (both are needed unless the status line says otherwise).
        sb.Append($"<br><font class='fontSize-sm' color='#9a9a9a'>{PanelSafe(Localizer.ForPlayer(player, "matchzy.pausehud.unpauseready"))} </font>");
        sb.Append(UnpauseMark("CT", "ct"));
        sb.Append("<font class='fontSize-sm'>&#160;&#160;</font>");
        sb.Append(UnpauseMark("TERRORIST", "t"));
        return sb.ToString();
    }

    // Side tag (CT / T), not the team name: two team names did not fit on one line of the
    // panel's fixed width and wrapped. The team that paused is named above.
    private string UnpauseMark(string side, string unpauseKey)
    {
        string teamName = side == "CT" ? "CT" : "T";
        bool ready = unpauseData.TryGetValue(unpauseKey, out var v) && v is bool b && b;
        return ready
            ? $"<font class='fontSize-sm' color='#37ff8b'>{PanelSafe(teamName)} &#10004;</font>"
            : $"<font class='fontSize-sm' color='#ff3b3b'>{PanelSafe(teamName)} &#10008;</font>";
    }
    // ── Tactical timeout (.tac) ─────────────────────────────────────────────────────────────
    // The game's own timeout, not mp_pause_match (isPaused stays false), so it has its own state.
    // Refreshed by the 1 s watch in MatchExtras.cs (PublishTacticalTimeoutStarted) until neither
    // side's timeout is active.
    private string tacHudTeamName = "";
    private string tacHudCaller = "";
    private int tacHudSide;
    private bool tacHudShown;

    private void StartTacticalTimeoutHud(CCSPlayerController player)
    {
        tacHudTeamName = GetTacTeamName(player);
        tacHudCaller = player.PlayerName;
        tacHudSide = player.TeamNum;
    }

    private void StopTacticalTimeoutHud()
    {
        tacHudSide = 0;
        if (!tacHudShown)
            return;
        tacHudShown = false;
        ResetTechPauseAntiFlash();
        if (pauseHud.Value == 2)
        {
            foreach (var p in playerData.Values)
            {
                if (p != null && p.IsValid && !p.IsBot)
                    p.PrintToCenterHtml(" ", 1);
            }
        }
    }

    private void TacticalTimeoutHudTick(CCSGameRules gameRules)
    {
        int hud = pauseHud.Value;
        // A pause box (.pause / .tech) wins: both would fight over the same center panel.
        if (hud <= 0 || tacHudSide == 0 || techPauseHudTeam != null)
            return;
        bool ct = tacHudSide == 3;
        bool pending = !gameRules.FreezePeriod;
        int left = (int)Math.Ceiling(ct ? gameRules.CTTimeOutRemaining : gameRules.TerroristTimeOutRemaining);
        int timeoutsLeft = ct ? gameRules.CTTimeOuts : gameRules.TerroristTimeOuts;

        foreach (var p in playerData.Values)
        {
            if (p == null || !p.IsValid || p.IsBot || IsCenterMenuOpen(p))
                continue;
            if (hud == 1)
            {
                p.PrintToCenter(pending
                    ? Localizer.ForPlayer(p, "matchzy.pause.tachintpending", tacHudTeamName)
                    : Localizer.ForPlayer(p, "matchzy.pause.tachint", tacHudTeamName, FormatReadyTime(Math.Max(0, left))));
                continue;
            }
            var sb = new StringBuilder(384);
            sb.Append($"<font class='fontSize-l' color='#5fb4ff'>{PanelSafe(Localizer.ForPlayer(p, "matchzy.pausehud.tactitle"))}</font>");
            sb.Append($"<br><font class='fontSize-m' color='#ffffff'>{PanelSafe(tacHudTeamName)}</font>");
            if (!string.IsNullOrEmpty(tacHudCaller))
                sb.Append($"<br><font class='fontSize-sm' color='#9a9a9a'>{PanelSafe(Localizer.ForPlayer(p, "matchzy.pausehud.calledby", tacHudCaller))}</font>");
            if (pending)
                sb.Append($"<br><font class='fontSize-m' color='#ffcf3f'>{PanelSafe(Localizer.ForPlayer(p, "matchzy.pausehud.pending"))}</font>");
            else
                sb.Append($"<br><font class='fontSize-m' color='#ffffff'>{PanelSafe(Localizer.ForPlayer(p, "matchzy.pausehud.tacends", FormatReadyTime(Math.Max(0, left))))}</font>");
            sb.Append($"<br><font class='fontSize-sm' color='#9a9a9a'>{PanelSafe(Localizer.ForPlayer(p, "matchzy.pausehud.tacsleft", Math.Max(0, timeoutsLeft)))}</font>");
            p.PrintToCenterHtml(sb.ToString(), 2);
        }
        tacHudShown = true;
    }
}
