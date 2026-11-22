using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace MatchZy
{
    // matchzy_ready_use_key: the use key (E) readies a player up during the ready phase, on top of
    // .ready. It only readies: E never un-readies, so opening a door or picking up a weapon after
    // readying up changes nothing.
    public partial class MatchZy
    {
        private void RegisterReadyUseKey()
        {
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnReadyUseKey);
        }

        private bool ReadyUseKeyActive()
        {
            return readyUseKey.Value && readyEnabled.Value && readyAvailable && !matchStarted
                && !isPractice && !isDryRun && !IsJoinReadyMode();
        }

        private void OnReadyUseKey(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if ((pressed & PlayerButtons.Use) == 0 || !ReadyUseKeyActive())
                return;
            try
            {
                if (player == null || !player.IsValid || player.IsBot || player.IsHLTV || !player.UserId.HasValue)
                    return;
                // Spectators and coaches do not ready up, and E picks the entry in an open center menu.
                if (!IsReadyGatePlayer(player) || IsCenterMenuOpen(player))
                    return;
                int userId = player.UserId.Value;
                if (playerReadyStatus.TryGetValue(userId, out bool ready) && ready)
                    return;

                // Out of the button callback (it runs inside the player's movement processing)
                // before the ready-up prints, starts timers and may start the match.
                int slot = player.Slot;
                Server.NextFrame(() =>
                {
                    try
                    {
                        var p = Utilities.GetPlayerFromSlot(slot);
                        if (p == null || !p.IsValid || p.IsBot || !p.UserId.HasValue || p.UserId.Value != userId)
                            return;
                        if (!ReadyUseKeyActive() || !IsReadyGatePlayer(p))
                            return;
                        OnPlayerReady(p, null);
                    }
                    catch (Exception e)
                    {
                        Log($"[OnReadyUseKey FATAL] {e.Message}");
                    }
                });
            }
            catch (Exception e)
            {
                Log($"[OnReadyUseKey FATAL] {e.Message}");
            }
        }
    }
}
