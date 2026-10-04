using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    public partial class MatchZy
    {
        // player_connect and player_say, as in Get5: sent while a match is loaded or running (same
        // gating as player_disconnect), in both event formats. player_say also covers chat commands.

        // Get5's ip_address has no port; CounterStrikeSharp's IpAddress can be "1.2.3.4:27005".
        // Anything with more than one colon (IPv6) is kept as it is.
        private static string IpWithoutPort(string? address)
        {
            if (string.IsNullOrEmpty(address))
                return "";
            int colon = address.IndexOf(':');
            return colon >= 0 && colon == address.LastIndexOf(':') ? address[..colon] : address;
        }

        // The text of a say / say_team command: chat sends it quoted, the console may not.
        private static string ChatMessage(string argString)
        {
            string message = argString.Trim();
            return message.Length >= 2 && message[0] == '"' && message[^1] == '"' ? message[1..^1] : message;
        }

        private static string LegacyPlayerTeam(CCSPlayerController player) =>
            player.TeamNum switch { 2 => "T", 3 => "CT", 1 => "SPEC", _ => "none" };

        private bool ShouldSendPlayerEvent(CCSPlayerController? player) =>
            (isMatchSetup || matchStarted) && player != null && player.IsValid && !player.IsBot && !player.IsHLTV
            && !string.IsNullOrEmpty(matchConfig.RemoteLogURL);

        // Players who joined while a match load was waiting for its map change (isMatchSetup is
        // only set about a second after the new map starts). Sent once the load completes.
        private readonly HashSet<int> pendingLoadConnects = new();

        // Called from EventPlayerConnectFull once the player has passed the whitelist and roster
        // checks, so a kicked player is not sent. Fires again after every map change, as in Get5.
        private void SendPlayerConnectEvent(CCSPlayerController player)
        {
            try
            {
                if (!isMatchSetup && !matchStarted && pendingMatchLoadJson != null
                    && player.IsValid && !player.IsBot && !player.IsHLTV && player.UserId.HasValue)
                {
                    pendingLoadConnects.Add(player.UserId.Value);
                    return;
                }
                if (!ShouldSendPlayerEvent(player))
                    return;
                string ip = IpWithoutPort(player.IpAddress);
                if (UseGet5Events)
                {
                    PublishEvent(new Get5PlayerConnectEvent
                    {
                        MatchId = liveMatchId,
                        Player = Get5Player(player),
                        IpAddress = ip,
                    });
                    return;
                }
                PublishEvent(new MatchZyPlayerConnectedEvent
                {
                    MatchId = liveMatchId,
                    Player = player.UserId ?? 0,
                    PlayerSteamId = player.SteamID.ToString(),
                    PlayerName = player.PlayerName,
                    PlayerTeam = LegacyPlayerTeam(player),
                    IpAddress = ip,
                });
            }
            catch (Exception e)
            {
                Log($"[player_connect FATAL] {e.Message}");
            }
        }

        // The pending load finished: send player_connect for those who joined during it and were
        // not kicked by the loaded match's roster check (same rule as EventPlayerConnectFull).
        private void FlushPendingLoadConnects(bool loaded)
        {
            var userIds = pendingLoadConnects.ToList();
            pendingLoadConnects.Clear();
            if (!loaded)
                return;
            foreach (int userId in userIds)
            {
                var player = Utilities.GetPlayerFromUserid(userId);
                if (!IsHumanPlayerValid(player))
                    continue;
                if (matchModeOnly && GetPlayerTeam(player!) == CounterStrikeSharp.API.Modules.Utils.CsTeam.None
                    && !IsPlayerAdmin(player, "", "@css/config"))
                    continue;
                SendPlayerConnectEvent(player!);
            }
        }

        // A command listener runs before CounterStrikeSharp and MatchZy handle the chat line, so a
        // command that resets the match (.stopmatch, .restart) is still sent, to that match's URL
        // (PublishEvent takes the target when the event is queued).
        private void RegisterPlayerSayListeners()
        {
            AddCommandListener("say", (player, info) => SendPlayerSayEvent(player, "say", info), HookMode.Pre);
            AddCommandListener("say_team", (player, info) => SendPlayerSayEvent(player, "say_team", info), HookMode.Pre);
        }

        private HookResult SendPlayerSayEvent(CCSPlayerController? player, string command, CommandInfo info)
        {
            try
            {
                if (!ShouldSendPlayerEvent(player))
                    return HookResult.Continue;
                string message = ChatMessage(info.ArgString);
                if (message.Length == 0)
                    return HookResult.Continue;
                if (UseGet5Events)
                {
                    PublishEvent(new Get5PlayerSayEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = isMatchLive ? GetRoundNumer() : -1,
                        RoundTime = isMatchLive ? LiveRoundTimeMs() : 0,
                        Player = Get5Player(player!),
                        Command = command,
                        Message = message,
                    });
                    return HookResult.Continue;
                }
                PublishEvent(new PlayerSayLiveEvent
                {
                    MatchId = liveMatchId,
                    MapNumber = matchConfig.CurrentMapNumber,
                    RoundNumber = isMatchLive ? GetRoundNumer() : -1,
                    PlayerName = player!.PlayerName,
                    PlayerSteamId = player.SteamID.ToString(),
                    PlayerTeam = LegacyPlayerTeam(player),
                    Command = command,
                    Message = message,
                });
            }
            catch (Exception e)
            {
                Log($"[player_say FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }
    }
}
