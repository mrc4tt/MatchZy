using System.Data;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Dapper;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace MatchZy
{
    public partial class MatchZy
    {
        // ─── Chat dispatch (called via commandActions / prefix matching) ────────────

        [ConsoleCommand("css_lastmatch", "Shows the most recently completed match scoreboard")]
        public void OnLastMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            FetchAndPrintLastMatch(player);
        }

        [ConsoleCommand("css_stats", "Shows career stats for a player by name (.stats <name>)")]
        public void OnStatsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            string arg = command?.ArgString?.Trim() ?? string.Empty;
            HandleStatsCommand(player, arg);
        }

        public void HandleStatsCommand(CCSPlayerController? player, string nameQuery)
        {
            nameQuery = nameQuery.Trim();
            if (string.IsNullOrWhiteSpace(nameQuery))
            {
                ReplyToUserCommand(player, "Usage: .stats <name>");
                return;
            }
            FetchAndPrintCareerStats(player, nameQuery);
        }

        // ─── Implementation ────────────────────────────────────────────────────────

        private void FetchAndPrintLastMatch(CCSPlayerController? player)
        {
            // Capture native-thread-only values BEFORE Task.Run.
            string gameDir = Server.GameDirectory;
            string moduleDir = ModuleDirectory;
            Task.Run(async () =>
            {
                try
                {
                    var (header, rows) = await QueryLastMatchAsync(gameDir, moduleDir);
                    Server.NextFrame(() =>
                    {
                        if (header == null)
                        {
                            ReplyToUserCommand(player, "No completed matches found.");
                            return;
                        }
                        ReplyToUserCommand(player, header);
                        foreach (var row in rows)
                            ReplyToUserCommand(player, row);
                    });
                }
                catch (Exception ex)
                {
                    Log($"[lastmatch FATAL] {ex.Message}");
                    Server.NextFrame(() =>
                        ReplyToUserCommand(player, "Stats lookup failed (see server log).")
                    );
                }
            });
        }

        private void FetchAndPrintCareerStats(CCSPlayerController? player, string nameQuery)
        {
            string gameDir = Server.GameDirectory;
            string moduleDir = ModuleDirectory;
            Task.Run(async () =>
            {
                try
                {
                    var lines = await QueryCareerStatsAsync(nameQuery, gameDir, moduleDir);
                    Server.NextFrame(() =>
                    {
                        if (lines.Count == 0)
                        {
                            ReplyToUserCommand(player, $"No stats found for \"{nameQuery}\".");
                            return;
                        }
                        foreach (var line in lines)
                            ReplyToUserCommand(player, line);
                    });
                }
                catch (Exception ex)
                {
                    Log($"[stats FATAL] {ex.Message}");
                    Server.NextFrame(() =>
                        ReplyToUserCommand(player, "Stats lookup failed (see server log).")
                    );
                }
            });
        }

        // ─── DB queries (background thread only) ───────────────────────────────────

        private async Task<(string? header, List<string> rows)> QueryLastMatchAsync(
            string gameDir,
            string moduleDir
        )
        {
            using var conn = OpenStatsConnection(gameDir, moduleDir);

            // Most recent finished match's most recent map
            var match = await conn.QueryFirstOrDefaultAsync(
                @"SELECT m.matchid AS MatchId, m.team1_name AS Team1, m.team1_score AS T1, m.team2_name AS Team2, m.team2_score AS T2, m.winner AS Winner,
                         mp.mapname AS MapName, mp.mapnumber AS MapNumber
                  FROM matchzy_stats_matches m
                  JOIN matchzy_stats_maps mp ON m.matchid = mp.matchid
                  WHERE m.end_time IS NOT NULL
                  ORDER BY m.matchid DESC, mp.mapnumber DESC
                  LIMIT 1"
            );
            if (match == null)
                return (null, new List<string>());

            long matchId = (long)match.MatchId;
            int mapNumber = Convert.ToInt32(match.MapNumber);
            string mapName = match.MapName ?? "?";
            string team1 = match.Team1 ?? "Team 1";
            string team2 = match.Team2 ?? "Team 2";
            int t1 = Convert.ToInt32(match.T1);
            int t2 = Convert.ToInt32(match.T2);

            var players = (
                await conn.QueryAsync(
                    @"SELECT name AS Name, team AS Team, kills AS K, deaths AS D, assists AS A, damage AS DMG, head_shot_kills AS HS
                  FROM matchzy_stats_players
                  WHERE matchid = @MatchId AND mapnumber = @MapNumber
                  ORDER BY kills DESC, damage DESC",
                    new { MatchId = matchId, MapNumber = mapNumber }
                )
            ).ToList();

            string header =
                $"{ChatColors.Green}Last match #{matchId}{ChatColors.Default} on {ChatColors.Yellow}{mapName}{ChatColors.Default} — "
                + $"{ChatColors.Green}{team1} {t1}{ChatColors.Default}:{ChatColors.Green}{t2} {team2}";

            var rows = new List<string>();
            int i = 1;
            foreach (var p in players)
            {
                int k = Convert.ToInt32(p.K);
                int d = Convert.ToInt32(p.D);
                int a = Convert.ToInt32(p.A);
                int dmg = Convert.ToInt32(p.DMG);
                int hs = Convert.ToInt32(p.HS);
                string name = (string)(p.Name ?? "?");
                string team = (string)(p.Team ?? "");
                rows.Add(
                    $" {i, 2}. {ChatColors.Yellow}{name}{ChatColors.Default} [{team}]  "
                        + $"{ChatColors.Green}{k}{ChatColors.Default}/{ChatColors.Red}{d}{ChatColors.Default}/"
                        + $"{ChatColors.LightBlue}{a}{ChatColors.Default}  "
                        + $"DMG {dmg}  HS {hs}"
                );
                i++;
            }
            return (header, rows);
        }

        private async Task<List<string>> QueryCareerStatsAsync(
            string nameQuery,
            string gameDir,
            string moduleDir
        )
        {
            using var conn = OpenStatsConnection(gameDir, moduleDir);

            const string aggregateSql =
                @"SELECT steamid64 AS SteamId,
                         MAX(name) AS Name,
                         COUNT(DISTINCT matchid || '-' || mapnumber) AS Maps,
                         SUM(kills) AS K,
                         SUM(deaths) AS D,
                         SUM(assists) AS A,
                         SUM(damage) AS DMG,
                         SUM(head_shot_kills) AS HS,
                         SUM(enemies5k) AS K5,
                         SUM(enemies4k) AS K4,
                         SUM(enemies3k) AS K3
                  FROM matchzy_stats_players";

            List<dynamic> rows;

            // SteamID64 path — exact 17-digit numeric input
            string trimmed = nameQuery.Trim();
            if (trimmed.Length == 17 && trimmed.All(char.IsDigit))
            {
                rows = (
                    await conn.QueryAsync(
                        aggregateSql + " WHERE steamid64 = @Sid GROUP BY steamid64",
                        new { Sid = ulong.Parse(trimmed) }
                    )
                ).ToList();
            }
            else
            {
                // Aggregate every player, then filter client-side using a normalized
                // (alphanumeric-only, lowercased) substring match. This means a query
                // for "Miksen" matches stored names like "- Miksen" or "[CLAN] Miksen".
                var allRows = (
                    await conn.QueryAsync(aggregateSql + " GROUP BY steamid64")
                ).ToList();
                string normQuery = NormalizeName(trimmed);
                rows = string.IsNullOrEmpty(normQuery)
                    ? new List<dynamic>()
                    : allRows
                        .Where(r => NormalizeName((string)(r.Name ?? "")).Contains(normQuery))
                        .OrderByDescending(r => Convert.ToInt64(r.K))
                        .Take(5)
                        .ToList();
            }

            var lines = new List<string>();
            if (rows.Count == 0)
                return lines;

            lines.Add(
                $"{ChatColors.Green}Career stats matching {ChatColors.Yellow}\"{nameQuery}\"{ChatColors.Default}:"
            );
            int i = 1;
            foreach (var r in rows)
            {
                int maps = Convert.ToInt32(r.Maps);
                int k = Convert.ToInt32(r.K);
                int d = Convert.ToInt32(r.D);
                int a = Convert.ToInt32(r.A);
                long dmg = Convert.ToInt64(r.DMG);
                int hs = Convert.ToInt32(r.HS);
                int multiK = Convert.ToInt32(r.K3) + Convert.ToInt32(r.K4) + Convert.ToInt32(r.K5);
                double kd = d > 0 ? (double)k / d : k;
                double hsPct = k > 0 ? (double)hs / k * 100 : 0;
                string name = (string)(r.Name ?? "?");
                lines.Add(
                    $" {i}. {ChatColors.Yellow}{name}{ChatColors.Default}  "
                        + $"Maps {maps}  K/D {kd:F2} ({k}/{d})  A {a}  "
                        + $"DMG {dmg}  HS {hsPct:F0}%  3K+/{multiK}"
                );
                i++;
            }
            return lines;
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private IDbConnection OpenStatsConnection(string gameDir, string moduleDir)
        {
            string configFile = Path.Combine(gameDir, "csgo/cfg/matchzy", "database.json");
            string dbType = "sqlite";
            DatabaseConfig? cfg = null;
            if (File.Exists(configFile))
            {
                try
                {
                    cfg = System.Text.Json.JsonSerializer.Deserialize<DatabaseConfig>(
                        File.ReadAllText(configFile)
                    );
                    dbType = cfg?.DatabaseType?.Trim().ToLower() ?? "sqlite";
                }
                catch
                {
                    dbType = "sqlite";
                }
            }

            IDbConnection conn;
            if (dbType == "mysql" && cfg != null)
            {
                conn = new MySqlConnection(
                    $"Server={cfg.MySqlHost};Port={cfg.MySqlPort};Database={cfg.MySqlDatabase};User Id={cfg.MySqlUsername};Password={cfg.MySqlPassword};"
                );
            }
            else
            {
                conn = new SqliteConnection($"Data Source={Path.Join(moduleDir, "matchzy.db")}");
            }
            conn.Open();
            return conn;
        }
    }
}
