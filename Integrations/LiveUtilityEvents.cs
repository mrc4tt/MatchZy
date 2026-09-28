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
        private void RegisterLiveUtilityEvents()
        {
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
                    Task.Run(async () => await SendEventAsync(liveEvent));
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
                    Task.Run(async () => await SendEventAsync(liveEvent));
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
                    Task.Run(async () => await SendEventAsync(liveEvent));
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
                Task.Run(async () => await SendEventAsync(liveEvent));
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
                Task.Run(async () => await SendEventAsync(liveEvent));
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
        private static string GetPlantedBombSite()
        {
            var c4 = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (c4 == null || !c4.IsValid)
                return "unknown";
            return c4.BombSite == 0 ? "A" : "B";
        }
    }
}
