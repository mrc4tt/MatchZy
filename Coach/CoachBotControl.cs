using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;

namespace MatchZy
{
    public partial class MatchZy
    {
        // A coach dies when freeze time ends and then watches the team; observing a bot on that team
        // offered "take control of the bot", so a coach could play the round in the bot's body.
        // m_bCanControlObservedBot is the engine's per-player permission (and the "Press E" hint):
        // keep it off for coaches every tick, because the engine sets it again while they observe.
        // Runs on OnTick and on OnServerPostEntityThink (see Load).
        // bot_controllable before a coach takeover forced it to 0; put back on map start.
        private string? savedBotControllable;

        private void RestoreBotControllable()
        {
            if (savedBotControllable == null)
                return;
            SetConvarValue(ConVar.Find("bot_controllable"), savedBotControllable);
            savedBotControllable = null;
        }

        private void ForbidCoachBotControlTick()
        {
            if (!matchStarted || matchzyTeam1.coach.Count + matchzyTeam2.coach.Count == 0)
                return;
            foreach (var coach in matchzyTeam1.coach.Concat(matchzyTeam2.coach))
            {
                if (coach == null || !coach.IsValid || !coach.CanControlObservedBot)
                    continue;
                coach.CanControlObservedBot = false;
                Utilities.SetStateChanged(coach, "CCSPlayerController", "m_bCanControlObservedBot");
            }
        }

        // Safety net when the engine lets the takeover through anyway: end it at once. The coach's
        // controlled body (the bot's) is killed a frame later (never during the event), with the
        // coach's suicide cash penalty suppressed like the freeze-time kill.
        public HookResult OnCoachBotTakeover(EventBotTakeover @event, GameEventInfo info)
        {
            CCSPlayerController? coach = @event.Userid;
            if (!matchStarted || !IsPlayerValid(coach) || !GetAllCoaches().Contains(coach!))
                return HookResult.Continue;

            Log($"[CoachBotTakeover] Coach {coach!.PlayerName} took control of a bot; ending it.");
            // The per-player flag did not hold: turn bot takeover off for everyone until the map
            // changes (bot_controllable 0), so it cannot happen again this map.
            if (savedBotControllable == null)
            {
                var cvar = ConVar.Find("bot_controllable");
                if (cvar != null)
                {
                    savedBotControllable = GetConvarStringValue(cvar);
                    SetConvarValue(cvar, "0");
                    Log($"[CoachBotTakeover] bot_controllable {savedBotControllable} -> 0 until the map changes.");
                }
            }
            CCSPlayerController coachRef = coach;
            Server.NextFrame(() =>
            {
                if (!IsPlayerValid(coachRef) || !coachRef.ControllingBot)
                    return;
                var body = coachRef.Pawn.Value;
                if (body == null || !body.IsValid || body.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    return;
                WithCoachSuicideCashSuppressed(() =>
                {
                    body.TakesDamage = true;
                    body.CommitSuicide(explode: false, force: true);
                });
                PrintToPlayerChat(coachRef, Localizer.ForPlayer(coachRef, "matchzy.coach.nobotcontrol"));
            });
            return HookResult.Continue;
        }
    }
}
