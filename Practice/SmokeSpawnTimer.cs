using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    // Spawn smoke timer (practice, on by default per player, .smoketimer toggles it):
    //   After a spawn teleport (E on a .showspawns marker, .spawn / .ctspawn / .tspawn) a clock is
    //   armed; it starts on the first movement, jump or attack input. A smoke thrown afterwards
    //   reports how long after the start it left the hand, and again when it pops (with its flight
    //   time), so a lineup can be checked against "smoke is up X s into the round".
    public partial class MatchZy
    {
        // Players (slot) who turned the spawn smoke timer off.
        private readonly HashSet<int> smokeTimerDisabled = new();
        // Players (slot) teleported to a spawn whose clock waits for the first input.
        private readonly HashSet<int> smokeTimerArmed = new();
        // Players (slot) whose clock is running, with its start.
        private readonly Dictionary<int, DateTime> smokeTimerStart = new();
        // Smoke projectile index -> clock start of its thrower, for the detonate report.
        private readonly Dictionary<int, DateTime> smokeTimerProjectiles = new();

        private const PlayerButtons SmokeTimerStartButtons =
            PlayerButtons.Forward | PlayerButtons.Back | PlayerButtons.Moveleft | PlayerButtons.Moveright |
            PlayerButtons.Jump | PlayerButtons.Attack | PlayerButtons.Attack2;

        [ConsoleCommand("css_smoketimer", "Practice: time smokes from the first move after a spawn teleport (toggle)")]
        public void OnSmokeTimerConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            int slot = player.Slot;
            if (smokeTimerDisabled.Remove(slot))
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.px.smoketimeron"));
                return;
            }
            smokeTimerDisabled.Add(slot);
            smokeTimerArmed.Remove(slot);
            smokeTimerStart.Remove(slot);
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.px.smoketimeroff"));
        }

        // Called after a spawn teleport: the clock restarts on the next input.
        private void ArmSmokeSpawnTimer(CCSPlayerController player)
        {
            if (!isPractice || smokeTimerDisabled.Contains(player.Slot))
                return;
            smokeTimerStart.Remove(player.Slot);
            smokeTimerArmed.Add(player.Slot);
        }

        private void OnSmokeTimerButtonHandler(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if (smokeTimerArmed.Count == 0 || (pressed & SmokeTimerStartButtons) == 0)
                return;
            if (player == null || !player.IsValid || !smokeTimerArmed.Remove(player.Slot))
                return;
            smokeTimerStart[player.Slot] = DateTime.Now;
        }

        // A smoke projectile spawned (OnEntitySpawnedHandler).
        private void OnSmokeTimerThrow(CCSPlayerController player, int projIndex)
        {
            if (!smokeTimerStart.TryGetValue(player.Slot, out var start))
                return;
            smokeTimerProjectiles[projIndex] = start;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.px.smoketimerthrown",
                $"{(DateTime.Now - start).TotalSeconds:0.00}"));
        }

        // The smoke popped (EventSmokegrenadeDetonateHandler); thrownTime is when it left the hand.
        private void OnSmokeTimerDetonate(CCSPlayerController player, int projIndex, DateTime thrownTime)
        {
            if (!smokeTimerProjectiles.Remove(projIndex, out var start))
                return;
            var now = DateTime.Now;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.px.smoketimerlanded",
                $"{(now - start).TotalSeconds:0.00}", $"{(now - thrownTime).TotalSeconds:0.00}"));
        }

        // Practice start and end (ResetPracticeExtras).
        private void ResetSmokeSpawnTimer()
        {
            smokeTimerDisabled.Clear();
            smokeTimerArmed.Clear();
            smokeTimerStart.Clear();
            smokeTimerProjectiles.Clear();
        }
    }
}
