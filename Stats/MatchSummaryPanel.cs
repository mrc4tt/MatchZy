using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy
{
    public partial class MatchZy
    {
        private CounterStrikeSharp.API.Modules.Timers.Timer? matchSummaryTimer;
        private string? matchSummaryHtml;
        private DateTime matchSummaryExpiry = DateTime.MinValue;

        public void ShowMatchSummaryPanel(MatchStatsJson? stats, int t1score, int t2score, string winnerName)
        {
            if (!matchSummaryPanelEnabled.Value || stats == null)
                return;

            int duration = Math.Max(3, matchSummaryPanelDuration.Value);
            matchSummaryHtml = BuildSummaryHtml(stats, t1score, t2score, winnerName);
            matchSummaryExpiry = DateTime.UtcNow.AddSeconds(duration);

            matchSummaryTimer?.Kill();
            matchSummaryTimer = AddTimer(1.0f, TickMatchSummaryPanel, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            // Print immediately too - first tick is a second away.
            BroadcastSummary();
        }

        private void TickMatchSummaryPanel()
        {
            if (DateTime.UtcNow >= matchSummaryExpiry || matchSummaryHtml == null)
            {
                matchSummaryTimer?.Kill();
                matchSummaryTimer = null;
                matchSummaryHtml = null;
                return;
            }
            BroadcastSummary();
        }

        private void BroadcastSummary()
        {
            if (matchSummaryHtml == null)
                return;
            foreach (var p in Utilities.GetPlayers())
            {
                if (!IsHumanPlayerValid(p))
                    continue;
                p.PrintToCenterHtml(matchSummaryHtml);
            }
        }

        private string BuildSummaryHtml(MatchStatsJson stats, int t1score, int t2score, string winnerName)
        {
            var allPlayers = stats.Team1.Players.Concat(stats.Team2.Players).OrderByDescending(p => p.Rating).ToList();

            var topRating = allPlayers.FirstOrDefault();
            var topClutcher = stats.Team1.Players.Concat(stats.Team2.Players).Select(p => new { Player = p, Wins = ParseClutchWins(p.Clutch1v1) + ParseClutchWins(p.Clutch1v2) }).Where(x => x.Wins > 0).OrderByDescending(x => x.Wins).FirstOrDefault();

            string scoreLine = $"<font color='#FFFFFF' class='fontSize-m'>" + $"{Escape(stats.Team1.Name)} <font color='#90EE90'>{t1score}</font>" + $" : " + $"<font color='#90EE90'>{t2score}</font> {Escape(stats.Team2.Name)}" + $"</font>";

            string winnerLine = string.IsNullOrEmpty(winnerName) ? "<font color='#CCCCCC' class='fontSize-s'>Match Drawn</font>" : $"<font color='#FFD700' class='fontSize-l'>{Escape(winnerName)} wins!</font>";

            string mvpLine = topRating == null ? string.Empty : $"<br><font color='#FFA500' class='fontSize-s'>★ MVP </font>" + $"<font color='#FFFFFF'>{Escape(topRating.Name)}</font>" + $" <font color='#AAAAAA'>" + $"{topRating.Kills}/{topRating.Deaths}/{topRating.Assists}" + $" • {topRating.Rating:F2} rating • {topRating.Adr:F0} ADR</font>";

            string clutchLine = topClutcher == null ? string.Empty : $"<br><font color='#87CEEB' class='fontSize-s'>♛ Clutch King </font>" + $"<font color='#FFFFFF'>{Escape(topClutcher.Player.Name)}</font>" + $" <font color='#AAAAAA'>{topClutcher.Player.Clutch1v1} 1v1 • {topClutcher.Player.Clutch1v2} 1v2</font>";

            string runnersUp = BuildTopFragsTable(allPlayers);

            return winnerLine + "<br>" + scoreLine + mvpLine + clutchLine + (string.IsNullOrEmpty(runnersUp) ? "" : "<br>" + runnersUp);
        }

        private string BuildTopFragsTable(List<MatchStatsPlayer> ordered)
        {
            if (ordered.Count <= 1)
                return string.Empty;
            var top = ordered.Take(3).Skip(1).ToList();
            if (top.Count == 0)
                return string.Empty;
            var rows = top.Select(p => $"<font color='#FFFFFF'>{Escape(p.Name)}</font> " + $"<font color='#AAAAAA'>{p.Kills}/{p.Deaths} • {p.Rating:F2}</font>");
            return "<font color='#90EE90' class='fontSize-s'>Top Frags</font><br>" + string.Join("<br>", rows);
        }

        private static int ParseClutchWins(string clutch)
        {
            // Format "wins/attempts" e.g. "2/3"
            if (string.IsNullOrEmpty(clutch))
                return 0;
            var parts = clutch.Split('/');
            return int.TryParse(parts[0], out int wins) ? wins : 0;
        }

        private static string Escape(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
