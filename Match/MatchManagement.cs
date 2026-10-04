using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    public partial class MatchZy
    {
        public MatchConfig matchConfig = new();
        public bool isMatchSetup = false;
        public bool matchModeOnly = false;
        public bool resetCvarsOnSeriesEnd = true;
        public string loadedConfigFile = "";
        public bool isG5ApiMatch = false;

        public Team matchzyTeam1 = new() { teamName = "COUNTER-TERRORISTS" };

        public Team matchzyTeam2 = new() { teamName = "TERRORISTS" };

        public Dictionary<Team, string> teamSides = new();
        public Dictionary<string, Team> reverseTeamSides = new();
        public List<string> mapRotationList = new();

        [ConsoleCommand("css_team1", "Sets team name for team CT")]
        [ConsoleCommand("css_ctname", "Sets team name for team CT")]
        public void OnTeam1Command(CCSPlayerController? player, CommandInfo command)
        {
            HandleTeamNameChangeCommand(player, command.ArgString, 1);
        }

        [ConsoleCommand("css_team2", "Sets team name for Terrorist")]
        [ConsoleCommand("css_tname", "Sets team name for Terrorist")]
        public void OnTeam2Command(CCSPlayerController? player, CommandInfo command)
        {
            HandleTeamNameChangeCommand(player, command.ArgString, 2);
        }

        [ConsoleCommand("matchzy_loadmatch", "Loads a match from the given JSON file path (relative to the csgo/ directory)")]
        public void LoadMatch(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                if (player != null)
                    return;
                if (isMatchSetup)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.matchisalreadysetup", liveMatchId));
                    Log($"[LoadMatch] A match is already setup with id: {liveMatchId}, cannot load a new match!");
                    return;
                }

                string fileName = command.ArgString;
                string filePath = Path.Join(Server.GameDirectory + "/csgo", fileName);
                if (!File.Exists(filePath))
                {
                    // command.ReplyToCommand($"[LoadMatch] Provided file does not exist! Usage: matchzy_loadmatch <filename>");
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.filedoesntexist"));
                    Log($"[LoadMatch] Provided file does not exist! Usage: matchzy_loadmatch <filename>");
                    return;
                }

                string jsonData = File.ReadAllText(filePath);
                bool success = LoadMatchFromJSON(jsonData);
                // (LoadMatchFromJSON reports exceptions as a failed load, so this resets too.)
                if (!success)
                {
                    // command.ReplyToCommand("Match load failed! Resetting current match");
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.matchloadfailed"));
                    ResetMatch();
                }

                loadedConfigFile = fileName;
            }
            catch (Exception e)
            {
                Log($"[LoadMatch - FATAL] An error occured: {e.Message}");
                return;
            }
        }

        [ConsoleCommand("get5_loadmatch_url", "Loads a match from the given URL")]
        [ConsoleCommand("matchzy_loadmatch_url", "Loads a match from the given URL")]
        public void LoadMatchFromURL(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (isMatchSetup)
            {
                // command.ReplyToCommand($"[LoadMatchDataCommand] A match is already setup with id: {liveMatchId}, cannot load a new match!");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.get5matchisalreadysetup", liveMatchId));
                Log($"[LoadMatchDataCommand] A match is already setup with id: {liveMatchId}, cannot load a new match!");
                return;
            }

            string url = command.ArgByIndex(1);

            string headerName = command.ArgCount > 3 ? command.ArgByIndex(2) : "";
            string headerValue = command.ArgCount > 3 ? command.ArgByIndex(3) : "";

            // The header value is usually an auth token: never write it to the log.
            Log($"[LoadMatchDataCommand] Match setup request received with URL: {RedactUrl(url)} headerName: {headerName}{(headerValue != "" ? " (header value set)" : "")}");

            if (!IsValidUrl(url))
            {
                // command.ReplyToCommand($"[LoadMatchDataCommand] Invalid URL: {url}. Please provide a valid URL to load the match!");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.invalidurl", RedactUrl(url)));
                Log($"[LoadMatchDataCommand] Invalid URL: {RedactUrl(url)}. Please provide a valid URL to load the match!");
                return;
            }

            try
            {
                Log($"[LoadMatchFromURL] Fetching match config from URL...");

                // Fetch async to avoid blocking game thread
                Task.Run(async () =>
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        if (headerName != "")
                        {
                            request.Headers.TryAddWithoutValidation(headerName, headerValue);
                        }

                        using var response = await _sharedHttpClient.SendAsync(request);

                        if (response.IsSuccessStatusCode)
                        {
                            string jsonData = await response.Content.ReadAsStringAsync();
                            // Not the body itself: its cvars block can carry tokens (remote log header values).
                            Log($"[LoadMatchFromURL] Received match config ({jsonData.Length} characters).");

                            // LoadMatchFromJSON uses native APIs - must run on game thread
                            Server.NextFrame(() =>
                            {
                                // The issuer may have left during the fetch; reply to the console then.
                                if (player != null && !player.IsValid)
                                    player = null;
                                bool success = LoadMatchFromJSON(jsonData);
                                if (!success)
                                {
                                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.matchloadfailed"));
                                    ResetMatch();
                                    return;
                                }

                                loadedConfigFile = url;
                                isG5ApiMatch = true;
                                Log($"[LoadMatchFromURL] G5API match detected and marked");
                            });
                        }
                        else
                        {
                            var statusCode = response.StatusCode;
                            Server.NextFrame(() =>
                            {
                                if (player != null && !player.IsValid)
                                    player = null;
                                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.httprequestfailed", statusCode));
                            });
                            Log($"[LoadMatchFromURL] HTTP request failed with status code: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[LoadMatchFromURL - FATAL] Async fetch error: {ex.Message}");
                    }
                });
            }
            catch (Exception e)
            {
                Log($"[LoadMatchFromURL - FATAL] An error occured: {e.Message}");
                return;
            }
        }

        private string ValidateMatchJsonStructure(JObject jsonData)
        {
            string[] requiredFields = { "maplist", "team1", "team2", "num_maps" };

            // Check if any required field is missing
            foreach (string field in requiredFields)
            {
                if (jsonData[field] == null)
                {
                    return $"Missing mandatory field: {field}";
                }
            }

            foreach (var property in jsonData.Properties())
            {
                string field = property.Name;

                switch (field)
                {
                    case "matchid":
                        // Get5 allows the id as a number or a numeric string.
                        if (!long.TryParse(jsonData[field]!.ToString(), out _))
                        {
                            return $"{field} should be a number!";
                        }
                        break;
                    case "players_per_team":
                    case "min_players_to_ready":
                    case "min_spectators_to_ready":
                    case "num_maps":
                        int numMaps;
                        if (!int.TryParse(jsonData[field]!.ToString(), out numMaps))
                        {
                            return $"{field} should be an integer!";
                        }

                        if (field == "num_maps" && numMaps < 1)
                        {
                            return $"{field} should be at least 1!";
                        }

                        if (field == "num_maps" && numMaps > jsonData["maplist"]!.ToObject<List<string>>()!.Count)
                        {
                            return $"{field} should be equal to or greater than maplist!";
                        }

                        break;

                    case "cvars":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }

                        break;

                    case "team1":
                    case "team2":
                    case "spectators":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }

                        if (field != "spectators" && (jsonData[field]!["name"] == null || string.IsNullOrWhiteSpace(jsonData[field]!["name"]!.ToString())))
                        {
                            return $"{field} should have a 'name'!";
                        }

                        // A bot team needs no roster, and "players": "any" opens the team to any human.
                        if ((field != "spectators") && !IsBotTeamToken(jsonData[field]) && !IsOpenRosterToken(jsonData[field]!["players"])
                            && (jsonData[field]!["players"] == null
                                || (jsonData[field]!["players"]!.Type != JTokenType.Object && jsonData[field]!["players"]!.Type != JTokenType.Array)))
                        {
                            return $"{field} should have 'players' (an object keyed by SteamID64 or an array of SteamID64s), \"players\": \"any\", or \"bots\": true!";
                        }

                        break;

                    case "veto_mode":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }

                        break;

                    case "maplist":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }

                        if (!jsonData[field]!.Any())
                        {
                            return $"{field} should contain atleast 1 map!";
                        }

                        foreach (var mapEntry in jsonData[field]!)
                        {
                            if (!IsSafeMapName(mapEntry.ToString()))
                                return $"{field} contains an invalid map name: {mapEntry}";
                        }

                        break;
                    case "map_sides":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }

                        string[] allowedValues = { "team1_ct", "team1_t", "team2_ct", "team2_t", "knife" };
                        bool allElementsValid = jsonData[field]!.All(element => allowedValues.Contains(element.ToString()));

                        if (!allElementsValid)
                        {
                            return $"{field} should be \"team1_ct\", \"team1_t\", or \"knife\"!";
                        }

                        if (jsonData[field]!.ToObject<List<string>>()!.Count < jsonData["num_maps"]!.Value<int>())
                        {
                            return $"{field} should be equal to or greater than num_maps!";
                        }

                        break;

                    case "skip_veto":
                    case "clinch_series":
                    case "wingman":
                        // Same forms the loader accepts (ParseCvarBool): true/false and 1/0. bool.TryParse
                        // alone refused 1/0, so a config the loader could read was rejected here.
                        string boolText = jsonData[field]!.ToString().Trim();
                        if (!bool.TryParse(boolText, out _) && boolText != "1" && boolText != "0")
                        {
                            return $"{field} should be a boolean (true/false or 1/0)!";
                        }

                        break;
                }
            }

            return "";
        }

        public bool LoadMatchFromJSON(string jsonData, bool skipMapChange = false)
        {
            // Any exception (invalid JSON, e.g. an HTML error page served with status 200, or an
            // unexpected value) used to escape to the caller half-applied and without a reset.
            // Report it as a failed load instead; every caller then resets the match.
            try
            {
                return LoadMatchFromJSONCore(jsonData, skipMapChange);
            }
            catch (Exception e)
            {
                Log($"[LoadMatchFromJSON] The match config could not be loaded: {e.Message}");
                return false;
            }
        }

        private bool LoadMatchFromJSONCore(string jsonData, bool skipMapChange)
        {
            JObject jsonDataObject = JObject.Parse(jsonData);

            string validationError = ValidateMatchJsonStructure(jsonDataObject);

            if (validationError != "")
            {
                Log($"[LoadMatchDataCommand] {validationError}");
                return false;
            }

            // A new match: the previous series' end state must not block this one.
            // As in Get5: nobody is ready in a newly loaded match (a .ready typed before the load, or a
            // .forceready, carried over when the map did not change), and the game is not left paused.
            foreach (var key in playerReadyStatus.Keys.ToList())
                playerReadyStatus[key] = false;
            foreach (var team in teamReadyOverride.Keys.ToList())
                teamReadyOverride[team] = false;
            _readyStatusDirty = true;
            if (isPaused)
                UnpauseMatch();
            else if (GetGameRules()?.GamePaused == true)
                Server.ExecuteCommand("mp_unpause_match;"); // paused outside MatchZy (console)
            matchLoadGeneration++;
            seriesEnded = false;
            currentMapFinished = false;

            if (jsonDataObject["matchid"] != null)
            {
                long.TryParse(jsonDataObject["matchid"]!.ToString(), out long parsedId);
                // Only honor positive matchids. Zero/negative means "no existing
                // match" - let InitMatchAsync allocate a fresh autoincrement row.
                if (parsedId > 0)
                    liveMatchId = parsedId;
                else
                    Log($"[LoadMatchFromJSON] Ignoring invalid matchid={parsedId} from JSON; will allocate new.");
            }

            JToken team1 = jsonDataObject["team1"]!;
            JToken team2 = jsonDataObject["team2"]!;
            JToken maplist = jsonDataObject["maplist"]!;

            if (team1["id"] != null)
                matchzyTeam1.id = team1["id"]!.ToString();
            if (team2["id"] != null)
                matchzyTeam2.id = team2["id"]!.ToString();

            matchzyTeam1.teamName = RemoveSpecialCharacters(team1["name"]!.ToString());
            matchzyTeam2.teamName = RemoveSpecialCharacters(team2["name"]!.ToString());
            matchzyTeam1.teamTag = RemoveSpecialCharacters(team1["tag"]?.ToString() ?? "");
            matchzyTeam2.teamTag = RemoveSpecialCharacters(team2["tag"]?.ToString() ?? "");
            matchzyTeam1.teamPlayers = team1["players"] == null || team1["players"]!.Type == JTokenType.Null ? null : team1["players"];
            matchzyTeam2.teamPlayers = team2["players"] == null || team2["players"]!.Type == JTokenType.Null ? null : team2["players"];
            matchzyTeam1.teamCoaches = CoachRosterFrom(team1);
            matchzyTeam2.teamCoaches = CoachRosterFrom(team2);

            // Players-vs-bots keys (Match/BotTeam.cs). Set on every load: the Team objects outlive
            // a match, so a flag left over from the previous one would carry into this one.
            matchzyTeam1.botTeam = IsBotTeamToken(team1);
            matchzyTeam2.botTeam = IsBotTeamToken(team2);
            matchzyTeam1.botDifficulty = BotDifficultyFrom(team1);
            matchzyTeam2.botDifficulty = BotDifficultyFrom(team2);
            matchzyTeam1.openRoster = !matchzyTeam1.botTeam && IsOpenRosterToken(team1["players"]);
            matchzyTeam2.openRoster = !matchzyTeam2.botTeam && IsOpenRosterToken(team2["players"]);
            // The "any" marker is not a roster; the bot team has none.
            if (matchzyTeam1.openRoster || matchzyTeam1.botTeam)
                matchzyTeam1.teamPlayers = null;
            if (matchzyTeam2.openRoster || matchzyTeam2.botTeam)
                matchzyTeam2.teamPlayers = null;

            if (matchzyTeam1.botTeam && matchzyTeam2.botTeam)
            {
                Log("[LOADMATCH] team1 and team2 cannot both be bot teams.");
                return false;
            }
            if (matchzyTeam1.openRoster && matchzyTeam2.openRoster)
            {
                Log("[LOADMATCH] Only one team can use \"players\": \"any\" - leave both rosters out for a free-for-all join instead.");
                return false;
            }

            var loadedConfig = new MatchConfig
            {
                MatchId = liveMatchId,
                MapsPool = maplist.ToObject<List<string>>()!,
                MapsLeftInVetoPool = maplist.ToObject<List<string>>()!,
                NumMaps = jsonDataObject["num_maps"]!.Value<int>(),
                MinPlayersToReady = minimumReadyRequired,
            };
            // The remote log settings come from config.cfg / the console, not the match JSON. Start
            // from the server's own settings (not the previous match's, which a "cvars" block may
            // have changed); this match's "cvars" block can still override them when it runs.
            // Applied before publishing, so a concurrent event send never sees an empty URL.
            ApplyDefaultRemoteLog(loadedConfig);
            matchConfig = loadedConfig;

            GetOptionalMatchValues(jsonDataObject);

            if (matchConfig.MapsPool.Count == matchConfig.NumMaps)
            {
                matchConfig.SkipVeto = true;
                isPreVeto = false;
            }
            else if (matchConfig.MapsPool.Count < matchConfig.NumMaps)
            {
                Log($"[LOADMATCH] The map pool {matchConfig.MapsPool.Count} is not large enough to play a series of {matchConfig.NumMaps} maps.");
                return false;
            }

            // A bot team cannot take part in a veto, so a bot match plays a fixed map list:
            // maplist length == num_maps, or skip_veto true (the first num_maps maps are played).
            if (HasBotTeam() && !matchConfig.SkipVeto)
            {
                Log($"[LOADMATCH] A match with a bot team needs a fixed map list: give exactly num_maps maps ({matchConfig.NumMaps}) or set \"skip_veto\": true. Refusing to load.");
                return false;
            }
            if (HasBotTeam() && IsNoBotsFlagSet())
                Log("[LOADMATCH] WARNING: the server runs with -nobots, so the bot team will be empty.");

            if (!matchConfig.SkipVeto)
            {
                if (matchConfig.MapBanOrder.Count != 0)
                {
                    if (!ValidateMapBanLogic())
                        return false;
                }
                else
                {
                    GenerateDefaultVetoSetup();
                }
            }

            GetCvarValues(jsonDataObject);

            Log($"[LOADMATCH] MinPlayersToReady: {matchConfig.MinPlayersToReady} SeriesClinch: {matchConfig.SeriesCanClinch}");
            Log($"[LOADMATCH] MapsPool: {string.Join(", ", matchConfig.MapsPool)} MapsLeftInVetoPool: {string.Join(", ", matchConfig.MapsLeftInVetoPool)}");

            LoadClientNames();

            if (matchConfig.SkipVeto)
            {
                // Copy the first k maps from the maplist to the final match maps.
                for (int i = 0; i < matchConfig.NumMaps; i++)
                {
                    matchConfig.Maplist.Add(matchConfig.MapsPool[i]);

                    // Push a map side if one hasn't been set yet.
                    if (matchConfig.MapSides.Count < matchConfig.Maplist.Count)
                    {
                        if (matchConfig.MatchSideType == "standard" || matchConfig.MatchSideType == "always_knife")
                        {
                            matchConfig.MapSides.Add("knife");
                        }
                        else if (matchConfig.MatchSideType == "random")
                        {
                            matchConfig.MapSides.Add(new Random().Next(0, 2) == 0 ? "team1_ct" : "team1_t");
                        }
                        else
                        {
                            matchConfig.MapSides.Add("team1_ct");
                        }
                    }
                }

                // Bots cannot knife and pick a side, so a bot match draws each knife side at random.
                if (HasBotTeam())
                {
                    for (int i = 0; i < matchConfig.MapSides.Count; i++)
                    {
                        if (matchConfig.MapSides[i] == "knife")
                            matchConfig.MapSides[i] = new Random().Next(0, 2) == 0 ? "team1_ct" : "team1_t";
                    }
                }

                string currentMapName = Server.MapName;
                string mapName = matchConfig.Maplist[0].ToString();

                if (!skipMapChange && (IsMapReloadRequiredForGameMode(matchConfig.Wingman) || mapReloadRequired || currentMapName != mapName))
                {
                    SetCorrectGameMode();
                    // The match needs a different map than the one we're on. Finishing setup here is
                    // pointless: the changelevel ends this map -> OnMapEnd -> ResetMatch wipes it, and
                    // the match would arrive on the new map as "none" (get5_status none/null, default
                    // team names, no ready). Stash the config and changelevel; OnMapStart re-loads it
                    // on the target map (skipMapChange, currentMap == mapName -> no second changelevel),
                    // carrying the match across like get5. Return success so the caller still records
                    // loadedConfigFile / isG5ApiMatch (captured in OnMapEndHandler, restored on resume).
                    pendingMatchLoadJson = jsonData;
                    ChangeMap(mapName, 0);
                    return true;
                }
            }
            else
            {
                isPreVeto = true;
            }

            readyAvailable = true;

            // This is done before starting warmup so that cvars like get5_remote_log_url are set properly to send the events
            ExecuteChangedConvars();

            StartWarmup();
            isMatchSetup = true;
            // StartWarmup ran just before isMatchSetup was set, so start the no-show timer here.
            MarkReadyPhaseStarted();

            if (matchConfig.SkipVeto)
                SetMapSides();

            SetTeamNames();
            UpdatePlayersMap();
            // After SetMapSides: the bot team's side is only known from here on.
            ApplyBotTeam();
            // Only when the sides are known (no veto pending); after a veto FinishVeto sets them
            // and the jointeam handler places players as they pick a team.
            if (matchConfig.SkipVeto)
                PlaceMatchPlayers();

            // Eagerly allocate matchid right after config validates so liveMatchId
            // is set well before HandleMatchStart fires. Avoids -1 leaking into
            // events, demo filenames, and round-backup names during warmup/knife.
            // If JSON already supplied a valid matchid (positive), skip alloc.
            string seriesType = "BO" + matchConfig.NumMaps.ToString();
            string mapNameForInit = Server.MapName;
            string serverIpForInit = GetServerIpForStats();
            string team1NameForInit = matchzyTeam1.teamName;
            string team2NameForInit = matchzyTeam2.teamName;
            int currentMapNumForInit = matchConfig.CurrentMapNumber;
            long preassignedId = liveMatchId;

            int seriesNumMaps = matchConfig.NumMaps;
            string matchzyTeam1Id = matchzyTeam1.id;
            string matchzyTeam2Id = matchzyTeam2.id;
            string matchzyTeam1Name = matchzyTeam1.teamName;
            string matchzyTeam2Name = matchzyTeam2.teamName;

            int loadGeneration = matchLoadGeneration;
            // Taken now: series_start is sent after the database round trip.
            var seriesStartTarget = CurrentRemoteLogTarget();
            Task.Run(async () =>
            {
                long allocatedId = preassignedId;
                if (allocatedId <= 0)
                {
                    int[] backoffMs = { 0, 200, 500, 1000, 2000 };
                    for (int attempt = 0; attempt < backoffMs.Length; attempt++)
                    {
                        if (backoffMs[attempt] > 0)
                            await Task.Delay(backoffMs[attempt]);
                        allocatedId = await database.InitMatchAsync(team1NameForInit, team2NameForInit, "-", false, -1, currentMapNumForInit, seriesType, mapNameForInit, serverIpForInit);
                        if (allocatedId > 0)
                            break;
                        Log($"[LoadMatchFromJSON] eager alloc attempt {attempt + 1}/{backoffMs.Length} returned {allocatedId}, retrying...");
                    }
                }
                else
                {
                    // The JSON supplied the matchid, so nothing has allocated a parent row for it.
                    // Create it now: matchzy_stats_maps/_players carry a foreign key to
                    // matchzy_stats_matches, and it also gives .stopmatch during warmup/veto a row
                    // to close out.
                    await database.EnsureMatchRowAsync(allocatedId, team1NameForInit, team2NameForInit, seriesType, serverIpForInit);
                }

                Server.NextFrame(() =>
                {
                    if (loadGeneration != matchLoadGeneration)
                    {
                        Log($"[LoadMatchFromJSON] Discarding matchid {allocatedId}: the match was reset or replaced while it was being allocated.");
                        return;
                    }
                    // series_start only for a load that is still current (queued here, on the game
                    // thread, in order with the match's other events).
                    SendEventAsync(new MatchZySeriesStartedEvent
                    {
                        // The id the match actually runs under: a ready-up can have allocated one
                        // before this arrived (that one wins, see below).
                        MatchId = liveMatchId > 0 && liveMatchId != allocatedId ? liveMatchId : allocatedId,
                        NumberOfMaps = seriesNumMaps,
                        Team1 = new(matchzyTeam1Id, matchzyTeam1Name),
                        Team2 = new(matchzyTeam2Id, matchzyTeam2Name),
                    }, seriesStartTarget);
                    // HandleMatchStart may already have allocated one (ready-up before this finished).
                    if (liveMatchId > 0 && liveMatchId != allocatedId)
                    {
                        Log($"[LoadMatchFromJSON] Keeping matchid {liveMatchId}; the early allocation {allocatedId} arrived after the match had started.");
                        return;
                    }
                    if (allocatedId > 0)
                    {
                        liveMatchId = allocatedId;
                        Log($"[LoadMatchFromJSON] Success with matchid: {liveMatchId}");
                    }
                    else
                    {
                        Log("[LoadMatchFromJSON] WARNING: eager alloc failed; HandleMatchStart will retry.");
                    }
                });

            });

            return true;
        }

        public bool LockTeamsManually()
        {
            try
            {
                CsTeam team1 = teamSides[matchzyTeam1] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
                CsTeam team2 = teamSides[matchzyTeam2] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;

                Dictionary<ulong, string> team1Players = new();
                Dictionary<ulong, string> team2Players = new();
                Dictionary<ulong, string> spectatorPlayers = new();

                foreach (var key in playerData.Keys)
                {
                    if (!playerData[key].IsValid)
                        continue;
                    if (playerData[key].TeamNum == (int)team1)
                        team1Players.Add(playerData[key].SteamID, playerData[key].PlayerName);
                    else if (playerData[key].TeamNum == (int)team2)
                        team2Players.Add(playerData[key].SteamID, playerData[key].PlayerName);
                    else if (playerData[key].TeamNum == (int)CsTeam.Spectator)
                        spectatorPlayers.Add(playerData[key].SteamID, playerData[key].PlayerName);
                }

                matchzyTeam1.teamPlayers = JToken.FromObject(team1Players);
                matchzyTeam2.teamPlayers = JToken.FromObject(team2Players);
                matchConfig.Spectators = JToken.FromObject(spectatorPlayers);
            }
            catch (Exception e)
            {
                Log($"[LockTeamsManually - FATAL] An error occured: {e.Message}");
                return false;
            }

            return true;
        }

        public void SaveMatchToJSON(string fileName = "")
        {
            try
            {
                if (!isMatchSetup)
                {
                    Log("[SaveMatchToJSON] No match is currently setup to save!");
                    return;
                }

                var matchData = new JObject();

                matchData["matchid"] = liveMatchId;
                matchData["num_maps"] = matchConfig.NumMaps;
                matchData["players_per_team"] = matchConfig.PlayersPerTeam;
                matchData["min_players_to_ready"] = matchConfig.MinPlayersToReady;
                matchData["min_spectators_to_ready"] = matchConfig.MinSpectatorsToReady;
                matchData["skip_veto"] = matchConfig.SkipVeto;
                matchData["clinch_series"] = matchConfig.SeriesCanClinch;
                matchData["wingman"] = matchConfig.Wingman;

                var team1Data = new JObject();
                team1Data["id"] = matchzyTeam1.id;
                team1Data["name"] = matchzyTeam1.teamName;
                team1Data["players"] = matchzyTeam1.teamPlayers;
                if (matchzyTeam1.teamCoaches != null)
                    team1Data["coaches"] = matchzyTeam1.teamCoaches;
                matchData["team1"] = team1Data;

                var team2Data = new JObject();
                team2Data["id"] = matchzyTeam2.id;
                team2Data["name"] = matchzyTeam2.teamName;
                team2Data["players"] = matchzyTeam2.teamPlayers;
                if (matchzyTeam2.teamCoaches != null)
                    team2Data["coaches"] = matchzyTeam2.teamCoaches;
                matchData["team2"] = team2Data;

                if (matchConfig.Spectators != null)
                {
                    var spectatorsData = new JObject();
                    spectatorsData["players"] = matchConfig.Spectators;
                    matchData["spectators"] = spectatorsData;
                }

                matchData["maplist"] = JArray.FromObject(matchConfig.MapsPool);

                if (matchConfig.MapSides != null && matchConfig.MapSides.Count > 0)
                {
                    matchData["map_sides"] = JArray.FromObject(matchConfig.MapSides);
                }

                if (matchConfig.MapBanOrder != null && matchConfig.MapBanOrder.Count > 0)
                {
                    matchData["veto_mode"] = JArray.FromObject(matchConfig.MapBanOrder);
                }

                if (matchConfig.ChangedCvars != null && matchConfig.ChangedCvars.Count > 0)
                {
                    var cvarsData = new JObject();
                    foreach (var cvar in matchConfig.ChangedCvars)
                    {
                        cvarsData[cvar.Key] = cvar.Value;
                    }
                    matchData["cvars"] = cvarsData;
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    fileName = $"match_{liveMatchId}_{timestamp}.json";
                }

                string filePath = Path.Join(Server.GameDirectory + "/csgo", fileName);

                string jsonString = matchData.ToString(Formatting.Indented);
                File.WriteAllText(filePath, jsonString);

                Log($"[SaveMatchToJSON] Match configuration saved to: {fileName}");
            }
            catch (Exception e)
            {
                Log($"[SaveMatchToJSON - FATAL] An error occurred: {e.Message}");
            }
        }

        public void SetMapSides()
        {
            int mapNumber = matchConfig.CurrentMapNumber;
            if (matchConfig.MapSides[mapNumber] == "team1_ct" || matchConfig.MapSides[mapNumber] == "team2_t")
            {
                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                isKnifeRequired = false;
            }
            else if (matchConfig.MapSides[mapNumber] == "team2_ct" || matchConfig.MapSides[mapNumber] == "team1_t")
            {
                teamSides[matchzyTeam2] = "CT";
                teamSides[matchzyTeam1] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam2;
                reverseTeamSides["TERRORIST"] = matchzyTeam1;
                isKnifeRequired = false;
            }
            else if (matchConfig.MapSides[mapNumber] == "knife")
            {
                // The knife round decides who plays which side, but teamSides/reverseTeamSides
                // still have to be seeded here. They are session-persistent dictionaries, so the
                // branch that used to only set isKnifeRequired left the PREVIOUS match's mapping
                // in place: after a halftime or a knife .switch that mapping is inverted, and
                // after a round restore its keys can point at Team objects that no longer exist.
                // GetPlayerTeam reads them to place every single player, so a carry-over either
                // puts both teams on the wrong side or (on a stale key) resolves everyone to
                // CsTeam.None, which drops them from playerData and leaves the server unlocked.
                // Seed the canonical pre-knife layout; the .stay/.switch handlers swap from here.
                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                isKnifeRequired = true;
            }

            SetTeamNames();
        }

        public void SetTeamNames()
        {
            string ctName = reverseTeamSides["CT"].teamName;
            string tName = reverseTeamSides["TERRORIST"].teamName;

            // Vanilla mode: auto-naming disabled and the teams still carry the scrim defaults (a
            // Get5/JSON match has explicit names and skips this) - keep mp_teamname_1/2 empty so the
            // scoreboard shows the game's own names, incl. on halftime/knife-swap re-applies.
            if (!autoTeamNamesEnabled.Value && ctName == "COUNTER-TERRORISTS" && tName == "TERRORISTS")
            {
                Server.ExecuteCommand("mp_teamname_1 \"\"; mp_teamname_2 \"\"");
                return;
            }

            // The engine swaps what mp_teamname_1/mp_teamname_2 display on the scoreboard
            // each time sides switch (halftime, knife swap, OT halftime).
            // Default: mp_teamname_1 → CT scoreboard, mp_teamname_2 → T scoreboard
            // After swap: mp_teamname_1 → T scoreboard, mp_teamname_2 → CT scoreboard
            if (isConvarMappingSwapped)
            {
                Server.ExecuteCommand($"mp_teamname_1 {TeamNameArg(tName)}; mp_teamname_2 {TeamNameArg(ctName)}");
            }
            else
            {
                Server.ExecuteCommand($"mp_teamname_1 {TeamNameArg(ctName)}; mp_teamname_2 {TeamNameArg(tName)}");
            }

            // Also set directly on CCSTeam entities for reliability.
            // Use the cached CCSTeam refs and only rescan on a miss. The refs can be stale right
            // after a changelevel (SetTeamNames may run before the post-map RefreshTeamEntities
            // timer fires) and a stale handle NREs on the ClanTeamname setter, which is what the
            // IsValid check plus RefreshTeamEntities covers. The old unconditional
            // FindAllEntitiesByDesignerName walked the whole entity list, reading DesignerName out
            // of native memory for every entity, twice per round on the game thread (inline at
            // round_start and again from the 0.5 s timer).
            try
            {
                if (_cachedCtTeam == null || !_cachedCtTeam.IsValid || _cachedTTeam == null || !_cachedTTeam.IsValid)
                    RefreshTeamEntities();
                if (_cachedCtTeam != null && _cachedCtTeam.IsValid)
                    _cachedCtTeam.ClanTeamname = ctName;
                if (_cachedTTeam != null && _cachedTTeam.IsValid)
                    _cachedTTeam.ClanTeamname = tName;
            }
            catch (Exception e)
            {
                Log($"[SetTeamNames] Entity approach failed (non-fatal): {e.Message}");
            }
        }

        public void GetCvarValues(JObject jsonDataObject)
        {
            try
            {
                if (jsonDataObject["cvars"] == null)
                    return;

                foreach (JProperty cvarData in jsonDataObject["cvars"]!)
                {
                    string cvarName = cvarData.Name;
                    string cvarValue = cvarData.Value.ToString();

                    // Each entry becomes a console command, so only real convars (and MatchZy/Get5
                    // settings) with plain values are accepted. A key such as "quit" or a value
                    // containing a quote or ';' could otherwise run any console command from the
                    // match JSON.
                    if (!IsAllowedMatchCvar(cvarName, cvarValue, out string rejectReason))
                    {
                        Log($"[GetCvarValues] Ignoring cvar '{cvarName}' from the match config: {rejectReason}");
                        continue;
                    }

                    var cvar = ConVar.Find(cvarName);
                    matchConfig.ChangedCvars[cvarName] = cvarValue;
                    if (cvar != null)
                    {
                        // An empty read of a non-string convar means the value could not be read;
                        // restoring "" would set it to 0 after the series, so keep the server value.
                        string original = GetConvarStringValue(cvar);
                        if (original != "" || cvar.Type == ConVarType.String)
                            matchConfig.OriginalCvars[cvarName] = original;
                    }
                    else if (PluginSettingAccessors.TryGetValue(cvarName, out var accessor))
                    {
                        // MatchZy settings that are console commands have no convar to read back, so
                        // remember the server's own value; otherwise the match's value stayed on the
                        // server after the series (e.g. matchzy_kick_when_no_match_loaded).
                        matchConfig.OriginalCvars[cvarName] = accessor.Get();
                        // get5_max_tech_pauses / get5_tech_pause_time also switch on Get5 pause rules;
                        // remember the server's mode so the series end puts it back.
                        if (cvarName.Equals("get5_max_tech_pauses", StringComparison.OrdinalIgnoreCase)
                            || cvarName.Equals("get5_tech_pause_time", StringComparison.OrdinalIgnoreCase)
                            || cvarName.Equals("matchzy_max_tech_pauses", StringComparison.OrdinalIgnoreCase)
                            || cvarName.Equals("matchzy_tech_pause_time", StringComparison.OrdinalIgnoreCase))
                            matchConfig.OriginalCvars.TryAdd("matchzy_tech_pause_mode", techPauseMode.Value.ToString());
                    }
                    else if (GetFakeConVarValue(cvarName) is string fakeValue)
                    {
                        // FakeConVar settings (matchzy_enable_tech_pause, ...) are not engine convars
                        // either; without this their match value stayed on the server after the series.
                        matchConfig.OriginalCvars[cvarName] = fakeValue;
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[GetCvarValues FATAL] An error occurred: {e.Message}");
            }
        }

        public void GetOptionalMatchValues(JObject jsonDataObject)
        {
            if (jsonDataObject["map_sides"] != null)
            {
                matchConfig.MapSides = jsonDataObject["map_sides"]!.ToObject<List<string>>()!;
            }

            if (jsonDataObject["players_per_team"] != null)
            {
                matchConfig.PlayersPerTeam = jsonDataObject["players_per_team"]!.Value<int>();
            }

            if (jsonDataObject["min_players_to_ready"] != null)
            {
                matchConfig.MinPlayersToReady = jsonDataObject["min_players_to_ready"]!.Value<int>();
            }

            if (jsonDataObject["min_spectators_to_ready"] != null)
            {
                matchConfig.MinSpectatorsToReady = jsonDataObject["min_spectators_to_ready"]!.Value<int>();
            }

            if (jsonDataObject["spectators"] != null && jsonDataObject["spectators"]!["players"] != null)
            {
                matchConfig.Spectators = jsonDataObject["spectators"]!["players"]!;
                if (matchConfig.Spectators is JArray spectatorsArray && spectatorsArray.Count == 0)
                {
                    // Convert the empty JArray to an empty JObject
                    matchConfig.Spectators = new JObject();
                }
            }

            if (jsonDataObject["clinch_series"] != null)
            {
                matchConfig.SeriesCanClinch = ParseCvarBool(jsonDataObject["clinch_series"]!.ToString(), matchConfig.SeriesCanClinch);
            }

            // Get5 side_type: standard / always_knife / never_knife (and MatchZy's random).
            if (jsonDataObject["side_type"] != null)
            {
                string sideType = jsonDataObject["side_type"]!.ToString().Trim().ToLowerInvariant();
                if (sideType is "standard" or "always_knife" or "never_knife" or "random")
                    matchConfig.MatchSideType = sideType;
                else
                    Log($"[LOADMATCH] Unknown side_type '{sideType}', using standard.");
            }

            // Get5 veto_first: team1 / team2 / random. Decides who starts a generated veto.
            vetoFirstTeam = null;
            if (jsonDataObject["veto_first"] != null)
            {
                string vetoFirst = jsonDataObject["veto_first"]!.ToString().Trim().ToLowerInvariant();
                vetoFirstTeam = vetoFirst switch
                {
                    "team1" => matchzyTeam1,
                    "team2" => matchzyTeam2,
                    "random" => new Random().Next(0, 2) == 0 ? matchzyTeam1 : matchzyTeam2,
                    _ => null,
                };
            }

            if (jsonDataObject["skip_veto"] != null)
            {
                matchConfig.SkipVeto = ParseCvarBool(jsonDataObject["skip_veto"]!.ToString(), matchConfig.SkipVeto);
            }

            if (jsonDataObject["wingman"] != null)
            {
                matchConfig.Wingman = ParseCvarBool(jsonDataObject["wingman"]!.ToString(), matchConfig.Wingman);
            }

            if (jsonDataObject["veto_mode"] != null)
            {
                matchConfig.MapBanOrder = jsonDataObject["veto_mode"]!.ToObject<List<string>>()!;
            }
        }

        public void HandleTeamNameChangeCommand(CCSPlayerController? player, string teamName, int teamNum)
        {
            if (!IsPlayerAdmin(player, "css_team", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            // if (matchStarted)
            // {
            //     // ReplyToUserCommand(player, "Team names cannot be changed once the match is started!");
            //     ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.teamcannotbechanged"));
            //     return;
            // }

            teamName = RemoveSpecialCharacters(teamName.Trim());

            if (teamName == "")
            {
                // ReplyToUserCommand(player, $"Usage: !team{teamNum} <name>");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!team{teamNum} <name>"));
                return;
            }

            string oldTeamName = "";
            if (teamNum == 1)
            {
                // teamNum 1 = CT side command: find whichever team is currently CT
                Team ctTeam = reverseTeamSides["CT"];
                oldTeamName = ctTeam.teamName;
                ctTeam.teamName = teamName;
                foreach (var coach in ctTeam.coach)
                {
                    ApplyClanTag(coach, $"[{ctTeam.teamName} COACH]");
                }
            }
            else if (teamNum == 2)
            {
                // teamNum 2 = T side command: find whichever team is currently T
                Team tTeam = reverseTeamSides["TERRORIST"];
                oldTeamName = tTeam.teamName;
                tTeam.teamName = teamName;
                foreach (var coach in tTeam.coach)
                {
                    ApplyClanTag(coach, $"[{tTeam.teamName} COACH]");
                }
            }

            SetTeamNames();

            // Propagate rename to DB so matchzy_stats_matches.team{1,2}_name
            // matches in-memory state. Snapshot before Task.Run to avoid
            // native-thread access.
            long matchIdSnap = liveMatchId;
            string t1Snap = matchzyTeam1.teamName;
            string t2Snap = matchzyTeam2.teamName;
            if (matchIdSnap > 0)
            {
                Task.Run(() => database.UpdateTeamNamesAsync(matchIdSnap, t1Snap, t2Snap));
            }

            // Add confirmation message
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.mm.teamchanged", oldTeamName == "" ? $"Team {teamNum}" : oldTeamName, teamName));

            // Refresh the wizard's Confirm menu so the new team name is visible
            // immediately. Defer one frame so the chat broadcast doesn't clobber
            // the menu render (same race ChatMenu has with EventPlayerChat).
            if (player != null && activeSetup != null && activeSetup.AdminSteamId == player.SteamID)
            {
                Server.NextFrame(() => OpenConfirmMenu(player));
            }
        }

        public void SwapSidesInTeamData(bool swapTeams)
        {
            // Surrender votes are kept per side; a side swap must not carry them to the other team.
            ResetGGVotes();
            (teamSides[matchzyTeam1], teamSides[matchzyTeam2]) = (teamSides[matchzyTeam2], teamSides[matchzyTeam1]);
            (reverseTeamSides["CT"], reverseTeamSides["TERRORIST"]) = (reverseTeamSides["TERRORIST"], reverseTeamSides["CT"]);

            // The engine also swaps what mp_teamname_1/mp_teamname_2 map to on the scoreboard
            isConvarMappingSwapped = !isConvarMappingSwapped;
        }

        // Returns true when at least one team has a non-empty player whitelist.
        // When false (e.g. matches created via .matchsetup wizard), team locking is bypassed
        // so players can freely choose T/CT without being forced back to spectator.
        /// <summary>
        /// True when the side already has players_per_team players (humans, no coaches, not
        /// counting <paramref name="except"/>). A roster may list substitutes; only
        /// players_per_team of them play at once.
        /// </summary>
        private bool IsSideFull(CsTeam side, CCSPlayerController? except)
        {
            if (side is not (CsTeam.Terrorist or CsTeam.CounterTerrorist) || matchConfig.PlayersPerTeam <= 0)
                return false;
            int count = 0;
            foreach (var p in Utilities.GetPlayers())
            {
                if (p == null || !p.IsValid || p.IsBot || p.IsHLTV || p.Team != side)
                    continue;
                if (except != null && p == except)
                    continue;
                if (IsMatchCoach(p))
                    continue;
                count++;
            }
            return count >= matchConfig.PlayersPerTeam;
        }

        private bool IsTeamWhitelistConfigured()
        {
            return RosterSize(matchzyTeam1.teamPlayers) > 0 || RosterSize(matchzyTeam2.teamPlayers) > 0
                || OpenRosterTeam() != null || HasBotTeam();
        }

        // Counts a roster token in either supported shape. Counting only JObject meant an
        // array-form roster read as "no whitelist" and quietly disabled team locking.
        private static int RosterSize(JToken? roster)
        {
            return roster switch
            {
                JObject rosterObject => rosterObject.Count,
                JArray rosterArray => rosterArray.Count,
                _ => 0,
            };
        }

        /// <summary>
        /// Resolves the side a rostered player belongs on. Returns CsTeam.None when the player is
        /// not in the match config - callers treat that as "not part of this match".
        ///
        /// None is also what a lookup failure degrades to, and that is expensive: every caller
        /// (EventPlayerConnectFull, UpdatePlayersMap, the jointeam listener) skips a None player,
        /// so a systematic failure here empties playerData, silently un-enforces team locking for
        /// the whole server and leaves matchzy_stats_players empty for the match. Hence TryGetValue
        /// plus a loud log rather than a bare indexer inside a catch-all.
        /// </summary>
        private CsTeam GetPlayerTeam(CCSPlayerController player)
        {
            var steamId = player.SteamID;
            try
            {
                // The bot team never takes a human, even one listed on its roster.
                Team? rosteredTeam = null;
                if (!matchzyTeam1.botTeam && LookupRosterEntry(matchzyTeam1.teamPlayers, steamId))
                    rosteredTeam = matchzyTeam1;
                else if (!matchzyTeam2.botTeam && LookupRosterEntry(matchzyTeam2.teamPlayers, steamId))
                    rosteredTeam = matchzyTeam2;
                else if (GetRosteredCoachTeam(steamId) is Team coachTeam)
                    rosteredTeam = coachTeam;
                else if (LookupRosterEntry(matchConfig.Spectators, steamId))
                    return CsTeam.Spectator;
                else
                    rosteredTeam = OpenRosterTeam();

                if (rosteredTeam == null)
                    return CsTeam.None;

                if (!teamSides.TryGetValue(rosteredTeam, out string? side))
                {
                    // teamSides is keyed by Team reference and has no entry for this object: the
                    // side mapping was never seeded for this match, or the Team instances were
                    // replaced (round restore) without re-registering them. Recover with the
                    // canonical layout instead of dropping every player out of the match.
                    Log($"[GetPlayerTeam - ERROR] No side registered for team '{rosteredTeam.teamName}'; reseeding default sides (team1=CT, team2=T).");
                    teamSides[matchzyTeam1] = "CT";
                    teamSides[matchzyTeam2] = "TERRORIST";
                    reverseTeamSides["CT"] = matchzyTeam1;
                    reverseTeamSides["TERRORIST"] = matchzyTeam2;
                    side = teamSides[rosteredTeam];
                }

                return side == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            }
            catch (Exception ex)
            {
                Log($"[GetPlayerTeam - FATAL] Exception occurred for {steamId}: {ex.Message}");
                return CsTeam.None;
            }
        }

        /// <summary>
        /// True when the steamid appears in a roster token. Accepts both shapes MatchZy match
        /// configs use: a JObject keyed by steamid ({"765...": "Name"}) and a plain JArray of
        /// steamids. A JArray indexed with a string key throws, which took the whole lookup down.
        /// </summary>
        private static bool LookupRosterEntry(JToken? roster, ulong steamId)
        {
            if (roster == null)
                return false;
            string key = steamId.ToString();
            if (roster is JObject rosterObject)
                return rosterObject[key] != null;
            // Get5 panels often write SteamID64s as bare numbers, so accept integers as well as strings.
            if (roster is JArray rosterArray)
                return rosterArray.Any(entry => (entry.Type == JTokenType.String || entry.Type == JTokenType.Integer) && entry.ToString() == key);
            return false;
        }

        // Set by EndSeries until the match is reset: the series is over and must not be ended again
        // (a second series_end, and the winner in the database overwritten).
        public bool seriesEnded = false;

        // Bumped by every match load and reset. Async work started for one match (the matchid
        // allocation at load) compares it before writing match state, so a .stopmatch or a new load
        // during the database round trip cannot receive the previous match's id.
        private int matchLoadGeneration = 0;

        // True from the end of a map until the next map goes live. Between the maps of a series there
        // is no map in progress, so ending the series then must not write end data for a map row.
        private bool currentMapFinished = false;

        public void EndSeries(string? winnerName, int restartDelay, int t1score, int t2score, bool writeEndData = true, bool noWinner = false)
        {
            if (seriesEnded)
            {
                Log($"[EndSeries] Series {liveMatchId} has already ended; ignoring the second end.");
                return;
            }
            seriesEnded = true;
            long matchId = liveMatchId;
            (int team1Score, int team2Score) = (matchzyTeam1.seriesScore, matchzyTeam2.seriesScore);
            if (winnerName == "Draw")
                winnerName = null;
            if (winnerName == null)
            {
                PrintLocalizedToAll("matchzy.matchmsg.seriestied", matchzyTeam1.teamName, matchzyTeam2.teamName);
            }
            else
            {
                PrintLocalizedToAll("matchzy.matchmsg.serieswon", winnerName);
            }

            var seriesResultEvent = new MatchZySeriesResultEvent()
            {
                MatchId = matchId,
                // A forfeit or surrender names its winner even when the series score does not show
                // one (1-1 in a BO3); otherwise use the series score.
                // noWinner: a tie by decision (nobody ready in time), whatever the series score says.
                Winner = noWinner ? BuildWinnerFor(null)
                    : winnerName == matchzyTeam1.teamName ? BuildWinnerFor(matchzyTeam1)
                    : winnerName == matchzyTeam2.teamName ? BuildWinnerFor(matchzyTeam2)
                    : BuildWinner(team1Score, team2Score),
                Team1SeriesScore = team1Score,
                Team2SeriesScore = team2Score,
                TimeUntilRestore = 10,
            };

            // -1 = no map in progress (between maps): SetMatchEndDataAsync then leaves the map rows alone.
            // Queued now, on the game thread: the event queue sends it after the map_result queued
            // before it (it used to wait for that task plus 500 ms on a background thread).
            PublishEvent(seriesResultEvent);
            int currentMapNumber = currentMapFinished ? -1 : matchConfig.CurrentMapNumber;
            Task.Run(async () =>
            {
                // HandleMatchEnd already wrote end data for this map; writing again here
                // duplicated the update and could clobber the map row's scores with series scores.
                if (writeEndData)
                {
                    // Map columns get this map's round score, the match columns the series score
                    // (both used to receive the series score).
                    await database.SetMatchEndDataAsync(matchId, currentMapNumber, winnerName ?? "Draw", t1score, t2score, winnerName ?? "Draw", team1Score, team2Score);
                }
            });

            // FIRST: Disable engine auto-change BEFORE restoring cvars - prevents race condition
            // where ResetChangedConvars re-enables them and engine races the plugin's map change.
            Server.ExecuteCommand("mp_match_end_changelevel 0");
            Server.ExecuteCommand("mp_match_end_restart 0");
            Server.ExecuteCommand("mp_endmatch_votenextmap 0");

            if (resetCvarsOnSeriesEnd)
                ResetChangedConvars();
            isMatchLive = false;
            isConvarMappingSwapped = false;

            // Re-enforce AFTER convar reset in case ResetChangedConvars restored them
            Server.ExecuteCommand("mp_match_end_changelevel 0");
            Server.ExecuteCommand("mp_match_end_restart 0");

            // Check if auto changelevel should be disabled
            bool shouldDisableAutoChangelevel = !matchEndAutoChangelevel.Value || isG5ApiMatch;

            if (isG5ApiMatch)
            {
                Log($"[EndSeries] G5API match detected - disabling auto changelevel to allow G5 to manage map rotation");
            }

            if (shouldDisableAutoChangelevel)
            {
                Log($"[EndSeries] Auto changelevel is disabled (ConVar: {matchEndAutoChangelevel.Value}, G5API: {isG5ApiMatch})");
                // Stay on map → keep GOTV/CSTV relay alive so spectators can watch to the end.
                // The old fork cfg dropped sv_hibernate_postgame_delay to 5 (upstream is 300),
                // so the server hibernated 5s postgame once only spectators remained → CSTV
                // disconnect. Restore the 300s grace at match end so the broadcast survives.
                Server.ExecuteCommand("sv_hibernate_postgame_delay 300");
                AddTimer(
                    restartDelay,
                    () =>
                    {
                        ResetMatch(false);
                    }
                );
                return;
            }

            // For last map (series end), change after exactly 15 seconds
            float mapChangeDelay = 15.0f;

            // Guard against empty map rotation
            if (mapRotationList.Count == 0)
            {
                Log("[EndSeries] WARNING: Map rotation list is empty! Cannot auto-changelevel. Resetting match on current map.");
                PrintLocalizedToAll("matchzy.matchmsg.norotationmaps");
                AddTimer(
                    restartDelay,
                    () =>
                    {
                        ResetMatch(false);
                    }
                );
                return;
            }

            // Get the next map in rotation
            string currentMap = Server.MapName;
            int currentIndex = mapRotationList.IndexOf(currentMap);
            string nextMap = currentIndex >= 0 && currentIndex < mapRotationList.Count - 1 ? mapRotationList[currentIndex + 1] : mapRotationList[0];

            Log($"[EndSeries] Current map: {currentMap}, Next map: {nextMap}, Change in {mapChangeDelay}s");

            // Notify players
            PrintLocalizedToAll("matchzy.matchmsg.nextmap", nextMap, (int)mapChangeDelay);

            matchEndMapChangeTimer = AddTimer(
                mapChangeDelay,
                () =>
                {
                    // Reset match state FIRST, then change map - serialized to avoid race condition
                    ResetMatch(false);

                    // Use the appropriate command based on map type
                    ChangeMapFromRotation(nextMap);

                    matchEndMapChangeTimer = null;
                }
            );
        }

        private void ChangeMapFromRotation(string mapName)
        {
            // Ensure demo is stopped before map change to prevent GOTV flush crash
            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
            }

            // Prevent engine from racing us with its own map change
            Server.ExecuteCommand("mp_match_end_changelevel 0");
            Server.ExecuteCommand("mp_match_end_restart 0");
            Server.ExecuteCommand("mp_endmatch_votenextmap 0");
            KickAllBotsProtectCSTV();

            // Check if it's a workshop map (starts with "workshop/" or is just a numeric ID)
            bool isWorkshopMap = mapName.StartsWith("workshop/") || long.TryParse(mapName, out _);

            // Execute on next frame for engine state safety
            Server.NextFrame(() =>
            {
                if (isWorkshopMap)
                {
                    string workshopId = mapName;
                    if (mapName.StartsWith("workshop/"))
                    {
                        string[] parts = mapName.Split('/');
                        if (parts.Length >= 2)
                        {
                            workshopId = parts[1];
                        }
                    }

                    Log($"[ChangeMapFromRotation] Workshop map, using host_workshop_map: {workshopId}");
                    Server.ExecuteCommand($"host_workshop_map {workshopId}");
                }
                else
                {
                    Log($"[ChangeMapFromRotation] Standard map, using changelevel: {mapName}");
                    Server.ExecuteCommand($"changelevel {mapName}");
                }
            });
        }

        public void HandlePlayoutConfig()
        {
            // A value set in the match config's "cvars" block wins over the mode defaults below;
            // e.g. "mp_overtime_enable": "0" for draws used to be overwritten with live.cfg's 1.
            bool fromMatchConfig(string cvar) => matchConfig.ChangedCvars.ContainsKey(cvar);

            if (isPlayOutEnabled || isPlayOutEnabled2)
            {
                if (!fromMatchConfig("mp_overtime_enable"))
                    Server.ExecuteCommand("mp_overtime_enable 0");
                if (!fromMatchConfig("mp_match_can_clinch"))
                    Server.ExecuteCommand("mp_match_can_clinch 0");
                Server.ExecuteCommand("mp_match_end_changelevel 0");
                Server.ExecuteCommand("mp_match_end_restart 0");
                Server.ExecuteCommand("mp_endmatch_votenextmap 0");
                return;
            }

            var absoluteCfgPath = Path.Join(Server.GameDirectory + "/csgo/cfg", GetGameMode() == 1 ? liveCfgPath : liveWingmanCfgPath);
            string? matchCanClinch = GetConvarValueFromCFGFile(absoluteCfgPath, "mp_match_can_clinch");
            string? overtimeEnabled = GetConvarValueFromCFGFile(absoluteCfgPath, "mp_overtime_enable");
            if (!fromMatchConfig("mp_match_can_clinch"))
                Server.ExecuteCommand($"mp_match_can_clinch {matchCanClinch ?? "1"}");
            if (!fromMatchConfig("mp_overtime_enable"))
                Server.ExecuteCommand($"mp_overtime_enable {overtimeEnabled ?? "1"}");
        }
    }
}
