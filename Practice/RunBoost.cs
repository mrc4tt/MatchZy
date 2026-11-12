using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // .runboost: puts you on top of the practice bot at your aim, and the bot then runs with your
    // movement keys (W/A/S/D relative to where you look, Shift walks). Ride it and jump off to
    // practice a run boost. .runboost again, dying, or landing on anything but the bot ends it; the
    // bot then goes back to its spot (the keep-on-spot tick takes over again).
    //
    // Every tick the bot gets your wish direction as its velocity (the vertical part is left to
    // gravity) and your yaw, flat. The jiggle and .botshoot ticks skip it meanwhile.
    public partial class MatchZy
    {
        private sealed class RunBoostState
        {
            public required CCSPlayerController Player { get; init; }
            public required CCSPlayerController Bot { get; init; }
            public required int BotUserId { get; init; }
            // Set once the rider has left the bot (jumped); the boost ends when they land elsewhere.
            public bool LeftBot;
            public float StartedAt;
        }

        private const float RunBoostRunSpeed = 250.0f;
        private const float RunBoostWalkSpeed = 130.0f;

        // Rider's userid -> state.
        private readonly Dictionary<int, RunBoostState> runBoosts = new();
        private readonly Vector _runBoostVel = new(0, 0, 0);
        private readonly QAngle _runBoostAng = new(0, 0, 0);
        private List<int>? _runBoostEnded;

        private bool IsBotRunBoosting(int botUserId)
        {
            foreach (var state in runBoosts.Values)
                if (state.BotUserId == botUserId)
                    return true;
            return false;
        }

        [ConsoleCommand("css_runboost", "Ride the practice bot at your aim; it copies your movement")]
        public void OnRunBoostConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null || !IsPlayerValid(player) || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (runBoosts.ContainsKey(userId))
            {
                StopRunBoost(userId);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.runboostoff"));
                return;
            }
            var bot = RequireBotAtAim(player);
            if (bot == null)
                return;
            int botUserId = bot.UserId!.Value;
            if (IsBotRunBoosting(botUserId) || IsBotReplayingPath(botUserId))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.runboostbusy"));
                return;
            }
            string botName = bot.PlayerName;
            Server.NextFrame(() =>
            {
                if (!isPractice || !IsPlayerValid(player) || !player.PawnIsAlive || !IsPlayerValid(bot) || !bot.PawnIsAlive)
                    return;
                var botPawn = bot.PlayerPawn.Value!;
                var pawn = player.PlayerPawn.Value!;
                var botOrigin = botPawn.AbsOrigin;
                if (botOrigin == null)
                    return;
                float yaw = pawn.EyeAngles.Y;
                // On the bot's head, like .boost (yaw only, so the body stays upright).
                pawn.Teleport(new Vector(botOrigin.X, botOrigin.Y, botOrigin.Z + 80.0f), new QAngle(0, yaw, 0), new Vector(0, 0, 0));
                botPawn.Teleport(null, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
                runBoosts[userId] = new RunBoostState { Player = player, Bot = bot, BotUserId = botUserId, StartedAt = Server.CurrentTime };
            });
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.runbooston", botName));
        }

        public void OnRunBoostTick()
        {
            if (runBoosts.Count == 0)
                return;
            try
            {
                _runBoostEnded?.Clear();
                foreach (var (userId, state) in runBoosts)
                {
                    var player = state.Player;
                    var bot = state.Bot;
                    if (!isPractice || !IsPlayerValid(player) || !player.PawnIsAlive || !IsPlayerValid(bot) || !bot.PawnIsAlive)
                    {
                        (_runBoostEnded ??= new()).Add(userId);
                        continue;
                    }
                    var pawn = player.PlayerPawn.Value!;
                    var botPawn = bot.PlayerPawn.Value!;

                    // Off the bot and back on solid ground somewhere else: the boost is over. A short
                    // grace after the start covers the teleport landing.
                    bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
                    var ground = pawn.GroundEntity.Value;
                    bool onBot = ground != null && ground.Handle == botPawn.Handle;
                    if (!onBot && !onGround)
                        state.LeftBot = true;
                    else if (!onBot && onGround && (state.LeftBot || Server.CurrentTime - state.StartedAt > 1.0f))
                    {
                        (_runBoostEnded ??= new()).Add(userId);
                        continue;
                    }

                    // The rider's wish direction from their keys and yaw.
                    PlayerButtons buttons = player.Buttons;
                    float forward = ((buttons & PlayerButtons.Forward) != 0 ? 1f : 0f) - ((buttons & PlayerButtons.Back) != 0 ? 1f : 0f);
                    float side = ((buttons & PlayerButtons.Moveright) != 0 ? 1f : 0f) - ((buttons & PlayerButtons.Moveleft) != 0 ? 1f : 0f);
                    float yaw = pawn.EyeAngles.Y;
                    double rad = yaw * Math.PI / 180.0;
                    double fx = Math.Cos(rad), fy = Math.Sin(rad);
                    // Right of the view direction.
                    double rx = Math.Sin(rad), ry = -Math.Cos(rad);
                    double wx = fx * forward + rx * side;
                    double wy = fy * forward + ry * side;
                    double len = Math.Sqrt(wx * wx + wy * wy);
                    float speed = (buttons & PlayerButtons.Speed) != 0 ? RunBoostWalkSpeed : RunBoostRunSpeed;
                    if (len > 0.001)
                    {
                        wx = wx / len * speed;
                        wy = wy / len * speed;
                    }
                    _runBoostVel.X = (float)wx;
                    _runBoostVel.Y = (float)wy;
                    _runBoostVel.Z = botPawn.AbsVelocity.Z;
                    _runBoostAng.X = 0;
                    _runBoostAng.Y = yaw;
                    _runBoostAng.Z = 0;
                    botPawn.Teleport(null, _runBoostAng, _runBoostVel);
                }
                if (_runBoostEnded != null)
                    foreach (int userId in _runBoostEnded)
                    {
                        StopRunBoost(userId);
                        var p = Utilities.GetPlayerFromUserid(userId);
                        if (p != null && p.IsValid && !p.IsBot)
                            p.PrintToChat($"{chatPrefix} {Localizer.ForPlayer(p, "matchzy.px.runboostoff")}");
                    }
            }
            catch (Exception e)
            {
                Log($"[RunBoost] {e.Message}");
            }
        }

        private void StopRunBoost(int riderUserId)
        {
            if (!runBoosts.Remove(riderUserId, out var state))
                return;
            var botPawn = state.Bot.IsValid ? state.Bot.PlayerPawn.Value : null;
            if (botPawn != null && botPawn.IsValid)
                botPawn.Teleport(null, null, new Vector(0, 0, 0));
        }

        // New map: the stored controller wrappers belong to the previous map (freed or reused
        // memory), so only forget them; no Teleport.
        private void ForgetRunBoostsOnMapStart()
        {
            runBoosts.Clear();
        }

        // Practice start and end (ResetPracticeExtras).
        private void ResetRunBoosts()
        {
            foreach (int userId in runBoosts.Keys.ToList())
                StopRunBoost(userId);
        }
    }
}
