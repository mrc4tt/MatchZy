using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // matchzy_ready_uneven_start_delay: a match without listed players and without
    // players_per_team (.matchsetup, "players": "any") where everyone is ready but one side is
    // short (4v5). Nothing tells the server whether the missing player is still on the way, so the
    // ready check waits for even sides. Instead of leaving the short side to type .forceready, a
    // countdown runs and the match then starts as it stands. .unready or a player joining a side
    // stops it: the sides are then even, or somebody is no longer ready.
    public partial class MatchZy
    {
        // RequiredPlayersOnSide then takes each open side at the size it has. Only set for the
        // length of one readiness check, never across a frame.
        private bool openSidesOwnSize;

        // When the countdown ends; null while it does not run.
        private DateTime? unevenStartAt;
        private int unevenStartLastAnnounced = -1;

        // Everyone on both sides is ready and only the uneven sides keep the match from starting.
        private bool UnevenStartPossible(out int ct, out int t)
        {
            ct = 0;
            t = 0;
            if (readyUnevenStartDelay.Value <= 0 || !isMatchSetup || !readyEnabled.Value || IsJoinReadyMode())
                return false;
            if (!readyAvailable || matchStarted || matchStartInProgress || mapChangePending)
                return false;
            if (!reverseTeamSides.TryGetValue("CT", out Team? ctTeam) || !reverseTeamSides.TryGetValue("TERRORIST", out Team? tTeam))
                return false;
            if (ctTeam == null || tTeam == null || !IsOpenSizedTeam(ctTeam) || !IsOpenSizedTeam(tTeam))
                return false;
            (ct, _) = GetTeamPlayerCount((int)CsTeam.CounterTerrorist, false);
            (t, _) = GetTeamPlayerCount((int)CsTeam.Terrorist, false);
            if (!ReadyLogic.UnevenSides(ct, t, forceReadyMaxMissing.Value))
                return false;
            openSidesOwnSize = true;
            try
            {
                return IsTeamsReady() && IsSpectatorsReady();
            }
            finally
            {
                openSidesOwnSize = false;
            }
        }

        // Called every second during the ready phase (SendReadyStatusHintMessage).
        private void HandleUnevenStartCountdown()
        {
            if (!UnevenStartPossible(out int ct, out int t))
            {
                if (unevenStartAt != null)
                {
                    unevenStartAt = null;
                    if (readyAvailable && !matchStarted && !matchStartInProgress && !mapChangePending)
                        PrintLocalizedToAll("matchzy.ready.unevenstartstopped");
                }
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (unevenStartAt == null)
            {
                int delay = readyUnevenStartDelay.Value;
                unevenStartAt = now.AddSeconds(delay);
                unevenStartLastAnnounced = delay;
                PrintLocalizedToAll("matchzy.ready.unevenstartcountdown", ct, t, delay);
                return;
            }
            int left = (int)Math.Ceiling((unevenStartAt.Value - now).TotalSeconds);
            if (left > 0)
            {
                if (left != unevenStartLastAnnounced && (left <= 5 || left % 10 == 0))
                {
                    unevenStartLastAnnounced = left;
                    PrintLocalizedToAll("matchzy.ready.unevenstartin", ct, t, left);
                }
                return;
            }
            unevenStartAt = null;
            Log($"[UnevenStart] Everyone is ready, starting the match {ct}v{t}.");
            openSidesOwnSize = true;
            try
            {
                CheckLiveRequired();
            }
            finally
            {
                openSidesOwnSize = false;
            }
        }
    }
}
