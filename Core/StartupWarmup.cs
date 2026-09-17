using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Methods the first LIVE round_start runs for the first time in the process. Warmup and
        // knife rounds take the !matchStarted / !isMatchLive early-outs, so the backup, stats and
        // damage-info paths are cold until the first live round, and that round pays their tier-0
        // JIT on the game thread. Anything here that references an optional runtime dependency
        // (CS2MenuManager menu types, fork-only CounterStrikeSharp types) must stay out of this list:
        // JIT-compiling such a method loads the assembly, which is exactly what the lazy menu
        // resolution in AdminMenu.cs is built to avoid.
        private static readonly string[] RoundStartWarmMethods =
        {
            nameof(HandlePostRoundStartEvent),
            nameof(HandlePlayoutConfig),
            nameof(GetConvarValueFromCFGFile),
            nameof(ReadCfgFileCached),
            nameof(FormatCvarValue),
            nameof(GetTeamsScore),
            nameof(GetRoundNumer),
            nameof(GetGameRules),
            nameof(HandleCoaches),
            nameof(GetAllCoaches),
            nameof(CreateMatchZyRoundDataBackup),
            nameof(CaptureScoreboardSnapshot),
            nameof(GetMatchConfig),
            nameof(GetTeamConfig),
            nameof(InitPlayerDamageInfo),
            nameof(UpdateHostname),
            nameof(SetTeamNames),
            nameof(RefreshTeamEntities),
            nameof(OnAdvancedStatsRoundStart),
            nameof(UpdateAliveCounts),
        };

        /// <summary>
        /// Pays the one-off costs of the live round_start path on a thread-pool thread at Load
        /// instead of on the game thread during the first live round of a match.
        ///
        /// The slow-frame profiler showed exactly one ~90 ms "MatchZy round_start" frame per
        /// match, not one per round. That is the first-use cost of the code the first live round
        /// touches: Newtonsoft builds and caches its contract for MatchConfig and Team on the
        /// first SerializeObject, System.Text.Json builds its metadata for the scoreboard and
        /// round-data types on the first Serialize, and the JIT compiles every method on the path
        /// on first call. All three caches are process-wide and thread-safe, so doing the same
        /// calls with throwaway data here means the real round finds them warm. Touches nothing
        /// in the engine, so it is safe off the game thread.
        /// </summary>
        private void WarmRoundStartPaths()
        {
            _ = Task.Run(() =>
            {
                Stopwatch sw = Stopwatch.StartNew();
                int prepared = 0;
                try
                {
                    // Newtonsoft: contract cache for the two types the round backup serializes.
                    Newtonsoft.Json.JsonConvert.SerializeObject(new MatchConfig());
                    Newtonsoft.Json.JsonConvert.SerializeObject(new Team { teamName = "warmup" });

                    // System.Text.Json: metadata is cached per options instance, so use the same
                    // instances the real path uses (default options for the scoreboard, the shared
                    // BackupJsonOptions for the round-data dictionary).
                    JsonSerializer.Serialize(new List<ScoreboardSnapshot> { new ScoreboardSnapshot() });
                    JsonSerializer.Serialize(new Dictionary<string, string> { { "warmup", "" } }, BackupJsonOptions);
                    JsonSerializer.Deserialize<Dictionary<string, string>>("{\"warmup\":\"\"}");

                    // Prepare managed rethrow entry points without invoking them. Native
                    // signature resolution and entity creation must remain on the game thread.
                    var throwMethod = typeof(GrenadeThrownData).GetMethod(nameof(GrenadeThrownData.Throw));
                    var createMethod = typeof(CounterStrikeSharp.API.Utilities).GetMethods()
                        .Single(m => m.Name == "CreateEntityByName" && m.IsGenericMethodDefinition)
                        .MakeGenericMethod(typeof(CounterStrikeSharp.API.Core.CFlashbangProjectile));
                    var spawnMethod = typeof(CounterStrikeSharp.API.Core.CBaseEntity)
                        .GetMethod("DispatchSpawn", Type.EmptyTypes);
                    foreach (var method in new[] { throwMethod, createMethod, spawnMethod })
                    {
                        if (method == null) continue;
                        try
                        {
                            RuntimeHelpers.PrepareMethod(method.MethodHandle);
                            prepared++;
                        }
                        catch (Exception e)
                        {
                            Log($"[Warmup] Could not prepare {method.Name}: {e.GetType().Name}: {e.Message}");
                        }
                    }

                    // JIT the round_start methods themselves. PrepareMethod compiles without
                    // running anything.
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    foreach (MethodInfo method in typeof(MatchZy).GetMethods(flags))
                    {
                        if (method.ContainsGenericParameters || Array.IndexOf(RoundStartWarmMethods, method.Name) < 0)
                            continue;
                        try
                        {
                            RuntimeHelpers.PrepareMethod(method.MethodHandle);
                            prepared++;
                        }
                        catch (Exception e)
                        {
                            Log($"[Warmup] Could not prepare {method.Name}: {e.GetType().Name}: {e.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Log($"[Warmup] round_start warm-up failed: {e.GetType().Name}: {e.Message}");
                }
                Log($"[Warmup] round_start path warmed in {sw.ElapsedMilliseconds} ms on a background thread ({prepared} methods prepared).");
            });
        }
    }
}
