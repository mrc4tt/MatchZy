using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Practice path recording and bot replay (one recording and one replay bot per player).
    //   .rec                       - start recording your movement; .rec again stops it.
    //   .replay [t|ct] [loop]      - a practice bot walks your last recording (default: the team you
    //                                recorded on). loop repeats it until .replay stop.
    //   .replay stop               - stop the replay and remove its bot.
    //   .saverec / .loadrec / .listrec / .delrec <name>
    //                              - keep recordings per map in pathrecordings/<map>.json.
    //
    // A recording is the pawn's position, eye angles, velocity and crouch state on every tick, plus
    // the grenades thrown while recording (the same throw data .rt uses, tagged with the tick). The
    // replay bot is a normal tracked practice bot (pracUsedBots, so the quota/kick logic leaves it
    // alone) that is teleported along the frames every tick with the recorded velocity. The jiggle
    // and .botshoot ticks skip it while it replays. The body is teleported with yaw only (a pitched
    // teleport tilts the model, issue #10); the pitch goes through setang, only when it moved.
    // Before each throw the bot draws that grenade (SelectItem redeploy, never a plain select: on a
    // grenade that throws it) and re-throws it on its tick from the recorded release point; a moment
    // later it goes back to its gun. The throw animation itself is not played.
    public partial class MatchZy
    {
        private readonly record struct PathFrame(float X, float Y, float Z, float Pitch, float Yaw, float VX, float VY, float VZ, bool Duck);

        private sealed class PathRecording
        {
            public required string MapName { get; init; }
            public required CsTeam Team { get; init; }
            public List<PathFrame> Frames { get; } = new();
            // Grenades thrown while recording, in throw order, keyed by the frame index they left the hand.
            public List<(int Frame, GrenadeThrownData Nade)> Throws { get; } = new();
        }

        private sealed class PathReplayState
        {
            public required PathRecording Recording { get; init; }
            public required ulong OwnerSteamId { get; init; }
            public bool Loop;
            public int Frame;
            public int NextThrow;
            // Throw index the grenade was drawn for (-1 = none), and the frame to go back to the gun.
            public int DrawnFor = -1;
            public int HolsterAt = -1;
            // Ticks spent waiting for a dead bot to respawn before the first frame.
            public int WaitTicks;
            public bool? LastDuck;
            public float LastPitch = float.NaN;
            public float LastYaw = float.NaN;
            // A role of a .strat playback: the bot stays where its path ends (no kick, no message);
            // .strat stop removes the strategy's bots.
            public bool StratRole;
        }

        // 60 seconds at 64 tick.
        private const int MaxPathFrames = 64 * 60;
        // Below this speed (units/s) a frame counts as standing still when trimming the ends.
        private const float PathIdleSpeed = 5.0f;
        // The grenade is drawn this many ticks before its throw (the deploy animation takes ~0.3 s)
        // and put away this many ticks after.
        private const int ReplayNadeDrawLead = 24;
        private const int ReplayNadeHolsterDelay = 16;
        // A replay bot that is dead at the start (killed while spawning, .botrespawn pending) is
        // waited for this long before the replay gives up.
        private const int ReplayRespawnWaitTicks = 64 * 3;

        // Per recorder steamid: the recording in progress.
        private readonly Dictionary<ulong, PathRecording> activePathRecordings = new();
        // Per player steamid: the last finished (or loaded) recording.
        private readonly Dictionary<ulong, PathRecording> lastPathRecordings = new();
        // Per bot userid: the replay it is walking.
        private readonly Dictionary<int, PathReplayState> activePathReplays = new();
        // Per player steamid: the bot userid used for their replays, reused by the next .replay.
        private readonly Dictionary<ulong, int> pathReplayBots = new();

        private readonly Vector _replayPos = new(0, 0, 0);
        private readonly Vector _replayVel = new(0, 0, 0);
        private readonly QAngle _replayAng = new(0, 0, 0);

        private bool IsBotReplayingPath(int botUserId) => activePathReplays.ContainsKey(botUserId);

        // Practice start and end (ResetPracticeExtras).
        private void ResetPathReplay()
        {
            ClearRecDisplays();
            activePathRecordings.Clear();
            // A .rec countdown froze its player (MOVETYPE_NONE); its unfreeze timer now sees no
            // pending entry and returns, so undo the freeze here (fresh lookup, the player may be gone).
            foreach (ulong steamId in pendingRecCountdowns.Keys)
                UnfreezeStratPlayer(Utilities.GetPlayerFromSteamId(steamId));
            pendingRecCountdowns.Clear();
            lastPathRecordings.Clear();
            activePathReplays.Clear();
            pathReplayBots.Clear();
        }

        // Per recorder steamid: the countdown generation of a .rec that has not started yet.
        private readonly Dictionary<ulong, int> pendingRecCountdowns = new();
        private int recCountdownGeneration;

        [ConsoleCommand("css_rec", "Start or stop recording your movement path")]
        [ConsoleCommand("css_record", "Start or stop recording your movement path")]
        public void OnRecConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleRecCommand(player, command.ArgString);

        public void HandleRecCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            ulong steamId = player!.SteamID;

            if (activePathRecordings.TryGetValue(steamId, out var running))
            {
                activePathRecordings.Remove(steamId);
                FinishPathRecording(player, steamId, running);
                return;
            }
            // .rec during the countdown cancels it.
            if (pendingRecCountdowns.Remove(steamId))
            {
                UnfreezeStratPlayer(player);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.reccancelled"));
                return;
            }
            if (IsStratRole(steamId) || IsSlotRecording(steamId))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.busy"));
                return;
            }

            if (!player.PawnIsAlive || (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.notalive"));
                return;
            }

            // Frozen for a 3 second countdown, so every take starts the same way.
            int generation = ++recCountdownGeneration;
            pendingRecCountdowns[steamId] = generation;
            Server.NextFrame(() => FreezeStratPlayer(player));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.reccountdown", StratCountdownSeconds));
            for (int left = StratCountdownSeconds; left >= 1; left--)
            {
                int secondsLeft = left;
                AddTimer(StratCountdownSeconds - left, () =>
                {
                    if (pendingRecCountdowns.TryGetValue(steamId, out int g) && g == generation && IsHumanPlayerValid(player))
                        PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.st.countdown", secondsLeft));
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            AddTimer(StratCountdownSeconds, () =>
            {
                if (!pendingRecCountdowns.TryGetValue(steamId, out int g) || g != generation)
                    return;
                pendingRecCountdowns.Remove(steamId);
                UnfreezeStratPlayer(player);
                if (!isPractice || !IsHumanPlayerValid(player) || !player.PawnIsAlive)
                    return;
                activePathRecordings[steamId] = new PathRecording { MapName = Server.MapName, Team = player.Team };
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.recstart", MaxPathFrames / 64));
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        // Called from RecordThrownNade for every recorded throw: tag it with the current tick of the
        // thrower's running .rec, if any.
        private void AddThrowToPathRecording(int userId, GrenadeThrownData nade)
        {
            if (activePathRecordings.Count == 0 && activeStrat == null)
                return;
            var player = Utilities.GetPlayerFromUserid(userId);
            if (player == null || !player.IsValid || player.IsBot)
                return;
            AddThrowToStratRecording(player.SteamID, nade);
            if (!activePathRecordings.TryGetValue(player.SteamID, out var recording))
                return;
            int frame = Math.Max(0, recording.Frames.Count - 1);
            recording.Throws.Add((frame, nade));
        }

        private void FinishPathRecording(CCSPlayerController? player, ulong steamId, PathRecording recording)
        {
            TrimIdleFrames(recording);
            if (recording.Frames.Count < 2)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.recempty"));
                return;
            }
            lastPathRecordings[steamId] = recording;
            RefreshRecDisplay(steamId);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.recsaved", PathSeconds(recording)));
        }

        private static string PathSeconds(PathRecording recording)
            => (recording.Frames.Count / 64.0f).ToString("0.0", CultureInfo.InvariantCulture);

        // Drops the standing-still frames before the first and after the last movement or throw, so
        // the replay starts at once. One idle frame is kept on each side as the start/end spot.
        // A throw counts as activity, so a throw from a standing spot is kept.
        private static void TrimIdleFrames(PathRecording recording)
        {
            var frames = recording.Frames;
            var throws = recording.Throws;
            static bool Moving(PathFrame f) => f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ > PathIdleSpeed * PathIdleSpeed;
            int first = frames.FindIndex(Moving);
            int last = frames.FindLastIndex(Moving);
            if (throws.Count > 0 && frames.Count > 0)
            {
                int firstThrow = Math.Min(throws[0].Frame, frames.Count - 1);
                int lastThrow = Math.Min(throws[^1].Frame, frames.Count - 1);
                first = first < 0 ? firstThrow : Math.Min(first, firstThrow);
                last = Math.Max(last, lastThrow);
            }
            if (first < 0)
            {
                frames.Clear();
                throws.Clear();
                return;
            }
            // Keep enough lead-in before the first throw for the bot to draw the grenade.
            int lead = throws.Count > 0 && throws[0].Frame <= first ? ReplayNadeDrawLead : 1;
            int start = Math.Max(0, first - lead);
            int end = Math.Min(frames.Count - 1, last + 1);
            frames.RemoveRange(end + 1, frames.Count - end - 1);
            frames.RemoveRange(0, start);
            for (int i = 0; i < throws.Count; i++)
                throws[i] = (Math.Clamp(throws[i].Frame - start, 0, frames.Count - 1), throws[i].Nade);
        }

        // .play / css_play: registered on OnUnCoachCommand (Teams.cs), which hands it to this in practice.
        [ConsoleCommand("css_replay", "Replay your last recorded path with a bot")]
        public void OnReplayConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleReplayCommand(player, command.ArgString);

        public void HandleReplayCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            ulong steamId = player!.SteamID;
            var tokens = arg.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 1 && tokens[0] == "stop")
            {
                if (pathReplayBots.TryGetValue(steamId, out int stopUid) && activePathReplays.ContainsKey(stopUid))
                {
                    EndPathReplay(stopUid, announce: false, kickBot: true);
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.stopped"));
                }
                else
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.notreplaying"));
                return;
            }

            if (activePathRecordings.ContainsKey(steamId))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.stillrecording"));
                return;
            }
            if (!lastPathRecordings.TryGetValue(steamId, out var recording))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.norecording"));
                return;
            }
            if (!IsSameMap(recording.MapName, Server.MapName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.othermap", recording.MapName));
                return;
            }

            CsTeam team = recording.Team;
            bool loop = false;
            foreach (string t in tokens)
            {
                if (t == "t")
                    team = CsTeam.Terrorist;
                else if (t == "ct")
                    team = CsTeam.CounterTerrorist;
                else if (t == "loop")
                    loop = true;
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".replay [t|ct] [loop] | .replay stop"));
                    return;
                }
            }

            // Reuse the bot of the previous replay when it is still there, alive and on that team.
            if (pathReplayBots.TryGetValue(steamId, out int uid))
            {
                var bot = Utilities.GetPlayerFromUserid(uid);
                bool tracked;
                lock (_botsDictLock)
                    tracked = pracUsedBots.ContainsKey(uid);
                if (tracked && IsPlayerValid(bot) && bot!.PawnIsAlive && bot.Team == team)
                {
                    StartPathReplay(player, uid, recording, loop);
                    return;
                }
                activePathReplays.Remove(uid);
                pathReplayBots.Remove(steamId);
            }

            if (IsNoBotsFlagSet())
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }
            if (isSpawningBot || !CanSpawnAnotherBot(player))
                return;

            HashSet<int> before;
            lock (_botsDictLock)
                before = new HashSet<int>(pracUsedBots.Keys);
            PathFrame f0 = recording.Frames[0];
            var startPos = new Position(new Vector(f0.X, f0.Y, f0.Z), new QAngle(0, f0.Yaw, 0));
            AddBot(player, false, team, false, startPos);
            AwaitReplayBot(player, steamId, recording, before, loop, 0);
        }

        // AddBot claims the bot over a few timers (pair spawns, retries). Watch pracUsedBots for the
        // new bot of this owner, then start the replay on it.
        private void AwaitReplayBot(CCSPlayerController owner, ulong steamId, PathRecording recording, HashSet<int> before, bool loop, int attempt)
        {
            AddTimer(0.15f, () =>
            {
                if (!isPractice || !IsHumanPlayerValid(owner))
                    return;
                int? claimed = null;
                lock (_botsDictLock)
                {
                    foreach (var (botUid, data) in pracUsedBots)
                    {
                        if (before.Contains(botUid))
                            continue;
                        if (data.TryGetValue("owner", out var o) && o is CCSPlayerController c && c.IsValid && c.SteamID == steamId)
                        {
                            claimed = botUid;
                            break;
                        }
                    }
                }
                if (claimed == null)
                {
                    if (attempt < 12)
                        AwaitReplayBot(owner, steamId, recording, before, loop, attempt + 1);
                    else
                        ReplyToUserCommand(owner, Localizer.ForPlayer(owner, "matchzy.rp.nobot"));
                    return;
                }
                pathReplayBots[steamId] = claimed.Value;
                StartPathReplay(owner, claimed.Value, recording, loop);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void StartPathReplay(CCSPlayerController owner, int botUid, PathRecording recording, bool loop)
        {
            activePathReplays[botUid] = new PathReplayState { Recording = recording, OwnerSteamId = owner.SteamID, Loop = loop };
            ReplyToUserCommand(owner, Localizer.ForPlayer(owner, loop ? "matchzy.rp.replayingloop" : "matchzy.rp.replaying", PathSeconds(recording)));
        }

        // OnTick: sample the recorders, move the replay bots.
        public void OnPathReplayTick()
        {
            if (!isPractice || (activePathRecordings.Count == 0 && activePathReplays.Count == 0))
                return;
            try
            {
                if (activePathRecordings.Count > 0)
                    SamplePathRecordings();
                if (activePathReplays.Count > 0)
                    StepPathReplays();
            }
            catch (Exception e)
            {
                Log($"[PathReplay] {e.Message}");
            }
        }

        private List<ulong>? _pathStopList;

        private void SamplePathRecordings()
        {
            _pathStopList?.Clear();
            foreach (var (steamId, recording) in activePathRecordings)
            {
                var player = Utilities.GetPlayerFromSteamId(steamId);
                if (recording.Frames.Count >= MaxPathFrames || !TryCapturePathFrame(player, out var frame))
                {
                    (_pathStopList ??= new()).Add(steamId);
                    continue;
                }
                recording.Frames.Add(frame);
            }
            if (_pathStopList == null || _pathStopList.Count == 0)
                return;
            // Death, team change, disconnect or the length cap ends the recording.
            foreach (ulong steamId in _pathStopList)
            {
                var recording = activePathRecordings[steamId];
                activePathRecordings.Remove(steamId);
                var player = Utilities.GetPlayerFromSteamId(steamId);
                FinishPathRecording(IsHumanPlayerValid(player) ? player : null, steamId, recording);
            }
        }

        // This tick's position, view, velocity and crouch of a living human, for .rec and .stratrec.
        private bool TryCapturePathFrame(CCSPlayerController? player, out PathFrame frame)
        {
            frame = default;
            var pawn = player?.PlayerPawn.Value;
            if (!IsHumanPlayerValid(player) || !player!.PawnIsAlive || pawn == null || pawn.AbsOrigin == null || pawn.AbsVelocity == null)
                return false;
            Vector o = pawn.AbsOrigin;
            Vector v = pawn.AbsVelocity;
            QAngle e = pawn.EyeAngles;
            bool duck = false;
            if (pawn.MovementServices != null && pawn.MovementServices.Handle != IntPtr.Zero)
                duck = new CCSPlayer_MovementServices(pawn.MovementServices.Handle).DuckAmount >= 0.5f;
            frame = new PathFrame(o.X, o.Y, o.Z, e.X, e.Y, v.X, v.Y, v.Z, duck);
            return true;
        }

        private List<int>? _replayDoneList;

        private void StepPathReplays()
        {
            _replayDoneList?.Clear();
            foreach (var (uid, state) in activePathReplays)
            {
                var bot = Utilities.GetPlayerFromUserid(uid);
                var pawn = bot?.PlayerPawn.Value;
                // A dead bot at the very start (died while spawning, .botrespawn brings it back) is
                // waited for; a dead, kicked or gone bot later on ends the replay.
                if (!IsPlayerValid(bot) || !bot!.PawnIsAlive || pawn == null)
                {
                    bool present = bot != null && bot.IsValid;
                    if (present && state.Frame == 0 && ++state.WaitTicks < ReplayRespawnWaitTicks)
                        continue;
                    (_replayDoneList ??= new()).Add(uid);
                    continue;
                }
                var frames = state.Recording.Frames;
                var throws = state.Recording.Throws;
                if (state.Frame >= frames.Count)
                {
                    if (!state.Loop)
                    {
                        (_replayDoneList ??= new()).Add(uid);
                        continue;
                    }
                    state.Frame = 0;
                    state.NextThrow = 0;
                    state.DrawnFor = -1;
                }
                int frameIndex = state.Frame++;
                PathFrame f = frames[frameIndex];
                _replayPos.X = f.X; _replayPos.Y = f.Y; _replayPos.Z = f.Z;
                _replayVel.X = f.VX; _replayVel.Y = f.VY; _replayVel.Z = f.VZ;
                _replayAng.X = 0; _replayAng.Y = f.Yaw; _replayAng.Z = 0;
                pawn.Teleport(_replayPos, _replayAng, _replayVel);
                if (float.IsNaN(state.LastPitch) || Math.Abs(state.LastPitch - f.Pitch) > 0.5f || Math.Abs(state.LastYaw - f.Yaw) > 0.5f)
                {
                    state.LastPitch = f.Pitch;
                    state.LastYaw = f.Yaw;
                    bot.ExecuteClientCommandFromServer(string.Create(CultureInfo.InvariantCulture, $"setang {f.Pitch:R} {f.Yaw:R} 0"));
                }
                if (state.LastDuck != f.Duck)
                {
                    state.LastDuck = f.Duck;
                    SetReplayBotDuck(bot, f.Duck);
                }

                // Draw the next grenade shortly before its throw, so the bot holds it when it flies.
                int next = state.NextThrow;
                if (next < throws.Count && state.DrawnFor < next && throws[next].Frame - ReplayNadeDrawLead <= frameIndex)
                {
                    state.DrawnFor = next;
                    state.HolsterAt = -1;
                    DrawReplayNade(bot, throws[next].Nade.Type);
                }
                // Re-throw the grenades released on this tick, from the recorded release point.
                while (state.NextThrow < throws.Count && throws[state.NextThrow].Frame <= frameIndex)
                {
                    throws[state.NextThrow++].Nade.Throw(bot, SmokeColorForThrow(bot));
                    state.HolsterAt = frameIndex + ReplayNadeHolsterDelay;
                }
                // Back to the gun after the throw, unless the next grenade is about to be drawn.
                if (state.HolsterAt >= 0 && frameIndex >= state.HolsterAt)
                {
                    state.HolsterAt = -1;
                    bool nextSoon = state.NextThrow < throws.Count && throws[state.NextThrow].Frame - ReplayNadeDrawLead <= frameIndex + 1;
                    if (!nextSoon)
                        HolsterReplayNade(bot);
                }
            }
            if (_replayDoneList == null || _replayDoneList.Count == 0)
                return;
            foreach (int uid in _replayDoneList)
            {
                bool stratRole = activePathReplays.TryGetValue(uid, out var done) && done.StratRole;
                EndPathReplay(uid, announce: !stratRole, kickBot: !stratRole);
            }
        }

        private static void SetReplayBotDuck(CCSPlayerController bot, bool duck)
        {
            try
            {
                ResetPlayerCrouch(bot, duck);
                var pawn = bot.PlayerPawn.Value;
                if (pawn?.Bot != null)
                    pawn.Bot.IsCrouching = duck;
            }
            catch { }
        }

        private static string? ReplayNadeClassname(string type, CsTeam team) => type switch
        {
            "smoke" => "weapon_smokegrenade",
            "hegrenade" => "weapon_hegrenade",
            "decoy" => "weapon_decoy",
            "flash" => "weapon_flashbang",
            "molotov" or "incendiary" => team == CsTeam.CounterTerrorist ? "weapon_incgrenade" : "weapon_molotov",
            _ => null,
        };

        // Put the grenade in the bot's hand. Give it when the bot has none, then draw it with the
        // SelectItem redeploy (subType 4): a plain select (subType 0) on a grenade throws it.
        private void DrawReplayNade(CCSPlayerController bot, string type)
        {
            string? nade = ReplayNadeClassname(type, bot.Team);
            var pawn = bot.PlayerPawn.Value;
            if (nade == null || pawn?.WeaponServices == null)
                return;
            try
            {
                bool owns = pawn.WeaponServices.MyWeapons.Any(h => h.Value != null && h.Value.IsValid && h.Value.DesignerName == nade);
                if (!owns)
                    bot.GiveNamedItem(nade);
                Server.NextFrame(() =>
                {
                    if (!IsPlayerValid(bot) || !bot.PawnIsAlive)
                        return;
                    if (!SwitchWeaponNative(bot, nade, 4))
                        EquipWeaponByName(bot, nade);
                });
            }
            catch (Exception e)
            {
                Log($"[PathReplay] draw {nade}: {e.Message}");
            }
        }

        // Back to the primary (else the pistol, else the knife) after a replayed throw.
        private void HolsterReplayNade(CCSPlayerController bot)
        {
            var pawn = bot.PlayerPawn.Value;
            if (pawn?.WeaponServices == null)
                return;
            try
            {
                string? gun = null;
                foreach (var h in pawn.WeaponServices.MyWeapons)
                {
                    var w = h.Value;
                    if (w == null || !w.IsValid)
                        continue;
                    string name = w.DesignerName;
                    if (IsGrenadeClassname(name) || name == "weapon_c4" || name.Contains("knife") || name == "weapon_taser")
                        continue;
                    var vdata = w.As<CCSWeaponBase>().VData;
                    if (gun == null || vdata?.GearSlot == gear_slot_t.GEAR_SLOT_RIFLE)
                        gun = name;
                }
                gun ??= "weapon_knife";
                if (!SwitchWeaponNative(bot, gun))
                    EquipWeaponByName(bot, gun);
            }
            catch (Exception e)
            {
                Log($"[PathReplay] holster: {e.Message}");
            }
        }

        // Without kickBot (the replay bot left behind for another reason) the bot stays where the
        // replay ended: that becomes its practice spot (respawn, jiggle, .startround), and it stands
        // still there.
        private void EndPathReplay(int uid, bool announce = true, bool kickBot = false)
        {
            if (!activePathReplays.Remove(uid, out var state))
                return;
            // A replay that ran to its end or was stopped with .replay stop removes its bot: it was
            // only there to walk the path.
            if (kickBot)
            {
                pathReplayBots.Remove(state.OwnerSteamId);
                KickReplayBot(uid);
                var finishedOwner = Utilities.GetPlayerFromSteamId(state.OwnerSteamId);
                if (announce && IsHumanPlayerValid(finishedOwner))
                    ReplyToUserCommand(finishedOwner, Localizer.ForPlayer(finishedOwner, "matchzy.rp.finished"));
                return;
            }
            var bot = Utilities.GetPlayerFromUserid(uid);
            var pawn = bot?.PlayerPawn.Value;
            var frames = state.Recording.Frames;
            if (IsPlayerValid(bot) && bot!.PawnIsAlive && pawn != null && frames.Count > 0)
            {
                PathFrame end = frames[Math.Clamp(state.Frame - 1, 0, frames.Count - 1)];
                var endPos = new Position(new Vector(end.X, end.Y, end.Z), new QAngle(0, end.Yaw, 0));
                lock (_botsDictLock)
                {
                    if (pracUsedBots.TryGetValue(uid, out var data))
                    {
                        data["position"] = endPos;
                        data["crouchstate"] = end.Duck;
                    }
                }
                pawn.Teleport(null, null, new Vector(0, 0, 0));
                if (state.DrawnFor >= 0)
                    HolsterReplayNade(bot);
            }
            if (!announce)
                return;
            var owner = Utilities.GetPlayerFromSteamId(state.OwnerSteamId);
            if (IsHumanPlayerValid(owner))
                ReplyToUserCommand(owner, Localizer.ForPlayer(owner, "matchzy.rp.finished"));
        }

        // Same kick as .nobot: untrack the bot and pin bot_quota to what is left first (otherwise the
        // engine refills the slot with a new bot), then kickid by userid, off the current frame.
        // Practice turns cash awards off, so the kick prints no kill reward to the other team.
        private void KickReplayBot(int uid)
        {
            // The userid goes to the next bot in this slot: drop this bot's armor/gun overrides.
            ForgetBotLoadout(uid);
            Server.NextFrame(() =>
            {
                var bot = Utilities.GetPlayerFromUserid(uid);
                if (bot == null || !bot.IsValid || !bot.IsBot)
                    return;
                int remainingBots;
                lock (_botsDictLock)
                {
                    pracUsedBots.Remove(uid);
                    remainingBots = pracUsedBots.Count;
                }
                Server.ExecuteCommand($"bot_quota {remainingBots}");
                Server.ExecuteCommand($"kickid {uid}");
            });
        }

        // ── Saved recordings: pathrecordings/<map>.json ──────────────────────────────────────────

        private sealed class SavedPathThrow
        {
            public int Frame { get; set; }
            public string Type { get; set; } = "";
            public float[] Pos { get; set; } = Array.Empty<float>();
            public float[] Ang { get; set; } = Array.Empty<float>();
            public float[] Vel { get; set; } = Array.Empty<float>();
            public float[] Spin { get; set; } = Array.Empty<float>();
            public float[] PlayerPos { get; set; } = Array.Empty<float>();
            public float[] PlayerAng { get; set; } = Array.Empty<float>();
            public ushort ItemIndex { get; set; }
            public float Duck { get; set; }
        }

        private sealed class SavedPath
        {
            public int Team { get; set; }
            // X, Y, Z, Pitch, Yaw, VX, VY, VZ, Duck (0/1) per tick.
            public List<float[]> Frames { get; set; } = new();
            public List<SavedPathThrow> Throws { get; set; } = new();
        }

        private static readonly JsonSerializerOptions SavedPathJsonOpts = new() { WriteIndented = false };
        private static readonly Regex SavedPathNameRegex = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

        // Saved paths of the current map, loaded on first use.
        private Dictionary<string, SavedPath>? _savedPaths;
        private string _savedPathsMap = "";
        private bool _savedPathsLoadFailed;

        private string SavedPathsFile(string map)
        {
            string safe = string.Concat(map.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
            return Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel($"pathrecordings/{safe}.json"));
        }

        private Dictionary<string, SavedPath> LoadSavedPaths()
        {
            string map = Server.MapName;
            if (_savedPaths != null && _savedPathsMap == map)
                return _savedPaths;
            _savedPathsMap = map;
            _savedPathsLoadFailed = false;
            string file = SavedPathsFile(map);
            try
            {
                var loaded = File.Exists(file)
                    ? JsonSerializer.Deserialize<Dictionary<string, SavedPath>>(File.ReadAllText(file), SavedPathJsonOpts)
                    : null;
                _savedPaths = new Dictionary<string, SavedPath>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, path) in loaded ?? new())
                    if (SanitizeSavedPath(path))
                        _savedPaths[name] = path;
            }
            catch (Exception e)
            {
                Log($"[PathReplay] load {file}: {e.Message}");
                BackupCorruptJsonFile(file, "PathRecordings");
                _savedPathsLoadFailed = true;
                _savedPaths = new Dictionary<string, SavedPath>(StringComparer.OrdinalIgnoreCase);
            }
            return _savedPaths;
        }

        // Valid JSON can still hold nulls ("Frames": null, a null frame or throw, null arrays in a
        // throw); FromSavedPath and .listrec dereference them all. Null lists become empty, null
        // entries are dropped. False for a null path.
        private static bool SanitizeSavedPath(SavedPath? path)
        {
            if (path == null)
                return false;
            path.Frames ??= new();
            path.Frames.RemoveAll(f => f == null);
            path.Throws ??= new();
            path.Throws.RemoveAll(t => t == null);
            foreach (var t in path.Throws)
            {
                t.Type ??= "";
                t.Pos ??= Array.Empty<float>();
                t.Ang ??= Array.Empty<float>();
                t.Vel ??= Array.Empty<float>();
                t.Spin ??= Array.Empty<float>();
                t.PlayerPos ??= Array.Empty<float>();
                t.PlayerAng ??= Array.Empty<float>();
            }
            return true;
        }

        // Serialized on the game thread: the commands keep mutating the same dictionaries and lists
        // (a shallow snapshot shares them), and System.Text.Json walking a list another thread is
        // changing throws or writes a torn file. Only the disk write runs off the game thread.
        private bool WriteSavedPaths(Dictionary<string, SavedPath> paths)
        {
            if (_savedPathsLoadFailed)
                return false;
            string file = SavedPathsFile(_savedPathsMap);
            WritePracticeJsonOrdered(file, JsonSerializer.Serialize(paths, SavedPathJsonOpts), "PathReplay");
            return true;
        }

        // Per-file write order for the off-thread practice JSON writes. Each write takes a sequence
        // number on the game thread; tasks can run (and take the lock) in any order, so a write whose
        // number is older than the last one written to that file is dropped instead of replacing the
        // newer content. Touched only inside the lock (the counter only on the game thread).
        private readonly object _practiceJsonWriteLock = new();
        private readonly Dictionary<string, long> _practiceJsonWrittenSeq = new();
        private long _practiceJsonWriteSeq;

        // Game thread: file is already resolved (Server.GameDirectory is never read in the task).
        private void WritePracticeJsonOrdered(string file, string json, string logTag)
        {
            long seq = ++_practiceJsonWriteSeq;
            Task.Run(() =>
            {
                try
                {
                    lock (_practiceJsonWriteLock)
                    {
                        if (_practiceJsonWrittenSeq.TryGetValue(file, out long written) && written > seq)
                            return;
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        string tmp = file + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, file, overwrite: true);
                        _practiceJsonWrittenSeq[file] = seq;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[MatchZy] [{logTag}] save {file}: {e.Message}");
                }
            });
        }

        private static float[] Vec(Vector v) => new[] { v.X, v.Y, v.Z };
        private static float[] Ang(QAngle a) => new[] { a.X, a.Y, a.Z };

        private static SavedPath ToSavedPath(PathRecording r) => new()
        {
            Team = (int)r.Team,
            Frames = r.Frames.Select(f => new[] { f.X, f.Y, f.Z, f.Pitch, f.Yaw, f.VX, f.VY, f.VZ, f.Duck ? 1f : 0f }).ToList(),
            Throws = r.Throws.Select(t => new SavedPathThrow
            {
                Frame = t.Frame,
                Type = t.Nade.Type,
                Pos = Vec(t.Nade.Position),
                Ang = Ang(t.Nade.Angle),
                Vel = Vec(t.Nade.Velocity),
                Spin = Vec(t.Nade.AngularVelocity),
                PlayerPos = Vec(t.Nade.PlayerPosition),
                PlayerAng = Ang(t.Nade.PlayerAngle),
                ItemIndex = t.Nade.ItemIndex,
                Duck = t.Nade.DuckAmount,
            }).ToList(),
        };

        private static PathRecording? FromSavedPath(SavedPath s, string map)
        {
            var r = new PathRecording { MapName = map, Team = s.Team == (int)CsTeam.CounterTerrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist };
            foreach (var f in s.Frames)
            {
                if (f.Length < 9)
                    return null;
                r.Frames.Add(new PathFrame(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8] >= 0.5f));
            }
            if (r.Frames.Count < 2)
                return null;
            foreach (var t in s.Throws)
            {
                if (t.Pos.Length < 3 || t.Ang.Length < 3 || t.Vel.Length < 3 || t.PlayerPos.Length < 3 || t.PlayerAng.Length < 3)
                    continue;
                var spin = t.Spin.Length >= 3 ? new Vector(t.Spin[0], t.Spin[1], t.Spin[2]) : null;
                var nade = new GrenadeThrownData(
                    new Vector(t.Pos[0], t.Pos[1], t.Pos[2]), new QAngle(t.Ang[0], t.Ang[1], t.Ang[2]),
                    new Vector(t.Vel[0], t.Vel[1], t.Vel[2]),
                    new Vector(t.PlayerPos[0], t.PlayerPos[1], t.PlayerPos[2]), new QAngle(t.PlayerAng[0], t.PlayerAng[1], t.PlayerAng[2]),
                    t.Type, DateTime.Now, t.ItemIndex, t.Duck, spin);
                r.Throws.Add((Math.Clamp(t.Frame, 0, r.Frames.Count - 1), nade));
            }
            return r;
        }

        [ConsoleCommand("css_saverec", "Save your last recorded path under a name on this map")]
        public void OnSaveRecConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleSaveRecCommand(player, command.ArgString);

        public void HandleSaveRecCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            string name = arg.Trim();
            if (!SavedPathNameRegex.IsMatch(name))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".saverec <name> (a-z, 0-9, _ -)"));
                return;
            }
            if (!lastPathRecordings.TryGetValue(player!.SteamID, out var recording) || !IsSameMap(recording.MapName, Server.MapName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.norecording"));
                return;
            }
            var paths = LoadSavedPaths();
            paths[name] = ToSavedPath(recording);
            if (!WriteSavedPaths(paths))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.fileunreadable"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.saved", name, PathSeconds(recording)));
        }

        [ConsoleCommand("css_loadrec", "Load a saved path; .replay then plays it")]
        public void OnLoadRecConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleLoadRecCommand(player, command.ArgString);

        public void HandleLoadRecCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            string name = arg.Trim();
            if (name.Length == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".loadrec <name>"));
                return;
            }
            if (activePathRecordings.ContainsKey(player!.SteamID))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.stillrecording"));
                return;
            }
            var paths = LoadSavedPaths();
            PathRecording? recording = paths.TryGetValue(name, out var saved) ? FromSavedPath(saved, Server.MapName) : null;
            if (recording == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.notfound", name));
                return;
            }
            lastPathRecordings[player.SteamID] = recording;
            RefreshRecDisplay(player.SteamID);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.loaded", name, PathSeconds(recording)));
        }

        [ConsoleCommand("css_listrec", "List the saved paths on this map")]
        public void OnListRecConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            var paths = LoadSavedPaths();
            if (paths.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.listnone"));
                return;
            }
            string list = string.Join(", ", paths.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key} ({(kv.Value.Frames.Count / 64.0f).ToString("0.0", CultureInfo.InvariantCulture)} s)"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.list", list));
        }

        [ConsoleCommand("css_delrec", "Delete a saved path on this map")]
        public void OnDelRecConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleDelRecCommand(player, command.ArgString);

        public void HandleDelRecCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            string name = arg.Trim();
            if (name.Length == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".delrec <name>"));
                return;
            }
            var paths = LoadSavedPaths();
            if (!paths.Remove(name))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.notfound", name));
                return;
            }
            if (!WriteSavedPaths(paths))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.fileunreadable"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.deleted", name));
        }
    }
}
