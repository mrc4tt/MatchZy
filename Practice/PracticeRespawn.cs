using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Practice respawning.
    //   .botrespawn [true|false]  - whether practice bots respawn after dying (on by default).
    //   .slp / .tlp / .dlp        - save / teleport to / delete your saved location (the same slot
    //                               as .savepos / .loadpos without a name).
    //
    // prac.cfg respawns everybody on death (mp_respawn_on_death_ct/t 1) and puts tracked bots back on
    // their spot (OnPlayerSpawn). The engine setting is per team, not per player, so with .botrespawn
    // off it goes to 0 for both teams and players are respawned by the plugin instead.
    // After a death, a player with a saved location (.slp / .savepos) respawns there.
    public partial class MatchZy
    {
        private bool botRespawnEnabled = true;
        // Players who died (not a .t/.ct/.spec switch) and should go to their saved location on the
        // next spawn.
        private readonly HashSet<int> respawnAtSavedLocation = new();
        private const float PracticeHumanRespawnDelay = 0.5f;
        // The engine's own mp_respawn_on_death waits out the death animation and freeze cam
        // (a couple of seconds). Tracked practice bots are brought back by the plugin instead.
        // Not in the death event itself: the pawn is still being torn down on that tick (weapon
        // drops, death notices, the other death handlers), and a respawn there re-enters the
        // engine mid-teardown. A few ticks later the death is fully processed and the respawn is
        // the same call .botrespawn already makes.
        private const float PracticeBotRespawnDelay = 0.1f;

        [ConsoleCommand("css_botrespawn", "Toggle whether practice bots respawn after dying")]
        public void OnBotRespawnConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotRespawnCommand(player, command.ArgString);

        public void HandleBotRespawnCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            if (!TryParseToggle(arg, botRespawnEnabled, out bool enabled))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".botrespawn [true|false]"));
                return;
            }
            botRespawnEnabled = enabled;
            if (enabled)
            {
                // mp_ignore_round_win_conditions stays on until the dead bots are back: with a
                // whole side dead, switching it off here ended the round on the spot.
                Server.ExecuteCommand("mp_respawn_on_death_ct 1; mp_respawn_on_death_t 1");
                // Bring back the bots that died while it was off. Respawn() did not revive a bot
                // that had been dead for a while (live test: still dead, team and pawn fine), so
                // after two tries such a bot is replaced by a new one on the same spot.
                var caller = player!;
                int replaced = 0;
                foreach (var bot in TrackedPracticeBots())
                {
                    var b = bot;
                    Server.NextFrame(() => RespawnIfDead(b));
                    AddTimer(0.5f, () => RespawnTrackedBotIfDead(b), TimerFlags.STOP_ON_MAPCHANGE);
                    if (b.IsValid && !b.PawnIsAlive)
                    {
                        // One second apart: AddBot handles one bot at a time.
                        float at = 1.0f + replaced++ * 1.0f;
                        AddTimer(at, () => ReplaceDeadTrackedBot(b, caller), TimerFlags.STOP_ON_MAPCHANGE);
                    }
                }
                AddTimer(2.0f + replaced * 1.0f, () =>
                {
                    if (isPractice && botRespawnEnabled)
                        Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            else
            {
                // Without engine respawns a team that loses its last bot would win/lose the round.
                Server.ExecuteCommand("mp_respawn_on_death_ct 0; mp_respawn_on_death_t 0; mp_ignore_round_win_conditions 1");
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, enabled ? "matchzy.px.botrespawnon" : "matchzy.px.botrespawnoff"));
        }

        [ConsoleCommand("css_slp", "Save your current location (same as .savepos)")]
        public void OnSlpCommand(CCSPlayerController? player, CommandInfo? command) => HandleSavePosCommand(player, "");

        [ConsoleCommand("css_tlp", "Teleport to your saved location (same as .loadpos)")]
        public void OnTlpCommand(CCSPlayerController? player, CommandInfo? command)
        {
            // Teleport + weapon re-deploy re-enter the engine: next frame, not on the command stack.
            Server.NextFrame(() =>
            {
                if (IsPlayerValid(player))
                    HandleLoadPosCommand(player, "");
            });
        }

        [ConsoleCommand("css_dlp", "Delete your saved location")]
        public void OnDlpCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player) || !player!.UserId.HasValue)
                return;
            bool removed = savedPlayerLocationData.Remove(player.UserId.Value);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, removed ? "matchzy.px.dlpdone" : "matchzy.pm.notsavedpos"));
        }

        // Pre: the .t/.ct/.spec switch marker (practiceSwitchNoDeath) is still set here; the Post
        // handler in MatchZy.cs removes it.
        private HookResult OnPracticeDeathPre(EventPlayerDeath @event, GameEventInfo info)
        {
            try
            {
                if (!isPractice || isDryRun)
                    return HookResult.Continue;
                var victim = @event.Userid;
                if (victim == null || !victim.IsValid || !victim.UserId.HasValue || victim.IsHLTV)
                    return HookResult.Continue;
                int uid = victim.UserId.Value;
                if (practiceSwitchNoDeath.Contains(uid))
                    return HookResult.Continue;

                if (!victim.IsBot && savedPlayerLocationData.ContainsKey(uid))
                    respawnAtSavedLocation.Add(uid);

                if (victim.IsBot && botRespawnEnabled)
                {
                    bool tracked;
                    lock (_botsDictLock)
                        tracked = pracUsedBots.ContainsKey(uid);
                    if (tracked)
                    {
                        var b = victim;
                        AddTimer(PracticeBotRespawnDelay, () =>
                        {
                            // Still ours? .nobots / a kick in between removes it from the list.
                            if (!botRespawnEnabled || !b.IsValid || !b.UserId.HasValue)
                                return;
                            lock (_botsDictLock)
                            {
                                if (!pracUsedBots.ContainsKey(b.UserId.Value))
                                    return;
                            }
                            RespawnIfDead(b);
                        }, TimerFlags.STOP_ON_MAPCHANGE);
                    }
                }

                // Engine respawns are off while .botrespawn is off: bring the humans back ourselves.
                if (!botRespawnEnabled && !victim.IsBot)
                {
                    var p = victim;
                    AddTimer(PracticeHumanRespawnDelay, () => RespawnIfDead(p), TimerFlags.STOP_ON_MAPCHANGE);
                }
            }
            catch (Exception e)
            {
                Log($"[PracticeDeath FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        private HookResult OnPracticeSpawnPost(EventPlayerSpawn @event, GameEventInfo info)
        {
            try
            {
                var player = @event.Userid;
                if (isPractice && player != null && player.IsValid && player.IsBot)
                {
                    ReapplyBotLoadout(player);
                    return HookResult.Continue;
                }
                if (!isPractice || player == null || !player.IsValid || player.IsBot || !player.UserId.HasValue)
                    return HookResult.Continue;
                if (!respawnAtSavedLocation.Remove(player.UserId.Value))
                    return HookResult.Continue;
                // Next frame: the engine places the pawn on a spawn point after this event.
                Server.NextFrame(() =>
                {
                    if (!IsPlayerValid(player) || !player.PawnIsAlive || !player.UserId.HasValue)
                        return;
                    if (savedPlayerLocationData.TryGetValue(player.UserId.Value, out var saved))
                        saved.LoadPosition(player);
                });
            }
            catch (Exception e)
            {
                Log($"[PracticeSpawn FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        // Respawn a dead T/CT player or bot. Never a spectator (Respawn on a spectator crashes).
        private void RespawnIfDead(CCSPlayerController player)
        {
            if (!isPractice || player == null || !player.IsValid || player.PawnIsAlive)
                return;
            if (player.Connected != PlayerConnectedState.Connected)
                return;
            if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
                return;
            if (player.PlayerPawn?.Value == null)
                return;
            player.Respawn();
        }

        // Second try for .botrespawn on.
        private void RespawnTrackedBotIfDead(CCSPlayerController bot)
        {
            if (!isPractice || !botRespawnEnabled || bot == null || !bot.IsValid || !bot.UserId.HasValue || bot.PawnIsAlive)
                return;
            lock (_botsDictLock)
            {
                if (!pracUsedBots.ContainsKey(bot.UserId.Value))
                    return;
            }
            RespawnIfDead(bot);
        }

        // Last resort for .botrespawn on: the bot is still dead, so it is kicked and a new bot is
        // added on its spot, same team and crouch state, through the normal .bot path.
        private void ReplaceDeadTrackedBot(CCSPlayerController bot, CCSPlayerController caller)
        {
            try
            {
                if (!isPractice || !botRespawnEnabled || bot == null || !bot.IsValid || !bot.IsBot || !bot.UserId.HasValue || bot.PawnIsAlive)
                    return;
                int uid = bot.UserId.Value;
                Position? spot = null;
                CCSPlayerController? owner = null;
                bool crouch = false;
                int remaining;
                lock (_botsDictLock)
                {
                    if (!pracUsedBots.TryGetValue(uid, out var data))
                        return;
                    if (data.TryGetValue("position", out var p) && p is Position pos) spot = pos;
                    if (data.TryGetValue("owner", out var o) && o is CCSPlayerController oc) owner = oc;
                    if (data.TryGetValue("crouchstate", out var c) && c is bool cb) crouch = cb;
                    if (spot == null)
                        return;
                    pracUsedBots.Remove(uid);
                    remaining = pracUsedBots.Count;
                }
                var team = (CsTeam)bot.TeamNum;
                if (team != CsTeam.Terrorist && team != CsTeam.CounterTerrorist)
                    return;
                Log($"[BotRespawn] {bot.PlayerName} still dead after .botrespawn on, replacing it with a new bot on its spot");
                ForgetBotLoadout(uid);
                // Quota to what is left before the kick, as .kickbot does, so the slot is not refilled.
                Server.ExecuteCommand($"bot_quota {remaining}");
                Server.ExecuteCommand($"kickid {uid}");
                var addFor = IsPlayerValid(owner) ? owner! : caller;
                var addSpot = spot;
                AddTimer(0.3f, () =>
                {
                    if (isPractice && IsPlayerValid(addFor))
                        AddBot(addFor, crouch, team, false, addSpot);
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception e)
            {
                Log($"[BotRespawn] {e.Message}");
            }
        }

        private void ResetPracticeRespawn()
        {
            if (!botRespawnEnabled && isPractice)
                Server.ExecuteCommand("mp_respawn_on_death_ct 1; mp_respawn_on_death_t 1; mp_ignore_round_win_conditions 0");
            botRespawnEnabled = true;
            respawnAtSavedLocation.Clear();
        }
    }
}
