using System.Drawing;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public class Position
    {
        public Vector PlayerPosition { get; private set; }
        public QAngle PlayerAngle { get; private set; }

        // Copy constructor
        public Position(Position other)
        {
            PlayerPosition = other.PlayerPosition;
            PlayerAngle = other.PlayerAngle;
        }

        public Position(Vector playerPosition, QAngle playerAngle)
        {
            // Create deep copies of the Vector and QAngle objects
            PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
            PlayerAngle = new QAngle(playerAngle.X, playerAngle.Y, playerAngle.Z);
        }

        public void Teleport(CCSPlayerController player)
        {
            if (
                player == null
                || !player.IsValid
                || !player.PlayerPawn.IsValid
                || player.PlayerPawn.Value == null
            )
                return;
            player.PlayerPawn.Value.Teleport(PlayerPosition, PlayerAngle, new Vector(0, 0, 0));
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            Position otherPosition = (Position)obj;

            return PlayerPosition.X == otherPosition.PlayerPosition.X
                && PlayerPosition.Y == otherPosition.PlayerPosition.Y
                && PlayerPosition.Z == otherPosition.PlayerPosition.Z
                && PlayerAngle.X == otherPosition.PlayerAngle.X
                && PlayerAngle.Y == otherPosition.PlayerAngle.Y
                && PlayerAngle.Z == otherPosition.PlayerAngle.Z;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + PlayerPosition.X.GetHashCode();
                hash = hash * 23 + PlayerPosition.Y.GetHashCode();
                hash = hash * 23 + PlayerPosition.Z.GetHashCode();
                hash = hash * 23 + PlayerAngle.X.GetHashCode();
                hash = hash * 23 + PlayerAngle.Y.GetHashCode();
                hash = hash * 23 + PlayerAngle.Z.GetHashCode();
                return hash;
            }
        }
    }

    public static class StringSimilarity
    {
        // Dice coefficient function
        public static double DiceCoefficient(string s1, string s2)
        {
            var bigrams1 = GetBigrams(s1);
            var bigrams2 = GetBigrams(s2);

            int intersection = bigrams1.Intersect(bigrams2).Count();
            return (2.0 * intersection) / (bigrams1.Count + bigrams2.Count);
        }

        // Get bigrams function
        private static List<string> GetBigrams(string input)
        {
            var bigrams = new List<string>();
            for (int i = 0; i < input.Length - 1; i++)
            {
                bigrams.Add(input.Substring(i, 2));
            }
            return bigrams;
        }

        /// <summary>
        /// Finds the name from a list of names that is nearest to the input name using the Dice coefficient.
        /// </summary>
        /// <param name="inputName">The input name to match.</param>
        /// <param name="names">The list of names to search from.</param>
        /// <returns>The nearest matching name from the list.</returns>
        public static string FindNearestName(string inputName, List<string> names)
        {
            if (inputName.Length == 1)
            {
                // If input name is a single character, find the name that starts with the same character
                var matchingName = names.FirstOrDefault(name =>
                    name.StartsWith(inputName, StringComparison.OrdinalIgnoreCase)
                );
                if (matchingName != null)
                {
                    return matchingName;
                }
            }
            // Otherwise, use the Dice coefficient to find the nearest name
            string nearestName =
                names.OrderByDescending(name => DiceCoefficient(inputName, name)).FirstOrDefault()
                ?? inputName;
            return nearestName;
        }
    }

    public partial class MatchZy
    {
        int maxLastGrenadesSavedLimit = 512;
        Dictionary<int, List<GrenadeThrownData>> lastGrenadesData = new();
        Dictionary<int, Dictionary<string, GrenadeThrownData>> nadeSpecificLastGrenadeData = new();
        Dictionary<int, DateTime> lastGrenadeThrownTime = new();
        Dictionary<int, PlayerPracticeTimer> playerTimers = new();
        Dictionary<int, PlayerLocationData> savedPlayerLocationData = new();

        public Dictionary<byte, List<Position>> spawnsData = GetEmptySpawnsData();
        public Dictionary<byte, List<Position>> coachSpawns = GetEmptySpawnsData();

        // (Backup)
        private readonly Dictionary<int, DateTime> lastRethrowTimes = new();
        private readonly object _botsDictLock = new();
        public Dictionary<int, Dictionary<string, object>> pracUsedBots =
            new Dictionary<int, Dictionary<string, object>>();
        private readonly HashSet<int> _botsBeingProcessed = new();

        public const string practiceCfgPath = "matchzy/prac.cfg";
        public const string dryrunCfgPath = "matchzy/dryrun.cfg";

        //		private readonly static Func<CCSGameRules, nint> CCSGameRules_PostCleanUp =
        //			new MemoryFunctionWithReturn<CCSGameRules, nint>("55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 48 89 BD ? ? ? ? E8 ? ? ? ? 66 83 F8").Invoke;
        // 55 48 89 e5 41 57 41 56 ?? ?? 41 54 53 48 81 EC A8 10 00 00

        private readonly static Func<CCSGameRules, nint> CCSGameRules_PostCleanUp =
            new MemoryFunctionWithReturn<CCSGameRules, nint>(
                "55 48 89 E5 41 57 49 89 FF 41 56 41 55 41 54 53 48 81 EC ? ? ? ? E8 ? ? ? ? 66 83 F8"
            ).Invoke;

        private Dictionary<
            string,
            CounterStrikeSharp.API.Modules.Timers.Timer
        > collisionGroupTimers = new();
        public bool isSpawningBot;
        public bool isDryRun = false;
        public List<int> noFlashList = new List<int>();

        public static Dictionary<byte, List<Position>> GetEmptySpawnsData()
        {
            return new Dictionary<byte, List<Position>>
            {
                { (byte)CsTeam.CounterTerrorist, new List<Position>() },
                { (byte)CsTeam.Terrorist, new List<Position>() },
            };
        }

        public void StartPracticeMode()
        {
            if (matchStarted)
                return;
            isPractice = true;
            isDryRun = false;
            isWarmup = false;
            readyAvailable = false;

            // Kill ready status hint timer
            readyStatusHintTimer?.Kill();
            readyStatusHintTimer = null;

            ClearClanTags();

            // Reset all player practice settings when entering practice
            ResetAllPlayerPracticeSettings(enteringPractice: true);

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath)))
            {
                Server.ExecuteCommand(
                    $"execifexists {practiceCfgPath};mp_roundtime 60;mp_roundtime_defuse 60"
                );
            }
            else
            {
                Server.ExecuteCommand(
                    """sv_cheats "true"; mp_force_pick_time "0"; bot_quota "0"; sv_showimpacts "1"; mp_limitteams "0"; sv_deadtalk "true"; sv_full_alltalk "true"; sv_ignoregrenaderadio "false"; mp_forcecamera "0"; sv_grenade_trajectory_prac_pipreview "true"; sv_grenade_trajectory_prac_trailtime "3"; sv_infinite_ammo "1"; weapon_auto_cleanup_time "15"; weapon_max_before_cleanup "30"; mp_buy_anywhere "1"; mp_maxmoney "9999999"; mp_startmoney "9999999";"""
                );
                Server.ExecuteCommand(
                    """mp_weapons_allow_typecount "-1"; mp_death_drop_defuser "false"; mp_death_drop_taser "false"; mp_drop_knife_enable "true"; mp_death_drop_grenade "0"; ammo_grenade_limit_total "5"; mp_defuser_allocation "2"; mp_free_armor "2"; mp_ct_default_grenades "weapon_incgrenade weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_ct_default_primary "weapon_m4a1";"""
                );
                Server.ExecuteCommand(
                    """mp_t_default_grenades "weapon_molotov weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_t_default_primary "weapon_ak47"; mp_warmup_online_enabled "true"; mp_warmup_pausetimer "1"; mp_warmup_start; bot_quota_mode normal; mp_solid_teammates 2; mp_autoteambalance false; mp_teammates_are_enemies false; buddha 1; buddha_ignore_bots 1; buddha_reset_hp 100;"""
                );
            }

            GetSpawns();
            Server.PrintToChatAll(
                $" {ChatColors.Green}Spawns: {ChatColors.Default}.spawn, .ctspawn, .tspawn, .bestspawn, .worstspawn"
            );
            Server.PrintToChatAll(
                $" {ChatColors.Green}Bots: {ChatColors.Default}.bot, .ctbot, .tbot, .nobots, .crouchbot, .boost, .crouchboost"
            );
            Server.PrintToChatAll(
                $" {ChatColors.Green}Nades: {ChatColors.Default}.loadnade, .savenade, .importnade, .listnades"
            );
            Server.PrintToChatAll(
                $" {ChatColors.Green}Nade Throw: {ChatColors.Default}.rethrow, .throwindex <index>, .lastindex, .delay <number>"
            );
            Server.PrintToChatAll(
                $" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.clear, .fastforward, .last, .back, .solid, .impacts, .traj"
            );
            Server.PrintToChatAll(
                $" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.nadecam, .savepos, .loadpos"
            );
            Server.PrintToChatAll(
                $" {ChatColors.Green}Sides & Others: {ChatColors.Default}.ct, .t, .spec, .fas, .god, .dryrun, .break, .nobreak, .exitprac"
            );
            Server.PrintToChatAll(
                $" {ChatColors.Default}Input {ChatColors.Green}.help{ChatColors.Default} View the full list of commands"
            );
        }

        public void GetSpawns()
        {
            // Resetting spawn data to avoid any glitches
            spawnsData = GetEmptySpawnsData();

            // FIX #1: Changed from 1 to int.MaxValue to properly find minimum priority
            int minPriority = int.MaxValue;

            // Find the minimum priority among CT spawns
            var spawnsct = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>(
                "info_player_counterterrorist"
            );
            foreach (var spawn in spawnsct)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority < minPriority)
                {
                    minPriority = spawn.Priority;
                }
            }

            // Collect ALL CT spawns with the minimum priority
            // IMPORTANT: We need all competitive spawns (not just 5) so that when a coach is present,
            // there are enough spawn points for the 5 regular players to use
            int ctSkipped = 0;
            foreach (var spawn in spawnsct)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority == minPriority)
                {
                    // Some workshop maps ship spawn entities without a fully-populated
                    // CBodyComponent/SceneNode; constructing a Position from null would NRE.
                    var origin = spawn.CBodyComponent?.SceneNode?.AbsOrigin;
                    var rotation = spawn.CBodyComponent?.SceneNode?.AbsRotation;
                    if (origin == null || rotation == null)
                    {
                        ctSkipped++;
                        continue;
                    }
                    spawnsData[(byte)CsTeam.CounterTerrorist]
                        .Add(new Position(origin, rotation));
                }
            }

            // FIX #2: Reset minPriority for T spawns instead of reusing CT's value
            minPriority = int.MaxValue;

            var spawnst = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>(
                "info_player_terrorist"
            );

            // FIX #3: Find the minimum priority among T spawns
            foreach (var spawn in spawnst)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority < minPriority)
                {
                    minPriority = spawn.Priority;
                }
            }

            // Collect ALL T spawns with the minimum priority
            // IMPORTANT: We need all competitive spawns (not just 5) so that when a coach is present,
            // there are enough spawn points for the 5 regular players to use
            int tSkipped = 0;
            foreach (var spawn in spawnst)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority == minPriority)
                {
                    var origin = spawn.CBodyComponent?.SceneNode?.AbsOrigin;
                    var rotation = spawn.CBodyComponent?.SceneNode?.AbsRotation;
                    if (origin == null || rotation == null)
                    {
                        tSkipped++;
                        continue;
                    }
                    spawnsData[(byte)CsTeam.Terrorist]
                        .Add(new Position(origin, rotation));
                }
            }

            Log(
                $"[GetSpawns] Loaded {spawnsData[(byte)CsTeam.CounterTerrorist].Count} CT spawns, {spawnsData[(byte)CsTeam.Terrorist].Count} T spawns"
                + (ctSkipped + tSkipped > 0 ? $" (skipped {ctSkipped} CT / {tSkipped} T with null body/scene components)" : "")
            );

            GetCoachSpawns();
        }

        private void HandleSpawnCommand(
            CCSPlayerController? player,
            string commandArg,
            byte teamNum,
            string command
        )
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            if (teamNum != 2 && teamNum != 3)
                return;
            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int spawnNumber) && spawnNumber >= 1)
                {
                    // Adjusting the spawnNumber according to the array index.
                    spawnNumber -= 1;
                    if (spawnsData.ContainsKey(teamNum) && spawnsData[teamNum].Count <= spawnNumber)
                        return;
                    if (player?.PlayerPawn?.IsValid == true && player.PlayerPawn.Value != null)
                    {
                        player.PlayerPawn.Value.Teleport(
                            spawnsData[teamNum][spawnNumber].PlayerPosition,
                            spawnsData[teamNum][spawnNumber].PlayerAngle,
                            new Vector(0, 0, 0)
                        );
                    }
                    ReplyToUserCommand(
                        player,
                        Localizer.ForPlayer(
                            player,
                            "matchzy.pm.movedtospawn",
                            $"{spawnNumber + 1}/{spawnsData[teamNum].Count}"
                        )
                    );
                }
                else
                {
                    ReplyToUserCommand(
                        player,
                        Localizer.ForPlayer(player, "matchzy.pm.negativenumber")
                    );
                    return;
                }
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $"!{command} <number>")
                );
            }
        }

        private string GetNadeType(string nadeName)
        {
            switch (nadeName)
            {
                case "weapon_flashbang":
                    return "Flash";
                case "weapon_smokegrenade":
                    return "Smoke";
                case "weapon_hegrenade":
                    return "HE";
                case "weapon_decoy":
                    return "Decoy";
                case "weapon_molotov":
                    return "Molly";
                case "weapon_incgrenade":
                    return "Molly";
                default:
                    return "";
            }
        }

        private void HandleSaveNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Split string into 2 parts
                string[] lineupUserString = saveNadeName.Split(' ');
                string lineupName = lineupUserString[0];
                string lineupDesc = string.Join(
                    " ",
                    lineupUserString,
                    1,
                    lineupUserString.Length - 1
                );

                // Get player info: steamid, pos, ang
                string playerSteamID;
                if (isSaveNadesAsGlobalEnabled == false)
                {
                    playerSteamID = player!.SteamID.ToString();
                }
                else
                {
                    playerSteamID = "default";
                }

                var pawn = player!.Pawn.Value;
                var playerPawn = player.PlayerPawn.Value;
                var sceneNode = pawn?.CBodyComponent?.SceneNode;
                if (playerPawn == null || sceneNode == null || sceneNode.AbsOrigin == null)
                {
                    ReplyToUserCommand(player, "Unable to read your position on this map.");
                    return;
                }
                QAngle playerAngle = playerPawn.EyeAngles;
                Vector playerPos = sceneNode.AbsOrigin;
                string currentMapName = Server.MapName;
                string nadeType = GetNadeType(
                    playerPawn.WeaponServices!.ActiveWeapon.Value!.DesignerName
                );

                // Define the file path
                string savednadesfileName = "matchzy/savednades.json";
                string savednadesPath = Path.Join(
                    Server.GameDirectory + "/csgo/cfg",
                    savednadesfileName
                );

                // Check if the file exists, if not, create it with an empty JSON object
                if (!File.Exists(savednadesPath))
                {
                    File.WriteAllText(savednadesPath, "{}");
                }

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    // Deserialize the existing JSON content
                    var savedNadesDict =
                        JsonSerializer.Deserialize<
                            Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                        >(existingJson)
                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    // Check if the lineup name already exists for the given SteamID
                    if (
                        savedNadesDict.ContainsKey(playerSteamID)
                        && savedNadesDict[playerSteamID].ContainsKey(lineupName)
                    )
                    {
                        // Check if the lineup already exists on the same map
                        if (savedNadesDict[playerSteamID][lineupName]["Map"] == currentMapName)
                        {
                            // Lineup already exists on the same map, reply to the user and return
                            ReplyToUserCommand(
                                player,
                                Localizer.ForPlayer(player, "matchzy.pm.lineupissaved")
                            );
                            return;
                        }
                    }

                    // Update or add the new lineup information
                    if (!savedNadesDict.ContainsKey(playerSteamID))
                    {
                        savedNadesDict[playerSteamID] =
                            new Dictionary<string, Dictionary<string, string>>();
                    }

                    savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                    {
                        { "Position", $"{playerPos.X} {playerPos.Y} {playerPos.Z + 4}" },
                        { "Angles", $"{playerAngle.X} {playerAngle.Y} {playerAngle.Z}" },
                        { "Desc", lineupDesc },
                        { "Map", currentMapName },
                        { "Type", nadeType },
                    };

                    // Serialize the updated dictionary back to JSON
                    string updatedJson = JsonSerializer.Serialize(
                        savedNadesDict,
                        new JsonSerializerOptions { WriteIndented = true }
                    );

                    // Write the updated JSON content back to the file
                    File.WriteAllText(savednadesPath, updatedJson);

                    PrintToPlayerChat(
                        player,
                        Localizer.ForPlayer(player, "matchzy.pm.lineupsavedsucces", lineupName)
                    );
                    PrintToAllChat(
                        Localizer[
                            "matchzy.pm.playersavedlineup",
                            player.PlayerName,
                            $"{lineupName} {playerPos} {playerAngle}"
                        ]
                    );
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $".savenade <name>")
                );
            }
        }

        private void HandleDeleteNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || player == null)
                return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Grab player steamid
                string playerSteamID;
                if (isSaveNadesAsGlobalEnabled == false)
                {
                    playerSteamID = player.SteamID.ToString();
                }
                else
                {
                    playerSteamID = "default";
                }

                // Define the file path
                string savednadesfileName = "matchzy/savednades.json";
                string savednadesPath = Path.Join(
                    Server.GameDirectory + "/csgo/cfg",
                    savednadesfileName
                );

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    //Console.WriteLine($"Existing JSON Content: {existingJson}");

                    // Deserialize the existing JSON content
                    var savedNadesDict =
                        JsonSerializer.Deserialize<
                            Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                        >(existingJson)
                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    // Check if the lineup exists for the given SteamID and name
                    if (
                        savedNadesDict.ContainsKey(playerSteamID)
                        && savedNadesDict[playerSteamID].ContainsKey(saveNadeName)
                    )
                    {
                        var lineupInfo = savedNadesDict[playerSteamID][saveNadeName];

                        // Check if the lineup is for the current maps
                        if (lineupInfo.ContainsKey("Map") && lineupInfo["Map"] == Server.MapName)
                        {
                            // Remove the specified lineup
                            savedNadesDict[playerSteamID].Remove(saveNadeName);

                            // Serialize the updated dictionary back to JSON
                            string updatedJson = JsonSerializer.Serialize(
                                savedNadesDict,
                                new JsonSerializerOptions { WriteIndented = true }
                            );

                            // Write the updated JSON content back to the file
                            File.WriteAllText(savednadesPath, updatedJson);

                            // ReplyToUserCommand(player, $"Lineup '{saveNadeName}' deleted successfully.");
                            ReplyToUserCommand(
                                player,
                                Localizer.ForPlayer(
                                    player,
                                    "matchzy.pm.lineupdeletesuccess",
                                    saveNadeName
                                )
                            );
                        }
                        else
                        {
                            // ReplyToUserCommand(player, $"Lineup '{saveNadeName}' not found on the current map!");
                            ReplyToUserCommand(
                                player,
                                Localizer.ForPlayer(
                                    player,
                                    "matchzy.pm.nadenotfoundonmap",
                                    saveNadeName
                                )
                            );
                        }
                    }
                    else
                    {
                        // ReplyToUserCommand(player, $"Lineup '{saveNadeName}' not found!");
                        ReplyToUserCommand(
                            player,
                            Localizer.ForPlayer(player, "matchzy.pm.lineupnotfound", saveNadeName)
                        );
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .delnade <name>");
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $".delnade <name>")
                );
            }
        }

        private void HandleImportNadeCommand(CCSPlayerController? player, string saveNadeCode)
        {
            if (!isPractice || player == null)
                return;

            if (!string.IsNullOrWhiteSpace(saveNadeCode))
            {
                try
                {
                    // Split the code into parts
                    string[] parts = saveNadeCode.Split(' ');

                    // Check if there are enough parts
                    if (parts.Length == 7)
                    {
                        // Extract name, pos, and ang from the parts
                        string lineupName = parts[0].Trim();
                        string[] posAng = parts.Skip(1).Select(p => p.Replace(",", "")).ToArray(); // Replace ',' with '' for proper parsing

                        // Get player info: steamid
                        string playerSteamID = player.SteamID.ToString();
                        string currentMapName = Server.MapName;

                        // Define the file path
                        string savednadesfileName = "matchzy/savednades.json";
                        string savednadesPath = Path.Join(
                            Server.GameDirectory + "/csgo/cfg",
                            savednadesfileName
                        );

                        // Read existing JSON content
                        string existingJson = File.ReadAllText(savednadesPath);

                        //Console.WriteLine($"Existing JSON Content: {existingJson}");

                        // Deserialize the existing JSON content
                        var savedNadesDict =
                            JsonSerializer.Deserialize<
                                Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                            >(existingJson)
                            ?? new Dictionary<
                                string,
                                Dictionary<string, Dictionary<string, string>>
                            >();

                        // Check if the lineup name already exists for the given SteamID on the same map
                        if (
                            savedNadesDict.ContainsKey(playerSteamID)
                            && savedNadesDict[playerSteamID].ContainsKey(lineupName)
                        )
                        {
                            var existingLineup = savedNadesDict[playerSteamID][lineupName];
                            if (
                                existingLineup.ContainsKey("Map")
                                && existingLineup["Map"] == currentMapName
                            )
                            {
                                // Lineup already exists on the same map, reply to the user and return
                                // ReplyToUserCommand(player, $"Lineup '{lineupName}' already exists! Please use a different name or use .delnade <nade>");
                                ReplyToUserCommand(
                                    player,
                                    Localizer.ForPlayer(
                                        player,
                                        "matchzy.pm.lineupalreadyexists",
                                        lineupName
                                    )
                                );
                                return;
                            }
                        }

                        // Update or add the new lineup information
                        if (!savedNadesDict.ContainsKey(playerSteamID))
                        {
                            savedNadesDict[playerSteamID] =
                                new Dictionary<string, Dictionary<string, string>>();
                        }

                        savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                        {
                            { "Position", $"{posAng[0]} {posAng[1]} {posAng[2]}" },
                            { "Angles", $"{posAng[3]} {posAng[4]} {posAng[5]}" },
                            { "Desc", "" },
                            { "Map", currentMapName },
                        };

                        // Serialize the updated dictionary back to JSON
                        string updatedJson = JsonSerializer.Serialize(
                            savedNadesDict,
                            new JsonSerializerOptions { WriteIndented = true }
                        );

                        // Write the updated JSON content back to the file
                        File.WriteAllText(savednadesPath, updatedJson);

                        // ReplyToUserCommand(player, $"Lineup '{lineupName}' imported and saved successfully.");
                        ReplyToUserCommand(
                            player,
                            Localizer.ForPlayer(player, "matchzy.pm.lineupimportedsuccess")
                        );
                    }
                    else
                    {
                        // ReplyToUserCommand(player, $"Invalid code format. Please provide a valid code with name, pos, and ang.");
                        ReplyToUserCommand(
                            player,
                            Localizer.ForPlayer(player, "matchzy.pm.lineupinvalidcode")
                        );
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .importnade <code>");
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $".importnade <code>")
                );
            }
        }

        private void HandleListNadesCommand(CCSPlayerController? player, string nadeFilter)
        {
            if (!isPractice || player == null)
                return;

            // Define the file path
            string savednadesfileName = "matchzy/savednades.json";
            string savednadesPath = Path.Join(
                Server.GameDirectory + "/csgo/cfg",
                savednadesfileName
            );

            try
            {
                // Read existing JSON content
                string existingJson = File.ReadAllText(savednadesPath);

                //Console.WriteLine($"Existing JSON Content: {existingJson}");

                // Deserialize the existing JSON content
                var savedNadesDict =
                    JsonSerializer.Deserialize<
                        Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                    >(existingJson)
                    ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                ReplyToUserCommand(
                    player,
                    $"\x0D-----All Saved Lineups for \x06{Server.MapName}\x0D-----"
                );

                // List lineups for the specified player
                ListLineups(player, "default", Server.MapName, savedNadesDict, nadeFilter);

                // List lineups for the current player
                ListLineups(
                    player,
                    player.SteamID.ToString(),
                    Server.MapName,
                    savedNadesDict,
                    nadeFilter
                );
            }
            catch (JsonException ex)
            {
                Log($"Error handling JSON: {ex.Message}");
                ReplyToUserCommand(player, $"Error handling JSON. Please check the server logs.");
            }
        }

        private void ListLineups(
            CCSPlayerController player,
            string steamID,
            string mapName,
            Dictionary<string, Dictionary<string, Dictionary<string, string>>> savedNadesDict,
            string nadeFilter
        )
        {
            if (savedNadesDict.ContainsKey(steamID))
            {
                foreach (var kvp in savedNadesDict[steamID])
                {
                    // Check if a filter is provided, and if so, apply the filter
                    if (
                        (
                            string.IsNullOrWhiteSpace(nadeFilter)
                            || kvp.Key.Contains(nadeFilter, StringComparison.OrdinalIgnoreCase)
                        )
                        && kvp.Value.ContainsKey("Map")
                        && kvp.Value["Map"] == mapName
                    )
                    {
                        // Format and reply with the lineup name
                        ReplyToUserCommand(
                            player,
                            $"\x06[{kvp.Value["Type"]}] \x0D.loadnade \x06{kvp.Key}"
                        );
                    }
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"No saved lineups found for the specified SteamID: ({steamID}).");
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.pm.nosavedlineups", steamID)
                );
            }
        }

        private void HandleLoadNadeCommand(CCSPlayerController? player, string loadNadeName)
        {
            if (!isPractice || player == null || !IsPlayerValid(player))
                return;

            if (!string.IsNullOrWhiteSpace(loadNadeName))
            {
                // Get player info: steamid
                string playerSteamID = player.SteamID.ToString();

                // Define the file path
                string savednadesfileName = "matchzy/savednades.json";
                string savednadesPath = Path.Join(
                    Server.GameDirectory + "/csgo/cfg",
                    savednadesfileName
                );

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    //Console.WriteLine($"Existing JSON Content: {existingJson}");

                    // Deserialize the existing JSON content
                    var savedNadesDict =
                        JsonSerializer.Deserialize<
                            Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                        >(existingJson)
                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    bool lineupFound = false;
                    bool lineupOnWrongMap = false;

                    // Check for the lineup in the player's steamID and the fixed steamID
                    foreach (string currentSteamID in new[] { playerSteamID, "default" })
                    {
                        if (savedNadesDict.ContainsKey(currentSteamID))
                        {
                            // Filter nade names based on the current map
                            var nadeNamesOnCurrentMap = savedNadesDict[currentSteamID]
                                .Where(n =>
                                    n.Value.ContainsKey("Map") && n.Value["Map"] == Server.MapName
                                )
                                .Select(n => n.Key)
                                .ToList();

                            // Find the nearest matching name
                            string nearestName = StringSimilarity.FindNearestName(
                                loadNadeName,
                                nadeNamesOnCurrentMap
                            );

                            if (savedNadesDict[currentSteamID].ContainsKey(nearestName))
                            {
                                var lineupInfo = savedNadesDict[currentSteamID][nearestName];

                                // Check if the lineup contains the "Map" key and if it matches the current map
                                if (
                                    lineupInfo.ContainsKey("Map")
                                    && lineupInfo["Map"] == Server.MapName
                                )
                                {
                                    // Extract position and angle from the lineup information
                                    string[] posArray = lineupInfo["Position"].Split(' ');
                                    string[] angArray = lineupInfo["Angles"].Split(' ');

                                    // Parse position and angle
                                    Vector loadedPlayerPos = new Vector(
                                        float.Parse(posArray[0]),
                                        float.Parse(posArray[1]),
                                        float.Parse(posArray[2])
                                    );
                                    QAngle loadedPlayerAngle = new QAngle(
                                        float.Parse(angArray[0]),
                                        float.Parse(angArray[1]),
                                        float.Parse(angArray[2])
                                    );

                                    // Teleport player
                                    player!.PlayerPawn!.Value!.Teleport(
                                        loadedPlayerPos,
                                        loadedPlayerAngle,
                                        new Vector(0, 0, 0)
                                    );

                                    // Change player inv slot
                                    switch (lineupInfo["Type"])
                                    {
                                        case "Flash":
                                            player.ExecuteClientCommand("slot7");
                                            break;
                                        case "Smoke":
                                            player.ExecuteClientCommand("slot8");
                                            break;
                                        case "HE":
                                            player.ExecuteClientCommand("slot6");
                                            break;
                                        case "Decoy":
                                            player.ExecuteClientCommand("slot9");
                                            break;
                                        case "Molly":
                                            player.ExecuteClientCommand("slot10");
                                            break;
                                        case "":
                                            player.ExecuteClientCommand("slot8");
                                            break;
                                    }

                                    // Extract description, if available
                                    string? lineupDesc = lineupInfo.ContainsKey("Desc")
                                        ? lineupInfo["Desc"]
                                        : null;

                                    // Print messages
                                    // ReplyToUserCommand(player, $"Lineup {ChatColors.Green}{nearestName}{ChatColors.Default} loaded successfully!");
                                    ReplyToUserCommand(
                                        player,
                                        Localizer.ForPlayer(
                                            player,
                                            "matchzy.pm.lineuploadedsuccess",
                                            nearestName
                                        )
                                    );

                                    if (!string.IsNullOrWhiteSpace(lineupDesc))
                                    {
                                        player.PrintToCenter($"{lineupDesc}");
                                        // ReplyToUserCommand(player, $"Description: {ChatColors.Green}{lineupDesc}{ChatColors.Default}");
                                        ReplyToUserCommand(
                                            player,
                                            Localizer.ForPlayer(
                                                player,
                                                "matchzy.pm.lineupdesc",
                                                lineupDesc
                                            )
                                        );
                                    }

                                    lineupFound = true;
                                    break;
                                }
                                else
                                {
                                    // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{nearestName}{ChatColor.Default} not found on the current map!");
                                    ReplyToUserCommand(
                                        player,
                                        Localizer.ForPlayer(
                                            player,
                                            "matchzy.pm.nadenotfoundonmap",
                                            nearestName
                                        )
                                    );
                                    lineupOnWrongMap = true;
                                }
                            }
                        }
                    }

                    if (!lineupFound && !lineupOnWrongMap)
                    {
                        // Lineup not found
                        // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{loadNadeName}{ChatColor.Default} not found!");
                        ReplyToUserCommand(
                            player,
                            Localizer.ForPlayer(player, "matchzy.pm.nadenotfound", loadNadeName)
                        );
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Nade not found! Usage: .loadnade <name>");
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.pm.loadnadenotfound")
                );
            }
        }

        public void ShowSpawnBeam(Position spawn, Color color)
        {
            CBeam? beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam == null)
            {
                Log($"Failed to create beam for the spawn");
                return;
            }

            beam.LifeState = 1;
            beam.Width = 5;
            beam.Render = color;

            beam.EndPos.X = spawn.PlayerPosition.X;
            beam.EndPos.Y = spawn.PlayerPosition.Y;
            beam.EndPos.Z = spawn.PlayerPosition.Z + 100.0f;

            beam.Teleport(spawn.PlayerPosition, new QAngle(0, 0, 0), new Vector(0, 0, 0));

            beam.DispatchSpawn();
        }

        public void RemoveSpawnBeams()
        {
            var beams = Utilities.FindAllEntitiesByDesignerName<CEntityInstance>("beam");
            foreach (var beam in beams)
            {
                if (beam == null)
                    continue;
                beam.Remove();
            }
        }

        [ConsoleCommand("css_god", "Sets Infinite health for player")]
        public void OnGodCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || !IsPlayerValid(player))
                return;

            if (player?.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null)
            {
                ReplyToUserCommand(player, "God command failed: invalid player or pawn.");
                return;
            }

            int currentHP = player.PlayerPawn.Value.Health;

            if (currentHP > 100)
            {
                player.PlayerPawn.Value.Health = 100;
                ReplyToUserCommand(
                    player,
                    "God is " + Localizer.ForPlayer(player, "matchzy.cc.disabled")
                );
            }
            else
            {
                player.PlayerPawn.Value.Health = 2147483647; // max 32bit int
                ReplyToUserCommand(
                    player,
                    "God is " + Localizer.ForPlayer(player, "matchzy.cc.enabled")
                );
            }
        }

        [ConsoleCommand("css_prac", "Starts practice mode")]
        [ConsoleCommand("css_tactics", "Starts practice mode")]
        public void OnPracCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, "Practice Mode cannot be started when a match has been started!");
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.pm.pracmatchstarted")
                );
                return;
            }

            matchStarted = false;
            isPlayOutEnabled = false;
            isPlayOutEnabled2 = false;
            isKnifeRound = false;
            isKnifeRequired = false;

            StartPracticeMode();
        }

        [ConsoleCommand("css_dry", "Starts dryrun")]
        [ConsoleCommand("css_dryrun", "Starts dryrun")]
        public void OnDryRunCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.pm.dryrunmatchstarted")
                );
                return;
            }

            // If already in dryrun, just restart the round instead of blocking
            if (isDryRun)
            {
                Server.ExecuteCommand("mp_restartgame 1");
                PrintToAllChat($"{ChatColors.Green}Dryrun restarted!");
                return;
            }

            Server.ExecuteCommand("bot_kick");
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
            noFlashList = new();

            ExecUnpracCommands(); // reset practice-specific cvars
            ExecDryRunCFG(); // apply dry run cvars and restart
            readyStatusHintTimer?.Kill();
            readyStatusHintTimer = null;
            ClearClanTags();

            isPractice = false;
            isDryRun = true;
        }

        [ConsoleCommand("css_exitdryrun", "Exit Dryrun & Start Practice")]
        [ConsoleCommand("css_exitdry", "Exit Dryrun & Start Practice")]
        [ConsoleCommand("css_stopdry", "Exit Dryrun & Start Practice")]
        [ConsoleCommand("css_enddry", "Exit Dryrun & Start Practice")]
        public void OnExitDryCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_exitdry", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                ReplyToUserCommand(player, "MatchZy is already in match mode!");
                return;
            }

            ExecExitDryCFG();
            StartPracticeMode();

            isDryRun = false;
        }

        [ConsoleCommand("css_spawn", "Teleport to provided spawn")]
        [ConsoleCommand("css_sp", "Teleport to provided spawn")]
        public void OnSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice)
                return;
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid)
                return;

            if (player.TeamNum == (byte)CsTeam.Spectator)
                return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, player.TeamNum, "spawn");
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $"!spawn <round>")
                );
            }
        }

        [ConsoleCommand("css_ctspawn", "Teleport to provided CT spawn")]
        public void OnCtSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice)
                return;
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid)
                return;

            if (player.TeamNum == (byte)CsTeam.Spectator)
                return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $"!ctspawn <round>")
                );
            }
        }

        [ConsoleCommand("css_tspawn", "Teleport to provided T spawn")]
        public void OnTSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice)
                return;
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid)
                return;

            if (player.TeamNum == (byte)CsTeam.Spectator)
                return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.Terrorist, "tspawn");
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $"!tspawn <round>")
                );
            }
        }

        private const int MaxPracticeBots = 5;

        private static bool IsNoBotsFlagSet()
        {
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch
            {
                return false;
            }

            return args.Any(a =>
                a.Equals("-nobots", StringComparison.OrdinalIgnoreCase)
                || a.Equals("+nobots", StringComparison.OrdinalIgnoreCase)
                || a.Equals("nobots", StringComparison.OrdinalIgnoreCase)
            );
        }

        private bool CanSpawnAnotherBot(CCSPlayerController? player)
        {
            // Count current bots
            int currentBotCount = Utilities
                .GetPlayers()
                .Count(p => p?.IsValid == true && p.IsBot && !p.IsHLTV);
            if (currentBotCount >= MaxPracticeBots)
            {
                player?.PrintToChat(
                    $" {ChatColors.Green}[MatchZy] {ChatColors.White}Maximum number of bots ({MaxPracticeBots}) already reached!"
                );
                return false;
            }
            return true;
        }

        [ConsoleCommand("css_bot", "Spawns a bot at the player's position")]
        public void OnBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                false
            );
        }

        [ConsoleCommand("css_tbot", "Spawns a T bot at the player's position")]
        public void OnTBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                false,
                CsTeam.Terrorist
            );
        }

        [ConsoleCommand("css_ctbot", "Spawns a CT bot at the player's position")]
        public void OnCtBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                false,
                CsTeam.CounterTerrorist
            );
        }

        [ConsoleCommand("css_cbot", "Spawns a crouched bot at the player's position")]
        [ConsoleCommand("css_crouchbot", "Spawns a crouched bot at the player's position")]
        [ConsoleCommand("css_duckbot")]
        public void OnCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            // crouched, auto team
            AddBot(
                player, /*crouch*/
                true
            );
        }

        [ConsoleCommand("css_tcrouchbot", "Spawns a crouched T bot at the player's position")]
        public void OnTCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                true,
                CsTeam.Terrorist
            );
        }

        [ConsoleCommand("css_ctcrouchbot", "Spawns a crouched CT bot at the player's position")]
        public void OnCtCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                true,
                CsTeam.CounterTerrorist
            );
        }

        [ConsoleCommand(
            "css_boost",
            "Spawns a bot at the player's position and boost the player on it"
        )]
        public void OnBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                false
            );

            var target = player; // capture for timer safety
            AddTimer(
                0.2f,
                () =>
                {
                    if (
                        target != null
                        && target.IsValid
                        && target.Connected == PlayerConnectedState.Connected
                        && target.PlayerPawn?.IsValid == true
                        && target.PlayerPawn.Value != null
                    )
                    {
                        ElevatePlayer(target);
                    }
                }
            );
        }

        [ConsoleCommand(
            "css_cboost",
            "Spawns a crouched bot at the player's position and boost the player on it"
        )]
        [ConsoleCommand(
            "css_crouchboost",
            "Spawns a crouched bot at the player's position and boost the player on it"
        )]
        [ConsoleCommand("css_duckboost")]
        public void OnCrouchBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice)
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            AddBot(player, true);
            AddTimer(0.2f, () => ElevatePlayer(player));
        }

        private void AddBot(CCSPlayerController? player, bool crouch, CsTeam? forceTeam = null)
        {
            try
            {
                if (
                    !isPractice
                    || player == null
                    || !player.IsValid
                    || player.PlayerPawn?.IsValid != true
                    || player.PlayerPawn.Value == null
                )
                    return;

                // Safely check movement services
                if (player.PlayerPawn.Value.MovementServices != null)
                {
                    try
                    {
                        CCSPlayer_MovementServices movementService = new(
                            player.PlayerPawn.Value.MovementServices.Handle
                        );
                        if ((int)movementService.DuckAmount == 1)
                        {
                            // Player was crouching while using .bot command
                            crouch = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[AddBot] Could not check crouch state: {ex.Message}");
                    }
                }

                isSpawningBot = true;

                // Determine target team
                CsTeam targetTeam =
                    forceTeam
                    ?? (CsTeam)player.TeamNum switch
                    {
                        CsTeam.CounterTerrorist => CsTeam.Terrorist,
                        CsTeam.Terrorist => CsTeam.CounterTerrorist,
                        _ => CsTeam.Terrorist,
                    };

                // Add bot to opposite team or forced team
                if (targetTeam == CsTeam.Terrorist)
                {
                    Server.ExecuteCommand("bot_join_team T");
                    Server.ExecuteCommand("bot_add_t");
                }
                else
                {
                    Server.ExecuteCommand("bot_join_team CT");
                    Server.ExecuteCommand("bot_add_ct");
                }

                // Once bot is added, we teleport it to the requested position
                var targetPlayer = player; // Capture for timer safety
                AddTimer(
                    0.1f,
                    () =>
                    {
                        if (
                            targetPlayer != null
                            && targetPlayer.IsValid
                            && targetPlayer.Connected == PlayerConnectedState.Connected
                        )
                        {
                            SpawnBot(targetPlayer, crouch);
                        }
                        else
                        {
                            isSpawningBot = false;
                        }
                    }
                );

                Server.ExecuteCommand("bot_stop 1");
                Server.ExecuteCommand("bot_freeze 1");
                Server.ExecuteCommand("bot_zombie 1");
            }
            catch (Exception ex)
            {
                Log($"[AddBot - ERROR] {ex.GetType().Name}: {ex.Message}");
                isSpawningBot = false;
            }
        }

        private CCSPlayerController? GetClosestBotOfPlayer(CCSPlayerController player)
        {
            if (!IsPlayerValid(player) || !player.UserId.HasValue)
                return null;

            CCSPlayerController? closestBot = null;
            float closestDistance = float.MaxValue;
            List<int> invalidBotIds = new();

            lock (_botsDictLock)
            {
                // Create snapshot to avoid modification during iteration
                var botSnapshot = pracUsedBots.ToList();

                foreach (var kvp in botSnapshot)
                {
                    int userId = kvp.Key;
                    var botDict = kvp.Value;

                    try
                    {
                        // Validate dictionary entries exist
                        if (!botDict.ContainsKey("owner") || !botDict.ContainsKey("controller"))
                        {
                            invalidBotIds.Add(userId);
                            continue;
                        }

                        // Safe casting with validation
                        if (
                            botDict["owner"] is not CCSPlayerController botOwner
                            || botDict["controller"] is not CCSPlayerController bot
                        )
                        {
                            invalidBotIds.Add(userId);
                            continue;
                        }

                        // Critical: Validate both controllers are still valid
                        if (
                            !IsPlayerValid(bot)
                            || !IsPlayerValid(botOwner)
                            || bot.Connected != PlayerConnectedState.Connected
                            || botOwner.Connected != PlayerConnectedState.Connected
                        )
                        {
                            invalidBotIds.Add(userId);
                            continue;
                        }

                        // Check ownership matches
                        if (
                            !botOwner.UserId.HasValue
                            || botOwner.UserId.Value != player.UserId.Value
                        )
                            continue;

                        // Safe distance calculation with null checks
                        float distance = CalculateSafeDistance(botOwner, bot);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestBot = bot;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[GetClosestBot] Exception for bot {userId}: {ex.Message}");
                        invalidBotIds.Add(userId);
                    }
                }

                // Clean up invalid entries after iteration
                foreach (var invalidId in invalidBotIds)
                {
                    pracUsedBots.Remove(invalidId);
                    _botsBeingProcessed.Remove(invalidId);
                }
            }

            return closestBot;
        }

        private float CalculateSafeDistance(
            CCSPlayerController player1,
            CCSPlayerController player2
        )
        {
            try
            {
                // Validate player pawns exist and are valid
                if (
                    player1?.PlayerPawn?.IsValid != true
                    || player1.PlayerPawn.Value == null
                    || player2?.PlayerPawn?.IsValid != true
                    || player2.PlayerPawn.Value == null
                )
                {
                    return float.MaxValue;
                }

                // Validate body components and scene nodes
                var p1Origin = player1.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;
                var p2Origin = player2.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;

                if (p1Origin == null || p2Origin == null)
                {
                    return float.MaxValue;
                }

                // Calculate Manhattan distance (more performant than Euclidean)
                return MathF.Abs(p1Origin.X - p2Origin.X)
                    + MathF.Abs(p1Origin.Y - p2Origin.Y)
                    + MathF.Abs(p1Origin.Z - p2Origin.Z);
            }
            catch (Exception ex)
            {
                Log($"[CalculateSafeDistance] Error: {ex.Message}");
                return float.MaxValue;
            }
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player))
                return HookResult.Continue;

            // Reset noflash on spawn
            if (
                player != null
                && player.UserId.HasValue
                && noFlashList.Contains(player.UserId.Value)
            )
            {
                noFlashList.Remove(player.UserId.Value);
            }

            // disable noclip on spawn -- all no clipping functionality is handled by the plugin!
            // Movement adjustments are consistent with cs2-noclip.
            CBasePlayerPawn pawn = player!.PlayerPawn.Value!;
            if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP)
            {
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }

            if (
                matchStarted
                && (matchzyTeam1.coach.Contains(player!) || matchzyTeam2.coach.Contains(player!))
            )
            {
                player!.InGameMoneyServices!.Account = 0;

                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;

                return HookResult.Continue;
            }

            // Respawing a bot where it was actually spawned during practice session
            if (isPractice && player!.IsValid && player.IsBot && player.UserId.HasValue)
            {
                lock (_botsDictLock)
                {
                    if (pracUsedBots.TryGetValue(player.UserId.Value, out var botData))
                    {
                        if (botData["position"] is Position botPosition)
                        {
                            player.PlayerPawn.Value?.Teleport(
                                botPosition.PlayerPosition,
                                botPosition.PlayerAngle,
                                new Vector(0, 0, 0)
                            );
                            bool isCrouched = (bool)botData["crouchstate"];
                            if (isCrouched)
                            {
                                player.PlayerPawn.Value!.Flags |= (uint)PlayerFlags.FL_DUCKING;
                                CCSPlayer_MovementServices movementService = new(
                                    player.PlayerPawn.Value.MovementServices!.Handle
                                );
                                AddTimer(0.1f, () => movementService.DuckAmount = 1);
                                AddTimer(
                                    0.2f,
                                    () =>
                                    {
                                        // Validate player and pawn still exist before accessing Bot
                                        if (
                                            IsPlayerValid(player)
                                            && player.PlayerPawn != null
                                            && player.PlayerPawn.Value != null
                                            && player.PlayerPawn.Value.Bot != null
                                        )
                                        {
                                            player.PlayerPawn.Value.Bot.IsCrouching = true;
                                        }
                                    }
                                );
                            }
                            CCSPlayerController? botOwner = (CCSPlayerController)botData["owner"];

                            // PATCHED: Added validation before scheduling collision timer
                            if (
                                botOwner != null
                                && botOwner.IsValid
                                && botOwner.PlayerPawn != null
                                && botOwner.PlayerPawn.IsValid
                                && player.IsValid
                                && player.PlayerPawn != null
                                && player.PlayerPawn.IsValid
                            )
                            {
                                // PATCHED: Capture player reference in lambda to ensure validation
                                var botPlayer = player;
                                AddTimer(
                                    0.2f,
                                    () =>
                                    {
                                        // PATCHED: Validate both players still exist before disabling collisions
                                        if (
                                            IsPlayerValid(botOwner)
                                            && IsPlayerValid(botPlayer)
                                            && botOwner.PlayerPawn != null
                                            && botOwner.PlayerPawn.IsValid
                                            && botPlayer.PlayerPawn != null
                                            && botPlayer.PlayerPawn.IsValid
                                        )
                                        {
                                            TemporarilyDisableCollisions(botOwner, botPlayer);
                                        }
                                    }
                                );
                            }
                        }
                    }
                    else if (!isSpawningBot && !player.IsHLTV)
                    {
                        // Bot has been spawned, but we didn't spawn it, so kick it.
                        // This most often happens when a player changes team with bot_quota_mode set to fill
                        // Extra bots from bot_add are already handled in SpawnBot
                        // Delay this for a few seconds to prevent crashes
                        // IMPORTANT: Capture PlayerName NOW before the timer fires, as bot may be invalid later
                        string botName = player.PlayerName;
                        Log($"Kicking bot {botName} due to erroneous spawning");
                        AddTimer(
                            2.5f,
                            () =>
                            {
                                Server.ExecuteCommand($"bot_kick {botName}");
                            }
                        );
                    }
                }

                return HookResult.Continue;
            }

            return HookResult.Continue;
        }

        // ============================================================================
        // SEPARATE Coach Handling - Cleaner and safer
        // ============================================================================
        private void HandleCoachSpawn(CCSPlayerController player)
        {
            try
            {
                if (player.InGameMoneyServices != null)
                {
                    player.InGameMoneyServices.Account = 0;
                    Utilities.SetStateChanged(
                        player,
                        "CCSPlayerController",
                        "m_pInGameMoneyServices"
                    );
                }

                if (player.PlayerPawn?.IsValid == true && player.PlayerPawn.Value != null)
                {
                    player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
                    player.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                }
            }
            catch (Exception ex)
            {
                Log($"[HandleCoachSpawn] Error: {ex.Message}");
            }
        }

        // ============================================================================
        // IMPROVED Bot Respawn - Critical crash prevention
        // ============================================================================
        private void HandleBotRespawn(CCSPlayerController player)
        {
            if (!player.UserId.HasValue)
                return;

            int userId = player.UserId.Value;

            lock (_botsDictLock)
            {
                // Check if this bot is being processed
                if (_botsBeingProcessed.Contains(userId))
                {
                    Log($"[HandleBotRespawn] Bot {userId} already being processed, skipping");
                    return;
                }

                // Bot exists in our tracking dictionary
                if (pracUsedBots.ContainsKey(userId))
                {
                    _botsBeingProcessed.Add(userId);
                    try
                    {
                        RespawnTrackedBot(player, userId);
                    }
                    finally
                    {
                        _botsBeingProcessed.Remove(userId);
                    }
                }
                // Bot spawned but we didn't spawn it - kick after delay
                else if (!isSpawningBot && player.IsHLTV)
                {
                    ScheduleSafeBotKick();
                }
            }
        }

        // ============================================================================
        // SAFE Tracked Bot Respawn
        // ============================================================================
        private void RespawnTrackedBot(CCSPlayerController player, int userId)
        {
            try
            {
                // Validate bot data exists
                if (
                    !pracUsedBots.TryGetValue(userId, out var botData)
                    || !botData.ContainsKey("position")
                )
                {
                    return;
                }

                // Validate player pawn
                if (player.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null)
                {
                    return;
                }

                // Get position safely
                if (botData["position"] is not Position botPosition)
                {
                    return;
                }

                // Teleport bot to stored position
                player.PlayerPawn.Value.Teleport(
                    botPosition.PlayerPosition,
                    botPosition.PlayerAngle,
                    new Vector(0, 0, 0)
                );

                // Handle crouch state safely
                if (
                    botData.ContainsKey("crouchstate")
                    && botData["crouchstate"] is bool isCrouched
                    && isCrouched
                )
                {
                    ApplyCrouchState(player);
                }

                // Handle collision disabling with owner
                if (
                    botData.ContainsKey("owner") && botData["owner"] is CCSPlayerController botOwner
                )
                {
                    if (IsPlayerValid(botOwner) && botOwner.PlayerPawn?.IsValid == true)
                    {
                        AddTimer(0.2f, () => SafelyDisableCollisions(botOwner, player));
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[RespawnTrackedBot] Error for bot {userId}: {ex.Message}");
                // Clean up on error
                lock (_botsDictLock)
                {
                    pracUsedBots.Remove(userId);
                }
            }
        }

        // ============================================================================
        // SAFE Crouch Application
        // ============================================================================
        private void ApplyCrouchState(CCSPlayerController player)
        {
            try
            {
                if (player.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null)
                    return;

                player.PlayerPawn.Value.Flags |= (uint)PlayerFlags.FL_DUCKING;

                if (player.PlayerPawn.Value.MovementServices != null)
                {
                    var movementService = new CCSPlayer_MovementServices(
                        player.PlayerPawn.Value.MovementServices.Handle
                    );

                    var capturedPlayer = player;
                    AddTimer(
                        0.1f,
                        () =>
                        {
                            if (
                                capturedPlayer?.IsValid == true
                                && capturedPlayer.PlayerPawn?.IsValid == true
                                && capturedPlayer.PlayerPawn.Value?.MovementServices != null
                            )
                            {
                                try
                                {
                                    movementService.DuckAmount = 1;
                                }
                                catch { }
                            }
                        }
                    );

                    AddTimer(
                        0.2f,
                        () =>
                        {
                            if (
                                capturedPlayer?.IsValid == true
                                && capturedPlayer.PlayerPawn?.IsValid == true
                                && capturedPlayer.PlayerPawn.Value?.Bot != null
                            )
                            {
                                try
                                {
                                    capturedPlayer.PlayerPawn.Value.Bot.IsCrouching = true;
                                }
                                catch { }
                            }
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                Log($"[ApplyCrouchState] Error: {ex.Message}");
            }
        }

        // ============================================================================
        // SAFE Collision Disabling - Wrapped in validation
        // ============================================================================
        private void SafelyDisableCollisions(CCSPlayerController p1, CCSPlayerController p2)
        {
            // Skip if either player is invalid
            if (
                !IsPlayerValid(p1)
                || !IsPlayerValid(p2)
                || p1.Connected != PlayerConnectedState.Connected
                || p2.Connected != PlayerConnectedState.Connected
            )
            {
                return;
            }

            // Additional validation before calling original method
            if (
                p1.PlayerPawn?.IsValid == true
                && p1.PlayerPawn.Value != null
                && p2.PlayerPawn?.IsValid == true
                && p2.PlayerPawn.Value != null
            )
            {
                try
                {
                    TemporarilyDisableCollisions(p1, p2);
                }
                catch (Exception ex)
                {
                    Log($"[SafelyDisableCollisions] Error: {ex.Message}");
                }
            }
        }

        // ============================================================================
        // SAFE Bot Kick Scheduling
        // ============================================================================
        private void ScheduleSafeBotKick()
        {
            AddTimer(
                2.5f,
                () =>
                {
                    try
                    {
                        // Double-check we're still in practice mode
                        if (!isPractice)
                            return;

                        Server.ExecuteCommand("bot_kick");
                    }
                    catch (Exception ex)
                    {
                        Log($"[ScheduleSafeBotKick] Error: {ex.Message}");
                    }
                }
            );
        }

        private float AbsolutDistance(CCSPlayerController player, CCSPlayerController bot)
        {
            try
            {
                if (
                    player?.PlayerPawn?.IsValid != true
                    || player.PlayerPawn.Value == null
                    || bot?.PlayerPawn?.IsValid != true
                    || bot.PlayerPawn.Value == null
                )
                    return float.MaxValue;

                var playerOrigin = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;
                var botOrigin = bot.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;

                if (playerOrigin == null || botOrigin == null)
                    return float.MaxValue;

                return MathF.Abs(playerOrigin.X - botOrigin.X)
                    + MathF.Abs(playerOrigin.Y - botOrigin.Y)
                    + MathF.Abs(playerOrigin.Z - botOrigin.Z);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private void SpawnBot(CCSPlayerController botOwner, bool crouch)
        {
            try
            {
                if (!IsPlayerValid(botOwner))
                    return;

                // ADDED: Validate botOwner has valid pawn and components
                if (
                    botOwner.PlayerPawn == null
                    || !botOwner.PlayerPawn.IsValid
                    || botOwner.PlayerPawn.Value == null
                    || botOwner.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin == null
                    || botOwner.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsRotation == null
                )
                {
                    Log($"[SpawnBot] Bot owner has invalid pawn or components");
                    return;
                }

                var botOwnerPawn = botOwner.PlayerPawn.Value;

                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>(
                    "cs_player_controller"
                );
                bool unusedBotFound = false;

                foreach (var tempPlayer in playerEntities)
                {
                    if (!IsPlayerValid(tempPlayer))
                        continue;
                    if (!tempPlayer.IsBot || tempPlayer.IsHLTV)
                        continue;
                    if (tempPlayer.UserId.HasValue)
                    {
                        bool isAlreadyUsed = false;
                        lock (_botsDictLock)
                        {
                            isAlreadyUsed = pracUsedBots.ContainsKey(tempPlayer.UserId.Value);
                        }

                        if (!isAlreadyUsed && unusedBotFound)
                        {
                            Log(
                                $"UNUSED BOT FOUND: {tempPlayer.UserId.Value} EXECUTING: kickid {tempPlayer.UserId.Value}"
                            );
                            Server.ExecuteCommand($"kickid {tempPlayer.UserId.Value}");
                            continue;
                        }
                        if (isAlreadyUsed)
                        {
                            continue;
                        }

                        // ADDED: Validate tempPlayer has valid pawn before proceeding
                        if (
                            tempPlayer.PlayerPawn == null
                            || !tempPlayer.PlayerPawn.IsValid
                            || tempPlayer.PlayerPawn.Value == null
                        )
                        {
                            Log(
                                $"[SpawnBot] Bot {tempPlayer.PlayerName} has invalid pawn, skipping"
                            );
                            continue;
                        }

                        // Create botOwnerPosition FIRST (before using it in dictionary)
                        Position botOwnerPosition = new Position(
                            botOwnerPawn.CBodyComponent!.SceneNode!.AbsOrigin,
                            botOwnerPawn.CBodyComponent!.SceneNode!.AbsRotation
                        );

                        // Now safely add to dictionary with lock
                        lock (_botsDictLock)
                        {
                            pracUsedBots[tempPlayer.UserId.Value] =
                                new Dictionary<string, object>();
                            pracUsedBots[tempPlayer.UserId.Value]["controller"] = tempPlayer;
                            pracUsedBots[tempPlayer.UserId.Value]["position"] = botOwnerPosition;
                            pracUsedBots[tempPlayer.UserId.Value]["owner"] = botOwner;
                            pracUsedBots[tempPlayer.UserId.Value]["crouchstate"] = crouch;
                        }

                        if (crouch)
                        {
                            // ADDED: Validate MovementServices before accessing
                            if (tempPlayer.PlayerPawn.Value.MovementServices != null)
                            {
                                CCSPlayer_MovementServices movementService = new(
                                    tempPlayer.PlayerPawn.Value.MovementServices.Handle
                                );
                                AddTimer(0.1f, () => movementService.DuckAmount = 1);
                                AddTimer(
                                    0.2f,
                                    () =>
                                    {
                                        if (tempPlayer.PlayerPawn?.Value?.Bot != null)
                                        {
                                            tempPlayer.PlayerPawn.Value.Bot.IsCrouching = true;
                                        }
                                    }
                                );
                            }
                        }

                        // Now safe - we validated PlayerPawn.Value above
                        tempPlayer.PlayerPawn.Value.Teleport(
                            botOwnerPosition.PlayerPosition,
                            botOwnerPosition.PlayerAngle,
                            new Vector(0, 0, 0)
                        );

                        // Your existing collision validation (already good!)
                        if (
                            IsPlayerValid(botOwner)
                            && IsPlayerValid(tempPlayer)
                            && botOwner.PlayerPawn != null
                            && botOwner.PlayerPawn.IsValid
                            && tempPlayer.PlayerPawn != null
                            && tempPlayer.PlayerPawn.IsValid
                        )
                        {
                            TemporarilyDisableCollisions(botOwner, tempPlayer);
                        }
                        unusedBotFound = true;
                    }
                }

                if (!unusedBotFound)
                {
                    PrintToAllChat(Localizer["matchzy.pm.botlimit"]);
                }

                isSpawningBot = false;
            }
            catch (JsonException ex)
            {
                Log($"[SpawnBot - FATAL] Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"[SpawnBot - FATAL] Unexpected error: {ex.Message}");
                isSpawningBot = false;
            }
        }

        public void TemporarilyDisableCollisions(CCSPlayerController p1, CCSPlayerController p2)
        {
            // PATCHED: Additional validation at method entry
            if (!IsPlayerValid(p1) || !IsPlayerValid(p2))
            {
                Log($"[CollisionFix] Invalid player controller(s) - skipping collision disable");
                return;
            }

            if (
                p1.PlayerPawn == null
                || !p1.PlayerPawn.IsValid
                || p1.PlayerPawn.Value == null
                || p2.PlayerPawn == null
                || !p2.PlayerPawn.IsValid
                || p2.PlayerPawn.Value == null
            )
            {
                Log($"[CollisionFix] Invalid player pawn(s) - skipping collision disable");
                return;
            }

            // PATCHED: Create unique key for this player pair
            string timerKey = $"{p1.UserId}_{p2.UserId}";

            // PATCHED: Kill existing timer for this specific pair
            if (collisionGroupTimers.ContainsKey(timerKey))
            {
                collisionGroupTimers[timerKey]?.Kill();
                collisionGroupTimers.Remove(timerKey);
            }

            // Reference collision code: https://github.com/Source2ZE/CS2Fixes/blob/f009e399ff23a81915e5a2b2afda20da2ba93ada/src/events.cpp#L150
            p1.PlayerPawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)
                CollisionGroup.COLLISION_GROUP_DEBRIS;
            p1.PlayerPawn.Value.Collision.CollisionGroup = (byte)
                CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2.PlayerPawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)
                CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2.PlayerPawn.Value.Collision.CollisionGroup = (byte)
                CollisionGroup.COLLISION_GROUP_DEBRIS;

            // TODO: call CollisionRulesChanged
            var p1p = p1.PlayerPawn;
            var p2p = p2.PlayerPawn;

            // PATCHED: Store timer in dictionary with unique key
            var timer = AddTimer(
                0.5f,
                () =>
                {
                    try
                    {
                        if (
                            p1p == null
                            || !p1p.IsValid
                            || p1p.Value == null
                            || !p1p.Value.IsValid
                            || p2p == null
                            || !p2p.IsValid
                            || p2p.Value == null
                            || !p2p.Value.IsValid
                        )
                        {
                            Log(
                                $"[CollisionFix] Player handle invalid - cleaning up timer for {timerKey}"
                            );
                            CleanupCollisionTimer(timerKey);
                            return;
                        }

                        // PATCHED: Additional null check for collision components
                        if (p1p.Value.Collision == null || p2p.Value.Collision == null)
                        {
                            Log(
                                $"[CollisionFix] Collision component null - cleaning up timer for {timerKey}"
                            );
                            CleanupCollisionTimer(timerKey);
                            return;
                        }

                        if (!DoPlayersCollide(p1p.Value, p2p.Value))
                        {
                            // Once they no longer collide
                            p1p.Value.Collision.CollisionAttribute.CollisionGroup = (byte)
                                CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            p1p.Value.Collision.CollisionGroup = (byte)
                                CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            p2p.Value.Collision.CollisionAttribute.CollisionGroup = (byte)
                                CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            p2p.Value.Collision.CollisionGroup = (byte)
                                CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            // TODO: call CollisionRulesChanged
                            CleanupCollisionTimer(timerKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[CollisionFix] Exception in collision timer callback: {ex.Message}");
                        CleanupCollisionTimer(timerKey);
                    }
                },
                TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE
            );

            collisionGroupTimers[timerKey] = timer;
        }

        private void CleanupCollisionTimer(string timerKey)
        {
            if (collisionGroupTimers.ContainsKey(timerKey))
            {
                collisionGroupTimers[timerKey]?.Kill();
                collisionGroupTimers.Remove(timerKey);
            }
        }

        public bool DoPlayersCollide(CCSPlayerPawn p1, CCSPlayerPawn p2)
        {
            Vector p1min,
                p1max,
                p2min,
                p2max;
            var p1pos = p1.AbsOrigin;
            var p2pos = p2.AbsOrigin;
            p1min = p1.Collision.Mins + p1pos!;
            p1max = p1.Collision.Maxs + p1pos!;
            p2min = p2.Collision.Mins + p2pos!;
            p2max = p2.Collision.Maxs + p2pos!;

            return p1min.X <= p2max.X
                && p1max.X >= p2min.X
                && p1min.Y <= p2max.Y
                && p1max.Y >= p2min.Y
                && p1min.Z <= p2max.Z
                && p1max.Z >= p2min.Z;
        }

        public void CleanupAllCollisionTimers()
        {
            foreach (var timer in collisionGroupTimers.Values)
            {
                timer?.Kill();
            }
            collisionGroupTimers.Clear();
        }

        private static void ElevatePlayer(CCSPlayerController? player)
        {
            if (
                player == null
                || !player.IsValid
                || !player.PlayerPawn.IsValid
                || player.PlayerPawn.Value == null
            )
                return;
            if (player.PlayerPawn.Value.CBodyComponent?.SceneNode == null)
                return;

            var origin = player.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin;
            player.PlayerPawn.Value.Teleport(
                new Vector(origin.X, origin.Y, origin.Z + 80.0f),
                player.PlayerPawn.Value.EyeAngles,
                new Vector(0, 0, 0)
            );
        }

        [ConsoleCommand("css_rs", "Removes bots from the practice session")]
        public void OnRestartRoundCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null)
                return;
            Server.ExecuteCommand("mp_restartgame 1");
        }

        [ConsoleCommand("css_nb", "")]
        [ConsoleCommand("css_nobot", "Removes the closest bot from the practice session")]
        public void OnNoBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || !IsPlayerValid(player))
                return;

            if (NativeAPI.GetCommandParamValue("-nobots", DataType.DATA_TYPE_INT, -1) == 1)
            {
                Server.PrintToConsole("[MatchZy] Bots are disabled due to '-nobots' flag.");
                PrintToAllChat(Localizer["matchzy.pm.nobots"]);
                return;
            }

            var closestBot = GetClosestBotOfPlayer(player);
            if (closestBot == null || !IsPlayerValid(closestBot) || !closestBot.UserId.HasValue)
                return;

            int botUserId = closestBot.UserId.Value;
            // IMPORTANT: Capture PlayerName NOW before NextFrame, as bot may become invalid
            string botName = closestBot.PlayerName;

            // Mark as being processed
            lock (_botsDictLock)
            {
                if (_botsBeingProcessed.Contains(botUserId))
                {
                    ReplyToUserCommand(player, "Bot is already being removed.");
                    return;
                }
                _botsBeingProcessed.Add(botUserId);
            }

            // Use NextFrame for safe execution
            Server.NextFrame(() =>
            {
                try
                {
                    // Re-validate before kicking
                    if (
                        closestBot.IsValid
                        && closestBot.PlayerPawn?.IsValid == true
                        && closestBot.Connected == PlayerConnectedState.Connected
                    )
                    {
                        Server.ExecuteCommand($"bot_kick {botName}");

                        // Clean up tracking after small delay
                        AddTimer(
                            0.1f,
                            () =>
                            {
                                lock (_botsDictLock)
                                {
                                    pracUsedBots.Remove(botUserId);
                                    _botsBeingProcessed.Remove(botUserId);
                                }
                            }
                        );
                    }
                    else
                    {
                        // Bot already invalid, just clean up
                        lock (_botsDictLock)
                        {
                            pracUsedBots.Remove(botUserId);
                            _botsBeingProcessed.Remove(botUserId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[OnNoBotCommand] Error kicking bot: {ex.Message}");
                    lock (_botsDictLock)
                    {
                        _botsBeingProcessed.Remove(botUserId);
                    }
                }
            });
        }

        public void CleanupBotTracking()
        {
            lock (_botsDictLock)
            {
                pracUsedBots.Clear();
                _botsBeingProcessed.Clear();
            }

            // Kill all collision timers
            if (collisionGroupTimers != null)
            {
                foreach (var timer in collisionGroupTimers.Values)
                {
                    timer?.Kill();
                }
                collisionGroupTimers.Clear();
            }
        }

        [ConsoleCommand("css_nbs", "Removes bots from the practice session")]
        [ConsoleCommand("css_nbots", "Removes bots from the practice session")]
        [ConsoleCommand("css_kickbots", "Removes bots from the practice session")]
        [ConsoleCommand("css_kbots", "Removes bots from the practice session")]
        [ConsoleCommand("css_clearbots", "")]
        [ConsoleCommand("css_removebots", "")]
        [ConsoleCommand("css_nobots", "Removes bots from the practice session")]
        public void OnNoBotsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null)
                return;
            Server.ExecuteCommand("bot_kick");
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
            CleanupAllCollisionTimers();
        }

        [ConsoleCommand("css_ff", "Fast forwards the timescale to 20 seconds")]
        [ConsoleCommand("css_fastforward", "Fast forwards the timescale to 20 seconds")]
        public void OnFFCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null)
                return;

            Dictionary<int, MoveType_t> preFastForwardMoveTypes = new();

            foreach (var key in playerData.Keys)
            {
                if (!IsPlayerValid(playerData[key]))
                    continue;
                preFastForwardMoveTypes[key] = playerData[key].PlayerPawn.Value!.MoveType;

                playerData[key].PlayerPawn.Value!.MoveType = MoveType_t.MOVETYPE_NONE;
                Schema.SetSchemaValue(
                    playerData[key].PlayerPawn.Value!.Handle,
                    "CBaseEntity",
                    "m_nActualMoveType",
                    0
                );
                Utilities.SetStateChanged(
                    playerData[key].PlayerPawn.Value!,
                    "CBaseEntity",
                    "m_MoveType"
                );
            }

            Server.PrintToChatAll($"{chatPrefix} Fastforwarding 10 seconds!");
            Server.ExecuteCommand("host_timescale 5");
            AddTimer(
                10.0f,
                () =>
                {
                    ResetFastForward(preFastForwardMoveTypes);
                }
            );
        }

        public void ResetFastForward(Dictionary<int, MoveType_t> preFastForwardMoveTypes)
        {
            if (!isPractice)
                return;
            Server.ExecuteCommand("host_timescale 1");
            foreach (var key in playerData.Keys)
            {
                if (!IsPlayerValid(playerData[key]))
                    continue;
                playerData[key].PlayerPawn.Value!.MoveType = preFastForwardMoveTypes[key];
                Schema.SetSchemaValue(
                    playerData[key].PlayerPawn.Value!.Handle,
                    "CBaseEntity",
                    "m_nActualMoveType",
                    (int)preFastForwardMoveTypes[key]
                );
                Utilities.SetStateChanged(
                    playerData[key].PlayerPawn.Value!,
                    "CBaseEntity",
                    "m_MoveType"
                );
            }
        }

        [ConsoleCommand("css_clear", "Removes all the available granades")]
        public void OnClearAllCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RemoveGrenadeEntities();
        }

        [ConsoleCommand("css_spec", "Switches team to Spectator")]
        public void OnSpecCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null)
                return;

            // Force respawn before switching to spec to clear any noclip/movement state
            if (player.PlayerPawn?.IsValid == true && player.PlayerPawn.Value != null)
            {
                // Reset movement type if noclip was enabled
                if (player.PlayerPawn.Value.MoveType == MoveType_t.MOVETYPE_NOCLIP)
                {
                    player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_WALK;
                }
            }

            SideSwitchCommand(player, CsTeam.Spectator);
        }

        [ConsoleCommand("css_fas", "Switches all other players to spectator")]
        [ConsoleCommand("css_watchme", "Switches all other players to spectator")]
        public void OnFASCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null)
                return;

            SideSwitchCommand(player, CsTeam.None);
        }

        [ConsoleCommand("css_noblind", "Disables flash effect for the player")]
        [ConsoleCommand("css_noflash", "Disables flash effect for the player")]
        public void OnNoFlashCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || player.UserId == null)
                return;

            int userId = player.UserId.Value;

            if (noFlashList.Contains(userId))
            {
                noFlashList.Remove(userId);
                ReplyToUserCommand(player, "Disabled noflash.");
            }
            else
            {
                noFlashList.Add(userId);
                ReplyToUserCommand(player, "Enabled noflash. Use .noflash again to disable.");
                Server.NextFrame(() =>
                {
                    if (!IsPlayerValid(player))
                        return;
                    KillFlashEffect(player);
                });
            }
        }

        [ConsoleCommand("css_breakrestore", "")]
        [ConsoleCommand("css_nobreak", "")]
        public void OnBreakRestoreCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice)
                return;

            var gameRules = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()
                ?.GameRules;

            if (gameRules != null)
            {
                CCSGameRules_PostCleanUp(gameRules);
                ReplyToUserCommand(player, $" {ChatColors.Yellow}Breakable props respawned!");
            }
        }

        [ConsoleCommand("css_break", "Breaks the breakable entities")]
        public void OnBreakCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice)
                return;

            // Get all breakable entities including doors
            var entities = Utilities
                .FindAllEntitiesByDesignerName<CBreakable>("prop_dynamic")
                .Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_breakable"))
                .Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("prop_door_rotating"))
                .Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_door"))
                .Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_door_rotating"));

            foreach (var entity in entities)
            {
                if (entity?.IsValid != true || entity.CBodyComponent?.SceneNode == null)
                    continue;

                var position = entity.CBodyComponent.SceneNode.AbsOrigin;
                var className = entity.DesignerName;
                var handle = entity.Handle.ToString();

                entity.AcceptInput("Break");
            }
        }

        public void KillFlashEffect(CCSPlayerController player)
        {
            if (!IsPlayerValid(player))
                return;
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null)
                return;
            playerPawn.FlashMaxAlpha = 0.5f;
        }

        // CsTeam.None is a special value to mean force all other players to spectator
        private void SideSwitchCommand(CCSPlayerController player, CsTeam team)
        {
            if (team > CsTeam.None)
            {
                if (player.TeamNum == (byte)CsTeam.Spectator)
                {
                    try
                    {
                        // Directly switch to the desired team
                        player.ChangeTeam(team);

                        // Add a small delay to ensure team switch is processed
                        AddTimer(
                            0.1f,
                            () =>
                            {
                                // Re-validate player after timer - may have disconnected
                                if (!IsPlayerValid(player))
                                    return;
                                player.Respawn();
                            }
                        );
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error switching team: {ex.Message}");
                        ReplyToUserCommand(
                            player,
                            Localizer.ForPlayer(player, "matchzy.pm.spectatorbroken")
                        );
                        return;
                    }
                }

                player.ChangeTeam(team);
                return;
            }

            Utilities
                .GetPlayers()
                .ForEach(
                    (x) =>
                    {
                        if (x.IsValid && !x.IsBot && x.UserId != player.UserId)
                        {
                            x.ChangeTeam(CsTeam.Spectator);
                        }
                    }
                );
        }

        // CsTeam.None is a special value to mean force all other players to spectator
        // private void SideSwitchCommand(CCSPlayerController player, CsTeam team)
        // {
        //     if (team > CsTeam.None)
        //     {
        //         if (player.TeamNum == (byte)CsTeam.Spectator)
        //         {
        //             // ReplyToUserCommand(player, "Switching to a team from spectator is currently broken, use the team menu.");
        //             ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.spectatorbroken"));
        //             return;
        //         }

        //         player.ChangeTeam(team);
        //         return;
        //     }

        //     Utilities.GetPlayers().ForEach((x) =>
        //     {
        //         if (x.IsValid && !x.IsBot && x.UserId != player.UserId)
        //         {
        //             x.ChangeTeam(CsTeam.Spectator);
        //         }
        //     });
        // }

        // RemoveGrenadeEntities SAFE
        public void RemoveGrenadeEntities()
        {
            if (!isPractice)
                return;

            // Snapshot entities first; removing during enumeration can crash the server
            var entities = new List<(CBaseEntity? entity, string label)>();

            entities.AddRange(
                Utilities
                    .FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>(
                        "smokegrenade_projectile"
                    )
                    .Select(e => ((CBaseEntity?)e, "smoke"))
            );
            entities.AddRange(
                Utilities
                    .FindAllEntitiesByDesignerName<CMolotovProjectile>("molotov_projectile")
                    .Select(e => ((CBaseEntity?)e, "molotov"))
            );
            entities.AddRange(
                Utilities
                    .FindAllEntitiesByDesignerName<CInferno>("inferno")
                    .Select(e => ((CBaseEntity?)e, "inferno"))
            );
            entities.AddRange(
                Utilities
                    .FindAllEntitiesByDesignerName<CHEGrenadeProjectile>("hegrenade_projectile")
                    .Select(e => ((CBaseEntity?)e, "hegrenade"))
            );
            entities.AddRange(
                Utilities
                    .FindAllEntitiesByDesignerName<CFlashbangProjectile>("flashbang_projectile")
                    .Select(e => ((CBaseEntity?)e, "flashbang"))
            );
            entities.AddRange(
                Utilities
                    .FindAllEntitiesByDesignerName<CDecoyProjectile>("decoy_projectile")
                    .Select(e => ((CBaseEntity?)e, "decoy"))
            );

            // Deduplicate by handle to avoid double-removal crashes
            var unique = new List<(CBaseEntity entity, string label)>();
            var seen = new HashSet<nint>();
            foreach (var (entity, label) in entities)
            {
                if (entity == null || entity.Handle == nint.Zero)
                    continue;
                if (!seen.Add(entity.Handle))
                    continue;
                unique.Add((entity, label));
            }

            // Defer actual removal to next frame to avoid touching entities mid-update
            Server.NextFrame(() =>
            {
                foreach (var (entity, label) in unique)
                {
                    SafeRemoveEntity(entity, label);
                }
            });
        }

        private void SafeRemoveEntity(CBaseEntity? entity, string label)
        {
            // Extra validation to avoid native crashes (invalid handle / already freed)
            if (entity == null || entity.Handle == nint.Zero || !entity.IsValid)
                return;

            try
            {
                // Some entities are safer to kill than remove directly
                entity.AcceptInput("Kill");
            }
            catch
            {
                // Fallback to Remove if Kill input is not supported
                try
                {
                    entity.Remove();
                }
                catch (Exception ex)
                {
                    Log($"[RemoveGrenadeEntities] Failed to remove {label}: {ex.Message}");
                }
            }
        }

        public void ExecDryRunCFG()
        {
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", dryrunCfgPath);

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath))
            {
                //Log($"[ExecDryRunCFG] Starting Dryrun! Executing Dryrun CFG from {dryrunCfgPath}");
                Server.ExecuteCommand($"exec {dryrunCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                //Log($"[ExecDryRunCFG] Starting Dryrun! Dryrun CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand(
                    "ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;"
                );
                Server.ExecuteCommand(
                    "cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 6;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_molotovusedelay 0;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_weapons_glow_on_ground 0;mp_win_panel_display_time 3;occlusion_test_async 0;spec_freeze_deathanim_time 0;spec_freeze_panel_extended_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_coaching_enabled 1;sv_competitive_official_5v5 1;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_holiday_mode 0;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_occlude_players 1;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;cash_team_loser_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;"
                );
            }
        }

        public void ExecExitDryCFG()
        {
            Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end");
        }

        public void ExecUnpracCommands()
        {
            Server.ExecuteCommand(
                "sv_cheats false;sv_grenade_trajectory_prac_pipreview false;sv_grenade_trajectory_prac_trailtime 0; mp_ct_default_grenades \"\"; mp_ct_default_primary \"\"; mp_t_default_grenades\"\"; mp_t_default_primary\"\"; mp_teammates_are_enemies false;"
            );
            Server.ExecuteCommand(
                "mp_death_drop_defuser true; mp_death_drop_taser true; mp_drop_knife_enable false; mp_death_drop_grenade 2; ammo_grenade_limit_total 4; mp_defuser_allocation 0; sv_infinite_ammo 0; mp_force_pick_time 15"
            );
        }

        public bool IsValidPositionForLastGrenade(CCSPlayerController player, int position)
        {
            int userId = player.UserId!.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nothrownnades"));
                return false;
            }

            if (lastGrenadesData[userId].Count < position)
            {
                PrintToPlayerChat(
                    player,
                    Localizer.ForPlayer(
                        player,
                        "matchzy.pm.grenadehistory",
                        $"{lastGrenadesData[userId].Count}"
                    )
                );
                return false;
            }

            return true;
        }

        public void RethrowSpecificNade(CCSPlayerController player, string nadeType)
        {
            if (!isPractice || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (
                !nadeSpecificLastGrenadeData.ContainsKey(userId)
                || !nadeSpecificLastGrenadeData[userId].ContainsKey(nadeType)
            )
            {
                PrintToPlayerChat(player, $"You have not thrown any {nadeType} yet!");
                return;
            }
            GrenadeThrownData grenadeThrown = nadeSpecificLastGrenadeData[userId][nadeType];
            if (grenadeThrown != null)
                AddTimer(grenadeThrown.Delay, () => grenadeThrown.Throw(player));
        }

        public void HandleBackCommand(CCSPlayerController player, string number)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (!string.IsNullOrWhiteSpace(number))
            {
                if (int.TryParse(number, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        lastGrenadesData[userId][positionNumber].LoadPosition(player);
                        // PrintToPlayerChat(player, $"Teleported to grenade of history position: {positionNumber+1}/{lastGrenadesData[userId].Count}");
                        PrintToPlayerChat(
                            player,
                            Localizer[
                                "matchzy.pm.tptogrenade",
                                $"{positionNumber + 1}/{lastGrenadesData[userId].Count}"
                            ]
                        );
                    }
                }
                else
                {
                    // PrintToPlayerChat(player, $"Invalid value for !back command. Please specify a valid non-negative number. Usage: !back <number>");
                    PrintToPlayerChat(player, Localizer["matchzy.pm.backinvalidvalue"]);
                    return;
                }
            }
            else
            {
                int thrownCount = lastGrenadesData.ContainsKey(userId)
                    ? lastGrenadesData[userId].Count
                    : 0;
                // ReplyToUserCommand(player, $"Usage: !back <number> (You've thrown {thrownCount} grenades till now)");
                ReplyToUserCommand(player, Localizer["matchzy.pm.backtonumber", thrownCount]);
            }
        }

        public void HandleThrowIndexCommand(CCSPlayerController player, string argString)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            int userId = player!.UserId!.Value;

            if (string.IsNullOrEmpty(argString))
            {
                int thrownCount = lastGrenadesData.ContainsKey(userId)
                    ? lastGrenadesData[userId].Count
                    : 0;
                // ReplyToUserCommand(player, $"Usage: !throwindex <number> (You've thrown {thrownCount} grenades till now)");
                ReplyToUserCommand(player, Localizer["matchzy.pm.throwindextonumber", thrownCount]);
                return;
            }

            string[] argsList = argString.Split();

            foreach (string arg in argsList)
            {
                if (int.TryParse(arg, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        GrenadeThrownData grenadeThrown = lastGrenadesData[userId][positionNumber];
                        AddTimer(grenadeThrown.Delay, () => grenadeThrown.Throw(player));
                        // PrintToPlayerChat(player, $"Throwing grenade of history position: {positionNumber+1}/{lastGrenadesData[userId].Count}");
                        PrintToPlayerChat(
                            player,
                            Localizer[
                                "matchzy.pm.throwgrenadehistory",
                                $"{positionNumber + 1}/{lastGrenadesData[userId].Count}"
                            ]
                        );
                    }
                }
                else
                {
                    // PrintToPlayerChat(player, $"'{arg}' is not a valid non-negative number for !throwindex command.");
                    PrintToPlayerChat(player, Localizer["matchzy.pm.backnegativenumber", arg]);
                }
            }
        }

        public void HandleDelayCommand(CCSPlayerController player, string delay)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (!isPractice || player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (string.IsNullOrWhiteSpace(delay))
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $"!delay <delay_in_seconds>")
                );
                return;
            }

            if (float.TryParse(delay, out float delayInSeconds) && delayInSeconds > 0)
            {
                if (IsValidPositionForLastGrenade(player, 0))
                {
                    lastGrenadesData[userId].Last().Delay = delayInSeconds;
                    PrintToPlayerChat(
                        player,
                        Localizer.ForPlayer(
                            player,
                            "matchzy.pm.delaygrenade",
                            $"{delayInSeconds:0.00}",
                            $"{lastGrenadesData[userId].Count}"
                        )
                    );
                }
            }
            else
            {
                PrintToPlayerChat(
                    player,
                    Localizer.ForPlayer(
                        player,
                        "matchzy.pm.delayvalidnumber",
                        $"{delayInSeconds:0.00}",
                        $"{lastGrenadesData[userId].Count}"
                    )
                );
                return;
            }
        }

        public void DisplayPracticeTimerCenter(int userId)
        {
            if (!playerData.ContainsKey(userId) || !playerTimers.ContainsKey(userId))
                return;
            if (!IsPlayerValid(playerData[userId]))
                return;
            playerTimers[userId].DisplayTimerCenter(playerData[userId]);
        }

        [ConsoleCommand("css_throw", "Throws the last thrown grenade")]
        [ConsoleCommand("css_rethrow", "Throws the last thrown grenade")]
        [ConsoleCommand("css_rt", "Throws the last thrown grenade")]
        public void OnRethrowCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                PrintToPlayerChat(player, $"You have not thrown any nade yet!");
                return;
            }
            GrenadeThrownData lastGrenade = lastGrenadesData[userId].Last();
            if (lastGrenade != null)
                AddTimer(lastGrenade.Delay, () => lastGrenade.Throw(player));
        }

        [ConsoleCommand("css_savepos", "Saves the player location")]
        public void OnSavePosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (
                !isPractice
                || player == null
                || !player.UserId.HasValue
                || player.PlayerPawn.Value == null
            )
                return;

            int userId = player.UserId.Value;
            var pawn = player.PlayerPawn.Value;
            Vector position = new(pawn.AbsOrigin?.X, pawn.AbsOrigin?.Y, pawn.AbsOrigin?.Z);
            QAngle angle = new(pawn.EyeAngles?.X, pawn.EyeAngles?.Y, pawn.EyeAngles?.Z);

            savedPlayerLocationData[userId] = new PlayerLocationData(position, angle);
            //Log($"[SavePos] Saved position for UserID {userId}, Position: {position}, Angle: {angle}!");
            PrintToPlayerChat(player, Localizer["matchzy.pm.savepos"]);
        }

        [ConsoleCommand("css_loadpos", "Loads the last saved player location")]
        public void OnLoadPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;

            if (player.TeamNum == (byte)CsTeam.Spectator)
                return;

            int userId = player.UserId.Value;
            if (!savedPlayerLocationData.TryGetValue(userId, out var playerLocationData))
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.notsavedpos"]);
                return;
            }

            playerLocationData.LoadPosition(player);
            PrintToPlayerChat(player, Localizer["matchzy.pm.loadpos"]);
        }

        [ConsoleCommand("css_throwsmoke", "Throws the last thrown smoke")]
        [ConsoleCommand("css_rethrowsmoke", "Throws the last thrown smoke")]
        public void OnRethrowSmokeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "smoke");
        }

        [ConsoleCommand("css_throwflash", "Throws the last thrown flash")]
        [ConsoleCommand("css_rethrowflash", "Throws the last thrown flash")]
        public void OnRethrowFlashCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "flash");
        }

        [ConsoleCommand("css_throwgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrowgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_thrownade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrownade", "Throws the last thrown he grenade")]
        public void OnRethrowGrenadeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "hegrenade");
        }

        [ConsoleCommand("css_throwmolotov", "Throws the last thrown molotov")]
        [ConsoleCommand("css_rethrowmolotov", "Throws the last thrown molotov")]
        public void OnRethrowMolotovCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "molotov");
        }

        [ConsoleCommand("css_throwdecoy", "Throws the last thrown decoy")]
        [ConsoleCommand("css_rethrowdecoy", "Throws the last thrown decoy")]
        public void OnRethrowDecoyCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "decoy");
        }

        [ConsoleCommand("css_last", "Teleports to the last thrown grenade position")]
        public void OnLastCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;

            // Prevent spectators from teleporting
            if (player.TeamNum == (byte)CsTeam.Spectator)
            {
                return;
            }

            int userId = player.UserId.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.notthrownnade"));
                return;
            }
            lastGrenadesData[userId].Last().LoadPosition(player);
        }

        [ConsoleCommand("css_back", "Teleports to the provided position in grenade thrown history")]
        public void OnBackCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;

            // Prevent spectators from teleporting
            if (player.TeamNum == (byte)CsTeam.Spectator)
            {
                return;
            }

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleBackCommand(player, commandArg);
            }
            else
            {
                int userId = player!.UserId!.Value;
                int thrownCount = lastGrenadesData.ContainsKey(userId)
                    ? lastGrenadesData[userId].Count
                    : 0;
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.pm.backtonumber", thrownCount)
                );
            }
        }

        [ConsoleCommand(
            "css_throwidx",
            "Throws grenade of provided position in grenade thrown history"
        )]
        [ConsoleCommand(
            "css_throwindex",
            "Throws grenade of provided position in grenade thrown history"
        )]
        public void OnThrowIndexCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
            {
                return;
            }

            if (command.ArgCount >= 2)
            {
                HandleThrowIndexCommand(player!, command.ArgString);
            }
            else
            {
                int userId = player!.UserId!.Value;
                int thrownCount = lastGrenadesData.ContainsKey(userId)
                    ? lastGrenadesData[userId].Count
                    : 0;
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.pm.throwindextonumber", thrownCount)
                );
            }
        }

        [ConsoleCommand("css_lastindex", "Returns index of the last thrown grenade")]
        public void OnLastIndexCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            if (IsValidPositionForLastGrenade(player!, 1))
            {
                PrintToPlayerChat(
                    player!,
                    Localizer.ForPlayer(
                        player,
                        "matchzy.pm.indexlastgrenade",
                        $"{lastGrenadesData[player!.UserId!.Value].Count}"
                    )
                );
            }
        }

        [ConsoleCommand(
            "css_delay",
            "Adds a delay to the last thrown grenade. Usage: !delay <delay_in_seconds>"
        )]
        public void OnDelayCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            if (command.ArgCount >= 2)
            {
                HandleDelayCommand(player!, command.ArgByIndex(1));
            }
            else
            {
                ReplyToUserCommand(
                    player,
                    Localizer.ForPlayer(player, "matchzy.cc.usage", $"!delay <delay_in_seconds>")
                );
            }
        }

        [ConsoleCommand("css_timer", "Starts a timer, use .timer again to stop it.")]
        public void OnTimerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            int userId = player!.UserId!.Value;
            if (playerTimers.ContainsKey(userId))
            {
                playerTimers[userId].KillTimer();
                double timerResult = playerTimers[userId].GetTimerResult();
                player.PrintToCenter($"Timer: {timerResult}s");
                PrintToPlayerChat(player, $"Timer stopped! Result: {timerResult}s");
                playerTimers.Remove(userId);
            }
            else
            {
                playerTimers[userId] = new PlayerPracticeTimer(PracticeTimerType.Immediate)
                {
                    StartTime = DateTime.Now,
                    Timer = AddTimer(
                        0.2f,
                        () => DisplayPracticeTimerCenter(userId),
                        TimerFlags.REPEAT
                    ),
                };
                PrintToPlayerChat(player, $"Timer started! User !timer to stop it.");
            }
        }

        [ConsoleCommand("css_sn", "Saves current nade position")]
        [ConsoleCommand("css_savenade", "Saves current nade position")]
        public void OnSaveNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            HandleSaveNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_ln", "Loades the nade with provided filter")]
        [ConsoleCommand("css_loadnade", "Loades the nade with provided filter")]
        public void OnLoadNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            HandleLoadNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_lin", "Lists the nade with provided filter")]
        [ConsoleCommand("css_listnades", "Lists the nade with provided filter")]
        public void OnListNadesCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            HandleListNadesCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_importnade", "Imports the nade with the given code")]
        [ConsoleCommand("css_in", "Imports the nade with the given code")]
        public void OnImportNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            HandleImportNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_deletenade", "Deletes the nade by name")]
        [ConsoleCommand("css_delnade", "Deletes the nade by name")]
        [ConsoleCommand("css_dn", "Deletes the nade by name")]
        public void OnDeleteNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            HandleDeleteNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_solid", "Toggles mp_solid_teammates in practice mode")]
        public void OnSolidCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            int solidValue = ConVar.Find("mp_solid_teammates")!.GetPrimitiveValue<int>();

            int newSolidValue = (solidValue == 0 || solidValue == 1) ? 2 : 1;

            ConVar.Find("mp_solid_teammates")!.SetValue(newSolidValue);

            PrintToAllChat($"mp_solid_teammates is now set to {newSolidValue}");
        }

        [GameEventHandler]
        public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player))
                return HookResult.Continue;

            // Clean up the slot so next player gets fresh state
            if (playerImpacts.ContainsKey(player!.Slot))
            {
                playerImpacts.Remove(player.Slot);
            }

            // Reset grenade preview for this slot
            _pipPreviewEnabled[player.Slot] = false;

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player) || player!.IsBot)
                return HookResult.Continue;

            if (isPractice)
            {
                // Force impacts OFF (default)
                player.ReplicateConVar("sv_showimpacts", "0");
                // Don't add to dictionary - let first .impacts command handle it

                // Force grenade preview ON (default in practice)
                _pipPreviewEnabled[player.Slot] = true;
                player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", "1");

                // Retry impacts after 2 seconds to ensure it sticks
                AddTimer(
                    2.0f,
                    () =>
                    {
                        if (!IsPlayerValid(player) || !player.IsValid)
                            return;

                        player.ReplicateConVar("sv_showimpacts", "0");
                    }
                );
            }

            return HookResult.Continue;
        }

        // impacts
        private Dictionary<int, bool> playerImpacts = new Dictionary<int, bool>();

        [ConsoleCommand("css_impacts", "Toggles sv_showimpacts in practice mode")]
        public void OnImpactsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            // If not in dictionary, initialize to TRUE since sv_showimpacts starts at "1" in practice
            if (!playerImpacts.ContainsKey(player!.Slot))
            {
                playerImpacts[player.Slot] = true; // Default is TRUE (on) because server has sv_showimpacts "1"
            }

            bool currentState = playerImpacts[player.Slot];

            // Toggle it
            bool enabled = !currentState;
            playerImpacts[player.Slot] = enabled;

            player.ReplicateConVar("sv_showimpacts", enabled ? "1" : "0");

            player.PrintToChat($" {ChatColors.Green}Show Impacts: {ChatColors.Default}{enabled}");
        }

        // nadepreview
        private readonly bool[] _pipPreviewEnabled = new bool[64];

        [ConsoleCommand("css_nadepreview", "Toggles nade preview mode for practices")]
        [ConsoleCommand("css_previewnade", "Toggles nade preview mode for practices")]
        [ConsoleCommand("css_cam", "Toggles nade preview mode for practices")]
        [ConsoleCommand("css_nadecam", "Toggles nade preview mode for practices")]
        [ConsoleCommand(
            "css_traj",
            "Toggles sv_grenade_trajectory_prac_pipreview in practice mode"
        )]
        [ConsoleCommand("css_pip", "Toggles sv_grenade_trajectory_prac_pipreview in practice mode")]
        public void OnTrajCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            // Toggle the player's personal preference
            bool enabled = !_pipPreviewEnabled[player!.Slot];
            _pipPreviewEnabled[player.Slot] = enabled;

            // Apply it client-side only
            player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", enabled ? "1" : "0");

            // Notify only the player (not all chat)
            player.PrintToChat(
                $" {ChatColors.Green}GrenadePreviewCam: {ChatColors.Default}{enabled}"
            );
        }

        // BACKUP TRAJ
        // public void OnTrajCommand(CCSPlayerController? player, CommandInfo? command)
        // {
        //     if (!isPractice || !IsPlayerValid(player)) return;

        //     bool trajValue = ConVar.Find("sv_grenade_trajectory_prac_pipreview")!.GetPrimitiveValue<bool>();

        //     Server.ExecuteCommand($"sv_grenade_trajectory_prac_pipreview {!trajValue}");

        //     PrintToAllChat($"{ChatColors.Green}Grenade-Preview Cam: {ChatColors.Default}{!trajValue}");
        // }

        [ConsoleCommand(
            "css_bestspawn",
            "Teleports you to your team's closest spawn from your current position"
        )]
        public void OnBestSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToBestSpawn(player!, player!.TeamNum);
        }

        [ConsoleCommand(
            "css_worstspawn",
            "Teleports you to your team's furthest spawn from your current position"
        )]
        public void OnWorstSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToWorstSpawn(player!, player!.TeamNum);
        }

        [ConsoleCommand(
            "css_bestctspawn",
            "Teleports you to CT team's closest spawn from your current position"
        )]
        public void OnBestCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToBestSpawn(player!, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand(
            "css_worstctspawn",
            "Teleports you to CT team's furthest spawn from your current position"
        )]
        public void OnWorstCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand(
            "css_besttspawn",
            "Teleports you to T team's closest spawn from your current position"
        )]
        public void OnBestTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToBestSpawn(player!, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand(
            "css_worsttspawn",
            "Teleports you to T team's furthest spawn from your current position"
        )]
        public void OnWorstTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand("css_showspawns", "Highlights all the competitive spawns")]
        public void OnShowSpawnsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            RemoveSpawnBeams();
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            foreach (Position spawn in spawnsData[(byte)CsTeam.CounterTerrorist])
            {
                ShowSpawnBeam(spawn, Color.Blue);
            }
            foreach (Position spawn in spawnsData[(byte)CsTeam.Terrorist])
            {
                ShowSpawnBeam(spawn, Color.Orange);
            }
        }

        [ConsoleCommand("css_hidespawns", "Hides the highlighted spawns")]
        public void OnHideSpawnsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            RemoveSpawnBeams();
        }

        private void ResetAllPlayerPracticeSettings(bool enteringPractice)
        {
            var players = Utilities.GetPlayers().Where(p => IsPlayerValid(p) && !p.IsBot);

            foreach (var player in players)
            {
                if (enteringPractice)
                {
                    // Entering practice mode - set practice defaults
                    player.ReplicateConVar("sv_showimpacts", "0");
                    if (playerImpacts.ContainsKey(player.Slot))
                        playerImpacts.Remove(player.Slot);

                    _pipPreviewEnabled[player.Slot] = true;
                    player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", "1");
                }
                else
                {
                    // Exiting practice mode - turn everything OFF
                    player.ReplicateConVar("sv_showimpacts", "0");
                    player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", "0");
                    if (playerImpacts.ContainsKey(player.Slot))
                        playerImpacts.Remove(player.Slot);
                    _pipPreviewEnabled[player.Slot] = false;
                }
            }
        }

        // [ConsoleCommand("css_color", "Set cl_color of current player")]
        // public void OnColorCommand(CCSPlayerController? player, CommandInfo? command)
        // {
        //     if (!IsPlayerValid(player)) return;

        //     if(command.ArgCount < 2){
        //         PrintToPlayerChat(player, $"add number of color, same as cl_color <number>");
        //         return;
        //     }

        //     PrintToPlayerChat(player, $"color was: {player.CompTeammateColor}, set to: {command.ArgByIndex(1)}");
        //     player.CompTeammateColor = int.Parse(command.ArgByIndex(1));
        // }

        public void TeleportPlayerToBestSpawn(CCSPlayerController player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns))
                return;
            var playerPawn = player?.PlayerPawn?.Value;
            var playerPosition = playerPawn?.CBodyComponent?.SceneNode?.AbsOrigin;
            if (playerPawn == null || playerPosition == null) return;
            int closestIndex = -1;
            double minDistance = double.MaxValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = index;
                }
            }

            playerPawn.Teleport(
                teamSpawns[closestIndex].PlayerPosition,
                teamSpawns[closestIndex].PlayerAngle,
                new Vector(0, 0, 0)
            );
        }

        public void TeleportPlayerToWorstSpawn(CCSPlayerController player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns))
                return;
            var playerPawn = player?.PlayerPawn?.Value;
            var playerPosition = playerPawn?.CBodyComponent?.SceneNode?.AbsOrigin;
            if (playerPawn == null || playerPosition == null) return;
            int farthestIndex = -1;
            double maxDistance = double.MinValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthestIndex = index;
                }
            }

            playerPawn.Teleport(
                teamSpawns[farthestIndex].PlayerPosition,
                teamSpawns[farthestIndex].PlayerAngle,
                new Vector(0, 0, 0)
            );
        }
    }
}
