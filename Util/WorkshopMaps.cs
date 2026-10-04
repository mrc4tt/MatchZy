using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Workshop map support.
        //
        // A server started with "+host_workshop_map <id>" and/or "+host_workshop_collection <id>"
        // (and no real "+map <name>", panels pass "+map <empty>") plays maps that only exist inside
        // workshop packages. A plain "changelevel de_safehouse" for such a map looks for
        // maps/de_safehouse.vpk, fails ("Failed to mount world vpk file"), shuts the server down with
        // NETWORK_DISCONNECT_CLIENT_NO_MAP while clients are still attached, and the engine then
        // crashes (use-after-free in libengine2, "~CServerChangelevelState with non empty m_Clients").
        // Workshop maps must be loaded with "host_workshop_map <id>" or, for a map of the hosted
        // collection, "ds_workshop_changelevel <name>".
        //
        // This file:
        //  - detects workshop launch options once (fork CommandLine helper, /proc/self/cmdline fallback),
        //  - learns which map names are workshop maps (and their ids where known),
        //  - builds the right map-change command for any map spec (BuildMapChangeCommand),
        //  - redirects a plain console "changelevel"/"map" for a workshop map (the engine's own
        //    match-end changelevel, other plugins) to the workshop command instead of letting it crash.

        private bool _workshopLaunchParsed;
        private string? _launchWorkshopMapId;      // +host_workshop_map
        private string? _workshopCollectionId;     // +host_workshop_collection (or set at runtime)
        private bool _launchMapIsEmpty;            // no usable "+map <name>" on the command line

        // Map name -> workshop id, for every workshop map whose id we know.
        private readonly Dictionary<string, string> _workshopMapIds = new(StringComparer.OrdinalIgnoreCase);
        // Map names known to be workshop maps (with or without a known id).
        private readonly HashSet<string> _workshopMapNames = new(StringComparer.OrdinalIgnoreCase);

        // Set by a host_workshop_map / ds_workshop_changelevel we saw; bound to the map name on the
        // next map start.
        private string? _pendingWorkshopMapId;
        private bool _pendingWorkshopByName;
        // Once any workshop map change has happened, the launch id no longer describes the current map.
        // Also set on a hot reload: the map that is up may be any workshop map loaded before the reload.
        private bool _workshopMapChangeSeen;

        // Lower-cased names of the map files in the install (maps/*.vpk|*.bsp); null = maps folder not found.
        private HashSet<string>? _localMapFiles;
        private bool _localMapFilesScanned;

        private static readonly string[] WorkshopGuardCommands = { "changelevel", "map" };

        // True when the server was started as a workshop server: no "+map <name>" and at least one of
        // +host_workshop_map / +host_workshop_collection.
        public bool IsWorkshopServer
        {
            get
            {
                EnsureWorkshopLaunchParsed();
                return _launchMapIsEmpty && (_launchWorkshopMapId != null || _workshopCollectionId != null);
            }
        }

        private void EnsureWorkshopLaunchParsed()
        {
            if (_workshopLaunchParsed)
                return;
            _workshopLaunchParsed = true;

            if (TryGetLaunchOption("+host_workshop_map", out var mapId) && IsWorkshopId(mapId))
                _launchWorkshopMapId = mapId;
            if (TryGetLaunchOption("+host_workshop_collection", out var collectionId) && IsWorkshopId(collectionId))
                _workshopCollectionId = collectionId;

            _launchMapIsEmpty = !TryGetLaunchOption("+map", out var launchMap)
                || string.IsNullOrWhiteSpace(launchMap)
                || launchMap.Equals("<empty>", StringComparison.OrdinalIgnoreCase);

            if (_launchWorkshopMapId != null || _workshopCollectionId != null)
            {
                Log($"[Workshop] Launch options: host_workshop_map={_launchWorkshopMapId ?? "-"}, " +
                    $"host_workshop_collection={_workshopCollectionId ?? "-"}, +map {(_launchMapIsEmpty ? "empty" : "set")} " +
                    $"-> workshop server: {IsWorkshopServer}");
            }
        }

        private void RegisterWorkshopHooks(bool hotReload)
        {
            EnsureWorkshopLaunchParsed();
            if (hotReload)
                _workshopMapChangeSeen = true;

            AddCommandListener("host_workshop_map", OnHostWorkshopMapCommand, HookMode.Pre);
            AddCommandListener("ds_workshop_changelevel", OnDsWorkshopChangelevelCommand, HookMode.Pre);
            AddCommandListener("host_workshop_collection", OnHostWorkshopCollectionCommand, HookMode.Pre);
            foreach (var command in WorkshopGuardCommands)
                AddCommandListener(command, OnPlainChangelevelGuard, HookMode.Pre);

            RegisterListener<Listeners.OnMapStart>(TrackWorkshopMapOnMapStart);
        }

        private static bool IsWorkshopId(string? value)
        {
            return !string.IsNullOrEmpty(value) && value.All(char.IsDigit) && value.Length <= 20;
        }

        private static string CommandArg(CommandInfo info)
        {
            return info.ArgCount > 1 ? info.ArgByIndex(1).Trim().Trim('"').Trim() : "";
        }

        private HookResult OnHostWorkshopMapCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player != null)
                return HookResult.Continue;
            string id = CommandArg(info);
            if (IsWorkshopId(id))
            {
                _pendingWorkshopMapId = id;
                _pendingWorkshopByName = false;
                _workshopMapChangeSeen = true;
            }
            return HookResult.Continue;
        }

        private HookResult OnDsWorkshopChangelevelCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player != null)
                return HookResult.Continue;
            if (CommandArg(info) != "")
            {
                _pendingWorkshopMapId = null;
                _pendingWorkshopByName = true;
                _workshopMapChangeSeen = true;
            }
            return HookResult.Continue;
        }

        private HookResult OnHostWorkshopCollectionCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (player != null)
                return HookResult.Continue;
            string id = CommandArg(info);
            if (IsWorkshopId(id))
                _workshopCollectionId = id;
            return HookResult.Continue;
        }

        private void TrackWorkshopMapOnMapStart(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName))
                return;

            // A workshop command that never loaded (bad id, failed download) leaves its pending state
            // behind; the next map is then a normal one and must not inherit it.
            if (IsLocalMapFile(mapName) == true)
            {
                // Stock map: nothing to learn.
            }
            else if (_pendingWorkshopMapId != null)
            {
                RememberWorkshopMap(mapName, _pendingWorkshopMapId);
            }
            else if (_pendingWorkshopByName)
            {
                RememberWorkshopMap(mapName, null);
            }
            else
            {
                BindLaunchWorkshopMap(mapName);
            }
            _pendingWorkshopMapId = null;
            _pendingWorkshopByName = false;
        }

        // The boot map of a "+host_workshop_map <id>" server is that id. MatchZy can load after the
        // first map started (OnMapStart is not replayed), so this also runs lazily from the lookups.
        private void BindLaunchWorkshopMap(string? mapName)
        {
            EnsureWorkshopLaunchParsed();
            if (_workshopMapChangeSeen || _launchWorkshopMapId == null || string.IsNullOrWhiteSpace(mapName))
                return;
            if (_workshopMapIds.ContainsValue(_launchWorkshopMapId) || IsLocalMapFile(mapName) == true)
                return;
            RememberWorkshopMap(mapName, _launchWorkshopMapId);
        }

        private void RememberWorkshopMap(string mapName, string? id)
        {
            if (!IsSafeMapName(mapName))
                return;
            bool known = _workshopMapNames.Contains(mapName);
            _workshopMapNames.Add(mapName);
            if (id != null)
            {
                if (_workshopMapIds.TryGetValue(mapName, out var existing) && existing == id && known)
                    return;
                _workshopMapIds[mapName] = id;
                Log($"[Workshop] {mapName} is workshop map {id}");
            }
            else if (!known)
            {
                Log($"[Workshop] {mapName} is a workshop collection map");
            }
        }

        private void LearnCurrentWorkshopMap()
        {
            if (!AreServerGlobalsReady())
                return;
            string current;
            try { current = Server.MapName; } catch { return; }
            BindLaunchWorkshopMap(current);
        }

        // Whether maps/<name>.vpk exists in the game install. null when the maps folder cannot be
        // found (unknown, callers fall back to IsMapValid).
        // Scanned once (the install does not change while the server runs), case-insensitive.
        private bool? IsLocalMapFile(string mapName)
        {
            if (!_localMapFilesScanned)
            {
                _localMapFilesScanned = true;
                try
                {
                    string gameDir = Server.GameDirectory;
                    foreach (var mapsDir in new[] { Path.Join(gameDir, "csgo", "maps"), Path.Join(gameDir, "maps") })
                    {
                        if (!Directory.Exists(mapsDir))
                            continue;
                        _localMapFiles = Directory.EnumerateFiles(mapsDir)
                            .Where(f => f.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
                            .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                            .ToHashSet();
                        break;
                    }
                }
                catch
                {
                    _localMapFiles = null;
                }
            }
            return _localMapFiles?.Contains(mapName.ToLowerInvariant());
        }

        private bool IsKnownWorkshopMap(string mapName)
        {
            LearnCurrentWorkshopMap();
            return _workshopMapNames.Contains(mapName);
        }

        // A map name the engine can load with a plain changelevel: a map file in the install and not a
        // known workshop map. (IsMapValid alone is not enough - a mounted workshop map can validate
        // while its world vpk is not in maps/.)
        private bool IsStockMap(string mapName)
        {
            if (IsKnownWorkshopMap(mapName))
                return false;
            if (IsLocalMapFile(mapName) == true)
                return true;
            if (!Server.IsMapValid(mapName))
                return false;
            // Valid but not a file in maps/ (other search path, addon). On a workshop server the
            // mounted current map validates the same way, and changelevel to it crashes.
            if (!IsWorkshopServer)
                return true;
            string current;
            try { current = Server.MapName; } catch { current = ""; }
            return !mapName.Equals(current, StringComparison.OrdinalIgnoreCase);
        }

        // Whether OnPlainChangelevelGuard takes over a console changelevel/map to this map (redirect
        // or block). The lifecycle hook uses the same test to skip announcing it.
        private bool IsGuardedChangelevelTarget(string target)
        {
            if (target == "" || !IsSafeMapName(target) || IsStockMap(target))
                return false;
            return IsWorkshopServer || BuildMapChangeCommand(target) != null;
        }

        /// <summary>
        /// The map name a map spec refers to, when it can be told: "workshop/&lt;id&gt;/&lt;name&gt;" -> name,
        /// "ws:&lt;name&gt;" -> name, a workshop id (bare, "ws/&lt;id&gt;", "workshop/&lt;id&gt;") -> the name
        /// it was seen with, a plain name -> itself. null for a workshop id we have not seen loaded.
        /// </summary>
        private string? MapSpecName(string spec)
        {
            spec = (spec ?? "").Trim();
            if (spec.StartsWith("ws:", StringComparison.OrdinalIgnoreCase))
                return spec["ws:".Length..].Trim();

            string? id = null;
            if (IsWorkshopId(spec))
                id = spec;
            else if (spec.StartsWith("ws/", StringComparison.OrdinalIgnoreCase) && IsWorkshopId(spec["ws/".Length..]))
                id = spec["ws/".Length..];
            else if (spec.StartsWith("workshop/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = spec.Split('/');
                if (parts.Length < 2 || !IsWorkshopId(parts[1]))
                    return spec;
                id = parts[1];
                // The "/<name>" part is typed by hand; the name the id actually loaded wins.
                string? seen = WorkshopMapNameForId(id);
                if (seen != null)
                    return seen;
                return parts.Length >= 3 && parts[2] != "" ? parts[2] : null;
            }

            if (id == null)
                return spec;
            return WorkshopMapNameForId(id);
        }

        private string? WorkshopMapNameForId(string id)
        {
            LearnCurrentWorkshopMap();
            foreach (var kv in _workshopMapIds)
            {
                if (kv.Value == id)
                    return kv.Key;
            }
            return null;
        }

        /// <summary>
        /// Whether a map spec (match config / rotation / backup entry, any format) is the map
        /// currently loaded. A workshop spec and the loaded map's plain name compare equal.
        /// </summary>
        private bool IsSameMap(string spec, string? currentMap)
        {
            if (string.IsNullOrEmpty(currentMap))
                return false;
            string? name = MapSpecName(spec);
            return name != null && name.Equals(currentMap, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The console command that loads a map spec, or null when it cannot be loaded.
        /// Workshop id forms -> host_workshop_map, "ws:&lt;name&gt;" -> ds_workshop_changelevel,
        /// a known workshop map -> host_workshop_map &lt;id&gt; (or ds_workshop_changelevel when only the
        /// name is known), a stock map -> changelevel, any other name on a server hosting a collection
        /// -> ds_workshop_changelevel.
        /// </summary>
        private string? BuildMapChangeCommand(string spec)
        {
            spec = (spec ?? "").Trim();
            if (spec == "")
                return null;
            EnsureWorkshopLaunchParsed();

            if (IsWorkshopId(spec))
                return $"host_workshop_map {spec}";
            if (spec.StartsWith("ws/", StringComparison.OrdinalIgnoreCase))
                return IsWorkshopId(spec["ws/".Length..]) ? $"host_workshop_map {spec["ws/".Length..]}" : null;
            if (spec.StartsWith("workshop/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = spec.Split('/');
                if (parts.Length < 2 || !IsWorkshopId(parts[1]))
                    return null;
                return $"host_workshop_map {parts[1]}";
            }

            string name = spec.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) ? spec["ws:".Length..].Trim() : spec;
            if (!IsSafeMapName(name))
                return null;
            if (spec.StartsWith("ws:", StringComparison.OrdinalIgnoreCase))
                return $"ds_workshop_changelevel \"{name}\"";

            if (IsKnownWorkshopMap(name))
            {
                if (_workshopMapIds.TryGetValue(name, out var id))
                    return $"host_workshop_map {id}";
                return $"ds_workshop_changelevel \"{name}\"";
            }

            if (IsStockMap(name))
                return $"changelevel \"{name}\"";

            // Not in the install: on a workshop server it can only be a collection map.
            if (_workshopCollectionId != null)
                return $"ds_workshop_changelevel \"{name}\"";

            return null;
        }

        // A plain console changelevel/map for a map that is not in the install would fail to mount
        // and crash the server with clients attached. This catches the engine's own match-end
        // changelevel (mp_match_end_restart 0 -> next mapgroup map by name) and other plugins, and
        // runs the workshop command instead.
        private HookResult OnPlainChangelevelGuard(CCSPlayerController? player, CommandInfo info)
        {
            // The boot "+map <empty>" lands here before the engine globals exist.
            if (player != null || !AreServerGlobalsReady())
                return HookResult.Continue;

            string target = CommandArg(info);

            try
            {
                if (!IsGuardedChangelevelTarget(target))
                    return HookResult.Continue;

                string? command = BuildMapChangeCommand(target);
                string verb = info.GetArg(0).ToLowerInvariant();
                if (command == null)
                {
                    Log($"[Workshop] Blocked '{verb} {target}': not in the install and no workshop id/collection to load it from (would crash the server).");
                    return HookResult.Stop;
                }

                Log($"[Workshop] '{verb} {target}' cannot load a workshop map; running '{command}' instead.");
                Server.NextFrame(() => Server.ExecuteCommand(command));
                return HookResult.Stop;
            }
            catch (Exception e)
            {
                Log($"[Workshop] changelevel guard: {e.Message}");
                return HookResult.Continue;
            }
        }
    }
}
