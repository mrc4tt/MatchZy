using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        // CS2 keeps an in-memory round backup (mp_backup_round_auto, on in live.cfg) to "handle
        // reconnecting players": a player who leaves and comes back in the same round gets the cash
        // they had when the round started. Money spent that round is refunded, while what they
        // bought was dropped on the ground when they left - buy, drop for teammates, reconnect, and
        // the money is back. Players reported 4000 turning into 6000 after a reconnect.
        //
        // Guard: remember a live player's money when they leave, and if they are back in the SAME
        // round with more, set it back to what they left with. A reconnect in a later round is left
        // alone (round-end cash is legitimate), and the cap is used once, so money earned after
        // coming back is never touched.
        private readonly Dictionary<ulong, (int Money, int Round)> reconnectMoneyCaps = new();

        // Checks after the reconnect: the engine's restore is not tied to a known event, so look a
        // frame after connect, again a bit later, and when the player is put back on a team.
        private static readonly float[] ReconnectMoneyCheckDelays = { 0.5f, 1.5f, 3.0f };

        private void RegisterReconnectMoneyGuard()
        {
            RegisterEventHandler<EventPlayerDisconnect>(OnReconnectMoneyDisconnect, HookMode.Pre);
            RegisterEventHandler<EventPlayerConnectFull>(OnReconnectMoneyConnectFull, HookMode.Post);
            RegisterEventHandler<EventPlayerTeam>(OnReconnectMoneyTeam, HookMode.Post);
            RegisterEventHandler<EventRoundStart>(OnReconnectMoneyRoundStart, HookMode.Post);
            RegisterListener<Listeners.OnMapStart>(_ => reconnectMoneyCaps.Clear());
        }

        // A .restore sets everyone's money on purpose; no cap from before it may apply after it.
        private void ClearReconnectMoneyCaps() => reconnectMoneyCaps.Clear();

        private int? CurrentRoundForReconnectMoney() => GetGameRules()?.TotalRoundsPlayed;

        private HookResult OnReconnectMoneyDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            try
            {
                if (!isMatchLive || isRoundRestoring)
                    return HookResult.Continue;
                var player = @event.Userid;
                if (player == null || !player.IsValid || player.IsBot || player.IsHLTV || player.SteamID == 0)
                    return HookResult.Continue;
                if (player.TeamNum != (int)CsTeam.Terrorist && player.TeamNum != (int)CsTeam.CounterTerrorist)
                    return HookResult.Continue;
                var money = player.InGameMoneyServices;
                int? round = CurrentRoundForReconnectMoney();
                if (money == null || round == null)
                    return HookResult.Continue;
                reconnectMoneyCaps[player.SteamID] = (money.Account, round.Value);
            }
            catch (Exception e)
            {
                Log($"[EventPlayerDisconnect ReconnectMoney FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private HookResult OnReconnectMoneyConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            try
            {
                var player = @event.Userid;
                if (player == null || !player.IsValid || player.IsBot || player.SteamID == 0)
                    return HookResult.Continue;
                ulong steamId = player.SteamID;
                if (!reconnectMoneyCaps.ContainsKey(steamId))
                    return HookResult.Continue;
                foreach (float delay in ReconnectMoneyCheckDelays)
                    AddTimer(delay, () => ApplyReconnectMoneyCap(steamId), TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception e)
            {
                Log($"[EventPlayerConnectFull ReconnectMoney FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private HookResult OnReconnectMoneyTeam(EventPlayerTeam @event, GameEventInfo info)
        {
            try
            {
                var player = @event.Userid;
                if (player == null || !player.IsValid || player.IsBot || player.SteamID == 0)
                    return HookResult.Continue;
                if (@event.Disconnect)
                    return HookResult.Continue;
                ulong steamId = player.SteamID;
                if (!reconnectMoneyCaps.ContainsKey(steamId))
                    return HookResult.Continue;
                Server.NextFrame(() => ApplyReconnectMoneyCap(steamId));
            }
            catch (Exception e)
            {
                Log($"[EventPlayerTeam ReconnectMoney FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private HookResult OnReconnectMoneyRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            try
            {
                // Caps only hold within the round the player left in.
                reconnectMoneyCaps.Clear();
            }
            catch (Exception e)
            {
                Log($"[EventRoundStart ReconnectMoney FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private void ApplyReconnectMoneyCap(ulong steamId)
        {
            try
            {
                if (!reconnectMoneyCaps.TryGetValue(steamId, out var cap))
                    return;
                if (!isMatchLive || isRoundRestoring || CurrentRoundForReconnectMoney() != cap.Round)
                {
                    reconnectMoneyCaps.Remove(steamId);
                    return;
                }
                var player = Utilities.GetPlayerFromSteamId(steamId);
                if (player == null || !player.IsValid || !player.UserId.HasValue
                    || player.Connected != PlayerConnectedState.Connected)
                    return;
                var money = player.InGameMoneyServices;
                if (money == null || money.Account <= cap.Money)
                    return;
                int restored = money.Account;
                money.Account = cap.Money;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
                // Used once: money earned after coming back stays.
                reconnectMoneyCaps.Remove(steamId);
                Log($"[ReconnectMoney] {player.PlayerName} ({steamId}) reconnected in the round they left and CS2 gave back ${restored}; set to ${cap.Money}, the money they left with.");
            }
            catch (Exception e)
            {
                Log($"[ReconnectMoney] {e.Message}");
            }
        }
    }
}
