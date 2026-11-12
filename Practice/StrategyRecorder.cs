using System.Globalization;
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
    // Strategy recorder: a whole team's execute, recorded together and played back by bots.
    //   .stratrec                - every living player on your team is a role. All are frozen for a
    //                              3 second countdown, then recorded from the same tick. .stratrec
    //                              again, .stoprecord or a double right click stops it.
    //   .strat [name] [me <n>]   - play the last (or a saved) strategy: one bot per role, all start
    //                              together after a 3 second countdown. "me <n>" plays role n yourself.
    //   .strat stop              - end the playback and remove its bots
    //   .savestrat <name>, .liststrat, .delstrat <name> - saved per map in strats/<map>.json
    //   .stoprecord              - stop your .rec, .stratrec or .stratslot recording (double right
    //                              click or Shift + E too)
    //   .createstrat <name>      - an empty strategy for your team, filled one slot at a time:
    //   .stratslot <name> <1-5>  - record that slot alone: the strategy's other slots are played by
    //                              bots at the same time (same countdown), so you time your part
    //                              against them. A slot already recorded is replaced.
    //   .playgroup <name>        - same as .strat <name>
    //   .strats                  - menu: pick a strategy, start it, record or clear its slots
    //
    // Each role is a normal path recording (PathReplay.cs): position, view, velocity, crouch and
    // grenades every tick, replayed by the same tick code. The leading idle time is cut by the same
    // amount from every role, so their timing against each other stays as recorded.
    public partial class MatchZy
    {
        private sealed class StratRole
        {
            public required string Name { get; init; }
            public required ulong SteamId { get; init; }
            public required PathRecording Path { get; init; }
            // 1-based slot (.strat me <n>, .stratslot).
            public int Slot { get; set; }
            public bool Done;
        }

        // A .stratslot take: one player records one slot while bots play the others.
        private sealed class SlotRecording
        {
            public required string StratName { get; init; }
            public required int Slot { get; init; }
            public required ulong SteamId { get; init; }
            public required PathRecording Path { get; init; }
            public bool Recording;
        }

        private const int MaxStratSlots = 5;
        private SlotRecording? activeSlotRecording;

        private bool IsSlotRecording(ulong steamId) => activeSlotRecording != null && activeSlotRecording.SteamId == steamId;

        private sealed class StratRecording
        {
            public required string MapName { get; init; }
            public required CsTeam Team { get; init; }
            public List<StratRole> Roles { get; } = new();
            // False during the countdown.
            public bool Recording;
        }

        private const int StratCountdownSeconds = 3;
        private const int MaxStratFrames = 64 * 120;
        private const float StopRecordDoubleClickSeconds = 0.4f;

        // The recording in progress (one at a time on the server).
        private StratRecording? activeStrat;
        // Bumped on every start/stop, so countdown timers of an older recording or playback do nothing.
        private int stratGeneration;
        // Per player steamid: the last strategy they recorded in or loaded.
        private readonly Dictionary<ulong, StratRecording> lastStrats = new();
        // The bots of the running playback, and the player who started it.
        private readonly List<int> stratPlaybackBots = new();
        private ulong stratPlaybackOwner;
        private bool stratPlaybackPending;
        // Last right click per userid, for the double click stop.
        private readonly Dictionary<int, float> lastAttack2Press = new();

        private void RegisterStrategyRecorder()
        {
            RegisterListener<Listeners.OnTick>(OnStratRecordTick);
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnStopRecordButtons);
        }

        // Practice start and end (ResetPracticeExtras).
        private void ResetStrats()
        {
            stratGeneration++;
            if (activeStrat != null)
                foreach (var role in activeStrat.Roles)
                    UnfreezeStratPlayer(Utilities.GetPlayerFromSteamId(role.SteamId));
            activeStrat = null;
            if (activeSlotRecording != null)
                UnfreezeStratPlayer(Utilities.GetPlayerFromSteamId(activeSlotRecording.SteamId));
            activeSlotRecording = null;
            // A ".strat <name> me <n>" owner frozen by StartStratCountdown: the bumped generation
            // stops the countdown timer that would unfreeze them, so undo the freeze here.
            if (stratPlaybackPending)
                UnfreezeStratPlayer(Utilities.GetPlayerFromSteamId(stratPlaybackOwner));
            lastStrats.Clear();
            stratPlaybackBots.Clear();
            stratPlaybackPending = false;
            lastAttack2Press.Clear();
        }

        private bool IsStratRole(ulong steamId)
            => activeStrat != null && activeStrat.Roles.Any(r => r.SteamId == steamId);

        // ── Recording ───────────────────────────────────────────────────────────────────────────

        [ConsoleCommand("css_stratrec", "Record a strategy: every living player on your team is a role")]
        public void OnStratRecConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            if (activeStrat != null)
            {
                if (IsStratRole(player!.SteamID))
                    FinishStratRecording();
                else
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.busy"));
                return;
            }
            if (!player!.PawnIsAlive || (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.notalive"));
                return;
            }
            if (activePathRecordings.ContainsKey(player.SteamID) || pendingRecCountdowns.ContainsKey(player.SteamID))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.stillrecording"));
                return;
            }
            if (activeSlotRecording != null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.busy"));
                return;
            }

            var strat = new StratRecording { MapName = Server.MapName, Team = player.Team };
            foreach (var p in Utilities.GetPlayers()
                .Where(p => IsHumanPlayerValid(p) && p.PawnIsAlive && p.Team == player.Team && !activePathRecordings.ContainsKey(p.SteamID))
                .OrderBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase))
            {
                strat.Roles.Add(new StratRole
                {
                    Name = p.PlayerName,
                    SteamId = p.SteamID,
                    Path = new PathRecording { MapName = Server.MapName, Team = player.Team },
                    Slot = strat.Roles.Count + 1,
                });
            }
            activeStrat = strat;
            int generation = ++stratGeneration;
            string roles = StratRoleList(strat);

            Server.NextFrame(() =>
            {
                if (generation != stratGeneration)
                    return;
                foreach (var role in strat.Roles)
                    FreezeStratPlayer(Utilities.GetPlayerFromSteamId(role.SteamId));
            });
            PrintToStratRoles(strat, "matchzy.st.recstart", strat.Roles.Count, StratCountdownSeconds);
            PrintToStratRoles(strat, "matchzy.st.roles", roles);
            for (int left = StratCountdownSeconds; left >= 1; left--)
            {
                int secondsLeft = left;
                AddTimer(StratCountdownSeconds - left, () =>
                {
                    if (generation == stratGeneration)
                        PrintToStratRoles(strat, "matchzy.st.countdown", secondsLeft);
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            AddTimer(StratCountdownSeconds, () =>
            {
                if (generation != stratGeneration || activeStrat != strat)
                    return;
                foreach (var role in strat.Roles)
                    UnfreezeStratPlayer(Utilities.GetPlayerFromSteamId(role.SteamId));
                strat.Recording = true;
                PrintToStratRoles(strat, "matchzy.st.recording");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        public void OnStratRecordTick()
        {
            var slot = activeSlotRecording;
            if (slot != null && slot.Recording)
            {
                var recorder = Utilities.GetPlayerFromSteamId(slot.SteamId);
                if (slot.Path.Frames.Count >= MaxStratFrames || !TryCapturePathFrame(recorder, out var slotFrame))
                    FinishSlotRecording();
                else
                    slot.Path.Frames.Add(slotFrame);
            }
            var strat = activeStrat;
            if (strat == null || !strat.Recording)
                return;
            try
            {
                bool anyLeft = false;
                foreach (var role in strat.Roles)
                {
                    if (role.Done)
                        continue;
                    var player = Utilities.GetPlayerFromSteamId(role.SteamId);
                    // Death, team change or disconnect ends that role; the others keep going.
                    if (player == null || player.Team != strat.Team || !TryCapturePathFrame(player, out var frame))
                    {
                        role.Done = true;
                        continue;
                    }
                    role.Path.Frames.Add(frame);
                    anyLeft = true;
                    if (role.Path.Frames.Count >= MaxStratFrames)
                    {
                        FinishStratRecording();
                        return;
                    }
                }
                if (!anyLeft)
                    FinishStratRecording();
            }
            catch (Exception e)
            {
                Log($"[StratRec] {e.Message}");
            }
        }

        // Called for every recorded throw (AddThrowToPathRecording).
        private void AddThrowToStratRecording(ulong steamId, GrenadeThrownData nade)
        {
            var slot = activeSlotRecording;
            if (slot != null && slot.Recording && slot.SteamId == steamId)
                slot.Path.Throws.Add((Math.Max(0, slot.Path.Frames.Count - 1), nade));
            var strat = activeStrat;
            if (strat == null || !strat.Recording)
                return;
            var role = strat.Roles.FirstOrDefault(r => r.SteamId == steamId && !r.Done);
            if (role == null)
                return;
            role.Path.Throws.Add((Math.Max(0, role.Path.Frames.Count - 1), nade));
        }

        private void FinishStratRecording()
        {
            var strat = activeStrat;
            if (strat == null)
                return;
            activeStrat = null;
            stratGeneration++;
            foreach (var role in strat.Roles)
                UnfreezeStratPlayer(Utilities.GetPlayerFromSteamId(role.SteamId));

            TrimStratIdle(strat);
            strat.Roles.RemoveAll(r => r.Path.Frames.Count < 2);
            if (strat.Roles.Count == 0)
            {
                PrintToStratSteamIds(strat, "matchzy.st.recempty");
                return;
            }
            foreach (var role in strat.Roles)
                lastStrats[role.SteamId] = strat;
            PrintToStratRoles(strat, "matchzy.st.recsaved", strat.Roles.Count, StratSeconds(strat));
        }

        // Cut the leading idle time by the same amount from every role (keeps their relative timing)
        // and each role's own trailing idle time.
        private static void TrimStratIdle(StratRecording strat)
        {
            static bool Moving(PathFrame f) => f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ > PathIdleSpeed * PathIdleSpeed;
            int first = int.MaxValue;
            bool throwFirst = false;
            foreach (var role in strat.Roles)
            {
                int moving = role.Path.Frames.FindIndex(Moving);
                if (moving >= 0 && moving < first)
                {
                    first = moving;
                    throwFirst = false;
                }
                if (role.Path.Throws.Count > 0 && role.Path.Throws[0].Frame < first)
                {
                    first = role.Path.Throws[0].Frame;
                    throwFirst = true;
                }
            }
            if (first == int.MaxValue)
            {
                foreach (var role in strat.Roles)
                {
                    role.Path.Frames.Clear();
                    role.Path.Throws.Clear();
                }
                return;
            }
            int start = Math.Max(0, first - (throwFirst ? ReplayNadeDrawLead : 1));
            foreach (var role in strat.Roles)
            {
                var frames = role.Path.Frames;
                var throws = role.Path.Throws;
                int cut = Math.Min(start, frames.Count);
                frames.RemoveRange(0, cut);
                int last = frames.FindLastIndex(Moving);
                if (throws.Count > 0)
                    last = Math.Max(last, throws[^1].Frame - cut);
                if (last >= 0 && last + 2 < frames.Count)
                    frames.RemoveRange(last + 2, frames.Count - last - 2);
                for (int i = 0; i < throws.Count; i++)
                    throws[i] = (Math.Clamp(throws[i].Frame - cut, 0, Math.Max(0, frames.Count - 1)), throws[i].Nade);
            }
        }

        [ConsoleCommand("css_stoprecord", "Stop your .rec or .stratrec recording")]
        public void OnStopRecordConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            if (!StopRecordingFor(player!))
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.notrecording"));
        }

        // Stops the caller's running .stratrec (as a role) or .rec. False when neither runs.
        private bool StopRecordingFor(CCSPlayerController player)
        {
            ulong steamId = player.SteamID;
            if (IsSlotRecording(steamId))
            {
                FinishSlotRecording();
                return true;
            }
            if (IsStratRole(steamId))
            {
                FinishStratRecording();
                return true;
            }
            if (pendingRecCountdowns.Remove(steamId))
            {
                UnfreezeStratPlayer(player);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.reccancelled"));
                return true;
            }
            if (activePathRecordings.Remove(steamId, out var running))
            {
                FinishPathRecording(player, steamId, running);
                return true;
            }
            return false;
        }

        // Two right clicks within 0.4 s, or Shift + E, stop the recording.
        private void OnStopRecordButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if (!isPractice || (pressed & (PlayerButtons.Attack2 | PlayerButtons.Use | PlayerButtons.Speed)) == 0)
                return;
            if (activePathRecordings.Count == 0 && activeStrat == null && activeSlotRecording == null)
                return;
            if (!IsHumanPlayerValid(player) || !player.UserId.HasValue)
                return;
            ulong steamId = player.SteamID;
            if (!activePathRecordings.ContainsKey(steamId)
                && !(activeStrat?.Recording == true && IsStratRole(steamId))
                && !(activeSlotRecording?.Recording == true && IsSlotRecording(steamId)))
                return;
            PlayerButtons held = player.Buttons | pressed;
            if ((pressed & (PlayerButtons.Use | PlayerButtons.Speed)) != 0
                && (held & PlayerButtons.Use) != 0 && (held & PlayerButtons.Speed) != 0)
            {
                Server.NextFrame(() =>
                {
                    if (IsHumanPlayerValid(player))
                        StopRecordingFor(player);
                });
                return;
            }
            if ((pressed & PlayerButtons.Attack2) == 0)
                return;
            int uid = player.UserId.Value;
            float now = Server.CurrentTime;
            if (lastAttack2Press.TryGetValue(uid, out float last) && now - last <= StopRecordDoubleClickSeconds && now >= last)
            {
                lastAttack2Press.Remove(uid);
                Server.NextFrame(() =>
                {
                    if (IsHumanPlayerValid(player))
                        StopRecordingFor(player);
                });
                return;
            }
            lastAttack2Press[uid] = now;
        }

        // ── Playback ────────────────────────────────────────────────────────────────────────────

        [ConsoleCommand("css_strat", "Play a strategy with bots: css_strat [name] [me <role>] | stop")]
        [ConsoleCommand("css_playgroup", "Play a strategy with bots: css_playgroup <name>")]
        public void OnStratConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleStratCommand(player, command.ArgString);

        public void HandleStratCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            var tokens = (arg ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 1 && tokens[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                StopStratPlayback();
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.stopped"));
                return;
            }
            if (stratPlaybackPending || stratPlaybackBots.Any(IsBotReplayingPath))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.alreadyplaying"));
                return;
            }
            if (activeStrat != null || activeSlotRecording != null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.busy"));
                return;
            }

            string? name = null;
            int meRole = -1;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Equals("me", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int n))
                {
                    meRole = n;
                    i++;
                }
                else if (name == null)
                    name = tokens[i];
                else
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".strat [name] [me <role>] | .strat stop"));
                    return;
                }
            }

            StratRecording? strat;
            if (name != null)
            {
                bool exists = LoadSavedStrats().TryGetValue(name, out var saved);
                strat = exists ? FromSavedStrat(saved!, Server.MapName) : null;
                if (strat == null)
                {
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, exists ? "matchzy.st.empty" : "matchzy.st.notfound", name));
                    return;
                }
                lastStrats[player!.SteamID] = strat;
            }
            else if (!lastStrats.TryGetValue(player!.SteamID, out strat))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.none"));
                return;
            }
            if (!IsSameMap(strat.MapName, Server.MapName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.othermap", strat.MapName));
                return;
            }
            if (meRole != -1 && !strat.Roles.Any(r => r.Slot == meRole))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.badrole", meRole, string.Join(", ", strat.Roles.Select(r => r.Slot))));
                return;
            }
            if (meRole != -1 && (!player.PawnIsAlive || player.Team != strat.Team))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.wrongteam"));
                return;
            }
            if (IsNoBotsFlagSet())
            {
                PrintToPlayerChat(player, Localizer.ForPlayer(player, "matchzy.pm.nobots"));
                return;
            }

            var botRoles = strat.Roles.Where(r => r.Slot != meRole).ToList();
            StopStratPlayback();
            stratPlaybackOwner = player.SteamID;
            stratPlaybackPending = true;
            int generation = ++stratGeneration;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.playing", name ?? "-", botRoles.Count, StratCountdownSeconds));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.roles", StratRoleList(strat)));
            var me = meRole >= 1 ? strat.Roles.First(r => r.Slot == meRole) : null;
            SpawnStratBots(player, strat, botRoles, 0, new List<(int Uid, StratRole Role)>(), generation,
                claimed => StartStratCountdown(player, claimed, generation, me, freezeInPlace: false, onGo: null));
        }

        // One bot per role, one after another (AddBot claims a bot over a few timers and cannot spawn
        // two at once). When all are there, the countdown starts.
        private void SpawnStratBots(CCSPlayerController owner, StratRecording strat, List<StratRole> botRoles, int index, List<(int Uid, StratRole Role)> claimed, int generation, Action<List<(int Uid, StratRole Role)>> allSpawned)
        {
            if (generation != stratGeneration)
                return;
            if (!isPractice || !IsHumanPlayerValid(owner))
            {
                AbortStratSpawn();
                return;
            }
            if (index >= botRoles.Count)
            {
                allSpawned(claimed);
                return;
            }
            if (isSpawningBot)
            {
                AddTimer(0.2f, () => SpawnStratBots(owner, strat, botRoles, index, claimed, generation, allSpawned), TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }
            if (!CanSpawnAnotherBot(owner))
            {
                ReplyToUserCommand(owner, Localizer.ForPlayer(owner, "matchzy.st.nobot"));
                allSpawned(claimed);
                return;
            }
            var role = botRoles[index];
            PathFrame f0 = role.Path.Frames[0];
            HashSet<int> before;
            lock (_botsDictLock)
                before = new HashSet<int>(pracUsedBots.Keys);
            AddBot(owner, f0.Duck, strat.Team, false, new Position(new Vector(f0.X, f0.Y, f0.Z), new QAngle(0, f0.Yaw, 0)));
            AwaitStratBot(owner, before, 0, generation, uid =>
            {
                claimed.Add((uid, role));
                stratPlaybackBots.Add(uid);
                SpawnStratBots(owner, strat, botRoles, index + 1, claimed, generation, allSpawned);
            }, () =>
            {
                ReplyToUserCommand(owner, Localizer.ForPlayer(owner, "matchzy.st.nobot"));
                SpawnStratBots(owner, strat, botRoles, index + 1, claimed, generation, allSpawned);
            });
        }

        private void AwaitStratBot(CCSPlayerController owner, HashSet<int> before, int attempt, int generation, Action<int> onClaimed, Action onFailed)
        {
            AddTimer(0.15f, () =>
            {
                if (generation != stratGeneration)
                    return;
                if (!isPractice || !IsHumanPlayerValid(owner))
                {
                    AbortStratSpawn();
                    return;
                }
                int? found = null;
                lock (_botsDictLock)
                {
                    foreach (var (botUid, data) in pracUsedBots)
                    {
                        if (before.Contains(botUid))
                            continue;
                        if (data.TryGetValue("owner", out var o) && o is CCSPlayerController c && c.IsValid && c.SteamID == owner.SteamID)
                        {
                            found = botUid;
                            break;
                        }
                    }
                }
                if (found != null)
                    onClaimed(found.Value);
                else if (attempt < 12)
                    AwaitStratBot(owner, before, attempt + 1, generation, onClaimed, onFailed);
                else
                    onFailed();
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        // The bots wait on their start spots (the keep-on-spot tick holds them there), the player of
        // "me <n>" is put on that role's start and frozen; then all go on the same tick.
        // me: the role the owner plays (put on its start), freezeInPlace: freeze the owner where they
        // stand (a .stratslot take). onGo runs on the tick the bots start.
        private void StartStratCountdown(CCSPlayerController owner, List<(int Uid, StratRole Role)> claimed, int generation, StratRole? me, bool freezeInPlace, Action? onGo)
        {
            bool frozen = false;
            if (me != null && IsHumanPlayerValid(owner) && owner.PawnIsAlive)
            {
                PathFrame f0 = me.Path.Frames[0];
                owner.PlayerPawn.Value!.Teleport(new Vector(f0.X, f0.Y, f0.Z), new QAngle(f0.Pitch, f0.Yaw, 0), new Vector(0, 0, 0));
                FlattenBodyRotationFrames(owner, f0.Yaw, 6);
                FreezeStratPlayer(owner);
                frozen = true;
            }
            else if (freezeInPlace && IsHumanPlayerValid(owner) && owner.PawnIsAlive)
            {
                FreezeStratPlayer(owner);
                frozen = true;
            }
            for (int left = StratCountdownSeconds; left >= 1; left--)
            {
                int secondsLeft = left;
                AddTimer(StratCountdownSeconds - left, () =>
                {
                    if (generation == stratGeneration && IsHumanPlayerValid(owner))
                        PrintToPlayerChat(owner, Localizer.ForPlayer(owner, "matchzy.st.countdown", secondsLeft));
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            AddTimer(StratCountdownSeconds, () =>
            {
                if (generation != stratGeneration)
                    return;
                stratPlaybackPending = false;
                if (frozen)
                    UnfreezeStratPlayer(owner);
                foreach (var (uid, role) in claimed)
                {
                    var bot = Utilities.GetPlayerFromUserid(uid);
                    if (!IsPlayerValid(bot))
                        continue;
                    activePathReplays[uid] = new PathReplayState { Recording = role.Path, OwnerSteamId = owner.SteamID, StratRole = true };
                }
                onGo?.Invoke();
                if (IsHumanPlayerValid(owner))
                    PrintToPlayerChat(owner, Localizer.ForPlayer(owner, onGo != null ? "matchzy.st.recording" : "matchzy.st.go"));
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        // The owner left (or practice ended) while the strat bots were still being spawned, with
        // this playback still current. Returning silently left stratPlaybackPending (and a .stratslot
        // take that never started recording) set, which blocked every later .strat/.stratslot.
        private void AbortStratSpawn()
        {
            if (activeSlotRecording != null && !activeSlotRecording.Recording)
                activeSlotRecording = null;
            StopStratPlayback();
        }

        private void StopStratPlayback()
        {
            stratGeneration++;
            stratPlaybackPending = false;
            var owner = Utilities.GetPlayerFromSteamId(stratPlaybackOwner);
            if (owner != null)
                UnfreezeStratPlayer(owner);
            foreach (int uid in stratPlaybackBots)
            {
                activePathReplays.Remove(uid);
                KickReplayBot(uid);
                ForgetBotLoadout(uid);
            }
            stratPlaybackBots.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────

        private static void FreezeStratPlayer(CCSPlayerController? player)
        {
            var pawn = player?.PlayerPawn.Value;
            if (player == null || !player.IsValid || pawn == null || !player.PawnIsAlive)
                return;
            pawn.MoveType = MoveType_t.MOVETYPE_NONE;
            pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }

        // Only undoes our own freeze (a .noclip set meanwhile stays).
        private static void UnfreezeStratPlayer(CCSPlayerController? player)
        {
            var pawn = player?.PlayerPawn.Value;
            if (player == null || !player.IsValid || pawn == null || pawn.MoveType != MoveType_t.MOVETYPE_NONE)
                return;
            pawn.MoveType = MoveType_t.MOVETYPE_WALK;
            pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }

        private void PrintToStratRoles(StratRecording strat, string key, params object[] args)
        {
            foreach (var role in strat.Roles)
            {
                var p = Utilities.GetPlayerFromSteamId(role.SteamId);
                if (IsHumanPlayerValid(p))
                    PrintToPlayerChat(p!, Localizer.ForPlayer(p, key, args));
            }
        }

        private void PrintToStratSteamIds(StratRecording strat, string key) => PrintToStratRoles(strat, key);

        private static string StratRoleList(StratRecording strat)
            => string.Join(", ", strat.Roles.Select(r => $"{r.Slot} = {r.Name}"));

        private static string StratSeconds(StratRecording strat)
            => (strat.Roles.Max(r => r.Path.Frames.Count) / 64.0f).ToString("0.0", CultureInfo.InvariantCulture);

        // ── Slots: build a strategy one role at a time ──────────────────────────────────────────

        [ConsoleCommand("css_createstrat", "Create an empty strategy for your team: css_createstrat <name>")]
        public void OnCreateStratConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleCreateStratCommand(player, command.ArgString);

        public void HandleCreateStratCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            string name = (arg ?? "").Trim();
            if (!SavedPathNameRegex.IsMatch(name) || name.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".createstrat <name> (a-z, 0-9, _ -)"));
                return;
            }
            if (player!.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.notalive"));
                return;
            }
            var strats = LoadSavedStrats();
            if (strats.ContainsKey(name))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.exists", name));
                return;
            }
            strats[name] = new SavedStrat { Team = (int)player.Team };
            if (!WriteSavedStrats(strats))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.fileunreadable"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.created", name, player.Team == CsTeam.CounterTerrorist ? "CT" : "T"));
        }

        [ConsoleCommand("css_stratslot", "Record one slot of a strategy while bots play the others: css_stratslot <name> <1-5>")]
        public void OnStratSlotConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleStratSlotCommand(player, command.ArgString);

        public void HandleStratSlotCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            var tokens = (arg ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int slot) || slot < 1 || slot > MaxStratSlots)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".stratslot <name> <1-5>"));
                return;
            }
            StartSlotRecording(player!, tokens[0], slot);
        }

        private void StartSlotRecording(CCSPlayerController player, string name, int slot)
        {
            if (!isPractice || !IsHumanPlayerValid(player))
                return;
            ulong steamId = player.SteamID;
            if (activeStrat != null || activeSlotRecording != null || stratPlaybackPending
                || activePathRecordings.ContainsKey(steamId) || pendingRecCountdowns.ContainsKey(steamId))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.busy"));
                return;
            }
            var strats = LoadSavedStrats();
            if (!strats.TryGetValue(name, out var saved))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.notfound", name));
                return;
            }
            var team = saved.Team == (int)CsTeam.CounterTerrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            if (!player.PawnIsAlive || player.Team != team)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.wrongteam"));
                return;
            }
            // The other recorded slots are played by bots; the slot being recorded is not.
            var others = FromSavedStrat(saved, Server.MapName)?.Roles.Where(r => r.Slot != slot).ToList() ?? new List<StratRole>();
            var strat = new StratRecording { MapName = Server.MapName, Team = team };
            strat.Roles.AddRange(others);

            StopStratPlayback();
            stratPlaybackOwner = steamId;
            stratPlaybackPending = true;
            int generation = ++stratGeneration;
            var take = new SlotRecording
            {
                StratName = name,
                Slot = slot,
                SteamId = steamId,
                Path = new PathRecording { MapName = Server.MapName, Team = team },
            };
            activeSlotRecording = take;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.slotstart", name, slot, others.Count, StratCountdownSeconds));
            SpawnStratBots(player, strat, others, 0, new List<(int Uid, StratRole Role)>(), generation,
                claimed => StartStratCountdown(player, claimed, generation, me: null, freezeInPlace: true, onGo: () =>
                {
                    if (activeSlotRecording == take)
                        take.Recording = true;
                }));
        }

        // Ends the take: the slot is stored in the saved strategy (trailing idle trimmed; the start
        // is not, it is the moment the other slots started) and the bots are removed.
        private void FinishSlotRecording()
        {
            var take = activeSlotRecording;
            if (take == null)
                return;
            activeSlotRecording = null;
            var player = Utilities.GetPlayerFromSteamId(take.SteamId);
            UnfreezeStratPlayer(player);
            StopStratPlayback();
            bool recorded = take.Recording;

            var frames = take.Path.Frames;
            static bool Moving(PathFrame f) => f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ > PathIdleSpeed * PathIdleSpeed;
            int last = frames.FindLastIndex(Moving);
            if (take.Path.Throws.Count > 0)
                last = Math.Max(last, take.Path.Throws[^1].Frame);
            if (last >= 0 && last + 2 < frames.Count)
                frames.RemoveRange(last + 2, frames.Count - last - 2);
            if (!recorded || frames.Count < 2 || last < 0)
            {
                if (IsHumanPlayerValid(player))
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, recorded ? "matchzy.st.recempty" : "matchzy.rp.reccancelled"));
                return;
            }

            var strats = LoadSavedStrats();
            if (!strats.TryGetValue(take.StratName, out var saved))
            {
                saved = new SavedStrat { Team = (int)take.Path.Team };
                strats[take.StratName] = saved;
            }
            saved.Roles.RemoveAll(r => r.Slot == take.Slot);
            saved.Roles.Add(new SavedStratRole
            {
                Slot = take.Slot,
                Name = IsHumanPlayerValid(player) ? player!.PlayerName : $"slot {take.Slot}",
                Path = ToSavedPath(take.Path),
            });
            saved.Roles.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            bool written = WriteSavedStrats(strats);
            if (!IsHumanPlayerValid(player))
                return;
            if (!written)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.fileunreadable"));
                return;
            }
            lastStrats[take.SteamId] = FromSavedStrat(saved, Server.MapName) ?? lastStrats.GetValueOrDefault(take.SteamId)!;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.slotsaved", take.Slot, take.StratName,
                (frames.Count / 64.0f).ToString("0.0", CultureInfo.InvariantCulture)));
        }

        // Removes one slot from a saved strategy (menu).
        private void ClearStratSlot(CCSPlayerController player, string name, int slot)
        {
            var strats = LoadSavedStrats();
            if (!strats.TryGetValue(name, out var saved) || saved.Roles.RemoveAll(r => r.Slot == slot) == 0)
                return;
            if (WriteSavedStrats(strats))
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.slotcleared", slot, name));
        }

        // ── Saved strategies: strats/<map>.json ─────────────────────────────────────────────────

        private sealed class SavedStratRole
        {
            public int Slot { get; set; }
            public string Name { get; set; } = "";
            public SavedPath Path { get; set; } = new();
        }

        private sealed class SavedStrat
        {
            public int Team { get; set; }
            public List<SavedStratRole> Roles { get; set; } = new();
        }

        private Dictionary<string, SavedStrat>? _savedStrats;
        private string _savedStratsMap = "";
        private bool _savedStratsLoadFailed;

        private string SavedStratsFile(string map)
        {
            string safe = string.Concat(map.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
            return Path.Join(Server.GameDirectory + "/csgo/cfg", MatchZyCfgRel($"strats/{safe}.json"));
        }

        private Dictionary<string, SavedStrat> LoadSavedStrats()
        {
            string map = Server.MapName;
            if (_savedStrats != null && _savedStratsMap == map)
                return _savedStrats;
            _savedStratsMap = map;
            _savedStratsLoadFailed = false;
            string file = SavedStratsFile(map);
            try
            {
                var loaded = File.Exists(file)
                    ? JsonSerializer.Deserialize<Dictionary<string, SavedStrat>>(File.ReadAllText(file), SavedPathJsonOpts)
                    : null;
                _savedStrats = new Dictionary<string, SavedStrat>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, strat) in loaded ?? new())
                {
                    // Valid JSON can still hold nulls (a null strat, "Roles": null, a null role or
                    // "Path": null); the commands dereference them all, so drop or empty them here.
                    if (strat == null)
                        continue;
                    strat.Roles ??= new();
                    strat.Roles.RemoveAll(r => r == null || !SanitizeSavedPath(r.Path));
                    foreach (var role in strat.Roles)
                        role.Name ??= "";
                    _savedStrats[name] = strat;
                }
            }
            catch (Exception e)
            {
                Log($"[StratRec] load {file}: {e.Message}");
                BackupCorruptJsonFile(file, "Strats");
                _savedStratsLoadFailed = true;
                _savedStrats = new Dictionary<string, SavedStrat>(StringComparer.OrdinalIgnoreCase);
            }
            return _savedStrats;
        }

        // Serialized on the game thread (the Roles lists are mutated there, e.g. .stratslot), only the
        // disk write runs off it, ordered per file (WritePracticeJsonOrdered). The path is resolved
        // here, never in the task.
        private bool WriteSavedStrats(Dictionary<string, SavedStrat> strats)
        {
            if (_savedStratsLoadFailed)
                return false;
            string file = SavedStratsFile(_savedStratsMap);
            WritePracticeJsonOrdered(file, JsonSerializer.Serialize(strats, SavedPathJsonOpts), "StratRec");
            return true;
        }

        private static SavedStrat ToSavedStrat(StratRecording strat) => new()
        {
            Team = (int)strat.Team,
            Roles = strat.Roles.Select(r => new SavedStratRole { Slot = r.Slot, Name = r.Name, Path = ToSavedPath(r.Path) }).ToList(),
        };

        private static StratRecording? FromSavedStrat(SavedStrat saved, string map)
        {
            var team = saved.Team == (int)CsTeam.CounterTerrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            var strat = new StratRecording { MapName = map, Team = team };
            for (int i = 0; i < saved.Roles.Count; i++)
            {
                var role = saved.Roles[i];
                var path = FromSavedPath(role.Path, map);
                if (path != null)
                    strat.Roles.Add(new StratRole { Name = role.Name, SteamId = 0, Path = path, Slot = role.Slot > 0 ? role.Slot : i + 1 });
            }
            strat.Roles.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return strat.Roles.Count > 0 ? strat : null;
        }

        [ConsoleCommand("css_savestrat", "Save your last recorded strategy under a name on this map")]
        public void OnSaveStratConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleSaveStratCommand(player, command.ArgString);

        public void HandleSaveStratCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            string name = (arg ?? "").Trim();
            if (!SavedPathNameRegex.IsMatch(name) || name.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".savestrat <name> (a-z, 0-9, _ -)"));
                return;
            }
            if (!lastStrats.TryGetValue(player!.SteamID, out var strat) || !IsSameMap(strat.MapName, Server.MapName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.none"));
                return;
            }
            var strats = LoadSavedStrats();
            strats[name] = ToSavedStrat(strat);
            if (!WriteSavedStrats(strats))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.fileunreadable"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.saved", name, strat.Roles.Count));
        }

        [ConsoleCommand("css_liststrat", "List the saved strategies on this map")]
        public void OnListStratConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            var strats = LoadSavedStrats();
            if (strats.Count == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.listnone"));
                return;
            }
            string list = string.Join(", ", strats.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key} ({(kv.Value.Team == (int)CsTeam.CounterTerrorist ? "CT" : "T")}, {kv.Value.Roles.Count})"));
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.list", list));
        }

        [ConsoleCommand("css_delstrat", "Delete a saved strategy on this map")]
        public void OnDelStratConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleDelStratCommand(player, command.ArgString);

        public void HandleDelStratCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            string name = (arg ?? "").Trim();
            if (name.Length == 0)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".delstrat <name>"));
                return;
            }
            var strats = LoadSavedStrats();
            if (!strats.Remove(name))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.notfound", name));
                return;
            }
            if (!WriteSavedStrats(strats))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.fileunreadable"));
                return;
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.st.deleted", name));
        }
    }
}
