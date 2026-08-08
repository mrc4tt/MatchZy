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
        // initializer runs in the plugin ctor - before Load() and before the async
        // DB-init Task). Doing it here, synchronously, shrinks the window in which a
        // concurrent SqliteConnection open from another plugin (e.g. CS2_SimpleAdmin)
        // on a worker thread can race our open during the native bootstrap and segfault.
        static Database()
        {
            EnsureSqliteProviderInitialized();
        }

        public void Dispose()
        {
            // Nothing to release: every DB operation opens and disposes its own
            // connection (see CreateNewConnection), so no long-lived handle is held.
        }

        private string? _connectionString;

        // Guards SQLitePCLRaw native initialization across plugins. The native
        // e_sqlite3 provider is not safe to initialize concurrently from multiple
        // threads; if another plugin (e.g. CS2_SimpleAdmin) opens a SqliteConnection
        // on a worker thread at the same moment we open ours on the main thread,
        // the native bootstrap can segfault. Pre-initialize once, eagerly.
        private static int _sqliteInitDone;

        // Returns null on success/already-initialized, or a flattened error string when the
        // native bootstrap failed (so the instance caller can Log it - this is static).
        private static string? EnsureSqliteProviderInitialized()
        {
            if (Interlocked.CompareExchange(ref _sqliteInitDone, 1, 0) != 0)
                return null;
            try
            {
                SQLitePCL.Batteries_V2.Init();
                return null;
            }
            catch (Exception ex)
            {
                // Another loader (Microsoft.Data.Sqlite static ctor or another
                // plugin) may have already initialized - that case is harmless.
                // But a genuine native-load failure (missing/incompatible
                // e_sqlite3 .so under the .NET 10 fork runtime) also lands here
                // and otherwise surfaces later as an opaque TypeInitializationException
                // on SqliteConnection. Return the full chain so the root cause is visible.
                return DescribeException(ex);
            }
        }

        /// <summary>
        /// Flattens an exception and its InnerException chain into a single line -
        /// TypeInitializationException et al. hide the real cause (e.g. a native
        /// DllNotFoundException) in InnerException, which the bare ex.Message drops.
        /// </summary>
        private static string DescribeException(Exception ex)
        {
            System.Text.StringBuilder sb = new();
            Exception? cur = ex;
            int depth = 0;
            while (cur != null)
            {
                if (depth > 0)
                    sb.Append(" -> INNER ");
                sb.Append('[').Append(cur.GetType().Name).Append("] ").Append(cur.Message);
                cur = cur.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        DatabaseConfig? config;
        public DatabaseType databaseType { get; set; }

        public async Task InitializeDatabaseAsync(string directory, string gameDirectory)
        {
            string? sqliteInitError = EnsureSqliteProviderInitialized();
            if (sqliteInitError != null)
                Log($"[EnsureSqliteProviderInitialized] Batteries_V2.Init() failed: {sqliteInitError}");
            ConnectDatabase(directory, gameDirectory);
            try
            {
                using IDbConnection conn = CreateNewConnection();
                conn.Open();

                // Log the actual connection type being used
                string dbType = (conn is SqliteConnection) ? "SQLite" : "MySQL";
                Log($"[InitializeDatabase] Using {dbType} database");

                // Create the `matchzy_stats_matches`, `matchzy_stats_players` and `matchzy_stats_maps` tables if they doesn't exist
                if (conn is SqliteConnection)
                {
                    await CreateRequiredTablesSQLiteAsync(conn);
                    //Log("[InitializeDatabase] SQLite tables created successfully");
                }
                else
                {
                    await CreateRequiredTablesSQLAsync(conn);
                    //Log("[InitializeDatabase] MySQL tables created successfully");
                }

                await MigratePlayerColumnNamesAsync(conn);
            }
            catch (Exception ex)
            {
                Log($"[InitializeDatabase - FATAL] Database connection or table creation error: {DescribeException(ex)}");
                Log($"[InitializeDatabase - FATAL] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Brings a matchzy_stats_players table created by an older build of this fork onto the
        /// upstream column names.
        ///
        /// This fork briefly shipped enemies5k/4k/3k/2k where upstream (and therefore every
        /// pre-existing database, and every web panel that reads one) uses enemy5ks/4ks/3ks/2ks.
        /// CREATE TABLE IF NOT EXISTS does nothing to a table that already exists, so installing the
        /// fork over an upstream database left the schema untouched and made every player INSERT
        /// fail with "Unknown column 'enemies5k' in 'field list'". The exception was swallowed per
        /// call, so matchzy_stats_matches and matchzy_stats_maps kept filling up normally while
        /// matchzy_stats_players silently stayed empty for every match.
        ///
        /// Upstream's names are canonical here: they are what existing data and external tooling
        /// already use. Only a database written by the affected fork builds needs renaming.
        /// </summary>
        private async Task MigratePlayerColumnNamesAsync(IDbConnection conn)
        {
            (string From, string To)[] renames =
            [
                ("enemies5k", "enemy5ks"),
                ("enemies4k", "enemy4ks"),
                ("enemies3k", "enemy3ks"),
                ("enemies2k", "enemy2ks"),
            ];

            try
            {
                HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
                if (conn is SqliteConnection)
                {
                    foreach (var name in await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('matchzy_stats_players')"))
                        columns.Add(name);
                }
                else
                {
                    foreach (var name in await conn.QueryAsync<string>("SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'matchzy_stats_players'"))
                        columns.Add(name);
                }

                if (columns.Count == 0)
                    return;

                foreach (var (from, to) in renames)
                {
                    // Nothing to do when the column already carries the upstream name, and never
                    // rename onto a name that is somehow already taken.
                    if (!columns.Contains(from) || columns.Contains(to))
                        continue;

                    // MySQL 5.7 has no RENAME COLUMN, so use CHANGE (which needs the type restated).
                    string sql = conn is SqliteConnection
                        ? $"ALTER TABLE matchzy_stats_players RENAME COLUMN {from} TO {to}"
                        : $"ALTER TABLE matchzy_stats_players CHANGE {from} {to} INT NOT NULL DEFAULT 0";

                    await conn.ExecuteAsync(sql);
                    Log($"[MigratePlayerColumnNames] Renamed matchzy_stats_players.{from} to {to}.");
                }
            }
            catch (Exception ex)
            {
                // Not fatal on its own, but it does mean player stats will keep failing to write,
                // so say so plainly rather than leaving an empty table as the only symptom.
                Log($"[MigratePlayerColumnNames - ERROR] Could not check or fix the player stats columns: {DescribeException(ex)}");
            }
        }

        public void ConnectDatabase(string directory, string gameDirectory)
        {
            try
            {
                SetDatabaseConfig(gameDirectory);

                if (config != null && databaseType == DatabaseType.MySQL)
                {
                    _connectionString = $"Server={config.MySqlHost};Port={config.MySqlPort};Database={config.MySqlDatabase};User Id={config.MySqlUsername};Password={config.MySqlPassword};";
                    Log("[ConnectDatabase] MySQL connection string configured");
                }
                else
                {
                    // "Default Timeout" is how long a command waits out a SQLITE_BUSY before
                    // giving up. It matters now that operations no longer share one handle:
                    // two writers can briefly contend on the file lock.
                    _connectionString = $"Data Source={Path.Join(directory, "matchzy.db")};Default Timeout=30";
                    databaseType = DatabaseType.SQLite;
                    Log("[ConnectDatabase] SQLite connection string configured");
                }
            }
            catch (Exception ex)
            {
                Log($"[ConnectDatabase - ERROR] Failed to configure connection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates a fresh, dedicated DB connection. EVERY database operation must use one of
        /// these and dispose it - never a shared field.
        ///
        /// Neither MySqlConnection nor SqliteConnection is thread-safe, and MatchZy issues DB work
        /// from Task.Run on pool threads: round-end player stats, map-end data and the series-end
        /// write all overlap at the end of the last round. Sharing one connection across those threw
        /// "there is already an open DataReader" / connection-in-use, which the per-method catch
        /// swallowed - so a whole match of player stats could vanish behind one log line. Per-call
        /// connections make the overlap harmless; the pool (on by default for both providers) keeps
        /// the open cost negligible.
        ///
        /// It also keeps LAST_INSERT_ID() / last_insert_rowid() honest - both are connection scoped,
        /// so a shared handle can hand back another operation's id.
        /// </summary>
        private IDbConnection CreateNewConnection()
        {
            if (_connectionString == null)
                throw new InvalidOperationException("Database connection string is not initialized");

            return databaseType == DatabaseType.MySQL ? new MySqlConnection(_connectionString) : new SqliteConnection(_connectionString);
        }

        private async Task CreateRequiredTablesSQLiteAsync(IDbConnection conn)
        {
            await conn.ExecuteAsync(
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

            await conn.ExecuteAsync(
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

            await conn.ExecuteAsync(
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
                    enemy5ks INTEGER NOT NULL DEFAULT 0,
                    enemy4ks INTEGER NOT NULL DEFAULT 0,
                    enemy3ks INTEGER NOT NULL DEFAULT 0,
                    enemy2ks INTEGER NOT NULL DEFAULT 0,
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

        private async Task CreateRequiredTablesSQLAsync(IDbConnection conn)
        {
            await conn.ExecuteAsync(
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

            await conn.ExecuteAsync(
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

            await conn.ExecuteAsync(
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
                    enemy5ks INT NOT NULL DEFAULT 0,
                    enemy4ks INT NOT NULL DEFAULT 0,
                    enemy3ks INT NOT NULL DEFAULT 0,
                    enemy2ks INT NOT NULL DEFAULT 0,
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

        public async Task<long> InitMatchAsync(string team1Name, string team2Name, string winner, bool isMatchSetup, long currentMatchId, int currentMapNumber, string seriesType, string mapName, string serverIp)
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

                    // The matchid came from outside (match JSON "matchid", a backup file, G5), so
                    // the parent matchzy_stats_matches row may not exist. matchzy_stats_maps has
                    // FOREIGN KEY (matchid) REFERENCES matchzy_stats_matches (matchid), which
                    // InnoDB enforces - inserting the map row first threw, InitMatch returned -1,
                    // and every later write bailed on "Invalid matchId: -1", losing the whole
                    // match's player stats. SQLite never enforced the FK, so this only ever showed
                    // up on MySQL. Make the parent exist first, idempotently.
                    await EnsureMatchRowAsync(conn, matchId, team1Name, team2Name, winner, seriesType, serverIp);

                    // Insert new map data. Upsert rather than plain INSERT: reloading the same
                    // matchid (e.g. restarting a match after a bad setup) otherwise violates
                    // PRIMARY KEY (matchid, mapnumber) and fails the same way. Result columns
                    // (winner/scores/end_time) are deliberately left alone - SetMatchEndData owns
                    // those, and a series' earlier maps must not be reset by a later map's start.
                    await conn.ExecuteAsync(
                        conn is SqliteConnection
                            ? @"
                            INSERT INTO matchzy_stats_maps (matchid, mapnumber, start_time, mapname)
                            VALUES (@MatchId, @MapNumber, @StartTime, @MapName)
                            ON CONFLICT(matchid, mapnumber) DO UPDATE SET
                                start_time = excluded.start_time,
                                mapname = excluded.mapname"
                            : @"
                            INSERT INTO matchzy_stats_maps (matchid, mapnumber, start_time, mapname)
                            VALUES (@MatchId, @MapNumber, @StartTime, @MapName)
                            ON DUPLICATE KEY UPDATE
                                start_time = VALUES(start_time),
                                mapname = VALUES(mapname)",
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
                        matchId = await conn.ExecuteScalarAsync<long>("SELECT last_insert_rowid()");
                    }
                    else
                    {
                        matchId = await conn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()");
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
                Log($"[InitMatch - FATAL] Error: {DescribeException(ex)}");
                Log($"[InitMatch - FATAL] Stack: {ex.StackTrace}");
                return -1;
            }
        }

        /// <summary>
        /// Makes sure matchzy_stats_matches holds a row for an externally supplied matchid, so the
        /// child rows in matchzy_stats_maps / matchzy_stats_players satisfy their foreign key.
        /// Idempotent: an existing row keeps its start_time, winner and end_time, and only the
        /// descriptive columns are refreshed.
        /// </summary>
        private static async Task EnsureMatchRowAsync(IDbConnection conn, long matchId, string team1Name, string team2Name, string winner, string seriesType, string serverIp)
        {
            await conn.ExecuteAsync(
                conn is SqliteConnection
                    ? @"
                    INSERT INTO matchzy_stats_matches (matchid, start_time, team1_name, team2_name, winner, series_type, server_ip)
                    VALUES (@MatchId, @StartTime, @Team1Name, @Team2Name, @Winner, @SeriesType, @ServerIp)
                    ON CONFLICT(matchid) DO UPDATE SET
                        team1_name = excluded.team1_name,
                        team2_name = excluded.team2_name,
                        series_type = excluded.series_type,
                        server_ip = excluded.server_ip"
                    : @"
                    INSERT INTO matchzy_stats_matches (matchid, start_time, team1_name, team2_name, winner, series_type, server_ip)
                    VALUES (@MatchId, @StartTime, @Team1Name, @Team2Name, @Winner, @SeriesType, @ServerIp)
                    ON DUPLICATE KEY UPDATE
                        team1_name = VALUES(team1_name),
                        team2_name = VALUES(team2_name),
                        series_type = VALUES(series_type),
                        server_ip = VALUES(server_ip)",
                new
                {
                    MatchId = matchId,
                    StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Team1Name = team1Name,
                    Team2Name = team2Name,
                    Winner = winner,
                    SeriesType = seriesType,
                    ServerIp = serverIp,
                }
            );
        }

        /// <summary>
        /// Public entry point for the above, used when a match config supplies its own matchid.
        /// Creating the parent row at load time (rather than at the first map insert) means a match
        /// stopped during warmup or veto still has a row for SetMatchCancelled to close.
        /// </summary>
        public async Task<bool> EnsureMatchRowAsync(long matchId, string team1Name, string team2Name, string seriesType, string serverIp)
        {
            if (matchId <= 0)
                return false;
            try
            {
                using IDbConnection conn = CreateNewConnection();
                conn.Open();
                await EnsureMatchRowAsync(conn, matchId, team1Name, team2Name, "-", seriesType, serverIp);
                return true;
            }
            catch (Exception ex)
            {
                Log($"[EnsureMatchRow - FATAL] Error: {DescribeException(ex)}");
                return false;
            }
        }

        public async Task SetMatchEndDataAsync(long matchId, int mapNumber, string mapWinner, int team1Score, int team2Score, string matchWinner, int matchTeam1Score, int matchTeam2Score)
        {
            if (matchId == -1)
            {
                Log("[SetMatchEndData - ERROR] Invalid matchId: -1");
                return;
            }

            try
            {
                using IDbConnection conn = CreateNewConnection();
                conn.Open();

                // Update map data
                await conn.ExecuteAsync(
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
                await conn.ExecuteAsync(
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

        /// <summary>
        /// Close out the rows of a match that was stopped before it could finish (.stopmatch,
        /// css_endmatch, css_restart, an admin-menu stop, ...). Only SetMatchEndDataAsync used to
        /// write end_time, and it runs on the natural-end and surrender paths only, so a cancelled
        /// match left end_time NULL forever and looked like it was still running.
        ///
        /// winner is deliberately NOT written: a finished match always stores a team name or "Draw"
        /// (GetMatchWinnerName), so "end_time set + winner empty" is an unambiguous marker for
        /// "cancelled" that needs no new column and no new value for consumers to learn.
        ///
        /// Both UPDATEs require end_time IS NULL so a cancel that lands after a natural end can
        /// never overwrite the real result.
        /// </summary>
        public async Task SetMatchCancelledAsync(long matchId, int mapNumber, int team1Score, int team2Score, int matchTeam1Score, int matchTeam2Score)
        {
            if (matchId <= 0)
            {
                Log($"[SetMatchCancelled - ERROR] Invalid matchId: {matchId}");
                return;
            }

            try
            {
                using IDbConnection conn = CreateNewConnection();
                conn.Open();

                string endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                int mapRows = await conn.ExecuteAsync(
                    @"
                    UPDATE matchzy_stats_maps
                    SET end_time = @EndTime,
                        team1_score = @Team1Score,
                        team2_score = @Team2Score
                    WHERE matchid = @MatchId AND mapnumber = @MapNumber AND end_time IS NULL",
                    new
                    {
                        EndTime = endTime,
                        Team1Score = team1Score,
                        Team2Score = team2Score,
                        MatchId = matchId,
                        MapNumber = mapNumber,
                    }
                );

                await conn.ExecuteAsync(
                    @"
                    UPDATE matchzy_stats_matches
                    SET end_time = @EndTime,
                        team1_score = @Team1Score,
                        team2_score = @Team2Score
                    WHERE matchid = @MatchId AND end_time IS NULL",
                    new
                    {
                        EndTime = endTime,
                        Team1Score = matchTeam1Score,
                        Team2Score = matchTeam2Score,
                        MatchId = matchId,
                    }
                );

                if (mapRows > 0)
                {
                    Log($"[SetMatchCancelled] Match {matchId} map {mapNumber} closed as cancelled ({team1Score}-{team2Score})");
                }
                else
                {
                    Log($"[SetMatchCancelled] Match {matchId} map {mapNumber} already had end_time set, left untouched");
                }
            }
            catch (Exception ex)
            {
                Log($"[SetMatchCancelled - FATAL] Error: {ex.Message}");
            }
        }

        public async Task UpdateTeamNamesAsync(long matchId, string team1Name, string team2Name)
        {
            if (matchId <= 0)
                return;
            try
            {
                using IDbConnection conn = CreateNewConnection();
                conn.Open();
                await conn.ExecuteAsync(
                    @"UPDATE matchzy_stats_matches
                      SET team1_name = @Team1Name, team2_name = @Team2Name
                      WHERE matchid = @MatchId",
                    new
                    {
                        Team1Name = team1Name,
                        Team2Name = team2Name,
                        MatchId = matchId,
                    }
                );
            }
            catch (Exception ex)
            {
                Log($"[UpdateTeamNames - FATAL] Error: {ex.Message}");
            }
        }

        public async Task UpdatePlayerStatsAsync(long matchId, int mapNumber, Dictionary<long, Dictionary<string, object>> playerStatsDictionary)
        {
            if (matchId == -1)
            {
                Log("[UpdatePlayerStats - ERROR] Invalid matchId: -1");
                return;
            }

            if (playerStatsDictionary.Count == 0)
            {
                Log($"[UpdatePlayerStats] Nothing to write for match {matchId} map {mapNumber}: the caller collected no player stats.");
                return;
            }

            int written = 0;

            try
            {
                using IDbConnection conn = CreateNewConnection();
                conn.Open();

                foreach (var kvp in playerStatsDictionary)
                {
                    var steamid64 = kvp.Key;
                    var playerStats = kvp.Value;

                    string sqlQuery;
                    if (conn is SqliteConnection)
                    {
                        sqlQuery =
                            @"
                            INSERT INTO matchzy_stats_players (
                                matchid, mapnumber, steamid64, team, name, kills, deaths, assists, damage,
                                enemy5ks, enemy4ks, enemy3ks, enemy2ks, utility_count, utility_damage,
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
                                enemy5ks = excluded.enemy5ks,
                                enemy4ks = excluded.enemy4ks,
                                enemy3ks = excluded.enemy3ks,
                                enemy2ks = excluded.enemy2ks,
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
                                enemy5ks, enemy4ks, enemy3ks, enemy2ks, utility_count, utility_damage,
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
                                enemy5ks = VALUES(enemy5ks),
                                enemy4ks = VALUES(enemy4ks),
                                enemy3ks = VALUES(enemy3ks),
                                enemy2ks = VALUES(enemy2ks),
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

                    // Per-player try/catch: one row the server rejects (an unrepresentable
                    // character in a name under a legacy column charset, a stray value) used to
                    // abort the whole loop from the outer catch, so a single bad player discarded
                    // every other player's stats for that round.
                    try
                    {
                        await conn.ExecuteAsync(
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
                        written++;
                    }
                    catch (Exception rowEx)
                    {
                        Log($"[UpdatePlayerStats - ERROR] Skipped steamid {steamid64} (name '{playerStats["PlayerName"]}'): {DescribeException(rowEx)}");
                    }
                }

                if (written != playerStatsDictionary.Count)
                {
                    Log($"[UpdatePlayerStats] Wrote {written}/{playerStatsDictionary.Count} player rows for match {matchId} map {mapNumber}.");
                }
            }
            catch (Exception ex)
            {
                Log($"[UpdatePlayerStats - FATAL] Error inserting/updating data after {written} rows: {DescribeException(ex)}");
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
                using IDbConnection conn = CreateNewConnection();
                conn.Open();

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
                using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    IEnumerable<dynamic> playerStatsData = await conn.QueryAsync("SELECT * FROM matchzy_stats_players WHERE matchid = @MatchId AND mapnumber = @MapNumber ORDER BY team, kills DESC", new { MatchId = matchId, MapNumber = mapNumber });

                    // Use the first data row to get the column names
                    dynamic? firstDataRow = playerStatsData.FirstOrDefault();
                    if (firstDataRow != null)
                    {
                        foreach (var propertyName in ((IDictionary<string, object>)firstDataRow).Keys)
                        {
                            csv.WriteField(propertyName);
                        }
                        csv.NextRecord(); // End of the column names row

                        // Write data to the CSV file
                        foreach (var playerStats in playerStatsData)
                        {
                            foreach (var propertyValue in ((IDictionary<string, object>)playerStats).Values)
                            {
                                csv.WriteField(propertyValue);
                            }
                            csv.NextRecord();
                        }
                    }
                }
                Log($"[WritePlayerStatsToCsv] Match stats for ID: {matchId} written successfully at: {csvFilePath}");
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
            string defaultConfigJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            string? configDir = Path.GetDirectoryName(configFile);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            File.WriteAllText(configFile, defaultConfigJson);

            Log($"[InitializeDatabase] Default configuration file created at: {configFile}");
        }

        private void SetDatabaseConfig(string gameDirectory)
        {
            string fileName = "database.json";
            // Case-resolved like every other MatchZy file: a hardcoded lowercase path created a second
            // cfg folder on servers whose folder is named differently, and database.json then went missing.
            string configFile = Path.Combine(ConfigManager.ResolveMatchZyCfgDir(gameDirectory), fileName);
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
                Log($"[SetDatabaseConfig - ERROR] Error deserializing database.json: {ex.Message}. Using SQLite DB");
                databaseType = DatabaseType.SQLite;
            }
            catch (Exception ex)
            {
                Log($"[SetDatabaseConfig - ERROR] Unexpected error reading database.json: {ex.Message}. Using SQLite DB");
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
