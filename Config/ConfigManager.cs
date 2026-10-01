using System;
using System.Collections.Generic;
using System.IO;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public class ConfigManager
    {
        // Lazy: avoids calling native Server.GameDirectory in the ctor, which CSS
        // may construct before the engine pointer is ready (would throw → load abort).
        // Resolves to an existing case-variant dir (e.g. "MatchZy") if one is present,
        // so we don't create a duplicate lowercase "matchzy" on case-sensitive Linux fs.
        private string ServerPath => ResolveMatchZyCfgDir(Server.GameDirectory);

        // Public accessor so the exec path in MatchZy.Load() uses the SAME case-resolved
        // dir the config files were created in. Hardcoding lowercase "matchzy" for the
        // File.Exists/execifexists broke on case-sensitive Linux when the on-disk dir was
        // e.g. "MatchZy" → config.cfg was never exec'd → user cvars (demo_path, etc.) ignored.
        public string GetMatchZyCfgDir() => ServerPath;

        // Resolves csgo/cfg/matchzy without caring about the folder casing on disk. Safe to call
        // off the main thread: it only touches the filesystem, never Server.* (pass the game
        // directory captured beforehand).
        public static string ResolveMatchZyCfgDir(string gameDirectory)
        {
            return ResolveConfigDir(Path.Combine(gameDirectory, "csgo", "cfg"), "matchzy");
        }

        // Picks ONE directory and always the same one, so reads and writes never end up in two
        // different folders. A server that has both "matchzy" and "MatchZy" (an old upstream install
        // plus a new one, on a case-sensitive Linux filesystem) used to get whichever one the
        // filesystem happened to list first, which is why files kept appearing in the folder the
        // admin was not using. Order: an exact "matchzy" wins, then an all-lowercase match, then the
        // first match in a stable alphabetical order.
        private static string ResolveConfigDir(string parent, string name)
        {
            try
            {
                if (Directory.Exists(parent))
                {
                    var matches = new List<string>();
                    foreach (var dir in Directory.GetDirectories(parent))
                    {
                        if (string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase))
                            matches.Add(dir);
                    }
                    if (matches.Count > 0)
                    {
                        matches.Sort(StringComparer.Ordinal);
                        var exact = matches.Find(d => Path.GetFileName(d) == name);
                        var lower = matches.Find(d => Path.GetFileName(d) == Path.GetFileName(d).ToLowerInvariant());
                        var chosen = exact ?? lower ?? matches[0];
                        return chosen;
                    }
                }
            }
            catch { /* fall through to default path */ }
            return Path.Combine(parent, name);
        }

        private void CreateConfigFile(string fileName, string content)
        {
            try
            {
                string filePath = Path.Combine(ServerPath, fileName);
                if (!Directory.Exists(ServerPath))
                {
                    Directory.CreateDirectory(ServerPath);
                }
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, content.TrimStart());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating {fileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// A default cfg as shipped in the repo's cfg/ folder, read from the plugin DLL's embedded
        /// resources ("MatchZy.cfg.live.cfg"). Null when it is not embedded.
        /// </summary>
        public static string? ReadDefaultCfg(string fileName)
        {
            using var stream = typeof(ConfigManager).Assembly.GetManifestResourceStream("MatchZy.cfg." + fileName);
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public void InitializeConfigs()
        {
            // The default cfgs are the files in the repo's cfg/ folder, embedded in the plugin DLL
            // (MatchZy.csproj) - one source of truth. They used to be duplicated here as strings and
            // the two copies had drifted apart (e.g. mp_match_end_changelevel, mp_round_restart_delay).
            var configs = new Dictionary<string, string>();
            foreach (string fileName in new[]
            {
                ConfigFiles.Paths.Config, ConfigFiles.Paths.Dryrun, ConfigFiles.Paths.Knife, ConfigFiles.Paths.Hill,
                ConfigFiles.Paths.Live, ConfigFiles.Paths.LiveWingman, ConfigFiles.Paths.Practice,
                ConfigFiles.Paths.Scrim, ConfigFiles.Paths.Sleep, ConfigFiles.Paths.Warmup,
            })
            {
                string? content = ReadDefaultCfg(fileName);
                if (content != null)
                    configs[fileName] = content;
                else
                    Console.WriteLine($"[MatchZy] Default {fileName} is missing from the plugin DLL; not created.");
            }
            foreach (var config in configs)
            {
                CreateConfigFile(config.Key, config.Value);
            }

            // Append any cvars missing from an existing config.cfg (preserves user edits)
            if (configs.TryGetValue(ConfigFiles.Paths.Config, out string? configTemplate))
                MergeMissingConfigCvars(configTemplate);

            // Create matchzymaps.cfg separately with default map rotation
            CreateMapRotationFile();
        }

        private void MergeMissingConfigCvars(string templateContent)
        {
            try
            {
                string filePath = Path.Combine(ServerPath, ConfigFiles.Paths.Config);
                if (!File.Exists(filePath))
                {
                    return;
                }

                string existing = File.ReadAllText(filePath);
                var existingCvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawLine in existing.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    if (line.StartsWith("//"))
                    {
                        // Treat a commented-out cvar (e.g. "// matchzy_x true") as present so
                        // we do not re-append something the admin deliberately disabled. Only
                        // when the cvar name is the FIRST token after //, not a passing mention
                        // in prose (e.g. "// Example matchzy_hostname_format ...").
                        var commented = line.TrimStart('/').Trim();
                        var commentedName = commented.Split(new[] { ' ', '\t' }, 2)[0];
                        if (commentedName.StartsWith("matchzy_", StringComparison.OrdinalIgnoreCase))
                        {
                            existingCvars.Add(commentedName);
                        }
                        continue;
                    }
                    var name = line.Split(new[] { ' ', '\t' }, 2)[0];
                    if (name.Length > 0)
                    {
                        existingCvars.Add(name);
                    }
                }

                var missingBlocks = new List<string>();
                var currentBlock = new List<string>();

                void FlushBlock()
                {
                    if (currentBlock.Count == 0)
                    {
                        return;
                    }
                    string? cvarName = null;
                    foreach (var bl in currentBlock)
                    {
                        var t = bl.Trim();
                        if (t.Length == 0 || t.StartsWith("//"))
                        {
                            continue;
                        }
                        cvarName = t.Split(new[] { ' ', '\t' }, 2)[0];
                        break;
                    }
                    if (cvarName != null && !existingCvars.Contains(cvarName))
                    {
                        missingBlocks.Add(string.Join("\n", currentBlock));
                    }
                    currentBlock.Clear();
                }

                foreach (var rawLine in templateContent.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        FlushBlock();
                    }
                    else
                    {
                        currentBlock.Add(line);
                    }
                }
                FlushBlock();

                const string header = "// --- Added by MatchZy update (missing cvars appended) ---";

                // Collapse duplicate update headers left by older builds, which appended a FRESH
                // header on every load (one per newly-added cvar). Keep the first; removing the
                // rest only drops redundant comment lines, never a cvar. Ensures there is only ever
                // ONE "Added by MatchZy update" header in the file.
                var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
                int firstHeaderIdx = lines.FindIndex(l => l.Trim() == header);
                bool removedDuplicateHeaders = false;
                if (firstHeaderIdx >= 0)
                {
                    for (int i = lines.Count - 1; i > firstHeaderIdx; i--)
                    {
                        if (lines[i].Trim() == header)
                        {
                            lines.RemoveAt(i);
                            removedDuplicateHeaders = true;
                        }
                    }
                }

                // Prune retired cvars: a convar that was renamed/removed leaves a dead line in old
                // config.cfg files that spams "Unknown command '<name>'!" every time config.cfg execs.
                // Remove the setting line (or a commented-out one) plus a directly-preceding help
                // comment. Only EXACT retired names are touched; the current replacement (if any) is
                // appended by the missing-cvar pass above, so this migrates the file cleanly.
                var retiredCvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "matchzy_ready_hint_suppress_warmup", // renamed -> matchzy_ready_hide_warmup_hud (itself now retired)
                    "matchzy_ready_hide_warmup_hud",      // folded into matchzy_ready_hint_style modes 1/2
                    "matchzy_ready_block_warmup_announce",// removed: experimental warmup-announce block (dead end)
                };
                bool removedRetired = false;
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    var t = lines[i].Trim();
                    if (t.Length == 0)
                        continue;
                    string first = t.StartsWith("//")
                        ? t.TrimStart('/').Trim().Split(new[] { ' ', '\t' }, 2)[0]
                        : t.Split(new[] { ' ', '\t' }, 2)[0];
                    if (!retiredCvars.Contains(first))
                        continue;
                    lines.RemoveAt(i);
                    // Also drop a single directly-preceding help comment (not the update header).
                    if (i - 1 >= 0 && lines[i - 1].Trim().StartsWith("//") && lines[i - 1].Trim() != header)
                        lines.RemoveAt(i - 1);
                    removedRetired = true;
                }

                // Nothing new to add and nothing to clean -> leave the file untouched.
                if (missingBlocks.Count == 0 && !removedDuplicateHeaders && !removedRetired)
                {
                    return;
                }

                var sb = new System.Text.StringBuilder(string.Join("\n", lines).TrimEnd('\n'));
                sb.Append('\n');
                if (missingBlocks.Count > 0)
                {
                    sb.Append('\n');
                    // Add the header only when the file has none (after the dedup above).
                    if (firstHeaderIdx < 0)
                    {
                        sb.Append(header).Append('\n');
                    }
                    foreach (var block in missingBlocks)
                    {
                        sb.Append(block);
                        sb.Append("\n\n");
                    }
                }

                // Rewrite (not append): only redundant header comment lines were removed, so admin
                // edits to actual cvars are preserved.
                File.WriteAllText(filePath, sb.ToString());
                if (missingBlocks.Count > 0)
                    Console.WriteLine($"[MatchZy] Appended {missingBlocks.Count} missing cvar(s) to {ConfigFiles.Paths.Config}.");
                if (removedDuplicateHeaders)
                    Console.WriteLine($"[MatchZy] Collapsed duplicate update headers in {ConfigFiles.Paths.Config}.");
                if (removedRetired)
                    Console.WriteLine($"[MatchZy] Removed retired cvar line(s) from {ConfigFiles.Paths.Config}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error merging {ConfigFiles.Paths.Config}: {ex.Message}");
            }
        }

        private void CreateMapRotationFile()
        {
            try
            {
                string filePath = Path.Combine(ServerPath, "matchzymaps.cfg");
                if (!Directory.Exists(ServerPath))
                {
                    Directory.CreateDirectory(ServerPath);
                }
                if (!File.Exists(filePath))
                {
                    string defaultMaps = ReadDefaultCfg("matchzymaps.cfg") ??
                        @"# MatchZy Map Rotation Configuration
# 
# This file controls automatic map changes after matches end
# 
# How it works:
# - One map per line
# - Lines starting with # are comments and will be ignored
# - Empty lines are ignored
# - Maps will rotate in the order listed (top to bottom)
# - After the last map, rotation returns to the first map
# - If only one map is listed, the server will reload that same map
# 
# Map Format Examples:
# de_dust2                    - Standard map
# de_ancient                  - Another standard map
# workshop/3070212801         - Workshop map (recommended format)
# 3070212801                  - Workshop map by ID only (also works)
# workshop/3070212801/de_cache - Workshop map with name (works too)
# 
# Important Notes:
# - Workshop maps MUST be downloaded on your server before they can be used
# - Use +host_workshop_collection in your server launch options to auto-download
# - The plugin will use host_workshop_map for workshop maps automatically
# - The plugin will use changelevel for standard maps automatically
# 
# Tip: You can have just one map for a dedicated server, or multiple maps for variety
#
# Default Active Duty Map Pool:

de_dust2
de_inferno
de_mirage
de_nuke
de_overpass
de_vertigo
de_ancient

# Workshop Map Examples (uncomment to use):
# workshop/3070212801
# workshop/3121217565";

                    File.WriteAllText(filePath, defaultMaps);
                    Console.WriteLine($"[MatchZy] Created default map rotation file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchZy] Error creating matchzymaps.txt: {ex.Message}");
            }
        }

        public List<string> LoadMapRotation()
        {
            var maps = new List<string>();
            try
            {
                string filePath = Path.Combine(ServerPath, "matchzymaps.cfg");
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                            continue;

                        maps.Add(trimmed);
                    }
                    Console.WriteLine($"[MatchZy] Loaded {maps.Count} maps from map rotation");
                }
                else
                {
                    Console.WriteLine($"[MatchZy] matchzymaps.cfg not found, creating default...");
                    CreateMapRotationFile();
                    return LoadMapRotation();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchZy] Error loading map rotation: {ex.Message}");
            }
            return maps;
        }
    }
}
