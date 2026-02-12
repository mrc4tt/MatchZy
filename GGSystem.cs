using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace MatchZy
{
    public partial class MatchZy
    {
        private Dictionary<CsTeam, HashSet<int>> ggVotes = new()
        {
            { CsTeam.CounterTerrorist, new HashSet<int>() },
            { CsTeam.Terrorist, new HashSet<int>() }
        };

        private CounterStrikeSharp.API.Modules.Timers.Timer? ggResetTimer = null;

        [ConsoleCommand("css_gg", "Vote to surrender the match")]
        public void OnGGCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerValid(player)) return;
            if (!isMatchLive)
            {
                ReplyToUserCommand(player, "GG can only be used during a live match!");
                return;
            }

            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, "GG can't be used during a halftime process");
                return;
            }

            var playerTeam = player!.Team;
            if (playerTeam != CsTeam.Terrorist && playerTeam != CsTeam.CounterTerrorist)
            {
                ReplyToUserCommand(player, "You must be on a team to vote for GG!");
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
                ReplyToUserCommand(player, $"Your team must be losing by at least 6 rounds to surrender! Current score: {playerTeamScore}-{opponentTeamScore}");
                return;
            }

            // Подсчитываем общее количество игроков в команде
            var teamPlayers = playerData.Values
                .Where(p => IsPlayerValid(p) && p.Team == playerTeam)
                .ToList();

            // Добавляем голос игрока
            if (!player.UserId.HasValue) return;

            if (ggVotes[playerTeam].Contains(player.UserId.Value))
            {
                ReplyToUserCommand(player, "You have already voted for GG!");
                return;
            }

            ggVotes[playerTeam].Add(player.UserId.Value);

            int votesNeeded;
            if (matchConfig.MinPlayersToReady == 1)
                votesNeeded = 1;
            else
                votesNeeded = Math.Max(2, matchConfig.MinPlayersToReady - 1);

            int currentVotes = ggVotes[playerTeam].Count;

            PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} voted to surrender. {ChatColors.Green}({currentVotes}/{votesNeeded}){ChatColors.Default} votes from {ChatColors.Green}{playerTeamName}{ChatColors.Default} [Score: {playerTeamScore}-{opponentTeamScore}]");

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
                    PrintToAllChat($"{ChatColors.Red}GG cancelled!{ChatColors.Default} {ChatColors.Green}{playerTeamName}{ChatColors.Default} is no longer losing by 6+ rounds. Current score: {finalPlayerTeamScore}-{finalOpponentTeamScore}");
                    ResetGGVotes();
                    return;
                }

                // Команда сдается - определяем победителя
                CsTeam winnerTeam = playerTeam == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
                string winnerTeamName = GetTeamName(winnerTeam);

                PrintToAllChat($"{ChatColors.Green}{playerTeamName}{ChatColors.Default} has surrendered! {ChatColors.Green}{winnerTeamName}{ChatColors.Default} wins!");

                // Используем такую же логику как в FFWSystem
                (int currentT1score, int currentT2score) = GetTeamsScore();

                int t1score_final, t2score_final;

                // Определяем команду-победителя (противник сдавшейся команды)
                Team? winnerMatchTeam = null;
                if (playerMatchTeam == matchzyTeam1)
                {
                    winnerMatchTeam = matchzyTeam2;
                }
                else
                {
                    winnerMatchTeam = matchzyTeam1;
                }

                // Увеличиваем серию счет команды-победителя
                if (winnerMatchTeam != null)
                {
                    winnerMatchTeam.seriesScore++;
                }

                // Устанавливаем финальный счет (как в FFWSystem)
                if (winnerMatchTeam == matchzyTeam1)
                {
                    t1score_final = Math.Max(currentT1score, 16);
                    t2score_final = currentT2score;
                }
                else
                {
                    t1score_final = currentT1score;
                    t2score_final = Math.Max(currentT2score, 16);
                }

                EndSeries(winnerTeamName, 5, t1score_final, t2score_final);
                ResetGGVotes();
            }
            else
            {
                // Устанавливаем таймер для сброса голосов через 60 секунд
                ggResetTimer?.Kill();
                ggResetTimer = AddTimer(60.0f, () =>
                {
                    if (ggVotes[playerTeam].Count > 0)
                    {
                        PrintToAllChat($"GG vote for {ChatColors.Green}{playerTeamName}{ChatColors.Default} has expired!");
                        ggVotes[playerTeam].Clear();
                    }
                });
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
