using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    public class Team
    {
        // Do not transfer engine-backed coach controllers to the backup worker.
        // Coaches are excluded from the serialized format already.
        internal Team SnapshotForBackup() => new()
        {
            id = id,
            teamName = teamName,
            teamFlag = teamFlag,
            teamTag = teamTag,
            teamPlayers = teamPlayers?.DeepClone(),
            teamCoaches = teamCoaches?.DeepClone(),
            seriesScore = seriesScore,
            openRoster = openRoster,
            botTeam = botTeam,
            botDifficulty = botDifficulty,
        };

        [JsonPropertyName("id")]
        public string id = "";

        [JsonPropertyName("teamname")]
        public required string teamName;

        [JsonPropertyName("teamflag")]
        public string teamFlag = "";

        [JsonPropertyName("teamtag")]
        public string teamTag = "";

        [JsonPropertyName("teamplayers")]
        public JToken? teamPlayers;

        // "coaches": optional roster of SteamIDs (same shapes as "players") that may join this team
        // as its coach. Listed coaches are made coaches automatically on connect.
        [JsonPropertyName("teamcoaches")]
        public JToken? teamCoaches;

        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public HashSet<CCSPlayerController> coach = [];

        [JsonPropertyName("seriesscore")]
        public int seriesScore = 0;

        // "players": "any" - any human not on another roster plays for this team.
        [JsonPropertyName("openroster")]
        public bool openRoster = false;

        // "bots": true - this team is played by bots (see Match/BotTeam.cs).
        [JsonPropertyName("botteam")]
        public bool botTeam = false;

        [JsonPropertyName("botdifficulty")]
        public int botDifficulty = 2;
    }

    public partial class MatchZy
    {
        [ConsoleCommand("css_coach", "Sets coach for the requested team")]
        public void OnCoachCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleCoachCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_play", "Sets coach for the requested team")]
        [ConsoleCommand("css_uncoach", "Sets coach for the requested team")]
        public void OnUnCoachCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !player.PlayerPawn.IsValid)
                return;
            if (isPractice)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.uncoachmatchonly"));
                return;
            }
            // Listed only as a coach (not as a player): uncoaching would put an extra player on the team.
            if (GetRosteredCoachTeam(player.SteamID) != null
                && !LookupRosterEntry(matchzyTeam1.teamPlayers, player.SteamID)
                && !LookupRosterEntry(matchzyTeam2.teamPlayers, player.SteamID))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.uncoachlisted"));
                return;
            }

            if (matchzyTeam1.coach.Contains(player))
            {
                player.Clan = "";
                matchzyTeam1.coach.Remove(player);
                SetPlayerVisible(player);
            }
            else if (matchzyTeam2.coach.Contains(player))
            {
                player.Clan = "";
                matchzyTeam2.coach.Remove(player);
                SetPlayerVisible(player);
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.notcoaching"));
                return;
            }

            if (player.InGameMoneyServices != null)
                player.InGameMoneyServices.Account = 0;

            // Give the ex-coach a competitive teammate color back (they were forced to -1).
            Server.NextFrame(EnforceCompetitiveTeammateColors);

            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.matchmsg.uncoached"));
        }

        [ConsoleCommand("matchzy_addplayer", "Adds player to the provided team")]
        [ConsoleCommand("get5_addplayer", "Adds player to the provided team")]
        public void OnAddPlayerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player != null || command == null)
                return;
            if (!isMatchSetup)
            {
                command.ReplyToCommand("No match is setup!");
                return;
            }
            if (IsHalfTimePhase())
            {
                command.ReplyToCommand("Cannot add players during halftime. Please wait until the next round starts.");
                return;
            }
            if (command.ArgCount < 3)
            {
                command.ReplyToCommand("Usage: matchzy_addplayer <steam64> <team> \"<name>\"");
                return;
            }

            string playerSteamId = command.ArgByIndex(1);
            string playerTeam = command.ArgByIndex(2);
            string playerName = command.ArgByIndex(3);
            bool success;
            if (playerTeam == "team1")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchzyTeam1.teamPlayers);
            }
            else if (playerTeam == "team2")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchzyTeam2.teamPlayers);
            }
            else if (playerTeam == "spec")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchConfig.Spectators);
            }
            else
            {
                command.ReplyToCommand("Unknown team: must be one of team1, team2, spec");
                return;
            }
            if (!success)
            {
                command.ReplyToCommand($"Failed to add player {playerName} to {playerTeam}. They may already be on a team or you provided an invalid Steam ID.");
                return;
            }
            command.ReplyToCommand($"Player {playerName} added to {playerTeam} successfully!");
        }

        [ConsoleCommand("matchzy_removeplayer", "Removes the player from all the teams")]
        [ConsoleCommand("get5_removeplayer", "Removes the player from all the teams")]
        [CommandHelper(minArgs: 1, usage: "<steam64>")]
        public void OnRemovePlayerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player != null || command == null)
                return;
            if (!isMatchSetup)
            {
                command.ReplyToCommand("No match is setup!");
                return;
            }
            if (IsHalfTimePhase())
            {
                command.ReplyToCommand("Cannot remove players during halftime. Please wait until the next round starts.");
                return;
            }

            string arg = command.GetArg(1);

            if (!ulong.TryParse(arg, out ulong steamId))
            {
                command.ReplyToCommand($"Invalid Steam64");
                return;
            }

            bool success = RemovePlayerFromTeam(steamId.ToString());
            if (success)
            {
                command.ReplyToCommand($"Successfully removed player {steamId}");
                CCSPlayerController? removedPlayer = Utilities.GetPlayerFromSteamId(steamId);
                if (IsPlayerValid(removedPlayer))
                {
                    //PrintToAllChat($"{RED} Kicking player {GREEN}{removedPlayer!.PlayerName}  {RED}Not a player in this game.");
                    //KickPlayer(removedPlayer);
                }
            }
            else
            {
                command.ReplyToCommand($"Player {steamId} not found in any team or the Steam ID was invalid.");
            }
        }

        /// <summary>
        /// Copies the serialized fields of <paramref name="source"/> onto <paramref name="target"/>,
        /// keeping the target's object identity. Required because teamSides is keyed by Team
        /// reference; see the call site in RestoreRoundBackup. The runtime-only `coach` set is
        /// intentionally not copied - it holds live controllers and is JsonIgnore'd.
        /// </summary>
        private static void CopyTeamData(Team source, Team target)
        {
            target.id = source.id;
            target.teamName = source.teamName;
            target.teamFlag = source.teamFlag;
            target.teamTag = source.teamTag;
            target.teamPlayers = source.teamPlayers;
            target.teamCoaches = source.teamCoaches;
            target.seriesScore = source.seriesScore;
            target.openRoster = source.openRoster;
            target.botTeam = source.botTeam;
            target.botDifficulty = source.botDifficulty;
        }

        public bool AddPlayerToTeam(string steamId, string name, JToken? team)
        {
            // Shape-agnostic lookups: indexing a JArray roster with a string key throws.
            if (!ulong.TryParse(steamId, out ulong parsedSteamId))
                return false;
            if (LookupRosterEntry(matchzyTeam1.teamPlayers, parsedSteamId))
                return false;
            if (LookupRosterEntry(matchzyTeam2.teamPlayers, parsedSteamId))
                return false;
            if (LookupRosterEntry(matchConfig.Spectators, parsedSteamId))
                return false;

            if (team is JObject jObjectTeam)
            {
                jObjectTeam.Add(steamId, name);
                LoadClientNames();
                return true;
            }
            else if (team is JArray jArrayTeam)
            {
                // An array roster holds SteamID64s only (no names), so store the id.
                jArrayTeam.Add(steamId);
                LoadClientNames();
                return true;
            }
            return false;
        }

        public bool RemovePlayerFromTeam(string steamId)
        {
            List<JToken?> teams = [matchzyTeam1.teamPlayers, matchzyTeam2.teamPlayers, matchConfig.Spectators];

            bool removed = false;
            foreach (var team in teams)
            {
                if (team is JObject jObjectTeam)
                {
                    removed |= jObjectTeam.Remove(steamId);
                }
                else if (team is JArray jArrayTeam)
                {
                    // JArray.Remove compares token references, so match the SteamID by value.
                    foreach (var entry in jArrayTeam.Where(e => (e.Type == JTokenType.String || e.Type == JTokenType.Integer) && e.ToString() == steamId).ToList())
                    {
                        entry.Remove();
                        removed = true;
                    }
                }
            }
            if (removed)
                LoadClientNames();
            return removed;
        }
    }
}
