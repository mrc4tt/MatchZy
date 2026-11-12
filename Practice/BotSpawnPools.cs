using System.Drawing;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Named bot spawn pools (practice). A pool is a list of spots; .placebot puts bots on random,
    // different spots from it, so every rep has a new setup.
    //   .botspawn <name>              - add your current spot (side, view direction, crouch) to a pool.
    //   .delbotspawn <name>           - delete a pool.
    //   .listbotspawn                 - list this map's pools.
    //   .placebot <number> <name>     - remove the current bots, then place that many on random spots.
    //   .placenewbot <number> <name>  - same, but keep the current bots.
    //   .kicklastbot                  - remove the most recently added practice bot.
    //   .showbotspawn                 - toggle green boxes on every saved spot; stand in one and
    //                                   press USE (E) to delete that spot.
    // Stored per map in botspawns.json next to botpositions.json, reusing its BotPos record.
    public partial class MatchZy
    {
        private string BotSpawnsPath =>
            Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("botspawns.json"));

        private bool _botSpawnsLoadFailed = false;

        private Dictionary<string, Dictionary<string, List<BotPos>>> LoadBotSpawns()
        {
            try
            {
                if (!File.Exists(BotSpawnsPath))
                {
                    _botSpawnsLoadFailed = false;
                    return new();
                }
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<BotPos>>>>(File.ReadAllText(BotSpawnsPath), BotPosJsonOpts) ?? new();
                // Valid JSON can still hold nulls (a null map, pool or spot); the commands dereference
                // every entry, so drop null maps/spots and treat a null pool as empty.
                foreach (var map in loaded.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList())
                    loaded.Remove(map);
                foreach (var pools in loaded.Values)
                    foreach (var pool in pools.Keys.ToList())
                    {
                        var spots = pools[pool] ?? new List<BotPos>();
                        spots.RemoveAll(b => b == null);
                        pools[pool] = spots;
                    }
                _botSpawnsLoadFailed = false;
                return loaded;
            }
            catch (Exception e)
            {
                Log($"[BotSpawns] load: {e.Message}");
                if (!_botSpawnsLoadFailed)
                    BackupCorruptJsonFile(BotSpawnsPath, "BotSpawns");
                _botSpawnsLoadFailed = true;
                return new();
            }
        }

        // Returns false (and writes nothing) when the file on disk failed to parse on the last load.
        private bool SaveBotSpawns(Dictionary<string, Dictionary<string, List<BotPos>>> data)
        {
            if (_botSpawnsLoadFailed)
            {
                Log($"[BotSpawns] save refused: {BotSpawnsPath} failed to parse. Fix or remove it first.");
                return false;
            }
            File.WriteAllText(BotSpawnsPath, JsonSerializer.Serialize(data, BotPosJsonOpts));
            return true;
        }

        private Dictionary<string, List<BotPos>>? MapBotSpawns(Dictionary<string, Dictionary<string, List<BotPos>>> data)
            => data.TryGetValue(Server.MapName, out var pools) ? pools : null;

        // Pool names are matched case-insensitively.
        private static string? FindPoolName(Dictionary<string, List<BotPos>> pools, string name)
            => pools.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

        [ConsoleCommand("css_botspawn", "Add your current spot to a named bot spawn pool")]
        public void OnBotSpawnConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotSpawnCommand(player, command.ArgString);

        public void HandleBotSpawnCommand(CCSPlayerController? player, string name)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player) || player!.PlayerPawn.Value?.AbsOrigin == null)
                return;
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Contains(' '))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".botspawn <name>"));
                return;
            }
            if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.bp.jointeam"));
                return;
            }
            var pawn = player.PlayerPawn.Value;
            var o = pawn.AbsOrigin!;
            bool crouch = false;
            if (pawn.MovementServices != null && pawn.MovementServices.Handle != IntPtr.Zero)
                crouch = new CCSPlayer_MovementServices(pawn.MovementServices.Handle).DuckAmount >= 0.5f;
            try
            {
                var data = LoadBotSpawns();
                if (!data.TryGetValue(Server.MapName, out var pools))
                {
                    pools = new();
                    data[Server.MapName] = pools;
                }
                string key = FindPoolName(pools, name) ?? name;
                if (!pools.TryGetValue(key, out var spots))
                {
                    spots = new();
                    pools[key] = spots;
                }
                spots.Add(new BotPos { X = o.X, Y = o.Y, Z = o.Z, Pitch = 0, Yaw = pawn.EyeAngles.Y, Team = player.TeamNum, Crouch = crouch });
                if (!SaveBotSpawns(data))
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnunreadable"));
                    return;
                }
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnadded", key, spots.Count));
                if (_botSpawnVizOn)
                    DrawBotSpawnMarkers();
            }
            catch (Exception e)
            {
                Log($"[BotSpawns] save: {e.Message}");
            }
        }

        [ConsoleCommand("css_delbotspawn", "Delete a named bot spawn pool")]
        public void OnDelBotSpawnConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleDelBotSpawnCommand(player, command.ArgString);

        public void HandleDelBotSpawnCommand(CCSPlayerController? player, string name)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".delbotspawn <name>"));
                return;
            }
            try
            {
                var data = LoadBotSpawns();
                var pools = MapBotSpawns(data);
                string? key = pools == null ? null : FindPoolName(pools, name);
                if (pools == null || key == null)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnnotfound", name));
                    return;
                }
                pools.Remove(key);
                if (!SaveBotSpawns(data))
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnunreadable"));
                    return;
                }
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawndeleted", key));
                if (_botSpawnVizOn)
                    DrawBotSpawnMarkers();
            }
            catch (Exception e)
            {
                Log($"[BotSpawns] delete: {e.Message}");
            }
        }

        [ConsoleCommand("css_listbotspawn", "List the bot spawn pools on this map")]
        public void OnListBotSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            var pools = MapBotSpawns(LoadBotSpawns());
            if (pools == null || pools.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnnone"));
                return;
            }
            string list = string.Join(", ", pools.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} ({kv.Value.Count})"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnlist", list));
        }

        [ConsoleCommand("css_placebot", "Replace the practice bots with bots on random spots of a pool")]
        public void OnPlaceBotConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandlePlaceBotCommand(player, command.ArgString, keepExisting: false);

        [ConsoleCommand("css_placenewbot", "Add bots on random spots of a pool, keeping the current bots")]
        public void OnPlaceNewBotConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandlePlaceBotCommand(player, command.ArgString, keepExisting: true);

        public void HandlePlaceBotCommand(CCSPlayerController? player, string args, bool keepExisting)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            string usage = keepExisting ? ".placenewbot <number> <name>" : ".placebot <number> <name>";
            var parts = (args ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // "<number> <name>", also accepted the other way round.
            int count = 0;
            string name = "";
            if (parts.Length == 2 && int.TryParse(parts[0], out count))
                name = parts[1];
            else if (parts.Length == 2 && int.TryParse(parts[1], out count))
                name = parts[0];
            if (name.Length == 0 || count < 1)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", usage));
                return;
            }
            var pools = MapBotSpawns(LoadBotSpawns());
            string? key = pools == null ? null : FindPoolName(pools, name);
            if (pools == null || key == null || pools[key].Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnnotfound", name));
                return;
            }

            // Random distinct spots, capped by the pool size and the practice bot limit.
            var spots = pools[key].OrderBy(_ => Random.Shared.Next()).ToList();
            // Every bot gets its own spot, so a pool with fewer spots than asked for caps the count.
            if (count > spots.Count)
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnfewspots", key, spots.Count, count));
            int existing = keepExisting ? TrackedPracticeBots().Count : 0;
            int room = Math.Max(0, MaxPracticeBots - existing);
            count = Math.Min(Math.Min(count, spots.Count), room);
            if (count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.prac.maxbots", MaxPracticeBots));
                return;
            }
            spots = spots.Take(count).ToList();

            float delay = 0.0f;
            if (!keepExisting && TrackedPracticeBots().Count > 0)
            {
                // Same as .nobot, then give the kicks a moment before adding new bots.
                OnNoBotsCommand(player, null);
                delay = 0.6f;
            }
            var owner = player!;
            if (delay > 0)
                AddTimer(delay, () => SpawnBotPositionsSequentially(owner, spots, 0, 0), TimerFlags.STOP_ON_MAPCHANGE);
            else
                SpawnBotPositionsSequentially(owner, spots, 0, 0);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botsplaced", count, key));
        }

        [ConsoleCommand("css_kicklastbot", "Remove the most recently added practice bot")]
        public void OnKickLastBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            int lastUserId;
            int remaining;
            lock (_botsDictLock)
            {
                if (pracUsedBots.Count == 0)
                {
                    lastUserId = -1;
                    remaining = 0;
                }
                else
                {
                    // Newest = highest claim sequence. Not the highest userid: userids follow the
                    // slot, which a new bot reuses after a kick. Entries without a sequence sort first.
                    lastUserId = pracUsedBots
                        .OrderByDescending(kv => kv.Value.TryGetValue(PracBotSpawnSeqKey, out var seq) && seq is long n ? n : long.MinValue)
                        .ThenByDescending(kv => kv.Key)
                        .First().Key;
                    pracUsedBots.Remove(lastUserId);
                    remaining = pracUsedBots.Count;
                }
            }
            if (lastUserId < 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.nobottokick"));
                return;
            }
            // Pin the quota first, else bot_quota_mode normal refills the slot with a new bot.
            Server.ExecuteCommand($"bot_quota {remaining}");
            Server.ExecuteCommand($"kickid {lastUserId}");
            ForgetBotCombatState(lastUserId);
            ForgetBotLoadout(lastUserId);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.kickedlastbot"));
        }

        // ── .showbotspawn ─────────────────────────────────────────────────────────────────────────
        private bool _botSpawnVizOn;
        private readonly List<uint> botSpawnMarkerEntities = new();
        // One entry per drawn box: the pool, its index in the pool and where the box is.
        private readonly List<(string Pool, int Index, float X, float Y, float FloorZ)> botSpawnMarkers = new();
        private readonly Dictionary<int, float> lastBotSpawnDeleteTime = new();

        [ConsoleCommand("css_showbotspawn", "Toggle green boxes on the saved bot spawn spots")]
        public void OnShowBotSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            if (_botSpawnVizOn)
            {
                ClearBotSpawnMarkers();
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnvizoff"));
                return;
            }
            // Next frame: file read + entity creation off the client's command processing.
            Server.NextFrame(() =>
            {
                if (!isPractice || !IsPlayerValid(player))
                    return;
                int shown = DrawBotSpawnMarkers();
                ReplyToUserCommand(player, Localizer.ForPlayer(player, shown > 0 ? "matchzy.px.botspawnvizon" : "matchzy.px.botspawnnone", shown));
            });
        }

        private int DrawBotSpawnMarkers()
        {
            RemoveBotSpawnMarkerEntities();
            var pools = MapBotSpawns(LoadBotSpawns());
            if (pools != null)
            {
                foreach (var (pool, spots) in pools)
                {
                    for (int i = 0; i < spots.Count; i++)
                    {
                        var bp = spots[i];
                        float floorZ = SpawnMarkerFloorZ(new Vector(bp.X, bp.Y, bp.Z));
                        botSpawnMarkerEntities.AddRange(DrawFloorBox(bp.X, bp.Y, floorZ + spawnMarkerFloorLift, spawnMarkerHalfSize, Color.Lime));
                        botSpawnMarkers.Add((pool, i, bp.X, bp.Y, floorZ));
                    }
                }
            }
            _botSpawnVizOn = botSpawnMarkers.Count > 0;
            return botSpawnMarkers.Count;
        }

        private void RemoveBotSpawnMarkerEntities()
        {
            foreach (uint raw in botSpawnMarkerEntities)
            {
                var ent = new CHandle<CBaseEntity>(raw).Value;
                if (ent != null && ent.IsValid)
                    SafeRemoveEntity(ent, "botspawnmarker");
            }
            botSpawnMarkerEntities.Clear();
            botSpawnMarkers.Clear();
        }

        private void ClearBotSpawnMarkers()
        {
            RemoveBotSpawnMarkerEntities();
            _botSpawnVizOn = false;
        }

        // After a map change the entities are gone: forget them without touching freed memory.
        private void ForgetBotSpawnMarkersOnMapStart()
        {
            botSpawnMarkerEntities.Clear();
            botSpawnMarkers.Clear();
            _botSpawnVizOn = false;
        }

        // OnPlayerButtonsChanged: USE while standing in a green box deletes that spot.
        private void OnBotSpawnMarkerButtonHandler(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if (!_botSpawnVizOn || !isPractice || (pressed & PlayerButtons.Use) == 0)
                return;
            if (!IsHumanPlayerValid(player) || !player.UserId.HasValue)
                return;
            var origin = player.PlayerPawn.Value?.AbsOrigin;
            if (origin == null)
                return;
            int uid = player.UserId.Value;
            float now = Server.CurrentTime;
            if (lastBotSpawnDeleteTime.TryGetValue(uid, out float last) && now - last < 0.5f)
                return;
            // This USE press teleports to a .showspawns marker: don't also delete a bot spot.
            if (spawnMarkersActive && TargetSpawnMarkerIndex(player) >= 0)
                return;

            int best = -1;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < botSpawnMarkers.Count; i++)
            {
                var m = botSpawnMarkers[i];
                float dx = origin.X - m.X, dy = origin.Y - m.Y, dz = origin.Z - m.FloorZ;
                if (Math.Abs(dx) > spawnMarkerHalfSize || Math.Abs(dy) > spawnMarkerHalfSize || Math.Abs(dz) > spawnMarkerStandInHeight)
                    continue;
                float d = dx * dx + dy * dy;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    best = i;
                }
            }
            if (best < 0)
                return;
            lastBotSpawnDeleteTime[uid] = now;
            var target = botSpawnMarkers[best];
            // Off the input callback (rule #4); file I/O and entity work on the next frame.
            Server.NextFrame(() => DeleteBotSpawnSpot(player, target.Pool, target.X, target.Y));
        }

        private void DeleteBotSpawnSpot(CCSPlayerController player, string pool, float x, float y)
        {
            try
            {
                var data = LoadBotSpawns();
                var pools = MapBotSpawns(data);
                if (pools == null || !pools.TryGetValue(pool, out var spots))
                    return;
                // Match by position, not index: the file may have changed since the boxes were drawn.
                int index = spots.FindIndex(s => Math.Abs(s.X - x) < 0.5f && Math.Abs(s.Y - y) < 0.5f);
                if (index < 0)
                    return;
                spots.RemoveAt(index);
                if (spots.Count == 0)
                    pools.Remove(pool);
                if (!SaveBotSpawns(data))
                    return;
                DrawBotSpawnMarkers();
                if (IsHumanPlayerValid(player))
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botspawnspotdeleted", pool, spots.Count));
            }
            catch (Exception e)
            {
                Log($"[BotSpawns] delete spot: {e.Message}");
            }
        }
    }
}
