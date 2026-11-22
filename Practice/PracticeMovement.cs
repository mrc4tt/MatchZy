using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Practice movement helpers.
    //   .jump   - the practice bot at your aim jumps once on its spot.
    //   .bhop   - toggle auto bunnyhop (hold jump) for everyone on the server.
    //   .r      - in practice: respawn yourself on a spawn of your side.
    //
    // A practice bot's AI is asleep (bot_stop / bot_freeze / bot_zombie), so it cannot be told to
    // press jump. The jump is therefore played by the plugin: the bot is moved along the arc of a
    // standing jump (301.99 u/s up, gravity 800), one step per tick, and put back on its spot.
    //
    // Auto bunnyhop is a server convar the client predicts from, so it cannot differ per player
    // without the client and the server disagreeing on every landing.
    public partial class MatchZy
    {
        private const float BotJumpSpeed = 301.993377f;
        private const float BotJumpGravity = 800.0f;
        private const float BotJumpTime = 2.0f * BotJumpSpeed / BotJumpGravity;

        // Bot userid -> where and when its jump started.
        private readonly Dictionary<int, (float Start, float X, float Y, float Z)> botJumps = new();
        private readonly List<int> botJumpsDone = new();
        private Vector? _botJumpPos;
        private Vector? _botJumpVel;
        private bool practiceBhopEnabled;

        [ConsoleCommand("css_jump", "Make the practice bot at your aim jump")]
        public void OnBotJumpConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            var bot = RequireBotAtAim(player);
            if (bot == null)
                return;
            int uid = bot.UserId!.Value;
            var origin = bot.PlayerPawn.Value?.AbsOrigin;
            if (origin == null || botJumps.ContainsKey(uid))
                return;
            botJumps[uid] = (Server.CurrentTime, origin.X, origin.Y, origin.Z);
        }

        public void OnBotJumpTick()
        {
            if (botJumps.Count == 0)
                return;
            try
            {
                if (!isPractice)
                {
                    botJumps.Clear();
                    return;
                }
                _botJumpPos ??= new Vector(0, 0, 0);
                _botJumpVel ??= new Vector(0, 0, 0);
                float now = Server.CurrentTime;
                foreach (var (uid, jump) in botJumps)
                {
                    var bot = Utilities.GetPlayerFromUserid(uid);
                    var pawn = bot?.PlayerPawn.Value;
                    if (bot == null || !bot.IsValid || !bot.PawnIsAlive || pawn == null || !pawn.IsValid)
                    {
                        botJumpsDone.Add(uid);
                        continue;
                    }
                    float t = now - jump.Start;
                    bool landed = t >= BotJumpTime || t < 0;
                    _botJumpPos.X = jump.X;
                    _botJumpPos.Y = jump.Y;
                    _botJumpPos.Z = landed ? jump.Z : jump.Z + BotJumpSpeed * t - 0.5f * BotJumpGravity * t * t;
                    _botJumpVel.X = 0;
                    _botJumpVel.Y = 0;
                    _botJumpVel.Z = landed ? 0 : BotJumpSpeed - BotJumpGravity * t;
                    // Null angle: the bot keeps its look and an upright body.
                    pawn.Teleport(_botJumpPos, null, _botJumpVel);
                    if (landed)
                        botJumpsDone.Add(uid);
                }
                foreach (int uid in botJumpsDone)
                    botJumps.Remove(uid);
                botJumpsDone.Clear();
            }
            catch (Exception e)
            {
                botJumps.Clear();
                botJumpsDone.Clear();
                Log($"[BotJump] {e.Message}");
            }
        }

        [ConsoleCommand("css_bhop", "Toggle auto bunnyhop in practice")]
        public void OnBhopConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            practiceBhopEnabled = !practiceBhopEnabled;
            ApplyPracticeBhop();
            PrintLocalizedToAll(practiceBhopEnabled ? "matchzy.px.bhopon" : "matchzy.px.bhopoff");
        }

        // sv_enablebunnyhopping lifts the speed cap on landing; without it auto bunnyhop keeps
        // jumping but never gains speed.
        private void ApplyPracticeBhop()
        {
            string v = practiceBhopEnabled ? "1" : "0";
            Server.ExecuteCommand($"sv_autobunnyhopping {v}; sv_enablebunnyhopping {v}");
        }

        // Practice start and end (ResetPracticeExtras), and plugin unload.
        private void ResetPracticeMovement()
        {
            botJumps.Clear();
            if (!practiceBhopEnabled)
                return;
            practiceBhopEnabled = false;
            ApplyPracticeBhop();
        }

        // .r in practice. Never a spectator (Respawn on a spectator crashes), and off the command
        // stack: a respawn re-enters the engine.
        private void RespawnSelfInPractice(CCSPlayerController player)
        {
            if (!IsPlayerValid(player))
                return;
            if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
                return;
            Server.NextFrame(() =>
            {
                if (!isPractice || !IsPlayerValid(player) || player.PlayerPawn?.Value == null)
                    return;
                if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
                    return;
                player.Respawn();
            });
        }
    }
}
