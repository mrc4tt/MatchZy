using System;
using CounterStrikeSharp.API;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    // Players-vs-bots matches. Two team keys in the match config:
    //
    //   "players": "any"   the team is open to any human who is not on another roster (no
    //                      SteamIDs needed). Named rosters keep working, and can be mixed.
    //   "bots": true       the team is played by bots, filled up to players_per_team, with an
    //                      optional "bot_difficulty" (0-3, default 2). Humans cannot join it.
    //
    // A bot team cannot veto, ready, knife or unpause, so a bot match must have a fixed map list
    // (the load is refused otherwise), knife sides are drawn at random instead, the bot side
    // always counts as ready/unpaused, and autopause never counts it short.
    public partial class MatchZy
    {
        private static bool IsOpenRosterToken(JToken? players)
        {
            if (players is not JValue { Type: JTokenType.String } value)
                return false;
            string text = ((string?)value ?? "").Trim();
            return text.Equals("any", StringComparison.OrdinalIgnoreCase) || text == "*";
        }

        private static bool IsBotTeamToken(JToken? team)
        {
            JToken? bots = team?["bots"];
            if (bots == null)
                return false;
            return bots.Type switch
            {
                JTokenType.Boolean => bots.Value<bool>(),
                JTokenType.Integer => bots.Value<long>() > 0,
                JTokenType.String => bool.TryParse(bots.ToString(), out bool b) ? b : int.TryParse(bots.ToString(), out int n) && n > 0,
                _ => false,
            };
        }

        private static int BotDifficultyFrom(JToken? team)
        {
            JToken? token = team?["bot_difficulty"];
            if (token != null && int.TryParse(token.ToString(), out int difficulty))
                return Math.Clamp(difficulty, 0, 3);
            return 2;
        }

        private Team? BotTeam() => matchzyTeam1.botTeam ? matchzyTeam1 : matchzyTeam2.botTeam ? matchzyTeam2 : null;

        private Team? OpenRosterTeam() => matchzyTeam1.openRoster ? matchzyTeam1 : matchzyTeam2.openRoster ? matchzyTeam2 : null;

        private bool HasBotTeam() => BotTeam() != null;

        // True when the given engine team (2 = T, 3 = CT) is currently played by the bot team.
        private bool IsBotSide(int teamNum)
        {
            Team? botTeam = BotTeam();
            string? side = teamNum == 3 ? "CT" : teamNum == 2 ? "TERRORIST" : null;
            return botTeam != null && side != null && reverseTeamSides.TryGetValue(side, out Team? team) && team == botTeam;
        }

        // Keep the bot team's bots on its current side. Idempotent, so it is re-run after every
        // cfg exec (warmup/knife/live cfgs all set bot_quota 0) and at every round start, which
        // also follows halftime/overtime swaps: the engine moves the bots with their team, and
        // bot_join_team keeps any replacement bot on the right side.
        private void ApplyBotTeam()
        {
            Team? botTeam = BotTeam();
            if (botTeam == null || !teamSides.TryGetValue(botTeam, out string? side))
                return;

            string joinTeam = side == "CT" ? "ct" : "t";
            int count = Math.Max(0, matchConfig.PlayersPerTeam);
            Server.ExecuteCommand(
                $"mp_autoteambalance 0; mp_limitteams 0; bot_quota_mode normal; " +
                $"bot_difficulty {botTeam.botDifficulty}; bot_join_team {joinTeam}; bot_quota {count}");
        }

        private void RemoveBotTeamBots()
        {
            if (HasBotTeam())
                KickAllBotsProtectCSTV();
        }
    }
}
