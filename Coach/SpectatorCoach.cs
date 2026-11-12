using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

// EXPERIMENTAL probe: matchzy_coaching_mode 3 ("spectator coach").
//
// A mode-3 coach has no body. Instead of being a 6th T/CT team member (which takes a spawn point,
// sits on the team scoreboard and can be handed the C4), the coach is moved to the Spectator team
// and tagged as the coach of its team through the engine's own coach field
// (CCSPlayerController.m_iCoachingTeam, honoured while sv_coaching_enabled is 1). The coach sets
// matchzyTeam1/2.coach stay the source of truth (GetAllCoaches, IsMatchCoach, ready counts, stats
// exclusions keep working); _spectatorCoaches is only the mode-3 marker on top of them.
//
// A plain spectator receives every entity, so the coach could watch the enemy team (ghosting).
// A CheckTransmit listener strips, per coach, every unspotted living enemy pawn and the weapons it
// carries from that coach's transmit set. Only removals, never additions, and never the coach's own
// observer target (the client dereferences its observer target every frame; an untransmitted target
// is a client crash). A 0.1 s pass on OnTick keeps the engine coach field, sv_coaching_enabled and
// the camera (locked to a living player of the coached team) in line.
//
// TODO (out of probe scope): voice and team-chat routing between the spectator coach and its team.
// The engine may already route it from m_iCoachingTeam; to be checked live before building anything.
public partial class MatchZy
{
    private sealed class SpectatorCoachState
    {
        // Set once the controller is really on the Spectator team. Before that the move (suicide ->
        // next frame ChangeTeam) is still in flight and the coach must not be treated as one.
        public bool ReachedSpectator;
        public long StartedAtMs;
        public int AppliedCoachingTeam = -1;
        // Pawn index of the last valid (own team) camera target: a switch that lands on an enemy is
        // moved on to the NEXT teammate after this one, so cycling still walks through the team.
        public uint LastTargetIndex;
        // m_iCoachingTeam value last written in a log line (the write itself is repeated at every
        // round start, the log is not).
        public int LoggedCoachingTeam = -1;
        // Entity index of the observer pawn whose m_iTeamNum we set to the coached team (0 = none).
        public uint TeamedPawnIndex;
        // Times m_iCoachingTeam had to be written again because it no longer held our value
        // (something else reset it). Shown by css_coachdiag.
        public int Rewrites;
        // Enemy pawn index -> time (Environment.TickCount64) until which that enemy is still sent
        // to this coach although the coached team has not spotted it: set by the line-of-sight
        // check from the watched teammate's eyes, and kept a moment after any sighting so a player
        // does not blink in and out between the engine's spotting updates. Written on the 0.1 s
        // pass, read in CheckTransmit (same thread).
        public readonly Dictionary<uint, long> EnemyVisibleUntil = new();
        // game_mode value last sent to this coach's client only (null = nothing sent, the client has
        // the server's own value). See ApplySpectatorCoachXrayBlock.
        public string? SentGameMode;
    }

    private readonly Dictionary<CCSPlayerController, SpectatorCoachState> _spectatorCoaches = new();
    private Listeners.CheckTransmit? _specCoachTransmitHandler;
    // Previous sv_coaching_enabled value while a mode-3 coach exists; null = not touched by us.
    // "" = the cvar was not found (logged once), so there is nothing to restore.
    private string? _specCoachSavedCoachingCvar;
    private int _specCoachTickCounter;
    private long _specCoachLastErrorLogMs;
    private long _specCoachLastCameraLogMs;
    // Entity indices of weapon_c4, refreshed on the 0.1 s pass so CheckTransmit never has to walk the
    // entity list. Each index is re-resolved and re-checked on every use (the entity can be gone).
    private readonly List<uint> _specCoachC4Indices = new();
    private readonly HashSet<uint> _specCoachHiddenPawns = new();
    // Server side coach radar filter active (sv_coaching_enabled 1 and mp_forcecamera != 0), set on
    // the 0.1 s pass; read by the spotted-entity message hook.
    private bool _specCoachServerRadarOk;
    private const long SpectatorCoachMoveTimeoutMs = 5000;

    public bool IsSpectatorCoach(CCSPlayerController? player) => player != null && _spectatorCoaches.ContainsKey(player);

    /// <summary>
    /// Coaches that have a body on T/CT (modes 1/2). Everything that places, kills, re-sides or
    /// re-seats a coach body, or hands a coach the C4, must use this instead of GetAllCoaches: a
    /// mode-3 coach is a spectator with no pawn of its own.
    /// </summary>
    public HashSet<CCSPlayerController> GetBodyCoaches()
    {
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (_spectatorCoaches.Count > 0)
            coaches.RemoveWhere(IsSpectatorCoach);
        return coaches;
    }

    // Called once from Load. The tick handler returns immediately while no mode-3 coach exists.
    private void RegisterSpectatorCoachListeners()
    {
        RegisterListener<Listeners.OnTick>(SpectatorCoachTick);
        RegisterListener<Listeners.OnServerPostEntityThink>(SpectatorCoachCameraPostThink);
        // New map: the previous map's glow props are gone with it, so only forget them (no Kill).
        RegisterListener<Listeners.OnMapStart>(_ => _specCoachGlows.Clear());
        // Radar positions of players outside the coach's view come in CCSUsrMsg_
        // ProcessSpottedEntityUpdate, sent to each client every 0.5-1 s (RE of build 14196). A
        // spectator coach is taken off its recipients, so the radar can only show players the
        // coach's own client has as entities (the observed teammate's surroundings).
        try
        {
            int umId = 325;
            try { int found = UserMessage.FindIdByName("CS_UM_ProcessSpottedEntityUpdate"); if (found > 0) umId = found; } catch { }
            HookUserMessage(umId, um =>
            {
                if (_spectatorCoaches.Count == 0)
                    return HookResult.Continue;
                try
                {
                    // While the server holds the coach state (sv_coaching_enabled 1, mp_forcecamera
                    // not 0, m_iCoachingTeam set) the server's radar filter already builds this
                    // message for a coach as CS:GO did: own team everywhere + spotted enemies (RE of
                    // build 14196, filter 0xad7dc0). The coach keeps it then, so far teammates stay
                    // on its radar. Only without that state (cvar parked for the round restart, a cfg
                    // set mp_forcecamera 0) would it carry every player, and the coach is taken off.
                    foreach (var (coach, state) in _spectatorCoaches)
                        if (state.ReachedSpectator && coach != null && coach.IsValid
                            && !(_specCoachServerRadarOk && (coach.CoachingTeam == 2 || coach.CoachingTeam == 3)))
                            um.Recipients.Remove(coach);
                }
                catch { }
                return HookResult.Continue;
            }, HookMode.Pre);
        }
        catch (Exception e)
        {
            Log($"[SpectatorCoach] radar message hook failed: {e.Message}");
        }
        // The coach stays in first person on a teammate: the client's camera-mode switch (spec_mode,
        // bound to space / jump) is refused, so the view never goes to the third-person chase cam
        // that flies to the next player. spec_next / spec_prev (mouse) still switch teammates.
        AddCommandListener("spec_mode", (player, info) =>
            player != null && player.IsValid && IsSpectatorCoach(player) ? HookResult.Handled : HookResult.Continue, HookMode.Pre);
        RegisterEventHandler<EventRoundPrestart>(OnCoachingCvarRoundPrestart, HookMode.Pre);
        RegisterEventHandler<EventRoundPoststart>(OnCoachingCvarRoundPoststart);
        RegisterEventHandler<EventPlayerTeam>(OnSpectatorCoachPlayerTeam);
        AddCommand("css_coachdiag", "Spectator coach state dump (server side)", (caller, info) =>
        {
            if (caller != null && !IsPlayerAdmin(caller, "css_coachdiag", "@css/config"))
                return;
            DumpSpectatorCoachState(caller);
        });
    }

    // CS2 crash with sv_coaching_enabled 1 (RE of build 14196): CCSGameRules round post-start, run
    // by every RestartRound (round start, warmup restart, mp_restartgame), passes the controller
    // of each observer pawn to CCSPlayerController::IsCoach without a NULL check. A player who
    // joined T/CT leaves an observer pawn whose controller handle no longer resolves; IsCoach
    // reads the cvar first and only then dereferences the controller, so with the cvar at 1 the
    // server dies on [NULL + m_iTeamNum]. round_prestart is fired earlier in the same
    // RestartRound, and RestartRound runs inside one frame: turning the cvar off here and back on
    // next frame (and at round_poststart) keeps that one call on the safe cvar-0 path. Only loss:
    // the engine does not force coaches to in-eye at round start.
    private bool _coachingCvarParked;

    private HookResult OnCoachingCvarRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        try
        {
            var cvar = ConVar.Find("sv_coaching_enabled");
            if (cvar == null) return HookResult.Continue;
            string v = GetConvarStringValue(cvar).Trim();
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)) return HookResult.Continue;
            SetConvarValue(cvar, "0");
            _coachingCvarParked = true;
            Server.NextFrame(RestoreParkedCoachingCvar);
        }
        catch (Exception e)
        {
            Log($"[EventRoundPrestart FATAL] coaching cvar: {e.Message}");
        }
        return HookResult.Continue;
    }

    private HookResult OnCoachingCvarRoundPoststart(EventRoundPoststart @event, GameEventInfo info)
    {
        try
        {
            RestoreParkedCoachingCvar();
            // The engine ran its round start (and our sv_coaching_enabled 0 -> 1 toggle): write the
            // coach state again and mark it changed, so every client has it for the new round.
            ForceSpectatorCoachStateResend();
        }
        catch (Exception e) { Log($"[EventRoundPoststart FATAL] coaching cvar: {e.Message}"); }
        return HookResult.Continue;
    }

    private void ForceSpectatorCoachStateResend()
    {
        foreach (var state in _spectatorCoaches.Values)
        {
            state.AppliedCoachingTeam = -1;
            state.SentGameMode = null; // send the x-ray block again for the new round
        }
        if (_spectatorCoaches.Count > 0)
            SpectatorCoachMaintain();
    }

    // CS2 never clears m_iCoachingTeam itself (CS:GO's JoinTeam did): a player who joins T/CT with
    // it still set would turn back into an engine coach on a later move to Spectator.
    private HookResult OnSpectatorCoachPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        try
        {
            if (@event.Team != (int)CsTeam.Terrorist && @event.Team != (int)CsTeam.CounterTerrorist)
                return HookResult.Continue;
            var player = @event.Userid;
            if (player == null || !player.IsValid)
                return HookResult.Continue;
            // A spectator coach who picked a side from the team menu: its old observer pawn must not
            // stay on T/CT until the next maintain pass drops the coach.
            if (_spectatorCoaches.TryGetValue(player, out var coachState))
                RestoreSpectatorCoachPawnTeam(player, coachState);
            if (player.CoachingTeam == 0)
                return HookResult.Continue;
            Server.NextFrame(() =>
            {
                if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected)
                    return;
                if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist)
                    return;
                ClearSpectatorCoachingTeam(player);
            });
        }
        catch (Exception e)
        {
            Log($"[EventPlayerTeam FATAL] coaching team: {e.Message}");
        }
        return HookResult.Continue;
    }

    private void RestoreParkedCoachingCvar()
    {
        if (!_coachingCvarParked) return;
        _coachingCvarParked = false;
        SetConvarValue(ConVar.Find("sv_coaching_enabled"), "1");
    }

    /// <summary>
    /// .coach in mode 3, after HandleCoachCommand passed all permission/roster checks. The player is
    /// on <paramref name="side"/> (the coached team's current side) at this point.
    /// </summary>
    private bool _specCoachBetaLogged;

    private void StartSpectatorCoach(CCSPlayerController player, Team team, CsTeam side)
    {
        if (!_specCoachBetaLogged)
        {
            _specCoachBetaLogged = true;
            Log("[SpectatorCoach] matchzy_coaching_mode 3 (spectator coach) is BETA. Report issues at github.com/mrc4tt/MatchZy/issues.");
        }
        team.coach.Add(player);
        _spectatorCoaches[player] = new SpectatorCoachState { StartedAtMs = Environment.TickCount64 };
        ResetCoachScoreboard(player);
        ApplyClanTag(player, $"[{team.teamName} COACH]");
        if (player.InGameMoneyServices != null)
            player.InGameMoneyServices.Account = 0;
        Log($"[SpectatorCoach] {player.PlayerName} ({player.SteamID}) is now spectator coach of {team.teamName} (side {side}); moving to Spectator.");
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.coach.spectatormode", team.teamName));
        PrintLocalizedToAll("matchzy.coach.announce", player.PlayerName, team.teamName);
        // Command handler -> engine team move: defer out of the chat/command dispatch (hard rule 4).
        Server.NextFrame(() => MoveSpectatorCoachToSpectator(player));
    }

    private void MoveSpectatorCoachToSpectator(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected || !IsSpectatorCoach(player))
            return;
        if (player.Team == CsTeam.Spectator)
            return;
        if (!player.PawnIsAlive)
        {
            MoveToSpectatorWhenDead(player, suicideSent: true);
            return;
        }

        // Never ChangeTeam a live pawn: ChangeTeam strips the weapons of a living pawn and other
        // plugins' weapon hooks re-enter mid-strip (SIGSEGV). Kill first (cash rules suppressed so the
        // enemy is not paid for the suicide), then MoveToSpectatorWhenDead does ChangeTeam on a later
        // frame once the pawn is really dead, re-validating the controller every frame.
        Action killThenMove = () =>
        {
            if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected || !IsSpectatorCoach(player))
                return;
            var pawn = player.PlayerPawn.Value;
            if (player.PawnIsAlive && pawn != null && pawn.IsValid)
            {
                // A body coach made untouchable earlier would ignore CommitSuicide.
                pawn.TakesDamage = true;
                WithCoachSuicideCashSuppressed(() => pawn.CommitSuicide(false, true));
            }
            Server.NextFrame(() =>
            {
                if (player != null && player.IsValid && IsSpectatorCoach(player))
                    MoveToSpectatorWhenDead(player, suicideSent: true);
            });
        };
        // A T carrying the C4 would drop it on death: hand it to a teammate first (killed via entity
        // IO, so the suicide waits a frame for it to be gone).
        if (player.TeamNum == (byte)CsTeam.Terrorist && HandOffCoachBomb(player, "SpectatorCoach"))
            Server.NextFrame(killThenMove);
        else
            killThenMove();
    }

    /// <summary>
    /// .uncoach for a mode-3 coach: drop the coach status and send the player back to the side the
    /// coached team is on, through SwitchPlayerTeam (engine HandleCommand_JoinTeam for a spectator;
    /// we never Respawn a spectator ourselves).
    /// </summary>
    private void EndSpectatorCoach(CCSPlayerController player)
    {
        CsTeam side = GetCoachTeam(player);
        DropSpectatorCoach(player, "uncoach");
        if (player.InGameMoneyServices != null)
            player.InGameMoneyServices.Account = 0;
        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.uncoached"));
        if ((side == CsTeam.Terrorist || side == CsTeam.CounterTerrorist) && player.Team == CsTeam.Spectator)
        {
            Log($"[SpectatorCoach] {player.PlayerName} uncoached; joining {side} via the engine join path.");
            SwitchPlayerTeam(player, side);
        }
    }

    /// <summary>
    /// Match reset/end (.stopmatch/.end, end of series): every spectator coach stops coaching and
    /// joins the side its team is on, like .uncoach. Called from ResetMatch before the coach sets
    /// and team sides are cleared, so the side is still known. The join goes through
    /// SwitchPlayerTeam (next frame, engine HandleCommand_JoinTeam for a spectator).
    /// </summary>
    private void ReturnSpectatorCoachesToTeams(string reason)
    {
        if (_spectatorCoaches.Count == 0)
            return;
        foreach (var coach in _spectatorCoaches.Keys.ToList())
        {
            if (coach == null || !coach.IsValid || coach.Connected != PlayerConnectedState.Connected)
                continue;
            CsTeam side = GetCoachTeam(coach);
            DropSpectatorCoach(coach, reason);
            if ((side == CsTeam.Terrorist || side == CsTeam.CounterTerrorist) && coach.Team == CsTeam.Spectator)
            {
                Log($"[SpectatorCoach] {coach.PlayerName} back to {side} ({reason}).");
                SwitchPlayerTeam(coach, side);
            }
        }
        RemoveAllSpectatorCoachGlows();
    }

    // Removes every trace of mode-3 coaching from a still-valid controller.
    private void DropSpectatorCoach(CCSPlayerController player, string reason)
    {
        matchzyTeam1.coach.Remove(player);
        matchzyTeam2.coach.Remove(player);
        if (_spectatorCoaches.TryGetValue(player, out var dropped))
        {
            RestoreSpectatorCoachPawnTeam(player, dropped);
            RestoreSpectatorCoachXrayBlock(player, dropped);
        }
        _spectatorCoaches.Remove(player);
        ClearSpectatorCoachingTeam(player);
        ApplyClanTag(player, "");
        Log($"[SpectatorCoach] {player.PlayerName} is no longer a spectator coach ({reason}).");
    }

    private void ClearSpectatorCoachingTeam(CCSPlayerController player)
    {
        try
        {
            if (player.IsValid && player.CoachingTeam != 0)
            {
                player.CoachingTeam = 0;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCoachingTeam");
                Log($"[SpectatorCoach] {player.PlayerName}: m_iCoachingTeam -> 0");
            }
        }
        catch (Exception e)
        {
            Log($"[SpectatorCoach] clearing m_iCoachingTeam failed: {e.Message}");
        }
    }

    private void SpectatorCoachTick()
    {
        if (_spectatorCoaches.Count == 0 && _specCoachTransmitHandler == null && _specCoachSavedCoachingCvar == null)
            return;
        // ~0.1 s at 64 tick: the engine-field upkeep does not need every tick.
        if (++_specCoachTickCounter % 6 != 0)
            return;
        try
        {
            SpectatorCoachMaintain();
        }
        catch (Exception e)
        {
            LogSpectatorCoachError("tick", e);
        }
    }

    // Camera lock after the entities thought: the coach's switch (a user command, processed in the
    // same frame) has been applied by then, so a target that landed on an enemy is moved to the
    // next teammate before the snapshot is sent, and the client never renders the enemy.
    private void SpectatorCoachCameraPostThink()
    {
        if (_spectatorCoaches.Count == 0)
            return;
        try
        {
            foreach (var (coach, state) in _spectatorCoaches)
                if (state.ReachedSpectator && coach != null && coach.IsValid)
                    LockSpectatorCoachCamera(coach, GetCoachTeam(coach), state);
        }
        catch (Exception e)
        {
            LogSpectatorCoachError("camera", e);
        }
    }

    private void SpectatorCoachMaintain()
    {
        long now = Environment.TickCount64;
        // The engine's coach radar (own team + spotted enemies) is off while mp_forcecamera is 0
        // (client and server then show every player to observers). A cfg run later (warmup, a
        // match config) can set it back to 0, so keep it at 1 while a spectator coach exists.
        if (_spectatorCoaches.Count > 0)
        {
            var forceCam = ConVar.Find("mp_forcecamera");
            if (forceCam != null && GetConvarStringValue(forceCam).Trim() == "0")
            {
                SetReplicatedCvar("mp_forcecamera", "1");
                Log("[SpectatorCoach] mp_forcecamera 0 -> 1 (coach radar would show every player)");
            }
        }
        foreach (var (coach, state) in _spectatorCoaches.ToList())
        {
            if (coach == null || !coach.IsValid || coach.Connected != PlayerConnectedState.Connected)
            {
                // Disconnect: the disconnect handler already removed the coach-set entry.
                RestoreSpectatorCoachPawnTeam(null, state);
                _spectatorCoaches.Remove(coach!);
                continue;
            }
            if (!matchzyTeam1.coach.Contains(coach) && !matchzyTeam2.coach.Contains(coach))
            {
                // The coach sets were cleared elsewhere (match reset/end): follow them.
                RestoreSpectatorCoachPawnTeam(coach, state);
                _spectatorCoaches.Remove(coach);
                ClearSpectatorCoachingTeam(coach);
                Log($"[SpectatorCoach] {coach.PlayerName} left the coach sets (match reset/end); marker cleared.");
                continue;
            }

            if (coach.Team == CsTeam.Spectator)
            {
                if (!state.ReachedSpectator)
                    Log($"[SpectatorCoach] {coach.PlayerName} is on Spectator now.");
                state.ReachedSpectator = true;
            }
            else if (state.ReachedSpectator || now - state.StartedAtMs > SpectatorCoachMoveTimeoutMs)
            {
                // Joined a side on their own (team menu) or the move never landed: a mode-3 coach on
                // T/CT has no body handling at all, so stop treating them as a coach.
                Log($"[SpectatorCoach] {coach.PlayerName} is on {coach.Team}, not Spectator (reached before: {state.ReachedSpectator}); dropping coach status.");
                DropSpectatorCoach(coach, "not on Spectator");
                PrintToPlayerChat(coach, Localizer.ForPlayer(coach, "matchzy.coach.spectatorleft"));
                continue;
            }
            else
            {
                continue; // move still in flight
            }

            // The coached team keeps its Team object across halftime; its side does not.
            CsTeam side = GetCoachTeam(coach);
            int want = side == CsTeam.Terrorist || side == CsTeam.CounterTerrorist ? (int)side : 0;
            // A plain field write is not networked: without SetStateChanged the client keeps 0 and
            // treats the coach as a plain spectator (enemy x-ray outlines, every player on the radar).
            // Written whenever it differs and once more after every round start.
            if (coach.CoachingTeam != want && state.AppliedCoachingTeam == want)
                state.Rewrites++;
            if (coach.CoachingTeam != want || state.AppliedCoachingTeam != want)
            {
                coach.CoachingTeam = want;
                Utilities.SetStateChanged(coach, "CCSPlayerController", "m_iCoachingTeam");
                state.AppliedCoachingTeam = want;
            }
            if (state.LoggedCoachingTeam != want)
            {
                state.LoggedCoachingTeam = want;
                Log($"[SpectatorCoach] {coach.PlayerName}: m_iCoachingTeam -> {want} ({side})");
            }
            ApplySpectatorCoachPawnTeam(coach, state, want);
            ApplySpectatorCoachXrayBlock(coach, state);

        }

        bool any = _spectatorCoaches.Count > 0;
        SetSpectatorCoachCvar(any);
        {
            var ce = ConVar.Find("sv_coaching_enabled");
            var fc = ConVar.Find("mp_forcecamera");
            _specCoachServerRadarOk = any && !_coachingCvarParked
                && ce != null && GetConvarStringValue(ce).Trim() is "1" or "true"
                && fc != null && GetConvarStringValue(fc).Trim() != "0";
        }
        // Hiding enemy pawns per client (CheckTransmit) is opt-in: with it on, the server ended with
        // "FATAL ERROR: WriteEnterPVS: GetEntServerClass failed" around round starts in testing.
        // Without it the coach is held in first person on a teammate (camera lock below), so the
        // screen shows what that teammate sees, and the engine's coach radar shows own team +
        // spotted enemies only.
        // Glows first: when the last coach leaves they are killed before the listener that keeps them
        // away from everyone else goes. New glows are only made once the listener is registered.
        UpdateSpectatorCoachGlows(any && specCoachTeamXray.Value);
        SetSpectatorCoachTransmit(any && (specCoachHideEnemies.Value || specCoachTeamXray.Value) || _specCoachGlows.Count > 0);
        if (_specCoachTransmitHandler != null && specCoachHideEnemies.Value)
            UpdateSpectatorCoachEnemySight(now);

        _specCoachC4Indices.Clear();
        if (any)
        {
            foreach (var c4 in Utilities.FindAllEntitiesByDesignerName<CBasePlayerWeapon>("weapon_c4"))
            {
                if (c4 != null && c4.IsValid)
                    _specCoachC4Indices.Add(c4.Index);
            }
        }
    }

    private void SetSpectatorCoachCvar(bool on)
    {
        if (on && _specCoachSavedCoachingCvar == null)
        {
            ConVar? cvar = ConVar.Find("sv_coaching_enabled");
            if (cvar == null)
            {
                _specCoachSavedCoachingCvar = "";
                Log("[SpectatorCoach] sv_coaching_enabled not found on this server; m_iCoachingTeam is set without it.");
                return;
            }
            _specCoachSavedCoachingCvar = GetConvarStringValue(cvar);
            SetReplicatedCvar("sv_coaching_enabled", "1");
            Log($"[SpectatorCoach] sv_coaching_enabled {_specCoachSavedCoachingCvar} -> 1 (spectator coach present)");
            // The engine's coach radar (own team + spotted enemies only) also needs
            // mp_forcecamera != 0; with 0 the radar and the server's spotted-entity message show
            // every player to observers (RE of build 14196, radar filter init).
            var forceCam = ConVar.Find("mp_forcecamera");
            if (forceCam != null && GetConvarStringValue(forceCam).Trim() == "0")
            {
                SetReplicatedCvar("mp_forcecamera", "1");
                Log("[SpectatorCoach] mp_forcecamera 0 -> 1 (coach radar would show every player)");
            }
        }
        else if (!on && _specCoachSavedCoachingCvar != null)
        {
            string saved = _specCoachSavedCoachingCvar;
            _specCoachSavedCoachingCvar = null;
            if (saved == "")
                return;
            SetReplicatedCvar("sv_coaching_enabled", saved);
            Log($"[SpectatorCoach] sv_coaching_enabled restored to {saved} (no spectator coach left)");
        }
    }

    // CheckTransmit crosses into managed code every tick for every client: registered only while a
    // mode-3 coach exists.
    private void SetSpectatorCoachTransmit(bool on)
    {
        if (on && _specCoachTransmitHandler == null)
        {
            _specCoachTransmitHandler = OnSpectatorCoachCheckTransmit;
            RegisterListener(_specCoachTransmitHandler);
            Log("[SpectatorCoach] anti-ghost CheckTransmit listener registered");
        }
        else if (!on && _specCoachTransmitHandler != null)
        {
            RemoveListener(_specCoachTransmitHandler);
            _specCoachTransmitHandler = null;
            Log("[SpectatorCoach] anti-ghost CheckTransmit listener removed");
        }
    }

    private void OnSpectatorCoachCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_spectatorCoaches.Count == 0 && _specCoachGlows.Count == 0)
            return;
        try
        {
            List<CCSPlayerController>? players = null;
            foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
            {
                if (receiver == null || !receiver.IsValid)
                {
                    // CSTV and slots without a valid controller: never send coach glow props.
                    FilterSpectatorCoachGlows(info, CsTeam.None, 0);
                    continue;
                }
                if (!_spectatorCoaches.TryGetValue(receiver, out SpectatorCoachState? state) || !state.ReachedSpectator)
                {
                    FilterSpectatorCoachGlows(info, CsTeam.None, 0);
                    continue;
                }
                CsTeam side = GetCoachTeam(receiver);
                if (side != CsTeam.Terrorist && side != CsTeam.CounterTerrorist)
                {
                    FilterSpectatorCoachGlows(info, CsTeam.None, 0);
                    continue;
                }

                players ??= Utilities.GetPlayers();

                // m_bSpottedByMask is a 64-bit set (two uint32 words) indexed by player slot: bit N set
                // = the player in slot N currently spots this entity. "Spotted by the coached team" =
                // any bit of a living coached-team player.
                uint team0 = 0, team1 = 0;
                foreach (var p in players)
                {
                    if (p == null || !p.IsValid || p.TeamNum != (byte)side || !p.PawnIsAlive)
                        continue;
                    int slot = p.Slot;
                    if (slot >= 0 && slot < 32)
                        team0 |= 1u << slot;
                    else if (slot >= 32 && slot < 64)
                        team1 |= 1u << (slot - 32);
                }

                // The entity the coach is looking through is never removed, whatever team it is on
                // (the death/freeze cam can briefly follow the killer before the camera lock fixes it).
                uint observed = 0;
                var obsServices = receiver.ObserverPawn.Value?.ObserverServices;
                var observedEnt = obsServices?.ObserverTarget.Value;
                if (observedEnt != null && observedEnt.IsValid)
                    observed = observedEnt.Index;

                FilterSpectatorCoachGlows(info, side, observed);
                if (!specCoachHideEnemies.Value)
                    continue;

                var transmit = info.TransmitEntities;
                _specCoachHiddenPawns.Clear();
                foreach (var enemy in players)
                {
                    if (enemy == null || !enemy.IsValid || !enemy.PawnIsAlive)
                        continue;
                    if (enemy.TeamNum != (byte)CsTeam.Terrorist && enemy.TeamNum != (byte)CsTeam.CounterTerrorist)
                        continue;
                    if (enemy.TeamNum == (byte)side)
                        continue;
                    var pawn = enemy.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.Index == observed)
                        continue;
                    var mask = pawn.EntitySpottedState.SpottedByMask;
                    bool spotted = (mask.Length > 0 && (mask[0] & team0) != 0) || (mask.Length > 1 && (mask[1] & team1) != 0);
                    if (spotted)
                        continue;
                    // In the watched teammate's sight (or seen a moment ago): the coach is looking
                    // through that teammate's eyes, so the enemy must be on screen.
                    if (state.EnemyVisibleUntil.TryGetValue(pawn.Index, out long visibleUntil) && visibleUntil >= Environment.TickCount64)
                        continue;

                    transmit.Remove(pawn);
                    _specCoachHiddenPawns.Add(pawn.Index);
                    var weapons = pawn.WeaponServices?.MyWeapons;
                    if (weapons == null)
                        continue;
                    foreach (var handle in weapons)
                    {
                        var w = handle.Value;
                        if (w != null && w.IsValid && w.Index != observed)
                            transmit.Remove(w);
                    }
                }

                // The C4 is not always listed in MyWeapons: hide it by owner too. A dropped C4 (no
                // owner) is hidden from a CT coach until the CTs spot it or the watched CT sees it,
                // so it is not on the coach's radar either.
                if (_specCoachHiddenPawns.Count > 0 || side == CsTeam.CounterTerrorist)
                {
                    foreach (uint idx in _specCoachC4Indices)
                    {
                        var c4 = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)idx);
                        if (c4 == null || !c4.IsValid || c4.DesignerName != "weapon_c4" || c4.Index == observed)
                            continue;
                        var owner = c4.OwnerEntity.Value;
                        if (owner != null && owner.IsValid)
                        {
                            if (_specCoachHiddenPawns.Contains(owner.Index))
                                transmit.Remove(c4);
                        }
                        else if (side == CsTeam.CounterTerrorist
                            && !(state.EnemyVisibleUntil.TryGetValue(c4.Index, out long c4Until) && c4Until >= Environment.TickCount64))
                        {
                            transmit.Remove(c4);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            LogSpectatorCoachError("transmit", e);
        }
    }

    /// <summary>
    /// Keeps a mode-3 coach's camera on a living player of the coached team. A plain spectator may
    /// free-roam or pick any player; with the enemy hidden by CheckTransmit that would only show
    /// empty map, and roaming would let the coach scout. Leaves the camera alone when nobody of the
    /// coached team is alive.
    /// </summary>
    private void LockSpectatorCoachCamera(CCSPlayerController coach, CsTeam side, SpectatorCoachState state)
    {
        if (side != CsTeam.Terrorist && side != CsTeam.CounterTerrorist)
            return;
        var obsPawn = coach.ObserverPawn.Value;
        if (obsPawn == null || !obsPawn.IsValid)
            return;
        var obs = obsPawn.ObserverServices;
        if (obs == null)
            return;

        byte mode = obs.ObserverMode;
        // Anything but first person on a teammate (free roam, fixed, third-person chase) could look
        // around corners: the coach is kept in the eyes of a teammate.
        bool freeMode = mode != (byte)ObserverMode_t.OBS_MODE_IN_EYE;

        // Any living player of the coached team is a valid target (the coach may switch between
        // them freely); only an enemy, a dead player or nothing is corrected. Matched by pawn
        // index: the designer-name test ("player") never matched, so every target was "fixed"
        // back to the first teammate.
        bool targetOk = false;
        var targetEnt = obs.ObserverTarget.Value;
        if (targetEnt != null && targetEnt.IsValid)
        {
            uint ti = targetEnt.Index;
            foreach (var p in Utilities.GetPlayers())
            {
                if (p == null || !p.IsValid || p.TeamNum != (byte)side || !p.PawnIsAlive || IsMatchCoach(p))
                    continue;
                var pp = p.PlayerPawn.Value;
                if (pp != null && pp.IsValid && pp.Index == ti)
                {
                    targetOk = true;
                    break;
                }
            }
        }
        if (targetOk)
            state.LastTargetIndex = targetEnt!.Index;
        if (targetOk && !freeMode)
            return;

        // The living teammates in slot order; the next one after the last valid target.
        var mates = new List<CCSPlayerPawn>();
        foreach (var p in Utilities.GetPlayers().OrderBy(p => p.Slot))
        {
            if (p == null || !p.IsValid || p.TeamNum != (byte)side || !p.PawnIsAlive || IsMatchCoach(p))
                continue;
            var pp = p.PlayerPawn.Value;
            if (pp != null && pp.IsValid && pp.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                mates.Add(pp);
        }
        if (mates.Count == 0)
            return;
        CCSPlayerPawn mate = mates[0];
        if (!targetOk)
        {
            int last = mates.FindIndex(m => m.Index == state.LastTargetIndex);
            if (last >= 0)
                mate = mates[(last + 1) % mates.Count];
        }
        else
        {
            mate = mates.Find(m => m.Index == targetEnt!.Index) ?? mates[0];
        }
        state.LastTargetIndex = mate.Index;

        if (!targetOk)
            obs.ObserverTarget.Raw = mate.EntityHandle.Raw;
        if (freeMode)
            obs.ObserverMode = (byte)ObserverMode_t.OBS_MODE_IN_EYE;
        Utilities.SetStateChanged(obsPawn, "CBasePlayerPawn", "m_pObserverServices");

        // Logged only when the camera mode had to be changed (a teammate switch is normal use).
        long now = Environment.TickCount64;
        if (freeMode && now - _specCoachLastCameraLogMs > 10000)
        {
            _specCoachLastCameraLogMs = now;
            Log($"[SpectatorCoach] camera lock {coach.PlayerName}: mode {mode} -> {obs.ObserverMode}");
        }
    }

    /// <summary>
    /// Camera lock, option A: CS2's observer rules (IsValidObserverTarget, SetObserverMode) read the
    /// OBSERVER PAWN's m_iTeamNum, which is 1 for a coach, so mp_forcecamera never limits a coach.
    /// The observer pawn gets the coached team (the controller stays on Spectator, which is what
    /// the coach logic, radar and x-ray read), so the engine treats the coach like a dead teammate:
    /// coached-team targets only, first person. Re-applied on every maintain pass (new observer
    /// pawn, halftime side swap, round start).
    /// </summary>
    private void ApplySpectatorCoachPawnTeam(CCSPlayerController coach, SpectatorCoachState state, int want)
    {
        var obsPawn = coach.ObserverPawn.Value;
        bool enabled = specCoachPawnTeam.Value && want != 0;
        if (obsPawn == null || !obsPawn.IsValid)
            return;
        if (state.TeamedPawnIndex != 0 && state.TeamedPawnIndex != obsPawn.Index)
            RestoreSpectatorCoachPawnTeam(null, state); // the engine gave the coach a new observer pawn
        byte target = enabled ? (byte)want : (byte)CsTeam.Spectator;
        if (obsPawn.TeamNum == target)
        {
            if (enabled) state.TeamedPawnIndex = obsPawn.Index;
            return;
        }
        obsPawn.TeamNum = target;
        Utilities.SetStateChanged(obsPawn, "CBaseEntity", "m_iTeamNum");
        state.TeamedPawnIndex = enabled ? obsPawn.Index : 0;
        Log($"[SpectatorCoach] {coach.PlayerName}: observer pawn #{obsPawn.Index} m_iTeamNum -> {target}");
    }

    // Puts a coach's observer pawn back on the Spectator team. Only touches an entity that is still
    // an observer pawn (the index may have been reused after the pawn was removed).
    private void RestoreSpectatorCoachPawnTeam(CCSPlayerController? coach, SpectatorCoachState state)
    {
        uint idx = state.TeamedPawnIndex;
        state.TeamedPawnIndex = 0;
        if (idx == 0)
            return;
        try
        {
            var pawn = Utilities.GetEntityFromIndex<CCSObserverPawn>((int)idx);
            if (pawn == null || !pawn.IsValid || pawn.DesignerName != "cs_observer_pawn")
                return;
            if (pawn.TeamNum == (byte)CsTeam.Terrorist || pawn.TeamNum == (byte)CsTeam.CounterTerrorist)
            {
                pawn.TeamNum = (byte)CsTeam.Spectator;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iTeamNum");
                Log($"[SpectatorCoach] {(coach != null && coach.IsValid ? coach.PlayerName : "coach")}: observer pawn #{idx} m_iTeamNum -> 1");
            }
        }
        catch (Exception e)
        {
            LogSpectatorCoachError("pawn team restore", e);
        }
    }

    private void RestoreSpectatorCoachPawnTeams()
    {
        foreach (var (coach, state) in _spectatorCoaches)
            RestoreSpectatorCoachPawnTeam(coach, state);
    }

    // ConVar.SetValue (SetConvarValue) writes the server's value in memory only, without the change
    // path that replicates a cvar; the console command goes through it. The sv_coaching_enabled
    // park/restore around round_prestart stays a raw write: it must be server-side and take effect
    // within the same frame.
    //
    // The CLIENT never takes sv_coaching_enabled (RE of build 14196): its copy is registered
    // FCVAR_DEVELOPMENTONLY without FCVAR_DEFENSIVE, so the engine's CNETMsg_SetConVar handler cannot
    // find it ("SetConVar: No such cvar ... skipping"), at signon and at runtime alike, and connect
    // resets it to 0. The coach's client therefore always runs the plain-spectator path (enemy
    // x-ray, full radar) whatever m_iCoachingTeam says; only not transmitting the enemy
    // (matchzy_spectator_coach_hide_enemies) keeps the coach from seeing it. The server value still
    // matters: the server's IsCoach, voice/chat routing and coach radar filter read it.
    //
    // sv_coaching_enabled is FCVAR_DEVELOPMENTONLY in CS2 (flags 0x82002, RE of build 14196): the
    // console refuses it ("Unknown command"), so the command alone never changed or sent it. The
    // dev-only/hidden flags are lifted on the server first (like CS2Fixes' cvar unlock); the value is
    // still replicated, and the client reads its own hidden copy.
    private readonly HashSet<string> _unlockedCvars = new();

    private void SetReplicatedCvar(string name, string value)
    {
        UnlockCvar(name);
        // A set to the value the server already holds is no change, so nothing would be sent: the
        // raw writes (round_prestart park/restore) can leave the server at 1 while clients still
        // have 0. Flip through the other value in the same command line to force a send.
        var current = ConVar.Find(name);
        if (current != null && (value == "0" || value == "1") && GetConvarStringValue(current).Trim() == value)
            Server.ExecuteCommand($"{name} {(value == "1" ? "0" : "1")}; {name} {value}");
        else
            Server.ExecuteCommand($"{name} {value}");
        Server.NextFrame(() =>
        {
            var cv = ConVar.Find(name);
            if (cv != null)
                Log($"[SpectatorCoach] {name} is now {GetConvarStringValue(cv)} (flags 0x{(ulong)cv.Flags:X})");
        });
    }

    private void UnlockCvar(string name)
    {
        if (_unlockedCvars.Contains(name))
            return;
        var cv = ConVar.Find(name);
        if (cv == null)
            return;
        var before = cv.Flags;
        var after = before & ~(ConVarFlags.FCVAR_DEVELOPMENTONLY | ConVarFlags.FCVAR_HIDDEN);
        if (after != before)
        {
            cv.Flags = after;
            Log($"[SpectatorCoach] {name}: flags 0x{(ulong)before:X} -> 0x{(ulong)after:X} (dev-only/hidden lifted so the console can set it)");
        }
        _unlockedCvars.Add(name);
    }

    private void DumpSpectatorCoachState(CCSPlayerController? caller)
    {
        void Out(string line)
        {
            Log($"[CoachDiag] {line}");
            if (caller != null && caller.IsValid)
                caller.PrintToConsole($"[CoachDiag] {line}");
        }
        string Cv(string n) { var c = ConVar.Find(n); return c == null ? "missing" : $"{GetConvarStringValue(c)} (flags 0x{(ulong)c.Flags:X})"; }
        Out($"sv_coaching_enabled {Cv("sv_coaching_enabled")}, mp_forcecamera {Cv("mp_forcecamera")}, coaches {_spectatorCoaches.Count}, cvar parked {_coachingCvarParked}");
        foreach (var (coach, state) in _spectatorCoaches)
        {
            if (coach == null || !coach.IsValid)
            {
                Out("coach entry not valid");
                continue;
            }
            var obsPawn = coach.ObserverPawn.Value;
            var pawn = coach.Pawn.Value;
            var obs = obsPawn?.ObserverServices;
            var target = obs?.ObserverTarget.Value;
            Out($"{coach.PlayerName}: controller team {coach.TeamNum}, m_iCoachingTeam {coach.CoachingTeam}, applied {state.AppliedCoachingTeam}, rewrites {state.Rewrites}, reached {state.ReachedSpectator}");
            Out($"  m_hPawn #{(pawn != null && pawn.IsValid ? pawn.Index : 0)} {pawn?.DesignerName} team {pawn?.TeamNum}; observer pawn #{(obsPawn != null && obsPawn.IsValid ? obsPawn.Index : 0)} team {obsPawn?.TeamNum}, mode {obs?.ObserverMode}, target #{(target != null && target.IsValid ? target.Index : 0)} {target?.DesignerName} team {target?.TeamNum}");
            DumpSpectatorCoachEnemies(coach, state, target, Out);
        }
    }

    // Per enemy, why it is or is not sent to this coach right now (spotted by the team, inside the
    // watched teammate's view cone, clear line from the teammate's eyes, sighting grace left).
    private void DumpSpectatorCoachEnemies(CCSPlayerController coach, SpectatorCoachState state, CBaseEntity? target, Action<string> Out)
    {
        CsTeam side = GetCoachTeam(coach);
        Out($"  hide_enemies {specCoachHideEnemies.Value}, trace api {HasCssTraceApi}, listener {_specCoachTransmitHandler != null}, side {side}");
        var players = Utilities.GetPlayers();
        CCSPlayerPawn? eyePawn = target != null && target.IsValid ? Utilities.GetEntityFromIndex<CCSPlayerPawn>((int)target.Index) : null;
        Vector? eye = null;
        float fx = 0, fy = 0, fz = 0;
        if (eyePawn != null && eyePawn.IsValid && eyePawn.AbsOrigin != null)
        {
            var o = eyePawn.AbsOrigin;
            float ez = eyePawn.ViewOffset.Z;
            eye = new Vector(o.X, o.Y, o.Z + (ez > 1f ? ez : 64f));
            var ang = eyePawn.EyeAngles;
            double yaw = ang.Y * Math.PI / 180.0, pitch = ang.X * Math.PI / 180.0;
            fx = (float)(Math.Cos(pitch) * Math.Cos(yaw)); fy = (float)(Math.Cos(pitch) * Math.Sin(yaw)); fz = (float)-Math.Sin(pitch);
        }
        long now = Environment.TickCount64;
        foreach (var enemy in players)
        {
            if (enemy == null || !enemy.IsValid || !enemy.PawnIsAlive || enemy.TeamNum == (byte)side)
                continue;
            if (enemy.TeamNum != (byte)CsTeam.Terrorist && enemy.TeamNum != (byte)CsTeam.CounterTerrorist)
                continue;
            var ep = enemy.PlayerPawn.Value;
            var eo = ep?.AbsOrigin;
            if (ep == null || !ep.IsValid || eo == null)
                continue;
            bool spotted = IsSpottedBySide(ep, players, side);
            string cone = "-", los = "-";
            if (eye != null && eyePawn != null)
            {
                float dx = eo.X - eye.X, dy = eo.Y - eye.Y, dz = eo.Z + 40f - eye.Z;
                float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                double deg = len < 1f ? 0 : Math.Acos(Math.Clamp((dx * fx + dy * fy + dz * fz) / len, -1f, 1f)) * 180.0 / Math.PI;
                cone = $"{deg:F0}deg{(deg <= SpecCoachSightHalfAngle ? "" : " (outside)")}";
                los = HasCssTraceApi ? (SpecCoachHasLineOfSight(eye, eo, eyePawn) ? "clear" : $"blocked ({SpecCoachTraceDetail(eye, eo, eyePawn)})") : "no trace api";
            }
            long left = state.EnemyVisibleUntil.TryGetValue(ep.Index, out long until) ? Math.Max(0, until - now) : 0;
            bool sent = spotted || left > 0;
            Out($"  enemy {enemy.PlayerName} #{ep.Index}: spotted {spotted}, view {cone}, line {los}, grace {left} ms -> {(sent ? "SENT" : "HIDDEN")}");
        }
    }

    // How long an enemy stays sent to the coach after the last sighting (engine spotting or our own
    // line-of-sight check). Covers the engine's ~0.5 s spotting refresh and a peek that ends.
    private const long SpecCoachSightGraceMs = 2000;
    // Half-angle of the view cone (degrees) in which the watched teammate can see an enemy: CS2's
    // 90 degree vertical FOV is about 106 degrees horizontal on 16:9; the margin covers 4:3 stretch
    // and a quick flick.
    private const float SpecCoachSightHalfAngle = 75f;

    /// <summary>
    /// For every spectator coach: which enemies the watched teammate can see right now. The engine's
    /// spotting (m_bSpottedByMask) lags and needs a clear view of specific body points, so an enemy
    /// on the teammate's screen could be missing from the coach's copy of the game. A trace from the
    /// teammate's eyes to the enemy's head, chest and feet, inside the teammate's view cone, marks the
    /// enemy visible; every sighting (trace or engine spotting) keeps it sent for SpecCoachSightGraceMs.
    /// </summary>
    private void UpdateSpectatorCoachEnemySight(long now)
    {
        if (!HasCssTraceApi)
            return;
        List<CCSPlayerController>? players = null;
        foreach (var (coach, state) in _spectatorCoaches)
        {
            if (!state.ReachedSpectator || coach == null || !coach.IsValid)
                continue;
            CsTeam side = GetCoachTeam(coach);
            if (side != CsTeam.Terrorist && side != CsTeam.CounterTerrorist)
                continue;
            players ??= Utilities.GetPlayers();

            // Drop sightings that ran out.
            if (state.EnemyVisibleUntil.Count > 0)
                foreach (var key in state.EnemyVisibleUntil.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList())
                    state.EnemyVisibleUntil.Remove(key);

            var watched = coach.ObserverPawn.Value?.ObserverServices?.ObserverTarget.Value;
            CCSPlayerPawn? eyePawn = null;
            if (watched != null && watched.IsValid)
                eyePawn = Utilities.GetEntityFromIndex<CCSPlayerPawn>((int)watched.Index);
            if (eyePawn == null || !eyePawn.IsValid || eyePawn.TeamNum != (byte)side || eyePawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                continue;
            var origin = eyePawn.AbsOrigin;
            if (origin == null)
                continue;
            float eyeZ = eyePawn.ViewOffset.Z;
            var eye = new Vector(origin.X, origin.Y, origin.Z + (eyeZ > 1f ? eyeZ : 64f));
            var ang = eyePawn.EyeAngles;
            double yaw = ang.Y * Math.PI / 180.0, pitch = ang.X * Math.PI / 180.0;
            float fx = (float)(Math.Cos(pitch) * Math.Cos(yaw)), fy = (float)(Math.Cos(pitch) * Math.Sin(yaw)), fz = (float)-Math.Sin(pitch);
            float cosLimit = (float)Math.Cos(SpecCoachSightHalfAngle * Math.PI / 180.0);

            foreach (var enemy in players)
            {
                if (enemy == null || !enemy.IsValid || !enemy.PawnIsAlive || enemy.TeamNum == (byte)side)
                    continue;
                if (enemy.TeamNum != (byte)CsTeam.Terrorist && enemy.TeamNum != (byte)CsTeam.CounterTerrorist)
                    continue;
                var pawn = enemy.PlayerPawn.Value;
                var eo = pawn?.AbsOrigin;
                if (pawn == null || !pawn.IsValid || eo == null)
                    continue;

                // Engine spotting by any teammate also refreshes the grace window.
                if (pawn.EntitySpottedState.Spotted && IsSpottedBySide(pawn, players, side))
                {
                    state.EnemyVisibleUntil[pawn.Index] = now + SpecCoachSightGraceMs;
                    continue;
                }

                float dx = eo.X - eye.X, dy = eo.Y - eye.Y, dz = eo.Z + 40f - eye.Z;
                float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 1f || (dx * fx + dy * fy + dz * fz) / len < cosLimit)
                    continue;
                if (SpecCoachHasLineOfSight(eye, eo, eyePawn))
                    state.EnemyVisibleUntil[pawn.Index] = now + SpecCoachSightGraceMs;
            }

            // A dropped C4 is Terrorist information: a CT coach gets it only once the CTs spot it or
            // the watched CT looks at it (same grace window as an enemy).
            if (side == CsTeam.CounterTerrorist)
            {
                foreach (uint idx in _specCoachC4Indices)
                {
                    var c4 = Utilities.GetEntityFromIndex<CC4>((int)idx);
                    if (c4 == null || !c4.IsValid || c4.DesignerName != "weapon_c4")
                        continue;
                    var owner = c4.OwnerEntity.Value;
                    if (owner != null && owner.IsValid)
                        continue; // carried: follows its carrier's visibility
                    var co = c4.AbsOrigin;
                    if (co == null)
                        continue;
                    if (c4.EntitySpottedState.Spotted && IsSpottedByMask(c4.EntitySpottedState.SpottedByMask, players, side))
                    {
                        state.EnemyVisibleUntil[c4.Index] = now + SpecCoachSightGraceMs;
                        continue;
                    }
                    float dx = co.X - eye.X, dy = co.Y - eye.Y, dz = co.Z - eye.Z;
                    float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len < 1f || (dx * fx + dy * fy + dz * fz) / len < cosLimit)
                        continue;
                    if (SpecCoachHasLineOfSight(eye, new Vector(co.X, co.Y, co.Z - 4f), eyePawn))
                        state.EnemyVisibleUntil[c4.Index] = now + SpecCoachSightGraceMs;
                }
            }
        }
    }

    private static bool IsSpottedBySide(CCSPlayerPawn pawn, List<CCSPlayerController> players, CsTeam side)
        => IsSpottedByMask(pawn.EntitySpottedState.SpottedByMask, players, side);

    private static bool IsSpottedByMask(Span<uint> mask, List<CCSPlayerController> players, CsTeam side)
    {
        foreach (var p in players)
        {
            if (p == null || !p.IsValid || p.TeamNum != (byte)side || !p.PawnIsAlive)
                continue;
            int slot = p.Slot;
            if (slot >= 0 && slot < 32 && mask.Length > 0 && (mask[0] & (1u << slot)) != 0)
                return true;
            if (slot >= 32 && slot < 64 && mask.Length > 1 && (mask[1] & (1u << (slot - 32))) != 0)
                return true;
        }
        return false;
    }

    // Head, chest and feet of the enemy; one clear line is enough. Traced against solid world only
    // (Contents.Solid: walls, floors, solid props). Masks.Solid also stopped on player hulls (the
    // watched teammate's own, a teammate or enemy in between) and on windows, so an enemy right in
    // the teammate's view counted as not seen and stayed invisible to the coach (live test). A hit
    // within 24 units of the aim point still counts as seen (a prop the enemy stands against). Smoke
    // does not block the trace; the client still draws the smoke in front of the enemy.
    // Diagnostics only: where the chest trace stopped (distance along the line of the total, whether
    // it started inside something, what it hit).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string SpecCoachTraceDetail(Vector eye, Vector enemyOrigin, CCSPlayerPawn ignore)
    {
#if HAS_CSS_TRACE
        try
        {
            var opts = new TraceOptions { InteractsWith = Contents.Solid };
            var target = new Vector(enemyOrigin.X, enemyOrigin.Y, enemyOrigin.Z + 40f);
            var tr = Trace.TraceEndShape(eye, target, ignore, opts);
            float dx = target.X - eye.X, dy = target.Y - eye.Y, dz = target.Z - eye.Z;
            float total = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            string hit = "?";
            try { var he = tr.HitEntity(); hit = he != null && he.IsValid ? $"{he.DesignerName} #{he.Index}" : "world"; } catch { }
            return $"chest at {tr.Fraction * total:F0}/{total:F0}u, startsolid {tr.IsAllSolid}, contents 0x{(ulong)tr.Contents:X}, hit {hit}";
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
#else
        return "no trace";
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SpecCoachHasLineOfSight(Vector eye, Vector enemyOrigin, CCSPlayerPawn ignore)
    {
#if HAS_CSS_TRACE
        var opts = new TraceOptions { InteractsWith = Contents.Solid };
        foreach (float h in new[] { 64f, 40f, 8f })
        {
            var target = new Vector(enemyOrigin.X, enemyOrigin.Y, enemyOrigin.Z + h);
            var tr = Trace.TraceEndShape(eye, target, ignore, opts);
            if (!tr.DidHit())
                return true;
            var hp = tr.HitPoint;
            float hx = hp.X - target.X, hy = hp.Y - target.Y, hz = hp.Z - target.Z;
            if (hx * hx + hy * hy + hz * hz <= 24f * 24f)
                return true;
        }
#endif
        return false;
    }

    /// <summary>
    /// No x-ray at all for a spectator coach. The CS2 client never accepts sv_coaching_enabled (RE of
    /// build 14196, see SetReplicatedCvar), so it draws outlines as for a plain spectator: through
    /// "spectator tools" on a classic non-casual game (IGameTypes game_type 0, game_mode not 0/5),
    /// every player; otherwise only for players that are not its enemy by team, and with the coach's
    /// client on team 1 every T/CT is an enemy while mp_forcecamera is 1. game_mode is a normal
    /// replicated cvar the client does accept: game_mode 0 (casual) sent to the coach's client only
    /// turns spectator tools off, so no player gets an outline. The server and every other client keep
    /// the real game mode; only the coach's HUD believes it is casual. Sent again every round start
    /// (a connect or map change resets the client to the server's value), restored when coaching ends.
    /// </summary>
    private void ApplySpectatorCoachXrayBlock(CCSPlayerController coach, SpectatorCoachState state)
    {
        if (!specCoachNoXray.Value)
        {
            RestoreSpectatorCoachXrayBlock(coach, state);
            return;
        }
        if (state.SentGameMode == "0")
            return;
        coach.ReplicateConVar("game_mode", "0");
        state.SentGameMode = "0";
    }

    private void RestoreSpectatorCoachXrayBlock(CCSPlayerController coach, SpectatorCoachState state)
    {
        if (state.SentGameMode == null)
            return;
        state.SentGameMode = null;
        try
        {
            if (coach == null || !coach.IsValid || coach.Connected != PlayerConnectedState.Connected)
                return;
            var gm = ConVar.Find("game_mode");
            if (gm == null)
                return;
            string real = GetConvarStringValue(gm).Trim();
            coach.ReplicateConVar("game_mode", real);
        }
        catch (Exception e)
        {
            LogSpectatorCoachError("x-ray block restore", e);
        }
    }

    private void LogSpectatorCoachError(string where, Exception e)
    {
        long now = Environment.TickCount64;
        if (now - _specCoachLastErrorLogMs < 5000)
            return;
        _specCoachLastErrorLogMs = now;
        Log($"[SpectatorCoach] {where} failed: {e.GetType().Name}: {e.Message}");
    }
}
