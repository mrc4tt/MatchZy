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

        // SourceMod's CSWeaponID (cstrike.inc), which Get5 sends as weapon.id and G5API stores. Weapons
        // without one (the MP5-SD), fire ("inferno"), the bomb ("planted_c4") and the world are 0, as in Get5.
        private static readonly Dictionary<string, int> Get5WeaponIds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["p228"] = 1, ["glock"] = 2, ["scout"] = 3, ["hegrenade"] = 4, ["xm1014"] = 5, ["c4"] = 6, ["mac10"] = 7,
            ["aug"] = 8, ["smokegrenade"] = 9, ["elite"] = 10, ["fiveseven"] = 11, ["ump45"] = 12, ["sg550"] = 13,
            ["galil"] = 14, ["famas"] = 15, ["usp"] = 16, ["awp"] = 17, ["mp5navy"] = 18, ["m249"] = 19, ["m3"] = 20,
            ["m4a1"] = 21, ["tmp"] = 22, ["g3sg1"] = 23, ["flashbang"] = 24, ["deagle"] = 25, ["sg552"] = 26, ["ak47"] = 27,
            ["knife"] = 28, ["p90"] = 29, ["shield"] = 30, ["kevlar"] = 31, ["assaultsuit"] = 32, ["nightvision"] = 33,
            ["galilar"] = 34, ["bizon"] = 35, ["mag7"] = 36, ["negev"] = 37, ["sawedoff"] = 38, ["tec9"] = 39, ["taser"] = 40,
            ["hkp2000"] = 41, ["mp7"] = 42, ["mp9"] = 43, ["nova"] = 44, ["p250"] = 45, ["scar17"] = 46, ["scar20"] = 47,
            ["sg556"] = 48, ["ssg08"] = 49, ["knifegg"] = 50, ["molotov"] = 51, ["decoy"] = 52, ["incgrenade"] = 53,
            ["defuser"] = 54, ["heavyassaultsuit"] = 55, ["cutters"] = 56, ["healthshot"] = 57, ["knife_t"] = 59,
            ["m4a1_silencer"] = 60, ["usp_silencer"] = 61, ["cz75a"] = 63, ["revolver"] = 64, ["tagrenade"] = 68,
            ["fists"] = 69, ["breachcharge"] = 70, ["tablet"] = 72, ["melee"] = 74, ["axe"] = 75, ["hammer"] = 76,
            ["spanner"] = 78, ["knife_ghost"] = 80, ["firebomb"] = 81, ["diversion"] = 82, ["frag_grenade"] = 83,
            ["snowball"] = 84, ["bumpmine"] = 85, ["bayonet"] = 500, ["knife_css"] = 503, ["knife_flip"] = 505,
            ["knife_gut"] = 506, ["knife_karambit"] = 507, ["knife_m9_bayonet"] = 508, ["knife_tactical"] = 509,
            ["knife_falchion"] = 512, ["knife_survival_bowie"] = 514, ["knife_butterfly"] = 515, ["knife_push"] = 516,
            ["knife_cord"] = 517, ["knife_canis"] = 518, ["knife_ursus"] = 519, ["knife_gypsy_jackknife"] = 520,
            ["knife_outdoor"] = 521, ["knife_stiletto"] = 522, ["knife_widowmaker"] = 523, ["knife_skeleton"] = 525,
        };

        private static Get5WeaponInfo Get5Weapon(string? weapon)
        {
            string name = (weapon ?? "").Trim();
            if (name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("weapon_".Length);
            if (name.Length == 0)
                name = "unknown";
            return new Get5WeaponInfo { Name = name, Id = Get5WeaponIds.TryGetValue(name, out int id) ? id : 0 };
        }

        // Rounds played when the current round started: Get5's round_number for round_end, which fires
        // after the score already counts the round. Set at round start and by a round restore.
        private int liveRoundNumber;

        // A round restore sends backup_loaded and the restored round's round_start itself, so the
        // round_start of the engine's restart that follows the load is not sent again.
        private bool roundStartSentByRestore;

        private void SendBackupLoadedEvents(string fileName, int roundNumber)
        {
            liveRoundNumber = roundNumber;
            PublishEvent(new MatchZyBackupLoadedEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                RoundNumber = roundNumber,
                FileName = fileName,
            });
            if (!UseGet5Events)
                return;
            // G5API drops the kills and plants of the undone rounds on this round_start.
            roundStartSentByRestore = true;
            PublishEvent(new RoundStartLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
                RoundNumber = roundNumber,
            });
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
                    // .pause (both teams must unpause) and automatic pauses are technical, as in
                    // upstream MatchZy; a round restore's pause is Get5's "backup".
                    lastGet5PauseType = paused.PauseType switch
                    {
                        "tech" or "auto" or "pause" => "technical",
                        "admin" => "admin",
                        "backup" => "backup",
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
