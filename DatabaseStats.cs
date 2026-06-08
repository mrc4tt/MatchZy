using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CsvHelper;
using CsvHelper.Configuration;
using Dapper;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace MatchZy
{
    public class Database : IDisposable
    {
        // Initialize the SQLite native provider eagerly, on the main thread, as soon
        // as the Database type is first touched (MatchZy's `database = new()` field
        // initializer runs in the plugin ctor — before Load() and before the async
        // DB-init Task). Doing it here, synchronously, shrinks the window in which a
        // concurrent SqliteConnection open from another plugin (e.g. CS2_SimpleAdmin)
        // on a worker thread can race our open during the native bootstrap and segfault.
        static Database()
        {
            EnsureSqliteProviderInitialized();
        }

        public void Dispose()
        {
            lock (_connectionLock)
            {
                connection?.Close();
                connection?.Dispose();
                connection = null;
            }
        }

        private IDbConnection? connection;
        private string? _connectionString;
        private readonly object _connectionLock = new();

        // Guards SQLitePCLRaw native initialization across plugins. The native
        // e_sqlite3 provider is not safe to initialize concurrently from multiple
        // threads; if another plugin (e.g. CS2_SimpleAdmin) opens a SqliteConnection
        // on a worker thread at the same moment we open ours on the main thread,
        // the native bootstrap can segfault. Pre-initialize once, eagerly.
        private static int _sqliteInitDone;

        private static void EnsureSqliteProviderInitialized()
        {
            if (Interlocked.CompareExchange(ref _sqliteInitDone, 1, 0) != 0)
                return;
            try
            {
                SQLitePCL.Batteries_V2.Init();
            }
            catch
            {
                // Another loader (Microsoft.Data.Sqlite static ctor or another
                // plugin) may have already initialized. Safe to ignore.
            }
        }

        DatabaseConfig? config;
        public DatabaseType databaseType { get; set; }

        public async Task InitializeDatabaseAsync(string directory, string gameDirectory)
        {
            EnsureSqliteProviderInitialized();
            ConnectDatabase(directory, gameDirectory);
            try
            {
                EnsureConnectionOpen();

                // Log the actual connection type being used
                string dbType = (connection is SqliteConnection) ? "SQLite" : "MySQL";
                Log($"[InitializeDatabase] Using {dbType} database");

                // Create the `matchzy_stats_matches`, `matchzy_stats_players` and `matchzy_stats_maps` tables if they doesn't exist
                if (connection is SqliteConnection)
                {
                    await CreateRequiredTablesSQLiteAsync();
                    //Log("[InitializeDatabase] SQLite tables created successfully");
                }
                else
                {
                    await CreateRequiredTablesSQLAsync();
                    //Log("[InitializeDatabase] MySQL tables created successfully");
                }
            }
            catch (Exception ex)
            {
                Log(
                    $"[InitializeDatabase - FATAL] Database connection or table creation error: {ex.Message}"
                );
                Log($"[InitializeDatabase - FATAL] Stack trace: {ex.StackTrace}");
            }
        }

        private void EnsureConnectionOpen()
        {
            lock (_connectionLock)
            {
                EnsureConnectionOpenLocked();
            }
        }

        private void EnsureConnectionOpenLocked()
        {
            if (connection == null)
            {
                throw new InvalidOperationException("Database connection is not initialized");
            }

            if (connection.State != ConnectionState.Open)
            {
                try
                {
                    connection.Open();
                }
                catch (Exception ex)
                {
                    Log(
                        $"[EnsureConnectionOpen] Failed to open connection: {ex.Message}, attempting reconnect..."
                    );
                    // For MySQL, try closing and reopening to handle stale connections
                    if (connection is MySqlConnection mysqlConn)
                    {
                        try
                        {
                            mysqlConn.Close();
                            mysqlConn.Open();
                            Log("[EnsureConnectionOpen] MySQL reconnection successful");
                        }
                        catch (Exception reconnectEx)
                        {
                            Log(
                                $"[EnsureConnectionOpen - FATAL] Reconnect failed: {reconnectEx.Message}"
                            );
                            throw;
                        }
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            else if (connection is MySqlConnection)
            {
                try
                {
                    // Validate the connection is still alive (server may have closed it due to wait_timeout)
                    connection.ExecuteScalar<int>("SELECT 1");
                }
                catch (Exception ex)
                {
                    Log(
                        $"[EnsureConnectionOpen] MySQL stale connection detected: {ex.Message}, reconnecting..."
                    );
                    try
                    {
                        connection.Close();
                        connection.Open();
                        Log("[EnsureConnectionOpen] MySQL reconnection successful");
                    }
                    catch (Exception reconnectEx)
                    {
                        Log(
                            $"[EnsureConnectionOpen - FATAL] Reconnect failed: {reconnectEx.Message}"
                        );
                        throw;
                    }
                }
            }
        }

        public void ConnectDatabase(string directory, string gameDirectory)
        {
            try
            {
                SetDatabaseConfig(gameDirectory);

                if (databaseType == DatabaseType.SQLite)
                {
                    _connectionString = $"Data Source={Path.Join(directory, "matchzy.db")}";
                    connection = new SqliteConnection(_connectionString);
                    Log("[ConnectDatabase] SQLite connection created");
                }
                else if (config != null && databaseType == DatabaseType.MySQL)
                {
                    _connectionString =
                        $"Server={config.MySqlHost};Port={config.MySqlPort};Database={config.MySqlDatabase};User Id={config.MySqlUsername};Password={config.MySqlPassword};";
                    connection = new MySqlConnection(_connectionString);
                    Log("[ConnectDatabase] MySQL connection created");
                }
                else
                {
                    _connectionString = $"Data Source={Path.Join(directory, "matchzy.db")}";
                    connection = new SqliteConnection(_connectionString);
                    databaseType = DatabaseType.SQLite;
                    Log("[ConnectDatabase] Fallback to SQLite connection created");
                }
            }
            catch (Exception ex)
            {
                Log($"[ConnectDatabase - ERROR] Failed to create connection: {ex.Message}");
                throw;
            }
        }

        // Creates a fresh, dedicated DB connection from the pool. Used by
        // operations that must not interleave with other DB work on the shared
        // `connection` — notably matchid allocation, where LAST_INSERT_ID() /
        // last_insert_rowid() are connection-scoped and corrupt under concurrency.
        private IDbConnection CreateNewConnection()
        {
            if (_connectionString == null)
                throw new InvalidOperationException("Database connection string is not initialized");

            return databaseType == DatabaseType.MySQL
                ? new MySqlConnection(_connectionString)
                : new SqliteConnection(_connectionString);
        }

        private async Task CreateRequiredTablesSQLiteAsync()
        {
            await connection!.ExecuteAsync(
                $@"
            CREATE TABLE IF NOT EXISTS matchzy_stats_matches (
                matchid INTEGER PRIMARY KEY AUTOINCREMENT,
                start_time DATETIME NOT NULL,
                end_time DATETIME DEFAULT NULL,
                winner TEXT NOT NULL DEFAULT '',
                series_type TEXT NOT NULL DEFAULT '',
                team1_name TEXT NOT NULL DEFAULT '',
                team1_score INTEGER NOT NULL DEFAULT 0,
                team2_name TEXT NOT NULL DEFAULT '',
                team2_score INTEGER NOT NULL DEFAULT 0,
                server_ip TEXT NOT NULL DEFAULT '0'
            )"
            );

            await connection!.ExecuteAsync(
                @"
                CREATE TABLE IF NOT EXISTS matchzy_stats_maps (
                    matchid INTEGER NOT NULL,
                    mapnumber INTEGER NOT NULL,
                    start_time DATETIME NOT NULL,
                    end_time DATETIME DEFAULT NULL,
                    winner TEXT NOT NULL DEFAULT '',
                    mapname TEXT NOT NULL DEFAULT '',
                    team1_score INTEGER NOT NULL DEFAULT 0,
                    team2_score INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (matchid, mapnumber),
                    FOREIGN KEY (matchid) REFERENCES matchzy_stats_matches (matchid)
                )"
            );

            await connection!.ExecuteAsync(
                @"
                CREATE TABLE IF NOT EXISTS matchzy_stats_players (
                    matchid INTEGER NOT NULL,
                    mapnumber INTEGER NOT NULL,
                    steamid64 INTEGER NOT NULL,
                    team TEXT NOT NULL DEFAULT '',
                    name TEXT NOT NULL,
                    kills INTEGER NOT NULL DEFAULT 0,
                    deaths INTEGER NOT NULL DEFAULT 0,
                    assists INTEGER NOT NULL DEFAULT 0,
                    damage INTEGER NOT NULL DEFAULT 0,
                    enemies5k INTEGER NOT NULL DEFAULT 0,
                    enemies4k INTEGER NOT NULL DEFAULT 0,
                    enemies3k INTEGER NOT NULL DEFAULT 0,
                    enemies2k INTEGER NOT NULL DEFAULT 0,
                    utility_count INTEGER NOT NULL DEFAULT 0,
                    utility_damage INTEGER NOT NULL DEFAULT 0,
                    utility_successes INTEGER NOT NULL DEFAULT 0,
                    utility_enemies INTEGER NOT NULL DEFAULT 0,
                    flash_count INTEGER NOT NULL DEFAULT 0,
                    flash_successes INTEGER NOT NULL DEFAULT 0,
                    health_points_removed_total INTEGER NOT NULL DEFAULT 0,
                    health_points_dealt_total INTEGER NOT NULL DEFAULT 0,
                    shots_fired_total INTEGER NOT NULL DEFAULT 0,
                    shots_on_target_total INTEGER NOT NULL DEFAULT 0,
                    v1_count INTEGER NOT NULL DEFAULT 0,
                    v1_wins INTEGER NOT NULL DEFAULT 0,
                    v2_count INTEGER NOT NULL DEFAULT 0,
                    v2_wins INTEGER NOT NULL DEFAULT 0,
                    entry_count INTEGER NOT NULL DEFAULT 0,
                    entry_wins INTEGER NOT NULL DEFAULT 0,
                    equipment_value INTEGER NOT NULL DEFAULT 0,
                    money_saved INTEGER NOT NULL DEFAULT 0,
                    kill_reward INTEGER NOT NULL DEFAULT 0,
                    live_time INTEGER NOT NULL DEFAULT 0,
                    head_shot_kills INTEGER NOT NULL DEFAULT 0,
                    cash_earned INTEGER NOT NULL DEFAULT 0,
                    enemies_flashed INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (matchid, mapnumber, steamid64),
                    FOREIGN KEY (matchid) REFERENCES matchzy_stats_matches (matchid)
                )"
            );
        }

        private async Task CreateRequiredTablesSQLAsync()
        {
            await connection!.ExecuteAsync(
                $@"
                CREATE TABLE IF NOT EXISTS matchzy_stats_matches (
                    matchid BIGINT AUTO_INCREMENT PRIMARY KEY,
                    start_time DATETIME NOT NULL,
                    end_time DATETIME DEFAULT NULL,
                    winner VARCHAR(64) NOT NULL DEFAULT '',
                    series_type VARCHAR(64) NOT NULL DEFAULT '',
                    team1_name VARCHAR(64) NOT NULL DEFAULT '',
                    team1_score INT NOT NULL DEFAULT 0,
                    team2_name VARCHAR(64) NOT NULL DEFAULT '',
                    team2_score INT NOT NULL DEFAULT 0,
                    server_ip VARCHAR(64) NOT NULL DEFAULT '0'
                )"
            );

            await connection!.ExecuteAsync(
                @"
                CREATE TABLE IF NOT EXISTS matchzy_stats_maps (
                    matchid BIGINT NOT NULL,
                    mapnumber INT NOT NULL,
                    start_time DATETIME NOT NULL,
                    end_time DATETIME DEFAULT NULL,
                    winner VARCHAR(64) NOT NULL DEFAULT '',
                    mapname VARCHAR(64) NOT NULL DEFAULT '',
                    team1_score INT NOT NULL DEFAULT 0,
                    team2_score INT NOT NULL DEFAULT 0,
                    PRIMARY KEY (matchid, mapnumber),
                    FOREIGN KEY (matchid) REFERENCES matchzy_stats_matches (matchid)
                )"
            );

            await connection!.ExecuteAsync(
                @"
                CREATE TABLE IF NOT EXISTS matchzy_stats_players (
                    matchid BIGINT NOT NULL,
                    mapnumber INT NOT NULL,
                    steamid64 BIGINT NOT NULL,
                    team VARCHAR(64) NOT NULL DEFAULT '',
                    name VARCHAR(64) NOT NULL,
                    kills INT NOT NULL DEFAULT 0,
                    deaths INT NOT NULL DEFAULT 0,
                    assists INT NOT NULL DEFAULT 0,
                    damage INT NOT NULL DEFAULT 0,
                    enemies5k INT NOT NULL DEFAULT 0,
                    enemies4k INT NOT NULL DEFAULT 0,
                    enemies3k INT NOT NULL DEFAULT 0,
                    enemies2k INT NOT NULL DEFAULT 0,
                    utility_count INT NOT NULL DEFAULT 0,
                    utility_damage INT NOT NULL DEFAULT 0,
                    utility_successes INT NOT NULL DEFAULT 0,
                    utility_enemies INT NOT NULL DEFAULT 0,
                    flash_count INT NOT NULL DEFAULT 0,
                    flash_successes INT NOT NULL DEFAULT 0,
                    health_points_removed_total INT NOT NULL DEFAULT 0,
                    health_points_dealt_total INT NOT NULL DEFAULT 0,
                    shots_fired_total INT NOT NULL DEFAULT 0,
                    shots_on_target_total INT NOT NULL DEFAULT 0,
                    v1_count INT NOT NULL DEFAULT 0,
                    v1_wins INT NOT NULL DEFAULT 0,
                    v2_count INT NOT NULL DEFAULT 0,
                    v2_wins INT NOT NULL DEFAULT 0,
                    entry_count INT NOT NULL DEFAULT 0,
                    entry_wins INT NOT NULL DEFAULT 0,
                    equipment_value INT NOT NULL DEFAULT 0,
                    money_saved INT NOT NULL DEFAULT 0,
                    kill_reward INT NOT NULL DEFAULT 0,
                    live_time INT NOT NULL DEFAULT 0,
                    head_shot_kills INT NOT NULL DEFAULT 0,
                    cash_earned INT NOT NULL DEFAULT 0,
                    enemies_flashed INT NOT NULL DEFAULT 0,
                    PRIMARY KEY (matchid, mapnumber, steamid64),
                    FOREIGN KEY (matchid) REFERENCES matchzy_stats_matches (matchid)
                )"
            );
        }

        public async Task<long> InitMatchAsync(
            string team1Name,
            string team2Name,
            string winner,
            bool isMatchSetup,
            long currentMatchId,
            int currentMapNumber,
            string seriesType,
            string mapName,
            string serverIp
        )
        {
            try
            {
                using IDbConnection conn = CreateNewConnection();
                conn.Open();
                long matchId;

                // matchid=0 is never a valid autoincrement value; treat as "no
                // existing match" so we allocate a fresh parent row instead of
                // FK-violating against a non-existent matchid=0.
                if (isMatchSetup && currentMatchId > 0)
                {
                    // Reuse existing match
                    matchId = currentMatchId;

                    // Insert new map data
                    await conn.ExecuteAsync(
                        @"
                        INSERT INTO matchzy_stats_maps (matchid, mapnumber, start_time, mapname)
                        VALUES (@MatchId, @MapNumber, @StartTime, @MapName)",
                        new
                        {
                            MatchId = matchId,
                            MapNumber = currentMapNumber,
                            StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            MapName = mapName,
                        }
                    );
                }
                else
                {
                    // Create new match. The INSERT and the id read run on the
                    // same dedicated connection with nothing else able to
                    // interleave, so LAST_INSERT_ID() / last_insert_rowid()
                    // (both connection-scoped) return this INSERT's id rather
                    // than a concurrent operation's.
                    await conn.ExecuteAsync(
                        @"
                        INSERT INTO matchzy_stats_matches (start_time, team1_name, team2_name, winner, series_type, server_ip)
                        VALUES (@StartTime, @Team1Name, @Team2Name, @Winner, @SeriesType, @ServerIp)",
                        new
                        {
                            StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            Team1Name = team1Name,
                            Team2Name = team2Name,
                            Winner = winner,
                            SeriesType = seriesType,
                            ServerIp = serverIp,
                        }
                    );

                    // Get the new match ID
                    if (conn is SqliteConnection)
                    {
                        matchId = await conn.ExecuteScalarAsync<long>(
                            "SELECT last_insert_rowid()"
                        );
                    }
                    else
                    {
                        matchId = await conn.ExecuteScalarAsync<long>(
                            "SELECT LAST_INSERT_ID()"
                        );
                    }

                    // last_insert_rowid()/LAST_INSERT_ID() return 0 if the INSERT
                    // didn't actually take effect (silent failure, rolled-back
                    // transaction, or scalar run on a connection with no prior
                    // INSERT). Surface as -1 so callers don't write garbage FKs.
                    if (matchId <= 0)
                    {
                        Log($"[InitMatch - ERROR] last_insert returned {matchId}; treating as failure.");
                        return -1;
                    }

                    // Insert map data
                    await conn.ExecuteAsync(
                        @"
                        INSERT INTO matchzy_stats_maps (matchid, mapnumber, start_time, mapname)
                        VALUES (@MatchId, @MapNumber, @StartTime, @MapName)",
                        new
                        {
                            MatchId = matchId,
                            MapNumber = currentMapNumber,
                            StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            MapName = mapName,
                        }
                    );
                }

                return matchId;
            }
            catch (Exception ex)
            {
                Log($"[InitMatch - FATAL] Error: {ex.Message}");
                return -1;
            }
        }

        public async Task SetMatchEndDataAsync(
            long matchId,
            int mapNumber,
            string mapWinner,
            int team1Score,
            int team2Score,
            string matchWinner,
            int matchTeam1Score,
            int matchTeam2Score
        )
        {
            if (matchId == -1)
            {
                Log("[SetMatchEndData - ERROR] Invalid matchId: -1");
                return;
            }

            try
            {
                EnsureConnectionOpen();

                // Update map data
                await connection!.ExecuteAsync(
                    @"
                    UPDATE matchzy_stats_maps
                    SET end_time = @EndTime,
                        winner = @Winner,
                        team1_score = @Team1Score,
                        team2_score = @Team2Score
                    WHERE matchid = @MatchId AND mapnumber = @MapNumber",
                    new
                    {
                        EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Winner = mapWinner,
                        Team1Score = team1Score,
                        Team2Score = team2Score,
                        MatchId = matchId,
                        MapNumber = mapNumber,
                    }
                );

                // Update match data
                await connection!.ExecuteAsync(
                    @"
                    UPDATE matchzy_stats_matches
                    SET end_time = @EndTime,
                        winner = @Winner,
                        team1_score = @Team1Score,
                        team2_score = @Team2Score
                    WHERE matchid = @MatchId",
                    new
                    {
                        EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Winner = matchWinner,
                        Team1Score = matchTeam1Score,
                        Team2Score = matchTeam2Score,
                        MatchId = matchId,
                    }
                );

                Log($"[SetMatchEndData] Match {matchId} end data set successfully");
            }
            catch (Exception ex)
            {
                Log($"[SetMatchEndData - FATAL] Error: {ex.Message}");
            }
        }

        public async Task UpdateTeamNamesAsync(long matchId, string team1Name, string team2Name)
        {
            if (matchId <= 0)
                return;
            try
            {
                EnsureConnectionOpen();
                await connection!.ExecuteAsync(
                    @"UPDATE matchzy_stats_matches
                      SET team1_name = @Team1Name, team2_name = @Team2Name
                      WHERE matchid = @MatchId",
                    new { Team1Name = team1Name, Team2Name = team2Name, MatchId = matchId }
                );
            }
            catch (Exception ex)
            {
                Log($"[UpdateTeamNames - FATAL] Error: {ex.Message}");
            }
        }

        public async Task UpdatePlayerStatsAsync(
            long matchId,
            int mapNumber,
            Dictionary<long, Dictionary<string, object>> playerStatsDictionary
        )
        {
            if (matchId == -1)
            {
                Log("[UpdatePlayerStats - ERROR] Invalid matchId: -1");
                return;
            }

            try
            {
                EnsureConnectionOpen();

                foreach (var kvp in playerStatsDictionary)
                {
                    var steamid64 = kvp.Key;
                    var playerStats = kvp.Value;

                    string sqlQuery;
                    if (connection is SqliteConnection)
                    {
                        sqlQuery =
                            @"
                            INSERT INTO matchzy_stats_players (
                                matchid, mapnumber, steamid64, team, name, kills, deaths, assists, damage,
                                enemies5k, enemies4k, enemies3k, enemies2k, utility_count, utility_damage,
                                utility_successes, utility_enemies, flash_count, flash_successes,
                                health_points_removed_total, health_points_dealt_total, shots_fired_total,
                                shots_on_target_total, v1_count, v1_wins, v2_count, v2_wins, entry_count,
                                entry_wins, equipment_value, money_saved, kill_reward, live_time,
                                head_shot_kills, cash_earned, enemies_flashed
                            )
                            VALUES (
                                @matchId, @mapNumber, @steamid64, @team, @name, @kills, @deaths, @assists, @damage,
                                @enemy5ks, @enemy4ks, @enemy3ks, @enemy2ks, @utility_count, @utility_damage,
                                @utility_successes, @utility_enemies, @flash_count, @flash_successes,
                                @health_points_removed_total, @health_points_dealt_total, @shots_fired_total,
                                @shots_on_target_total, @v1_count, @v1_wins, @v2_count, @v2_wins, @entry_count,
                                @entry_wins, @equipment_value, @money_saved, @kill_reward, @live_time,
                                @head_shot_kills, @cash_earned, @enemies_flashed
                            )
                            ON CONFLICT(matchid, mapnumber, steamid64) DO UPDATE SET
                                team = excluded.team,
                                name = excluded.name,
                                kills = excluded.kills,
                                deaths = excluded.deaths,
                                assists = excluded.assists,
                                damage = excluded.damage,
                                enemies5k = excluded.enemies5k,
                                enemies4k = excluded.enemies4k,
                                enemies3k = excluded.enemies3k,
                                enemies2k = excluded.enemies2k,
                                utility_count = excluded.utility_count,
                                utility_damage = excluded.utility_damage,
                                utility_successes = excluded.utility_successes,
                                utility_enemies = excluded.utility_enemies,
                                flash_count = excluded.flash_count,
                                flash_successes = excluded.flash_successes,
                                health_points_removed_total = excluded.health_points_removed_total,
                                health_points_dealt_total = excluded.health_points_dealt_total,
                                shots_fired_total = excluded.shots_fired_total,
                                shots_on_target_total = excluded.shots_on_target_total,
                                v1_count = excluded.v1_count,
                                v1_wins = excluded.v1_wins,
                                v2_count = excluded.v2_count,
                                v2_wins = excluded.v2_wins,
                                entry_count = excluded.entry_count,
                                entry_wins = excluded.entry_wins,
                                equipment_value = excluded.equipment_value,
                                money_saved = excluded.money_saved,
                                kill_reward = excluded.kill_reward,
                                live_time = excluded.live_time,
                                head_shot_kills = excluded.head_shot_kills,
                                cash_earned = excluded.cash_earned,
                                enemies_flashed = excluded.enemies_flashed";
                    }
                    else
                    {
                        sqlQuery =
                            @"
                            INSERT INTO matchzy_stats_players (
                                matchid, mapnumber, steamid64, team, name, kills, deaths, assists, damage,
                                enemies5k, enemies4k, enemies3k, enemies2k, utility_count, utility_damage,
                                utility_successes, utility_enemies, flash_count, flash_successes,
                                health_points_removed_total, health_points_dealt_total, shots_fired_total,
                                shots_on_target_total, v1_count, v1_wins, v2_count, v2_wins, entry_count,
                                entry_wins, equipment_value, money_saved, kill_reward, live_time,
                                head_shot_kills, cash_earned, enemies_flashed
                            )
                            VALUES (
                                @matchId, @mapNumber, @steamid64, @team, @name, @kills, @deaths, @assists, @damage,
                                @enemy5ks, @enemy4ks, @enemy3ks, @enemy2ks, @utility_count, @utility_damage,
                                @utility_successes, @utility_enemies, @flash_count, @flash_successes,
                                @health_points_removed_total, @health_points_dealt_total, @shots_fired_total,
                                @shots_on_target_total, @v1_count, @v1_wins, @v2_count, @v2_wins, @entry_count,
                                @entry_wins, @equipment_value, @money_saved, @kill_reward, @live_time,
                                @head_shot_kills, @cash_earned, @enemies_flashed
                            )
                            ON DUPLICATE KEY UPDATE
                                team = VALUES(team),
                                name = VALUES(name),
                                kills = VALUES(kills),
                                deaths = VALUES(deaths),
                                assists = VALUES(assists),
                                damage = VALUES(damage),
                                enemies5k = VALUES(enemies5k),
                                enemies4k = VALUES(enemies4k),
                                enemies3k = VALUES(enemies3k),
                                enemies2k = VALUES(enemies2k),
                                utility_count = VALUES(utility_count),
                                utility_damage = VALUES(utility_damage),
                                utility_successes = VALUES(utility_successes),
                                utility_enemies = VALUES(utility_enemies),
                                flash_count = VALUES(flash_count),
                                flash_successes = VALUES(flash_successes),
                                health_points_removed_total = VALUES(health_points_removed_total),
                                health_points_dealt_total = VALUES(health_points_dealt_total),
                                shots_fired_total = VALUES(shots_fired_total),
                                shots_on_target_total = VALUES(shots_on_target_total),
                                v1_count = VALUES(v1_count),
                                v1_wins = VALUES(v1_wins),
                                v2_count = VALUES(v2_count),
                                v2_wins = VALUES(v2_wins),
                                entry_count = VALUES(entry_count),
                                entry_wins = VALUES(entry_wins),
                                equipment_value = VALUES(equipment_value),
                                money_saved = VALUES(money_saved),
                                kill_reward = VALUES(kill_reward),
                                live_time = VALUES(live_time),
                                head_shot_kills = VALUES(head_shot_kills),
                                cash_earned = VALUES(cash_earned),
                                enemies_flashed = VALUES(enemies_flashed)";
                    }

                    await connection!.ExecuteAsync(
                        sqlQuery,
                        new
                        {
                            matchId,
                            mapNumber,
                            steamid64,
                            team = playerStats["TeamName"],
                            name = playerStats["PlayerName"],
                            kills = playerStats["Kills"],
                            deaths = playerStats["Deaths"],
                            damage = playerStats["Damage"],
                            assists = playerStats["Assists"],
                            enemy5ks = playerStats["Enemy5Ks"],
                            enemy4ks = playerStats["Enemy4Ks"],
                            enemy3ks = playerStats["Enemy3Ks"],
                            enemy2ks = playerStats["Enemy2Ks"],
                            utility_count = playerStats["UtilityCount"],
                            utility_damage = playerStats["UtilityDamage"],
                            utility_successes = playerStats["UtilitySuccess"],
                            utility_enemies = playerStats["UtilityEnemies"],
                            flash_count = playerStats["FlashCount"],
                            flash_successes = playerStats["FlashSuccess"],
                            health_points_removed_total = playerStats["HealthPointsRemovedTotal"],
                            health_points_dealt_total = playerStats["HealthPointsDealtTotal"],
                            shots_fired_total = playerStats["ShotsFiredTotal"],
                            shots_on_target_total = playerStats["ShotsOnTargetTotal"],
                            v1_count = playerStats["1v1Count"],
                            v1_wins = playerStats["1v1Wins"],
                            v2_count = playerStats["1v2Count"],
                            v2_wins = playerStats["1v2Wins"],
                            entry_count = playerStats["EntryCount"],
                            entry_wins = playerStats["EntryWins"],
                            equipment_value = playerStats["EquipmentValue"],
                            money_saved = playerStats["MoneySaved"],
                            kill_reward = playerStats["KillReward"],
                            live_time = playerStats["LiveTime"],
                            head_shot_kills = playerStats["HeadShotKills"],
                            cash_earned = playerStats["CashEarned"],
                            enemies_flashed = playerStats["EnemiesFlashed"],
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                Log($"[UpdatePlayerStats - FATAL] Error inserting/updating data: {ex.Message}");
            }
        }

        public async Task WritePlayerStatsToCsvAsync(string filePath, long matchId, int mapNumber)
        {
            if (matchId == -1)
            {
                Log("[WritePlayerStatsToCsv - ERROR] Invalid matchId: -1");
                return;
            }

            try
            {
                EnsureConnectionOpen();

                string csvFilePath = $"{filePath}/match_data_map{mapNumber}_{matchId}.csv";
                string? directoryPath = Path.GetDirectoryName(csvFilePath);
                if (directoryPath != null)
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                }

                using (var writer = new StreamWriter(csvFilePath))
                using (
                    var csv = new CsvWriter(
                        writer,
                        new CsvConfiguration(CultureInfo.InvariantCulture)
                    )
                )
                {
                    IEnumerable<dynamic> playerStatsData = await connection!.QueryAsync(
                        "SELECT * FROM matchzy_stats_players WHERE matchid = @MatchId AND mapnumber = @MapNumber ORDER BY team, kills DESC",
                        new { MatchId = matchId, MapNumber = mapNumber }
                    );

                    // Use the first data row to get the column names
                    dynamic? firstDataRow = playerStatsData.FirstOrDefault();
                    if (firstDataRow != null)
                    {
                        foreach (
                            var propertyName in ((IDictionary<string, object>)firstDataRow).Keys
                        )
                        {
                            csv.WriteField(propertyName);
                        }
                        csv.NextRecord(); // End of the column names row

                        // Write data to the CSV file
                        foreach (var playerStats in playerStatsData)
                        {
                            foreach (
                                var propertyValue in (
                                    (IDictionary<string, object>)playerStats
                                ).Values
                            )
                            {
                                csv.WriteField(propertyValue);
                            }
                            csv.NextRecord();
                        }
                    }
                }
                Log(
                    $"[WritePlayerStatsToCsv] Match stats for ID: {matchId} written successfully at: {csvFilePath}"
                );
            }
            catch (Exception ex)
            {
                Log($"[WritePlayerStatsToCsv - FATAL] Error writing data: {ex.Message}");
            }
        }

        private void CreateDefaultConfigFile(string configFile)
        {
            // Create a default configuration
            DatabaseConfig defaultConfig = new DatabaseConfig
            {
                DatabaseType = "SQLite",
                MySqlHost = "your_mysql_host",
                MySqlDatabase = "your_mysql_database",
                MySqlUsername = "your_mysql_username",
                MySqlPassword = "your_mysql_password",
                MySqlPort = 3306,
            };

            // Serialize and save the default configuration to the file
            string defaultConfigJson = JsonSerializer.Serialize(
                defaultConfig,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(configFile, defaultConfigJson);

            Log($"[InitializeDatabase] Default configuration file created at: {configFile}");
        }

        private void SetDatabaseConfig(string gameDirectory)
        {
            string fileName = "database.json";
            string configFile = Path.Combine(gameDirectory + "/csgo/cfg/matchzy", fileName);
            if (!File.Exists(configFile))
            {
                // Create a default configuration if the file doesn't exist
                Log($"[SetDatabaseConfig] database.json doesn't exist, creating default!");
                CreateDefaultConfigFile(configFile);
            }

            try
            {
                string jsonContent = File.ReadAllText(configFile);
                config = JsonSerializer.Deserialize<DatabaseConfig>(jsonContent);

                // Set the database type based on config
                if (config != null && config.DatabaseType?.Trim().ToLower() == "sqlite")
                {
                    databaseType = DatabaseType.SQLite;
                    Log($"[SetDatabaseConfig] Database type set to: SQLite (from database.json)");
                }
                else if (config != null && config.DatabaseType?.Trim().ToLower() == "mysql")
                {
                    databaseType = DatabaseType.MySQL;
                    Log($"[SetDatabaseConfig] Database type set to: MySQL (from database.json)");
                }
                else
                {
                    databaseType = DatabaseType.SQLite;
                    Log($"[SetDatabaseConfig] Database type not recognized, defaulting to: SQLite");
                }
            }
            catch (JsonException ex)
            {
                Log(
                    $"[SetDatabaseConfig - ERROR] Error deserializing database.json: {ex.Message}. Using SQLite DB"
                );
                databaseType = DatabaseType.SQLite;
            }
            catch (Exception ex)
            {
                Log(
                    $"[SetDatabaseConfig - ERROR] Unexpected error reading database.json: {ex.Message}. Using SQLite DB"
                );
                databaseType = DatabaseType.SQLite;
            }
        }

        private void Log(string message)
        {
            Console.WriteLine("[MatchZy] " + message);
        }

        internal void SetMatchEndData(long matchId, string v, int team1Score, int team2Score)
        {
            throw new NotImplementedException();
        }

        public enum DatabaseType
        {
            SQLite,
            MySQL,
        }
    }

    public class DatabaseConfig
    {
        public string? DatabaseType { get; set; }
        public string? MySqlHost { get; set; }
        public string? MySqlDatabase { get; set; }
        public string? MySqlUsername { get; set; }
        public string? MySqlPassword { get; set; }
        public int? MySqlPort { get; set; }
    }
}
