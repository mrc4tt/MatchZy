using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Live scorebot events for utility and the bomb carrier: grenade_thrown, grenade_detonated,
        // player_blinded, bomb_pickup, bomb_dropped, bomb_exploded. Same gating as player_hurt:
        // only while the match is live and a remote log URL is set.
        // player_kill bookkeeping. Map kills reset when a map goes live (ResetAdvancedStats); round
        // kills and the opening-kill flag reset when the round number changes.
        private readonly Dictionary<ulong, int> liveMapKills = new();
        private readonly Dictionary<ulong, int> liveRoundKills = new();
        private int liveKillRound = -1;
        private bool liveRoundFirstKillDone = false;

        // Called at every live round start: a restore of the current round replays the same round
        // number, so keying the reset on the round number alone carried the aborted attempt's kills over.
        private void ResetLiveRoundKillCounters()
        {
            liveRoundKills.Clear();
            liveRoundFirstKillDone = false;
            liveKillRound = GetRoundNumer();
        }

        private void ResetLiveKillCounters()
        {
            liveMapKills.Clear();
            liveRoundKills.Clear();
            liveKillRound = -1;
            liveRoundFirstKillDone = false;
        }

        private HookResult SendPlayerKillEvent(EventPlayerDeath @event)
        {
            try
            {
                if (!ShouldSendLiveEvent())
                    return HookResult.Continue;

                var victim = @event.Userid;
                var killer = @event.Attacker;
                if (victim == null || !victim.IsValid || killer == null || !killer.IsValid)
                    return HookResult.Continue; // world damage, fall, bomb: a death, not a kill
                if (killer == victim || IsMatchCoach(victim))
                    return HookResult.Continue; // suicide / the coach's freeze-time removal

                int round = GetRoundNumer();
                if (round != liveKillRound)
                {
                    liveKillRound = round;
                    liveRoundKills.Clear();
                    liveRoundFirstKillDone = false;
                }

                bool teamKill = killer.TeamNum == victim.TeamNum;
                bool firstKill = false;
                int roundKills = 0;
                int mapKills = 0;
                ulong killerKey = killer.IsBot ? BotStatsId(killer.PlayerName) : killer.SteamID;
                if (!teamKill)
                {
                    firstKill = !liveRoundFirstKillDone;
                    liveRoundFirstKillDone = true;
                    roundKills = liveRoundKills[killerKey] = liveRoundKills.GetValueOrDefault(killerKey) + 1;
                    mapKills = liveMapKills[killerKey] = liveMapKills.GetValueOrDefault(killerKey) + 1;
                }
                else
                {
                    roundKills = liveRoundKills.GetValueOrDefault(killerKey);
                    mapKills = liveMapKills.GetValueOrDefault(killerKey);
                }

                var killerPawn = killer.PlayerPawn?.Value;
                var victimPawn = victim.PlayerPawn?.Value;
                double? distance = null;
                var from = killerPawn?.AbsOrigin;
                var to = victimPawn?.AbsOrigin;
                if (from != null && to != null)
                {
                    float dx = from.X - to.X, dy = from.Y - to.Y, dz = from.Z - to.Z;
                    distance = Math.Round(Math.Sqrt(dx * dx + dy * dy + dz * dz), 1);
                }

                var assister = @event.Assister;
                bool assisterValid = assister != null && assister.IsValid;
                var (ctAlive, tAlive) = CountAlivePlayers();

                var killEvent = new PlayerKillLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = round,
                    KillerName = killer.PlayerName,
                    KillerSteamId = killer.SteamID.ToString(),
                    KillerTeam = GetTeamSide(killer),
                    KillerHp = killerPawn?.Health ?? 0,
                    VictimName = victim.PlayerName,
                    VictimSteamId = victim.SteamID.ToString(),
                    VictimTeam = GetTeamSide(victim),
                    AssisterName = assisterValid ? assister!.PlayerName : null,
                    AssisterSteamId = assisterValid ? assister!.SteamID.ToString() : null,
                    FlashAssist = assisterValid && @event.Assistedflash,
                    Weapon = @event.Weapon ?? "unknown",
                    Headshot = @event.Headshot,
                    Penetrated = @event.Penetrated > 0,
                    Noscope = @event.Noscope,
                    Thrusmoke = @event.Thrusmoke,
                    Attackerblind = @event.Attackerblind,
                    Distance = distance,
                    TeamKill = teamKill,
                    FirstKill = firstKill,
                    TradeKill = !teamKill && lastDeathWasTradeKill,
                    KillerRoundKills = roundKills,
                    KillerMapKills = mapKills,
                    CtAlive = ctAlive,
                    TAlive = tAlive,
                };
                PublishEvent(killEvent);
            }
            catch (Exception e)
            {
                Log($"[SendPlayerKillEvent FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private void RegisterLiveUtilityEvents()
        {
            // player_kill. Registered after the advanced-stats player_death handler (Load order), so
            // lastDeathWasTradeKill already describes this death.
            RegisterEventHandler<EventPlayerDeath>((@event, info) => SendPlayerKillEvent(@event), HookMode.Post);

            RegisterEventHandler<EventGrenadeThrown>((@event, info) =>
            {
                try
                {
                    if (!ShouldSendLiveEvent())
                        return HookResult.Continue;
                    var player = @event.Userid;
                    if (player == null || !player.IsValid)
                        return HookResult.Continue;

                    var liveEvent = new GrenadeThrownLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        PlayerName = player.PlayerName,
                        PlayerSteamId = player.SteamID.ToString(),
                        PlayerTeam = GetTeamSide(player),
                        Grenade = NormalizeGrenadeName(@event.Weapon),
                    };
                    PublishEvent(liveEvent);
                }
                catch (Exception e)
                {
                    Log($"[EventGrenadeThrown FATAL] {e.Message}");
                }
                return HookResult.Continue;
            });

            RegisterEventHandler<EventSmokegrenadeDetonate>((@event, info) =>
                SendGrenadeDetonated("smoke", @event.Userid, @event.X, @event.Y, @event.Z));
            RegisterEventHandler<EventFlashbangDetonate>((@event, info) =>
                SendGrenadeDetonated("flash", @event.Userid, @event.X, @event.Y, @event.Z));
            RegisterEventHandler<EventHegrenadeDetonate>((@event, info) =>
                SendGrenadeDetonated("he", @event.Userid, @event.X, @event.Y, @event.Z));
            // Fires for both molotov and incendiary.
            RegisterEventHandler<EventMolotovDetonate>((@event, info) =>
                SendGrenadeDetonated("molotov", @event.Userid, @event.X, @event.Y, @event.Z));

            RegisterEventHandler<EventPlayerBlind>((@event, info) =>
            {
                try
                {
                    if (!ShouldSendLiveEvent())
                        return HookResult.Continue;
                    var victim = @event.Userid;
                    if (victim == null || !victim.IsValid || victim.IsHLTV)
                        return HookResult.Continue;
                    if (@event.BlindDuration <= 0f)
                        return HookResult.Continue;

                    var attacker = @event.Attacker;
                    bool attackerValid = attacker != null && attacker.IsValid;

                    var liveEvent = new PlayerBlindedLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        AttackerName = attackerValid ? attacker!.PlayerName : null,
                        AttackerSteamId = attackerValid ? attacker!.SteamID.ToString() : null,
                        AttackerTeam = attackerValid ? GetTeamSide(attacker) : null,
                        VictimName = victim.PlayerName,
                        VictimSteamId = victim.SteamID.ToString(),
                        VictimTeam = GetTeamSide(victim),
                        Duration = MathF.Round(@event.BlindDuration, 2),
                        TeamFlash = attackerValid && attacker != victim && attacker!.TeamNum == victim.TeamNum,
                    };
                    PublishEvent(liveEvent);
                }
                catch (Exception e)
                {
                    Log($"[EventPlayerBlind FATAL] {e.Message}");
                }
                return HookResult.Continue;
            });

            RegisterEventHandler<EventBombPickup>((@event, info) =>
                SendBombCarrierEvent("bomb_pickup", @event.Userid));
            RegisterEventHandler<EventBombDropped>((@event, info) =>
                SendBombCarrierEvent("bomb_dropped", @event.Userid));

            RegisterEventHandler<EventBombExploded>((@event, info) =>
            {
                try
                {
                    if (!ShouldSendLiveEvent())
                        return HookResult.Continue;

                    var (ctAlive, tAlive) = CountAlivePlayers();
                    var liveEvent = new BombExplodedLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        Site = GetPlantedBombSite(),
                        CtAlive = ctAlive,
                        TAlive = tAlive,
                    };
                    PublishEvent(liveEvent);
                }
                catch (Exception e)
                {
                    Log($"[EventBombExploded FATAL] {e.Message}");
                }
                return HookResult.Continue;
            }, HookMode.Pre); // Pre: the planted_c4 entity (and its site) still exists.
        }

        private bool ShouldSendLiveEvent()
        {
            return isMatchLive && !string.IsNullOrEmpty(matchConfig.RemoteLogURL);
        }

        private HookResult SendGrenadeDetonated(string grenade, CCSPlayerController? player, float x, float y, float z)
        {
            try
            {
                if (!ShouldSendLiveEvent())
                    return HookResult.Continue;
                bool playerValid = player != null && player.IsValid;

                var liveEvent = new GrenadeDetonatedLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = GetRoundNumer(),
                    PlayerName = playerValid ? player!.PlayerName : null,
                    PlayerSteamId = playerValid ? player!.SteamID.ToString() : null,
                    PlayerTeam = playerValid ? GetTeamSide(player) : null,
                    Grenade = grenade,
                    X = x,
                    Y = y,
                    Z = z,
                };
                PublishEvent(liveEvent);
            }
            catch (Exception e)
            {
                Log($"[SendGrenadeDetonated FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private HookResult SendBombCarrierEvent(string eventName, CCSPlayerController? player)
        {
            try
            {
                if (!ShouldSendLiveEvent())
                    return HookResult.Continue;
                if (player == null || !player.IsValid)
                    return HookResult.Continue;

                var liveEvent = new BombCarrierLiveEvent(eventName)
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = GetRoundNumer(),
                    PlayerName = player.PlayerName,
                    PlayerSteamId = player.SteamID.ToString(),
                };
                PublishEvent(liveEvent);
            }
            catch (Exception e)
            {
                Log($"[SendBombCarrierEvent FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private static string NormalizeGrenadeName(string? weapon)
        {
            string w = (weapon ?? "").Replace("weapon_", "");
            return w switch
            {
                "smokegrenade" => "smoke",
                "flashbang" => "flash",
                "hegrenade" => "he",
                "molotov" => "molotov",
                "incgrenade" => "incendiary",
                "decoy" => "decoy",
                _ => string.IsNullOrEmpty(w) ? "unknown" : w,
            };
        }

        // The bomb_planted / bomb_defused / bomb_exploded "site" field is the entity index of the
        // bombsite trigger, not 0/1, so read A/B from the planted_c4 entity instead.
        // The site of the bomb planted this round, read once at bomb_planted. Finding planted_c4
        // walks the entity list; defuse and explode (explode runs in an already heavy frame) reuse it.
        private string? plantedBombSite;

        private string GetPlantedBombSite(bool refresh = false)
        {
            if (!refresh && plantedBombSite != null)
                return plantedBombSite;
            var c4 = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            plantedBombSite = c4 == null || !c4.IsValid ? null : c4.BombSite == 0 ? "A" : "B";
            return plantedBombSite ?? "unknown";
        }
    }
}
