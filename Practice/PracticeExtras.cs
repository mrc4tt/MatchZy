using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

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
            RegisterListener<Listeners.OnTick>(OnRunBoostTick);
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnBotSpawnMarkerButtonHandler);
            RegisterEventHandler<EventPlayerDeath>(OnPracticeDeathPre, HookMode.Pre);
            RegisterEventHandler<EventPlayerSpawn>(OnPracticeSpawnPost);
            RegisterXray();
            RegisterStrategyRecorder();
            RegisterUtilityExpire();
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnSmokeTimerButtonHandler);
        }

        // Practice start and end: every extra goes back to its default, so a new session never
        // inherits shooting bots, a frozen .startround or regeneration settings.
        private void ResetPracticeExtras()
        {
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
