using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Vector3 = System.Numerics.Vector3;

namespace MatchZy
{
    // Practice extras modelled on what workshop practice maps offer:
    //   .throwsync [names]  the server throws saved lineups so that they all arrive at the same moment
    //   .nadespeed <10-500> grenades fly in slow (or fast) motion while one of yours is in the air
    //   .nextnade / .prevnade [type]  step through the saved lineups of this map
    public partial class MatchZy
    {
        // Seconds from the throw until an HE or a flashbang goes off (103 / 104 ticks measured).
        private const float SyncFuseSeconds = 1.6f;
        private const int SyncMaxLineups = 10;

        private static (string Key, ushort Item)? SyncGrenadeOf(string savedType) => (savedType ?? "").Trim().ToLowerInvariant() switch
        {
            "smoke" => ("smoke", (ushort)45),
            "flash" => ("flash", (ushort)43),
            "he" => ("hegrenade", (ushort)44),
            "molly" or "molotov" => ("molotov", (ushort)46),
            "incendiary" => ("incendiary", (ushort)48),
            "decoy" => ("decoy", (ushort)47),
            _ => null,
        };

        [ConsoleCommand("css_throwsync", "Practice: throw saved lineups so they arrive together")]
        public void OnThrowSyncCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (!HasCssTraceApi)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.throwsync.needs_trace"));
                return;
            }
            var wanted = FinderWords(command);
            var pawn = player.PlayerPawn?.Value;
            GrenadeSimLogic.TraceFn trace = (Vector3 a, Vector3 b, out Vector3 hit, out Vector3 normal, out float fraction) =>
                FinderGrenadeTrace(a, b, out hit, out normal, out fraction, pawn != null && pawn.IsValid ? pawn : null);
            float slope = ConVar.Find("weapon_molotov_maxdetonateslope")?.GetPrimitiveValue<float>() ?? 30f;
            float air = ConVar.Find("molotov_throw_detonate_time")?.GetPrimitiveValue<float>() ?? 2f;

            var picked = new List<(NadeLineup Lineup, string Key, ushort Item, Vector3 Pos, Vector3 Vel, float Time)>();
            int withoutThrow = 0;
            foreach (var l in LoadLineupsForCurrentMap(player).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (wanted.Count > 0 && !wanted.Any(w => string.Equals(w, l.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (SyncGrenadeOf(l.Type) is not (string key, ushort item))
                    continue;
                if (!LineupFormat.TryParse(l.NadePos, out float px, out float py, out float pz)
                    || !LineupFormat.TryParse(l.NadeVel, out float vx, out float vy, out float vz) || vx * vx + vy * vy + vz * vz < 1f)
                {
                    withoutThrow++;
                    continue;
                }
                var pos = new Vector3(px, py, pz);
                var vel = new Vector3(vx, vy, vz);
                // When it "arrives": a smoke or decoy where it comes to rest, a molotov where it
                // ignites, an HE or flash when its fuse runs out.
                float time;
                if (key is "hegrenade" or "flash")
                    time = FinderFuseSeconds(key == "flash" ? "flash" : "he");
                else
                {
                    var fuse = key is "molotov" or "incendiary" ? new GrenadeSimLogic.Fuse(air, MathF.Cos(slope * MathF.PI / 180f)) : default;
                    var flight = GrenadeSimLogic.Simulate(new GrenadeSimLogic.Launch(pos, vel), trace, 20f, 1, null, fuse);
                    if (!flight.Stopped)
                        continue;
                    time = flight.Time;
                }
                picked.Add((l, key, item, pos, vel, time));
                if (picked.Count == SyncMaxLineups)
                    break;
            }
            if (picked.Count == 0)
            {
                PrintToPlayerChat(player, withoutThrow > 0
                    ? Localizer.ForPlayer(player, "matchzy.pp.menu.throwsync.no_throw_stored", withoutThrow)
                    : Localizer.ForPlayer(player, "matchzy.pp.menu.throwsync.none"));
                return;
            }
            float longest = picked.Max(p => p.Time);
            int slot = player.Slot;
            foreach (var p in picked)
            {
                var item = p;
                float wait = longest - item.Time;
                void Go()
                {
                    var who = Utilities.GetPlayerFromSlot(slot);
                    if (who == null || !IsPlayerValid(who) || !isPractice)
                        return;
                    new GrenadeThrownData(new Vector(item.Pos.X, item.Pos.Y, item.Pos.Z), new QAngle(0, 0, 0), new Vector(item.Vel.X, item.Vel.Y, item.Vel.Z),
                        item.Lineup.Pos.PlayerPosition, item.Lineup.Pos.PlayerAngle, item.Key, DateTime.Now, item.Item, 0f, new Vector(600, 0, 0)).Throw(who);
                }
                if (wait < 0.02f)
                    Server.NextFrame(Go);
                else
                    AddTimer(wait, Go, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            }
            string thrownNames = string.Join(", ", picked.Select(p => Localizer.ForPlayer(player, "matchzy.pp.menu.throwsync.item", p.Lineup.Name, p.Time)));
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.throwsync.throwing", picked.Count, longest, thrownNames));
            if (withoutThrow > 0)
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.throwsync.left_out", withoutThrow));
        }

        // ---- .nadespeed: slow motion while a grenade of a player is in the air -----------------

        private float nadeSpeedScale = 1f;
        private bool nadeSpeedOn;
        private readonly List<(uint Handle, int Since)> nadeSpeedLive = new();

        [ConsoleCommand("css_nadespeed", "Practice: grenade flight speed in percent (10-500), slow motion while a grenade flies")]
        public void OnNadeSpeedCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            string? word = FinderWords(command).FirstOrDefault();
            if (word is null or "off" or "100" or "normal")
            {
                nadeSpeedScale = 1f;
                RestoreNadeSpeed();
                PrintToPlayerChat(player, word == null
                    ? Localizer.ForPlayer(player, "matchzy.pp.menu.nadespeed.normal_help")
                    : Localizer.ForPlayer(player, "matchzy.pp.menu.nadespeed.normal"));
                return;
            }
            if (!float.TryParse(word.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.nadespeed.usage"));
                return;
            }
            nadeSpeedScale = Math.Clamp(percent, 10f, 500f) / 100f;
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.nadespeed.set", nadeSpeedScale * 100f));
        }

        // From OnEntitySpawnedHandler: a real grenade of a player.
        private void OnNadeSpeedThrow(CBaseCSGrenadeProjectile projectile)
        {
            if (!isPractice || Math.Abs(nadeSpeedScale - 1f) < 0.001f || !projectile.IsValid)
                return;
            nadeSpeedLive.Add((projectile.EntityHandle.Raw, Server.TickCount));
            if (!nadeSpeedOn)
            {
                nadeSpeedOn = true;
                Server.ExecuteCommand(string.Format(CultureInfo.InvariantCulture, "host_timescale {0:0.###}", nadeSpeedScale));
            }
        }

        private void RestoreNadeSpeed()
        {
            nadeSpeedLive.Clear();
            if (!nadeSpeedOn)
                return;
            nadeSpeedOn = false;
            Server.ExecuteCommand("host_timescale 1");
        }

        private void OnNadeSpeedTick()
        {
            if (!nadeSpeedOn)
                return;
            try
            {
                if (!isPractice)
                {
                    RestoreNadeSpeed();
                    return;
                }
                int tick = Server.TickCount;
                for (int i = nadeSpeedLive.Count - 1; i >= 0; i--)
                {
                    var e = new CHandle<CBaseCSGrenadeProjectile>(nadeSpeedLive[i].Handle).Value;
                    bool over = e == null || !e.IsValid || tick - nadeSpeedLive[i].Since > 64 * 30;
                    if (!over && tick - nadeSpeedLive[i].Since > 20)
                    {
                        var v = e!.AbsVelocity;
                        over = v.X * v.X + v.Y * v.Y + v.Z * v.Z < 25f; // lying still (a smoke about to pop)
                    }
                    if (over)
                        nadeSpeedLive.RemoveAt(i);
                }
                if (nadeSpeedLive.Count == 0)
                    RestoreNadeSpeed();
            }
            catch (Exception e)
            {
                Log($"[NadeSpeed] {e.Message}");
                RestoreNadeSpeed();
            }
        }

        // ---- .nextnade / .prevnade: step through the saved lineups ------------------------------

        private readonly Dictionary<int, (int Index, string Filter)> nadeStep = new();

        [ConsoleCommand("css_nextnade", "Practice: go to the next saved lineup of this map (optional type: smoke flash he molly decoy)")]
        public void OnNextNadeCommand(CCSPlayerController? player, CommandInfo? command) => StepSavedNade(player, command, 1);

        [ConsoleCommand("css_prevnade", "Practice: go to the previous saved lineup of this map")]
        public void OnPrevNadeCommand(CCSPlayerController? player, CommandInfo? command) => StepSavedNade(player, command, -1);

        private void StepSavedNade(CCSPlayerController? player, CommandInfo? command, int step)
        {
            if (!RequirePractice(player) || player == null)
                return;
            nadeStep.TryGetValue(player.Slot, out var state);
            string filter = state.Filter ?? "";
            string? word = FinderWords(command).FirstOrDefault();
            if (word != null)
            {
                filter = word is "all" or "any" or "off" ? "" : word switch
                {
                    "he" or "nade" or "hegrenade" => "he",
                    "molly" or "molotov" or "inc" or "incendiary" => "mol",
                    "flash" or "flashbang" => "flash",
                    _ => word,
                };
                state.Index = step > 0 ? -1 : 0; // a new filter starts at its first (or last) lineup
            }
            var all = LoadLineupsForCurrentMap(player).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var list = filter == "" ? all : all.Where(l =>
            {
                string t = (l.Type ?? "").ToLowerInvariant();
                return t.StartsWith(filter, StringComparison.Ordinal) || (filter == "mol" && t.StartsWith("inc", StringComparison.Ordinal));
            }).ToList();
            if (list.Count == 0)
            {
                PrintToPlayerChat(player, filter == "" ? Localizer.ForPlayer(player, "matchzy.pp.menu.step.none") : Localizer.ForPlayer(player, "matchzy.pp.menu.step.none_type"));
                nadeStep[player.Slot] = (0, filter);
                return;
            }
            int index = ((state.Index + step) % list.Count + list.Count) % list.Count;
            nadeStep[player.Slot] = (index, filter);
            var lineup = list[index];
            string howThrown = lineup.Throw != "" ? " " + lineup.Throw : "";
            PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.step.current", index + 1, list.Count, lineup.Name, lineup.Type, howThrown));
            LoadNadeLineup(player, lineup); // also lineups of the shared pack
        }

        // ---- .savelast: save the throw the player just made, as it was thrown --------------------
        //
        // Every player is watched all the time (cheap: a few reads a tick): where they last stood
        // still, how far they moved on the ground since, whether they jumped, ducked or walked,
        // and which mouse buttons they used for this throw. When a grenade leaves the hand that
        // says what kind of throw it was and where it started, without the player telling us.
        private sealed class ThrowWatch
        {
            public int StillTicks;
            public Vector3 StillPos;
            public bool HasStill;
            public float GroundDistance;
            public Vector3 Prev;
            public bool HasPrev;
            public bool Jumped, Ducked, Walked, Left, Right, WasAttacking;
        }

        private sealed class ThrownInfo
        {
            public string NadeType = "";      // smoke, flash, hegrenade, molotov, incendiary, decoy
            public string Throw = "";         // as shown and stored: "Jumpthrow", "Step Throw (right click)" ...
            public Vector3 Spot;              // where the throw started (the last place they stood still)
            public bool HasSpot;
            public DateTime At;
        }

        private readonly Dictionary<int, ThrowWatch> throwWatch = new();
        private readonly Dictionary<int, ThrownInfo> lastThrownInfo = new();
        private const int ThrowStillTicks = 8;

        // Every tick in practice (from the finder tick).
        private void TickThrowWatch()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot || !player.PawnIsAlive)
                    continue;
                var pawn = player.PlayerPawn?.Value;
                var o = pawn?.AbsOrigin;
                if (pawn == null || !pawn.IsValid || o == null)
                    continue;
                if (!throwWatch.TryGetValue(player.Slot, out var w))
                    throwWatch[player.Slot] = w = new ThrowWatch();
                var pos = new Vector3(o.X, o.Y, o.Z);
                var buttons = player.Buttons;
                bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
                bool moveKeys = (buttons & (PlayerButtons.Forward | PlayerButtons.Back | PlayerButtons.Moveleft | PlayerButtons.Moveright)) != 0;
                bool jump = (buttons & PlayerButtons.Jump) != 0;
                float step = w.HasPrev ? GrenadeSimLogic.HorizontalDistance(pos, w.Prev) : 0f;
                w.Prev = pos;
                w.HasPrev = true;

                // Standing still on the ground, hands off the keys: this is where a throw starts.
                if (onGround && !moveKeys && !jump && step < 0.05f)
                {
                    if (++w.StillTicks >= ThrowStillTicks)
                    {
                        w.StillPos = pos;
                        w.HasStill = true;
                        w.GroundDistance = 0f;
                        w.Jumped = w.Ducked = w.Walked = false;
                    }
                }
                else
                {
                    w.StillTicks = 0;
                    if (onGround)
                        w.GroundDistance += step;
                    else
                        w.Jumped = true;
                    if (jump)
                        w.Jumped = true;
                    if (moveKeys && (buttons & PlayerButtons.Speed) != 0)
                        w.Walked = true;
                }
                if ((pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0)
                    w.Ducked = true;

                // The mouse buttons of this pin pull only: a new press starts over.
                bool attacking = (buttons & (PlayerButtons.Attack | PlayerButtons.Attack2)) != 0;
                if (attacking && !w.WasAttacking)
                    w.Left = w.Right = false;
                if ((buttons & PlayerButtons.Attack) != 0) w.Left = true;
                if ((buttons & PlayerButtons.Attack2) != 0) w.Right = true;
                w.WasAttacking = attacking;
            }
        }

        // From OnEntitySpawnedHandler: a real grenade of this player has left the hand.
        private void OnThrowWatchThrow(CCSPlayerController player, string nadeType)
        {
            if (!isPractice || !throwWatch.TryGetValue(player.Slot, out var w))
                return;
            // What the player did: stood (under 5 u on the ground), a step (under 50 u), walked
            // (walk key) or ran, with or without a jump, ducked or not.
            string kind = w.GroundDistance < 5f ? (w.Ducked ? "Crouch" : "")
                : w.GroundDistance < 50f ? "Step"
                : w.Walked ? "Walk" : "Run";
            string label = w.Jumped
                ? (kind == "" ? "Jumpthrow" : kind + " Jumpthrow")
                : (kind == "" ? "Normal Throw" : kind + " Throw");
            if (w.Left && w.Right) label += " (both buttons)";
            else if (w.Right) label += " (right click)";
            lastThrownInfo[player.Slot] = new ThrownInfo
            {
                NadeType = nadeType, Throw = label, Spot = w.StillPos, HasSpot = w.HasStill, At = DateTime.Now,
            };
        }

        [ConsoleCommand("css_savelast", "Practice: save the grenade you just threw as a lineup (.savelast [name])")]
        public void OnSaveLastCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || player == null)
                return;
            if (!player.UserId.HasValue || !lastGrenadesData.TryGetValue(player.UserId.Value, out var throws) || throws.Count == 0
                || !lastThrownInfo.TryGetValue(player.Slot, out var info) || (DateTime.Now - info.At).TotalMinutes > 10)
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.savelast.nothing"));
                return;
            }
            var thrown = throws[^1];
            string type = thrown.Type switch
            {
                "smoke" => "Smoke", "flash" => "Flash", "hegrenade" => "HE", "decoy" => "Decoy",
                "molotov" => "Molly", "incendiary" => "Incendiary", _ => "",
            };
            if (type == "")
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.savelast.unknown"));
                return;
            }
            // The spot: where they last stood still before the throw, when that is near the release
            // (a jump or a run-up starts there). Otherwise where they were at the release.
            var release = new Vector3(thrown.PlayerPosition.X, thrown.PlayerPosition.Y, thrown.PlayerPosition.Z);
            var spot = info.HasSpot && GrenadeSimLogic.HorizontalDistance(info.Spot, release) < 600f ? info.Spot : release;
            try
            {
                string map = Server.MapName;
                string steam = isSaveNadesAsGlobalEnabled ? "default" : player.SteamID.ToString();
                string path = Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel("savednades.json"));
                if (!File.Exists(path))
                    File.WriteAllText(path, "{}");
                var all = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(ReadSavedNadesJson(path))
                    ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                if (!all.TryGetValue(steam, out var mine))
                    all[steam] = mine = new Dictionary<string, Dictionary<string, string>>();

                // The name: what the player typed, else the grenade and a number ("smoke3").
                string name = string.Join(" ", FinderWords(command)).Trim();
                bool given = name != "";
                if (!given)
                {
                    // Numbered across every player and the shared pack, not per player: two
                    // players both got "smoke1", and .libadd of both overwrote one with the other.
                    name = LineupNameLogic.NextFree(type.ToLowerInvariant(), LineupNamesInUse(all, mine, map));
                }
                bool replaces = mine.ContainsKey(name);
                if (!replaces && mine.Count >= maxSavedNades)
                {
                    PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nadelimitreached", $"{maxSavedNades}"));
                    return;
                }
                mine[name] = new Dictionary<string, string>
                {
                    { "Position", LineupFormat.Format(spot.X, spot.Y, spot.Z) },
                    { "Angles", LineupFormat.Format(thrown.PlayerAngle.X, thrown.PlayerAngle.Y, 0f) },
                    { "Desc", "" },
                    { "Map", map },
                    { "Type", type },
                    { "Throw", info.Throw },
                    { "NadePos", LineupFormat.Format(thrown.Position.X, thrown.Position.Y, thrown.Position.Z) },
                    { "NadeVel", LineupFormat.Format(thrown.Velocity.X, thrown.Velocity.Y, thrown.Velocity.Z) },
                    { "ThrowFrom", LineupFormat.Format(release.X, release.Y, release.Z) },
                    { "Duck", thrown.DuckAmount.ToString("0.##", CultureInfo.InvariantCulture) },
                };
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(all, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                ShowNadeMarksAfterSave(player);
                PrintToPlayerChat(player, replaces
                    ? Localizer.ForPlayer(player, "matchzy.pp.menu.savelast.replaced", name, type, info.Throw)
                    : Localizer.ForPlayer(player, "matchzy.pp.menu.savelast.saved", name, type, info.Throw));
                PrintToPlayerChat(player, given
                    ? Localizer.ForPlayer(player, "matchzy.pp.menu.savelast.hint_named", name)
                    : Localizer.ForPlayer(player, "matchzy.pp.menu.savelast.hint_auto", name));
            }
            catch (Exception e)
            {
                Log($"[SaveLast] {e.GetType().Name}: {e.Message}");
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pp.menu.savelast.failed"));
            }
        }

        private void ForgetThrowWatch(int slot)
        {
            throwWatch.Remove(slot);
            lastThrownInfo.Remove(slot);
        }
    }
}
