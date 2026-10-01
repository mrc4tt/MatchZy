using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

/// <summary>
/// Advanced statistics tracking for HLTV-style stats
/// Tracks: KAST, Opening Duels, Trade Kills, Clutches
/// </summary>
public partial class MatchZy
{
    // ═══════════════════════════════════════════════════════════════════
    // Per-round tracking data
    // ═══════════════════════════════════════════════════════════════════

    // Track if this is the first kill of the round
    private bool roundFirstKillOccurred = false;

    // Track deaths this round with timestamp for trade kill detection
    private List<RoundDeath> roundDeaths = new List<RoundDeath>();

    // Track per-player round stats for KAST calculation
    private Dictionary<ulong, PlayerRoundStats> playerRoundStats = new Dictionary<ulong, PlayerRoundStats>();

    // Track alive players per team for clutch detection
    private int aliveT = 0;
    private int aliveCT = 0;
    private bool clutchInProgress = false;
    private ulong? clutchPlayer = null;
    private int clutchOpponents = 0;

    // Advanced stats storage (persists across rounds)
    private Dictionary<ulong, AdvancedPlayerStats> advancedStats = new Dictionary<ulong, AdvancedPlayerStats>();

    // Total rounds played (for percentage calculations)
    private int totalRoundsPlayed = 0;

    // ═══════════════════════════════════════════════════════════════════
    // Data structures
    // ═══════════════════════════════════════════════════════════════════

    private class RoundDeath
    {
        public ulong VictimSteamId { get; set; }
        public ulong? KillerSteamId { get; set; }
        public CsTeam VictimTeam { get; set; }
        public DateTime Time { get; set; }
        public bool WasTraded { get; set; } = false;
    }

    private class PlayerRoundStats
    {
        public bool GotKill { get; set; } = false;
        public int Kills { get; set; } = 0; // enemy kills this round (for 1k rounds)
        public bool GotAssist { get; set; } = false;
        public bool Survived { get; set; } = true; // Assume alive until death
        public bool WasTraded { get; set; } = false;
        public bool WasOpeningKill { get; set; } = false;
        public bool WasOpeningDeath { get; set; } = false;
    }

    public class AdvancedPlayerStats
    {
        public int RoundsPlayed { get; set; } = 0;
        public int KastRounds { get; set; } = 0; // Rounds with K/A/S/T
        public int OpeningKills { get; set; } = 0;
        public int OpeningDeaths { get; set; } = 0;
        public int TradeKills { get; set; } = 0;
        public int TradedDeaths { get; set; } = 0; // Deaths that were traded by teammate

        // Per-side opening duels (the side the player was on when it happened)
        public int FirstKillsT { get; set; } = 0;
        public int FirstKillsCT { get; set; } = 0;
        public int FirstDeathsT { get; set; } = 0;
        public int FirstDeathsCT { get; set; } = 0;

        // Counters the engine's MatchStats does not provide
        public int TeamKills { get; set; } = 0;
        public int Suicides { get; set; } = 0;
        public int KnifeKills { get; set; } = 0;
        public int FlashAssists { get; set; } = 0;
        public int FriendliesFlashed { get; set; } = 0;
        public int BombPlants { get; set; } = 0;
        public int BombDefuses { get; set; } = 0;
        public int Kills1 { get; set; } = 0; // rounds with exactly one kill (Get5 "1k")

        // Clutch stats
        public int Clutch1v1Attempts { get; set; } = 0;
        public int Clutch1v1Wins { get; set; } = 0;
        public int Clutch1v2Attempts { get; set; } = 0;
        public int Clutch1v2Wins { get; set; } = 0;
        public int Clutch1v3Attempts { get; set; } = 0;
        public int Clutch1v3Wins { get; set; } = 0;
        public int Clutch1v4Attempts { get; set; } = 0;
        public int Clutch1v4Wins { get; set; } = 0;
        public int Clutch1v5Attempts { get; set; } = 0;
        public int Clutch1v5Wins { get; set; } = 0;

        public double KastPercentage => RoundsPlayed > 0 ? (double)KastRounds / RoundsPlayed * 100 : 0;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Round lifecycle handlers
    // ═══════════════════════════════════════════════════════════════════

    private void OnAdvancedStatsRoundStart()
    {
        if (!isMatchLive)
            return;

        // Reset per-round tracking
        roundFirstKillOccurred = false;
        roundDeaths.Clear();
        playerRoundStats.Clear();
        clutchInProgress = false;
        clutchPlayer = null;
        clutchOpponents = 0;

        // Initialize round stats for all players
        foreach (var player in Utilities.GetPlayers())
        {
            // Only players on a side; spectators and coaches are not part of the round.
            if (player == null || !player.IsValid || player.IsBot || player.TeamNum is not (2 or 3) || IsMatchCoach(player))
                continue;
            var steamId = player.SteamID;

            playerRoundStats[steamId] = new PlayerRoundStats();

            // Ensure advanced stats entry exists
            if (!advancedStats.ContainsKey(steamId))
            {
                advancedStats[steamId] = new AdvancedPlayerStats();
            }
        }

        // Count alive players
        UpdateAliveCounts();
    }

    private void OnAdvancedStatsRoundEnd(CsTeam winnerTeam)
    {
        // A round restore loading: the stats were just put back to the restored round's start.
        if (!isMatchLive || isRoundRestoring)
            return;

        totalRoundsPlayed++;

        // Process clutch result if one was in progress
        if (clutchInProgress && clutchPlayer.HasValue)
        {
            ProcessClutchResult(winnerTeam);
        }

        // Calculate KAST for each player
        foreach (var kvp in playerRoundStats)
        {
            var steamId = kvp.Key;
            var roundStats = kvp.Value;

            if (!advancedStats.ContainsKey(steamId))
            {
                advancedStats[steamId] = new AdvancedPlayerStats();
            }

            var stats = advancedStats[steamId];
            stats.RoundsPlayed++;

            // KAST: Kill OR Assist OR Survived OR Traded
            bool kast = roundStats.GotKill || roundStats.GotAssist || roundStats.Survived || roundStats.WasTraded;
            if (kast)
            {
                stats.KastRounds++;
            }

            if (roundStats.Kills == 1)
            {
                stats.Kills1++;
            }

            // Opening stats
            if (roundStats.WasOpeningKill)
            {
                stats.OpeningKills++;
            }
            if (roundStats.WasOpeningDeath)
            {
                stats.OpeningDeaths++;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Kill event handler (call this from EventPlayerDeath)
    // ═══════════════════════════════════════════════════════════════════

    private AdvancedPlayerStats AdvancedStatsFor(ulong steamId)
    {
        if (!advancedStats.TryGetValue(steamId, out var stats))
        {
            stats = new AdvancedPlayerStats();
            advancedStats[steamId] = stats;
        }
        return stats;
    }

    // Bomb plant/defuse and team-flash counters for the stats events. Humans only, live match only,
    // like the rest of the advanced stats.
    private void OnAdvancedStatsBombPlanted(CCSPlayerController? player)
    {
        if (isMatchLive && player != null && player.IsValid && !player.IsBot)
            AdvancedStatsFor(player.SteamID).BombPlants++;
    }

    private void OnAdvancedStatsBombDefused(CCSPlayerController? player)
    {
        if (isMatchLive && player != null && player.IsValid && !player.IsBot)
            AdvancedStatsFor(player.SteamID).BombDefuses++;
    }

    private void OnAdvancedStatsPlayerBlind(CCSPlayerController? victim, CCSPlayerController? attacker, float duration)
    {
        if (!isMatchLive || duration <= 0f)
            return;
        if (victim == null || !victim.IsValid || attacker == null || !attacker.IsValid || attacker.IsBot)
            return;
        if (attacker != victim && attacker.TeamNum == victim.TeamNum)
            AdvancedStatsFor(attacker.SteamID).FriendliesFlashed++;
    }

    // Set by OnAdvancedStatsPlayerDeath for the death it just processed; read by the player_kill
    // event handler, which is registered after it and runs for the same player_death.
    private bool lastDeathWasTradeKill = false;

    public void OnAdvancedStatsPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker, CCSPlayerController? assister, string weapon = "", bool assistedFlash = false)
    {
        lastDeathWasTradeKill = false;
        if (!isMatchLive || victim == null || !victim.IsValid || victim.IsBot)
            return;
        // The coach's end-of-freezetime suicide would otherwise be the first death of every round
        // and take the opening duel away from the real players.
        if (IsMatchCoach(victim))
            return;

        var victimSteamId = victim.SteamID;
        var victimTeam = (CsTeam)victim.TeamNum;

        bool attackerValid = attacker != null && attacker.IsValid;
        bool isSuicide = !attackerValid || attacker == victim;
        bool isTeamKill = !isSuicide && attacker!.TeamNum == victim.TeamNum;

        if (isSuicide)
        {
            // World damage / fall / bomb has no attacker; only an explicit self-kill is a suicide.
            if (attackerValid)
                AdvancedStatsFor(victimSteamId).Suicides++;
        }
        else if (isTeamKill)
        {
            if (!attacker!.IsBot)
                AdvancedStatsFor(attacker.SteamID).TeamKills++;
        }
        else if (!attacker!.IsBot && (weapon.Contains("knife", StringComparison.OrdinalIgnoreCase) || weapon.Contains("bayonet", StringComparison.OrdinalIgnoreCase)))
        {
            AdvancedStatsFor(attacker.SteamID).KnifeKills++;
        }

        if (assistedFlash && !isTeamKill && assister != null && assister.IsValid && !assister.IsBot && assister.TeamNum != victim.TeamNum)
            AdvancedStatsFor(assister.SteamID).FlashAssists++;

        // Mark player as dead this round
        if (playerRoundStats.TryGetValue(victimSteamId, out var victimRoundStats))
        {
            victimRoundStats.Survived = false;
        }

        // Track the death
        var death = new RoundDeath
        {
            VictimSteamId = victimSteamId,
            // Only a kill on an enemy counts as the killer's: a suicide used to credit the victim
            // with a kill (KAST "K" for dying), and a team kill counted as an opening/trade kill.
            KillerSteamId = !isSuicide && !isTeamKill && !attacker!.IsBot ? attacker.SteamID : null,
            VictimTeam = victimTeam,
            Time = DateTime.UtcNow,
        };

        // ─────────────────────────────────────────────────────────────────
        // Opening kill/death detection
        // ─────────────────────────────────────────────────────────────────
        if (!roundFirstKillOccurred)
        {
            roundFirstKillOccurred = true;

            // Mark opening death
            if (playerRoundStats.TryGetValue(victimSteamId, out var vrs))
            {
                vrs.WasOpeningDeath = true;
            }
            if (victimTeam == CsTeam.Terrorist)
                AdvancedStatsFor(victimSteamId).FirstDeathsT++;
            else if (victimTeam == CsTeam.CounterTerrorist)
                AdvancedStatsFor(victimSteamId).FirstDeathsCT++;

            // Mark opening kill
            if (death.KillerSteamId.HasValue && playerRoundStats.TryGetValue(death.KillerSteamId.Value, out var krs))
            {
                krs.WasOpeningKill = true;
            }
            if (death.KillerSteamId.HasValue)
            {
                // The killer is on the other side of the victim.
                if (victimTeam == CsTeam.CounterTerrorist)
                    AdvancedStatsFor(death.KillerSteamId.Value).FirstKillsT++;
                else if (victimTeam == CsTeam.Terrorist)
                    AdvancedStatsFor(death.KillerSteamId.Value).FirstKillsCT++;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Trade kill detection (kill within 5 seconds of teammate death)
        // ─────────────────────────────────────────────────────────────────
        if (death.KillerSteamId.HasValue)
        {
            var killerSteamId = death.KillerSteamId.Value;
            var killerTeam = victimTeam == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;

            // Check if this kill avenges a recent teammate death.
            // Perf: manual scan for the first match instead of .Where().ToList()
            // + .Any() + .First() - this runs on every death (~10x/round) and the
            // LINQ chain allocated a closure + List each call.
            var now = DateTime.UtcNow;
            RoundDeath? tradedDeath = null;
            foreach (var d in roundDeaths)
            {
                if (
                    d.VictimTeam == killerTeam
                    && d.KillerSteamId == victimSteamId // the person who just died killed our teammate
                    && !d.WasTraded
                    && (now - d.Time).TotalSeconds <= 5
                )
                {
                    tradedDeath = d;
                    break;
                }
            }

            if (tradedDeath != null)
            {
                // This is a trade kill!
                tradedDeath.WasTraded = true;
                lastDeathWasTradeKill = true;

                // Mark the killer's trade kill stat
                if (!advancedStats.ContainsKey(killerSteamId))
                {
                    advancedStats[killerSteamId] = new AdvancedPlayerStats();
                }
                advancedStats[killerSteamId].TradeKills++;

                // Mark the traded player's round stats
                if (playerRoundStats.TryGetValue(tradedDeath.VictimSteamId, out var tradedPlayerStats))
                {
                    tradedPlayerStats.WasTraded = true;
                }

                // Update their traded deaths count
                if (advancedStats.TryGetValue(tradedDeath.VictimSteamId, out var tradedStats))
                {
                    tradedStats.TradedDeaths++;
                }
            }

            // Mark that killer got a kill this round
            if (playerRoundStats.TryGetValue(killerSteamId, out var killerRoundStats))
            {
                killerRoundStats.GotKill = true;
                killerRoundStats.Kills++;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Assist tracking
        // ─────────────────────────────────────────────────────────────────
        if (assister != null && assister.IsValid && !assister.IsBot)
        {
            if (playerRoundStats.TryGetValue(assister.SteamID, out var assisterRoundStats))
            {
                assisterRoundStats.GotAssist = true;
            }
        }

        // Add death to round deaths list
        roundDeaths.Add(death);

        // Update alive counts and check for clutch
        UpdateAliveCounts();
        CheckClutchSituation(victimTeam);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Clutch detection
    // ═══════════════════════════════════════════════════════════════════

    private void UpdateAliveCounts()
    {
        aliveT = 0;
        aliveCT = 0;

        // Perf: iterate the cached playerData dict instead of Utilities.GetPlayers()
        // - this runs on every player death during live matches (~10x/round), and
        // GetPlayers() allocates a List + crosses the native boundary each call.
        foreach (var player in playerData.Values)
        {
            if (player == null || !player.IsValid || player.IsBot)
                continue;
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || pawn.Health <= 0)
                continue;

            var team = player.TeamNum;
            if (team == (int)CsTeam.Terrorist)
                aliveT++;
            else if (team == (int)CsTeam.CounterTerrorist)
                aliveCT++;
        }
    }

    private void CheckClutchSituation(CsTeam victimTeam)
    {
        // A clutch begins when one team has exactly 1 player and the other has more
        if (clutchInProgress)
            return; // Already tracking a clutch

        CCSPlayerController? clutcher = null;
        int opponents = 0;

        if (aliveT == 1 && aliveCT > 0)
        {
            // T is clutching
            clutcher = Utilities.GetPlayers().FirstOrDefault(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == (int)CsTeam.Terrorist && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.Health > 0);
            opponents = aliveCT;
        }
        else if (aliveCT == 1 && aliveT > 0)
        {
            // CT is clutching
            clutcher = Utilities.GetPlayers().FirstOrDefault(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == (int)CsTeam.CounterTerrorist && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.Health > 0);
            opponents = aliveT;
        }

        if (clutcher != null && opponents >= 1 && opponents <= 5)
        {
            clutchInProgress = true;
            clutchPlayer = clutcher.SteamID;
            clutchOpponents = opponents;

            // Record clutch attempt
            if (!advancedStats.ContainsKey(clutcher.SteamID))
            {
                advancedStats[clutcher.SteamID] = new AdvancedPlayerStats();
            }

            var stats = advancedStats[clutcher.SteamID];
            switch (opponents)
            {
                case 1:
                    stats.Clutch1v1Attempts++;
                    break;
                case 2:
                    stats.Clutch1v2Attempts++;
                    break;
                case 3:
                    stats.Clutch1v3Attempts++;
                    break;
                case 4:
                    stats.Clutch1v4Attempts++;
                    break;
                case 5:
                    stats.Clutch1v5Attempts++;
                    break;
            }

            //Log($"[AdvancedStats] Clutch started: {clutcher.PlayerName} 1v{opponents}");
        }
    }

    private void ProcessClutchResult(CsTeam winnerTeam)
    {
        if (!clutchPlayer.HasValue || !advancedStats.ContainsKey(clutchPlayer.Value))
            return;

        var clutcherPlayer = Utilities.GetPlayers().FirstOrDefault(p => p != null && p.IsValid && !p.IsBot && p.SteamID == clutchPlayer.Value);

        if (clutcherPlayer == null)
            return;

        var clutcherTeam = (CsTeam)clutcherPlayer.TeamNum;
        bool clutchWon = clutcherTeam == winnerTeam;

        if (clutchWon)
        {
            var stats = advancedStats[clutchPlayer.Value];
            switch (clutchOpponents)
            {
                case 1:
                    stats.Clutch1v1Wins++;
                    break;
                case 2:
                    stats.Clutch1v2Wins++;
                    break;
                case 3:
                    stats.Clutch1v3Wins++;
                    break;
                case 4:
                    stats.Clutch1v4Wins++;
                    break;
                case 5:
                    stats.Clutch1v5Wins++;
                    break;
            }

            //Log($"[AdvancedStats] Clutch WON: {clutcherPlayer.PlayerName} 1v{clutchOpponents}");
        }
        else
        {
            //Log($"[AdvancedStats] Clutch LOST: {clutcherPlayer.PlayerName} 1v{clutchOpponents}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Reset stats for new match
    // ═══════════════════════════════════════════════════════════════════

    private sealed class AdvancedStatsSnapshot
    {
        public Dictionary<ulong, AdvancedPlayerStats> Players { get; set; } = new();
        public int TotalRoundsPlayed { get; set; }
    }

    /// <summary>
    /// The advanced stats as they are at the start of the current round, stored in each round
    /// backup. A round restore puts them back: the opening kills, trades, clutches and KAST of the
    /// rounds being replayed used to stay counted and were then counted a second time.
    /// </summary>
    private string CaptureAdvancedStatsSnapshot()
    {
        try
        {
            return JsonSerializer.Serialize(new AdvancedStatsSnapshot { Players = advancedStats, TotalRoundsPlayed = totalRoundsPlayed });
        }
        catch (Exception ex)
        {
            Log($"[AdvancedStats] Could not capture the snapshot: {ex.Message}");
            return "";
        }
    }

    private void RestoreAdvancedStatsSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return; // older backup without the snapshot: keep the current stats
        try
        {
            var snapshot = JsonSerializer.Deserialize<AdvancedStatsSnapshot>(json);
            if (snapshot == null)
                return;
            advancedStats = snapshot.Players ?? new();
            totalRoundsPlayed = snapshot.TotalRoundsPlayed;
            // The round being restored starts again from scratch.
            playerRoundStats.Clear();
            roundDeaths.Clear();
            roundFirstKillOccurred = false;
            clutchInProgress = false;
            clutchPlayer = null;
            clutchOpponents = 0;
        }
        catch (Exception ex)
        {
            Log($"[AdvancedStats] Could not restore the snapshot: {ex.Message}");
        }
    }

    private void ResetAdvancedStats()
    {
        ResetLiveKillCounters();
        advancedStats.Clear();
        playerRoundStats.Clear();
        roundDeaths.Clear();
        roundFirstKillOccurred = false;
        totalRoundsPlayed = 0;
        clutchInProgress = false;
        clutchPlayer = null;
        clutchOpponents = 0;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Get advanced stats for a player
    // ═══════════════════════════════════════════════════════════════════

    public AdvancedPlayerStats? GetAdvancedStats(ulong steamId)
    {
        return advancedStats.TryGetValue(steamId, out var stats) ? stats : null;
    }

    // ═══════════════════════════════════════════════════════════════════
    // JSON Export for Wings stats page
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Collects all match stats data on the MAIN THREAD. Call this before Task.Run().
    /// Returns a pre-built MatchStatsJson object safe to serialize on any thread.
    /// </summary>
    public MatchStatsJson? CollectMatchStatsForExport(string demoFilename, int t1score, int t2score, List<StatsPlayer> playerStatsListTeam1, List<StatsPlayer> playerStatsListTeam2)
    {
        try
        {
            // Rounds on the scoreboard, not the round_end counter: a round replayed after a restore
            // ends twice, which counted it twice and understated ADR, KPR and rating.
            int roundsPlayed = t1score + t2score > 0 ? t1score + t2score : totalRoundsPlayed;
            var allPlayers = new List<MatchStatsPlayer>();

            foreach (var player in playerStatsListTeam1)
            {
                allPlayers.Add(CreateMatchStatsPlayer(player, matchzyTeam1.teamName, roundsPlayed));
            }
            foreach (var player in playerStatsListTeam2)
            {
                allPlayers.Add(CreateMatchStatsPlayer(player, matchzyTeam2.teamName, roundsPlayed));
            }

            return new MatchStatsJson
            {
                MatchId = liveMatchId,
                Map = Server.MapName,
                Date = DateTime.UtcNow.ToString("o"),
                TotalRounds = roundsPlayed,
                DemoFilename = demoFilename,
                Team1 = new TeamStatsJson
                {
                    Name = matchzyTeam1.teamName,
                    Score = t1score,
                    Players = allPlayers.Where(p => p.Team == matchzyTeam1.teamName).OrderByDescending(p => p.Rating).ToList(),
                },
                Team2 = new TeamStatsJson
                {
                    Name = matchzyTeam2.teamName,
                    Score = t2score,
                    Players = allPlayers.Where(p => p.Team == matchzyTeam2.teamName).OrderByDescending(p => p.Rating).ToList(),
                },
                // Empty on a draw (it used to name team2).
                Winner = t1score > t2score ? matchzyTeam1.teamName : t2score > t1score ? matchzyTeam2.teamName : "",
            };
        }
        catch (Exception ex)
        {
            Log($"[AdvancedStats] Failed to collect stats: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes pre-collected match stats to disk. Safe to call from a background thread.
    /// </summary>
    public async Task WriteMatchStatsJsonAsync(MatchStatsJson matchStats, string demoFilename, string statsDirectory)
    {
        try
        {
            if (!Directory.Exists(statsDirectory))
            {
                Directory.CreateDirectory(statsDirectory);
            }

            string statsFilename = Path.GetFileNameWithoutExtension(demoFilename) + "_stats.json";
            string statsPath = Path.Combine(statsDirectory, statsFilename);

            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            string json = JsonSerializer.Serialize(matchStats, options);
            await File.WriteAllTextAsync(statsPath, json);

            //Log($"[AdvancedStats] Match stats exported to: {statsPath}");
        }
        catch (Exception ex)
        {
            Log($"[AdvancedStats] Failed to export stats: {ex.Message}");
        }
    }

    private MatchStatsPlayer CreateMatchStatsPlayer(StatsPlayer player, string teamName, int rounds)
    {
        var stats = player.Stats;
        var steamId = ulong.Parse(player.SteamId);
        var advanced = GetAdvancedStats(steamId) ?? new AdvancedPlayerStats();

        // Calculate derived stats
        double kpr = rounds > 0 ? (double)stats.Kills / rounds : 0;
        double dpr = rounds > 0 ? (double)stats.Deaths / rounds : 0;
        double adr = rounds > 0 ? (double)stats.Damage / rounds : 0;
        double hsPercent = stats.Kills > 0 ? (double)stats.HeadshotKills / stats.Kills * 100 : 0;
        double kastPercent = advanced.KastPercentage;

        // HLTV 2.0 Rating calculation (simplified)
        // Rating = 0.0073*KAST + 0.3591*KPR - 0.5329*DPR + 0.2372*Impact + 0.0032*ADR + 0.1587
        // Impact is hard to calculate, so we use a simplified version
        double impact = kpr * 0.5 + (stats.Kills5 * 0.5 + stats.Kills4 * 0.3 + stats.Kills3 * 0.2) / Math.Max(1, rounds);

        double rating = 0.0073 * kastPercent + 0.3591 * kpr - 0.5329 * dpr + 0.2372 * impact + 0.0032 * adr + 0.1587;

        // Clamp rating to reasonable range
        rating = Math.Max(0.0, Math.Min(3.0, rating));

        return new MatchStatsPlayer
        {
            Name = player.Name,
            SteamId = player.SteamId,
            Team = teamName,
            Kills = stats.Kills,
            Deaths = stats.Deaths,
            Assists = stats.Assists,
            Damage = stats.Damage,
            Adr = Math.Round(adr, 1),
            Kast = Math.Round(kastPercent, 1),
            Rating = Math.Round(rating, 2),
            HeadshotKills = stats.HeadshotKills,
            HsPercent = Math.Round(hsPercent, 1),
            MultiKills = stats.Kills5 + stats.Kills4 + stats.Kills3,
            Kills5 = stats.Kills5,
            Kills4 = stats.Kills4,
            Kills3 = stats.Kills3,
            OpeningKills = advanced.OpeningKills,
            OpeningDeaths = advanced.OpeningDeaths,
            TradeKills = advanced.TradeKills,
            Clutch1v1 = $"{advanced.Clutch1v1Wins}/{advanced.Clutch1v1Attempts}",
            Clutch1v2 = $"{advanced.Clutch1v2Wins}/{advanced.Clutch1v2Attempts}",
            FlashAssists = stats.FlashAssists,
            UtilityDamage = stats.UtilityDamage,
        };
    }
}

// ═══════════════════════════════════════════════════════════════════
// JSON export data structures
// ═══════════════════════════════════════════════════════════════════

public class MatchStatsJson
{
    [JsonPropertyName("match_id")]
    public long MatchId { get; set; }

    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("total_rounds")]
    public int TotalRounds { get; set; }

    [JsonPropertyName("demo_filename")]
    public string DemoFilename { get; set; } = "";

    [JsonPropertyName("winner")]
    public string Winner { get; set; } = "";

    [JsonPropertyName("team1")]
    public TeamStatsJson Team1 { get; set; } = new();

    [JsonPropertyName("team2")]
    public TeamStatsJson Team2 { get; set; } = new();
}

public class TeamStatsJson
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("players")]
    public List<MatchStatsPlayer> Players { get; set; } = new();
}

public class MatchStatsPlayer
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("steam_id")]
    public string SteamId { get; set; } = "";

    [JsonPropertyName("team")]
    public string Team { get; set; } = "";

    [JsonPropertyName("kills")]
    public int Kills { get; set; }

    [JsonPropertyName("deaths")]
    public int Deaths { get; set; }

    [JsonPropertyName("assists")]
    public int Assists { get; set; }

    [JsonPropertyName("damage")]
    public int Damage { get; set; }

    [JsonPropertyName("adr")]
    public double Adr { get; set; }

    [JsonPropertyName("kast")]
    public double Kast { get; set; }

    [JsonPropertyName("rating")]
    public double Rating { get; set; }

    [JsonPropertyName("headshot_kills")]
    public int HeadshotKills { get; set; }

    [JsonPropertyName("hs_percent")]
    public double HsPercent { get; set; }

    [JsonPropertyName("multi_kills")]
    public int MultiKills { get; set; }

    [JsonPropertyName("5k")]
    public int Kills5 { get; set; }

    [JsonPropertyName("4k")]
    public int Kills4 { get; set; }

    [JsonPropertyName("3k")]
    public int Kills3 { get; set; }

    [JsonPropertyName("opening_kills")]
    public int OpeningKills { get; set; }

    [JsonPropertyName("opening_deaths")]
    public int OpeningDeaths { get; set; }

    [JsonPropertyName("trade_kills")]
    public int TradeKills { get; set; }

    [JsonPropertyName("1v1")]
    public string Clutch1v1 { get; set; } = "0/0";

    [JsonPropertyName("1v2")]
    public string Clutch1v2 { get; set; } = "0/0";

    [JsonPropertyName("flash_assists")]
    public int FlashAssists { get; set; }

    [JsonPropertyName("utility_damage")]
    public int UtilityDamage { get; set; }
}
