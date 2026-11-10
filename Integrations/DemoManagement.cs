using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        public string demoPath = "demos/";
        public string demoNameFormat = "{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}";
        public string demoUploadURL = "";
        public string demoUploadHeaderKey = "";
        public string demoUploadHeaderValue = "";
        public string activeDemoFile = "";
        // Earlier files of this map's recording, left behind when the watchdog restarted a stalled
        // recording into a new file. Uploaded together with activeDemoFile when the map ends.
        private readonly List<string> previousDemoSegments = new();
        public bool isDemoRecording = false;
        public bool isDemoUploadS3Enabled = false;

        // Set by every going-live path (match/scrim/hill): their cfg runs mp_restartgame AFTER the
        // exec, and the engine restart wipes an in-flight tv_record. The start is deferred to the
        // first live round_start that happens once the restart has certainly landed
        // (HandlePostRoundStartEvent), with a fallback timer if that round_start never arrives.
        public bool demoStartPending = false;

        // Server.CurrentTime before which a round_start must NOT start the demo. The going-live cfgs
        // fire a round_start of their own (mp_warmup_end) a moment BEFORE their mp_restartgame lands,
        // so without this floor the pending flag was consumed by the pre-restart round_start and the
        // demo was killed by the restart that followed.
        public float demoStartArmTime = 0f;

        // Set once the going-live mp_restartgame has been seen queued in the game rules
        // (m_flRestartRoundTime in the future). The first round_start after that, with no restart
        // left in the queue, is the real round 1, so the demo starts there instead of waiting out
        // demoStartArmTime (the restart lands ~1 s after LIVE, before the 4 s floor, so the demo
        // used to fall through to the 8 s fallback, mid-freezetime).
        public bool demoSawRestart = false;
        private CounterStrikeSharp.API.Modules.Timers.Timer? demoRestartPollTimer;

        private int demoStartAttempts = 0;
        private const int DemoStartMaxAttempts = 3;

        // Mid-match recording watchdog: the start-time growth check only proves the demo was alive
        // during its first seconds. GOTV can stall later (for example the CSTV bot kicked by a bot_quota change),
        // so a repeating timer keeps sampling the file size for the whole match and restarts the
        // recording if it stops growing.
        private CounterStrikeSharp.API.Modules.Timers.Timer? demoWatchdogTimer;
        private long demoWatchdogLastSize = 0;
        private int demoWatchdogStalledChecks = 0;
        private int demoWatchdogRequiredStrikes = 3;
        private const float DemoWatchdogIntervalSeconds = 60.0f;

        // "CSTV Recording..." is announced once per demo and only once the file is confirmed on disk.
        // The going-live paths used to print it from tv_enable alone, so players were told the match
        // was being recorded even when the recording had already been thrown away.
        private bool demoAnnounced = false;

        // One tv_enable 0/1 restart of a missing CSTV bot per demo start sequence. Pending while the
        // restart is in flight; a map change or a stopped recording cancels the deferred tv_record.
        private bool cstvRestartTried = false;
        private bool cstvRestartPending = false;

        /// <summary>
        /// Arm the deferred demo start used by StartLive / StartScrim / StartHill.
        /// </summary>
        /// <param name="restartSettleSeconds">Round starts before this many seconds are ignored, so the cfg's mp_restartgame cannot clobber the recording.</param>
        /// <param name="fallbackSeconds">Start the demo anyway if no qualifying round_start arrives.</param>
        public void ArmDemoStart(float restartSettleSeconds = 4.0f, float fallbackSeconds = 8.0f)
        {
            demoStartAttempts = 0;
            demoAnnounced = false;
            cstvRestartTried = false;
            // A new match's recording: parts left by an earlier match that was stopped (no
            // StopDemoRecording) must not be uploaded under this match's id.
            previousDemoSegments.Clear();
            demoStartPending = true;
            demoStartArmTime = Server.CurrentTime + restartSettleSeconds;
            demoSawRestart = false;
            // Watch for the queued restart every 0.1 s until it shows up or the fallback fires.
            // A restore without mp_restartgame never shows one and keeps the arm-time floor.
            float pollUntil = Server.CurrentTime + fallbackSeconds;
            demoRestartPollTimer?.Kill();
            demoRestartPollTimer = AddTimer(0.1f, () =>
            {
                if (!demoStartPending || demoSawRestart || Server.CurrentTime > pollUntil)
                {
                    demoRestartPollTimer?.Kill();
                    demoRestartPollTimer = null;
                    return;
                }
                if (IsRestartQueued())
                    demoSawRestart = true;
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(fallbackSeconds, () =>
            {
                if (demoStartPending) StartDemoRecording();
            });
        }

        /// <summary>
        /// True while an mp_restartgame is queued and has not landed yet.
        /// </summary>
        public bool IsRestartQueued()
        {
            try
            {
                var rules = GetGameRules();
                return rules != null && rules.RestartRoundTime > 0f && rules.RestartRoundTime > Server.CurrentTime;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Called on every live round_start while a demo start is pending. Starts the demo on the
        /// first round_start after the going-live restart has landed, or after the arm-time floor
        /// when no restart was seen.
        /// </summary>
        public void TryStartPendingDemo()
        {
            if (!demoStartPending || !isMatchLive)
                return;
            if (IsRestartQueued())
            {
                // This is the round_start mp_warmup_end fires just before the restart lands.
                demoSawRestart = true;
                return;
            }
            if (demoSawRestart || Server.CurrentTime >= demoStartArmTime)
                StartDemoRecording();
        }

        /// <summary>
        /// Drop all demo state on a map change. The engine stops GOTV recording on a changelevel, but a
        /// map change we do not drive ourselves (CS2-SimpleAdmin css_map, an RTV plugin, a plain
        /// changelevel) never reaches our teardown, so isDemoRecording stayed true forever and every
        /// later StartDemoRecording was swallowed by the "already recording" guard.
        /// </summary>
        public void ResetDemoStateOnMapStart()
        {
            if (isDemoRecording)
            {
                Log($"[Demo] Map changed while recording {activeDemoFile} - clearing stale recording state.");
            }
            isDemoRecording = false;
            demoStartPending = false;
            demoStartArmTime = 0f;
            demoStartAttempts = 0;
            demoAnnounced = false;
            cstvRestartTried = false;
            cstvRestartPending = false;
            activeDemoFile = "";
            previousDemoSegments.Clear();
            demoWatchdogTimer?.Kill();
            demoWatchdogTimer = null;
            demoSawRestart = false;
            demoRestartPollTimer?.Kill();
            demoRestartPollTimer = null;
        }

        // matchzy_demo_keep_days: deletes .dem files in the demo folder (matchzy_demo_path, in every
        // folder the engine can write it to) older than that many days. The current recording, its
        // earlier parts and the last stopped demo are kept, and nothing is deleted while an upload
        // runs. Only the top level of the demo folder and only *.dem, so other files a server keeps
        // there are untouched. Runs at load and at series end, off the game thread.
        private void CleanupOldDemos()
        {
            int days = demoKeepDays.Value;
            if (days <= 0)
                return;
            if (Interlocked.CompareExchange(ref _pendingUploads, 0, 0) > 0)
            {
                Log("[DemoCleanup] A demo upload is running; old demos are cleaned up next time.");
                return;
            }
            string folder = demoPath ?? "";
            string[] dirs = EngineDemoDirs().Select(d => Path.GetFullPath(Path.Join(d, folder))).Distinct().ToArray();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in previousDemoSegments.Append(activeDemoFile).Append(lastStoppedDemoFile))
                if (!string.IsNullOrEmpty(f))
                    keep.Add(Path.GetFileName(f));
            DateTime cutoff = DateTime.UtcNow.AddDays(-days);
            Task.Run(() =>
            {
                int deleted = 0;
                long bytes = 0;
                foreach (string dir in dirs)
                {
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (string file in Directory.EnumerateFiles(dir, "*.dem", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            if (keep.Contains(Path.GetFileName(file)))
                                continue;
                            var info = new FileInfo(file);
                            if (info.LastWriteTimeUtc >= cutoff)
                                continue;
                            long size = info.Length;
                            info.Delete();
                            deleted++;
                            bytes += size;
                        }
                        catch (Exception) { }
                    }
                }
                if (deleted > 0)
                    Log($"[DemoCleanup] Deleted {deleted} demo file(s) older than {days} day(s), {bytes / (1024 * 1024)} MB.");
            });
        }

        // matchzy_demo_live_only: CS2's tv_autorecord records every map from the moment it loads, so a
        // server left idle (or in practice mode) for hours writes a demo of nothing. With the convar on,
        // MatchZy turns tv_autorecord off and stops a recording it did not start itself. Its own demo
        // (isDemoRecording / demoStartPending, a live map) is never touched.
        private void EnforceDemoLiveOnly(string reason)
        {
            if (!demoLiveOnly.Value)
                return;
            if (ConVar.Find("tv_autorecord") != null)
                Server.ExecuteCommand("tv_autorecord 0");
            if (isDemoRecording || demoStartPending || cstvRestartPending || isMatchLive || matchStarted)
                return;
            // tv_stoprecord with nothing recording only prints "Not recording" in the console.
            Server.ExecuteCommand("tv_stoprecord");
            Log($"[Demo] matchzy_demo_live_only ({reason}): tv_autorecord 0, stopped any recording outside a live map.");
        }

        // Called at map start. server.cfg runs on every map load and can turn tv_autorecord back on,
        // and the engine starts its auto recording during the load, so check again a few seconds in.
        private void ScheduleDemoLiveOnlyCheck()
        {
            if (!demoLiveOnly.Value)
                return;
            AddTimer(5.0f, () => EnforceDemoLiveOnly("map start"), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(20.0f, () => EnforceDemoLiveOnly("map start"), TimerFlags.STOP_ON_MAPCHANGE);
        }

        private bool IsGOTVEnabled()
        {
            // -nohltv is a bare flag with no value, so probe the raw process command line for it.
            // Deliberately NOT CounterStrikeSharp.API.CommandLine: that class only exists in the
            // forked CounterStrikeSharp build, and touching it on a stock upstream server throws
            // TypeLoadException the moment this method is JIT-compiled.
            if (HasLaunchOption("-nohltv"))
            {
                Log("[Demo] Not recording: server was started with -nohltv.");
                return false;
            }

            // Prefer the LIVE tv_enable convar over the command line. Plenty of hosts enable GOTV from
            // a cfg (autoexec, server.cfg, a provider's own cstv.cfg) rather than a +tv_enable launch
            // option, and the command-line-only check silently disabled demo recording on those servers.
            ConVar? tvEnable = _cvTvEnable ??= ConVar.Find("tv_enable");
            if (tvEnable != null)
            {
                if (tvEnable.GetPrimitiveValue<bool>()) return true;
                Log("[Demo] Not recording: tv_enable is 0.");
                return false;
            }

            // Convar not resolvable yet - fall back to the launch options.
            string tvEnableParam = NativeAPI.GetCommandParamValue("+tv_enable", DataType.DATA_TYPE_STRING, "0");
            if (tvEnableParam != "1")
            {
                Log("[Demo] Not recording: tv_enable convar not found and +tv_enable is not 1 on the command line.");
                return false;
            }

            return true;
        }

        // tv_enable 1 only says GOTV is configured, not that the CSTV bot is still connected. The
        // engine's bot_quota sweep can kick it (it counts the CSTV bot as a bot), and a tv_record
        // without it creates no file and prints nothing on hosts that mute the HLTV channels.
        private static bool IsCstvClientPresent()
        {
            foreach (var controller in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
            {
                if (controller == null || !controller.IsValid || !controller.IsHLTV)
                    continue;
                if (controller.Connected == PlayerConnectedState.Connected
                    || controller.Connected == PlayerConnectedState.Connecting
                    || controller.Connected == PlayerConnectedState.Reconnecting)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Bring a kicked CSTV bot back with tv_enable 0 -> 1 and record a few seconds later. Only
        /// with tv_enable_dynamic 1: without it a runtime tv_enable change waits for the next map,
        /// so the toggle would do nothing useful. The two commands run on separate frames (a timer
        /// apart) and never while a recording is active. Returns true when the restart was started
        /// and the tv_record is deferred.
        /// </summary>
        private bool TryRestartMissingCstv()
        {
            if (cstvRestartTried)
                return false;
            cstvRestartTried = true;

            bool dynamicEnabled = false;
            try { dynamicEnabled = ConVar.Find("tv_enable_dynamic")?.GetPrimitiveValue<bool>() == true; } catch { }
            if (!dynamicEnabled)
            {
                Log("[Demo] CSTV bot is not connected and tv_enable_dynamic is 0 - GOTV comes back only after a map change.");
                return false;
            }

            Log("[Demo] CSTV bot is not connected (kicked?) - restarting GOTV with tv_enable 0/1 before recording.");
            cstvRestartPending = true;
            Server.ExecuteCommand("tv_enable 0");
            // Not STOP_ON_MAPCHANGE: GOTV must be switched back on even if the map changes meanwhile.
            AddTimer(2.0f, () => Server.ExecuteCommand("tv_enable 1"));
            AddTimer(6.0f, () =>
            {
                if (!cstvRestartPending)
                    return;
                cstvRestartPending = false;
                if (isDemoRecording)
                    return;
                // Still missing: StartDemoRecording sees cstvRestartTried and gives up without a tv_record.
                StartDemoRecording();
            }, TimerFlags.STOP_ON_MAPCHANGE);
            return true;
        }

        /// <summary>
        /// The CSTV bot left the server mid-map (kicked by a bot_quota sweep, an admin or another
        /// plugin). A running demo ends with it, so keep the file as a segment and record again
        /// once GOTV is back, the same way the watchdog handles a stalled recording.
        /// </summary>
        public void OnCstvDisconnected()
        {
            if (!isDemoRecording && !demoStartPending)
                return;
            Log("[Demo] CSTV bot left the server while a demo was recording or about to start.");
            // Deferred off the engine's disconnect path. A map change also disconnects CSTV: the new
            // map clears isDemoRecording (ResetDemoStateOnMapStart), so the check below does nothing then.
            AddTimer(1.0f, () =>
            {
                if (!isDemoRecording || IsCstvClientPresent())
                    return;
                demoWatchdogTimer?.Kill();
                demoWatchdogTimer = null;
                // Keep what was recorded up to the kick: it is uploaded with the next segment.
                string fullPath = EngineDemoPath(activeDemoFile);
                if (!string.IsNullOrEmpty(activeDemoFile) && File.Exists(fullPath) && !previousDemoSegments.Contains(activeDemoFile))
                    previousDemoSegments.Add(activeDemoFile);
                isDemoRecording = false;
                demoStartAttempts = 0;
                cstvRestartTried = false;
                Server.ExecuteCommand("tv_stoprecord");
                StartDemoRecording();
            }, TimerFlags.STOP_ON_MAPCHANGE);
            // A pending start (demoStartPending) needs nothing here: StartDemoRecording checks for
            // the CSTV bot itself and restarts GOTV.
        }

        public void StartDemoRecording()
        {
            demoStartPending = false;

            // Idempotent: already recording (e.g. pending-flag + fallback timer both fired) -> no-op,
            // don't restart the demo mid-file.
            if (isDemoRecording)
            {
                return;
            }
            // Check if GOTV is properly enabled before starting (it logs its own reason if not)
            if (!IsGOTVEnabled())
            {
                AnnounceDemoStatus("matchzy.demo.notenabled");
                return;
            }

            // A GOTV restart is already in flight: its timer starts the recording.
            if (cstvRestartPending)
                return;
            if (!IsCstvClientPresent())
            {
                if (TryRestartMissingCstv())
                    return;
                // tv_enable is 1 but the CSTV bot is not on the server (kicked, or GOTV did not come
                // back). A tv_record now would do nothing, so do not send one.
                Log("[Demo] Not recording: tv_enable is 1 but the CSTV bot is not on the server.");
                AnnounceDemoStatus("matchzy.demo.notonserver");
                activeDemoFile = "";
                return;
            }

            demoStartAttempts++;
            // ASCII only: the upload sends the file name in the MatchZy-FileName / Get5-FileName headers,
            // and .NET refuses a header with non-ASCII characters, so a team name such as "Ærø" lost the upload.
            string demoFileName = ToAsciiFileName(FormatCvarValue(demoNameFormat).Replace(" ", "_")) + ".dem";
            string tempDemoPath = demoPath == "" ? demoFileName : demoPath + demoFileName;

            // Never record over an existing demo. A name format without {TIME} gives the watchdog
            // restart (and a replayed map) the same file name, and tv_record would truncate the
            // earlier recording. Add _part2, _part3, ... instead.
            try
            {
                string baseDemoPath = tempDemoPath.Substring(0, tempDemoPath.Length - ".dem".Length);
                for (int part = 2; EngineDemoDirs().Any(dir => File.Exists(Path.Join(dir, tempDemoPath))) && part < 100; part++)
                    tempDemoPath = $"{baseDemoPath}_part{part}.dem";
                demoFileName = Path.GetFileName(tempDemoPath);
            }
            catch (Exception ex)
            {
                Log($"[StartDemoRecording] Could not check for an existing demo file: {ex.Message}");
            }
            try
            {
                // Every folder the engine may write the demo to (Windows: csgo/ and csgo/addons/metamod/).
                foreach (string dir in EngineDemoDirs())
                {
                    if (!Directory.Exists(dir))
                        continue;
                    string? directoryPath = Path.GetDirectoryName(Path.Join(dir, demoPath));
                    if (directoryPath != null && !Directory.Exists(directoryPath))
                        Directory.CreateDirectory(directoryPath);
                }
            }
            catch (Exception ex)
            {
                // Could not create the demo folder - record into the csgo root instead of not at all.
                Log($"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording without path. Name: {demoFileName}");
                tempDemoPath = demoFileName;
            }

            activeDemoFile = tempDemoPath;
            string recordArg = CsgoPathArg(tempDemoPath);
            // tv_record_immediate 1 makes GOTV write the .dem while the match runs instead of buffering
            // it, so the file is on disk (which is what the verification below checks) and survives a
            // server crash mid-match. Only sent when the build has the convar.
            if (ConVar.Find("tv_record_immediate") != null)
                Server.ExecuteCommand("tv_record_immediate 1");
            Server.ExecuteCommand($"tv_record {recordArg}");
            isDemoRecording = true;
            // Once per demo: a retry (another tv_record attempt) is the same recording for a panel.
            if (demoStartAttempts == 1)
                PublishDemoRecordingStart(tempDemoPath);
            Log($"[StartDemoRecording] tv_record {recordArg} (attempt {demoStartAttempts}/{DemoStartMaxAttempts})");
            VerifyDemoRecording(tempDemoPath);
        }

        /// <summary>
        /// Confirm a short while later that GOTV is actually recording, and retry if it is not. A
        /// tv_record that the engine drops (an mp_restartgame landing right after it is the usual
        /// cause) reports nothing at all, so without this the plugin believed it was recording for
        /// the whole match and only the missing file at the end gave it away.
        ///
        /// The existence check (3s) is what gates the retry and the "CSTV Recording..." announce:
        /// the engine creates the .dem the moment it accepts tv_record, so a missing file means the
        /// command was dropped. File GROWTH cannot be checked that early: GOTV's async demo writer
        /// (HLTVServerAsync) holds tv_delay seconds of frames in memory before anything past the
        /// header reaches disk, so the file legitimately sits at header size (~75 KB) for the whole
        /// delay window. The growth probe therefore waits tv_delay plus a margin, and only then
        /// treats a static file as a dead recording.
        /// </summary>
        private void VerifyDemoRecording(string expectedDemoFile)
        {
            // The engine creates the .dem as soon as it accepts tv_record, so 3 s is plenty; a miss
            // still goes through the retry path below.
            AddTimer(3.0f, () =>
            {
                // Something else already stopped or replaced the recording - nothing to verify.
                if (!isDemoRecording || activeDemoFile != expectedDemoFile) return;

                string fullPath = EngineDemoPath(expectedDemoFile);
                if (!File.Exists(fullPath))
                {
                    HandleDemoStartFailure(expectedDemoFile, "never appeared on disk");
                    return;
                }

                Log($"[Demo] Recording confirmed on disk: {expectedDemoFile}");
                AnnounceDemoStatus("matchzy.util.cstvrecording");

                long sizeAtFirstCheck = 0;
                try { sizeAtFirstCheck = new FileInfo(fullPath).Length; } catch { }

                float growthGrace = DemoGrowthGraceSeconds();
                AddTimer(growthGrace, () =>
                {
                    if (!isDemoRecording || activeDemoFile != expectedDemoFile) return;

                    long sizeNow = -1;
                    try { if (File.Exists(fullPath)) sizeNow = new FileInfo(fullPath).Length; } catch { }

                    if (sizeNow > sizeAtFirstCheck)
                    {
                        StartDemoWatchdog(expectedDemoFile, sizeNow);
                        return;
                    }
                    HandleDemoStartFailure(expectedDemoFile, $"is on disk but not growing {growthGrace:F0}s after the start ({sizeAtFirstCheck} -> {sizeNow} bytes)");
                });
            });
        }

        private static string? engineCsgoDirectory;

        /// <summary>
        /// The csgo/ folder as the engine itself sees it, for paths handed to engine commands.
        /// Server.GameDirectory can name the game through another path than the engine uses, for
        /// example a Docker host path (/home/fshost/...) while the engine runs from /home/fsh/...,
        /// and the engine rejects a write outside its own tree ("Rejecting round backup file path").
        /// On Linux the folder is taken from the running binary (/proc/self/exe, which the kernel
        /// reports fully resolved), then from the working directory, before falling back to
        /// Server.GameDirectory. Every file the engine writes (round backup .txt files, demos) is also
        /// read back through this folder, so the plugin always reads what this server's engine wrote.
        /// MatchZy's own files (MatchZyDataBackup, stats, configs) keep using Server.GameDirectory.
        /// </summary>
        public string EngineCsgoDirectory()
        {
            if (engineCsgoDirectory != null)
                return engineCsgoDirectory;
            string fallback = Path.Join(Server.GameDirectory, "csgo");
            string? found = null;
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    // .../game/bin/linuxsteamrt64/cs2 -> .../game/csgo
                    string? exe = new FileInfo("/proc/self/exe").LinkTarget;
                    string? gameDir = exe == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(exe)));
                    if (gameDir != null && File.Exists(Path.Join(gameDir, "csgo", "gameinfo.gi")))
                        found = Path.Join(gameDir, "csgo");
                }
                catch (Exception ex)
                {
                    Log($"[EngineCsgoDirectory] /proc/self/exe: {ex.Message}");
                }
                if (found == null)
                {
                    try
                    {
                        // The server runs from game/bin/linuxsteamrt64.
                        string gameDir = Path.GetFullPath(Path.Join(Environment.CurrentDirectory, "..", ".."));
                        if (File.Exists(Path.Join(gameDir, "csgo", "gameinfo.gi")))
                            found = Path.Join(gameDir, "csgo");
                    }
                    catch (Exception ex)
                    {
                        Log($"[EngineCsgoDirectory] working directory: {ex.Message}");
                    }
                }
            }
            engineCsgoDirectory = found ?? fallback;
            if (found != null && found != fallback)
                Log($"[EngineCsgoDirectory] Engine paths use {found} (Server.GameDirectory points at {fallback}).");
            return engineCsgoDirectory;
        }

        /// <summary>
        /// Console argument for a file the engine writes under csgo/ (tv_record). Not for round backups:
        /// since CS2 1.41.9.1 the engine rejects an absolute mp_backup_round_file (see SetupRoundBackupFile).
        /// On Linux an absolute path: the engine resolves a relative path against the first Game search
        /// path in gameinfo.gi, which is csgo/addons/metamod on Metamod servers. On Windows the path is
        /// csgo-relative: the record command rejects a drive letter ("record D:/...: invalid path"), and
        /// since the October 2026 updates also a drive-less root path ("/Steam/...": "is a current-drive-root
        /// path ... cannot be prefixed"), so no absolute form is left. The file then lands in csgo/ or
        /// csgo/addons/metamod/, and EngineDemoPath finds it in either. Always quoted, with forward
        /// slashes: the console tokenizer splits an unquoted argument at a space.
        /// </summary>
        public string CsgoPathArg(string csgoRelativePath)
        {
            string path = OperatingSystem.IsWindows()
                ? csgoRelativePath
                : Path.Join(EngineCsgoDirectory(), csgoRelativePath);
            return $"\"{path.Replace('\\', '/')}\"";
        }

        // Folders a recorded demo can be in: csgo/, and on Windows (csgo-relative tv_record, see
        // CsgoPathArg) also csgo/addons/metamod/, the first Game search path on Metamod servers.
        private string[] EngineDemoDirs()
        {
            string csgoDir = EngineCsgoDirectory();
            return OperatingSystem.IsWindows()
                ? new[] { csgoDir, Path.Combine(csgoDir, "addons", "metamod") }
                : new[] { csgoDir };
        }

        /// <summary>
        /// Full path of a csgo-relative demo file as the engine wrote it: the first folder from
        /// EngineDemoDirs that has it, else the csgo/ path (for a file not written yet).
        /// </summary>
        private string EngineDemoPath(string csgoRelativePath)
        {
            foreach (string dir in EngineDemoDirs())
            {
                string candidate = Path.Join(dir, csgoRelativePath);
                if (File.Exists(candidate))
                    return candidate;
            }
            return Path.Join(EngineCsgoDirectory(), csgoRelativePath);
        }

        /// <summary>
        /// How long after tv_record the .dem may legitimately stay at header size: the GOTV delay
        /// (nothing hits disk before delayed frames exist) plus a small flush margin. The margin
        /// cannot be zero - with tv_delay 0 the file still needs a moment to get past the header.
        /// </summary>
        private float DemoGrowthGraceSeconds()
        {
            return Math.Max(30.0f, GetTvDelaySeconds() + 30.0f);
        }

        private int GetTvDelaySeconds()
        {
            try
            {
                ConVar? tvDelay = _cvTvDelay ??= ConVar.Find("tv_delay");
                if (tvDelay != null) return Math.Max(0, tvDelay.GetPrimitiveValue<int>());
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Keep watching the demo for the rest of the match. Every interval the file size is
        /// sampled; only after enough consecutive samples with no growth is the recording declared
        /// dead and restarted into a fresh file. The strike count is derived from tv_delay: the
        /// async demo writer buffers the GOTV delay in memory, so a healthy recording can go a
        /// delay's worth of seconds without the file moving, and restarting on a shorter stall
        /// would kill a live recording (which is exactly what an early version of this did).
        /// </summary>
        private void StartDemoWatchdog(string expectedDemoFile, long knownSize)
        {
            demoWatchdogTimer?.Kill();
            demoWatchdogTimer = null;
            demoWatchdogLastSize = knownSize;
            demoWatchdogStalledChecks = 0;

            // Stall window: at least 2 minutes, and always past the GOTV delay.
            float stallWindowSeconds = Math.Max(120.0f, GetTvDelaySeconds() + 60.0f);
            demoWatchdogRequiredStrikes = (int)Math.Ceiling(stallWindowSeconds / DemoWatchdogIntervalSeconds);

            demoWatchdogTimer = AddTimer(DemoWatchdogIntervalSeconds, () =>
            {
                // Recording stopped or replaced through the normal paths - watchdog is done.
                if (!isDemoRecording || activeDemoFile != expectedDemoFile)
                {
                    demoWatchdogTimer?.Kill();
                    demoWatchdogTimer = null;
                    return;
                }

                string fullPath = EngineDemoPath(expectedDemoFile);
                long sizeNow = -1;
                try { if (File.Exists(fullPath)) sizeNow = new FileInfo(fullPath).Length; } catch { }

                if (sizeNow > demoWatchdogLastSize)
                {
                    demoWatchdogLastSize = sizeNow;
                    demoWatchdogStalledChecks = 0;
                    return;
                }

                demoWatchdogStalledChecks++;
                Log($"[Demo] Watchdog: {expectedDemoFile} has not grown for {demoWatchdogStalledChecks}/{demoWatchdogRequiredStrikes} check(s) (size {sizeNow} bytes).");
                if (demoWatchdogStalledChecks < demoWatchdogRequiredStrikes) return;

                // Dead mid-match. Restart into a fresh file and let the whole verify chain
                // (existence + growth + this watchdog) run again on the new recording.
                demoWatchdogTimer?.Kill();
                demoWatchdogTimer = null;
                Log($"[Demo] Watchdog: recording {expectedDemoFile} stalled mid-match - restarting into a new demo.");
                // Keep the stalled file: it holds the match up to the stall and is uploaded too.
                if (!string.IsNullOrEmpty(activeDemoFile) && !previousDemoSegments.Contains(activeDemoFile))
                    previousDemoSegments.Add(activeDemoFile);
                isDemoRecording = false;
                demoStartAttempts = 0;
                cstvRestartTried = false;
                Server.ExecuteCommand("tv_stoprecord");
                StartDemoRecording();
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        /// <summary>
        /// A demo start attempt turned out dead (file missing or not growing): stop whatever GOTV
        /// thinks it is doing and retry with a fresh tv_record, up to DemoStartMaxAttempts.
        /// </summary>
        private void HandleDemoStartFailure(string expectedDemoFile, string reason)
        {
            // Clear the flag first, otherwise the retry is swallowed by the idempotency guard.
            isDemoRecording = false;

            // If the engine is half-recording (stalled file open), a bare tv_record would be
            // rejected with "already recording" - clear it so the retry starts clean.
            Server.ExecuteCommand("tv_stoprecord");

            if (demoStartAttempts >= DemoStartMaxAttempts)
            {
                Log($"[Demo] GOTV demo {expectedDemoFile} {reason} after {demoStartAttempts} attempts - giving up for this map.");
                AnnounceDemoStatus("matchzy.demo.startfailed");
                // No demo exists, so map_result / match_cancelled and the stats export must not name one.
                activeDemoFile = "";
                return;
            }
            Log($"[Demo] GOTV demo {expectedDemoFile} {reason} - retrying tv_record.");
            StartDemoRecording();
        }

        /// <summary>
        /// Tell the server once whether this match is being recorded. Retries stay silent - only the
        /// final outcome is announced, so a demo that needed a second tv_record does not print twice.
        /// </summary>
        private void AnnounceDemoStatus(string key)
        {
            if (demoAnnounced) return;
            demoAnnounced = true;
            PrintLocalizedToAll(key);
        }

        // The demo file map_result / the stats export should name: the current recording, or the last
        // earlier part when a watchdog restart could not start a new one. Set by StopDemoRecording.
        public string lastStoppedDemoFile = "";

        /// <summary>
        /// Stop a running recording outside the normal map end (.endmatch / reset, a map change or
        /// reload): tv_stoprecord, stop the watchdog and tell the server the demo is saved. These
        /// paths used to send a bare tv_stoprecord, so the chat never said the recording had ended.
        /// No upload: an ended-early or abandoned map is not uploaded here, same as before.
        /// </summary>
        private void StopDemoAndAnnounce()
        {
            if (!isDemoRecording)
                return;
            Server.ExecuteCommand("tv_stoprecord");
            isDemoRecording = false;
            demoWatchdogTimer?.Kill();
            demoWatchdogTimer = null;
            var files = new List<string>(previousDemoSegments);
            if (!string.IsNullOrEmpty(activeDemoFile))
                files.Add(activeDemoFile);
            Log($"[StopDemo] tv_stoprecord - {activeDemoFile}");
            if (files.Count > 0)
                PrintLocalizedToAll("matchzy.demo.saved", string.Join(", ", files.Select(f => Path.GetFileName(f))));
        }

        public void StopDemoRecording(string activeDemoFile, long liveMatchId, int currentMapNumber)
        {
            // The match is over: a deferred tv_record from a GOTV restart must not start a new demo.
            cstvRestartPending = false;
            // Earlier segments from a watchdog restart come first, then the current file. The current
            // file only counts while it is actually recording (a failed restart leaves none).
            var segmentFiles = new List<string>(previousDemoSegments);
            if (isDemoRecording && !string.IsNullOrEmpty(activeDemoFile))
                segmentFiles.Add(activeDemoFile);
            previousDemoSegments.Clear();
            lastStoppedDemoFile = segmentFiles.Count > 0 ? segmentFiles[^1] : "";
            var segmentPaths = segmentFiles.Select(f => EngineDemoPath(f)).ToList();
            (int t1score, int t2score) = GetTeamsScore();
            int roundNumber = t1score + t2score;

            // Snapshot the upload settings now, not when the upload timer fires: the series end
            // restores the match config's cvars right after this, which can clear a demo upload URL
            // that came from the match config.
            string uploadURL = demoUploadURL;
            string headerKey = demoUploadHeaderKey;
            string headerValue = demoUploadHeaderValue;
            bool useS3 = isDemoUploadS3Enabled;
            // Also the event target: the upload (15 s plus the transfer) usually finishes after the
            // series ended and the match was reset, when the remote log URL is back to the server's
            // own. (liveMatchId here is this method's parameter, so it is already fixed.)
            long uploadMatchId = liveMatchId;
            var eventTarget = CurrentRemoteLogTarget();

            if (isDemoRecording)
            {
                Server.ExecuteCommand("tv_stoprecord");
                isDemoRecording = false;
                demoStartPending = false;
                string savedNames = string.Join(", ", segmentFiles.Select(f => Path.GetFileName(f)));
                PrintLocalizedToAll(uploadURL != "" ? "matchzy.demo.savedupload" : "matchzy.demo.saved", savedNames);
                demoWatchdogTimer?.Kill();
                demoWatchdogTimer = null;
                Log($"[StopDemoRecording] tv_stoprecord - {activeDemoFile}");
            }

            // Also runs when the recording is no longer active: parts left by a watchdog restart that
            // then failed still hold the match up to the stall and must be uploaded.
            if (segmentPaths.Count > 0)
            {
                // Counted from now (the upload starts 15 s later) so the empty-server shutdown waits for it.
                bool countsAsUpload = uploadURL != "";
                if (countsAsUpload)
                    Interlocked.Increment(ref _pendingUploads);
                AddTimer(15, () =>
                {
                    Task.Run(async () =>
                    {
                      try
                      {
                        foreach (string segmentPath in segmentPaths)
                        {
                            bool uploadSuccess = await UploadFileAsync(segmentPath, uploadURL, headerKey, headerValue, uploadMatchId, currentMapNumber, roundNumber, useS3);

                            // Only report the result when an upload was actually configured, otherwise every
                            // server without a demo upload URL would emit a failed demo_upload_ended per map.
                            if (uploadURL == "") return;

                            await SendEventAsync(new MatchZyDemoUploadedEvent
                            {
                                MatchId = uploadMatchId,
                                MapNumber = currentMapNumber,
                                FileName = Path.GetFileName(segmentPath),
                                Success = uploadSuccess,
                            }, eventTarget);
                        }
                      }
                      finally
                      {
                        if (countsAsUpload)
                            Interlocked.Decrement(ref _pendingUploads);
                      }
                    });
                });
            }
        }

        [ConsoleCommand("get5_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        [ConsoleCommand("matchzy_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        public void DemoUploadHeaderKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string header = command.ArgByIndex(1).Trim();

            if (header != "") demoUploadHeaderKey = header;
        }

        [ConsoleCommand("get5_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        [ConsoleCommand("matchzy_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        public void DemoUploadHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            string headerValue = command.ArgByIndex(1).Trim();

            if (headerValue != "") demoUploadHeaderValue = headerValue;
        }

        private string FormatDemoName()
        {
            string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");

            var demoName = demoNameFormat.Replace("{TIME}", formattedTime).Replace("{MATCH_ID}", $"{liveMatchId}").Replace("{MAP}", Server.MapName).Replace("{MAPNUMBER}", matchConfig.CurrentMapNumber.ToString()).Replace("{TEAM1}", matchzyTeam1.teamName).Replace("{TEAM2}", matchzyTeam2.teamName).Replace(" ", "_");
            return $"{demoName}.dem";
        }
    }
}
