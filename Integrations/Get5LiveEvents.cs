using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Anything but "legacy" means the Get5 format, so a typo falls back to the default.
        private bool UseGet5Events => !string.Equals(eventsFormat.Value?.Trim(), "legacy", StringComparison.OrdinalIgnoreCase);

        // Wall-clock start of the current round's play time (freeze time end), for round_time.
        // Null during freeze time.
        private long? liveRoundPlayStartMs;

        private void MarkLiveRoundPlayStart() => liveRoundPlayStartMs = Environment.TickCount64;

        private void ClearLiveRoundPlayStart() => liveRoundPlayStartMs = null;

        // Milliseconds since freeze time ended this round (Get5's round_time), 0 during freeze time.
        private int LiveRoundTimeMs()
        {
            if (liveRoundPlayStartMs == null)
                return 0;
            long elapsed = Environment.TickCount64 - liveRoundPlayStartMs.Value;
            return elapsed <= 0 ? 0 : (int)Math.Min(elapsed, int.MaxValue);
        }

        private static string? Get5Side(int teamNum)
        {
            return teamNum switch
            {
                (int)CsTeam.CounterTerrorist => "ct",
                (int)CsTeam.Terrorist => "t",
                (int)CsTeam.Spectator => "spec",
                _ => null,
            };
        }

        private static Get5PlayerInfo Get5Player(CCSPlayerController player)
        {
            int userId = player.UserId ?? 0;
            return new Get5PlayerInfo
            {
                SteamId = player.IsBot ? $"BOT-{userId}" : player.SteamID.ToString(),
                Name = player.PlayerName,
                UserId = userId,
                Side = Get5Side(player.TeamNum),
                IsBot = player.IsBot,
            };
        }

        private static Get5PlayerInfo? Get5PlayerOrNull(CCSPlayerController? player)
        {
            return player != null && player.IsValid ? Get5Player(player) : null;
        }

        // CS2 item definition indexes (items_game.txt) by weapon name. Get5 on CS:GO sent SourceMod's
        // CSWeaponID here, which does not exist in CS2; the definition index is CS2's own weapon id.
        private static readonly Dictionary<string, int> WeaponDefIndex = new(StringComparer.OrdinalIgnoreCase)
        {
            { "deagle", 1 }, { "elite", 2 }, { "fiveseven", 3 }, { "glock", 4 }, { "ak47", 7 }, { "aug", 8 },
            { "awp", 9 }, { "famas", 10 }, { "g3sg1", 11 }, { "galilar", 13 }, { "m249", 14 }, { "m4a1", 16 },
            { "mac10", 17 }, { "p90", 19 }, { "mp5sd", 23 }, { "ump45", 24 }, { "xm1014", 25 }, { "bizon", 26 },
            { "mag7", 27 }, { "negev", 28 }, { "sawedoff", 29 }, { "tec9", 30 }, { "taser", 31 }, { "hkp2000", 32 },
            { "mp7", 33 }, { "mp9", 34 }, { "nova", 35 }, { "p250", 36 }, { "scar20", 38 }, { "sg556", 39 },
            { "ssg08", 40 }, { "knife", 42 }, { "flashbang", 43 }, { "hegrenade", 44 }, { "smokegrenade", 45 },
            { "molotov", 46 }, { "decoy", 47 }, { "incgrenade", 48 }, { "c4", 49 }, { "knife_t", 59 },
            { "m4a1_silencer", 60 }, { "usp_silencer", 61 }, { "cz75a", 63 }, { "revolver", 64 },
        };

        private static Get5WeaponInfo Get5Weapon(string? weapon)
        {
            string name = (weapon ?? "").Trim();
            if (name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("weapon_".Length);
            if (name.Length == 0)
                name = "unknown";
            // Knife skins (knife_karambit, bayonet, ...) all report as some knife name.
            int id = WeaponDefIndex.TryGetValue(name, out int def) ? def
                : name.StartsWith("knife", StringComparison.OrdinalIgnoreCase) || name == "bayonet" ? 42
                : 0;
            return new Get5WeaponInfo { Name = name, Id = id };
        }

        // "A"/"B" from GetPlantedBombSite to Get5's "a"/"b".
        private string? Get5BombSite(bool refresh = false)
        {
            string site = GetPlantedBombSite(refresh);
            return site == "A" ? "a" : site == "B" ? "b" : null;
        }

        // Milliseconds left on the planted bomb (blow time cached at bomb_planted), 0 when unknown.
        private int BombTimeRemainingMs()
        {
            if (plantedBombBlowTime == null)
                return 0;
            float left = plantedBombBlowTime.Value - Server.CurrentTime;
            return left <= 0 ? 0 : (int)(left * 1000);
        }

        // Pause type and team of the pause in progress, so game_unpaused can repeat them (Get5 sends both).
        private string lastGet5PauseType = "admin";
        private string? lastGet5PauseTeam;

        // Pause events are built in many places; in the Get5 format they are translated here instead
        // of at every call site. Everything else is passed through.
        private MatchZyEvent ToGet5Event(MatchZyEvent @event)
        {
            switch (@event)
            {
                case MatchPausedLiveEvent paused:
                    lastGet5PauseType = paused.PauseType switch
                    {
                        "tech" or "auto" => "technical",
                        "admin" => "admin",
                        _ => "tactical",
                    };
                    lastGet5PauseTeam = paused.TeamName == null ? null
                        : paused.TeamName == matchzyTeam1.teamName ? "team1"
                        : paused.TeamName == matchzyTeam2.teamName ? "team2"
                        : null;
                    return new Get5MatchPauseEvent("game_paused")
                    {
                        MatchId = paused.MatchId,
                        MapNumber = paused.MapNumber,
                        Team = lastGet5PauseTeam,
                        PauseType = lastGet5PauseType,
                        RoundNumber = paused.RoundNumber,
                        MaxDuration = paused.MaxDuration,
                    };
                case MatchUnpausedLiveEvent unpaused:
                    // A pause that sent no game_paused (round restore) ends here too: report it as
                    // an admin pause instead of repeating an older pause's team and type.
                    var unpausedEvent = new Get5MatchPauseEvent("game_unpaused")
                    {
                        MatchId = unpaused.MatchId,
                        MapNumber = unpaused.MapNumber,
                        Team = lastGet5PauseTeam,
                        PauseType = lastGet5PauseType,
                        RoundNumber = unpaused.RoundNumber,
                    };
                    lastGet5PauseType = "admin";
                    lastGet5PauseTeam = null;
                    return unpausedEvent;
                default:
                    return @event;
            }
        }
    }
}
