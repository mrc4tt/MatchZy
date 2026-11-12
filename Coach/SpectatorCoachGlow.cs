using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

// Teammate outlines for a spectator coach (matchzy_coaching_mode 3), like a dead player of the team
// sees them. The client's own spectator x-ray is switched off for the coach (game_mode 0, see
// ApplySpectatorCoachXrayBlock) because it would outline the enemy too, so the outlines are built
// on the server instead: every living player of a side that has a spectator coach gets a glowing
// copy of its model (same setup as practice .xray: an invisible prop_dynamic follows the pawn and
// the glowing prop follows that one). The CheckTransmit listener sends those props only to the
// coaches of that side, so no other client (players, the other coach, CSTV) ever receives them.
// Enemies never get a copy, so nothing can leak even if a filter pass fails.
public partial class MatchZy
{
    private sealed class SpecCoachGlow
    {
        public uint PawnIndex;
        public byte Team;
        public uint RelayRaw;
        public uint GlowRaw;
    }

    // Pawn entity index -> its glow props.
    private readonly Dictionary<uint, SpecCoachGlow> _specCoachGlows = new();

    private static readonly Color SpecCoachGlowT = Color.FromArgb(255, 255, 165, 0);
    private static readonly Color SpecCoachGlowCT = Color.FromArgb(255, 80, 160, 255);

    /// <summary>
    /// 0.1 s pass: adds glows for living players of coached sides, removes the ones whose pawn died,
    /// changed side or whose side no longer has a coach. Props wiped by a round restart are re-made.
    /// Only call after the CheckTransmit listener is registered, so a new prop is never sent to
    /// everyone for a tick.
    /// </summary>
    private void UpdateSpectatorCoachGlows(bool enabled)
    {
        bool coachT = false, coachCT = false;
        if (enabled && _specCoachTransmitHandler != null)
        {
            foreach (var (coach, state) in _spectatorCoaches)
            {
                if (!state.ReachedSpectator || coach == null || !coach.IsValid)
                    continue;
                CsTeam side = GetCoachTeam(coach);
                if (side == CsTeam.Terrorist) coachT = true;
                else if (side == CsTeam.CounterTerrorist) coachCT = true;
            }
        }

        bool used = coachT || coachCT;
        if (_glowUsersCoach != used)
        {
            _glowUsersCoach = used;
            // Just quieted: the log_flags command runs later in the frame, so make the props on
            // the next 0.1 s pass.
            if (RefreshGlowAssertQuiet())
                return;
        }

        HashSet<uint> wanted = new();
        if (coachT || coachCT)
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (p == null || !p.IsValid || !p.PawnIsAlive || p.IsHLTV)
                    continue;
                bool isT = p.TeamNum == (byte)CsTeam.Terrorist, isCT = p.TeamNum == (byte)CsTeam.CounterTerrorist;
                if (!(isT && coachT) && !(isCT && coachCT))
                    continue;
                var pawn = p.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid)
                    continue;
                wanted.Add(pawn.Index);
                if (_specCoachGlows.TryGetValue(pawn.Index, out var g))
                {
                    if (g.Team == p.TeamNum && SpecCoachGlowAlive(g))
                        continue;
                    KillSpecCoachGlow(g);
                    _specCoachGlows.Remove(pawn.Index);
                }
                var made = CreateSpecCoachGlow(pawn, p.TeamNum);
                if (made != null)
                    _specCoachGlows[pawn.Index] = made;
            }
        }

        if (_specCoachGlows.Count == 0)
            return;
        foreach (uint idx in _specCoachGlows.Keys.ToList())
        {
            if (wanted.Contains(idx))
                continue;
            KillSpecCoachGlow(_specCoachGlows[idx]);
            _specCoachGlows.Remove(idx);
        }
    }

    private static bool SpecCoachGlowAlive(SpecCoachGlow g)
    {
        var glow = new CHandle<CBaseModelEntity>(g.GlowRaw).Value;
        var relay = new CHandle<CBaseModelEntity>(g.RelayRaw).Value;
        return glow != null && glow.IsValid && relay != null && relay.IsValid;
    }

    private SpecCoachGlow? CreateSpecCoachGlow(CCSPlayerPawn pawn, byte team)
    {
        try
        {
            string? model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
            if (string.IsNullOrEmpty(model))
                return null;
            var pair = CreateGlowPair(pawn, model, team == (byte)CsTeam.Terrorist ? SpecCoachGlowT : SpecCoachGlowCT);
            if (pair == null)
                return null;
            return new SpecCoachGlow { PawnIndex = pawn.Index, Team = team, RelayRaw = pair.Value.Relay, GlowRaw = pair.Value.Glow };
        }
        catch (Exception e)
        {
            LogSpectatorCoachError("glow", e);
            return null;
        }
    }

    private static void KillSpecCoachGlow(SpecCoachGlow g)
    {
        var glow = new CHandle<CBaseModelEntity>(g.GlowRaw).Value;
        if (glow != null && glow.IsValid)
            glow.AcceptInput("Kill");
        var relay = new CHandle<CBaseModelEntity>(g.RelayRaw).Value;
        if (relay != null && relay.IsValid)
            relay.AcceptInput("Kill");
    }

    private void RemoveAllSpectatorCoachGlows()
    {
        foreach (var g in _specCoachGlows.Values)
            KillSpecCoachGlow(g);
        _specCoachGlows.Clear();
    }

    /// <summary>
    /// CheckTransmit part: a glow prop reaches only a coach of the glowing player's side (forced in
    /// with its pawn, so far teammates show too), and never the coach looking through that player's
    /// eyes (the copy would wrap the first-person view).
    /// <paramref name="coachSide"/> is None for every receiver that is not a spectator coach.
    /// </summary>
    private void FilterSpectatorCoachGlows(CCheckTransmitInfo info, CsTeam coachSide, uint observed)
    {
        if (_specCoachGlows.Count == 0)
            return;
        var transmit = info.TransmitEntities;
        foreach (var g in _specCoachGlows.Values)
        {
            var glow = new CHandle<CBaseModelEntity>(g.GlowRaw).Value;
            if (coachSide != CsTeam.None && g.Team == (byte)coachSide && g.PawnIndex != observed)
            {
                // Coach of this side: force the teammate's pawn and both props in, also when they
                // are outside the PVS of the watched teammate, so far teammates are outlined too.
                // The whole parent chain (glow -> relay -> pawn) goes in together, and only
                // entities resolved valid right now (adding a freed index is the WriteEnterPVS
                // crash class).
                var relayIn = new CHandle<CBaseModelEntity>(g.RelayRaw).Value;
                var pawnIn = Utilities.GetEntityFromIndex<CCSPlayerPawn>((int)g.PawnIndex);
                if (glow != null && glow.IsValid && relayIn != null && relayIn.IsValid
                    && pawnIn != null && pawnIn.IsValid && pawnIn.LifeState == (byte)LifeState_t.LIFE_ALIVE
                    && pawnIn.TeamNum == g.Team)
                {
                    transmit.Add(pawnIn);
                    transmit.Add(relayIn);
                    transmit.Add(glow);
                }
                continue;
            }
            if (glow != null && glow.IsValid)
                transmit.Remove(glow);
            var relay = new CHandle<CBaseModelEntity>(g.RelayRaw).Value;
            if (relay != null && relay.IsValid)
                transmit.Remove(relay);
        }
    }
}
