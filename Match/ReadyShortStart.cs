using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // matchzy_ready_short_start_delay: everyone on T and CT is ready, but the match does not start
    // because there are fewer players than it waits for.
    // - Pug (no match loaded): fewer ready players than matchzy_minimum_ready_required (10), so a
    //   3v3, 4v4 or 4v5 never started without an admin.
    // - Match without listed players and without players_per_team (.matchsetup, "players": "any")
    //   with uneven sides (4v5): the ready check waits for the short side to fill up.
    // Nothing tells the server whether more players are on the way, so it must not start at once.
    // A countdown runs instead and the match then starts as it stands. .unready or a player
    // joining a side stops it.
    public partial class MatchZy
    {
        // RequiredPlayersOnSide then takes each open side at the size it has. Only set for the
        // length of one readiness check, never across a frame.
        private bool openSidesOwnSize;

        // The countdown ran out: CheckLiveRequired starts a pug below matchzy_minimum_ready_required.
        private bool shortStartNow;

        // When the countdown ends; null while it does not run.
        private DateTime? shortStartAt;
        private int shortStartLastAnnounced = -1;

        // Everyone on both sides is ready and only the uneven sides keep the match from starting.
        private bool ShortStartPossible(out int ct, out int t)
        {
            ct = 0;
            t = 0;
            if (readyShortStartDelay.Value <= 0 || !readyEnabled.Value || IsJoinReadyMode())
                return false;
            if (!readyAvailable || matchStarted || matchStartInProgress || mapChangePending)
                return false;
            if (isPractice || isDryRun)
                return false;
            if (!isMatchSetup)
                return PugShortStartPossible(out ct, out t);
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

        // Pug (no match loaded): every player on T and CT is ready, but they are fewer than
        // matchzy_minimum_ready_required (10 by default), so a 3v3, 4v4 or 4v5 never got there.
        // With 0 ("every connected player") or matchzy_ready_per_team the normal check already
        // starts the match, so there is nothing to count down.
        private bool PugShortStartPossible(out int ct, out int t)
        {
            ct = 0;
            t = 0;
            int needed = EffectiveMinimumReadyRequired();
            if (needed <= 0 || EffectiveReadyPerTeam() > 0)
                return false;
            (int gatePlayers, int readyPlayers) = CountReadyGatePlayers();
            if (gatePlayers <= 0 || readyPlayers < gatePlayers || readyPlayers >= needed)
                return false;
            (ct, _) = GetTeamPlayerCount((int)CsTeam.CounterTerrorist, false);
            (t, _) = GetTeamPlayerCount((int)CsTeam.Terrorist, false);
            return ReadyLogic.ShortSides(ct, t, forceReadyMaxMissing.Value);
        }

        // Called every second during the ready phase (SendReadyStatusHintMessage).
        private void HandleShortStartCountdown()
        {
            if (!ShortStartPossible(out int ct, out int t))
            {
                if (shortStartAt != null)
                {
                    shortStartAt = null;
                    if (readyAvailable && !matchStarted && !matchStartInProgress && !mapChangePending)
                        PrintLocalizedToAll("matchzy.ready.shortstartstopped");
                }
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (shortStartAt == null)
            {
                int delay = readyShortStartDelay.Value;
                shortStartAt = now.AddSeconds(delay);
                shortStartLastAnnounced = delay;
                PrintLocalizedToAll("matchzy.ready.shortstartcountdown", ct, t, delay);
                return;
            }
            int left = (int)Math.Ceiling((shortStartAt.Value - now).TotalSeconds);
            if (left > 0)
            {
                if (left != shortStartLastAnnounced && (left <= 5 || left % 10 == 0))
                {
                    shortStartLastAnnounced = left;
                    PrintLocalizedToAll("matchzy.ready.shortstartin", ct, t, left);
                }
                return;
            }
            shortStartAt = null;
            Log($"[ShortStart] Everyone is ready, starting the match {ct}v{t}.");
            openSidesOwnSize = true;
            shortStartNow = true;
            try
            {
                CheckLiveRequired();
            }
            finally
            {
                openSidesOwnSize = false;
                shortStartNow = false;
            }
        }
    }
}
