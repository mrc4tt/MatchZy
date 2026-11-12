using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // .xray: every living player and bot glows through walls (T orange, CT blue), for checking
    // lineups, angles and bot spots. Practice only, for everyone on the server.
    //
    // A pawn's own glow does not render, so each pawn gets a glowing copy of its model: an invisible
    // prop_dynamic follows the pawn and the glowing prop follows that one (the usual CS2 glow setup).
    // The props are removed with entity-IO Kill on death, disconnect and when .xray is turned off,
    // and added again on spawn.
    public partial class MatchZy
    {
        private bool xrayEnabled;
        // Pawn owner's userid -> raw entity handles (index + serial) of (relay prop, glow prop).
        // Raw handles, not wrappers: after a map change a stale entry resolves to null instead of
        // pointing at freed (or reused) entity memory.
        private readonly Dictionary<int, (uint Relay, uint Glow)> xrayGlows = new();

        private void RegisterXray()
        {
            RegisterEventHandler<EventPlayerSpawn>(OnXraySpawn);
            RegisterEventHandler<EventPlayerDeath>(OnXrayDeath);
            RegisterEventHandler<EventPlayerDisconnect>(OnXrayDisconnect);
        }

        [ConsoleCommand("css_xray", "Toggle xray (players glow through walls) in practice")]
        public void OnXrayConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            xrayEnabled = !xrayEnabled;
            // Quiet the assert first; when it was just switched on, the log_flags command runs later
            // in the frame, so the props are made a moment after it.
            bool justQuieted = RefreshGlowAssertQuiet();
            // Off the command stack: creating and killing entities re-enters the engine.
            Action apply = () =>
            {
                if (xrayEnabled && isPractice)
                {
                    foreach (var p in Utilities.GetPlayers())
                        AddXrayGlow(p);
                }
                else
                {
                    RemoveAllXrayGlows();
                }
            };
            if (justQuieted)
                AddTimer(0.1f, apply, TimerFlags.STOP_ON_MAPCHANGE);
            else
                Server.NextFrame(apply);
            PrintLocalizedToAll(xrayEnabled ? "matchzy.px.xrayon" : "matchzy.px.xrayoff");
        }

        private void AddXrayGlow(CCSPlayerController player)
        {
            try
            {
                if (!IsPlayerValid(player) || !player.PawnIsAlive || !player.UserId.HasValue || player.IsHLTV)
                    return;
                if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
                    return;
                var pawn = player.PlayerPawn.Value!;
                string? model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
                if (string.IsNullOrEmpty(model))
                    return;
                RemoveXrayGlow(player.UserId.Value);

                var color = player.TeamNum == (byte)CsTeam.Terrorist
                    ? Color.FromArgb(255, 255, 165, 0)
                    : Color.FromArgb(255, 80, 160, 255);
                var pair = CreateGlowPair(pawn, model, color);
                if (pair != null)
                    xrayGlows[player.UserId.Value] = pair.Value;
            }
            catch (Exception e)
            {
                Log($"[Xray] {e.Message}");
            }
        }

        /// <summary>
        /// A glowing copy of a pawn's model that follows the pawn (the usual CS2 glow setup: an
        /// invisible relay prop follows the pawn and the glowing prop follows the relay). Used by
        /// .xray and the spectator coach outlines. Returns the raw handles of (relay, glow).
        /// SetModel has to come before the spawn: a model given only as a spawn keyvalue, or set
        /// again a frame later, gave props that did not glow (both tested live). That order makes
        /// the engine print a harmless debug "Assertion Failed ... EF_IN_STAGING_LIST" (SetupModel),
        /// which SetGlowAssertQuiet keeps off the console while glows are in use.
        /// </summary>
        private (uint Relay, uint Glow)? CreateGlowPair(CCSPlayerPawn pawn, string model, Color color)
        {
            var relay = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
            var glow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
            if (relay == null || glow == null)
            {
                if (relay != null && relay.IsValid) relay.AcceptInput("Kill");
                if (glow != null && glow.IsValid) glow.AcceptInput("Kill");
                return null;
            }

            relay.SetModel(model);
            relay.Spawnflags = 256u; // no collisions
            relay.RenderMode = RenderMode_t.kRenderNone;
            relay.DispatchSpawn();

            glow.SetModel(model);
            glow.Spawnflags = 256u;
            glow.DispatchSpawn();
            glow.Glow.GlowColorOverride = color;
            glow.Glow.GlowRange = 5000;
            glow.Glow.GlowRangeMin = 0;
            glow.Glow.GlowTeam = -1;
            glow.Glow.GlowType = 3;

            relay.AcceptInput("FollowEntity", pawn, relay, "!activator");
            glow.AcceptInput("FollowEntity", relay, glow, "!activator");
            return (relay.EntityHandle.Raw, glow.EntityHandle.Raw);
        }

        // True while the spectator coach outlines are in use (set by the coach code).
        private bool _glowUsersCoach;
        // Last state sent with log_flags (null = never sent: the engine default, echo on).
        private bool? _glowAssertQuiet;

        /// <summary>
        /// The tier0 log channel "Assert" carries the engine's debug asserts. While .xray or the
        /// spectator coach outlines are in use it is set to DoNotEcho, so the harmless
        /// EF_IN_STAGING_LIST assert of every glow prop stays off the console; back to normal
        /// (other asserts visible again) once nothing uses glows.
        /// </summary>
        /// <returns>True when the quiet flag was just switched on through the console command fallback: it only runs later in the
        /// frame (server command buffer), so props made right now would still print the assert.
        /// The caller then makes them a moment later.</returns>
        private bool RefreshGlowAssertQuiet()
        {
            bool want = (xrayEnabled && isPractice) || _glowUsersCoach;
            if (_glowAssertQuiet == want || (_glowAssertQuiet == null && !want))
                return false;
            _glowAssertQuiet = want;
            // Directly through tier0: immediate and silent. The console command is the fallback.
            if (Tier0Logging.SetChannelFlag("Assert", Tier0Logging.LCF_DO_NOT_ECHO, want))
                return false;
            Server.ExecuteCommand(want ? "log_flags Assert +DoNotEcho" : "log_flags Assert -DoNotEcho");
            return want;
        }

        private void RemoveXrayGlow(int userId)
        {
            if (!xrayGlows.Remove(userId, out var props))
                return;
            var glow = new CHandle<CBaseModelEntity>(props.Glow).Value;
            if (glow != null && glow.IsValid)
                glow.AcceptInput("Kill");
            var relay = new CHandle<CBaseModelEntity>(props.Relay).Value;
            if (relay != null && relay.IsValid)
                relay.AcceptInput("Kill");
        }

        private void RemoveAllXrayGlows()
        {
            foreach (int userId in xrayGlows.Keys.ToList())
                RemoveXrayGlow(userId);
        }

        // Practice start and end (ResetPracticeExtras).
        private void ResetXray()
        {
            xrayEnabled = false;
            RemoveAllXrayGlows();
            RefreshGlowAssertQuiet();
        }

        // New map: the previous map's props are gone with it, so only forget them (no Kill).
        private void ForgetXrayOnMapStart()
        {
            xrayGlows.Clear();
            xrayEnabled = false;
            RefreshGlowAssertQuiet();
        }

        private HookResult OnXraySpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            if (!xrayEnabled || !isPractice)
                return HookResult.Continue;
            var player = @event.Userid;
            // Next frame: the pawn is placed and has its model after the event.
            Server.NextFrame(() =>
            {
                if (xrayEnabled && isPractice && player != null)
                    AddXrayGlow(player);
            });
            return HookResult.Continue;
        }

        private HookResult OnXrayDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            if (xrayGlows.Count == 0)
                return HookResult.Continue;
            var victim = @event.Userid;
            if (victim != null && victim.UserId.HasValue)
            {
                int userId = victim.UserId.Value;
                Server.NextFrame(() => RemoveXrayGlow(userId));
            }
            return HookResult.Continue;
        }

        private HookResult OnXrayDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            if (xrayGlows.Count == 0)
                return HookResult.Continue;
            var player = @event.Userid;
            if (player != null && player.UserId.HasValue)
            {
                int userId = player.UserId.Value;
                Server.NextFrame(() => RemoveXrayGlow(userId));
            }
            return HookResult.Continue;
        }
    }
}
