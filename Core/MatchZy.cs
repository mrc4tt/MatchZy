using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace MatchZy
{
    public partial class MatchZy : BasePlugin
    {
        public override string ModuleName => "MatchZy";
        public override string ModuleVersion => "0.8.56";
        public override string ModuleAuthor => "WD- Edited by Miksen @ FSHOST.me";
        public override string ModuleDescription => "A plugin for running and managing CS2 practice/pugs/scrims/matches!";
        public string chatPrefix = $"{ChatColors.Green}[MatchZy]{ChatColors.Default}";
        public string adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";

        // Plugin start phase data
        public bool isPractice = false;
        public bool isSleep = false;
        public bool readyAvailable = false;
        public bool matchStarted = false;
        // Synchronous guard: HandleMatchStart defers StartKnifeRound to Task.Run→Server.NextFrame,
        // so matchStarted flips late. Two rapid CheckLiveRequired calls both passed → knife started
        // twice → "KNIFE!" printed 6x. Set true synchronously at entry, cleared on match reset.
        public bool matchStartInProgress = false;
        public bool isWarmup = false;
        public bool isKnifeRound = false;
        public bool isSideSelectionPhase = false;
        public bool isMatchLive = false;
        public bool isConvarMappingSwapped = false;
        public long liveMatchId = -1;
        public int autoStartMode = 1;
        private bool autoStartLatched = false;
        public bool mapReloadRequired = false;

        public CounterStrikeSharp.API.Modules.Timers.Timer? SideSelectionTimer = null;

        // Pause Data
        public bool isPaused = false;
        public Dictionary<string, object> unpauseData = new Dictionary<string, object>
        {
            { "ct", false },
            { "t", false },
            { "pauseTeam", "" },
        };

        bool isPauseCommandForTactical = false;

        // Knife Data
        public int knifeWinner = 0;
        public string knifeWinnerName = "";

        // Players Data (including admins)
        public int connectedPlayers = 0;
        private Dictionary<int, bool> playerReadyStatus = new Dictionary<int, bool>();
        private Dictionary<int, CCSPlayerController> playerData = new Dictionary<int, CCSPlayerController>();

        private void SetupRestartConfirmationCleanup()
        {
            AddTimer(
                10.0f,
                () =>
                {
                    var now = DateTime.Now;
                    var expired = pendingRestartConfirmations.Where(kvp => (now - kvp.Value).TotalSeconds > RESTART_CONFIRMATION_TIMEOUT_SECONDS).Select(kvp => kvp.Key).ToList();

                    foreach (var steamId in expired)
                    {
                        pendingRestartConfirmations.Remove(steamId);
                    }
                },
                TimerFlags.REPEAT
            );
        }

        // Admin Data
        private Dictionary<string, string> loadedAdmins = new Dictionary<string, string>();

        // Cached ConVar references - looked up once, avoid per-frame string lookups
        private ConVar? _cvTvEnable = null;
        private ConVar? _cvMatchRestartDelay = null;
        private ConVar? _cvMatchEndChangelevel = null;
        private ConVar? _cvMatchEndRestart = null;

        // Cached CCSTeam entity references - refreshed on map start, avoids entity scan per event
        private CCSTeam? _cachedCtTeam = null;
        private CCSTeam? _cachedTTeam = null;

        /// <summary>
        /// Refreshes cached CCSTeam entity references. Call on map start and when entities may have changed.
        /// </summary>
        private void RefreshTeamEntities()
        {
            _cachedCtTeam = null;
            _cachedTTeam = null;
            try
            {
                var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
                foreach (var team in teamEntities)
                {
                    if (team.Teamname == "CT")
                        _cachedCtTeam = team;
                    else if (team.Teamname == "TERRORIST")
                        _cachedTTeam = team;
                }
            }
            catch (Exception e)
            {
                Log($"[RefreshTeamEntities] Failed: {e.Message}");
            }
        }

        // Timers
        public CounterStrikeSharp.API.Modules.Timers.Timer? unreadyPlayerMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? sideSelectionMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? pausedStateTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? roundKnifeStartMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? readyStatusHintTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? matchEndMapChangeTimer = null;

        // Ready status hint - event-driven with dirty flag to avoid per-second recomputation
        private bool _readyStatusDirty = true;
        private string _cachedReadyHintMessage = "";

        // Each message is kept in chat display for ~13 seconds, hence setting default chat timer to 13 seconds.
        // Configurable using matchzy_chat_messages_timer_delay <seconds>
        public int chatTimerDelay = 13;
        public int afterReadyDelay = 3;
        public int roundKnifeStartMessageDelay = 11;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2;
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;
        public bool isPlayOutEnabled = false;
        public bool isPlayOutEnabled2 = false;
        // Folder name of a detected dedicated map plugin (CS2-SimpleAdmin / CS2MapChange), or null.
        // When set, MatchZy yields the map command: it registers neither css_map nor handles .map,
        // so a single .map does not fire two changelevels (which disconnects players). Set in Load.
        private string? _conflictingMapPlugin;
        // Server time of the last accepted map-change request, to debounce a duplicate .map that
        // fires both the chat dispatch and css_map (on servers where '.' is a chat trigger).
        private float _lastMapChangeRequestTime = -999f;
        public bool playerHasTakenDamage = false;

        // User command - action map
        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;

        // SQLite/MySQL Database
        private Database database = new();

        /// <summary>
        /// Count alive non-bot, non-HLTV players per team. Used by scorebot events.
        /// </summary>
        private (int ctAlive, int tAlive) CountAlivePlayers()
        {
            int ct = 0,
                t = 0;
            foreach (var kvp in playerData)
            {
                var p = kvp.Value;
                if (p == null || !p.IsValid || p.IsBot || p.IsHLTV)
                    continue;
                if (p.PlayerPawn?.Value == null)
                    continue;
                if (p.PlayerPawn.Value.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    continue;
                if (p.TeamNum == (int)CsTeam.CounterTerrorist)
                    ct++;
                else if (p.TeamNum == (int)CsTeam.Terrorist)
                    t++;
            }
            return (ct, t);
        }

        /// <summary>
        /// Get a player's side string ("CT" / "T" / "unknown").
        /// </summary>
        private static string GetTeamSide(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid)
                return "unknown";
            return player.TeamNum == (int)CsTeam.CounterTerrorist ? "CT" : "T";
        }

        public void DrawSideSelection()
        {
            if (!isSideSelectionPhase)
                return;

            SideSelectionTimer?.Kill();
            SideSelectionTimer = null;

            SideSelectionTimer = AddTimer(
                60.0f,
                () =>
                {
                    Server.NextFrame(() =>
                    {
                        if (!isSideSelectionPhase)
                            return;

                        PrintToAllChat(Localizer["matchzy.knife.decidedtostay", knifeWinnerName]);
                        StartLive();
                    });
                }
            );

            // Timers for alerts - these are safe as they only display messages
            AddTimer(
                20.0f,
                () =>
                {
                    if (isSideSelectionPhase)
                    {
                        Server.NextFrame(() =>
                        {
                            PrintToAllChat($"{ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has 40 seconds left to choose side!");
                        });
                    }
                }
            );

            AddTimer(
                40.0f,
                () =>
                {
                    if (isSideSelectionPhase)
                    {
                        Server.NextFrame(() =>
                        {
                            PrintToAllChat($"{ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has 20 seconds left to choose side!");
                        });
                    }
                }
            );

            AddTimer(
                50.0f,
                () =>
                {
                    if (isSideSelectionPhase)
                    {
                        Server.NextFrame(() =>
                        {
                            PrintToAllChat($"{ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has 10 seconds left to choose side!");
                        });
                    }
                }
            );
        }

        public override void Load(bool hotReload)
        {
            // Run DB init on a background thread instead of blocking Load with
            // .GetAwaiter().GetResult(). Blocking here races with other plugins'
            // SQLite native init on worker threads (observed crash: concurrent
            // open from CS2_SimpleAdmin + MatchZy main-thread open during Load
            // segfaulted in EnsureConnectionOpen). The Database type now serializes
            // its own connection access, so DB-dependent calls elsewhere are safe
            // to fire before this completes - they will block on the lock.
            string moduleDir = ModuleDirectory;
            string gameDir = Server.GameDirectory;
            _ = Task.Run(async () =>
            {
                try
                {
                    await database.InitializeDatabaseAsync(moduleDir, gameDir);
                }
                catch (Exception ex)
                {
                    Log($"[Load] Database init failed: {ex.Message}");
                }
            });
            // Wrap startup config/ConVar/AutoStart init in try/catch. A throw anywhere
            // here (missing/locked cfg file, transient I/O error, null ConVar) would
            // otherwise propagate out of Load() and make CSS abort the plugin load -
            // the observed "sometimes the plugin doesn't load". Treat as non-fatal.
            try
            {
                // This sets default config ConVars (Backup)
                //Server.ExecuteCommand("execifexists matchzy/config.cfg");

                //Loads CFGs from ConfigFiles.cs (Source Code)
                var configManager = new ConfigManager();
                configManager.InitializeConfigs();
                mapRotationList = configManager.LoadMapRotation();

                // Load MatchZy admins (admins.json). Was never called before, so
                // loadedAdmins stayed empty and IsPlayerAdmin ignored the file.
                LoadAdmins();

                // Use the SAME case-resolved dir the configs were created in. Hardcoding
                // lowercase "matchzy" here failed on case-sensitive Linux when the on-disk
                // dir was e.g. "MatchZy" → execifexists skipped → config.cfg (demo_path etc.) ignored.
                var matchzyCfgDir = configManager.GetMatchZyCfgDir();
                var cfgFolderName = Path.GetFileName(matchzyCfgDir.TrimEnd('/'));
                var configPath = Path.Combine(matchzyCfgDir, ConfigFiles.Paths.Config);
                if (File.Exists(configPath))
                {
                    Server.ExecuteCommand($"execifexists {cfgFolderName}/{ConfigFiles.Paths.Config}");
                }

                // Detect a dedicated map plugin (CS2-SimpleAdmin / CS2MapChange) now - folder scan,
                // config-independent - so both the console css_map registration below AND the .map
                // chat dispatch (see EventPlayerChat) can yield to it. If MatchZy also handled the
                // map change, one .map would fire TWO changelevels (MatchZy + that plugin via
                // CSS routing .map -> css_map), disconnecting players (NETWORK_DISCONNECT_CREATE_SERVER_FAILED).
                _conflictingMapPlugin = DetectConflictingMapPlugin();

                // Register css_map dynamically (NOT via a [ConsoleCommand] attribute) only when
                // enabled AND no dedicated map plugin is present. Deferred so the config.cfg value
                // is applied first.
                AddTimer(3.0f, () =>
                {
                    if (!mapConsoleCommandEnabled.Value)
                        return;
                    if (_conflictingMapPlugin != null)
                    {
                        Log($"[css_map] '{_conflictingMapPlugin}' detected - MatchZy is not registering css_map or handling .map (that plugin owns the map command).");
                        return;
                    }
                    AddCommand("css_map", "Changes the map (map name or workshop id)", (p, c) => OnMapCommand(p, c));
                });

                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;

                // Cache ConVar references once - avoids per-frame string-based lookups
                _cvTvEnable = ConVar.Find("tv_enable");
                _cvMatchRestartDelay = ConVar.Find("mp_match_restart_delay");
                _cvMatchEndChangelevel = ConVar.Find("mp_match_end_changelevel");
                _cvMatchEndRestart = ConVar.Find("mp_match_end_restart");

                if (!hotReload)
                {
                    // Initial autostart for the already-running map. OnMapStart does not
                    // replay for the map that was loaded before the plugin, so this timer
                    // is the only initial trigger. Use the same 1.0s delay + entity refresh
                    // as the OnMapStart path: 0.1s was too early - config.cfg exec hadn't
                    // applied (stale autoStartMode) and team entities weren't ready yet
                    // (AutoStart → StartWarmup touches entities → intermittent NRE).
                    AddTimer(
                        1.0f,
                        () =>
                        {
                            // AutoStart reads matchzy_autostart_mode live from its ConVar, so any
                            // cfg that set it (mapchange/exec scripts) is already reflected here.
                            RefreshTeamEntities();
                            AutoStart();
                        }
                    );
                }
                else
                {
                    // Plugin should not be reloaded while a match is live (this would messup with the match flags which were set)
                    // Only hot-reload the plugin if you are testing something and don't want to restart the server time and again.
                    UpdatePlayersMap();
                    RefreshTeamEntities();

                    // AutoStart reads matchzy_autostart_mode live from its ConVar (preserved across hot-reload).
                    AutoStart();
                }
            }
            catch (Exception ex)
            {
                Log($"[Load FATAL] Startup config/init failed (non-fatal, plugin still loaded): {ex}");
            }

            SetupRestartConfirmationCleanup();

            commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>
            {
                { ".ready", OnPlayerReady },
                { ".r", OnRCommand },
                { ".gaben", OnPlayerReady },
                { ".rdy", OnPlayerReady },
                { ".forceready", OnForceReadyCommandCommand },
                { ".unready", OnPlayerUnReady },
                { ".notready", OnPlayerUnReady },
                { ".ur", OnPlayerUnReady },
                { ".nr", OnPlayerUnReady },
                { ".urdy", OnPlayerUnReady },
                { ".stay", OnTeamStay },
                { ".switch", OnTeamSwitch },
                { ".swap", OnTeamSwitch },
                { ".tech", OnTechCommand },
                { ".p", OnPauseCommand },
                { ".pause", OnPauseCommand },
                { ".unpause", OnUnpauseCommand },
                { ".up", OnUnpauseCommand },
                { ".forcepause", OnForcePauseCommand },
                { ".fp", OnForcePauseCommand },
                { ".forceunpause", OnForceUnpauseCommand },
                { ".fup", OnForceUnpauseCommand },
                { ".tac", OnTacCommand },
                { ".rk", OnKnifeCommand },
                { ".knife", OnKnifeCommand },
                { ".kr", OnKnifeCommand },
                { ".start", OnStartCommand },
                { ".force", OnStartCommand },
                { ".forcestart", OnStartCommand },
                { ".breakrestore", OnBreakRestoreCommand },
                { ".nobreak", OnBreakRestoreCommand },
                { ".rs", OnRestartRoundCommand },
                { ".rr", OnRestartRoundCommand },
                { ".restart", OnRestartMatchCommand },
                { ".abort", OnRestartMatchCommand },
                { ".forceend", OnEndMatchCommand },
                { ".forcestop", OnStopMatchCommand },
                { ".matchgg", OnSurrenderCommand },
                { ".endmatch", OnStopMatchCommand },
                { ".endgame", OnStopMatchCommand },
                { ".endscrim", OnStopMatchCommand },
                { ".stopgame", OnStopMatchCommand },
                { ".stopmatch", OnStopMatchCommand },
                { ".exitscrim", OnStopMatchCommand },
                { ".end", OnStopMatchCommand },
                { ".settings", OnMatchSettingsCommand },
                { ".config", OnMatchSettingsCommand },
                { ".whitelist", OnWLCommand },
                { ".globalnades", OnSaveNadesAsGlobalCommand },
                { ".prac", OnPracCommand },
                { ".showspawns", OnShowSpawnsCommand },
                { ".hidespawns", OnHideSpawnsCommand },
                { ".tactics", OnPracCommand },
                { ".training", OnPracCommand },
                { ".dryrun", OnDryRunCommand },
                { ".dry", OnDryRunCommand },
                { ".exitdry", OnExitDryCommand },
                { ".exitdryrun", OnExitDryCommand },
                { ".stopdry", OnExitDryCommand },
                { ".enddry", OnExitDryCommand },
                { ".noflash", OnNoFlashCommand },
                { ".noblind", OnNoFlashCommand },
                { ".break", OnBreakCommand },
                { ".bot", OnBotCommand },
                { ".tbot", OnTBotCommand },
                { ".ctbot", OnCtBotCommand },
                { ".previewnade", OnTrajCommand },
                { ".nadepreview", OnTrajCommand },
                { ".nadecam", OnTrajCommand },
                { ".cam", OnTrajCommand },
                { ".warmup", OnWarmupCommand },
                { ".crouchbot", OnCrouchBotCommand },
                { ".tcrouchbot", OnTCrouchBotCommand },
                { ".ctcrouchbot", OnCtCrouchBotCommand },
                { ".cbot", OnCrouchBotCommand },
                { ".boost", OnBoostBotCommand },
                { ".crouchboost", OnCrouchBoostBotCommand },
                { ".cboost", OnCrouchBoostBotCommand },
                { ".nobot", OnNoBotCommand },
                { ".removebot", OnNoBotCommand },
                { ".kickbot", OnNoBotCommand },
                { ".unbot", OnNoBotCommand },
                { ".nb", OnNoBotCommand },
                { ".nobots", OnNoBotsCommand },
                { ".clearbots", OnNoBotsCommand },
                { ".removebots", OnNoBotsCommand },
                { ".kickbots", OnNoBotsCommand },
                { ".nbots", OnNoBotsCommand },
                { ".nbts", OnNoBotsCommand },
                { ".nbs", OnNoBotsCommand },
                { ".kbots", OnNoBotsCommand },
                { ".solid", OnSolidCommand },
                { ".impacts", OnImpactsCommand },
                { ".traj", OnTrajCommand },
                { ".pip", OnTrajCommand },
                { ".god", OnGodCommand },
                { ".ff", OnFFCommand },
                { ".fastforward", OnFFCommand },
                //{ ".clear", OnClearCommand },
                { ".clear", OnClearAllCommand },
                { ".match", OnMatchCommand },
                { ".uncoach", OnUnCoachCommand },
                { ".play", OnUnCoachCommand },
                { ".exitprac", OnExitPracCommand },
                { ".exittraining", OnExitPracCommand },
                { ".noprac", OnExitPracCommand },
                { ".stop", OnStopCommand },
                { ".help", OnHelpCommand },
                { ".scrim", OnScrimCommand },
                { ".playout", OnScrimCommand },
                { ".po", OnScrimCommand },
                { ".hill", OnHillCommand },
                { ".t", OnTCommand },
                { ".ct", OnCTCommand },
                { ".spec", OnSpecCommand },
                { ".fas", OnFASCommand },
                { ".watchme", OnFASCommand },
                { ".last", OnLastCommand },
                { ".throw", OnRethrowCommand },
                { ".rethrow", OnRethrowCommand },
                { ".rt", OnRethrowCommand },
                { ".grt", OnGlobalRethrowCommand },
                { ".globalrethrow", OnGlobalRethrowCommand },
                { ".throwsmoke", OnRethrowSmokeCommand },
                { ".rethrowsmoke", OnRethrowSmokeCommand },
                { ".thrownade", OnRethrowGrenadeCommand },
                { ".rethrownade", OnRethrowGrenadeCommand },
                { ".rethrowgrenade", OnRethrowGrenadeCommand },
                { ".throwgrenade", OnRethrowGrenadeCommand },
                { ".rethrowflash", OnRethrowFlashCommand },
                { ".throwflash", OnRethrowFlashCommand },
                { ".rethrowdecoy", OnRethrowDecoyCommand },
                { ".throwdecoy", OnRethrowDecoyCommand },
                { ".throwmolotov", OnRethrowMolotovCommand },
                { ".rethrowmolotov", OnRethrowMolotovCommand },
                { ".timer", OnTimerCommand },
                { ".sleep", OnSleepCommand },
                { ".lastindex", OnLastIndexCommand },
                { ".bestspawn", OnBestSpawnCommand },
                { ".worstspawn", OnWorstSpawnCommand },
                { ".bestctspawn", OnBestCTSpawnCommand },
                { ".worstctspawn", OnWorstCTSpawnCommand },
                { ".besttspawn", OnBestTSpawnCommand },
                { ".worsttspawn", OnWorstTSpawnCommand },
                { ".savepos", OnSavePosCommand },
                { ".loadpos", OnLoadPosCommand },
                { ".listpos", OnListPosCommand },
                { ".delpos", OnDelPosCommand },
                { ".flashtest", OnFlashTestCommand },
                { ".ft", OnFlashTestCommand },
                { ".blind", OnSelfFlashCommand },
                { ".wipe", OnWipeNadesCommand },
                { ".clearnades", OnWipeNadesCommand },
                { ".jt", OnJumpThrowCommand },
                { ".jumpthrow", OnJumpThrowCommand },
                { ".cleanup", OnCleanupCommand },
                { ".autoclear", OnAutoClearCommand },
                { ".landmarker", OnLandMarkerCommand },
                { ".lm", OnLandMarkerCommand },
                { ".mynades", OnMyNadesCommand },
                { ".arc", OnArcCommand },
                { ".traceline", OnArcCommand },
                { ".predict", OnPredictCommand },
                { ".version", OnMatchZyVersionCommand },
                { ".readycheck", OnReadyCheckCommand },
                { ".rcheck", OnReadyCheckCommand },
                { ".rc", OnReadyCheckCommand },
                { ".mhelp", OnAdminHelpCommand },
                { ".matchadmin", OnMatchAdminCommand },
                { ".ma", OnMatchAdminCommand },
                { ".matchsetup", OnMatchSetupCommand },
                //{ ".color", OnColorCommand }
                //{ ".gg", OnGGCommand }
            };

            RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFullHandler);
            RegisterEventHandler<EventPlayerDisconnect>(EventPlayerDisconnectHandler);
            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerSpawn>(OnCoachPlayerSpawn, HookMode.Post);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventPlayerPing>(EventPlayerPingHandler);
            //RegisterEventHandler<EventCsIntermission>(OnEventCsIntermissionPost);
            RegisterListener<Listeners.OnMapEnd>(OnMapEndHandler);
            RegisterListener<Listeners.OnClientDisconnectPost>(playerSlot =>
            {
                // May not be required, but just to be on safe side so that player data is properly updated in dictionaries
                // Update: Commenting the below function as it was being called multiple times on map change.
                // UpdatePlayersMap();
            });

            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);
            // Ready-panel HTML render: runs every tick but early-outs cheaply unless the ready
            // phase is active (matchzy_ready_hint_style = 1). Per-tick re-send keeps the panel
            // solid. NOTE: a ClientPrint suppression hook to hide the native "WARMUP" HUD was
            // tried and removed - the WARMUP banner is a client-side Panorama element driven by
            // m_bWarmupPeriod, NOT a server ClientPrint (verified in libserver.so: the "WARMUP"
            // string has no server xref), so no print hook can suppress it. The hook only spammed
            // the DynoHook "could not allocate trampoline" error for zero benefit.
            RegisterListener<Listeners.OnTick>(RenderReadyPanel);

            // Practice interactive spawn markers: +use aimed at a .showspawns beam teleports
            // to that spawn. Edge-triggered (fires once on Use press), gated on spawnMarkersActive.
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnSpawnMarkerButtonHandler);

            // Demo-arc sampler: samples traced grenades each tick. No-op unless .arc is on
            // and a grenade is mid-flight.
            RegisterListener<Listeners.OnTick>(TraceArcTick);


            RegisterEventHandler<EventPlayerTeam>(
                (@event, info) =>
                {
                    CCSPlayerController? player = @event.Userid;
                    if (!IsPlayerValid(player))
                        return HookResult.Continue;

                    if (matchzyTeam1.coach.Contains(player!) || matchzyTeam2.coach.Contains(player!))
                    {
                        @event.Silent = true;
                        return HookResult.Changed;
                    }
                    return HookResult.Continue;
                },
                HookMode.Pre
            );

            RegisterEventHandler<EventPlayerTeam>(
                (@event, info) =>
                {
                    if (!isMatchSetup && !isVeto)
                        return HookResult.Continue;

                    // Open-join matches (no team whitelist, e.g. .matchsetup wizard): let players choose freely.
                    if (!IsTeamWhitelistConfigured())
                        return HookResult.Continue;

                    CCSPlayerController? player = @event.Userid;

                    if (!IsPlayerValid(player))
                        return HookResult.Continue;

                    if (player!.IsHLTV || player.IsBot)
                    {
                        return HookResult.Continue;
                    }

                    CsTeam playerTeam = GetPlayerTeam(player);

                    SwitchPlayerTeam(player, playerTeam);

                    return HookResult.Continue;
                }
            );

            AddCommandListener(
                "jointeam",
                (player, info) =>
                {
                    if ((isMatchSetup || isVeto) && player != null && player.IsValid && IsTeamWhitelistConfigured())
                    {
                        if (int.TryParse(info.ArgByIndex(1), out int joiningTeam))
                        {
                            int playerTeam = (int)GetPlayerTeam(player);

                            // Allow spectators to join freely (team 1 = spectator in CS2)
                            if (joiningTeam == 1)
                            {
                                return HookResult.Continue;
                            }

                            // For other teams, restrict to assigned team
                            if (joiningTeam != playerTeam)
                            {
                                return HookResult.Stop;
                            }
                        }
                    }
                    return HookResult.Continue;
                }
            );

            AddCommandListener("noclip", OnConsoleNoClip); // Override noclip

            RegisterEventHandler<EventRoundEnd>(
                (@event, info) =>
                {
                    if (!isKnifeRound)
                        return HookResult.Continue;

                    DetermineKnifeWinner();
                    @event.Winner = knifeWinner;
                    int finalEvent = 10;
                    if (knifeWinner == 3)
                    {
                        finalEvent = 8;
                    }
                    else if (knifeWinner == 2)
                    {
                        finalEvent = 9;
                    }
                    @event.Reason = finalEvent;
                    isSideSelectionPhase = true;
                    isKnifeRound = false;
                    StartAfterKnifeWarmup();

                    return HookResult.Changed;
                },
                HookMode.Pre
            );

            RegisterEventHandler<EventRoundEnd>(
                (@event, info) =>
                {
                    try
                    {
                        if (isDryRun)
                        {
                            // Mark dryrun as finished immediately so no other handlers treat this as dryrun
                            isDryRun = false;

                            // CRITICAL: Do NOT call StartPracticeMode() or mp_restartgame synchronously
                            // inside EventRoundEnd - the engine is mid-round-transition and player/entity
                            // state is not stable. Defer to a short timer so the round end completes cleanly.
                            AddTimer(
                                0.5f,
                                () =>
                                {
                                    StartPracticeMode();
                                    PrintToAllChat($"{ChatColors.Green}Practice Mode has been restored. You can run .dry again if you wish.");
                                    Server.ExecuteCommand("mp_warmup_start; mp_warmup_pausetimer 1; mp_restartgame 1");
                                }
                            );

                            return HookResult.Continue;
                        }

                        if (!isMatchLive)
                            return HookResult.Continue;
                        HandlePostRoundEndEvent(@event);
                        return HookResult.Continue;
                    }
                    catch (Exception e)
                    {
                        Log($"[EventRoundEnd FATAL] An error occurred: {e.Message}");
                        return HookResult.Continue;
                    }
                },
                HookMode.Post
            );

            RegisterListener<Listeners.OnMapStart>(mapName =>
            {
                // Re-arm AutoStart latch: allow exactly one AutoStart for this new map.
                autoStartLatched = false;
                AddTimer(
                    1.0f,
                    () =>
                    {
                        // Refresh cached team entities after map load (entities aren't available immediately)
                        RefreshTeamEntities();

                        if (!isMatchSetup)
                        {
                            AutoStart();
                            return;
                        }
                        if (isWarmup)
                            StartWarmup();
                        if (isPractice)
                            StartPracticeMode();
                    }
                );
            });

            RegisterEventHandler<EventPlayerDeath>(
                (@event, info) =>
                {
                    // Setting money back to 16000 when a player dies in warmup
                    var player = @event.Userid;
                    if (!isWarmup)
                        return HookResult.Continue;
                    if (!IsPlayerValid(player))
                        return HookResult.Continue;
                    if (player!.InGameMoneyServices != null)
                        player.InGameMoneyServices.Account = 16000;
                    return HookResult.Continue;
                }
            );

            // Practice side-switch (.t/.ct/.spec) suicide must not count as a death. This Post
            // handler fires AFTER the engine has incremented the death stat, so decrementing here
            // lands on the same tick - the scoreboard never settles on the +1.
            RegisterEventHandler<EventPlayerDeath>(
                (@event, info) =>
                {
                    var victim = @event.Userid;
                    if (victim == null || !victim.IsValid || victim.UserId == null)
                        return HookResult.Continue;
                    if (!practiceSwitchNoDeath.Remove(victim.UserId.Value))
                        return HookResult.Continue;

                    var ms = victim.ActionTrackingServices?.MatchStats;
                    if (ms != null)
                        ms.Deaths = Math.Max(0, ms.Deaths - 1);
                    // Suicide also docks a point - give it back so the switch is score-neutral.
                    victim.Score += 1;
                    return HookResult.Continue;
                }
            );

            // Advanced stats tracking for player deaths
            RegisterEventHandler<EventPlayerDeath>(
                (@event, info) =>
                {
                    if (!isMatchLive)
                        return HookResult.Continue;

                    var victim = @event.Userid;
                    var attacker = @event.Attacker;
                    var assister = @event.Assister;

                    OnAdvancedStatsPlayerDeath(victim, attacker, assister);

                    return HookResult.Continue;
                }
            );

            // ── Live scorebot: player_death event ──
            RegisterEventHandler<EventPlayerDeath>(
                (@event, info) =>
                {
                    if (!matchStarted)
                        return HookResult.Continue;
                    if (string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                        return HookResult.Continue;

                    var victim = @event.Userid;
                    if (victim == null || !victim.IsValid)
                        return HookResult.Continue;

                    var attacker = @event.Attacker;
                    var assister = @event.Assister;
                    bool isSuicide = attacker == null || !attacker.IsValid || attacker == victim;

                    // Count alive players per team AFTER this death
                    var (ctAlive, tAlive) = CountAlivePlayers();

                    var deathEvent = new PlayerDeathLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        AttackerName = isSuicide ? null : attacker?.PlayerName,
                        AttackerSteamId = isSuicide ? null : attacker?.SteamID.ToString(),
                        AttackerTeam = isSuicide ? null : GetTeamSide(attacker),
                        VictimName = victim.PlayerName,
                        VictimSteamId = victim.SteamID.ToString(),
                        VictimTeam = GetTeamSide(victim),
                        AssisterName = assister != null && assister.IsValid ? assister.PlayerName : null,
                        AssisterSteamId = assister != null && assister.IsValid ? assister.SteamID.ToString() : null,
                        Weapon = @event.Weapon ?? "unknown",
                        Headshot = @event.Headshot,
                        Penetrated = @event.Penetrated > 0,
                        Noscope = @event.Noscope,
                        Thrusmoke = @event.Thrusmoke,
                        Attackerblind = @event.Attackerblind,
                        IsSuicide = isSuicide,
                        CtAlive = ctAlive,
                        TAlive = tAlive,
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(deathEvent);
                    });
                    return HookResult.Continue;
                },
                HookMode.Post
            );

            // ── Live scorebot: bomb_planted event ──
            RegisterEventHandler<EventBombPlanted>(
                (@event, info) =>
                {
                    if (!matchStarted)
                        return HookResult.Continue;
                    if (string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                        return HookResult.Continue;

                    var player = @event.Userid;
                    if (player == null || !player.IsValid)
                        return HookResult.Continue;

                    // Count alive players
                    var (ctAlive, tAlive) = CountAlivePlayers();

                    var plantEvent = new BombPlantedLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        PlayerName = player.PlayerName,
                        PlayerSteamId = player.SteamID.ToString(),
                        Site = @event.Site == 0 ? "A" : "B",
                        CtAlive = ctAlive,
                        TAlive = tAlive,
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(plantEvent);
                    });
                    return HookResult.Continue;
                }
            );

            // ── Live scorebot: bomb_defused event ──
            RegisterEventHandler<EventBombDefused>(
                (@event, info) =>
                {
                    if (!matchStarted)
                        return HookResult.Continue;
                    if (string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                        return HookResult.Continue;

                    var player = @event.Userid;
                    if (player == null || !player.IsValid)
                        return HookResult.Continue;

                    // Count alive players
                    var (ctAlive, tAlive) = CountAlivePlayers();

                    var defuseEvent = new BombDefusedLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        PlayerName = player.PlayerName,
                        PlayerSteamId = player.SteamID.ToString(),
                        Site = @event.Site == 0 ? "A" : "B",
                        CtAlive = ctAlive,
                        TAlive = tAlive,
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(defuseEvent);
                    });
                    return HookResult.Continue;
                }
            );

            // ── Live scorebot: freezetime_end event ──
            RegisterEventHandler<EventRoundFreezeEnd>(
                (@event, info) =>
                {
                    if (!matchStarted)
                        return HookResult.Continue;
                    if (string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                        return HookResult.Continue;

                    int ctAlive = 0,
                        tAlive = 0;
                    var players = new List<LivePlayerInfo>();

                    foreach (var kvp in playerData)
                    {
                        var p = kvp.Value;
                        if (p == null || !p.IsValid || p.IsBot || p.IsHLTV)
                            continue;
                        if (p.PlayerPawn?.Value == null)
                            continue;

                        var pawn = p.PlayerPawn.Value;
                        bool alive = pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE;
                        string side =
                            p.TeamNum == (int)CsTeam.CounterTerrorist ? "CT"
                            : p.TeamNum == (int)CsTeam.Terrorist ? "T"
                            : "SPEC";
                        if (side == "SPEC")
                            continue;

                        if (alive)
                        {
                            if (side == "CT")
                                ctAlive++;
                            else
                                tAlive++;
                        }

                        players.Add(
                            new LivePlayerInfo
                            {
                                Name = p.PlayerName,
                                SteamId = p.SteamID.ToString(),
                                Team = side,
                                Hp = alive ? pawn.Health : 0,
                                Armor = pawn.ArmorValue,
                                HasHelmet = p.PlayerPawn.Value.ItemServices != null && (p.PlayerPawn.Value.ItemServices as CCSPlayer_ItemServices)?.HasHelmet == true,
                                HasDefuser = p.PlayerPawn.Value.ItemServices != null && (p.PlayerPawn.Value.ItemServices as CCSPlayer_ItemServices)?.HasDefuser == true,
                                Money = p.InGameMoneyServices?.Account ?? 0,
                            }
                        );
                    }

                    var freezeEndEvent = new FreezetimeEndLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        CtAlive = ctAlive,
                        TAlive = tAlive,
                        Players = players,
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(freezeEndEvent);
                    });
                    return HookResult.Continue;
                }
            );

            RegisterEventHandler<EventPlayerHurt>(
                (@event, info) =>
                {
                    CCSPlayerController? attacker = @event.Attacker;
                    CCSPlayerController? victim = @event.Userid;

                    if (!IsPlayerValid(attacker) || !IsPlayerValid(victim))
                        return HookResult.Continue;

                    if (isPractice && victim!.IsBot && !attacker!.IsBot)
                    {
                        int damage = @event.DmgHealth;
                        int postDamageHealth = @event.Health;
                        PrintToPlayerChat(attacker!, Localizer.ForPlayer(attacker!, "matchzy.pracc.damage", damage, victim.PlayerName, postDamageHealth));
                        return HookResult.Continue;
                    }

                    if (!attacker!.IsValid)
                        return HookResult.Continue;

                    bool isDamageDealt = @event.DmgHealth > 0 || @event.DmgArmor > 0;
                    if (attacker.IsBot && !isDamageDealt)
                        return HookResult.Continue;
                    if (matchStarted && victim!.TeamNum != attacker.TeamNum)
                    {
                        int targetId = (int)victim.UserId!;
                        UpdatePlayerDamageInfo(@event, targetId);
                        if (attacker != victim)
                            playerHasTakenDamage = true;
                    }

                    return HookResult.Continue;
                }
            );

            // ── Live scorebot: player_hurt event ──
            RegisterEventHandler<EventPlayerHurt>(
                (@event, info) =>
                {
                    if (!isMatchLive)
                        return HookResult.Continue;
                    if (string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                        return HookResult.Continue;

                    var victim = @event.Userid;
                    if (victim == null || !victim.IsValid || victim.IsBot)
                        return HookResult.Continue;

                    var attacker = @event.Attacker;
                    bool hasDamage = @event.DmgHealth > 0 || @event.DmgArmor > 0;
                    if (!hasDamage)
                        return HookResult.Continue;

                    var hurtEvent = new PlayerHurtLiveEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        AttackerName = attacker != null && attacker.IsValid && !attacker.IsBot ? attacker.PlayerName : null,
                        AttackerSteamId = attacker != null && attacker.IsValid && !attacker.IsBot ? attacker.SteamID.ToString() : null,
                        VictimName = victim.PlayerName,
                        VictimSteamId = victim.SteamID.ToString(),
                        VictimTeam = GetTeamSide(victim),
                        HpRemaining = @event.Health,
                        ArmorRemaining = @event.Armor,
                        DamageHealth = @event.DmgHealth,
                        DamageArmor = @event.DmgArmor,
                        Weapon = @event.Weapon ?? "unknown",
                        Hitgroup = @event.Hitgroup,
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(hurtEvent);
                    });
                    return HookResult.Continue;
                }
            );

            RegisterEventHandler<EventPlayerChat>(
                (@event, info) =>
                {
                    int currentVersion = Api.GetVersion();
                    int index = @event.Userid + 1;

                    // Validate the userid before proceeding
                    if (@event.Userid < 0)
                    {
                        // This is likely a console command or invalid event, skip processing
                        return HookResult.Continue;
                    }

                    var playerUserId = NativeAPI.GetUseridFromIndex(index);

                    var originalMessage = @event.Text.Trim();
                    var message = @event.Text.Trim().ToLower();

                    var parts = originalMessage.Split(' ');
                    var messageCommand = parts.Length > 0 ? parts[0] : string.Empty;
                    var messageCommandArg = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

                    CCSPlayerController? player = null;
                    if (playerData.TryGetValue(playerUserId, out CCSPlayerController? value))
                    {
                        player = value;
                    }

                    if (player == null)
                    {
                        // Somehow we did not have the player in playerData, hence updating the maps again before getting the player
                        UpdatePlayersMap();

                        // Add validation before accessing the dictionary
                        if (playerData.TryGetValue(playerUserId, out CCSPlayerController? playerValue))
                        {
                            player = playerValue;
                        }
                        else
                        {
                            // Player not found even after updating, skip processing
                            return HookResult.Continue;
                        }
                    }

                    // Final null check - if player is still null after all attempts, return
                    if (player == null)
                    {
                        return HookResult.Continue;
                    }

                    // Handling player commands - exact match first, then prefix-based
                    if (commandActions.TryGetValue(message, out var action))
                    {
                        action(player, null);
                        // Exact match found - skip prefix checks (these commands take no args)
                        return HookResult.Continue;
                    }

                    if (message.StartsWith(".readyrequired"))
                    {
                        HandleReadyRequiredCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".teamsize"))
                    {
                        HandleReadyRequiredCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".restore"))
                    {
                        HandleRestoreCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".asay"))
                    {
                        HandleAdminSayCommand(player, messageCommandArg);
                    }

                    // .map chat dispatch (CSS's default chat triggers are ! and /, so . is not
                    // auto-routed to css_map - MatchZy handles it here). Skip it when MatchZy is
                    // yielding the map command (convar off, or a dedicated map plugin detected) so
                    // the owning plugin handles .map alone. HandleMapChangeCommand is also debounced,
                    // which covers servers that DO add . as a chat trigger (then .map hits both this
                    // dispatch and css_map) - without it the map would change twice and disconnect
                    // players (NETWORK_DISCONNECT_CREATE_SERVER_FAILED).
                    if (message.StartsWith(".map") && mapConsoleCommandEnabled.Value && _conflictingMapPlugin == null)
                    {
                        HandleMapChangeCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".savenade") || message.StartsWith(".sn"))
                    {
                        HandleSaveNadeCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".delnade") || message.StartsWith(".dn"))
                    {
                        HandleDeleteNadeCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".deletenade"))
                    {
                        HandleDeleteNadeCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".importnade"))
                    {
                        HandleImportNadeCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".listnades") || message.StartsWith(".lin"))
                    {
                        HandleListNadesCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".loadnade") || message.StartsWith(".ln"))
                    {
                        HandleLoadNadeCommand(player, messageCommandArg);
                    }

                    // Named position slots (#2). No-arg .savepos/.loadpos/.listpos/.delpos hit the
                    // exact-match block above (returns early); these fire only for the arg forms.
                    if (message.StartsWith(".savepos "))
                    {
                        HandleSavePosCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".loadpos "))
                    {
                        HandleLoadPosCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".delpos "))
                    {
                        HandleDelPosCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".spawn") || message.StartsWith(".sp"))
                    {
                        HandleSpawnCommand(player, messageCommandArg, player.TeamNum, "spawn");
                    }

                    if (message.StartsWith(".ctspawn") || message.StartsWith(".cts"))
                    {
                        HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
                    }

                    if (message.StartsWith(".tspawn") || message.StartsWith(".ts"))
                    {
                        HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.Terrorist, "tspawn");
                    }

                    if (message.StartsWith(".team1") || message.StartsWith(".ctname"))
                    {
                        HandleTeamNameChangeCommand(player, messageCommandArg, 1);
                    }

                    if (message.StartsWith(".team2") || message.StartsWith(".tname"))
                    {
                        HandleTeamNameChangeCommand(player, messageCommandArg, 2);
                    }

                    if (message.StartsWith(".savecoachspawn"))
                    {
                        HandleSaveCoachSpawnCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".clearcoachspawns"))
                    {
                        HandleClearCoachSpawnsCommand(player);
                    }

                    if (message.StartsWith(".listcoachspawns"))
                    {
                        HandleListCoachSpawnsCommand(player);
                    }

                    if (message.StartsWith(".coach"))
                    {
                        HandleCoachCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".ban"))
                    {
                        HandeMapBanCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".pick"))
                    {
                        HandeMapPickCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".back"))
                    {
                        HandleBackCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".delay"))
                    {
                        HandleDelayCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".throwindex"))
                    {
                        HandleThrowIndexCommand(player, messageCommandArg);
                    }

                    if (message.StartsWith(".throwidx"))
                    {
                        HandleThrowIndexCommand(player, messageCommandArg);
                    }

                    return HookResult.Continue;
                }
            );

            RegisterEventHandler<EventPlayerBlind>(
                (@event, info) =>
                {
                    CCSPlayerController? player = @event.Userid;
                    CCSPlayerController? attacker = @event.Attacker;
                    if (!isPractice)
                        return HookResult.Continue;

                    if (!IsPlayerValid(player) || !IsPlayerValid(attacker))
                        return HookResult.Continue;

                    if (attacker!.IsValid && !attacker.IsBot)
                    {
                        double roundedBlindDuration = Math.Round(@event.BlindDuration, 2);
                        PrintToPlayerChat(attacker, Localizer.ForPlayer(attacker, "matchzy.pracc.blind", player!.PlayerName, roundedBlindDuration));
                    }

                    var userId = player!.UserId;

                    // #3 flash-test HUD: tell the VICTIM their own blind duration (pop-flash /
                    // self-flash tuning), opt-in per player via .flashtest. Distinct from the
                    // attacker readout above.
                    if (userId != null && !player.IsBot && flashTestList.Contains((int)userId))
                    {
                        double ownBlind = Math.Round(@event.BlindDuration, 2);
                        bool self = attacker!.Handle == player.Handle;
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, self ? "matchzy.pm.flashtestself" : "matchzy.pm.flashtestother", $"{ownBlind}"));
                    }

                    if (userId != null && noFlashList.Contains((int)userId))
                    {
                        Server.NextFrame(() =>
                        {
                            // Re-validate player - may have disconnected
                            if (!IsPlayerValid(player))
                                return;
                            KillFlashEffect(player);
                        });
                    }

                    return HookResult.Continue;
                }
            );

            RegisterEventHandler<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            RegisterEventHandler<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventMolotovDetonate>(EventMolotovDetonateHandler);
            RegisterEventHandler<EventDecoyStarted>(EventDecoyDetonateHandler);
        }
    }
}
