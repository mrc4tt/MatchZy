using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        private CounterStrikeSharp.API.Modules.Timers.Timer? ffwTimer = null;
        private CounterStrikeSharp.API.Modules.Timers.Timer? ffwCheckTimer = null;
        private List<CounterStrikeSharp.API.Modules.Timers.Timer> ffwMessageTimers = new();
        private bool ffwActive = false;
        private CsTeam ffwRequestingTeam = CsTeam.None;
        private CsTeam ffwMissingTeam = CsTeam.None;

        private Team? ffwRequestingMatchTeam = null;
        private Team? ffwMissingMatchTeam = null;

        public void CheckForMissingTeams()
        {
            if (!isMatchLive || ffwActive)
                return;

            int ctCount = 0;
            int tCount = 0;

            foreach (var p in playerData.Values)
            {
                if (!IsPlayerValid(p))
                    continue;
                if (p.Team == CsTeam.CounterTerrorist)
                    ctCount++;
                else if (p.Team == CsTeam.Terrorist)
                    tCount++;
            }

            if (ctCount > 0 && tCount == 0)
            {
                StartFFW(CsTeam.CounterTerrorist, CsTeam.Terrorist);
            }
            else if (tCount > 0 && ctCount == 0)
            {
                StartFFW(CsTeam.Terrorist, CsTeam.CounterTerrorist);
            }
        }

        private void StartFFW(CsTeam requestingTeam, CsTeam missingTeam)
        {
            // Если FFW уже активен, не запускаем новый
            if (ffwActive)
                return;
            if (!isMatchLive)
                return;

            ffwActive = true;
            ffwRequestingTeam = requestingTeam;
            ffwMissingTeam = missingTeam;

            // Очищаем старые таймеры сообщений
            ClearFFWMessageTimers();

            if (requestingTeam == CsTeam.CounterTerrorist)
            {
                ffwRequestingMatchTeam = reverseTeamSides["CT"];
                ffwMissingMatchTeam = reverseTeamSides["TERRORIST"];
            }
            else
            {
                ffwRequestingMatchTeam = reverseTeamSides["TERRORIST"];
                ffwMissingMatchTeam = reverseTeamSides["CT"];
            }

            string missingTeamName = ffwMissingMatchTeam!.teamName;

            PrintLocalizedToAll("matchzy.matchmsg.ffwstarted", missingTeamName, 4);

            ffwTimer = AddTimer(
                240.0f,
                () =>
                {
                    if (ffwActive)
                    {
                        EndFFW(true);
                    }
                }
            );

            // Сохраняем все таймеры сообщений
            ffwMessageTimers.Add(
                AddTimer(
                    60.0f,
                    () =>
                    {
                        if (ffwActive && ffwMissingMatchTeam != null)
                        {
                            PrintLocalizedToAll("matchzy.matchmsg.ffwminutesleft", ffwMissingMatchTeam.teamName, 3);
                        }
                    }
                )
            );

            ffwMessageTimers.Add(
                AddTimer(
                    120.0f,
                    () =>
                    {
                        if (ffwActive && ffwMissingMatchTeam != null)
                        {
                            PrintLocalizedToAll("matchzy.matchmsg.ffwminutesleft", ffwMissingMatchTeam.teamName, 2);
                        }
                    }
                )
            );

            ffwMessageTimers.Add(
                AddTimer(
                    180.0f,
                    () =>
                    {
                        if (ffwActive && ffwMissingMatchTeam != null)
                        {
                            PrintLocalizedToAll("matchzy.matchmsg.ffwoneminuteleft", ffwMissingMatchTeam.teamName);
                        }
                    }
                )
            );

            ffwMessageTimers.Add(
                AddTimer(
                    210.0f,
                    () =>
                    {
                        if (ffwActive && ffwMissingMatchTeam != null)
                        {
                            PrintLocalizedToAll("matchzy.matchmsg.ffwsecondsleft", ffwMissingMatchTeam.teamName, 30);
                        }
                    }
                )
            );
        }

        private void ClearFFWMessageTimers()
        {
            foreach (var timer in ffwMessageTimers)
            {
                timer?.Kill();
            }
            ffwMessageTimers.Clear();
        }

        private void EndFFW(bool forfeit)
        {
            ffwTimer?.Kill();
            ffwTimer = null;
            ClearFFWMessageTimers();
            ffwActive = false;

            if (forfeit && ffwRequestingMatchTeam != null && ffwMissingMatchTeam != null)
            {
                // Перепроверяем перед выдачей победы
                bool missingTeamStillEmpty = true;

                foreach (var p in playerData.Values)
                {
                    if (!IsPlayerValid(p))
                        continue;

                    Team? playerMatchTeam = null;
                    if (p.Team == CsTeam.CounterTerrorist)
                        playerMatchTeam = reverseTeamSides["CT"];
                    else if (p.Team == CsTeam.Terrorist)
                        playerMatchTeam = reverseTeamSides["TERRORIST"];

                    if (playerMatchTeam == ffwMissingMatchTeam)
                    {
                        missingTeamStillEmpty = false;
                        break;
                    }
                }

                if (!missingTeamStillEmpty)
                {
                    PrintLocalizedToAll("matchzy.matchmsg.ffwreturnedlast", ffwMissingMatchTeam.teamName);
                    ffwRequestingTeam = CsTeam.None;
                    ffwMissingTeam = CsTeam.None;
                    ffwRequestingMatchTeam = null;
                    ffwMissingMatchTeam = null;
                    return;
                }

                string winnerName = ffwRequestingMatchTeam.teamName;
                string loserName = ffwMissingMatchTeam.teamName;

                PrintLocalizedToAll("matchzy.matchmsg.ffwforfeit", loserName, winnerName);

                StopFFWMonitoring();

                EndSeriesWithWinner(ffwRequestingMatchTeam);
            }
            else
            {
                if (ffwMissingMatchTeam != null)
                {
                    PrintLocalizedToAll("matchzy.matchmsg.ffwreturned", ffwMissingMatchTeam.teamName);
                }
            }

            ffwRequestingTeam = CsTeam.None;
            ffwMissingTeam = CsTeam.None;
            ffwRequestingMatchTeam = null;
            ffwMissingMatchTeam = null;
        }

        public void CheckFFWStatus()
        {
            if (!ffwActive || ffwMissingMatchTeam == null)
                return;

            foreach (var p in playerData.Values)
            {
                if (!IsPlayerValid(p))
                    continue;

                Team? playerMatchTeam = null;
                if (p.Team == CsTeam.CounterTerrorist)
                    playerMatchTeam = reverseTeamSides["CT"];
                else if (p.Team == CsTeam.Terrorist)
                    playerMatchTeam = reverseTeamSides["TERRORIST"];

                if (playerMatchTeam == ffwMissingMatchTeam)
                {
                    EndFFW(false);
                    return;
                }
            }
        }

        private string GetTeamName(CsTeam team)
        {
            if (team == CsTeam.CounterTerrorist)
            {
                if (reverseTeamSides["CT"] == matchzyTeam1)
                {
                    return matchzyTeam1.teamName;
                }
                else
                {
                    return matchzyTeam2.teamName;
                }
            }
            else if (team == CsTeam.Terrorist)
            {
                if (reverseTeamSides["TERRORIST"] == matchzyTeam1)
                {
                    return matchzyTeam1.teamName;
                }
                else
                {
                    return matchzyTeam2.teamName;
                }
            }
            return "Unknown Team";
        }

        public void StartFFWMonitoring()
        {
            if (ffwCheckTimer != null)
                return;

            ffwCheckTimer = AddTimer(
                5.0f,
                () =>
                {
                    if (isMatchLive && !ffwActive)
                    {
                        CheckForMissingTeams();
                    }
                },
                CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT
            );
        }

        public void StopFFWMonitoring()
        {
            ffwCheckTimer?.Kill();
            ffwCheckTimer = null;
            ClearFFWMessageTimers();
            ffwActive = false;
        }
    }
}
