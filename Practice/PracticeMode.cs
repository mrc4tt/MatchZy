using System.Drawing;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public class Position
    {
        public Vector PlayerPosition { get; private set; }
        public QAngle PlayerAngle { get; private set; }

        // Copy constructor
        public Position(Position other)
        {
            PlayerPosition = other.PlayerPosition;
            PlayerAngle = other.PlayerAngle;
        }

        public Position(Vector playerPosition, QAngle playerAngle)
        {
            // Create deep copies of the Vector and QAngle objects
            PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
            PlayerAngle = new QAngle(playerAngle.X, playerAngle.Y, playerAngle.Z);
        }

        public void Teleport(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null)
                return;
            player.PlayerPawn.Value.Teleport(PlayerPosition, PlayerAngle, new Vector(0, 0, 0));
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            Position otherPosition = (Position)obj;

            return PlayerPosition.X == otherPosition.PlayerPosition.X && PlayerPosition.Y == otherPosition.PlayerPosition.Y && PlayerPosition.Z == otherPosition.PlayerPosition.Z && PlayerAngle.X == otherPosition.PlayerAngle.X && PlayerAngle.Y == otherPosition.PlayerAngle.Y && PlayerAngle.Z == otherPosition.PlayerAngle.Z;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + PlayerPosition.X.GetHashCode();
                hash = hash * 23 + PlayerPosition.Y.GetHashCode();
                hash = hash * 23 + PlayerPosition.Z.GetHashCode();
                hash = hash * 23 + PlayerAngle.X.GetHashCode();
                hash = hash * 23 + PlayerAngle.Y.GetHashCode();
                hash = hash * 23 + PlayerAngle.Z.GetHashCode();
                return hash;
            }
        }
    }

    public static class StringSimilarity
    {
        // Dice coefficient function
        public static double DiceCoefficient(string s1, string s2)
        {
            var bigrams1 = GetBigrams(s1);
            var bigrams2 = GetBigrams(s2);

            int intersection = bigrams1.Intersect(bigrams2).Count();
            return (2.0 * intersection) / (bigrams1.Count + bigrams2.Count);
        }

        // Get bigrams function
        private static List<string> GetBigrams(string input)
        {
            var bigrams = new List<string>();
            for (int i = 0; i < input.Length - 1; i++)
            {
                bigrams.Add(input.Substring(i, 2));
            }
            return bigrams;
        }

        /// <summary>
        /// Finds the name from a list of names that is nearest to the input name using the Dice coefficient.
        /// </summary>
        /// <param name="inputName">The input name to match.</param>
        /// <param name="names">The list of names to search from.</param>
        /// <returns>The nearest matching name from the list.</returns>
        /// <summary>
        /// An exact (case-insensitive) match first, then the closest name by Dice coefficient, but
        /// only when it is close enough. FindNearestName always returns something, so a typo loaded
        /// (or deleted) a different, unrelated entry instead of answering "not found".
        /// </summary>
        public static string? FindMatchingName(string inputName, List<string> names, double minScore = 0.4)
        {
            string? exact = names.FirstOrDefault(n => string.Equals(n, inputName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
            if (inputName.Length == 1)
                return names.FirstOrDefault(name => name.StartsWith(inputName, StringComparison.OrdinalIgnoreCase));
            string? best = null;
            double bestScore = -1;
            foreach (var name in names)
            {
                double score = DiceCoefficient(inputName, name);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = name;
                }
            }
            return bestScore >= minScore ? best : null;
        }

        public static string FindNearestName(string inputName, List<string> names)
        {
            if (inputName.Length == 1)
            {
                // If input name is a single character, find the name that starts with the same character
                var matchingName = names.FirstOrDefault(name => name.StartsWith(inputName, StringComparison.OrdinalIgnoreCase));
                if (matchingName != null)
                {
                    return matchingName;
                }
            }
            // Otherwise, use the Dice coefficient to find the nearest name
            string nearestName = names.OrderByDescending(name => DiceCoefficient(inputName, name)).FirstOrDefault() ?? inputName;
            return nearestName;
        }
    }

    // Demo-arc: sampled flight path of one thrown grenade (see TraceArcTick / DrawArc).
    public class NadeArcTrace
    {
        public List<Vector> Points = new();
        public int Ticks = 0;
    }

    public partial class MatchZy
    {
        int maxLastGrenadesSavedLimit = 512;
        // Per-account saved-lineup cap (.savenade / .mynades).
        const int maxSavedNades = 500;
        Dictionary<int, List<GrenadeThrownData>> lastGrenadesData = new();
        // Per-player cursor into lastGrenadesData for no-arg .back stepping (issue
        // MatchZy-Enhanced#7 / CS:GO prac parity). Absent = not stepping yet: the first
        // no-arg .back jumps to the newest nade, each subsequent press steps one older,
        // and it stops at the oldest instead of wrapping. `.last` and `.back N` prime the
        // cursor so the next no-arg .back steps from there. Reset on a new throw and on
        // disconnect (indices renumber when the history is trimmed).
        Dictionary<int, int> lastGrenadeBackCursor = new();
        // Interactive spawn markers (issue MatchZy-Enhanced#9): while .showspawns beams
        // are drawn, pressing +use aimed at a marker teleports to that spawn. activeSpawnMarkers
        // mirrors the drawn beams (CT then T); spawnMarkersActive gates the OnPlayerButtonsChanged
        // listener so it's a no-op when hidden / outside practice. Per-player use timestamp
        // debounces a rapid/held +use so one press = one teleport.
        bool spawnMarkersActive = false;
        readonly List<Position> activeSpawnMarkers = new();
        readonly Dictionary<int, float> lastSpawnMarkerUseTime = new();
        const float spawnMarkerUseCooldown = 0.3f;      // seconds between accepted +use teleports
        const float spawnMarkerAimMinDot = 0.985f;      // ~10 degree aim cone onto a marker
        const float spawnMarkerStandRadiusSq = 8.0f * 8.0f;  // issue #11: only the spawn you're exactly on is excluded
        const float spawnMarkerStandHeight = 90.0f;     // vertical band for the standing-on check
        Dictionary<int, Dictionary<string, GrenadeThrownData>> nadeSpecificLastGrenadeData = new();
        Dictionary<int, DateTime> lastGrenadeThrownTime = new();
        // Molotov/incendiary detonation time is tracked PER PROJECTILE (entity index -> throw time +
        // thrower), because a per-player slot made a second molotov overwrite the first (double-lineup
        // practice printed wrong/missing times). EventMolotovDetonate carries no usable entityid, so the
        // print happens when the PROJECTILE entity is deleted: the projectile dies the moment its fire
        // starts, and OnEntityDeletedHandler pairs it with an EventInfernoStartburn seen for the same
        // thrower within 0.2s (infernoStartTimes). A mid-air burst deletes the projectile with no
        // startburn, so it correctly prints nothing. The startburn also records whether the fire came
        // from an incendiary (SourceItemDefIndex 48) - the old TeamNum label lied for picked-up nades.
        Dictionary<int, (DateTime Time, int Client)> molotovProjectileThrows = new();
        Dictionary<int, (DateTime Time, bool IsIncendiary)> infernoStartTimes = new();
        Dictionary<int, PlayerPracticeTimer> playerTimers = new();
        Dictionary<int, PlayerLocationData> savedPlayerLocationData = new();
        // Named position slots (#2): .savepos <name> / .loadpos <name> / .listpos / .delpos <name>.
        // Separate from the single default slot above (no-arg .savepos/.loadpos). userId -> name -> pos.
        Dictionary<int, Dictionary<string, PlayerLocationData>> namedPlayerPositions = new();
        const int maxNamedPositions = 32;
        // Flash-test HUD (#3): userIds who opted into a chat readout of their own blind duration
        // whenever they get flashed (pop-flash / self-flash tuning). Toggled by .flashtest / .ft.
        readonly HashSet<int> flashTestList = new();
        // .autoclear: when true, every detonation wipes older utility and keeps only the just-
        // detonated result (fast lineup iteration). Server-wide toggle.
        bool autoClearUtility = false;
        // .landmarker: when true, each detonation drops a short-lived beam at the impact point.
        bool showLandingMarkers = false;
        // .arc / .traceline (demo-arc): when true, each thrown grenade's flight is sampled
        // (projectile index -> point list) and drawn as a CBeam poly-line when it lands. Sampling
        // runs in TraceArcTick, gated so it's a no-op when nothing is being traced.
        bool traceNadeArcs = false;
        readonly Dictionary<uint, NadeArcTrace> tracedArcs = new();
        int arcTickCounter = 0;


        public Dictionary<byte, List<Position>> spawnsData = GetEmptySpawnsData();
        public Dictionary<byte, List<Position>> coachSpawns = GetEmptySpawnsData();

        // (Backup)
        private readonly object _botsDictLock = new();
        public Dictionary<int, Dictionary<string, object>> pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
        private readonly HashSet<int> _botsBeingProcessed = new();

        public string practiceCfgPath => MatchZyCfgRel("prac.cfg");

        // Guard for practice-only commands: outside practice, tell the player instead of doing
        // nothing (a silent return looked like a broken command).
        private bool RequirePractice(CCSPlayerController? player)
        {
            if (isPractice)
                return true;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cmd.pracnotactive"));
            return false;
        }
        public string dryrunCfgPath => MatchZyCfgRel("dryrun.cfg");

        // Resolved by key from the plugin's own gamedata/matchzy.json (single source of truth) -
        // the byte signature lives ONLY in gamedata, never in this source, so it self-heals on a
        // CS2 update by regenerating the gamedata entry (no code change / rebuild of MatchZy).
        // Ships with the plugin, so it resolves on fork and stock upstream CounterStrikeSharp.
        //
        // Lazy + guarded so the lookup runs on first practice use, NOT during MatchZy's static
        // initialization. A throwing resolve in a static field initializer surfaces as a
        // TypeInitializationException while CSS is instantiating the plugin (before Load(),
        // outside every try/catch) and makes CSS skip the whole plugin - the intermittent
        // "MatchZy didn't auto-load, need css_plugins load MatchZy" boot failure. If the key is
        // somehow absent (server on an older fork build), GetSignature throws and we degrade to a
        // no-op (breakrestore reports "unavailable" instead of crashing).
        private static readonly Lazy<Func<CCSGameRules, nint>?> CCSGameRules_PostCleanUp = new(() =>
        {
            try
            {
                return new MemoryFunctionWithReturn<CCSGameRules, nint>(
                    GameData.GetSignature("CCSGameRules_PostCleanUp")).Invoke;
            }
            catch
            {
                return null;
            }
        });

        private Dictionary<string, CounterStrikeSharp.API.Modules.Timers.Timer> collisionGroupTimers = new();
        public bool isSpawningBot;
        public bool isDryRun = false;
        public List<int> noFlashList = new List<int>();

        // UserIds whose next death is a practice side-switch suicide (.t/.ct/.spec) - zeroed in
        // the Post EventPlayerDeath handler so it never counts on the scoreboard.
        public readonly HashSet<int> practiceSwitchNoDeath = new();

        // Flags a practice team-switch suicide so it does not count as a death (see the Post
        // EventPlayerDeath handler in MatchZy.cs). The flag expires after a second: if the death
        // event never arrives, a stale flag would otherwise swallow the player's next real death.
        private void MarkPracticeSwitchNoDeath(int userId)
        {
            practiceSwitchNoDeath.Add(userId);
            AddTimer(1.0f, () => practiceSwitchNoDeath.Remove(userId));
        }

        public static Dictionary<byte, List<Position>> GetEmptySpawnsData()
        {
            // Pre-size the lists. Growing a List (List.AddWithResize) threw
            // ArrayTypeMismatchException when GetSpawns runs under the AcceleratorCSS Harmony tracer
            // (patched GetSpawns_Patch1): the resize allocates a fresh backing array and the
            // instrumented generic path mistyped it. A capacity that comfortably covers any
            // competitive map means the throwing resize path is never taken.
            return new Dictionary<byte, List<Position>>
            {
                { (byte)CsTeam.CounterTerrorist, new List<Position>(128) },
                { (byte)CsTeam.Terrorist, new List<Position>(128) },
            };
        }

        public void StartPracticeMode()
        {
            if (matchStarted)
                return;
            // A restore queued by a .match game does not survive a switch to practice (it kept the
            // match state across every later map change).
            isRoundRestorePending = false;
            pendingRestoreFileName = "";
            // Practice manages its own bots; clear any warmup aim-bots first.
            KillWarmupBots();
            isPractice = true;
            isDryRun = false;
            isWarmup = false;
            readyAvailable = false;

            // Kill ready status hint timer
            readyStatusHintTimer?.Kill();
            readyStatusHintTimer = null;

            // Undo the ready-phase HUD manipulation before loading prac.cfg. The panel forces
            // m_bWarmupPeriod=false + m_bGameRestart (to hide WARMUP + stop flashing); left set,
            // a stuck m_bGameRestart makes the game think a restart is already running, so
            // prac.cfg's mp_restartgame/mp_warmup_start are ignored (round never restarts, warmup
            // time wrong). Reset to a clean warmup baseline first.
            RestoreReadyPhaseGameState();

            ClearClanTags();

            // Reset all player practice settings when entering practice
            ResetAllPlayerPracticeSettings(enteringPractice: true);

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath)))
            {
                practiceUsesWarmup = false;
                Server.ExecuteCommand($"execifexists {practiceCfgPath};{ModeOverrideExec(practiceCfgPath)};mp_roundtime 60;mp_roundtime_defuse 60");
            }
            else
            {
                practiceUsesWarmup = true;
                Server.ExecuteCommand("""sv_cheats "true"; mp_force_pick_time "0"; bot_quota "0"; sv_showimpacts "1"; mp_limitteams "0"; sv_deadtalk "true"; sv_full_alltalk "true"; sv_ignoregrenaderadio "false"; mp_forcecamera "0"; sv_grenade_trajectory_prac_pipreview "true"; sv_grenade_trajectory_prac_trailtime "3"; sv_infinite_ammo "1"; weapon_auto_cleanup_time "15"; weapon_max_before_cleanup "30"; mp_buy_anywhere "1"; mp_maxmoney "9999999"; mp_startmoney "9999999";""");
                Server.ExecuteCommand("""mp_weapons_allow_typecount "-1"; mp_death_drop_defuser "false"; mp_death_drop_taser "false"; mp_drop_knife_enable "true"; mp_death_drop_grenade "0"; ammo_grenade_limit_total "5"; mp_defuser_allocation "2"; mp_free_armor "2"; mp_ct_default_grenades "weapon_incgrenade weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_ct_default_primary "weapon_m4a1";""");
                Server.ExecuteCommand("""mp_t_default_grenades "weapon_molotov weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_t_default_primary "weapon_ak47"; mp_warmuptime 9999; mp_warmuptime_all_players_connected 0; mp_warmup_online_enabled "true"; mp_warmup_pausetimer "1"; mp_warmup_start; bot_quota_mode normal; mp_solid_teammates 2; mp_autoteambalance false; mp_teammates_are_enemies false; buddha 1; buddha_ignore_bots 1; buddha_reset_hp 100;""");
                // CS2 March 2026+: Disable magazine-based reload (ammo discard on reload) for practice mode
                if (pracDisableMagazineDrop.Value)
                {
                    Server.ExecuteCommand("""sv_magazine_drop_enabled "false";""");
                }
            }

            // Practice team-damage: grenade / molotov / friendly-fire testing would otherwise trip the
            // round's team-damage penalties (kick / warn). Disable them in practice regardless of which
            // cfg branch ran above.
            Server.ExecuteCommand("mp_autokick 0; mp_spawnprotectiontime 0; mp_td_dmgtokick 0; mp_td_dmgtowarn 0; mp_td_spawndmgthreshold 0; mp_tkpunish 0");

            // Clear leftover bots ourselves (by name, CSTV-safe) - the cfg templates no longer run a
            // bare bot_kick, which could also take out the CSTV bot and kill GOTV.
            KickAllBotsProtectCSTV();

            // prac.cfg runs mp_warmup_start, not a full mp_restartgame, so the round-history icons
            // ("skulls") above the scoreboard survive whatever was played before - most visibly when
            // going dryrun -> .prac, where the dryrun rounds stay on screen. Practice never wants a
            // round history, so wipe it on entry (and again per round below).
            ScheduleRoundHistoryWipe();

            // Practice started right after a map load (AutoStart at +1s) usually runs before anyone
            // is connected. The engine only begins its warmup period on the first player connect, so
            // prac.cfg's mp_warmup_end was a no-op and the player then lands in a mp_warmuptime
            // countdown (5s on a typical server cfg) before practice actually becomes a round.
            // Settle it once things have landed; the warmup RoundStart in HandlePostRoundStartEvent
            // covers the first-connect case whenever it happens.
            AddTimer(3.0f, () => SettlePracticeWarmupState("+3s"));

            // Resolve the grenade factory signatures now, while practice is switching modes anyway,
            // instead of on the first .rt / .throw of each grenade type (a long stall mid-practice).
            long prewarmStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int factories = GrenadeFunctions.Prewarm();
            Log($"[Practice] Grenade factories resolved: {factories}/4 in {System.Diagnostics.Stopwatch.GetElapsedTime(prewarmStart).TotalMilliseconds:0.0} ms.");

            GetSpawns();
            string[] practiceHelpKeys =
            {
                "matchzy.prac.help.spawns", "matchzy.prac.help.bots", "matchzy.prac.help.botpositions",
                "matchzy.prac.help.nades", "matchzy.prac.help.nadethrow", "matchzy.prac.help.utility",
                "matchzy.prac.help.positions", "matchzy.prac.help.sides", "matchzy.prac.help.full"
            };
            foreach (var helpPlayer in Utilities.GetPlayers())
            {
                if (helpPlayer == null || !helpPlayer.IsValid || helpPlayer.IsBot || helpPlayer.IsHLTV)
                    continue;
                foreach (var helpKey in practiceHelpKeys)
                    helpPlayer.PrintToChat($" {Localizer.ForPlayer(helpPlayer, helpKey)}");
            }
        }

        public void GetSpawns()
        {
            // Resetting spawn data to avoid any glitches
            spawnsData = GetEmptySpawnsData();

            int ctSkipped = 0, tSkipped = 0;
            try
            {
                // Materialize the entity queries to a concrete List<SpawnPoint> BEFORE iterating.
                // Iterating the lazy IEnumerable from FindAllEntitiesByDesignerName directly threw
                // ArrayTypeMismatchException when GetSpawns runs under the AcceleratorCSS Harmony
                // tracer (GetSpawns_Patch1): the instrumented generic-enumerator path mistypes its
                // backing array. A concrete List materialized outside the hot loop dodges it, and the
                // pre-sized spawnsData lists (GetEmptySpawnsData) avoid the resize path too.
                var spawnsct = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").ToList();
                ctSkipped = CollectMinPrioritySpawns(spawnsct, (byte)CsTeam.CounterTerrorist);

                var spawnst = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").ToList();
                tSkipped = CollectMinPrioritySpawns(spawnst, (byte)CsTeam.Terrorist);
            }
            catch (Exception e)
            {
                // Never let a spawn-scan failure (or a tracer artifact) crash the .prac command; keep
                // whatever was collected so far.
                Log($"[GetSpawns] Error scanning spawns: {e.GetType().Name}: {e.Message}");
            }

            Log($"[GetSpawns] Loaded {spawnsData[(byte)CsTeam.CounterTerrorist].Count} CT spawns, {spawnsData[(byte)CsTeam.Terrorist].Count} T spawns" + (ctSkipped + tSkipped > 0 ? $" (skipped {ctSkipped} CT / {tSkipped} T with null body/scene components)" : ""));

            GetCoachSpawns();
        }

        // Finds the minimum spawn priority in the (already materialized) list, then collects every
        // enabled spawn at that priority into spawnsData[team]. Returns how many were skipped for a
        // null body/scene component (some workshop maps ship spawns without a populated SceneNode,
        // which would NRE when building a Position). IMPORTANT: all competitive spawns are kept (not
        // just 5) so a coach present still leaves enough points for the 5 regular players.
        private int CollectMinPrioritySpawns(List<SpawnPoint> spawns, byte team)
        {
            int minPriority = int.MaxValue;
            foreach (var spawn in spawns)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority < minPriority)
                    minPriority = spawn.Priority;
            }

            int skipped = 0;
            foreach (var spawn in spawns)
            {
                if (!spawn.IsValid || !spawn.Enabled || spawn.Priority != minPriority)
                    continue;
                var origin = spawn.CBodyComponent?.SceneNode?.AbsOrigin;
                var rotation = spawn.CBodyComponent?.SceneNode?.AbsRotation;
                if (origin == null || rotation == null)
                {
                    skipped++;
                    continue;
                }
                spawnsData[team].Add(new Position(origin, rotation));
            }
            return skipped;
        }

        private void HandleSpawnCommand(CCSPlayerController? player, string commandArg, byte teamNum, string command)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            if (teamNum != 2 && teamNum != 3)
                return;
            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int spawnNumber) && spawnNumber >= 1)
                {
                    // Adjusting the spawnNumber according to the array index.
                    spawnNumber -= 1;
                    if (spawnsData.ContainsKey(teamNum) && spawnsData[teamNum].Count <= spawnNumber)
                        return;
                    if (player?.PlayerPawn?.IsValid == true && player.PlayerPawn.Value != null)
                    {
                        // Route through TeleportUpright so a steep spawn angle doesn't tilt the
                        // whole body (issue MatchZy-Enhanced#8).
                        TeleportUpright(player, spawnsData[teamNum][spawnNumber].PlayerPosition, spawnsData[teamNum][spawnNumber].PlayerAngle);
                    }
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.movedtospawn", $"{spawnNumber + 1}/{spawnsData[teamNum].Count}"));
                }
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.negativenumber", command));
                    return;
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!{command} <number>"));
            }
        }

        private string GetNadeType(string nadeName)
        {
            switch (nadeName)
            {
                case "weapon_flashbang":
                    return "Flash";
                case "weapon_smokegrenade":
                    return "Smoke";
                case "weapon_hegrenade":
                    return "HE";
                case "weapon_decoy":
                    return "Decoy";
                case "weapon_molotov":
                    return "Molly";
                case "weapon_incgrenade":
                    return "Molly";
                default:
                    return "";
            }
        }

        private void HandleSaveNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Parse: <name> [throwtype] [comment]. The 2nd token is treated as a throw style only
                // if it is a recognized one (normal/jump/run/walk/crouch...); otherwise it is part of
                // the comment. So both ".sn ctspawn jumpthrow bad smoke" and ".sn ctspawn just a note"
                // work - the first stores Throw="Jumpthrow" Desc="bad smoke", the second Desc="just a note".
                string[] lineupUserString = saveNadeName.Split(' ');
                string lineupName = lineupUserString[0];
                string lineupThrow = "";
                int descStart = 1;
                if (lineupUserString.Length >= 2)
                {
                    string? t = NormalizeThrowType(lineupUserString[1]);
                    if (t != null) { lineupThrow = t; descStart = 2; }
                }
                string lineupDesc = lineupUserString.Length > descStart
                    ? string.Join(" ", lineupUserString, descStart, lineupUserString.Length - descStart)
                    : "";

                // Get player info: steamid, pos, ang
                string playerSteamID;
                if (isSaveNadesAsGlobalEnabled == false)
                {
                    playerSteamID = player!.SteamID.ToString();
                }
                else
                {
                    playerSteamID = "default";
                }

                var pawn = player!.Pawn.Value;
                var playerPawn = player.PlayerPawn.Value;
                var sceneNode = pawn?.CBodyComponent?.SceneNode;
                if (playerPawn == null || sceneNode == null || sceneNode.AbsOrigin == null)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.posreadfailed"));
                    return;
                }
                QAngle playerAngle = playerPawn.EyeAngles;
                Vector playerPos = sceneNode.AbsOrigin;
                string currentMapName = Server.MapName;
                // Resolve the grenade in hand. Saving while holding a knife/pistol/rifle (or with no
                // active weapon) is allowed on purpose: Type stays empty and the marker label shows a
                // blank "[]" type line (name + throw still render). The ?? "" keeps this null-safe so
                // the command never throws when no weapon is active.
                string activeWeapon = playerPawn.WeaponServices?.ActiveWeapon?.Value?.DesignerName ?? "";
                string nadeType = GetNadeType(activeWeapon);

                // Define the file path
                string savednadesfileName = MatchZyCfgRel("savednades.json");
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                // Check if the file exists, if not, create it with an empty JSON object
                if (!File.Exists(savednadesPath))
                {
                    File.WriteAllText(savednadesPath, "{}");
                }

                try
                {
                    // Read existing JSON content
                    string existingJson = ReadSavedNadesJson(savednadesPath);

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson) ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    // Check if the lineup name already exists for the given SteamID
                    if (savedNadesDict.ContainsKey(playerSteamID) && savedNadesDict[playerSteamID].ContainsKey(lineupName))
                    {
                        // Check if the lineup already exists on the same map
                        if (savedNadesDict[playerSteamID][lineupName]["Map"] == currentMapName)
                        {
                            // Lineup already exists on the same map, reply to the user and return
                            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupissaved"));
                            return;
                        }
                    }

                    // Per-account save cap: allow overwriting an existing name, but block
                    // brand-new lineups once the player hits maxSavedNades.
                    bool isNewLineup = !savedNadesDict.ContainsKey(playerSteamID) || !savedNadesDict[playerSteamID].ContainsKey(lineupName);
                    int currentCount = savedNadesDict.TryGetValue(playerSteamID, out var existingSlots) ? existingSlots.Count : 0;
                    if (isNewLineup && currentCount >= maxSavedNades)
                    {
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nadelimitreached", $"{maxSavedNades}"));
                        return;
                    }

                    // Update or add the new lineup information
                    if (!savedNadesDict.ContainsKey(playerSteamID))
                    {
                        savedNadesDict[playerSteamID] = new Dictionary<string, Dictionary<string, string>>();
                    }

                    savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                    {
                        // Store the EXACT feet origin (AbsOrigin), matching getpos_exact.
                        // A previous +4 lift wrote the position 4u above the real stance,
                        // so .loadnade teleported you 4u high - throwing before you fell
                        // those 4u released the nade from the wrong height (clipped tight
                        // corners). Teleporting to the exact standing origin is safe (the
                        // player stood there), same as setpos_exact.
                        // Invariant culture: "{x}" used the server locale, so a ',' locale wrote
                        // "123,5" and the file then failed to load elsewhere.
                        { "Position", LineupFormat.Format(playerPos.X, playerPos.Y, playerPos.Z) },
                        { "Angles", LineupFormat.Format(playerAngle.X, playerAngle.Y, playerAngle.Z) },
                        { "Desc", lineupDesc },
                        { "Map", currentMapName },
                        { "Type", nadeType },
                        { "Throw", lineupThrow },
                    };

                    // Serialize the updated dictionary back to JSON
                    string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                    // Write the updated JSON content back to the file
                    File.WriteAllText(savednadesPath, updatedJson);
                    RefreshNadeMarkersIfActive(player);

                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.lineupsavedsucces", lineupName));
                    PrintLocalizedToAll("matchzy.pm.playersavedlineup", player.PlayerName, $"{lineupName} {playerPos} {playerAngle}");
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $".savenade <name> [throwtype] <comment> (throwtype: normal/jump/run/walk/crouch; shows on the .shownades label)"));
            }
        }

        [ConsoleCommand("css_mynades", "Shows how many grenade lineups you have saved")]
        public void OnMyNadesCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            string steamId = isSaveNadesAsGlobalEnabled ? "default" : player.SteamID.ToString();
            string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
            int count = 0;
            try
            {
                if (File.Exists(path))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path));
                    if (dict != null && dict.TryGetValue(steamId, out var slots))
                        count = slots.Count;
                }
            }
            catch (Exception e)
            {
                Log($"[MyNades] {e.Message}");
            }
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.mynades", $"{count}", $"{maxSavedNades}"));
        }

        private void HandleDeleteNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || player == null)
                return;

            if (string.IsNullOrWhiteSpace(saveNadeName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".delnade <name> [name2 ...] | .delnade all"));
                return;
            }

            string playerSteamID = isSaveNadesAsGlobalEnabled ? "default" : player.SteamID.ToString();
            string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));

            try
            {
                string existingJson = ReadSavedNadesJson(savednadesPath);
                var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson) ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                if (!savedNadesDict.TryGetValue(playerSteamID, out var slots) || slots.Count == 0)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupnotfound", saveNadeName));
                    return;
                }

                string currentMap = Server.MapName;
                string[] names = saveNadeName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var deleted = new List<string>();
                var notFound = new List<string>();

                if (names.Length == 1 && names[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    // Delete every lineup this player saved on the current map.
                    foreach (var kv in slots.Where(k => k.Value.TryGetValue("Map", out var m) && m == currentMap).ToList())
                    {
                        slots.Remove(kv.Key);
                        deleted.Add(kv.Key);
                    }
                }
                else
                {
                    // Delete each named lineup (only if it lives on the current map, as before).
                    foreach (var name in names)
                    {
                        if (slots.TryGetValue(name, out var info) && info.TryGetValue("Map", out var m) && m == currentMap)
                        {
                            slots.Remove(name);
                            deleted.Add(name);
                        }
                        else
                        {
                            notFound.Add(name);
                        }
                    }
                }

                if (deleted.Count > 0)
                {
                    File.WriteAllText(savednadesPath, JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true }));
                    RefreshNadeMarkersIfActive(player);
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nadesdeleted", string.Join(", ", deleted)));
                }
                if (notFound.Count > 0)
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nadesnotfound", string.Join(", ", notFound)));
                if (deleted.Count == 0 && notFound.Count == 0)
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupnotfound", saveNadeName));
            }
            catch (JsonException ex)
            {
                Log($"Error handling JSON: {ex.Message}");
            }
        }

        private void HandleImportNadeCommand(CCSPlayerController? player, string saveNadeCode)
        {
            if (!isPractice || player == null)
                return;

            if (!string.IsNullOrWhiteSpace(saveNadeCode))
            {
                try
                {
                    // Split the code into parts
                    string[] parts = saveNadeCode.Split(' ');

                    // Check if there are enough parts
                    if (parts.Length == 7)
                    {
                        // Extract name, pos, and ang from the parts
                        string lineupName = parts[0].Trim();
                        // A trailing ',' (copied from a getpos line) is dropped; a ',' inside a number is a
                        // decimal separator. Removing every ',' turned "1,5" into 15.
                        string[] posAng = parts.Skip(1).Select(p => p.TrimEnd(',')).ToArray();

                        // Only finite numbers inside the map bounds: NaN / Infinity / 1e39 were stored
                        // as-is and later used as a teleport target and marker origin.
                        var parsed = new float[6];
                        for (int i = 0; i < 6; i++)
                        {
                            if (!LineupFormat.TryParseNumber(posAng[i], out parsed[i]) || Math.Abs(parsed[i]) > 65536f)
                            {
                                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupinvalidcode"));
                                return;
                            }
                        }

                        // Get player info: steamid
                        string playerSteamID = player.SteamID.ToString();
                        string currentMapName = Server.MapName;

                        // Define the file path
                        string savednadesfileName = MatchZyCfgRel("savednades.json");
                        string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                        // Read existing JSON content
                        string existingJson = ReadSavedNadesJson(savednadesPath);

                        //Console.WriteLine($"Existing JSON Content: {existingJson}");

                        // Deserialize the existing JSON content
                        var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson) ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                        // Check if the lineup name already exists for the given SteamID on the same map
                        if (savedNadesDict.ContainsKey(playerSteamID) && savedNadesDict[playerSteamID].ContainsKey(lineupName))
                        {
                            var existingLineup = savedNadesDict[playerSteamID][lineupName];
                            if (existingLineup.ContainsKey("Map") && existingLineup["Map"] == currentMapName)
                            {
                                // Lineup already exists on the same map, reply to the user and return
                                // ReplyToUserCommand(player, $"Lineup '{lineupName}' already exists! Please use a different name or use .delnade <nade>");
                                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupalreadyexists", lineupName));
                                return;
                            }
                        }

                        // Update or add the new lineup information
                        if (!savedNadesDict.ContainsKey(playerSteamID))
                        {
                            savedNadesDict[playerSteamID] = new Dictionary<string, Dictionary<string, string>>();
                        }

                        savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                        {
                            { "Position", LineupFormat.Format(parsed[0], parsed[1], parsed[2]) },
                            { "Angles", LineupFormat.Format(parsed[3], parsed[4], parsed[5]) },
                            { "Desc", "" },
                            { "Map", currentMapName },
                            // The import code has no grenade type; .loadnade and the library need one.
                            { "Type", "Smoke" },
                        };

                        // Serialize the updated dictionary back to JSON
                        string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                        // Write the updated JSON content back to the file
                        File.WriteAllText(savednadesPath, updatedJson);
                        RefreshNadeMarkersIfActive(player);

                        // ReplyToUserCommand(player, $"Lineup '{lineupName}' imported and saved successfully.");
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupimportedsuccess", lineupName));
                    }
                    else
                    {
                        // ReplyToUserCommand(player, $"Invalid code format. Please provide a valid code with name, pos, and ang.");
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupinvalidcode"));
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .importnade <code>");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $".importnade <code>"));
            }
        }

        // Read savednades.json, tolerating its absence. On a fresh server (or before
        // the first .savenade) the file does not exist yet; File.ReadAllText would throw
        // FileNotFoundException, which the callers' catch (JsonException) does NOT cover,
        // crashing .listnades/.loadnade/.delnade/.importnade. Returning "{}" yields an
        // empty dict so the normal "no lineups" branches fire instead.
        private static string ReadSavedNadesJson(string path)
            => File.Exists(path) ? File.ReadAllText(path) : "{}";

        private void HandleListNadesCommand(CCSPlayerController? player, string nadeFilter)
        {
            if (!isPractice || player == null)
                return;

            // Define the file path
            string savednadesfileName = MatchZyCfgRel("savednades.json");
            string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

            try
            {
                // Read existing JSON content
                string existingJson = ReadSavedNadesJson(savednadesPath);

                //Console.WriteLine($"Existing JSON Content: {existingJson}");

                // Deserialize the existing JSON content
                var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson) ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.lineuplistheader", Server.MapName));

                var ordered = OrderedLineupsForMap(player, savedNadesDict, nadeFilter);
                if (ordered.Count == 0)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nosavedlineups", Server.MapName));
                }
                else
                {
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        string type = ordered[i].Info.TryGetValue("Type", out var t) ? t : "";
                        string name = ordered[i].Name;
                        // #N [Type] .ln <Name> or .ln #N
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.lineuplistentry", i + 1, type, name));
                    }
                }
            }
            catch (JsonException ex)
            {
                Log($"Error handling JSON: {ex.Message}");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.jsonerror"));
            }
        }

        // group: the savednades.json owner key ("default" or a SteamID) when the caller already knows
        // which lineup it means (.ln #N, the nades menu); null searches the player's own, then default.
        private void HandleLoadNadeCommand(CCSPlayerController? player, string loadNadeName, string? group = null)
        {
            if (!isPractice || player == null || !IsPlayerValid(player))
                return;
            // Covers the chat .ln path and the nades menu too, not only css_loadnade.
            if (!CanPracticeTeleport(player))
                return;

            if (!string.IsNullOrWhiteSpace(loadNadeName))
            {
                // Get player info: steamid
                string playerSteamID = player.SteamID.ToString();

                // Define the file path
                string savednadesfileName = MatchZyCfgRel("savednades.json");
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                try
                {
                    // Read existing JSON content
                    string existingJson = ReadSavedNadesJson(savednadesPath);

                    //Console.WriteLine($"Existing JSON Content: {existingJson}");

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson) ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    // Load by index: .ln #3 or .ln 3 -> the 3rd lineup as numbered by .listnades.
                    string idxArg = loadNadeName.Trim().TrimStart('#');
                    if (int.TryParse(idxArg, out int loadIdx))
                    {
                        var ordered = OrderedLineupsForMap(player, savedNadesDict, "");
                        if (loadIdx >= 1 && loadIdx <= ordered.Count)
                        {
                            loadNadeName = ordered[loadIdx - 1].Name;
                            group = ordered[loadIdx - 1].Steam;
                        }
                        else
                        {
                            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nadenotfound", loadNadeName));
                            return;
                        }
                    }

                    bool lineupFound = false;
                    bool lineupOnWrongMap = false;

                    // Resolve which lineup is meant before loading anything: an exact name in either
                    // group wins, then the closest name across both groups if it is close enough. It
                    // used to take the player's own closest name first, so clicking a shared lineup
                    // (or a typo) loaded an unrelated personal lineup.
                    string[] groups = group != null ? new[] { group } : new[] { playerSteamID, "default" };
                    var namesByGroup = groups
                        .Where(g => savedNadesDict.ContainsKey(g))
                        .Select(g => (Group: g, Names: savedNadesDict[g].Where(n => n.Value.ContainsKey("Map") && n.Value["Map"] == Server.MapName).Select(n => n.Key).ToList()))
                        .ToList();
                    string? resolvedGroup = null;
                    string? resolvedName = null;
                    foreach (var (g, names) in namesByGroup)
                    {
                        string? exact = names.FirstOrDefault(n => string.Equals(n, loadNadeName, StringComparison.OrdinalIgnoreCase));
                        if (exact != null)
                        {
                            resolvedGroup = g;
                            resolvedName = exact;
                            break;
                        }
                    }
                    if (resolvedName == null)
                    {
                        var all = namesByGroup.SelectMany(x => x.Names.Select(n => (x.Group, Name: n))).ToList();
                        string? fuzzy = StringSimilarity.FindMatchingName(loadNadeName, all.Select(x => x.Name).ToList());
                        if (fuzzy != null)
                        {
                            resolvedName = fuzzy;
                            resolvedGroup = all.First(x => x.Name == fuzzy).Group;
                        }
                    }

                    foreach (string currentSteamID in groups)
                    {
                        if (currentSteamID == resolvedGroup && savedNadesDict.ContainsKey(currentSteamID))
                        {
                            string nearestName = resolvedName!;

                            if (savedNadesDict[currentSteamID].ContainsKey(nearestName))
                            {
                                var lineupInfo = savedNadesDict[currentSteamID][nearestName];

                                // Check if the lineup contains the "Map" key and if it matches the current map
                                if (lineupInfo.ContainsKey("Map") && lineupInfo["Map"] == Server.MapName)
                                {
                                    // Parse position and angle. A malformed entry used to throw a
                                    // FormatException the JsonException handler did not catch.
                                    if (!LineupFormat.TryParse(lineupInfo.GetValueOrDefault("Position"), out float px, out float py, out float pz)
                                        || !LineupFormat.TryParse(lineupInfo.GetValueOrDefault("Angles"), out float ax, out float ay, out float az))
                                    {
                                        Log($"[LoadNade] Lineup '{nearestName}' has an invalid position or angle; skipping.");
                                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.lineupinvalidpos", nearestName));
                                        return;
                                    }
                                    Vector loadedPlayerPos = new Vector(px, py, pz);
                                    QAngle loadedPlayerAngle = new QAngle(ax, ay, az);

                                    // Issues #391/#393 (AG2): teleport to the
                                    // lineup position and clear any stuck throw
                                    // pose via a weapon re-deploy (no respawn).
                                    // The grenade is re-deployed by classname
                                    // (CS2 `slotN` grenade commands are dead).
                                    // Imported lineups (.importnade) carry no Type; default to smoke.
                                    string nadeType = lineupInfo.TryGetValue("Type", out var storedType) ? storedType : "Smoke";
                                    bool isCT = player.TeamNum == (byte)CsTeam.CounterTerrorist;
                                    string nadeWeapon = nadeType switch
                                    {
                                        "Flash" => "weapon_flashbang",
                                        "Smoke" => "weapon_smokegrenade",
                                        "HE" => "weapon_hegrenade",
                                        "Decoy" => "weapon_decoy",
                                        "Molly" => isCT ? "weapon_incgrenade" : "weapon_molotov",
                                        _ => "weapon_smokegrenade",
                                    };
                                    TeleportAndClearPose(player, loadedPlayerPos, loadedPlayerAngle, wantDucked: false, deployWeapon: nadeWeapon, giveDeploy: true);

                                    // Extract description, if available
                                    string? lineupDesc = lineupInfo.ContainsKey("Desc") ? lineupInfo["Desc"] : null;

                                    // Print messages
                                    // ReplyToUserCommand(player, $"Lineup {ChatColors.Green}{nearestName}{ChatColors.Default} loaded successfully!");
                                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineuploadedsuccess", nearestName));

                                    if (!string.IsNullOrWhiteSpace(lineupDesc))
                                    {
                                        player.PrintToCenter($"{lineupDesc}");
                                        // ReplyToUserCommand(player, $"Description: {ChatColors.Green}{lineupDesc}{ChatColors.Default}");
                                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.lineupdesc", lineupDesc));
                                    }

                                    lineupFound = true;
                                    break;
                                }
                                else
                                {
                                    // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{nearestName}{ChatColor.Default} not found on the current map!");
                                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nadenotfoundonmap", nearestName));
                                    lineupOnWrongMap = true;
                                }
                            }
                        }
                    }

                    if (!lineupFound && !lineupOnWrongMap)
                    {
                        // Lineup not found
                        // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{loadNadeName}{ChatColor.Default} not found!");
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nadenotfound", loadNadeName));
                    }
                }
                catch (Exception ex)
                {
                    // Any failure (unreadable savednades.json, ...) used to leave the player without a reply.
                    Log($"[LoadNade] {ex.Message}");
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.nadenotfound", loadNadeName));
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Nade not found! Usage: .loadnade <name>");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.loadnadenotfound"));
            }
        }

        public void ShowSpawnBeam(Position spawn, Color color)
        {
            CBeam? beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam == null)
            {
                Log($"Failed to create beam for the spawn");
                return;
            }

            beam.LifeState = 1;
            beam.Width = 5;
            beam.Render = color;

            // Lift the beam base +8u off the floor (issue MatchZy-Enhanced#11): at +0 the
            // marker sinks under shallow water (e.g. de_ancient) and is invisible.
            Vector basePos = new Vector(spawn.PlayerPosition.X, spawn.PlayerPosition.Y, spawn.PlayerPosition.Z + 8.0f);

            beam.EndPos.X = basePos.X;
            beam.EndPos.Y = basePos.Y;
            beam.EndPos.Z = spawn.PlayerPosition.Z + 100.0f;

            beam.Teleport(basePos, new QAngle(0, 0, 0), new Vector(0, 0, 0));

            beam.DispatchSpawn();
            spawnMarkerBeams.Add(beam.EntityHandle.Raw);
        }

        // Only the beams ShowSpawnBeam drew. Bot-position, coach-spawn, grenade-arc and landing
        // markers share the "beam" designer name but have their own owners/toggles, so a
        // designer-wide sweep here left those toggles on with dead entity refs. Stored as raw
        // entity handles (index + serial) so a stale entry after a map change resolves to null
        // instead of a dangling pointer.
        readonly List<uint> spawnMarkerBeams = new();

        public void RemoveSpawnBeams()
        {
            foreach (uint raw in spawnMarkerBeams)
            {
                var beam = new CHandle<CBeam>(raw).Value;
                if (beam != null && beam.IsValid)
                    SafeRemoveEntity(beam, "spawnbeam");
            }
            spawnMarkerBeams.Clear();
            // Full teardown: also disarm the +use interaction. Every mode-transition path
            // (match start, prac restart, sleep, map change) already calls RemoveSpawnBeams,
            // so folding the flag/list reset here disarms interaction on all of them.
            spawnMarkersActive = false;
            activeSpawnMarkers.Clear();
            // Also tear down grenade-library markers (same mode-transition paths).
            HideNadeMarkers();
        }

        // #F self-flash: throw a flashbang at your own face for pop-flash reaction reps
        // (no teammate/bind needed). Spawns a flashbang_projectile at eye height moving
        // forward a hair so it arms and pops in front of you. Marked Globalname="custom"
        // so OnEntitySpawned does NOT record it into the .last/.rt nade history.
        [ConsoleCommand("css_blind", "Throw a flashbang at yourself (pop-flash reaction practice)")]
        public void OnSelfFlashCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !player.UserId.HasValue || player.PlayerPawn.Value == null)
                return;
            if (player.TeamNum != (byte)CsTeam.CounterTerrorist && player.TeamNum != (byte)CsTeam.Terrorist)
                return;
            var pawn = player.PlayerPawn.Value;
            if (pawn.AbsOrigin == null)
                return;

            Vector origin = pawn.AbsOrigin;
            QAngle ang = pawn.EyeAngles;
            double pitch = ang.X * Math.PI / 180.0;
            double yaw = ang.Y * Math.PI / 180.0;
            float fx = (float)(Math.Cos(pitch) * Math.Cos(yaw));
            float fy = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            float fz = (float)(-Math.Sin(pitch));
            Vector spawnPos = new Vector(origin.X + fx * 20f, origin.Y + fy * 20f, origin.Z + 62f);
            Vector velocity = new Vector(fx * 150f, fy * 150f, fz * 150f + 60f);

            var flash = Utilities.CreateEntityByName<CFlashbangProjectile>("flashbang_projectile");
            if (flash == null)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.selfflashfail"));
                return;
            }
            flash.DispatchSpawn();
            flash.InitialPosition.X = spawnPos.X;
            flash.InitialPosition.Y = spawnPos.Y;
            flash.InitialPosition.Z = spawnPos.Z;
            flash.InitialVelocity.X = velocity.X;
            flash.InitialVelocity.Y = velocity.Y;
            flash.InitialVelocity.Z = velocity.Z;
            flash.Teleport(spawnPos, new QAngle(0, 0, 0), velocity);
            flash.Globalname = "custom";
            flash.TeamNum = player.TeamNum;
            flash.Thrower.Raw = player.PlayerPawn.Raw;
            flash.OriginalThrower.Raw = player.PlayerPawn.Raw;
            flash.OwnerEntity.Raw = player.PlayerPawn.Raw;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.selfflash"));
        }

        // #I wipe: clear this player's grenade throw history (.last / .back / .rt / .throwindex
        // sources) without leaving/re-entering practice.
        [ConsoleCommand("css_wipe", "Clears your grenade throw history")]
        [ConsoleCommand("css_clearnades", "Clears your grenade throw history")]
        public void OnWipeNadesCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            lastGrenadesData.Remove(userId);
            nadeSpecificLastGrenadeData.Remove(userId);
            lastGrenadeBackCursor.Remove(userId);
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nadeswiped"));
        }

        [ConsoleCommand("css_god", "Sets Infinite health for player")]
        public void OnGodCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !IsPlayerValid(player))
                return;

            if (player?.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.godfailed"));
                return;
            }

            int currentHP = player.PlayerPawn.Value.Health;

            if (currentHP > 100)
            {
                player.PlayerPawn.Value.Health = 100;
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.godstate", Localizer.ForPlayer(player, "matchzy.cc.disabled")));
            }
            else
            {
                player.PlayerPawn.Value.Health = int.MaxValue - 100; // max 32bit int
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.godstate", Localizer.ForPlayer(player, "matchzy.cc.enabled")));
            }
        }

        [ConsoleCommand("css_prac", "Starts practice mode")]
        [ConsoleCommand("css_tactics", "Starts practice mode")]
        public void OnPracCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, "Practice Mode cannot be started when a match has been started!");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.pracmatchstarted"));
                return;
            }

            matchStarted = false;
            matchStartInProgress = false;
            isPlayOutEnabled = false;
            isPlayOutEnabled2 = false;
            isKnifeRound = false;
            isKnifeRequired = false;

            SetExplicitMode(2);
            StartPracticeMode();
        }

        [ConsoleCommand("css_dry", "Starts dryrun")]
        [ConsoleCommand("css_dryrun", "Starts dryrun")]
        public void OnDryRunCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.dryrunmatchstarted"));
                return;
            }

            // If already in dryrun, just restart the round instead of blocking
            if (isDryRun)
            {
                Server.ExecuteCommand("mp_restartgame 1");
                ScheduleRoundHistoryWipe();
                PrintLocalizedToAll("matchzy.prac.dryrunrestarted");
                return;
            }

            // Coming from practice: the same teardown as leaving practice for a match, or the
            // practice per-player settings (impacts, grenade preview) and the spawn / lineup markers
            // stayed on through the dryrun.
            if (isPractice)
            {
                ResetAllPlayerPracticeSettings(enteringPractice: false);
                CleanupAllCollisionTimers();
                RemoveSpawnBeams();
            }

            KickAllBotsProtectCSTV();
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
            noFlashList = new();

            ExecUnpracCommands(); // reset practice-specific cvars
            ExecDryRunCFG(); // apply dry run cvars and restart
            readyStatusHintTimer?.Kill();
            readyStatusHintTimer = null;
            ClearClanTags();
            ClearPracticeTimers();

            isPractice = false;
            isDryRun = true;
            autoStartLatched = true; // explicit mode choice; no autostart mode maps to dryrun, so leave the cvar alone

            // Start dryrun from an empty round-history strip - whatever practice/warmup left behind is
            // not part of this dryrun. The rounds played during the dryrun then build up normally.
            ScheduleRoundHistoryWipe();
        }

        [ConsoleCommand("css_exitdryrun", "Exit Dryrun (back to match warmup)")]
        [ConsoleCommand("css_exitdry", "Exit Dryrun (back to match warmup)")]
        [ConsoleCommand("css_stopdry", "Exit Dryrun (back to match warmup)")]
        [ConsoleCommand("css_enddry", "Exit Dryrun (back to match warmup)")]
        public void OnExitDryCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_exitdry", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.alreadymatchmode"));
                return;
            }
            // Only a running dryrun can be exited: in practice this used to restart the game and
            // throw everyone out of practice with an "exited dryrun" message.
            if (!isDryRun)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.nodryrun"));
                return;
            }

            ExecExitDryCFG();
            // Exit dryrun to match warmup, NOT back into practice. Dryrun is usually a pre-match test,
            // so returning to practice was surprising; land in the neutral match-warmup hub (same as
            // .exitprac) and let the admin run .prac themselves if they want practice again.
            isDryRun = false;
            StartMatchMode();
            HandlePlayoutConfig();
            // ExecExitDryCFG's mp_restartgame lands a few frames later and can re-stamp the round
            // counters after StartMatchMode's wipe, leaving the dryrun icons on the warmup scoreboard.
            ScheduleRoundHistoryWipe();
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.exitdry"));
        }

        [ConsoleCommand("css_spawn", "Teleport to provided spawn")]
        [ConsoleCommand("css_sp", "Teleport to provided spawn")]
        public void OnSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid)
                return;

            if (player.TeamNum == (byte)CsTeam.Spectator)
                return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, player.TeamNum, "spawn");
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!spawn <round>"));
            }
        }

        [ConsoleCommand("css_ctspawn", "Teleport to provided CT spawn")]
        public void OnCtSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid)
                return;

            if (player.TeamNum == (byte)CsTeam.Spectator)
                return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!ctspawn <round>"));
            }
        }

        [ConsoleCommand("css_tspawn", "Teleport to provided T spawn")]
        public void OnTSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid)
                return;

            if (player.TeamNum == (byte)CsTeam.Spectator)
                return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.Terrorist, "tspawn");
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!tspawn <round>"));
            }
        }

        private const int MaxPracticeBots = 5;

        // Cached so the "bots are disabled" line is logged once per load, not on every .bot.
        private static bool? noBotsFlag;

        /// <summary>
        /// True when the server was started with -nobots, which makes every bot command pointless.
        /// </summary>
        /// <remarks>
        /// This used to read Environment.GetCommandLineArgs() directly, which inside the
        /// CounterStrikeSharp host does not reliably return the server's real argv - so -nobots went
        /// unnoticed and .bot ran anyway. With bots disabled the engine still hands out a controller
        /// and pawn for bot_add but never gives it a player model: the result is solid, damageable
        /// and completely invisible. HasLaunchOption (Util/CssApiCompat.cs) asks the engine's own
        /// ICommandLine through the fork's CommandLine helper, falling back to /proc/self/cmdline.
        /// </remarks>
        private static bool IsNoBotsFlagSet()
        {
            if (noBotsFlag == null)
            {
                noBotsFlag = HasLaunchOption("-nobots") || HasLaunchOption("+nobots") || HasLaunchOption("nobots");
            }

            return noBotsFlag.Value;
        }

        /// <summary>
        /// Kick every quota/practice/warmup bot by userid instead of running a bare bot_kick. The bare
        /// form (and the quota housekeeping of bot_quota_mode normal) can also take out the CSTV bot,
        /// which kills GOTV and any running demo recording. Kicking each bot by userid skips the HLTV
        /// client entirely. The quota is dropped to 0 first, else bot_quota_mode normal refills the bots.
        /// </summary>
        public void KickAllBotsProtectCSTV()
        {
            Server.ExecuteCommand("bot_quota 0");
            foreach (var p in Utilities.GetPlayers())
            {
                if (p?.IsValid != true || !p.IsBot || p.IsHLTV || !p.UserId.HasValue) continue;
                // kickid by userid: bot names can repeat, and a bare bot_kick also takes the CSTV bot.
                Server.ExecuteCommand($"kickid {p.UserId.Value}");
            }
        }

        private bool CanSpawnAnotherBot(CCSPlayerController? player)
        {
            // Count current bots
            int currentBotCount = Utilities.GetPlayers().Count(p => p?.IsValid == true && p.IsBot && !p.IsHLTV);
            if (currentBotCount >= MaxPracticeBots)
            {
                player?.PrintToChat($" {ChatColors.Green}[MatchZy] {ChatColors.White}{Localizer.ForPlayer(player, "matchzy.prac.maxbots", MaxPracticeBots)}");
                return false;
            }

            // Protect the CSTV bot: bot_add on a full server makes the engine free a slot, and the
            // slot it takes is the CSTV bot's - which kicks CSTV and kills GOTV/demo recording. Count
            // every occupied slot (humans, bots AND CSTV) and refuse the spawn when there is no free
            // slot left for the new bot. Only enforced while a CSTV client is actually connected:
            // with tv_enable 0 there is nothing to protect, and listen servers can report a bogus
            // Server.MaxPlayers (1), which made this guard refuse every bot spawn there.
            bool cstvConnected = Utilities.GetPlayers().Any(p => p?.IsValid == true && p.IsHLTV);
            int occupiedSlots = Utilities.GetPlayers().Count(p => p?.IsValid == true && p.Connected == PlayerConnectedState.Connected);
            if (cstvConnected && occupiedSlots + 1 > Server.MaxPlayers)
            {
                player?.PrintToChat($" {ChatColors.Green}[MatchZy] {ChatColors.White}{Localizer.ForPlayer(player, "matchzy.prac.serverfullbot", occupiedSlots, Server.MaxPlayers)}");
                Log($"[CanSpawnAnotherBot] Refused bot spawn: {occupiedSlots}/{Server.MaxPlayers} slots occupied, a bot_add would displace the CSTV bot.");
                return false;
            }
            return true;
        }

        [ConsoleCommand("css_bot", "Spawns a bot at the player's position")]
        public void OnBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                false
            );
        }

        [ConsoleCommand("css_tbot", "Spawns a T bot at the player's position")]
        public void OnTBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                false,
                CsTeam.Terrorist
            );
        }

        [ConsoleCommand("css_ctbot", "Spawns a CT bot at the player's position")]
        public void OnCtBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                false,
                CsTeam.CounterTerrorist
            );
        }

        [ConsoleCommand("css_cbot", "Spawns a crouched bot at the player's position")]
        [ConsoleCommand("css_crouchbot", "Spawns a crouched bot at the player's position")]
        [ConsoleCommand("css_duckbot")]
        public void OnCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            // crouched, auto team, boost the player onto the crouched bot (spawn above it).
            AddBot(
                player, /*crouch*/
                true,
                boost: true
            );
        }

        [ConsoleCommand("css_tcrouchbot", "Spawns a crouched T bot at the player's position")]
        public void OnTCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                true,
                CsTeam.Terrorist
            );
        }

        [ConsoleCommand("css_ctcrouchbot", "Spawns a crouched CT bot at the player's position")]
        public void OnCtCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(
                player, /*crouch*/
                true,
                CsTeam.CounterTerrorist
            );
        }

        [ConsoleCommand("css_boost", "Spawns a bot at the player's position and boost the player on it")]
        public void OnBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            // boost: lift handled inside SpawnBot (same tick the bot lands) with
            // collisions kept solid, so the player rests on the bot instead of
            // sinking through it.
            AddBot(player, /*crouch*/ false, boost: true);
        }

        [ConsoleCommand("css_cboost", "Spawns a crouched bot at the player's position and boost the player on it")]
        [ConsoleCommand("css_crouchboost", "Spawns a crouched bot at the player's position and boost the player on it")]
        [ConsoleCommand("css_duckboost")]
        public void OnCrouchBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[Info] Bots disabled due to -nobots flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            if (!CanSpawnAnotherBot(player))
                return;

            AddBot(player, true, boost: true);
        }

        private void AddBot(CCSPlayerController? player, bool crouch, CsTeam? forceTeam = null, bool boost = false, Position? posOverride = null)
        {
            try
            {
                if (!isPractice || player == null || !player.IsValid || player.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null)
                    return;

                // Safely check movement services
                if (player.PlayerPawn.Value.MovementServices != null)
                {
                    try
                    {
                        CCSPlayer_MovementServices movementService = new(player.PlayerPawn.Value.MovementServices.Handle);
                        if ((int)movementService.DuckAmount == 1)
                        {
                            // Player was crouching while using .bot command
                            crouch = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[AddBot] Could not check crouch state: {ex.Message}");
                    }
                }

                isSpawningBot = true;

                // Determine target team
                CsTeam targetTeam =
                    forceTeam
                    ?? (CsTeam)player.TeamNum switch
                    {
                        CsTeam.CounterTerrorist => CsTeam.Terrorist,
                        CsTeam.Terrorist => CsTeam.CounterTerrorist,
                        _ => CsTeam.Terrorist,
                    };

                // Pre-pin the quota to the count INCLUDING the bot about to be added, so the
                // engine's quota think has no headroom to add a balance partner alongside
                // bot_add_t/_ct (the pair-spawn: one .bot -> a bot on each team). SpawnBot
                // re-pins to the tracked count after the claim, and its claim pass still
                // kicks a pair extra if the engine adds one anyway.
                int quotaBotCount;
                lock (_botsDictLock)
                    quotaBotCount = pracUsedBots.Count;
                Server.ExecuteCommand($"bot_quota {quotaBotCount + 1}");

                // Add bot to opposite team or forced team. Use ONLY bot_add_t / bot_add_ct - it already
                // routes the bot to that team. A preceding bot_join_team ALSO spawned a bot, so the two
                // together produced two bots per .bot (confirmed via diag: one .bot -> Crew + Shamat).
                if (targetTeam == CsTeam.Terrorist)
                    Server.ExecuteCommand("bot_add_t");
                else
                    Server.ExecuteCommand("bot_add_ct");

                // Once bot is added, we teleport it to the requested position
                var targetPlayer = player; // Capture for timer safety
                AddTimer(
                    0.1f,
                    () =>
                    {
                        if (targetPlayer != null && targetPlayer.IsValid && targetPlayer.Connected == PlayerConnectedState.Connected)
                        {
                            SpawnBot(targetPlayer, crouch, boost, targetTeam, posOverride);
                        }
                        else
                        {
                            isSpawningBot = false;
                        }
                    }
                );

                Server.ExecuteCommand("bot_stop 1");
                Server.ExecuteCommand("bot_freeze 1");
                Server.ExecuteCommand("bot_zombie 1");
            }
            catch (Exception ex)
            {
                Log($"[AddBot - ERROR] {ex.GetType().Name}: {ex.Message}");
                isSpawningBot = false;
            }
        }

        private CCSPlayerController? GetClosestBotOfPlayer(CCSPlayerController player)
        {
            if (!IsPlayerValid(player) || !player.UserId.HasValue)
                return null;

            CCSPlayerController? closestBot = null;
            float closestDistance = float.MaxValue;
            List<int> invalidBotIds = new();

            lock (_botsDictLock)
            {
                // Create snapshot to avoid modification during iteration
                var botSnapshot = pracUsedBots.ToList();

                foreach (var kvp in botSnapshot)
                {
                    int userId = kvp.Key;
                    var botDict = kvp.Value;

                    try
                    {
                        // Validate dictionary entries exist
                        if (!botDict.ContainsKey("owner") || !botDict.ContainsKey("controller"))
                        {
                            invalidBotIds.Add(userId);
                            continue;
                        }

                        // Safe casting with validation
                        if (botDict["owner"] is not CCSPlayerController botOwner || botDict["controller"] is not CCSPlayerController bot)
                        {
                            invalidBotIds.Add(userId);
                            continue;
                        }

                        // Critical: Validate both controllers are still valid
                        if (!IsPlayerValid(bot) || !IsPlayerValid(botOwner) || bot.Connected != PlayerConnectedState.Connected || botOwner.Connected != PlayerConnectedState.Connected)
                        {
                            invalidBotIds.Add(userId);
                            continue;
                        }

                        // Check ownership matches
                        if (!botOwner.UserId.HasValue || botOwner.UserId.Value != player.UserId.Value)
                            continue;

                        // Safe distance calculation with null checks
                        float distance = CalculateSafeDistance(botOwner, bot);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestBot = bot;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[GetClosestBot] Exception for bot {userId}: {ex.Message}");
                        invalidBotIds.Add(userId);
                    }
                }

                // Clean up invalid entries after iteration
                foreach (var invalidId in invalidBotIds)
                {
                    pracUsedBots.Remove(invalidId);
                    _botsBeingProcessed.Remove(invalidId);
                }
            }

            return closestBot;
        }

        private float CalculateSafeDistance(CCSPlayerController player1, CCSPlayerController player2)
        {
            try
            {
                // Validate player pawns exist and are valid
                if (player1?.PlayerPawn?.IsValid != true || player1.PlayerPawn.Value == null || player2?.PlayerPawn?.IsValid != true || player2.PlayerPawn.Value == null)
                {
                    return float.MaxValue;
                }

                // Validate body components and scene nodes
                var p1Origin = player1.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;
                var p2Origin = player2.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;

                if (p1Origin == null || p2Origin == null)
                {
                    return float.MaxValue;
                }

                // Calculate Manhattan distance (more performant than Euclidean)
                return MathF.Abs(p1Origin.X - p2Origin.X) + MathF.Abs(p1Origin.Y - p2Origin.Y) + MathF.Abs(p1Origin.Z - p2Origin.Z);
            }
            catch (Exception ex)
            {
                Log($"[CalculateSafeDistance] Error: {ex.Message}");
                return float.MaxValue;
            }
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player))
                return HookResult.Continue;

            // Reset noflash on spawn
            if (player != null && player.UserId.HasValue && noFlashList.Contains(player.UserId.Value))
            {
                noFlashList.Remove(player.UserId.Value);
            }

            // disable noclip on spawn -- all no clipping functionality is handled by the plugin!
            // Movement adjustments are consistent with cs2-noclip.
            CBasePlayerPawn pawn = player!.PlayerPawn.Value!;
            pawn.ResetNoclipToWalk();

            if (matchStarted && (matchzyTeam1.coach.Contains(player!) || matchzyTeam2.coach.Contains(player!)))
            {
                player!.InGameMoneyServices!.Account = 0;

                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;

                return HookResult.Continue;
            }

            // Respawing a bot where it was actually spawned during practice session
            if (isPractice && player!.IsValid && player.IsBot && player.UserId.HasValue)
            {
                lock (_botsDictLock)
                {
                    if (pracUsedBots.TryGetValue(player.UserId.Value, out var botData))
                    {
                        if (botData["position"] is Position botPosition)
                        {
                            player.PlayerPawn.Value?.Teleport(botPosition.PlayerPosition, botPosition.PlayerAngle, new Vector(0, 0, 0));
                            bool isCrouched = (bool)botData["crouchstate"];
                            if (isCrouched)
                            {
                                player.PlayerPawn.Value!.Flags |= (uint)PlayerFlags.FL_DUCKING;
                                var crouchBot = player;
                                AddTimer(0.1f, () => SetBotDuckAmountFull(crouchBot));
                                AddTimer(0.2f, () => SetBotCrouching(crouchBot));
                            }
                            CCSPlayerController? botOwner = (CCSPlayerController)botData["owner"];

                            // PATCHED: Added validation before scheduling collision timer
                            if (botOwner != null && botOwner.IsValid && botOwner.PlayerPawn != null && botOwner.PlayerPawn.IsValid && player.IsValid && player.PlayerPawn != null && player.PlayerPawn.IsValid)
                            {
                                // PATCHED: Capture player reference in lambda to ensure validation
                                var botPlayer = player;
                                AddTimer(
                                    0.2f,
                                    () =>
                                    {
                                        // PATCHED: Validate both players still exist before disabling collisions
                                        if (IsPlayerValid(botOwner) && IsPlayerValid(botPlayer) && botOwner.PlayerPawn != null && botOwner.PlayerPawn.IsValid && botPlayer.PlayerPawn != null && botPlayer.PlayerPawn.IsValid)
                                        {
                                            TemporarilyDisableCollisions(botOwner, botPlayer);
                                        }
                                    }
                                );
                            }
                        }
                    }
                    else if (!isSpawningBot && !player.IsHLTV)
                    {
                        // Bot has been spawned, but we didn't spawn it, so kick it.
                        // This most often happens when a player changes team with bot_quota_mode set to fill
                        // Extra bots from bot_add are already handled in SpawnBot
                        // Delay this for a few seconds to prevent crashes
                        // Kick by userid, not name: the name may be reused by a bot we spawn in the
                        // meantime (and bot_kick with an unquoted name can match the wrong one).
                        string botName = player.PlayerName;
                        int strayUserId = player.UserId.Value;
                        var strayBot = player;
                        Log($"Kicking bot {botName} due to erroneous spawning");
                        AddTimer(
                            2.5f,
                            () =>
                            {
                                if (strayBot == null || !strayBot.IsValid || !strayBot.IsBot || strayBot.IsHLTV
                                    || strayBot.UserId != strayUserId)
                                    return;
                                lock (_botsDictLock)
                                {
                                    // Claimed as a practice bot since: keep it.
                                    if (pracUsedBots.ContainsKey(strayUserId))
                                        return;
                                }
                                Server.ExecuteCommand($"kickid {strayUserId}");
                            }
                        );
                    }
                }

                return HookResult.Continue;
            }

            return HookResult.Continue;
        }

        // Timer-deferred crouch writes for practice bots. The bot may have been kicked or its pawn
        // replaced by the time these fire, so re-validate the controller + pawn and re-fetch
        // MovementServices from the live pawn (a MovementServices captured before the timer can
        // point at freed memory).
        private void SetBotDuckAmountFull(CCSPlayerController? bot)
        {
            if (!IsPlayerValid(bot))
                return;
            var pawn = bot!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.MovementServices == null || pawn.MovementServices.Handle == IntPtr.Zero)
                return;
            try
            {
                new CCSPlayer_MovementServices(pawn.MovementServices.Handle).DuckAmount = 1;
            }
            catch (Exception ex)
            {
                Log($"[SetBotDuckAmountFull] {ex.Message}");
            }
        }

        private void SetBotCrouching(CCSPlayerController? bot)
        {
            if (!IsPlayerValid(bot))
                return;
            var pawn = bot!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.Bot == null)
                return;
            try
            {
                pawn.Bot.IsCrouching = true;
            }
            catch (Exception ex)
            {
                Log($"[SetBotCrouching] {ex.Message}");
            }
        }

        // ============================================================================
        // SEPARATE Coach Handling - Cleaner and safer
        // ============================================================================
        private void HandleCoachSpawn(CCSPlayerController player)
        {
            try
            {
                if (player.InGameMoneyServices != null)
                {
                    player.InGameMoneyServices.Account = 0;
                    Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
                }

                if (player.PlayerPawn?.IsValid == true && player.PlayerPawn.Value != null)
                {
                    player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
                    player.PlayerPawn.Value.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                }
            }
            catch (Exception ex)
            {
                Log($"[HandleCoachSpawn] Error: {ex.Message}");
            }
        }

        // ============================================================================
        // IMPROVED Bot Respawn - Critical crash prevention
        // ============================================================================
        private void HandleBotRespawn(CCSPlayerController player)
        {
            if (!player.UserId.HasValue)
                return;

            int userId = player.UserId.Value;

            lock (_botsDictLock)
            {
                // Check if this bot is being processed
                if (_botsBeingProcessed.Contains(userId))
                {
                    Log($"[HandleBotRespawn] Bot {userId} already being processed, skipping");
                    return;
                }

                // Bot exists in our tracking dictionary
                if (pracUsedBots.ContainsKey(userId))
                {
                    _botsBeingProcessed.Add(userId);
                    try
                    {
                        RespawnTrackedBot(player, userId);
                    }
                    finally
                    {
                        _botsBeingProcessed.Remove(userId);
                    }
                }
                // Bot spawned but we didn't spawn it - kick after delay
                else if (!isSpawningBot && player.IsHLTV)
                {
                    ScheduleSafeBotKick();
                }
            }
        }

        // ============================================================================
        // SAFE Tracked Bot Respawn
        // ============================================================================
        private void RespawnTrackedBot(CCSPlayerController player, int userId)
        {
            try
            {
                // Validate bot data exists
                if (!pracUsedBots.TryGetValue(userId, out var botData) || !botData.ContainsKey("position"))
                {
                    return;
                }

                // Validate player pawn
                if (player.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null)
                {
                    return;
                }

                // Get position safely
                if (botData["position"] is not Position botPosition)
                {
                    return;
                }

                // Teleport bot to stored position
                player.PlayerPawn.Value.Teleport(botPosition.PlayerPosition, botPosition.PlayerAngle, new Vector(0, 0, 0));

                // Handle crouch state safely
                if (botData.ContainsKey("crouchstate") && botData["crouchstate"] is bool isCrouched && isCrouched)
                {
                    ApplyCrouchState(player);
                }

                // Handle collision disabling with owner
                if (botData.ContainsKey("owner") && botData["owner"] is CCSPlayerController botOwner)
                {
                    if (IsPlayerValid(botOwner) && botOwner.PlayerPawn?.IsValid == true)
                    {
                        AddTimer(0.2f, () => SafelyDisableCollisions(botOwner, player));
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[RespawnTrackedBot] Error for bot {userId}: {ex.Message}");
                // Clean up on error
                lock (_botsDictLock)
                {
                    pracUsedBots.Remove(userId);
                }
            }
        }

        // ============================================================================
        // SAFE Crouch Application
        // ============================================================================
        private void ApplyCrouchState(CCSPlayerController player)
        {
            try
            {
                if (player.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null)
                    return;

                player.PlayerPawn.Value.Flags |= (uint)PlayerFlags.FL_DUCKING;

                if (player.PlayerPawn.Value.MovementServices != null)
                {
                    var capturedPlayer = player;
                    AddTimer(0.1f, () => SetBotDuckAmountFull(capturedPlayer));
                    AddTimer(0.2f, () => SetBotCrouching(capturedPlayer));
                }
            }
            catch (Exception ex)
            {
                Log($"[ApplyCrouchState] Error: {ex.Message}");
            }
        }

        // ============================================================================
        // SAFE Collision Disabling - Wrapped in validation
        // ============================================================================
        private void SafelyDisableCollisions(CCSPlayerController p1, CCSPlayerController p2)
        {
            // Skip if either player is invalid
            if (!IsPlayerValid(p1) || !IsPlayerValid(p2) || p1.Connected != PlayerConnectedState.Connected || p2.Connected != PlayerConnectedState.Connected)
            {
                return;
            }

            // Additional validation before calling original method
            if (p1.PlayerPawn?.IsValid == true && p1.PlayerPawn.Value != null && p2.PlayerPawn?.IsValid == true && p2.PlayerPawn.Value != null)
            {
                try
                {
                    TemporarilyDisableCollisions(p1, p2);
                }
                catch (Exception ex)
                {
                    Log($"[SafelyDisableCollisions] Error: {ex.Message}");
                }
            }
        }

        // ============================================================================
        // SAFE Bot Kick Scheduling
        // ============================================================================
        private void ScheduleSafeBotKick()
        {
            AddTimer(
                2.5f,
                () =>
                {
                    try
                    {
                        // Double-check we're still in practice mode
                        if (!isPractice)
                            return;

                        KickAllBotsProtectCSTV();
                    }
                    catch (Exception ex)
                    {
                        Log($"[ScheduleSafeBotKick] Error: {ex.Message}");
                    }
                }
            );
        }

        private float AbsolutDistance(CCSPlayerController player, CCSPlayerController bot)
        {
            try
            {
                if (player?.PlayerPawn?.IsValid != true || player.PlayerPawn.Value == null || bot?.PlayerPawn?.IsValid != true || bot.PlayerPawn.Value == null)
                    return float.MaxValue;

                var playerOrigin = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;
                var botOrigin = bot.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;

                if (playerOrigin == null || botOrigin == null)
                    return float.MaxValue;

                return MathF.Abs(playerOrigin.X - botOrigin.X) + MathF.Abs(playerOrigin.Y - botOrigin.Y) + MathF.Abs(playerOrigin.Z - botOrigin.Z);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private void SpawnBot(CCSPlayerController botOwner, bool crouch, bool boost = false, CsTeam targetTeam = CsTeam.None, Position? posOverride = null, int attempt = 0)
        {
            try
            {
                if (!IsPlayerValid(botOwner))
                {
                    isSpawningBot = false;
                    return;
                }

                // ADDED: Validate botOwner has valid pawn and components
                if (botOwner.PlayerPawn == null || !botOwner.PlayerPawn.IsValid || botOwner.PlayerPawn.Value == null || botOwner.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin == null || botOwner.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsRotation == null)
                {
                    Log($"[SpawnBot] Bot owner has invalid pawn or components");
                    isSpawningBot = false;
                    return;
                }

                var botOwnerPawn = botOwner.PlayerPawn.Value;

                // Capture the spawn spot on the first attempt so retries land the bot where
                // the owner stood when the command was issued, not wherever they walked
                // meanwhile. A posOverride (named bot position via .loadbotpos) still wins.
                posOverride ??= new Position(botOwnerPawn.CBodyComponent!.SceneNode!.AbsOrigin, botOwnerPawn.CBodyComponent!.SceneNode!.AbsRotation);

                // Gather ALL unused bots first and pick the claim afterwards. bot_add_t/_ct
                // pair-spawns on current CS2 builds (one bot per team) and the enumeration
                // order is arbitrary: kicking/claiming while enumerating could kick the bot
                // we actually want or claim the wrong-team pair extra.
                List<CCSPlayerController> unusedBots = new();
                foreach (var tempPlayer in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
                {
                    if (!IsPlayerValid(tempPlayer))
                        continue;
                    if (!tempPlayer.IsBot || tempPlayer.IsHLTV)
                        continue;
                    if (!tempPlayer.UserId.HasValue)
                        continue;
                    bool isAlreadyUsed;
                    lock (_botsDictLock)
                    {
                        isAlreadyUsed = pracUsedBots.ContainsKey(tempPlayer.UserId.Value);
                    }
                    if (isAlreadyUsed)
                        continue;

                    // ADDED: Validate tempPlayer has valid pawn before proceeding
                    if (tempPlayer.PlayerPawn == null || !tempPlayer.PlayerPawn.IsValid || tempPlayer.PlayerPawn.Value == null)
                    {
                        Log($"[SpawnBot] Bot {tempPlayer.PlayerName} has invalid pawn, skipping");
                        continue;
                    }
                    unusedBots.Add(tempPlayer);
                }

                // Prefer the requested team. Unassigned (team 0, still joining) is claimable
                // as a fallback - it is the bot bot_add_t/_ct itself requested.
                CCSPlayerController? claimedBot = targetTeam == CsTeam.None
                    ? unusedBots.FirstOrDefault()
                    : unusedBots.FirstOrDefault(b => b.TeamNum == (byte)targetTeam)
                      ?? unusedBots.FirstOrDefault(b => b.TeamNum == (byte)CsTeam.None);

                // The right-team bot of a pair often arrives a tick or two AFTER bot_add, so
                // a wrong-team-only enumeration here does not mean failure yet. Retry before
                // giving up, and kick nothing meanwhile (the extras are dealt with once the
                // outcome is known). isSpawningBot stays true across retries so the
                // erroneous-spawn kicker and the late sweep leave the incoming bot alone.
                if (claimedBot == null && attempt < 4)
                {
                    var retryOwner = botOwner;
                    var retryPos = posOverride;
                    AddTimer(0.15f, () =>
                    {
                        if (IsPlayerValid(retryOwner) && retryOwner.Connected == PlayerConnectedState.Connected)
                        {
                            SpawnBot(retryOwner, crouch, boost, targetTeam, retryPos, attempt + 1);
                        }
                        else
                        {
                            isSpawningBot = false;
                        }
                    });
                    return;
                }

                // Kick every unused bot we are not claiming (pair extras / wrong-team spawns).
                // bot_quota is pinned below so they will not refill.
                foreach (var extra in unusedBots)
                {
                    if (extra == claimedBot)
                        continue;
                    Log($"[SpawnBot] kicking extra pair bot {extra.PlayerName} (team {extra.TeamNum}, wanted {(byte)targetTeam})");
                    Server.ExecuteCommand($"kickid {extra.UserId!.Value}");
                }

                if (claimedBot != null)
                {
                    Position botOwnerPosition = posOverride;

                    // Now safely add to dictionary with lock
                    lock (_botsDictLock)
                    {
                        pracUsedBots[claimedBot.UserId!.Value] = new Dictionary<string, object>();
                        pracUsedBots[claimedBot.UserId.Value]["controller"] = claimedBot;
                        pracUsedBots[claimedBot.UserId.Value]["position"] = botOwnerPosition;
                        pracUsedBots[claimedBot.UserId.Value]["owner"] = botOwner;
                        pracUsedBots[claimedBot.UserId.Value]["crouchstate"] = crouch;
                    }

                    if (crouch)
                    {
                        // ADDED: Validate MovementServices before accessing
                        if (claimedBot.PlayerPawn!.Value!.MovementServices != null)
                        {
                            var crouchBot = claimedBot;
                            AddTimer(0.1f, () => SetBotDuckAmountFull(crouchBot));
                            AddTimer(0.2f, () => SetBotCrouching(crouchBot));
                        }
                    }

                    // Now safe - we validated PlayerPawn.Value above.
                    // Route every bot spawn through TeleportUpright: full-angle teleport (bot
                    // inherits facing) then flatten the body scene node over several frames so a
                    // look-down pitch never tilts the model flat / clips it under the map. Body
                    // stands upright at the owner's (or the saved spot's) position.
                    TeleportUpright(claimedBot, botOwnerPosition.PlayerPosition, botOwnerPosition.PlayerAngle);

                    if (boost)
                    {
                        // Boost: keep BOTH solid (no DEBRIS) and lift the owner
                        // onto the bot's crown in the same tick the bot lands, so
                        // the player rests on top instead of clipping through.
                        // Disabling collisions here is what made the player sink
                        // back through the bot (gravity re-overlaps the bot before
                        // the 0.5s re-solidify timer fires).
                        float ownerYaw = botOwnerPawn.EyeAngles.Y; // yaw only - keep body flat (issue #10)
                        botOwnerPawn.Teleport(
                            new Vector(botOwnerPosition.PlayerPosition.X, botOwnerPosition.PlayerPosition.Y, botOwnerPosition.PlayerPosition.Z + 80.0f),
                            new QAngle(0, ownerYaw, 0),
                            new Vector(0, 0, 0)
                        );
                    }
                    else
                    {
                        // Your existing collision validation (already good!)
                        if (IsPlayerValid(botOwner) && IsPlayerValid(claimedBot) && botOwner.PlayerPawn != null && botOwner.PlayerPawn.IsValid && claimedBot.PlayerPawn != null && claimedBot.PlayerPawn.IsValid)
                        {
                            TemporarilyDisableCollisions(botOwner, claimedBot);
                        }
                    }
                }
                bool unusedBotFound = claimedBot != null;

                // Lock bot_quota to exactly the number of tracked practice bots. With bot_quota_mode
                // normal, kicking an extra bot (from bot_add spawning two, or a quota fill) triggers an
                // immediate REFILL - so the extra keeps coming back. Pinning the quota to our tracked
                // count stops the refill loop, so .bot leaves exactly one new bot.
                int trackedBotCount;
                lock (_botsDictLock)
                    trackedBotCount = pracUsedBots.Count;
                Server.ExecuteCommand($"bot_quota_mode normal; bot_quota {trackedBotCount}");

                if (!unusedBotFound)
                {
                    PrintLocalizedToAll("matchzy.pm.botlimit");
                }

                isSpawningBot = false;

                // Late-pair sweep: on current CS2 builds one bot_add_t/_ct can spawn a PAIR (one per
                // team, balance behavior), and the second bot often arrives a tick AFTER the dedupe
                // enumeration above - so it was never seen, the erroneous-spawn kicker skipped it
                // (isSpawningBot was still true), and ".bot added a bot to each team". Sweep again
                // shortly after and kick anything untracked, regardless of arrival timing.
                AddTimer(0.6f, KickUntrackedPracticeBots);
            }
            catch (JsonException ex)
            {
                Log($"[SpawnBot - FATAL] Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"[SpawnBot - FATAL] Unexpected error: {ex.Message}");
                isSpawningBot = false;
            }
        }

        // Userids we've already issued a kick for in the late sweep. kickid is async - the bot lingers
        // a tick or two, so a second sweep (another .bot fired within ~0.6s) would re-see and re-log
        // the same leftover. Tracking the id here suppresses the duplicate log; the set self-prunes to
        // still-present bots each sweep, so an id is dropped once its kick actually lands.
        private readonly HashSet<int> _kickedUntrackedBotIds = new();

        // Kick every practice bot that is neither a tracked .bot nor a bot-replay puppet, then re-pin
        // the quota. Idempotent; safe to run any time in practice.
        private void KickUntrackedPracticeBots()
        {
            try
            {
                if (!isPractice || isSpawningBot)
                    return;
                var presentBotIds = new HashSet<int>();
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || !p.IsBot || p.IsHLTV || !p.UserId.HasValue)
                        continue;
                    int uid = p.UserId.Value;
                    presentBotIds.Add(uid);
                    bool tracked;
                    lock (_botsDictLock)
                        tracked = pracUsedBots.ContainsKey(uid);
                    if (!tracked)
                    {
                        // Only log the first time we kick this leftover; Add returns false if a prior
                        // sweep already flagged it (kick still pending) -> no duplicate log spam.
                        if (_kickedUntrackedBotIds.Add(uid))
                            Log($"[SpawnBot] kicking late untracked bot {p.PlayerName} (pair-spawn leftover)");
                        Server.ExecuteCommand($"kickid {uid}");
                    }
                }
                // Drop ids whose bot is gone (kick landed), so a later recycled userid logs fresh.
                _kickedUntrackedBotIds.IntersectWith(presentBotIds);
                int trackedCount;
                lock (_botsDictLock)
                    trackedCount = pracUsedBots.Count;
                Server.ExecuteCommand($"bot_quota {trackedCount}");
            }
            catch (Exception e)
            {
                Log($"[SpawnBot] late sweep: {e.Message}");
            }
        }

        public void TemporarilyDisableCollisions(CCSPlayerController p1, CCSPlayerController p2)
        {
            // PATCHED: Additional validation at method entry
            if (!IsPlayerValid(p1) || !IsPlayerValid(p2))
            {
                Log($"[CollisionFix] Invalid player controller(s) - skipping collision disable");
                return;
            }

            if (p1.PlayerPawn == null || !p1.PlayerPawn.IsValid || p1.PlayerPawn.Value == null || p2.PlayerPawn == null || !p2.PlayerPawn.IsValid || p2.PlayerPawn.Value == null)
            {
                Log($"[CollisionFix] Invalid player pawn(s) - skipping collision disable");
                return;
            }

            // PATCHED: Create unique key for this player pair
            string timerKey = $"{p1.UserId}_{p2.UserId}";

            // PATCHED: Kill existing timer for this specific pair
            if (collisionGroupTimers.ContainsKey(timerKey))
            {
                collisionGroupTimers[timerKey]?.Kill();
                collisionGroupTimers.Remove(timerKey);
            }

            // Reference collision code: https://github.com/Source2ZE/CS2Fixes/blob/f009e399ff23a81915e5a2b2afda20da2ba93ada/src/events.cpp#L150
            p1.PlayerPawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p1.PlayerPawn.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2.PlayerPawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2.PlayerPawn.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;

            // TODO: call CollisionRulesChanged
            var p1p = p1.PlayerPawn;
            var p2p = p2.PlayerPawn;

            // PATCHED: Store timer in dictionary with unique key
            var timer = AddTimer(
                0.5f,
                () =>
                {
                    try
                    {
                        if (p1p == null || !p1p.IsValid || p1p.Value == null || !p1p.Value.IsValid || p2p == null || !p2p.IsValid || p2p.Value == null || !p2p.Value.IsValid)
                        {
                            Log($"[CollisionFix] Player handle invalid - cleaning up timer for {timerKey}");
                            CleanupCollisionTimer(timerKey);
                            return;
                        }

                        // PATCHED: Additional null check for collision components
                        if (p1p.Value.Collision == null || p2p.Value.Collision == null)
                        {
                            Log($"[CollisionFix] Collision component null - cleaning up timer for {timerKey}");
                            CleanupCollisionTimer(timerKey);
                            return;
                        }

                        if (!DoPlayersCollide(p1p.Value, p2p.Value))
                        {
                            // Once they no longer collide
                            p1p.Value.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            p1p.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            p2p.Value.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            p2p.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                            // TODO: call CollisionRulesChanged
                            CleanupCollisionTimer(timerKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[CollisionFix] Exception in collision timer callback: {ex.Message}");
                        CleanupCollisionTimer(timerKey);
                    }
                },
                TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE
            );

            collisionGroupTimers[timerKey] = timer;
        }

        private void CleanupCollisionTimer(string timerKey)
        {
            if (collisionGroupTimers.ContainsKey(timerKey))
            {
                collisionGroupTimers[timerKey]?.Kill();
                collisionGroupTimers.Remove(timerKey);
            }
        }

        public bool DoPlayersCollide(CCSPlayerPawn p1, CCSPlayerPawn p2)
        {
            Vector p1min,
                p1max,
                p2min,
                p2max;
            var p1pos = p1.AbsOrigin;
            var p2pos = p2.AbsOrigin;
            p1min = p1.Collision.Mins + p1pos!;
            p1max = p1.Collision.Maxs + p1pos!;
            p2min = p2.Collision.Mins + p2pos!;
            p2max = p2.Collision.Maxs + p2pos!;

            return p1min.X <= p2max.X && p1max.X >= p2min.X && p1min.Y <= p2max.Y && p1max.Y >= p2min.Y && p1min.Z <= p2max.Z && p1max.Z >= p2min.Z;
        }

        public void CleanupAllCollisionTimers()
        {
            foreach (var timer in collisionGroupTimers.Values)
            {
                timer?.Kill();
            }
            collisionGroupTimers.Clear();
        }

        [ConsoleCommand("css_rs", "Removes bots from the practice session")]
        public void OnRestartRoundCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            // Restarts the game for everyone on the server: admins only.
            if (!IsPlayerAdmin(player, "css_rs", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            Server.ExecuteCommand("mp_restartgame 1");
        }

        [ConsoleCommand("css_nb", "")]
        [ConsoleCommand("css_nobot", "Removes the closest bot from the practice session")]
        public void OnNoBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !IsPlayerValid(player))
                return;

            if (IsNoBotsFlagSet())
            {
                Server.PrintToConsole("[MatchZy] Bots are disabled due to '-nobots' flag.");
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            var closestBot = GetClosestBotOfPlayer(player);
            if (closestBot == null || !IsPlayerValid(closestBot) || !closestBot.UserId.HasValue)
                return;

            int botUserId = closestBot.UserId.Value;

            // Mark as being processed
            lock (_botsDictLock)
            {
                if (_botsBeingProcessed.Contains(botUserId))
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.botbeingremoved"));
                    return;
                }
                _botsBeingProcessed.Add(botUserId);
            }

            // Use NextFrame for safe execution
            Server.NextFrame(() =>
            {
                try
                {
                    // Re-validate before kicking
                    if (closestBot.IsValid && closestBot.PlayerPawn?.IsValid == true && closestBot.Connected == PlayerConnectedState.Connected)
                    {
                        // kickid by the captured userid, not bot_kick <name> (names can repeat).
                        Server.ExecuteCommand($"kickid {botUserId}");

                        // Clean up tracking after small delay
                        AddTimer(
                            0.1f,
                            () =>
                            {
                                lock (_botsDictLock)
                                {
                                    pracUsedBots.Remove(botUserId);
                                    _botsBeingProcessed.Remove(botUserId);
                                }
                            }
                        );
                    }
                    else
                    {
                        // Bot already invalid, just clean up
                        lock (_botsDictLock)
                        {
                            pracUsedBots.Remove(botUserId);
                            _botsBeingProcessed.Remove(botUserId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[OnNoBotCommand] Error kicking bot: {ex.Message}");
                    lock (_botsDictLock)
                    {
                        _botsBeingProcessed.Remove(botUserId);
                    }
                }
            });
        }

        public void CleanupBotTracking()
        {
            lock (_botsDictLock)
            {
                pracUsedBots.Clear();
                _botsBeingProcessed.Clear();
            }

            // Kill all collision timers
            if (collisionGroupTimers != null)
            {
                foreach (var timer in collisionGroupTimers.Values)
                {
                    timer?.Kill();
                }
                collisionGroupTimers.Clear();
            }
        }

        [ConsoleCommand("css_nbs", "Removes bots from the practice session")]
        [ConsoleCommand("css_nbots", "Removes bots from the practice session")]
        [ConsoleCommand("css_kickbots", "Removes bots from the practice session")]
        [ConsoleCommand("css_kbots", "Removes bots from the practice session")]
        [ConsoleCommand("css_clearbots", "")]
        [ConsoleCommand("css_removebots", "")]
        [ConsoleCommand("css_nobots", "Removes bots from the practice session")]
        public void OnNoBotsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            // Drop the quota to 0 BEFORE kicking, else bot_quota_mode normal refills the kicked bots.
            KickAllBotsProtectCSTV();
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
            CleanupAllCollisionTimers();
        }

        // Active .ff window: its restore timer and the movetypes snapshotted when it began. A second
        // .ff inside the window is ignored, else it would snapshot MOVETYPE_NONE as the "pre" state
        // and leave everyone frozen when the window ends.
        CounterStrikeSharp.API.Modules.Timers.Timer? fastForwardTimer = null;
        Dictionary<int, MoveType_t>? preFastForwardMoveTypes = null;

        [ConsoleCommand("css_ff", "Fast forwards the timescale to 20 seconds")]
        [ConsoleCommand("css_fastforward", "Fast forwards the timescale to 20 seconds")]
        public void OnFFCommand(CCSPlayerController? player, CommandInfo? command)
        {
            // Outside practice, .ff is the friendly fire toggle for the coming match (.friendlyfire).
            if (!isPractice)
            {
                OnFriendlyFireCommand(player, command);
                return;
            }
            if (player == null)
                return;

            if (preFastForwardMoveTypes != null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.fastforwardrunning"));
                return;
            }

            Dictionary<int, MoveType_t> moveTypes = new();

            foreach (var key in playerData.Keys)
            {
                if (!IsPlayerValid(playerData[key]))
                    continue;
                moveTypes[key] = playerData[key].PlayerPawn.Value!.MoveType;

                playerData[key].PlayerPawn.Value!.MoveType = MoveType_t.MOVETYPE_NONE;
                Schema.SetSchemaValue(playerData[key].PlayerPawn.Value!.Handle, "CBaseEntity", "m_nActualMoveType", 0);
                Utilities.SetStateChanged(playerData[key].PlayerPawn.Value!, "CBaseEntity", "m_MoveType");
            }
            preFastForwardMoveTypes = moveTypes;

            PrintLocalizedToAll("matchzy.prac.fastforwarding");
            Server.ExecuteCommand("host_timescale 5");
            fastForwardTimer?.Kill();
            fastForwardTimer = AddTimer(10.0f, ResetFastForward);
        }

        public void ResetFastForward()
        {
            // Always drop the timescale first, even if practice ended during the window, so the
            // server never stays at 5x.
            Server.ExecuteCommand("host_timescale 1");
            fastForwardTimer = null;
            var moveTypes = preFastForwardMoveTypes;
            preFastForwardMoveTypes = null;
            if (moveTypes == null)
                return;

            foreach (var key in playerData.Keys)
            {
                var p = playerData[key];
                if (!IsPlayerValid(p))
                    continue;
                var pawn = p.PlayerPawn.Value!;
                MoveType_t restore;
                if (!isPractice || !moveTypes.TryGetValue(key, out restore))
                {
                    // Practice ended mid-window, or the player joined during it (no snapshot): only
                    // unfreeze pawns still held by the fast forward.
                    if (pawn.MoveType != MoveType_t.MOVETYPE_NONE)
                        continue;
                    restore = MoveType_t.MOVETYPE_WALK;
                }
                pawn.MoveType = restore;
                Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", (int)restore);
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }
        }

        [ConsoleCommand("css_clear", "Removes all the available granades")]
        public void OnClearAllCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RemoveGrenadeEntities();
        }

        [ConsoleCommand("css_cleanup", "Clears all utility currently on the map")]
        public void OnCleanupCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            RemoveGrenadeEntities();
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.utilitycleared"));
        }

        [ConsoleCommand("css_autoclear", "Toggle auto-clearing older utility when a new grenade detonates")]
        public void OnAutoClearCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            autoClearUtility = !autoClearUtility;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, autoClearUtility ? "matchzy.pm.autoclearon" : "matchzy.pm.autoclearoff"));
        }

        [ConsoleCommand("css_landmarker", "Toggle a beam marker at each grenade's detonation point")]
        [ConsoleCommand("css_lm", "Toggle a beam marker at each grenade's detonation point")]
        public void OnLandMarkerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            showLandingMarkers = !showLandingMarkers;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, showLandingMarkers ? "matchzy.pm.landmarkeron" : "matchzy.pm.landmarkeroff"));
        }

        [ConsoleCommand("css_arc", "Toggle drawing the trajectory arc of thrown grenades")]
        [ConsoleCommand("css_traceline", "Toggle drawing the trajectory arc of thrown grenades")]
        public void OnArcCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            traceNadeArcs = !traceNadeArcs;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, traceNadeArcs ? "matchzy.pm.arcon" : "matchzy.pm.arcoff"));
        }

        // Register a freshly-thrown projectile for arc tracing (called from OnEntitySpawnedHandler
        // when .arc is on). projectile.Index keys the sampled points collected each tick.
        public void RegisterArcTrace(uint projectileIndex)
        {
            if (!traceNadeArcs)
                return;
            tracedArcs[projectileIndex] = new NadeArcTrace();
        }

        // OnTick sampler: append each traced projectile's position, and when it lands (entity gone)
        // or a safety cap is hit, draw its arc and drop it. Cheap no-op while nothing is traced.
        private void TraceArcTick()
        {
            if (tracedArcs.Count == 0)
                return;
            if (!isPractice)
            {
                tracedArcs.Clear();
                return;
            }

            arcTickCounter++;
            bool sample = (arcTickCounter % 2) == 0;   // ~32 samples/sec

            List<uint>? finished = null;
            foreach (var kv in tracedArcs)
            {
                var trace = kv.Value;
                trace.Ticks++;
                var ent = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)kv.Key);
                bool alive = ent != null && ent.IsValid && ent.AbsOrigin != null;
                if (alive && sample && trace.Points.Count < 160)
                {
                    var o = ent!.AbsOrigin!;
                    trace.Points.Add(new Vector(o.X, o.Y, o.Z));
                }
                // Finish when the projectile is gone (detonated) or after a safety cap (~4s at 64t)
                // guards against an index that was reused by another entity.
                if (!alive || trace.Ticks > 256)
                    (finished ??= new()).Add(kv.Key);
            }

            if (finished != null)
            {
                foreach (var idx in finished)
                {
                    DrawArc(tracedArcs[idx].Points);
                    tracedArcs.Remove(idx);
                }
            }
        }

        // Store a recorded throw into the per-player history (source for .last / .back / .rt /
        // .throwindex). Shared by the normal AbsVelocity path and the position-delta recovery
        // path in OnEntitySpawnedHandler.
        public void RecordThrownNade(int client, string nadeType, Vector position, QAngle angle, Vector playerPos, QAngle eyeAngles, ushort itemIndex, float duckAmount, Vector velocity, Vector angularVelocity)
        {
            var data = new GrenadeThrownData(position, angle, velocity, playerPos, eyeAngles, nadeType, DateTime.Now, itemIndex, duckAmount, angularVelocity);
            if (!lastGrenadesData.ContainsKey(client))
                lastGrenadesData[client] = new();
            if (!nadeSpecificLastGrenadeData.ContainsKey(client))
                nadeSpecificLastGrenadeData[client] = new();

            nadeSpecificLastGrenadeData[client][nadeType] = data;
            lastGrenadesData[client].Add(data);
            if (maxLastGrenadesSavedLimit != 0 && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit)
                lastGrenadesData[client].RemoveAt(0);

            // Reset the no-arg .back cursor: a new throw restarts stepping from the newest nade
            // (issue MatchZy-Enhanced#7), and avoids a stale index after the history trim.
            lastGrenadeBackCursor.Remove(client);
        }

        // Draw a sampled arc as a chain of CBeams, auto-removed after a few seconds.
        private void DrawArc(List<Vector> pts)
        {
            if (pts.Count < 2)
                return;
            long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var beams = new List<CBeam>(pts.Count - 1);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var b = Utilities.CreateEntityByName<CBeam>("beam");
                if (b == null)
                    continue;
                b.LifeState = 1;
                b.Width = 1.5f;
                b.Render = Color.Cyan;
                b.EndPos.X = pts[i + 1].X;
                b.EndPos.Y = pts[i + 1].Y;
                b.EndPos.Z = pts[i + 1].Z;
                b.Teleport(pts[i], new QAngle(0, 0, 0), new Vector(0, 0, 0));
                b.DispatchSpawn();
                beams.Add(b);
            }
            AddTimer(10.0f, () =>
            {
                foreach (var b in beams)
                    if (b != null && b.IsValid)
                        SafeRemoveEntity(b, "arc");
            }, TimerFlags.STOP_ON_MAPCHANGE);
            double elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            if (elapsedMs >= 5.0)
                Log($"[arc perf] Created {beams.Count} beams in {elapsedMs:F1} ms on the game thread.");
        }

        [ConsoleCommand("css_spec", "Switches team to Spectator")]
        public void OnSpecCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;

            // Force respawn before switching to spec to clear any noclip/movement state
            if (player.PlayerPawn?.IsValid == true && player.PlayerPawn.Value != null)
            {
                // Reset movement type if noclip was enabled
                if (player.PlayerPawn.Value.MoveType == MoveType_t.MOVETYPE_NOCLIP)
                {
                    player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_WALK;
                }
            }

            SideSwitchCommand(player, CsTeam.Spectator);
        }

        [ConsoleCommand("css_fas", "Switches all other players to spectator")]
        [ConsoleCommand("css_watchme", "Switches all other players to spectator")]
        public void OnFASCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null)
                return;
            // Moves every other player to spectator: admins only.
            if (!IsPlayerAdmin(player, "css_fas", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            SideSwitchCommand(player, CsTeam.None);
        }

        [ConsoleCommand("css_noblind", "Disables flash effect for the player")]
        [ConsoleCommand("css_noflash", "Disables flash effect for the player")]
        public void OnNoFlashCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || player.UserId == null)
                return;

            int userId = player.UserId.Value;

            if (noFlashList.Contains(userId))
            {
                noFlashList.Remove(userId);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.noflashoff"));
            }
            else
            {
                noFlashList.Add(userId);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.noflashon"));
                Server.NextFrame(() =>
                {
                    if (!IsPlayerValid(player))
                        return;
                    KillFlashEffect(player);
                });
            }
        }

        [ConsoleCommand("css_breakrestore", "")]
        [ConsoleCommand("css_nobreak", "")]
        public void OnBreakRestoreCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;

            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
            if (gameRules == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.breakrestorenorules"));
                return;
            }

            var postCleanUp = CCSGameRules_PostCleanUp.Value;
            if (postCleanUp == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.breakrestorenosig"));
                Log("[OnBreakRestoreCommand] CCSGameRules_PostCleanUp signature unresolved - breakrestore skipped.");
                return;
            }

            postCleanUp(gameRules);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.breakrestored"));
        }

        [ConsoleCommand("css_break", "Breaks the breakable entities")]
        public void OnBreakCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;

            // Get all breakable entities including doors
            var entities = Utilities.FindAllEntitiesByDesignerName<CBreakable>("prop_dynamic").Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_breakable")).Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("prop_door_rotating")).Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_door")).Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_door_rotating"));

            foreach (var entity in entities)
            {
                if (entity?.IsValid != true || entity.CBodyComponent?.SceneNode == null)
                    continue;

                var position = entity.CBodyComponent.SceneNode.AbsOrigin;
                var className = entity.DesignerName;
                var handle = entity.Handle.ToString();

                entity.AcceptInput("Break");
            }
        }

        public void KillFlashEffect(CCSPlayerController player)
        {
            if (!IsPlayerValid(player))
                return;
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null)
                return;
            playerPawn.FlashMaxAlpha = 0.5f;
        }

        // CsTeam.None is a special value to mean force all other players to spectator
        private void SideSwitchCommand(CCSPlayerController player, CsTeam team)
        {
            if (team > CsTeam.None)
            {
                // SideSwitchCommand runs inside a chat/console command handler (.t/.ct/.spec), i.e.
                // on the engine's command-dispatch stack - so everything below is marshalled off it
                // via Server.NextFrame first.
                Server.NextFrame(() =>
                {
                    // Controller-only validity, NOT IsPlayerValid: a spectator whose player pawn
                    // the engine already destroyed fails IsPlayerValid's pawn checks, and the
                    // whole switch would silently no-op for exactly the player this path must
                    // serve. Pawn-dependent steps below guard themselves.
                    if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected)
                        return;

                    // Already on the requested side (e.g. .ct while on CT): skip the whole
                    // suicide -> SwitchTeam -> Respawn cycle. Switching to the team you are already on
                    // still runs the engine's ChangeBasePlayerTeamAndPendingTeam path with
                    // req team == current team, which has rarely crashed there - and there is nothing
                    // to switch. Just put a dead player back in on T/CT (never respawn a spectator).
                    // Route through RespawnWhenTeamApplied: a pawn-less ex-spectator no-ops the
                    // fork's Respawn(), the helper handles that case.
                    if ((byte)team == player.TeamNum)
                    {
                        if ((team == CsTeam.Terrorist || team == CsTeam.CounterTerrorist) && !player.PawnIsAlive)
                            RespawnWhenTeamApplied(player, team, attemptsLeft: 5);
                        return;
                    }

                    try
                    {
                        // Kill the live pawn BEFORE changing team. The engine's live-player
                        // ChangeTeam strips/destroys the held weapons inline; weapon-lifecycle hooks
                        // from other plugins (e.g. skin plugins) then re-enter on a half-destroyed
                        // weapon and SIGSEGV *inside* ChangeTeam - the reproducible .t/.ct/.spec
                        // practice crash. A normal death just DROPS the weapons, so a dead,
                        // weaponless player never hits that strip path.
                        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
                        if (pawn != null && pawn.IsValid && player.PawnIsAlive)
                        {
                            // Flag this player so the side-switch suicide does NOT count as a
                            // death on the practice scoreboard. The engine increments the death
                            // stat during EventPlayerDeath, AFTER any restore we could do here -
                            // so the reset is done in the Post EventPlayerDeath handler (fires the
                            // exact death tick → scoreboard never settles on the +1). See
                            // MatchZy.cs. Only flag when a suicide actually fires: a stale flag
                            // (e.g. from a spectator switch with no pawn to kill) would swallow
                            // the player's next real death instead.
                            if (player.UserId.HasValue)
                                MarkPracticeSwitchNoDeath(player.UserId.Value);
                            // A pawn that cannot take damage (e.g. after .coachtest) ignores the
                            // suicide and would reach the team change alive.
                            pawn.TakesDamage = true;
                            pawn.CommitSuicide(explode: false, force: true);
                        }

                        // Do the actual switch a frame later, once the death (and weapon drop) has
                        // settled and the pawn is no longer holding anything to strip.
                        Server.NextFrame(() =>
                        {
                            // Controller-only validity (see the guard at the top of this path):
                            // IsPlayerValid would reject the pawn-less ex-spectator this branch
                            // must handle.
                            if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected)
                                return;
                            try
                            {
                                // For a live T<->CT switch use SwitchTeam, not ChangeTeam. ChangeTeam
                                // is a vtable OFFSET in gamedata (fragile across CS2 builds) and runs
                                // the engine's full live team-change path (weapon strip → plugin hooks
                                // → SIGSEGV). SwitchTeam is SIGNATURE-based (build-robust) and just
                                // sets the team number; the Respawn below puts the player in on the
                                // new side.
                                // TWO exceptions - in both the pawn is dead / absent, so the engine's
                                // weapon-strip path has nothing to strip and the full path is safe:
                                // 1. Target Spectator: SwitchTeam only accepts play teams -
                                //    SwitchTeam(Spectator=1) logs "CCSPlayerPawnBase::SwitchTeam( 1 )
                                //    - invalid team index." and does nothing. Use ChangeTeam. No
                                //    respawn (spectator).
                                // 2. Source Spectator/None (.t/.ct while spectating): SwitchTeam only
                                //    writes the team number - it does NOT leave observer mode or
                                //    create a player pawn, so a follow-up Respawn fires on an
                                //    observer-state controller. That is the never-respawn-a-Spectator
                                //    crash class; live symptom is the client dropping itself with
                                //    NETWORK_DISCONNECT_LOOPDEACTIVATE right after the switch.
                                //    ChangeTeam alone is not enough either: it applies the team but
                                //    leaves the controller POSSESSING the observer pawn, and the
                                //    respawn vfunc no-ops while observing - the player lands on the
                                //    team but stays dead forever. Upstream MatchZy never solved this
                                //    (it blocks the switch with "use the team menu").
                                //    Every CSS-level API was tried and fails here:
                                //    ExecuteClientCommandFromServer only dispatches registered
                                //    ConCommands (FindConCommand) and jointeam is handled in the
                                //    game's ClientCommand dispatch -> silent no-op;
                                //    ExecuteClientCommand pushes the command to the client, which
                                //    refuses server-pushed jointeam -> also inert; and SetPawn on
                                //    the detached dead pawn SIGSEGVs (both the CSS 4-arg binding
                                //    and a CS2Fixes-matched 6-arg binding - confirmed twice live).
                                //    Working fix: call the ENGINE's own join handler directly -
                                //    HandleCommand_JoinTeam(controller, team, 2 = immediate) -
                                //    exactly what the engine's spectate/jointeam commands invoke
                                //    (see handleCommandJoinTeam). Falls back to ChangeTeam (team
                                //    applies, player stays dead, helper nags the team menu) if the
                                //    gamedata key is missing (gamedata/matchzy.json not deployed).
                                // Never from team None (still in the team menu): the join handler on an
                                // unassigned controller is the 0.8.88 crash class. They pick a team first.
                                if (player.TeamNum == (byte)CsTeam.None)
                                {
                                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.spectatorbroken"));
                                    return;
                                }
                                bool fromSpectator = player.TeamNum <= (byte)CsTeam.Spectator;
                                bool joinHandlerUsed = true;
                                if (team == CsTeam.Spectator)
                                {
                                    // Only once the pawn is really dead: the suicide above can be a
                                    // no-op or land a few ticks late (see MoveToSpectatorWhenDead).
                                    MoveToSpectatorWhenDead(player, suicideSent: true);
                                }
                                else if (fromSpectator)
                                {
                                    try
                                    {
                                        handleCommandJoinTeam.Value.Invoke(player, (byte)team, 2, 0f);
                                    }
                                    catch (Exception joinEx)
                                    {
                                        Log($"[SideSwitchCommand] HandleCommand_JoinTeam unavailable ({joinEx.Message}), falling back to ChangeTeam");
                                        player.ChangeTeam(team);
                                        joinHandlerUsed = false;
                                    }
                                }
                                else
                                {
                                    player.SwitchTeam(team);
                                }

                                // Practice: respawn onto an actual playing side (T/CT) so you're
                                // live instantly. NEVER respawn when the target is Spectator -
                                // respawning a spectator crashes the server.
                                if (team == CsTeam.Terrorist || team == CsTeam.CounterTerrorist)
                                {
                                    if (fromSpectator && !joinHandlerUsed)
                                    {
                                        // The ChangeTeam fallback leaves the controller observing;
                                        // a Respawn on that controller is the never-respawn-a-
                                        // spectator crash class. The player joins via the team menu.
                                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.spectatorbroken"));
                                    }
                                    else if (fromSpectator)
                                    {
                                        // The join handler applies the team a few frames later; the
                                        // helper polls for it to land, then respawns.
                                        RespawnWhenTeamApplied(player, team, RespawnRetryAttempts);
                                    }
                                    else
                                    {
                                        player.Respawn();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error switching team: {ex.Message}");
                                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.spectatorbroken"));
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error switching team: {ex.Message}");
                        ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.spectatorbroken"));
                    }
                });
                return;
            }

            // .watchme / .fas: force every OTHER human to spectator. Same crash class as the self
            // switch above - x.ChangeTeam(Spectator) on a live pawn runs the weapon-strip path and
            // other plugins' weapon hooks re-enter on a half-destroyed weapon -> SIGSEGV. Use the same
            // safe path: off the command stack -> CommitSuicide (drops weapons) -> next frame
            // SwitchTeam(Spectator) (signature-based). No respawn (spectator).
            Server.NextFrame(() =>
            {
                foreach (var x in Utilities.GetPlayers())
                {
                    if (x == null || !x.IsValid || x.IsBot || x.IsHLTV || x.UserId == player.UserId)
                        continue;
                    var target = x;
                    try
                    {
                        CCSPlayerPawn? pawn = target.PlayerPawn.Value;
                        if (pawn != null && pawn.IsValid && target.PawnIsAlive)
                        {
                            // .fas moves the others to spectator; that suicide is not a death either.
                            if (target.UserId.HasValue)
                                MarkPracticeSwitchNoDeath(target.UserId.Value);
                            // A pawn that cannot take damage ignores the suicide.
                            pawn.TakesDamage = true;
                            pawn.CommitSuicide(explode: false, force: true);
                        }
                        Server.NextFrame(() =>
                        {
                            // Gate on controller validity only: after the suicide the pawn may be
                            // invalid, but ChangeTeam operates on the controller.
                            if (target == null || !target.IsValid)
                                return;
                            // ChangeTeam, NOT SwitchTeam: SwitchTeam is signature-based and only accepts
                            // play teams (T/CT) - SwitchTeam(Spectator=1) logs
                            // "CCSPlayerPawnBase::SwitchTeam( 1 ) - invalid team index." and does nothing.
                            // The pawn was just suicided above (dead, weapons dropped), so ChangeTeam's
                            // weapon-strip path has nothing to strip -> no crash. No respawn (spectator).
                            // Only once the pawn is really dead (see MoveToSpectatorWhenDead).
                            MoveToSpectatorWhenDead(target, suicideSent: true);
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"[watchme] {ex.Message}");
                    }
                }
            });
        }

        private const int RespawnRetryAttempts = 20;

        // Engine CCSPlayerController::HandleCommand_JoinTeam(controller, team, flags, f)
        // - the function the engine's own "jointeam" client command dispatches to
        // (identified in libserver.so build 14173 by its error string
        // "HandleCommand_JoinTeam( %d ) - invalid team index."). flags: bit 1 set
        // (value 3) queues the change to the next round for a dead/spectating player
        // ("teamchange_pending"); value 2 applies the join IMMEDIATELY - the engine's
        // internal spectate handler calls (controller, 1, 2). This runs the complete
        // join flow (leave observer mode, set up the pawn) that no CSS-callable API
        // reaches: ChangeTeam leaves the controller observing, and both
        // ExecuteClientCommand variants fail to deliver "jointeam" (see the comment at
        // the call site). Resolved from the gamedata key
        // "CBasePlayerController_HandleCommand_JoinTeam" (the name the gamedata regen
        // tooling emits) or the older "CCSPlayerController_HandleCommandJoinTeam", shipped
        // in the plugin's own gamedata/matchzy.json so it resolves on both the fork and
        // stock upstream CounterStrikeSharp (regenerate via the CS2SigMaker/ida-pro-mcp
        // tooling if a CS2 update breaks it). Both names are accepted because a regen
        // renamed the key once and silently broke this path. If neither key is present
        // GetSignature throws, the caller catches and falls back to ChangeTeam + the
        // team-menu hint.
        private static readonly string[] HandleCommandJoinTeamKeys =
        {
            "CBasePlayerController_HandleCommand_JoinTeam",
            "CCSPlayerController_HandleCommandJoinTeam",
        };

        private static readonly Lazy<MemoryFunctionVoid<CCSPlayerController, int, int, float>> handleCommandJoinTeam =
            new(() => new MemoryFunctionVoid<CCSPlayerController, int, int, float>(ResolveHandleCommandJoinTeamSignature()));

        private static string ResolveHandleCommandJoinTeamSignature()
        {
            Exception? last = null;
            foreach (var key in HandleCommandJoinTeamKeys)
            {
                try
                {
                    return GameData.GetSignature(key);
                }
                catch (Exception e)
                {
                    last = e;
                }
            }
            throw new InvalidOperationException(
                $"None of the gamedata keys {string.Join(", ", HandleCommandJoinTeamKeys)} is present in gamedata/matchzy.json.", last);
        }

        // Respawn a player once a spectator->team join (client-issued "jointeam", see
        // SideSwitchCommand) has actually applied. The join lands some frames/net round
        // trips later, so poll on a short timer instead of a single next-frame check.
        // Stops as soon as the player is alive, disconnects, practice ends, or attempts
        // run out (~2s at 0.1s per attempt).
        //
        // Traps this helper works around (all confirmed on a live server):
        // - Do NOT gate on IsPlayerValid: it requires a valid PLAYER pawn, which an
        //   ex-spectator may not have, so the guard would bail out every attempt.
        //   Controller-level validity is enough for the calls below.
        // - The fork's Respawn() early-returns on a null pawn; when the pawn handle is
        //   empty, call the CCSPlayerController_Respawn vfunc directly (it recreates the
        //   pawn).
        // - The controller may still possess the OBSERVER pawn after the team applies; in
        //   that state the respawn vfunc no-ops, so a few plain Respawn() attempts are
        //   followed by one escalation: re-possess the player pawn via the FULL-ARITY
        //   SetPawn binding (setPawnFullArity - the CSS built-in 4-arg binding SIGSEGVs,
        //   see its comment), then the respawn vfunc. This mirrors CS2Fixes'
        //   CCSPlayerController::Respawn() exactly.
        // - If nothing lands, tell the player to use the team menu (upstream MatchZy's
        //   fallback for this same engine limitation).
        // keepGoing: optional predicate replacing the default "still in practice" check (the
        // match-setup auto-placement respawns during the ready phase, where isPractice is false).
        private void RespawnWhenTeamApplied(CCSPlayerController player, CsTeam team, int attemptsLeft, Func<bool>? keepGoing = null)
        {
            AddTimer(0.1f, () =>
            {
                if (!(keepGoing?.Invoke() ?? isPractice) || player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected)
                    return;
                if (player.PawnIsAlive)
                    return;
                if (player.TeamNum == (byte)team)
                {
                    try
                    {
                        // NO SetPawn escalation here - EVER. Re-possessing the detached dead
                        // pawn of an ex-spectator SIGSEGVs, confirmed twice on a live server
                        // (with the CSS built-in 4-arg binding AND with the CS2Fixes-matched
                        // full-arity binding). CS2Fixes only ever calls SetPawn on a pawn the
                        // controller still possesses (their spec-joiners go through the
                        // ENGINE's own jointeam handler first, hooked at the metamod level,
                        // which CSS plugins cannot trigger).
                        if (player.PlayerPawn.Value == null)
                        {
                            Log($"[RespawnWhenTeamApplied] {player.PlayerName}: pawn handle empty, calling Respawn vfunc directly ({attemptsLeft} attempts left)");
                            VirtualFunction.CreateVoid<IntPtr>(player.Handle, GameData.GetOffset("CCSPlayerController_Respawn"))(player.Handle);
                        }
                        else
                        {
                            player.Respawn();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[RespawnWhenTeamApplied] respawn failed: {ex.Message}");
                        return;
                    }
                    // Don't stop yet: verify next attempt that the respawn actually took
                    // (it can no-op while the join is still settling).
                }
                if (attemptsLeft > 1)
                {
                    RespawnWhenTeamApplied(player, team, attemptsLeft - 1, keepGoing);
                }
                else
                {
                    Log($"[RespawnWhenTeamApplied] gave up: {player.PlayerName} team={player.TeamNum} target={(byte)team} pawnNull={player.PlayerPawn.Value == null} alive={player.PawnIsAlive}");
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.spectatorbroken"));
                }
            });
        }

        // Snapshot every live utility entity (projectiles + smoke clouds + infernos),
        // deduped by handle. Snapshotting first avoids removing during enumeration (crash).
        private List<(CBaseEntity entity, string label)> GatherUtilityEntities()
        {
            // One pass over the entity list. Six FindAllEntitiesByDesignerName calls (one per
            // projectile type) walked the whole list six times on every grenade detonation while
            // .autoclear was on.
            var entities = new List<(CBaseEntity? entity, string label)>();
            foreach (var e in Utilities.GetAllEntities())
            {
                if (e == null || e.Handle == nint.Zero)
                    continue;
                string? label = e.DesignerName switch
                {
                    "smokegrenade_projectile" => "smoke",
                    "molotov_projectile" => "molotov",
                    "inferno" => "inferno",
                    "hegrenade_projectile" => "hegrenade",
                    "flashbang_projectile" => "flashbang",
                    "decoy_projectile" => "decoy",
                    _ => null,
                };
                if (label != null)
                    entities.Add((new CBaseEntity(e.Handle), label));
            }

            var unique = new List<(CBaseEntity entity, string label)>();
            var seen = new HashSet<nint>();
            foreach (var (entity, label) in entities)
            {
                if (entity == null || entity.Handle == nint.Zero)
                    continue;
                if (!seen.Add(entity.Handle))
                    continue;
                unique.Add((entity, label));
            }
            return unique;
        }

        // RemoveGrenadeEntities SAFE
        public void RemoveGrenadeEntities()
        {
            if (!isPractice)
                return;

            // Drop pending detonation times: .clear before utility lands left stale entries that made a
            // later .rt print absurd flight times.
            lastGrenadeThrownTime.Clear();
            molotovProjectileThrows.Clear();
            infernoStartTimes.Clear();

            var unique = GatherUtilityEntities();
            // Defer actual removal to next frame to avoid touching entities mid-update
            Server.NextFrame(() =>
            {
                foreach (var (entity, label) in unique)
                {
                    SafeRemoveEntity(entity, label);
                }
            });
        }

        // Clear all utility EXCEPT what sits within `radius` of keepPos. Used by .autoclear
        // on a detonation: the just-detonated smoke cloud / inferno spawns at the detonation
        // point, so keeping a small radius preserves the newest result while wiping older util.
        public void ClearUtilityExcept(Vector keepPos, float radius)
        {
            if (!isPractice)
                return;

            var unique = GatherUtilityEntities();
            float r2 = radius * radius;
            Server.NextFrame(() =>
            {
                foreach (var (entity, label) in unique)
                {
                    if (entity != null && entity.IsValid && entity.AbsOrigin is { } o)
                    {
                        float dx = o.X - keepPos.X, dy = o.Y - keepPos.Y, dz = o.Z - keepPos.Z;
                        if (dx * dx + dy * dy + dz * dz <= r2)
                            continue;   // keep the just-detonated / nearby utility
                    }
                    SafeRemoveEntity(entity, label);
                }
            });
        }

        // Called from every detonate handler. Runs the opt-in detonation behaviors:
        // .autoclear (wipe older utility, keep what just detonated at (x,y,z)) and
        // .landmarker (draw a temporary beam at the detonation point).
        public void OnUtilityDetonated(float x, float y, float z)
        {
            if (!isPractice)
                return;
            if (autoClearUtility)
                ClearUtilityExcept(new Vector(x, y, z), 200f);
            if (showLandingMarkers)
                DrawLandingMarker(x, y, z);
        }

        // Landing marker: a short vertical beam at a detonation point, auto-removed after a
        // few seconds. Reuses the CBeam draw from the spawn markers; removes itself on its own timer.
        private void DrawLandingMarker(float x, float y, float z)
        {
            CBeam? beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam == null)
                return;
            beam.LifeState = 1;
            beam.Width = 3;
            beam.Render = Color.Yellow;
            Vector basePos = new Vector(x, y, z + 4.0f);
            beam.EndPos.X = x;
            beam.EndPos.Y = y;
            beam.EndPos.Z = z + 70.0f;
            beam.Teleport(basePos, new QAngle(0, 0, 0), new Vector(0, 0, 0));
            beam.DispatchSpawn();
            // Resolved through the handle (a map change frees the entity) and dropped on map change.
            uint beamRaw = beam.EntityHandle.Raw;
            AddTimer(6.0f, () =>
            {
                var live = new CHandle<CBeam>(beamRaw).Value;
                if (live != null && live.IsValid)
                    SafeRemoveEntity(live, "landmarker");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void SafeRemoveEntity(CBaseEntity? entity, string label)
        {
            // Extra validation to avoid native crashes (invalid handle / already freed)
            if (entity == null || entity.Handle == nint.Zero || !entity.IsValid)
                return;

            try
            {
                // Some entities are safer to kill than remove directly
                entity.AcceptInput("Kill");
            }
            catch
            {
                // Fallback to Remove if Kill input is not supported
                try
                {
                    entity.Remove();
                }
                catch (Exception ex)
                {
                    Log($"[RemoveGrenadeEntities] Failed to remove {label}: {ex.Message}");
                }
            }
        }

        public void ExecDryRunCFG()
        {
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", dryrunCfgPath);

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath))
            {
                //Log($"[ExecDryRunCFG] Starting Dryrun! Executing Dryrun CFG from {dryrunCfgPath}");
                Server.ExecuteCommand($"exec {dryrunCfgPath};{ModeOverrideExec(dryrunCfgPath)}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                //Log($"[ExecDryRunCFG] Starting Dryrun! Dryrun CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 6;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_molotovusedelay 0;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_weapons_glow_on_ground 0;mp_win_panel_display_time 3;occlusion_test_async 0;spec_freeze_deathanim_time 0;spec_freeze_panel_extended_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_coaching_enabled 1;sv_competitive_official_5v5 1;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_holiday_mode 0;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_occlude_players 1;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;cash_team_loser_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
            }
        }

        public void ExecExitDryCFG()
        {
            Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end");
        }

        public void ExecUnpracCommands()
        {
            // Bot jiggle and its position markers belong to the practice session; the next session
            // used to start with bots already jiggling.
            _botJiggleOn = false;
            ClearBotPosViz();

            // A .ff still running when practice ends would leave the server at 5x (and players
            // frozen) until its 10 s timer fired; end it now.
            if (fastForwardTimer != null || preFastForwardMoveTypes != null)
            {
                fastForwardTimer?.Kill();
                ResetFastForward();
            }
            // prac.cfg settings no mode cfg resets: undo them here, before sv_cheats goes off (buddha is
            // a cheat command). Otherwise all-talk and buddha stayed on into a dry run or warmup.
            Server.ExecuteCommand("buddha 0; buddha_ignore_bots 0; sv_full_alltalk 0; sv_showimpacts 0;");
            Server.ExecuteCommand("sv_cheats false;sv_grenade_trajectory_prac_pipreview false;sv_grenade_trajectory_prac_trailtime 0; mp_ct_default_grenades \"\"; mp_ct_default_primary \"\"; mp_t_default_grenades\"\"; mp_t_default_primary\"\"; mp_teammates_are_enemies false;");
            Server.ExecuteCommand("mp_death_drop_defuser true; mp_death_drop_taser true; mp_drop_knife_enable false; mp_death_drop_grenade 2; ammo_grenade_limit_total 4; mp_defuser_allocation 0; sv_infinite_ammo 0; mp_force_pick_time 15");
            // CS2 March 2026+: Re-enable magazine-based reload when exiting practice mode
            Server.ExecuteCommand("""sv_magazine_drop_enabled "true";""");
        }

        public bool IsValidPositionForLastGrenade(CCSPlayerController player, int position)
        {
            int userId = player.UserId!.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nothrownnades"));
                return false;
            }

            if (lastGrenadesData[userId].Count < position)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.grenadehistory", $"{lastGrenadesData[userId].Count}"));
                return false;
            }

            return true;
        }

        public void RethrowSpecificNade(CCSPlayerController player, string nadeType)
        {
            if (!isPractice || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (!nadeSpecificLastGrenadeData.ContainsKey(userId) || !nadeSpecificLastGrenadeData[userId].ContainsKey(nadeType))
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nothrownnadestype", nadeType));
                return;
            }
            GrenadeThrownData grenadeThrown = nadeSpecificLastGrenadeData[userId][nadeType];
            if (grenadeThrown != null)
                AddTimer(grenadeThrown.Delay, () => { if (isPractice && IsPlayerValid(player)) grenadeThrown.Throw(player, SmokeColorForThrow(player)); }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        // Practice teleports (.back, .last, .loadpos, .loadnade) only move a living player on T/CT:
        // teleporting a dead pawn or a spectator moves an entity the player is not controlling.
        private bool CanPracticeTeleport(CCSPlayerController? player)
        {
            if (player == null || !IsPlayerValid(player))
                return false;
            if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
                return false;
            if (player.PlayerPawn.Value!.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.prac.mustbealive"));
                return false;
            }
            return true;
        }

        public void HandleBackCommand(CCSPlayerController? player, string number)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;
            // Guard here (not only in css_back) so the chat .back path is covered too.
            if (!CanPracticeTeleport(player))
                return;
            int userId = player.UserId.Value;
            if (!string.IsNullOrWhiteSpace(number))
            {
                if (int.TryParse(number, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        lastGrenadesData[userId][positionNumber].LoadPosition(player);
                        // Prime the cursor so a following no-arg .back steps older from here.
                        lastGrenadeBackCursor[userId] = positionNumber;
                        // PrintToPlayerChat(player, $"Teleported to grenade of history position: {positionNumber+1}/{lastGrenadesData[userId].Count}");
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.tptogrenade", $"{positionNumber + 1}/{lastGrenadesData[userId].Count}"));
                    }
                }
                else
                {
                    // PrintToPlayerChat(player, $"Invalid value for !back command. Please specify a valid non-negative number. Usage: !back <number>");
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.backinvalidvalue"));
                    return;
                }
            }
            else
            {
                // No-arg .back: step iteratively backward through nade history (CS:GO prac
                // parity, issue MatchZy-Enhanced#7). First press jumps to the newest nade;
                // each subsequent press steps one older; at the oldest it stops (no wrap).
                if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nothrownnades"));
                    return;
                }
                int count = lastGrenadesData[userId].Count;
                int cursor;
                if (!lastGrenadeBackCursor.TryGetValue(userId, out cursor))
                {
                    // First press with no active cursor: jump to the most recent nade.
                    cursor = count - 1;
                }
                else if (cursor <= 0)
                {
                    // Already at the oldest nade in history - don't wrap around.
                    lastGrenadeBackCursor[userId] = 0;
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.backatoldest"));
                    return;
                }
                else
                {
                    // Step one grenade older.
                    cursor -= 1;
                }
                // Defensive clamp: the history can only shrink from the front on a new throw,
                // and a throw resets the cursor, so this should already be in range.
                if (cursor >= count)
                    cursor = count - 1;
                lastGrenadeBackCursor[userId] = cursor;
                lastGrenadesData[userId][cursor].LoadPosition(player);
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.tptogrenade", $"{cursor + 1}/{count}"));
            }
        }

        public void HandleThrowIndexCommand(CCSPlayerController? player, string argString)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;
            int userId = player!.UserId!.Value;

            if (string.IsNullOrEmpty(argString))
            {
                int thrownCount = lastGrenadesData.ContainsKey(userId) ? lastGrenadesData[userId].Count : 0;
                // ReplyToUserCommand(player, $"Usage: !throwindex <number> (You've thrown {thrownCount} grenades till now)");
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.throwindextonumber", thrownCount));
                return;
            }

            string[] argsList = argString.Split();

            foreach (string arg in argsList)
            {
                if (int.TryParse(arg, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        GrenadeThrownData grenadeThrown = lastGrenadesData[userId][positionNumber];
                        AddTimer(grenadeThrown.Delay, () => { if (isPractice && IsPlayerValid(player)) grenadeThrown.Throw(player, SmokeColorForThrow(player)); }, TimerFlags.STOP_ON_MAPCHANGE);
                        // PrintToPlayerChat(player, $"Throwing grenade of history position: {positionNumber+1}/{lastGrenadesData[userId].Count}");
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.throwgrenadehistory", $"{positionNumber + 1}/{lastGrenadesData[userId].Count}"));
                    }
                }
                else
                {
                    // PrintToPlayerChat(player, $"'{arg}' is not a valid non-negative number for !throwindex command.");
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.backnegativenumber", arg));
                }
            }
        }

        public void HandleDelayCommand(CCSPlayerController? player, string delay)
        {
            if (!isPractice || !IsPlayerValid(player))
                return;

            if (!isPractice || player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (string.IsNullOrWhiteSpace(delay))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!delay <delay_in_seconds>"));
                return;
            }

            // 0 is allowed so a previously set delay can be cleared.
            if (float.TryParse(delay, out float delayInSeconds) && delayInSeconds >= 0)
            {
                if (IsValidPositionForLastGrenade(player, 0))
                {
                    lastGrenadesData[userId].Last().Delay = delayInSeconds;
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.delaygrenade", $"{delayInSeconds:0.00}", $"{lastGrenadesData[userId].Count}"));
                }
            }
            else
            {
                // Invalid number: show usage. The old reply indexed lastGrenadesData[userId] (KeyNotFound
                // with no throw history) and its text claimed the delay had been set.
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!delay <delay_in_seconds>"));
                return;
            }
        }

        public void DisplayPracticeTimerCenter(int userId)
        {
            if (!playerTimers.TryGetValue(userId, out var practiceTimer))
                return;
            // Practice ended while the REPEAT timer was running: stop it here so it doesn't keep
            // ticking into the match.
            if (!isPractice)
            {
                practiceTimer.KillTimer();
                playerTimers.Remove(userId);
                return;
            }
            if (!playerData.ContainsKey(userId))
                return;
            if (!IsPlayerValid(playerData[userId]))
                return;
            var timerPlayer = playerData[userId];
            timerPlayer.PrintToCenter(Localizer.ForPlayer(timerPlayer, "matchzy.prac.timercenter", playerTimers[userId].GetTimerResult().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
        }

        // Kill and drop every running .timer. Called on any exit from practice mode.
        public void ClearPracticeTimers()
        {
            foreach (var practiceTimer in playerTimers.Values)
                practiceTimer.KillTimer();
            playerTimers.Clear();
        }

        [ConsoleCommand("css_throw", "Throws the last thrown grenade")]
        [ConsoleCommand("css_rethrow", "Throws the last thrown grenade")]
        [ConsoleCommand("css_rt", "Throws the last thrown grenade")]
        public void OnRethrowCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nothrownnades"));
                return;
            }
            GrenadeThrownData lastGrenade = lastGrenadesData[userId].Last();
            if (lastGrenade != null)
                AddTimer(lastGrenade.Delay, () => { if (isPractice && IsPlayerValid(player)) lastGrenade.Throw(player, SmokeColorForThrow(player)); }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        [ConsoleCommand("css_grt", "Rethrows every player's last thrown grenade at once")]
        [ConsoleCommand("css_globalrethrow", "Rethrows every player's last thrown grenade at once")]
        public void OnGlobalRethrowCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            // Throws for every player on the server: admins only.
            if (!IsPlayerAdmin(player, "css_grt", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            int thrown = 0;
            foreach (var target in Utilities.GetPlayers())
            {
                // Re-validate every target - GetPlayers can include stale/disconnecting slots.
                if (!IsPlayerValid(target) || !target.UserId.HasValue)
                    continue;
                int userId = target.UserId.Value;
                if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
                    continue;

                GrenadeThrownData lastGrenade = lastGrenadesData[userId].Last();
                if (lastGrenade == null)
                    continue;

                // Capture the target so the delayed callback throws for the right player;
                // Throw() re-validates before touching the pawn (safe if they leave meanwhile).
                CCSPlayerController thrower = target;
                AddTimer(lastGrenade.Delay, () => { if (isPractice && IsPlayerValid(thrower)) lastGrenade.Throw(thrower, SmokeColorForThrow(thrower)); }, TimerFlags.STOP_ON_MAPCHANGE);
                thrown++;
            }

            if (player != null)
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.rethrewcount", thrown));
        }

        // Normalize a named-position slot: trim, lowercase, keep [a-z0-9_-], cap length.
        // Empty result => caller falls back to the default (no-arg) slot.
        private static string SanitizePosName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                if (sb.Length >= 24)
                    break;
            }
            return sb.ToString();
        }

        [ConsoleCommand("css_savepos", "Saves the player location. Usage: .savepos [name]")]
        public void OnSavePosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            string name = command != null && command.ArgCount >= 2 ? command.ArgByIndex(1) : "";
            HandleSavePosCommand(player, name);
        }

        public void HandleSavePosCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || player == null || !player.UserId.HasValue || player.PlayerPawn.Value == null)
                return;

            int userId = player.UserId.Value;
            var pawn = player.PlayerPawn.Value;
            Vector position = new(pawn.AbsOrigin?.X, pawn.AbsOrigin?.Y, pawn.AbsOrigin?.Z);
            QAngle angle = new(pawn.EyeAngles?.X, pawn.EyeAngles?.Y, pawn.EyeAngles?.Z);
            var data = new PlayerLocationData(position, angle);

            string slot = SanitizePosName(name);
            if (slot == "")
            {
                // Default single slot (unchanged behavior).
                savedPlayerLocationData[userId] = data;
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.savepos"));
                return;
            }

            if (!namedPlayerPositions.TryGetValue(userId, out var slots))
            {
                slots = new();
                namedPlayerPositions[userId] = slots;
            }
            if (!slots.ContainsKey(slot) && slots.Count >= maxNamedPositions)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.posslotsfull", $"{maxNamedPositions}"));
                return;
            }
            slots[slot] = data;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.saveposnamed", slot));
        }

        [ConsoleCommand("css_loadpos", "Loads a saved player location. Usage: .loadpos [name]")]
        public void OnLoadPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            string name = command != null && command.ArgCount >= 2 ? command.ArgByIndex(1) : "";
            HandleLoadPosCommand(player, name);
        }

        public void HandleLoadPosCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;

            if (!CanPracticeTeleport(player))
                return;

            int userId = player.UserId.Value;
            string slot = SanitizePosName(name);

            if (slot == "")
            {
                if (!savedPlayerLocationData.TryGetValue(userId, out var defaultData))
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.notsavedpos"));
                    return;
                }
                defaultData.LoadPosition(player);
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.loadpos"));
                return;
            }

            if (!namedPlayerPositions.TryGetValue(userId, out var slots) || !slots.TryGetValue(slot, out var data))
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.notsavedposnamed", slot));
                return;
            }
            data.LoadPosition(player);
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.loadposnamed", slot));
        }

        [ConsoleCommand("css_listpos", "Lists your named saved positions")]
        public void OnListPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            HandleListPosCommand(player);
        }

        public void HandleListPosCommand(CCSPlayerController? player)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (!namedPlayerPositions.TryGetValue(userId, out var slots) || slots.Count == 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.posnonenamed"));
                return;
            }
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.poslist", string.Join(", ", slots.Keys.OrderBy(k => k))));
        }

        [ConsoleCommand("css_delpos", "Deletes a named saved position. Usage: .delpos <name>")]
        public void OnDelPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            string name = command != null && command.ArgCount >= 2 ? command.ArgByIndex(1) : "";
            HandleDelPosCommand(player, name);
        }

        public void HandleDelPosCommand(CCSPlayerController? player, string name)
        {
            if (!isPractice || player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            string slot = SanitizePosName(name);
            if (slot == "")
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".delpos <name>"));
                return;
            }
            if (namedPlayerPositions.TryGetValue(userId, out var slots) && slots.Remove(slot))
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.delposnamed", slot));
            else
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.notsavedposnamed", slot));
        }

        [ConsoleCommand("css_flashtest", "Toggle a readout of your own blind duration when flashed")]
        public void OnFlashTestCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !player.UserId.HasValue)
                return;
            int userId = player.UserId.Value;
            if (flashTestList.Remove(userId))
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.flashtestoff"));
            else
            {
                flashTestList.Add(userId);
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.flashteston"));
            }
        }

        [ConsoleCommand("css_throwsmoke", "Throws the last thrown smoke")]
        [ConsoleCommand("css_rethrowsmoke", "Throws the last thrown smoke")]
        public void OnRethrowSmokeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "smoke");
        }

        [ConsoleCommand("css_throwflash", "Throws the last thrown flash")]
        [ConsoleCommand("css_rethrowflash", "Throws the last thrown flash")]
        public void OnRethrowFlashCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "flash");
        }

        [ConsoleCommand("css_throwgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrowgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_thrownade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrownade", "Throws the last thrown he grenade")]
        public void OnRethrowGrenadeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "hegrenade");
        }

        [ConsoleCommand("css_throwmolotov", "Throws the last thrown molotov")]
        [ConsoleCommand("css_rethrowmolotov", "Throws the last thrown molotov")]
        public void OnRethrowMolotovCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "molotov");
        }

        [ConsoleCommand("css_throwdecoy", "Throws the last thrown decoy")]
        [ConsoleCommand("css_rethrowdecoy", "Throws the last thrown decoy")]
        public void OnRethrowDecoyCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null)
                return;
            RethrowSpecificNade(player, "decoy");
        }

        [ConsoleCommand("css_last", "Teleports to the last thrown grenade position")]
        public void OnLastCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !player.UserId.HasValue)
                return;

            // No teleporting spectators or dead pawns
            if (!CanPracticeTeleport(player))
                return;

            int userId = player.UserId.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.notthrownnade"));
                return;
            }
            lastGrenadesData[userId].Last().LoadPosition(player);
            // Prime the cursor at the newest nade so a following no-arg .back steps older.
            lastGrenadeBackCursor[userId] = lastGrenadesData[userId].Count - 1;
        }

        [ConsoleCommand("css_back", "Teleports to the provided position in grenade thrown history")]
        public void OnBackCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (player == null || !player.UserId.HasValue)
                return;

            // Prevent spectators from teleporting
            if (player.TeamNum == (byte)CsTeam.Spectator)
            {
                return;
            }

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleBackCommand(player, commandArg);
            }
            else
            {
                int userId = player!.UserId!.Value;
                int thrownCount = lastGrenadesData.ContainsKey(userId) ? lastGrenadesData[userId].Count : 0;
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.backtonumber", thrownCount));
            }
        }

        [ConsoleCommand("css_throwidx", "Throws grenade of provided position in grenade thrown history")]
        [ConsoleCommand("css_throwindex", "Throws grenade of provided position in grenade thrown history")]
        public void OnThrowIndexCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player))
            {
                return;
            }

            if (command.ArgCount >= 2)
            {
                HandleThrowIndexCommand(player!, command.ArgString);
            }
            else
            {
                int userId = player!.UserId!.Value;
                int thrownCount = lastGrenadesData.ContainsKey(userId) ? lastGrenadesData[userId].Count : 0;
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.pm.throwindextonumber", thrownCount));
            }
        }

        [ConsoleCommand("css_lastindex", "Returns index of the last thrown grenade")]
        public void OnLastIndexCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;
            if (IsValidPositionForLastGrenade(player!, 1))
            {
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.indexlastgrenade", $"{lastGrenadesData[player!.UserId!.Value].Count}"));
            }
        }

        [ConsoleCommand("css_delay", "Adds a delay to the last thrown grenade. Usage: !delay <delay_in_seconds>")]
        public void OnDelayCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;
            if (command.ArgCount >= 2)
            {
                HandleDelayCommand(player!, command.ArgByIndex(1));
            }
            else
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", $"!delay <delay_in_seconds>"));
            }
        }

        [ConsoleCommand("css_timer", "Starts a timer, use .timer again to stop it.")]
        public void OnTimerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;
            int userId = player!.UserId!.Value;
            if (playerTimers.ContainsKey(userId))
            {
                playerTimers[userId].KillTimer();
                // Invariant '.' and two decimals: the localizer formats with the player's culture.
                string timerResult = playerTimers[userId].GetTimerResult().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                player.PrintToCenter(Localizer.ForPlayer(player, "matchzy.prac.timercenter", timerResult));
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.prac.timerstopped", timerResult));
                playerTimers.Remove(userId);
            }
            else
            {
                playerTimers[userId] = new PlayerPracticeTimer(PracticeTimerType.Immediate) { StartTime = DateTime.Now, Timer = AddTimer(0.2f, () => DisplayPracticeTimerCenter(userId), TimerFlags.REPEAT) };
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.prac.timerstarted"));
            }
        }

        [ConsoleCommand("css_sn", "Saves current nade position")]
        [ConsoleCommand("css_savenade", "Saves current nade position")]
        public void OnSaveNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            HandleSaveNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_ln", "Loades the nade with provided filter")]
        [ConsoleCommand("css_loadnade", "Loades the nade with provided filter")]
        public void OnLoadNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            HandleLoadNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_lin", "Lists the nade with provided filter")]
        [ConsoleCommand("css_listnades", "Lists the nade with provided filter")]
        public void OnListNadesCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            HandleListNadesCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_importnade", "Imports the nade with the given code")]
        [ConsoleCommand("css_in", "Imports the nade with the given code")]
        public void OnImportNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            HandleImportNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_deletenade", "Deletes the nade by name")]
        [ConsoleCommand("css_delnade", "Deletes the nade by name")]
        [ConsoleCommand("css_dn", "Deletes the nade by name")]
        public void OnDeleteNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            HandleDeleteNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_solid", "Toggles mp_solid_teammates in practice mode")]
        public void OnSolidCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            int solidValue = ConVar.Find("mp_solid_teammates")!.GetPrimitiveValue<int>();
            int newSolidValue = (solidValue == 0 || solidValue == 1) ? 2 : 1;
            ConVar.Find("mp_solid_teammates")!.SetValue(newSolidValue);
            PrintLocalizedToAll("matchzy.prac.solidteammates", newSolidValue);
        }

        [GameEventHandler]
        public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            // Controller-level check: a leaving player can already be Disconnecting or have no
            // pawn, and IsPlayerValid would then skip the per-slot cleanup.
            if (player == null || !player.IsValid)
                return HookResult.Continue;

            // Clean up the slot so next player gets fresh state
            if (playerImpacts.ContainsKey(player!.Slot))
            {
                playerImpacts.Remove(player.Slot);
            }

            // Reset grenade preview for this slot
            _pipPreviewEnabled[player.Slot] = false;

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player) || player!.IsBot)
                return HookResult.Continue;

            if (isPractice)
            {
                // Impacts ON by default in practice. sv_showimpacts is replicated per client and the
                // client keeps whatever it last received (0 from the previous mode, e.g. deathmatch),
                // so push the practice value explicitly; .impacts toggles it off per player.
                player.ReplicateConVar("sv_showimpacts", "1");
                // Don't add to dictionary - let first .impacts command handle it

                // Force grenade preview ON (default in practice)
                _pipPreviewEnabled[player.Slot] = true;
                player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", "1");

                // Retry impacts after 2 seconds to ensure it sticks
                AddTimer(
                    2.0f,
                    () =>
                    {
                        if (!IsPlayerValid(player) || !player.IsValid)
                            return;

                        player.ReplicateConVar("sv_showimpacts", "1");
                    }
                );
            }

            return HookResult.Continue;
        }

        // impacts
        private Dictionary<int, bool> playerImpacts = new Dictionary<int, bool>();

        [ConsoleCommand("css_impacts", "Toggles sv_showimpacts in practice mode")]
        public void OnImpactsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            // Not in the dictionary = never toggled = at the per-player default, which is ON
            // (OnPlayerConnectFull / ResetAllPlayerPracticeSettings replicate sv_showimpacts 1).
            // Keep this in sync with those two sites or the first press toggles the wrong way.
            if (!playerImpacts.ContainsKey(player!.Slot))
            {
                playerImpacts[player.Slot] = true;
            }

            bool currentState = playerImpacts[player.Slot];

            // Toggle it
            bool enabled = !currentState;
            playerImpacts[player.Slot] = enabled;

            player.ReplicateConVar("sv_showimpacts", enabled ? "1" : "0");

            player.PrintToChat($" {Localizer.ForPlayer(player, "matchzy.prac.showimpacts", Localizer.ForPlayer(player, enabled ? "matchzy.cc.enabled" : "matchzy.cc.disabled"))}");
        }

        private readonly bool[] _pipPreviewEnabled = new bool[64];

        [ConsoleCommand("css_cam", "Toggles nade preview mode for practices")]
        [ConsoleCommand("css_nadecam", "Toggles nade preview mode for practices")]
        [ConsoleCommand("css_traj", "Toggles sv_grenade_trajectory_prac_pipreview in practice mode")]
        [ConsoleCommand("css_pip", "Toggles sv_grenade_trajectory_prac_pipreview in practice mode")]
        public void OnTrajCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            // Toggle the player's personal preference
            bool enabled = !_pipPreviewEnabled[player!.Slot];
            _pipPreviewEnabled[player.Slot] = enabled;

            // Apply it client-side only
            player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", enabled ? "1" : "0");

            // Notify only the player (not all chat)
            player.PrintToChat($" {Localizer.ForPlayer(player, "matchzy.prac.grenadepreviewcam", Localizer.ForPlayer(player, enabled ? "matchzy.cc.enabled" : "matchzy.cc.disabled"))}");
        }

        [ConsoleCommand("css_bestspawn", "Teleports you to your team's closest spawn from your current position")]
        public void OnBestSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToBestSpawn(player!, player!.TeamNum);
        }

        [ConsoleCommand("css_worstspawn", "Teleports you to your team's furthest spawn from your current position")]
        public void OnWorstSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToWorstSpawn(player!, player!.TeamNum);
        }

        [ConsoleCommand("css_bestctspawn", "Teleports you to CT team's closest spawn from your current position")]
        public void OnBestCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToBestSpawn(player!, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand("css_worstctspawn", "Teleports you to CT team's furthest spawn from your current position")]
        public void OnWorstCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand("css_besttspawn", "Teleports you to T team's closest spawn from your current position")]
        public void OnBestTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToBestSpawn(player!, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand("css_worsttspawn", "Teleports you to T team's furthest spawn from your current position")]
        public void OnWorstTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;

            if (player!.TeamNum == (byte)CsTeam.Spectator)
                return;

            TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand("css_showspawns", "Highlights all the competitive spawns")]
        public void OnShowSpawnsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;
            RemoveSpawnBeams();   // clears the flag + list too
            if (spawnsData.Values.Any(list => list.Count == 0))
                GetSpawns();
            foreach (Position spawn in spawnsData[(byte)CsTeam.CounterTerrorist])
            {
                ShowSpawnBeam(spawn, Color.Blue);
                activeSpawnMarkers.Add(spawn);
            }
            foreach (Position spawn in spawnsData[(byte)CsTeam.Terrorist])
            {
                ShowSpawnBeam(spawn, Color.Orange);
                activeSpawnMarkers.Add(spawn);
            }
            // Arm the +use teleport now that markers are drawn.
            spawnMarkersActive = activeSpawnMarkers.Count > 0;
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.spawnmarkerson"));
        }

        // +use onto a drawn spawn marker teleports to that spawn (issue MatchZy-Enhanced#9).
        // Registered as an OnPlayerButtonsChanged listener so it fires once on the rising edge
        // of the Use button - no per-tick polling. Gated on spawnMarkersActive so it's a no-op
        // whenever markers are hidden or we're not in practice.
        private void OnSpawnMarkerButtonHandler(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if (!spawnMarkersActive || !isPractice)
                return;
            if ((pressed & PlayerButtons.Use) == 0)
                return;
            if (!IsPlayerValid(player) || player.IsBot || !player.UserId.HasValue)
                return;
            if (player.TeamNum != (byte)CsTeam.CounterTerrorist && player.TeamNum != (byte)CsTeam.Terrorist)
                return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || pawn.AbsOrigin == null)
                return;

            int uid = player.UserId.Value;
            float now = Server.CurrentTime;
            if (lastSpawnMarkerUseTime.TryGetValue(uid, out float last) && now - last < spawnMarkerUseCooldown)
                return;

            // Eye position + forward unit vector from the view angles.
            Vector origin = pawn.AbsOrigin;
            Vector eye = new Vector(origin.X, origin.Y, origin.Z + 64.0f);
            QAngle ang = pawn.EyeAngles;
            double pitch = ang.X * Math.PI / 180.0;
            double yaw = ang.Y * Math.PI / 180.0;
            var forwardX = (float)(Math.Cos(pitch) * Math.Cos(yaw));
            var forwardY = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            var forwardZ = (float)(-Math.Sin(pitch));

            int bestIndex = -1;
            float bestDot = spawnMarkerAimMinDot;
            for (int i = 0; i < activeSpawnMarkers.Count; i++)
            {
                Vector sp = activeSpawnMarkers[i].PlayerPosition;
                // Exclude the spawn you're standing exactly on (issue #11: 8u radius) so you can
                // still re-center onto a neighbouring marker you're merely near.
                float hdx = sp.X - origin.X, hdy = sp.Y - origin.Y, vdz = sp.Z - origin.Z;
                if (hdx * hdx + hdy * hdy <= spawnMarkerStandRadiusSq && Math.Abs(vdz) <= spawnMarkerStandHeight)
                    continue;
                // Aim at ~mid-beam (spawn + 40z) so looking at the visible beam registers, and
                // pick the marker with the tightest aim cone (largest dot with the view forward).
                float tx = sp.X - eye.X, ty = sp.Y - eye.Y, tz = (sp.Z + 40.0f) - eye.Z;
                float dist = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
                if (dist < 1.0f)
                    continue;
                float dot = (tx * forwardX + ty * forwardY + tz * forwardZ) / dist;
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                return;

            lastSpawnMarkerUseTime[uid] = now;
            Position target = activeSpawnMarkers[bestIndex];
            // Defer off the input callback and re-validate (rules #4 / #7): teleport upright so a
            // steep spawn angle doesn't tilt the model.
            Server.NextFrame(() =>
            {
                if (!IsPlayerValid(player))
                    return;
                TeleportUpright(player, target.PlayerPosition, target.PlayerAngle);
            });
        }

        [ConsoleCommand("css_hidespawns", "Hides the highlighted spawns")]
        public void OnHideSpawnsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            if (!IsPlayerValid(player))
                return;
            RemoveSpawnBeams();   // also disarms the +use interaction
            PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.pm.spawnmarkersoff"));
        }

        private void ResetAllPlayerPracticeSettings(bool enteringPractice)
        {
            if (!enteringPractice)
            {
                // Every mode transition out of practice runs through here: stop the .timer repeaters
                // and take down the .showbotpos markers (RemoveSpawnBeams only owns spawn markers).
                ClearPracticeTimers();
                ClearBotPosViz();
            }

            var players = Utilities.GetPlayers().Where(p => IsPlayerValid(p) && !p.IsBot);

            foreach (var player in players)
            {
                if (enteringPractice)
                {
                    // Entering practice mode - set practice defaults (impacts on; see OnPlayerConnectFull)
                    player.ReplicateConVar("sv_showimpacts", "1");
                    if (playerImpacts.ContainsKey(player.Slot))
                        playerImpacts.Remove(player.Slot);

                    _pipPreviewEnabled[player.Slot] = true;
                    player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", "1");
                }
                else
                {
                    // Exiting practice mode - turn everything OFF
                    player.ReplicateConVar("sv_showimpacts", "0");
                    player.ReplicateConVar("sv_grenade_trajectory_prac_pipreview", "0");
                    if (playerImpacts.ContainsKey(player.Slot))
                        playerImpacts.Remove(player.Slot);
                    _pipPreviewEnabled[player.Slot] = false;
                }
            }
        }

        // Valid competitive teammate colors (see GetPlayerTeammateColor). Anything else
        // falls back to red, so we clamp input to this range.
        private static readonly string[] CompColorNames = { "blue", "green", "yellow", "orange", "purple" };

        [ConsoleCommand("css_color", "Set your competitive teammate color (0-4)")]
        public void OnColorCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerValid(player) || command == null)
                return;

            // Guard: a valid int in 0..4. int.Parse threw on non-numeric input before,
            // and out-of-range values networked as the red fallback.
            if (command.ArgCount < 2
                || !int.TryParse(command.ArgByIndex(1), out int color)
                || color < 0 || color >= CompColorNames.Length)
            {
                PrintToPlayerChat(player!, Localizer.ForPlayer(player, "matchzy.cc.usage", $"css_color <0-{CompColorNames.Length - 1}>  ({string.Join(", ", CompColorNames.Select((c, i) => $"{i}={c}"))})"));
                return;
            }

            int previous = player!.CompTeammateColor;
            if (color == previous)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.prac.coloralready", color, CompColorNames[color]));
                return;
            }

            player.CompTeammateColor = color;
            // Mark networked-dirty so the change actually propagates to clients.
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompTeammateColor");
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.prac.colorset", color, CompColorNames[color]));
        }

        public void TeleportPlayerToBestSpawn(CCSPlayerController player, byte teamNum)
        {
            // Spawn lists can be empty (not collected yet on this map): collect like css_spawn does,
            // and no-op if there are still none rather than indexing [-1].
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns) || teamSpawns.Count == 0)
            {
                GetSpawns();
                if (!spawnsData.TryGetValue(teamNum, out teamSpawns) || teamSpawns.Count == 0)
                    return;
            }
            var playerPawn = player?.PlayerPawn?.Value;
            var playerPosition = playerPawn?.CBodyComponent?.SceneNode?.AbsOrigin;
            if (playerPawn == null || playerPosition == null)
                return;
            int closestIndex = -1;
            double minDistance = double.MaxValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = index;
                }
            }

            if (closestIndex < 0)
                return;
            TeleportUpright(player, teamSpawns[closestIndex].PlayerPosition, teamSpawns[closestIndex].PlayerAngle);
        }

        public void TeleportPlayerToWorstSpawn(CCSPlayerController player, byte teamNum)
        {
            // Spawn lists can be empty (not collected yet on this map): collect like css_spawn does,
            // and no-op if there are still none rather than indexing [-1].
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns) || teamSpawns.Count == 0)
            {
                GetSpawns();
                if (!spawnsData.TryGetValue(teamNum, out teamSpawns) || teamSpawns.Count == 0)
                    return;
            }
            var playerPawn = player?.PlayerPawn?.Value;
            var playerPosition = playerPawn?.CBodyComponent?.SceneNode?.AbsOrigin;
            if (playerPawn == null || playerPosition == null)
                return;
            int farthestIndex = -1;
            double maxDistance = double.MinValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthestIndex = index;
                }
            }

            if (farthestIndex < 0)
                return;
            TeleportUpright(player, teamSpawns[farthestIndex].PlayerPosition, teamSpawns[farthestIndex].PlayerAngle);
        }
    }
}
