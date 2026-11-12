using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace MatchZy
{
    // Wiring for the practice extras: bot combat (BotCombat.cs), health regeneration
    // (PracticeHealth.cs), respawning (PracticeRespawn.cs), .startround (StartRound.cs) and bot spawn
    // pools (BotSpawnPools.cs).
    public partial class MatchZy
    {
        // Called once from Load.
        private void RegisterPracticeExtras()
        {
            RegisterListener<Listeners.OnTick>(OnBotCombatTick);
            RegisterListener<Listeners.OnTick>(OnPathReplayTick);
            RegisterNadeTutorial();
            RegisterNadeFollowCam();
            RegisterListener<Listeners.OnTick>(OnRunBoostTick);
            RegisterListener<Listeners.OnServerPostEntityThink>(OnPracticeBuyZonePostThink);
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnBotSpawnMarkerButtonHandler);
            RegisterEventHandler<EventPlayerDeath>(OnPracticeDeathPre, HookMode.Pre);
            RegisterEventHandler<EventPlayerSpawn>(OnPracticeSpawnPost);
            RegisterXray();
            RegisterStrategyRecorder();
            RegisterUtilityExpire();
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnSmokeTimerButtonHandler);
        }

        // E (+use) opens the buy menu on the client whenever the networked m_bInBuyZone is true, and
        // with mp_buy_anywhere the engine sets it true everywhere, so E opened the buy menu instead of
        // using the practice markers. Forcing it false after every entity think (the engine sets it
        // again each tick under mp_buy_anywhere) keeps E free; B (buymenu) does not depend on it and still opens the menu.
        // Practice only and off with matchzy_prac_use_opens_buymenu true.
        private void OnPracticeBuyZonePostThink()
        {
            if (!isPractice || pracUseOpensBuyMenu.Value)
            {
                if (buyZoneSuppressed)
                {
                    buyZoneSuppressed = false;
                    RestoreBuyZoneFlags();
                    // Leaving practice starts warmup, whose mp_restartgame respawns everyone a moment
                    // later; check again once they stand at their spawn.
                    AddTimer(2.0f, RestoreBuyZoneFlags, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                }
                return;
            }
            buyZoneSuppressed = true;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot)
                    continue;
                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid)
                    continue;
                if (!Schema.GetSchemaValue<bool>(pawn.Handle, "CCSPlayerPawn", "m_bInBuyZone"))
                    continue;
                Schema.SetSchemaValue(pawn.Handle, "CCSPlayerPawn", "m_bInBuyZone", false);
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_bInBuyZone");
            }
        }

        // True while OnPracticeBuyZonePostThink is forcing m_bInBuyZone false.
        private bool buyZoneSuppressed;

        // Without mp_buy_anywhere the engine only writes m_bInBuyZone when a pawn enters or leaves a
        // func_buyzone trigger. A player who was already standing in the zone when practice ended
        // (or after a respawn into it) gets no new touch, so the false we forced stuck and buying was
        // refused with "left the buy zone". Recompute it once from the buy zone bounds; the engine's
        // touch logic takes over from there.
        private void RestoreBuyZoneFlags()
        {
            try
            {
                var zones = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_buyzone")
                    .Where(zone => zone != null && zone.IsValid && zone.Collision != null && zone.AbsOrigin != null)
                    .ToList();
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot)
                        continue;
                    var pawn = player.PlayerPawn?.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                        continue;
                    var feet = pawn.AbsOrigin;
                    bool inZone = zones.Any(zone =>
                    {
                        if (zone.TeamNum != 0 && zone.TeamNum != pawn.TeamNum)
                            return false;
                        var origin = zone.AbsOrigin!;
                        var mins = zone.Collision!.Mins;
                        var maxs = zone.Collision!.Maxs;
                        float z = feet.Z + 16f;
                        return feet.X >= origin.X + mins.X && feet.X <= origin.X + maxs.X
                            && feet.Y >= origin.Y + mins.Y && feet.Y <= origin.Y + maxs.Y
                            && z >= origin.Z + mins.Z && z <= origin.Z + maxs.Z;
                    });
                    if (Schema.GetSchemaValue<bool>(pawn.Handle, "CCSPlayerPawn", "m_bInBuyZone") == inZone)
                        continue;
                    Schema.SetSchemaValue(pawn.Handle, "CCSPlayerPawn", "m_bInBuyZone", inZone);
                    Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_bInBuyZone");
                }
            }
            catch (Exception e)
            {
                Log($"[RestoreBuyZoneFlags] {e.GetType().Name}: {e.Message}");
            }
        }

        // Practice start and end: every extra goes back to its default, so a new session never
        // inherits shooting bots, a frozen .startround or regeneration settings.
        private void ResetPracticeExtras()
        {
            ResetNadeFollow(mapStart: false);
            ResetBotCombatState();
            botShootEnabled = false;
            botReactionTimeMs = 500;
            _botJiggleRandom = false;
            RerollBotJiggle();
            ResetPracticeHealth();
            ResetPracticeRespawn();
            ResetStartRound();
            ClearBotSpawnMarkers();
            ResetPathReplay();
            ResetBotLoadouts();
            ResetRunBoosts();
            ResetXray();
            ResetStrats();
            ResetUtilityExpire();
            ResetSmokeSpawnTimer();
        }

        // Chat dispatch for the extras (both the bare and the argument forms). Returns true when the
        // command word was one of these.
        private bool HandlePracticeExtraChatCommand(CCSPlayerController player, string cmdWord, string arg)
        {
            switch (cmdWord)
            {
                case ".botshoot": HandleBotShootCommand(player, arg); return true;
                case ".kevlar": HandleBotArmorCommand(player, helmet: false); return true;
                case ".armor": HandleBotArmorCommand(player, helmet: true); return true;
                case ".botgun": HandleBotGunCommand(player, arg); return true;
                case ".dismiss": OnDismissConsoleCommand(player, null); return true;
                case ".dismissall": OnDismissAllConsoleCommand(player, null); return true;
                case ".runboost": OnRunBoostConsoleCommand(player, null); return true;
                case ".stratrec": OnStratRecConsoleCommand(player, null); return true;
                case ".stoprecord": OnStopRecordConsoleCommand(player, null); return true;
                case ".strat": HandleStratCommand(player, arg); return true;
                case ".savestrat": HandleSaveStratCommand(player, arg); return true;
                case ".liststrat": OnListStratConsoleCommand(player, null); return true;
                case ".delstrat": HandleDelStratCommand(player, arg); return true;
                case ".createstrat": HandleCreateStratCommand(player, arg); return true;
                case ".stratslot": HandleStratSlotCommand(player, arg); return true;
                case ".playgroup": HandleStratCommand(player, arg); return true;
                case ".strats": OnStratsMenuCommand(player, null); return true;
                case ".xray": OnXrayConsoleCommand(player, null); return true;
                case ".expire": OnExpireConsoleCommand(player, null); return true;
                case ".smoketimer": OnSmokeTimerConsoleCommand(player, null); return true;
                case ".shownades": ShowNadesWithFilter(player, arg); return true;
                case ".coloredsmokes": OnColoredSmokesConsoleCommand(player, null); return true;
                case ".botreactiontime": HandleBotReactionTimeCommand(player, arg); return true;
                case ".botrespawn": HandleBotRespawnCommand(player, arg); return true;
                case ".botlifereg": HandleBotLifeRegCommand(player, arg); return true;
                case ".liferegon": HandleLifeRegenCommand(player, arg); return true;
                case ".allliferegon": HandleAllLifeRegenCommand(player, arg); return true;
                case ".botjigglerange": HandleBotJiggleRangeCommand(player, arg); return true;
                case ".botjigglerandom": HandleBotJiggleRandomCommand(player, arg); return true;
                case ".startround": OnStartRoundCommand(player, null); return true;
                case ".rec": HandleRecCommand(player, arg); return true;
                case ".replay": HandleReplayCommand(player, arg); return true;
                case ".record": HandleRecCommand(player, arg); return true;
                case ".play" when isPractice: HandleReplayCommand(player, arg); return true;
                case ".saverec": HandleSaveRecCommand(player, arg); return true;
                case ".loadrec": HandleLoadRecCommand(player, arg); return true;
                case ".listrec": OnListRecConsoleCommand(player, null); return true;
                case ".delrec": HandleDelRecCommand(player, arg); return true;
                case ".showrec": OnShowRecConsoleCommand(player, null); return true;
                case ".botspawn": HandleBotSpawnCommand(player, arg); return true;
                case ".delbotspawn": HandleDelBotSpawnCommand(player, arg); return true;
                case ".listbotspawn": OnListBotSpawnCommand(player, null); return true;
                case ".placebot": HandlePlaceBotCommand(player, arg, keepExisting: false); return true;
                case ".placenewbot": HandlePlaceBotCommand(player, arg, keepExisting: true); return true;
                case ".kicklastbot": OnKickLastBotCommand(player, null); return true;
                case ".showbotspawn": OnShowBotSpawnCommand(player, null); return true;
                case ".slp": OnSlpCommand(player, null); return true;
                case ".tlp": OnTlpCommand(player, null); return true;
                case ".dlp": OnDlpCommand(player, null); return true;
            }
            return false;
        }
    }
}
