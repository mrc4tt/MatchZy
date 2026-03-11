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

            PrintToAllChat(
                $"FFW timer started! {ChatColors.Green}{missingTeamName}{ChatColors.Default} has {ChatColors.Green}4{ChatColors.Default} minutes to return!"
            );

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
                            PrintToAllChat(
                                $"{ChatColors.Green}{ffwMissingMatchTeam.teamName}{ChatColors.Default} has {ChatColors.Green}3{ChatColors.Default} minutes left to return!"
                            );
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
                            PrintToAllChat(
                                $"{ChatColors.Green}{ffwMissingMatchTeam.teamName}{ChatColors.Default} has {ChatColors.Green}2{ChatColors.Default} minutes left to return!"
                            );
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
                            PrintToAllChat(
                                $"{ChatColors.Green}{ffwMissingMatchTeam.teamName}{ChatColors.Default} has {ChatColors.Green}1{ChatColors.Default} minute left to return!"
                            );
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
                            PrintToAllChat(
                                $"{ChatColors.Green}{ffwMissingMatchTeam.teamName}{ChatColors.Default} has {ChatColors.Green}30{ChatColors.Default} seconds left to return!"
                            );
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
                    PrintToAllChat(
                        $"{ChatColors.Green}{ffwMissingMatchTeam.teamName}{ChatColors.Default} has returned at the last moment! FFW cancelled."
                    );
                    ffwRequestingTeam = CsTeam.None;
                    ffwMissingTeam = CsTeam.None;
                    ffwRequestingMatchTeam = null;
                    ffwMissingMatchTeam = null;
                    return;
                }

                string winnerName = ffwRequestingMatchTeam.teamName;
                string loserName = ffwMissingMatchTeam.teamName;

                PrintToAllChat(
                    $"{ChatColors.Green}{loserName}{ChatColors.Default} failed to return! {ChatColors.Green}{winnerName}{ChatColors.Default} wins by forfeit!"
                );

                StopFFWMonitoring();

                (int currentT1score, int currentT2score) = GetTeamsScore();

                int t1score,
                    t2score;

                if (ffwRequestingMatchTeam == matchzyTeam1)
                {
                    t1score = Math.Max(currentT1score, 16);
                    t2score = currentT2score;
                    matchzyTeam1.seriesScore++;
                }
                else
                {
                    t1score = currentT1score;
                    t2score = Math.Max(currentT2score, 16);
                    matchzyTeam2.seriesScore++;
                }

                EndSeries(winnerName, 5, t1score, t2score);
            }
            else
            {
                if (ffwMissingMatchTeam != null)
                {
                    PrintToAllChat(
                        $"{ChatColors.Green}{ffwMissingMatchTeam.teamName}{ChatColors.Default} has returned! FFW cancelled."
                    );
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
