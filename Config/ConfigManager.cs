using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
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
        public static string? ReadDefaultCfg(string fileName) => ReadResource(CfgResourcePrefix + fileName);

        /// <summary>
        /// Every console name the plugin registers: FakeConVar fields and [ConsoleCommand] methods.
        /// The config.cfg merge prunes matchzy_* lines that are not in this set (removed from the
        /// source), so a retired setting does not stay behind as "Unknown command" on old servers.
        /// </summary>
        public static HashSet<string> CollectRegisteredNames(object plugin)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = plugin.GetType();
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(FakeConVar<>))
                    continue;
                object? cvar = field.GetValue(field.IsStatic ? null : plugin);
                if (cvar?.GetType().GetProperty("Name")?.GetValue(cvar) is string name)
                    names.Add(name);
            }
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                foreach (ConsoleCommandAttribute attr in method.GetCustomAttributes<ConsoleCommandAttribute>())
                    names.Add(attr.Command);
            }
            return names;
        }

        public void InitializeConfigs(ISet<string>? registeredNames = null)
        {
            // The default cfgs are the files in the repo's cfg/ folder, embedded in the plugin DLL
            // (MatchZy.csproj) - one source of truth. They used to be duplicated here as strings and
            // the two copies had drifted apart (e.g. mp_match_end_changelevel, mp_round_restart_delay).
            var configs = new Dictionary<string, string>();
            foreach (string fileName in new[]
            {
                ConfigFiles.Paths.Config, ConfigFiles.Paths.Dryrun, ConfigFiles.Paths.Knife, ConfigFiles.Paths.Hill,
                ConfigFiles.Paths.Live, ConfigFiles.Paths.LiveWingman, ConfigFiles.Paths.LiveRush,
                ConfigFiles.Paths.Live1v1, ConfigFiles.Paths.Practice,
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
                MergeMissingConfigCvars(configTemplate, registeredNames);

            // Create matchzymaps.cfg separately with default map rotation
            CreateMapRotationFile();

            WriteReferenceDefaults();
        }

        private const string DefaultsFolderName = "defaults";
        private const string CfgResourcePrefix = "MatchZy.cfg.";
        private const string DatabaseExampleResource = "MatchZy.defaults.database.json.example";

        private const string DefaultsReadme =
"""
MatchZy - reference defaults
============================

This folder holds the default config files of the MatchZy version that is
currently installed. MatchZy rewrites it every time the plugin loads, so it
always matches the running version.

  - Nothing in this folder is executed or read by MatchZy.
  - Do NOT edit files here. Your changes are overwritten on the next load.
    Edit the files one folder up instead (the folder that contains this
    "defaults" folder).

What it is for
--------------
Your own config files are never overwritten by an update. When a release
changes a default (the changelog says which file and which setting), compare
your file with the copy in this folder and copy over the lines you want.

  Linux:    diff ../live.cfg live.cfg
  Windows:  fc ..\live.cfg live.cfg

Getting a fresh default
-----------------------
Delete (or rename) your own file, for example warmup.cfg, and restart the
server or reload the plugin. MatchZy writes the current default in its place.
For config.cfg this resets every plugin setting: keep a copy and copy your own
values back afterwards.

Your own changes to a mode
--------------------------
Instead of editing warmup.cfg (or any other mode cfg), you can put only the
lines you want to change in a file named after it with "_override" added,
for example warmup_override.cfg, one folder up. MatchZy runs it right after
warmup.cfg, so its values win. MatchZy never creates or changes these files.
Works for warmup, knife, live, live_wingman, live_rush, live_1v1, scrim, hill,
prac, dryrun and sleep. Settings from a loaded match config's "cvars" block
still apply on top.

config.cfg
----------
New settings are added to the bottom of your existing config.cfg
automatically, under a "// --- Added by MatchZy update" header. Settings you
already have, including commented-out ones, are left alone. Changed defaults of
existing settings are not applied for you: compare with config.cfg here.
The only lines MatchZy removes are settings that no longer exist (renamed or
retired), so the server does not log "Unknown command" for them.

database.json.example
---------------------
Example of database.json with the MySQL fields. MatchZy creates the real
database.json (SQLite) one folder up on first load if it does not exist.
To use MySQL, copy the MySQL fields into that file, set "DatabaseType" to
"MySQL" and restart the server.

Files
-----
One copy of every default cfg this version ships: config.cfg holds the plugin
settings (matchzy_* convars), matchzymaps.cfg the map rotation, and each other
file is the game-mode config it is named after (warmup.cfg, knife.cfg,
live.cfg, prac.cfg, ...).

admins.json, whitelist.cfg and savednades.json are not here: MatchZy creates
them one folder up the first time they are needed, and they have no defaults
to compare with.
""";

        // Writes reference copies of the current defaults to <cfg dir>/defaults/: every embedded cfg,
        // database.json.example and a README. Never exec'd. Lives under the case-resolved cfg dir
        // (not in the release zip) so it lands in the folder the server actually uses and never
        // creates a second matchzy/MatchZy folder. A file is only rewritten when its content
        // differs, and reference cfgs that a newer version no longer ships are removed.
        private void WriteReferenceDefaults()
        {
            try
            {
                string dir = Path.Combine(ServerPath, DefaultsFolderName);
                Directory.CreateDirectory(dir);

                var expectedCfgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var assembly = typeof(ConfigManager).Assembly;
                foreach (string resource in assembly.GetManifestResourceNames())
                {
                    if (!resource.StartsWith(CfgResourcePrefix, StringComparison.Ordinal))
                        continue;
                    string fileName = resource.Substring(CfgResourcePrefix.Length);
                    string? content = ReadResource(resource);
                    if (content == null)
                        continue;
                    expectedCfgs.Add(fileName);
                    WriteIfChanged(Path.Combine(dir, fileName), content.TrimStart());
                }

                string? dbExample = ReadResource(DatabaseExampleResource);
                if (dbExample != null)
                    WriteIfChanged(Path.Combine(dir, "database.json.example"), dbExample);

                WriteIfChanged(Path.Combine(dir, "README.txt"), DefaultsReadme.TrimStart() + "\n");

                // Drop reference cfgs of files this version no longer ships. Only *.cfg: anything
                // else an admin put here is left alone.
                foreach (string existing in Directory.GetFiles(dir, "*.cfg"))
                {
                    if (!expectedCfgs.Contains(Path.GetFileName(existing)))
                        File.Delete(existing);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchZy] Could not write reference defaults: {ex.Message}");
            }
        }

        private static string? ReadResource(string name)
        {
            using var stream = typeof(ConfigManager).Assembly.GetManifestResourceStream(name);
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content)
                return;
            File.WriteAllText(path, content);
        }

        // The merge itself is ConfigCfgMerge.Merge (Logic/, unit tested); this only does the file I/O.
        private void MergeMissingConfigCvars(string templateContent, ISet<string>? registeredNames)
        {
            try
            {
                string filePath = Path.Combine(ServerPath, ConfigFiles.Paths.Config);
                if (!File.Exists(filePath))
                {
                    return;
                }

                var result = ConfigCfgMerge.Merge(File.ReadAllText(filePath), templateContent, registeredNames);
                if (result.PruneSkipped)
                    Console.WriteLine($"[MatchZy] Skipped pruning removed cvars from {ConfigFiles.Paths.Config}: the registered cvar list is incomplete.");
                if (result.NewContent == null)
                    return;

                // Rewrite (not append): only redundant header comment lines and retired settings were
                // removed, so admin edits to actual cvars are preserved.
                File.WriteAllText(filePath, result.NewContent);
                if (result.AppendedCount > 0)
                    Console.WriteLine($"[MatchZy] Appended {result.AppendedCount} missing cvar(s) to {ConfigFiles.Paths.Config}.");
                if (result.CollapsedDuplicateHeaders)
                    Console.WriteLine($"[MatchZy] Collapsed duplicate update headers in {ConfigFiles.Paths.Config}.");
                if (result.RemovedNames.Count > 0)
                    Console.WriteLine($"[MatchZy] Removed cvar(s) no longer in MatchZy from {ConfigFiles.Paths.Config}: {string.Join(", ", result.RemovedNames)}");
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
                }
                else
                {
                    Console.WriteLine($"[MatchZy] matchzymaps.cfg not found, creating default...");
                    CreateMapRotationFile();
                    // Read it back once. CreateMapRotationFile swallows its errors, so recursing
                    // on a folder that cannot be written (read-only mount, wrong owner, full disk)
                    // never ended: a StackOverflowException kills the server at every start.
                    if (File.Exists(filePath))
                        return LoadMapRotation();
                    Console.WriteLine("[MatchZy] Could not create matchzymaps.cfg; map rotation is empty.");
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
