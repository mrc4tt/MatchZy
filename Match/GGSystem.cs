using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        private Dictionary<CsTeam, HashSet<int>> ggVotes = new() { { CsTeam.CounterTerrorist, new HashSet<int>() }, { CsTeam.Terrorist, new HashSet<int>() } };

        private CounterStrikeSharp.API.Modules.Timers.Timer? ggResetTimer = null;

        [ConsoleCommand("css_gg", "Vote to surrender the match")]
        public void OnGGCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerValid(player))
                return;
            if (!isMatchLive)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.ggnotlive"));
                return;
            }

            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.gghalftime"));
                return;
            }

            var playerTeam = player!.Team;
            if (playerTeam != CsTeam.Terrorist && playerTeam != CsTeam.CounterTerrorist)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.ggnoteam"));
                return;
            }

            // Coaches do not play and do not vote.
            if (IsMatchCoach(player))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.ggcoach"));
                return;
            }

            // A surrender ends the whole series. In a BO2/BO3/BO5 that would hand over the remaining
            // maps too, so it is only allowed in a single-map match.
            if (isMatchSetup && matchConfig.NumMaps > 1)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.ggseries"));
                return;
            }

            //
            (int t1score, int t2score) = GetTeamsScore();
            int playerTeamScore = 0;
            int opponentTeamScore = 0;
            string playerTeamName = GetTeamName(playerTeam);

            // Define MatchTeam for the player's team (as in FFWSystem)
            Team? playerMatchTeam = null;
            if (playerTeam == CsTeam.CounterTerrorist)
            {
                playerMatchTeam = reverseTeamSides["CT"];
                if (reverseTeamSides["CT"] == matchzyTeam1)
                {
                    playerTeamScore = t1score;
                    opponentTeamScore = t2score;
                }
                else
                {
                    playerTeamScore = t2score;
                    opponentTeamScore = t1score;
                }
            }
            else if (playerTeam == CsTeam.Terrorist)
            {
                playerMatchTeam = reverseTeamSides["TERRORIST"];
                if (reverseTeamSides["TERRORIST"] == matchzyTeam1)
                {
                    playerTeamScore = t1score;
                    opponentTeamScore = t2score;
                }
                else
                {
                    playerTeamScore = t2score;
                    opponentTeamScore = t1score;
                }
            }

            // Проверяем, что команда проигрывает на 6 или более раундов
            int scoreDifference = opponentTeamScore - playerTeamScore;
            if (scoreDifference < 6)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.ggnotlosing", playerTeamScore, opponentTeamScore));
                return;
            }

            // Подсчитываем общее количество игроков в команде
            var teamPlayers = playerData.Values.Where(p => IsPlayerValid(p) && p.Team == playerTeam).ToList();

            // Добавляем голос игрока
            if (!player.UserId.HasValue)
                return;

            if (ggVotes[playerTeam].Contains(player.UserId.Value))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.ggalreadyvoted"));
                return;
            }

            ggVotes[playerTeam].Add(player.UserId.Value);

            // Sized from the players actually on the team (no coaches), not from min_players_to_ready:
            // a tournament config with min_players_to_ready 1 let a single player surrender.
            int teamSize = GetTeamPlayerCount(playerTeam);
            int votesNeeded = teamSize <= 2 ? Math.Max(1, teamSize) : teamSize - 1;

            int currentVotes = ggVotes[playerTeam].Count;

            PrintLocalizedToAll("matchzy.matchmsg.ggvoted", player.PlayerName, currentVotes, votesNeeded, playerTeamName, playerTeamScore, opponentTeamScore);

            // Проверяем, достаточно ли голосов
            if (currentVotes >= votesNeeded)
            {
                // Финальная проверка счета перед сдачей
                (int finalT1score, int finalT2score) = GetTeamsScore();
                int finalPlayerTeamScore = 0;
                int finalOpponentTeamScore = 0;

                if (playerTeam == CsTeam.CounterTerrorist)
                {
                    if (reverseTeamSides["CT"] == matchzyTeam1)
                    {
                        finalPlayerTeamScore = finalT1score;
                        finalOpponentTeamScore = finalT2score;
                    }
                    else
                    {
                        finalPlayerTeamScore = finalT2score;
                        finalOpponentTeamScore = finalT1score;
                    }
                }
                else if (playerTeam == CsTeam.Terrorist)
                {
                    if (reverseTeamSides["TERRORIST"] == matchzyTeam1)
                    {
                        finalPlayerTeamScore = finalT1score;
                        finalOpponentTeamScore = finalT2score;
                    }
                    else
                    {
                        finalPlayerTeamScore = finalT2score;
                        finalOpponentTeamScore = finalT1score;
                    }
                }

                // Проверяем, что команда все еще проигрывает на 6+ раундов
                int finalScoreDifference = finalOpponentTeamScore - finalPlayerTeamScore;
                if (finalScoreDifference < 6)
                {
                    PrintLocalizedToAll("matchzy.matchmsg.ggcancelled", playerTeamName, finalPlayerTeamScore, finalOpponentTeamScore);
                    ResetGGVotes();
                    return;
                }

                // Команда сдается - определяем победителя
                CsTeam winnerTeam = playerTeam == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
                string winnerTeamName = GetTeamName(winnerTeam);

                PrintLocalizedToAll("matchzy.matchmsg.ggsurrendered", playerTeamName, winnerTeamName);

                Team winnerMatchTeam = playerMatchTeam == matchzyTeam1 ? matchzyTeam2 : matchzyTeam1;
                EndSeriesWithWinner(winnerMatchTeam);
                ResetGGVotes();
            }
            else
            {
                // Устанавливаем таймер для сброса голосов через 60 секунд
                ggResetTimer?.Kill();
                ggResetTimer = AddTimer(
                    60.0f,
                    () =>
                    {
                        if (ggVotes[playerTeam].Count > 0)
                        {
                            PrintLocalizedToAll("matchzy.matchmsg.ggexpired", playerTeamName);
                            ggVotes[playerTeam].Clear();
                        }
                    }
                );
            }
        }

        private void ResetGGVotes()
        {
            ggResetTimer?.Kill();
            ggResetTimer = null;
            ggVotes[CsTeam.CounterTerrorist].Clear();
            ggVotes[CsTeam.Terrorist].Clear();
        }

        // Вызывать при завершении матча
        private void OnMatchEnd()
        {
            ResetGGVotes();
        }

        // Вызывать при смене сторон для сброса голосов GG
        public void OnSideSwitch()
        {
            ResetGGVotes();
        }
    }
}
