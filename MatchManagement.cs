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

        [ConsoleCommand(
            "matchzy_loadmatch",
            "Loads a match from the given JSON file path (relative to the csgo/ directory)"
        )]
        public void LoadMatch(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                if (player != null)
                    return;
                if (isMatchSetup)
                {
                    ReplyToUserCommand(
                        player,
                        Localizer.ForPlayer(player, "matchzy.mm.matchisalreadysetup", liveMatchId)
                    );
                    Log(
                        $"[LoadMatch] A match is already setup with id: {liveMatchId}, cannot load a new match!"
                    );
                    return;
                }

                string fileName = command.ArgString;
                string filePath = Path.Join(Server.GameDirectory + "/csgo", fileName);
                if (!File.Exists(filePath))
                {
                    // command.ReplyToCommand($"[LoadMatch] Provided file does not exist! Usage: matchzy_loadmatch <filename>");
                    ReplyToUserCommand(
                        player,
                        Localizer.ForPlayer(player, "matchzy.mm.filedoesntexist")
                    );
                    Log(
                        $"[LoadMatch] Provided file does not exist! Usage: matchzy_loadmatch <filename>"
                    );
                    return;
                }

                string jsonData = File.ReadAllText(filePath);
                bool success = LoadMatchFromJSON(jsonData);
                if (!success)
                {
                    // command.ReplyToCommand("Match load failed! Resetting current match");
                    ReplyToUserCommand(
                        player,
                        Localizer.ForPlayer(player, "matchzy.mm.matchloadfailed")
                    );
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
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.mm.get5matchisalreadysetup", liveMatchId)
                );
                Log(
                    $"[LoadMatchDataCommand] A match is already setup with id: {liveMatchId}, cannot load a new match!"
                );
                return;
            }

            string url = command.ArgByIndex(1);

            string headerName = command.ArgCount > 3 ? command.ArgByIndex(2) : "";
            string headerValue = command.ArgCount > 3 ? command.ArgByIndex(3) : "";

            Log(
                $"[LoadMatchDataCommand] Match setup request received with URL: {url} headerName: {headerName} and headerValue: {headerValue}"
            );

            if (!IsValidUrl(url))
            {
                // command.ReplyToCommand($"[LoadMatchDataCommand] Invalid URL: {url}. Please provide a valid URL to load the match!");
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.mm.invalidurl", url)
                );
                Log(
                    $"[LoadMatchDataCommand] Invalid URL: {url}. Please provide a valid URL to load the match!"
                );
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

                        var response = await _sharedHttpClient.SendAsync(request);

                        if (response.IsSuccessStatusCode)
                        {
                            string jsonData = await response.Content.ReadAsStringAsync();
                            Log($"[LoadMatchFromURL] Received following data: {jsonData}");

                            // LoadMatchFromJSON uses native APIs — must run on game thread
                            Server.NextFrame(() =>
                            {
                                bool success = LoadMatchFromJSON(jsonData);
                                if (!success)
                                {
                                    ReplyToUserCommand(
                                        player,
                                        Localizer.ForPlayer(player, "matchzy.mm.matchloadfailed")
                                    );
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
                            Server.NextFrame(() =>
                            {
                                ReplyToUserCommand(
                                    player,
                                    Localizer.ForPlayer(
                                        player,
                                        "matchzy.mm.httprequestfailed",
                                        response.StatusCode
                                    )
                                );
                            });
                            Log(
                                $"[LoadMatchFromURL] HTTP request failed with status code: {response.StatusCode}"
                            );
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
                    case "players_per_team":
                    case "min_players_to_ready":
                    case "min_spectators_to_ready":
                    case "num_maps":
                        int numMaps;
                        if (!int.TryParse(jsonData[field]!.ToString(), out numMaps))
                        {
                            return $"{field} should be an integer!";
                        }

                        if (
                            field == "num_maps"
                            && numMaps > jsonData["maplist"]!.ToObject<List<string>>()!.Count
                        )
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

                        if (
                            (field != "spectators")
                            && (
                                jsonData[field]!["players"] == null
                                || jsonData[field]!["players"]!.Type != JTokenType.Object
                            )
                        )
                        {
                            return $"{field} should have 'players' JSON!";
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

                        break;
                    case "map_sides":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }

                        string[] allowedValues =
                        {
                            "team1_ct",
                            "team1_t",
                            "team2_ct",
                            "team2_t",
                            "knife",
                        };
                        bool allElementsValid = jsonData[field]!.All(element =>
                            allowedValues.Contains(element.ToString())
                        );

                        if (!allElementsValid)
                        {
                            return $"{field} should be \"team1_ct\", \"team1_t\", or \"knife\"!";
                        }

                        if (
                            jsonData[field]!.ToObject<List<string>>()!.Count
                            < jsonData["num_maps"]!.Value<int>()
                        )
                        {
                            return $"{field} should be equal to or greater than num_maps!";
                        }

                        break;

                    case "skip_veto":
                    case "clinch_series":
                    case "wingman":
                        if (!bool.TryParse(jsonData[field]!.ToString(), out bool result))
                        {
                            return $"{field} should be a boolean!";
                        }

                        break;
                }
            }

            return "";
        }

        public bool LoadMatchFromJSON(string jsonData)
        {
            JObject jsonDataObject = JObject.Parse(jsonData);

            string validationError = ValidateMatchJsonStructure(jsonDataObject);

            if (validationError != "")
            {
                Log($"[LoadMatchDataCommand] {validationError}");
                return false;
            }

            if (jsonDataObject["matchid"] != null)
            {
                liveMatchId = (long)jsonDataObject["matchid"]!;
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
            matchzyTeam1.teamPlayers =
                team1["players"] == null || team1["players"]!.Type == JTokenType.Null
                    ? null
                    : team1["players"];
            matchzyTeam2.teamPlayers =
                team2["players"] == null || team2["players"]!.Type == JTokenType.Null
                    ? null
                    : team2["players"];

            matchConfig = new()
            {
                MatchId = liveMatchId,
                MapsPool = maplist.ToObject<List<string>>()!,
                MapsLeftInVetoPool = maplist.ToObject<List<string>>()!,
                NumMaps = jsonDataObject["num_maps"]!.Value<int>(),
                MinPlayersToReady = minimumReadyRequired,
            };

            GetOptionalMatchValues(jsonDataObject);

            if (matchConfig.MapsPool.Count == matchConfig.NumMaps)
            {
                matchConfig.SkipVeto = true;
                isPreVeto = false;
            }
            else if (matchConfig.MapsPool.Count < matchConfig.NumMaps)
            {
                Log(
                    $"[LOADMATCH] The map pool {matchConfig.MapsPool.Count} is not large enough to play a series of {matchConfig.NumMaps} maps."
                );
                return false;
            }

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

            Log(
                $"[LOADMATCH] MinPlayersToReady: {matchConfig.MinPlayersToReady} SeriesClinch: {matchConfig.SeriesCanClinch}"
            );
            Log(
                $"[LOADMATCH] MapsPool: {string.Join(", ", matchConfig.MapsPool)} MapsLeftInVetoPool: {string.Join(", ", matchConfig.MapsLeftInVetoPool)}"
            );

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
                        if (
                            matchConfig.MatchSideType == "standard"
                            || matchConfig.MatchSideType == "always_knife"
                        )
                        {
                            matchConfig.MapSides.Add("knife");
                        }
                        else if (matchConfig.MatchSideType == "random")
                        {
                            matchConfig.MapSides.Add(
                                new Random().Next(0, 2) == 0 ? "team1_ct" : "team1_t"
                            );
                        }
                        else
                        {
                            matchConfig.MapSides.Add("team1_ct");
                        }
                    }
                }

                string currentMapName = Server.MapName;
                string mapName = matchConfig.Maplist[0].ToString();

                if (
                    IsMapReloadRequiredForGameMode(matchConfig.Wingman)
                    || mapReloadRequired
                    || currentMapName != mapName
                )
                {
                    SetCorrectGameMode();
                    ChangeMap(mapName, 0);
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

            if (matchConfig.SkipVeto)
                SetMapSides();

            SetTeamNames();
            UpdatePlayersMap();

            var seriesStartedEvent = new MatchZySeriesStartedEvent
            {
                MatchId = liveMatchId,
                NumberOfMaps = matchConfig.NumMaps,
                Team1 = new(matchzyTeam1.id, matchzyTeam1.teamName),
                Team2 = new(matchzyTeam2.id, matchzyTeam2.teamName),
            };

            Task.Run(async () =>
            {
                await SendEventAsync(seriesStartedEvent);
            });

            Log($"[LoadMatchFromJSON] Success with matchid: {liveMatchId}!");
            return true;
        }

        public bool LockTeamsManually()
        {
            try
            {
                CsTeam team1 =
                    teamSides[matchzyTeam1] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
                CsTeam team2 =
                    teamSides[matchzyTeam2] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;

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
                matchData["team1"] = team1Data;

                var team2Data = new JObject();
                team2Data["id"] = matchzyTeam2.id;
                team2Data["name"] = matchzyTeam2.teamName;
                team2Data["players"] = matchzyTeam2.teamPlayers;
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
            if (
                matchConfig.MapSides[mapNumber] == "team1_ct"
                || matchConfig.MapSides[mapNumber] == "team2_t"
            )
            {
                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                isKnifeRequired = false;
            }
            else if (
                matchConfig.MapSides[mapNumber] == "team2_ct"
                || matchConfig.MapSides[mapNumber] == "team1_t"
            )
            {
                teamSides[matchzyTeam2] = "CT";
                teamSides[matchzyTeam1] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam2;
                reverseTeamSides["TERRORIST"] = matchzyTeam1;
                isKnifeRequired = false;
            }
            else if (matchConfig.MapSides[mapNumber] == "knife")
            {
                isKnifeRequired = true;
            }

            SetTeamNames();
        }

        public void SetTeamNames()
        {
            string ctName = reverseTeamSides["CT"].teamName;
            string tName = reverseTeamSides["TERRORIST"].teamName;

            // The engine swaps what mp_teamname_1/mp_teamname_2 display on the scoreboard
            // each time sides switch (halftime, knife swap, OT halftime).
            // Default: mp_teamname_1 → CT scoreboard, mp_teamname_2 → T scoreboard
            // After swap: mp_teamname_1 → T scoreboard, mp_teamname_2 → CT scoreboard
            if (isConvarMappingSwapped)
            {
                Server.ExecuteCommand($"mp_teamname_1 {tName}; mp_teamname_2 {ctName}");
            }
            else
            {
                Server.ExecuteCommand($"mp_teamname_1 {ctName}; mp_teamname_2 {tName}");
            }

            // Also set directly on CCSTeam entities for reliability
            try
            {
                // Use cached entities — no entity scan
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

                    var cvar = ConVar.Find(cvarName);
                    matchConfig.ChangedCvars[cvarName] = cvarValue;
                    if (cvar != null)
                    {
                        matchConfig.OriginalCvars[cvarName] = GetConvarStringValue(cvar);
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
                matchConfig.MinPlayersToReady = jsonDataObject[
                    "min_players_to_ready"
                ]!.Value<int>();
            }

            if (jsonDataObject["min_spectators_to_ready"] != null)
            {
                matchConfig.MinSpectatorsToReady = jsonDataObject[
                    "min_spectators_to_ready"
                ]!.Value<int>();
            }

            if (
                jsonDataObject["spectators"] != null
                && jsonDataObject["spectators"]!["players"] != null
            )
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
                matchConfig.SeriesCanClinch = bool.Parse(
                    jsonDataObject["clinch_series"]!.ToString()
                );
            }

            if (jsonDataObject["skip_veto"] != null)
            {
                matchConfig.SkipVeto = bool.Parse(jsonDataObject["skip_veto"]!.ToString());
            }

            if (jsonDataObject["wingman"] != null)
            {
                matchConfig.Wingman = bool.Parse(jsonDataObject["wingman"]!.ToString());
            }

            if (jsonDataObject["veto_mode"] != null)
            {
                matchConfig.MapBanOrder = jsonDataObject["veto_mode"]!.ToObject<List<string>>()!;
            }
        }

        public void HandleTeamNameChangeCommand(
            CCSPlayerController? player,
            string teamName,
            int teamNum
        )
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
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $"!team{teamNum} <name>")
                );
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
                    coach.Clan = $"[{ctTeam.teamName} COACH]";
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
                    coach.Clan = $"[{tTeam.teamName} COACH]";
                }
            }

            SetTeamNames();

            // Add confirmation message
            ReplyToUserCommand(
                player,
                Localizer.ForPlayer(
                    player,
                    "matchzy.mm.teamchanged",
                    oldTeamName == "" ? $"Team {teamNum}" : oldTeamName,
                    teamName
                )
            );
        }

        public void SwapSidesInTeamData(bool swapTeams)
        {
            (teamSides[matchzyTeam1], teamSides[matchzyTeam2]) = (
                teamSides[matchzyTeam2],
                teamSides[matchzyTeam1]
            );
            (reverseTeamSides["CT"], reverseTeamSides["TERRORIST"]) = (
                reverseTeamSides["TERRORIST"],
                reverseTeamSides["CT"]
            );

            // The engine also swaps what mp_teamname_1/mp_teamname_2 map to on the scoreboard
            isConvarMappingSwapped = !isConvarMappingSwapped;
        }

        private CsTeam GetPlayerTeam(CCSPlayerController player)
        {
            CsTeam playerTeam = CsTeam.None;
            var steamId = player.SteamID;
            try
            {
                if (
                    matchzyTeam1.teamPlayers != null
                    && matchzyTeam1.teamPlayers[steamId.ToString()] != null
                )
                {
                    if (teamSides[matchzyTeam1] == "CT")
                    {
                        playerTeam = CsTeam.CounterTerrorist;
                    }
                    else if (teamSides[matchzyTeam1] == "TERRORIST")
                    {
                        playerTeam = CsTeam.Terrorist;
                    }
                }
                else if (
                    matchzyTeam2.teamPlayers != null
                    && matchzyTeam2.teamPlayers[steamId.ToString()] != null
                )
                {
                    if (teamSides[matchzyTeam2] == "CT")
                    {
                        playerTeam = CsTeam.CounterTerrorist;
                    }
                    else if (teamSides[matchzyTeam2] == "TERRORIST")
                    {
                        playerTeam = CsTeam.Terrorist;
                    }
                }
                else if (
                    matchConfig.Spectators != null
                    && matchConfig.Spectators[steamId.ToString()] != null
                )
                {
                    playerTeam = CsTeam.Spectator;
                }
            }
            catch (Exception ex)
            {
                Log($"[GetPlayerTeam - FATAL] Exception occurred: {ex.Message}");
            }

            return playerTeam;
        }

        public void EndSeries(string? winnerName, int restartDelay, int t1score, int t2score)
        {
            long matchId = liveMatchId;
            (int team1Score, int team2Score) = (matchzyTeam1.seriesScore, matchzyTeam2.seriesScore);
            if (winnerName == null)
            {
                PrintToAllChat(
                    $"{ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} and {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} have tied the match"
                );
            }
            else
            {
                Server.PrintToChatAll(
                    $"{chatPrefix} {ChatColors.Green}{winnerName}{ChatColors.Default} has won the match"
                );
            }

            string winnerTeam =
                (winnerName == null) ? "none"
                : matchzyTeam1.seriesScore > matchzyTeam2.seriesScore ? "team1"
                : "team2";

            var seriesResultEvent = new MatchZySeriesResultEvent()
            {
                MatchId = matchId,
                Winner = new Winner(
                    t1score > t2score && reverseTeamSides["CT"] == matchzyTeam1 ? "3" : "2",
                    winnerTeam
                ),
                Team1SeriesScore = team1Score,
                Team2SeriesScore = team2Score,
                TimeUntilRestore = 10,
            };

            Task.Run(async () =>
            {
                await database.SetMatchEndDataAsync(
                    matchId,
                    matchConfig.CurrentMapNumber,
                    winnerName ?? "Draw",
                    team1Score,
                    team2Score,
                    winnerName ?? "Draw",
                    team1Score,
                    team2Score
                );
                // Making sure that map end event is fired first
                await Task.Delay(2000);
                await SendEventAsync(seriesResultEvent);
            });

            // FIRST: Disable engine auto-change BEFORE restoring cvars — prevents race condition
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
                Log(
                    $"[EndSeries] G5API match detected - disabling auto changelevel to allow G5 to manage map rotation"
                );
            }

            if (shouldDisableAutoChangelevel)
            {
                Log(
                    $"[EndSeries] Auto changelevel is disabled (ConVar: {matchEndAutoChangelevel.Value}, G5API: {isG5ApiMatch})"
                );
                AddTimer(
                    restartDelay,
                    () =>
                    {
                        ResetMatch(false);
                    }
                );
                return;
            }

            // For last map (series end), change after exactly 10 seconds
            float mapChangeDelay = 10.0f;

            // Guard against empty map rotation
            if (mapRotationList.Count == 0)
            {
                Log(
                    "[EndSeries] WARNING: Map rotation list is empty! Cannot auto-changelevel. Resetting match on current map."
                );
                PrintToAllChat($"{ChatColors.Red}No maps in rotation — staying on current map.");
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
            string nextMap =
                currentIndex >= 0 && currentIndex < mapRotationList.Count - 1
                    ? mapRotationList[currentIndex + 1]
                    : mapRotationList[0];

            Log(
                $"[EndSeries] Current map: {currentMap}, Next map: {nextMap}, Change in {mapChangeDelay}s"
            );

            // Notify players
            PrintToAllChat(
                $"Next map: {ChatColors.Green}{nextMap}{ChatColors.Default} — changing in {(int)mapChangeDelay} seconds."
            );

            matchEndMapChangeTimer = AddTimer(
                mapChangeDelay,
                () =>
                {
                    // Reset match state FIRST, then change map — serialized to avoid race condition
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
            Server.ExecuteCommand("bot_kick");

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

                    Log(
                        $"[ChangeMapFromRotation] Workshop map, using host_workshop_map: {workshopId}"
                    );
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
            if (isPlayOutEnabled)
            {
                Server.ExecuteCommand("mp_overtime_enable 0");
                Server.ExecuteCommand("mp_match_can_clinch false");
            }
            else if (isPlayOutEnabled2)
            {
                Server.ExecuteCommand("mp_overtime_enable 0");
                Server.ExecuteCommand("mp_match_can_clinch false");
            }
            else
            {
                var absoluteCfgPath = Path.Join(
                    Server.GameDirectory + "/csgo/cfg",
                    GetGameMode() == 1 ? liveCfgPath : liveWingmanCfgPath
                );
                string? matchCanClinch = GetConvarValueFromCFGFile(
                    absoluteCfgPath,
                    "mp_match_can_clinch"
                );
                string? overtimeEnabled = GetConvarValueFromCFGFile(
                    absoluteCfgPath,
                    "mp_overtime_enable"
                );
                Server.ExecuteCommand($"mp_match_can_clinch {matchCanClinch ?? "1"}");
                Server.ExecuteCommand($"mp_overtime_enable {overtimeEnabled ?? "1"}");
            }
        }
    }
}
