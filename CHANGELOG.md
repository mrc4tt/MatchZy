# Changelog

Customized fork of [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak, adapted for CS2 game-server hosting. On top of upstream it adds a remote log HTTP API, G5API compatibility, auto changelevel, advanced stats (HLTV 2.0 rating / KAST / clutch / opening duels), a coach system, a pause overhaul, and in-game admin and match-setup menus.

Fork version numbering is independent of upstream. Upstream changelog: <https://github.com/shobhit-pathak/MatchZy/blob/main/CHANGELOG.md>

# 0.8.74

#### August 11, 2026

- Demo recording is now verified by the file actually growing on disk, not only by the file existing. GOTV's demo writer buffers tv_delay seconds of frames in memory before anything past the header reaches disk, so the check waits out the configured GOTV delay plus a margin before it treats a static file as a dead recording. A dead recording is stopped and started again into a new file, up to three attempts.
- The demo file is watched for the whole match, not only at the start. The file size is sampled once a minute; a recording whose file stops growing for several minutes in a row (scaled to the GOTV delay) is restarted into a new demo file. The restart is silent in chat and only reported in the server log.
- A failed demo start runs tv_stoprecord before retrying, so a recording the engine considers open but stalled cannot make every retry fail with "already recording".
- The plugin now sets tv_enable_dynamic 0 when a demo recording starts and when practice mode is set up. Dynamic CSTV (a +tv_enable_dynamic 1 launch option used by some hosts) removes the CSTV bot whenever nobody is spectating, which looks like the bot being kicked and can stall a running recording.
- Practice bot commands (.bot and friends) no longer add a bot when the server has no free slot. bot_add on a full server takes the CSTV bot's slot, kicking CSTV and killing GOTV and the demo recording. The player gets a chat message that the server is full instead.
- Bots are now kicked one by one by name instead of with a bare bot_kick (practice start and end, dryrun start, match reset, and the warmup and practice cfg templates). The bare bot_kick could also take out the CSTV bot, which killed GOTV and the demo recording.
- Molotov and incendiary detonation times in practice are now tracked per projectile instead of per player, so throwing two mollies in a row prints a correct time for each. The molotov/incendiary label now comes from the grenade itself instead of the thrower's team, which mislabeled picked-up nades.
- Added matchzy_auto_team_names_enabled. Set to 0 to stop MatchZy from renaming scoreboard teams in scrim mode (the automatic team_<playername> naming at knife/match start), keeping the game's own team names. Team names from a Get5/JSON match config are always applied regardless. Setting matchzy_ct_name or matchzy_t_name to "" never disabled the renaming, it only switched to the player-based naming; the config.cfg comments now say so.

# 0.8.73

#### August 6, 2026

- Fixed GOTV/CSTV demos not being recorded on .match, .scrim and .hill. The cfg for each of those modes is followed by mp_restartgame, and that restart kills a recording started before it. The demo was started on the round change that the cfg itself causes, which happens just before the restart, so the recording was thrown away a second later and no .dem was ever written. The plugin now waits for the restart to finish before starting the demo.
- The plugin now checks a few seconds after starting a demo that the file really exists on disk, and starts it again if it does not (up to three attempts). A dropped recording used to go unnoticed until the end of the map, when the demo turned out to be missing.
- GOTV recording now also works on servers that enable GOTV from a config file. Only the tv_enable launch option was checked, so a server that set tv_enable 1 in autoexec.cfg, server.cfg or a hosting provider's own CSTV config recorded nothing and said nothing about it.
- Fixed no demo being recorded for the rest of the session after a map change made by another plugin. A map change done through CS2-SimpleAdmin, an RTV plugin or the changelevel command did not go through MatchZy, so the plugin still believed a recording was running and refused to start a new one on every following match.
- The "CSTV Recording..." message in chat is now only shown once the demo file has actually been created, and it is shown once per match. Previously it was printed at go-live whenever tv_enable was 1, which said the match was being recorded even when the recording had already been dropped. If no demo could be started, the server now says that in chat instead of staying silent.
- Demos are now written continuously (tv_record_immediate) instead of being held in memory, so a demo of a match that ends in a server crash is no longer lost.
- Demo recording now reports itself in the server log: when it starts, when it is confirmed on disk, when it is retried, when it stops, and the reason when it is not started at all.
- A match that is stopped before it finishes now gets an end time written to the database. Stopping a match with .stopmatch, !endmatch, !forceend, !restart, !surrender or the Stop Match button in the admin menu left end_time empty in matchzy_stats_maps and matchzy_stats_matches, so the match stayed in the database looking like it was still running. The round score at the moment of the stop is stored as well.
- A stopped match is recognisable by having an end time but no winner. The winner column is left empty on purpose, since a match that plays out always stores a team name or "Draw" there. Rows that already have an end time are never overwritten.
- This also covers a match stopped during the knife round or side selection, which had already been given a database row at ready-up.
- config.cfg is no longer part of the release zip. Unpacking an update over an existing server could overwrite an edited config.cfg and wipe the settings on it. Servers without a config.cfg still get a complete one written on the next plugin load, and servers that have one keep it: new cvars from an update are appended to it as before.
- The release zip now contains database.json.example next to the config files, listing the MySQL fields. The real database.json is still never shipped, since overwriting it on an update would replace the server's MySQL login with the SQLite default and quietly stop stats from reaching the database.
- scrim.cfg written by the plugin now includes sv_pure, sv_pure_kick_clients and sv_pure_trace, matching the scrim.cfg that ships in the release zip.
- Fixed player stats not being saved on MySQL when the match config carries its own matchid. The plugin wrote the map row without first creating the match row it points at, which MySQL rejects, and every later write for that match was then skipped with "Invalid matchId: -1" in the log. The match row is now created up front. SQLite was unaffected, so this only showed on servers using MySQL.
- Loading a match config with a matchid that was already used no longer breaks stats for that match. The map row is updated instead of inserted a second time.
- A match that supplies its own matchid now gets its database row at load time rather than at go-live, so stopping it during warmup or veto still records an end time.
- Fixed database writes being lost when several of them happened at once. Round-end player stats, map-end data and series-end data share the end of the last round, and they were all sent over one shared database connection, which cannot handle simultaneous use. Each operation now uses its own connection.
- Fixed team assignment on maps set to knife in the match config. Sides were not initialised for those maps, so the plugin reused the side assignment left over from the previous match. Depending on what that was, teams were placed on the wrong sides, or no player was recognised as part of the match at all and everyone could join whichever team they wanted.
- Restoring a round backup no longer breaks side tracking for the rest of the session. The restore replaced the team objects the plugin tracks sides by, which could leave every player unassigned from that point on. Coach assignments now also survive a restore.
- Match configs that list team players as an array of Steam IDs are now accepted everywhere. Previously only the object form was recognised, and an array made the plugin treat the match as having no team lists, which disabled team locking.
- A player row the database rejects no longer takes the rest of the team down with it. One bad row used to abort the write for every remaining player in that round; each player is now written on its own and the skipped one is named in the log.
- The server log now says so when a round produces no player stats at all, instead of leaving an empty player stats table as the only clue. It also reports when only part of a team was written.
- Fixed .restore doing nothing while restoring the same round from the server console worked. The round snapshot is taken at round start, the same moment the server writes its own round file, so the copy stored inside the snapshot could be cut off half way. Restoring then wrote that cut-off copy over the server's intact round file, and the load failed without an error. A cut-off copy is now detected and the server's own file is used instead.
- A round snapshot no longer stores a half written copy of the server's round file. It is left empty instead and filled in a moment later, which already happened when the file was missing entirely but not when it was still being written.
- Round backups are checked for completeness before being offered to the server. An incomplete file is reported as such rather than being loaded and silently ignored.
- Fixed player stats never being written on a database created by an earlier MatchZy version. The four multi kill columns were named enemies5k, enemies4k, enemies3k and enemies2k here but enemy5ks, enemy4ks, enemy3ks and enemy2ks everywhere else, and an existing table is never altered by the plugin, so every player row was rejected by the database. The match and map tables were unaffected, which is why matches kept being recorded while the player table stayed empty. The upstream names are now used, and a table created by an affected version is renamed automatically on the next plugin load.
- server_ip in the match table again includes the port, as it did before. Rows written since then hold the address without it.
- Fixed a possible server crash when a player leaves in the same moment they are moved to their assigned team during match setup or veto. The team move is applied one frame later and the player was not re-checked in between.
- !loadbackup now says when it cannot read a file name from what was typed, instead of reporting that a backup with an empty name does not exist. Every use of the command is also written to the server log with the file name it received, since its replies otherwise only reach the player who typed it.
- Loading a backup during warmup now states that the backup is queued and will be restored once the match goes live. It previously reported that the backup had loaded successfully even though nothing had been restored yet, which is why running the command a second time appeared to be required: the second run forces the restore immediately. Both options are now named in the message.
- The round icons above the scoreboard are now cleared when switching game mode. Rounds played in dryrun stayed on screen after .prac, .exitdry, .scrim or .hill, so practice or warmup started with a row of icons from a game that was already over. Entering and restarting a dryrun clears them as well, so a dryrun always starts from an empty row.
- Practice mode keeps the row of round icons empty. Practice rounds still end and used to add an icon each time, filling the top of the scoreboard over a long session. Dryrun is unaffected and still builds up its round icons normally until .exitdry.

# 0.8.72

#### August 5, 2026

- Added matchzy_restore_auto_unpause. When enabled, the match unpauses on its own after a round restore instead of waiting for both teams to type .unpause. Default: false (unchanged behavior).
- Added matchzy_restore_unpause_delay, the countdown in seconds used by the automatic unpause. Default: 5.
- Fixed the match staying paused with no way to unpause when matchzy_pause_after_restore was disabled. The game already pauses itself on a backup load (mp_backup_restore_load_autopause), and MatchZy did not know about it, so .unpause did nothing.
- Restoring a backup that holds no round data no longer reports a successful restore and pauses the match. The admin now gets a message that nothing was restored. If the server still has the round file the game itself wrote for that round, the restore now uses it instead of refusing.
- Round backups no longer end up without round data. The backup was written at round start, at which point the game had not always finished writing its own round file yet, so the round data was missing and that round could not be restored later. The backup is now completed once the game is done.
- A round restore is now announced only once the server has actually loaded it. If the load does not take effect, the restore is reported as failed in chat and the reason is written to the server console, instead of announcing a successful restore and pausing a match that never moved.
- The round backup file is now rewritten from the stored backup every time before it is loaded. A leftover file from an earlier match with the same match id and round number was being loaded instead, which could make a restore do nothing.
- Restoring the current round with !restorecurrent, !restartround, !rr or !rrestore no longer asks for a "yes" confirmation. The command is admin-only and !restore never asked for one.
- Fixed .restorecurrent and .rrestore replying "invalid value for restore command" when typed with anything after the command name.
- The pause after a round restore is now applied after the backup has actually been loaded, instead of before it.
- A round restore now also rolls the scoreboard back: the round history above the scoreboard is cut to the restored round, and player kills, deaths, assists, damage, score and MVPs are set back to what they were in that round. Added matchzy_restore_scoreboard_stats to turn it off. Older backup files only get the round history rolled back, since they carry no player snapshot.
- The chat commands .backup, .backups, .backupmenu, .restorelast, .rl, .restorecurrent and .rrestore now work. Only the !backup and /backup forms were reaching the plugin.
- The round history above the scoreboard is now cleared when a match is ended and the server returns to warmup, instead of keeping the win icons of the finished match next to a 0-0 score.
- Fixed MatchZy writing config files into a second cfg folder on servers that have both csgo/cfg/matchzy and csgo/cfg/MatchZy. One folder is now picked consistently (an exact lowercase "matchzy" first), and the folder in use is printed in the server log at startup. If both folders exist, a warning names the one being used so the unused folder can be deleted.
- database.json is now read from the same cfg folder as the rest of the config files instead of a hardcoded lowercase path.
- Fixed MatchZy commands running twice on servers that list "." as a chat trigger in CounterStrikeSharp's configs/core.json (for example "PublicChatTrigger": [ "!", "." ]). CounterStrikeSharp already turns ".map" into the css_map console command, and MatchZy handled the same chat line a second time, so the command was carried out twice and every reply was printed twice (".map junkname" answered "Invalid map name!" twice). MatchZy now detects the trigger at startup and leaves those commands to CounterStrikeSharp. Chat commands that have no console command of their own, such as .rdy and .knife, are unaffected. Servers using the default "!" and "/" triggers see no change.
- Added matchzy_dot_trigger_dedupe to turn the above off (default: true). It only has an effect on servers that use "." as a chat trigger.

# 0.8.71

#### August 1, 2026

- Recorded demos are now uploaded once the map ends, if matchzy_demo_upload_url is set. The fork previously stopped the recording without ever uploading it.
- Added matchzy_demo_upload_s3 (alias get5_demo_upload_s3). When enabled, the demo is sent with an HTTP PUT and the raw .dem file as the body, for S3-compatible storage that uses presigned upload URLs. Sign the URL with Content-Type application/octet-stream. Default: false.
- Added matchzy_demo_upload_header_key and matchzy_demo_upload_header_value (aliases get5_demo_upload_header_key / get5_demo_upload_header_value) to send a custom authentication header with the upload.
- Large demo uploads no longer fail part way through. The upload now has its own timeout instead of sharing the 10 second one used for event publishing.
- Added the demo_upload_ended event, sent to the remote log URL after each upload with the demo filename and whether it succeeded.
- An invalid matchzy_demo_upload_url is now reported in the server log instead of being ignored silently.
- matchzy_demo_upload_url and the two upload header settings are deliberately not written to config.cfg, because a presigned URL and an authentication token are credentials and config.cfg is world readable. Set them from a private cfg. config.cfg documents the syntax in a comment.

# 0.8.70

#### July 31, 2026

- Fixed match end data being written to the database twice at series end (duplicate "[SetMatchEndData] Match X end data set successfully" log lines). The second write could also overwrite the final map's per-map score with the series score.

# 0.8.69

#### July 30, 2026

- Fixed `.t` / `.ct` while spectating in practice: previously the player was either kicked (client drop with NETWORK_DISCONNECT_LOOPDEACTIVATE) or joined the team permanently dead. The switch now calls the engine's own HandleCommand_JoinTeam (immediate mode), which runs the complete join flow and spawns the player. Requires the updated gamedata/matchzy.json (new key: CCSPlayerController_HandleCommandJoinTeam); without it the switch falls back to the old behavior plus a team-menu hint.
- Fixed a practice side-switch sometimes swallowing the player's next real death on the scoreboard: the no-death flag was set even when no switch suicide fired (e.g. switching while dead or spectating) and lingered until the next death.

# 0.8.68

#### July 29, 2026

- Fixed `.botjiggle` making a freshly spawned `.bot` invisible: the jiggle anchored the new bot at the map spawn point it briefly appeared on and yanked it back there every tick, so it never showed up at the lineup. Jiggle now strafes each bot around its assigned practice spot; bots not spawned via practice commands are no longer jiggled at all.
- `.bot` now pre-pins bot_quota to the expected count (current bots + 1) right before bot_add, so the engine's quota logic has no headroom to spawn the second bot of a pair in the first place. If the engine pair-spawns anyway, the extra is still detected and kicked as before.
- Fixed `.bot` / `.tbot` / `.ctbot` sometimes spawning nothing (console: "kicking wrong-team pair bot"): when bot_add pair-spawned one bot per team, the wrong-team bot could be seen first and the requested one arrived a tick later, after the claim pass had already given up and kicked everything. The claim now prefers the requested team over the whole set and retries briefly before failing, so the right bot is kept and only the pair extra is kicked.
- `.backups` (css_backupmenu) now works when no match is live: it lists the 5 newest backup files on disk (match id, round, score, map, age) with a ready-to-use restore command for each. Useful after a server crash - reconnect, run `.backups`, restore the round the match crashed on.
- Added `css_loadbackup` as an alias of `matchzy_loadbackup` / `get5_loadbackup`, so a backup file can be restored from chat with `!loadbackup <file>` (admin only). Previously the command was only usable from the server console.

# 0.8.67

#### July 28, 2026

- Removed `matchzy_random_spawns` and the 0.8.65 coach auto-scatter entirely. Live match/scrim rounds always use the map's competitive spawns; a coach never changes player spawn behavior (the coach sits at their own viewing spot and does not take a competitive spawn point). Dryrun mode keeps its random spawns. If your existing config.cfg contains a matchzy_random_spawns line, it can be deleted (a leftover line only causes a harmless unknown-command console note).
- Fixed a coach still displacing one player off the standard competitive spawns (e.g. Ancient CT: one player left in the back corner instead of the line of 5). The reseat treated every enabled spawn as valid, so the displaced player was never pulled back; it now only accepts the map's lowest-priority (competitive) spawn set and moves the displaced player onto the spawn the coach freed up. Also fixed a player standing exactly on a competitive spawn being re-teleported every round (a neighbouring player could claim that spawn first; spawn claiming is now nearest-first).
- Added `bot_quota 0` to live.cfg, scrim.cfg, hill.cfg, knife.cfg and warmup.cfg. The CS2 default is 10, so on servers whose base config never zeroes it the engine could quietly add or refill bots during warmup and matches.
- Added `mp_randomspawn 0` to scrim.cfg, hill.cfg and knife.cfg (live.cfg already had it). The cvar is sticky, so a mode or plugin that had set it to 1 earlier could leave scrim/hill/knife rounds with randomized engine spawns.
- The coach-displaced player is now corrected in the spawn frame itself (before the client renders), instead of a visible teleport during freezetime - players no longer notice anything when a coach is on. Nobody is ever moved to Spectator (no ghosting window), and the timer-based reseat stays as a safety net.
- The coach's freezetime death no longer shows up in the kill feed (the old suppression only matched self-attributed suicides; the forced kill reports the world as attacker and slipped through).
- The coach no longer steals one of the five competitive teammate colors: the coach is set colorless and the five real players on each side always hold the five distinct colors (the ex-coach gets a color back on .uncoach).
- Fixed the coach not dying at all on current CS2 builds: the coach body is made untouchable at placement, and the engine now drops the forced suicide when the pawn takes no damage - damage is re-enabled for the kill itself. Also moved the kill to the end of freezetime (about 1 second before live) so the coach is alive at the viewing spot for the whole tactical talk, without an idle body entering the live round or a long black death-cam during freezetime.
- The coach is never moved through the Spectator team anymore (ghosting risk): the freezetime-end fallback for a still-alive coach now kills the coach instead of bouncing them Spectator-and-back, and the coach team fixup switches directly between sides.

# 0.8.66

#### July 27, 2026

- Fixed GOTV/CSTV demos not recording on `.scrim` and `.hill` (worked on `.match`). scrim.cfg / hill.cfg run `mp_restartgame`, whose restart clobbered the `tv_record` that was started on a fixed 2s timer; the demo now starts on the first live round after the restart settles (with a fallback), so it records reliably.
- Fixed `matchzy_random_spawns` (and the coach auto-shuffle) leaving players on the same spawns every round: the shuffle ran at round start before players finished respawning, so the teleport did nothing. It now runs a moment later, so spawns actually vary. Added debug lines (under `matchzy_coach_debug`) showing the spawn pool size and how many players were moved.

# 0.8.65

#### July 27, 2026

- Added `matchzy_random_spawns` (default false): randomizes player spawns each live round across all enabled map spawns instead of the fixed competitive set, so maps no longer always reuse the same spots (e.g. Dust2 T now uses all 10 spawns, not the same 5). For casual/scrim variety only; leave off for ranked matches. While on, the coach spawn-reseat is skipped. (Note: a side can only vary up to the number of spawn entities the map defines - Dust2 CT has only 5, so it cannot vary.)
- Random spawns now also turn on automatically while a coach is present (no need to set the convar), since a coach on a side is exactly when spawns look "always the same". Coaches are excluded from the shuffle (they stay at their viewing spot). Reverts to normal once the last coach leaves.
- Fixed the coach being killed ~1 second AFTER the round went live (visible/idle body into the round) instead of during freezetime. The kill is now scheduled at a small fixed delay early in freezetime, so the coach body is reliably gone before the round starts, regardless of the `mp_freezetime` value (previously it read `mp_freezetime` - a float cvar - as an int, mistiming the kill).
- Fixed `.watchme` / `.fas` (and `.spec`) failing to move players to Spectator and spamming `CCSPlayerPawnBase::SwitchTeam( 1 ) - invalid team index.`: `SwitchTeam` only accepts T/CT, so the Spectator move now uses `ChangeTeam` on the already-dead pawn.
- Removed log warning spam `Field CCSPlayerController:m_szClanName is not networked, but SetStateChanged was called on it` (and the same for `CCSGameRules:m_fNextUpdateTeamClanNamesTime`) from the ready clan-tag refresh. Both fields are not networked, so the calls were no-ops; scoreboard tag updates are unaffected.

# 0.8.64

#### July 24, 2026

- Fixed a rare server crash when using `.ct` / `.t` / `.spec` to switch to the side you are already on. It ran a redundant suicide plus team-switch to your current team (the engine's `ChangeBasePlayerTeamAndPendingTeam` with the requested team equal to the current one occasionally crashed). It is now a no-op, only respawning you if you were dead on T/CT.
- `.cbot` / `.crouchbot` / `.duckbot` now boost you on top of the crouched bot (spawn above it), matching `.crouchboost`.
- Fixed `.crouchboost` / `.cboost` skipping the player-validity and bot-limit checks that `.boost` already ran.
- Fixed `.loadbotpos` placing a bot tilted or under the map when the saved spot was recorded while looking up or down: the bot is now always placed upright, facing the saved direction.
- Reduced duplicate "kicking late untracked bot" log spam when spawning several bots in quick succession.
- Removed the experimental `.jt` / `.jumpthrow` command and the `matchzy_experimental_jumpthrow` convar (the server-side input injection was unreliable across CS2 builds).

# 0.8.63

#### July 24, 2026

- Fixed a match load being wiped when the config's first map differs from the current map. Loading via `get5_loadmatch_url` / `matchzy_loadmatch` (or the file variants) changelevels to the match map; that changelevel ended the outgoing map and reset the just-loaded match, so the server arrived on the new map with no match (`get5_status` returned `none` / null matchid, default team names, no ready). The match is now carried across the changelevel and re-loaded on the target map, matching get5 behavior. Loading while already on the first map was unaffected and still works.

# 0.8.62

#### July 24, 2026

- New named bot positions (practice): `.savebotpos <name>` (`.sbp`) saves your current spot as a named bot placement for the current map, `.loadbotpos <name>` (`.lbp`) spawns a bot at that saved spot (no name spawns every saved placement on the map), `.listbotpos` (`.listbp`) lists them, `.delbotpos <name>` (`.dbp`) removes one. Stored per map in `cfg/matchzy/botpositions.json`.
- New `.showbotpos` (`.showbp`): toggles in-world markers (beam plus name label, CT lime / T orange) at every saved bot placement on the map; redraws itself after a map change.
- New `.botjiggle`: toggles all practice bots strafing side to side for dodge/aim reps. New `matchzy_botjiggle_range` convar (default 30) tunes the strafe width.
- `.cbot` / `.crouchbot` / `.duckbot` now boost you on top of the crouched bot (spawn above it), matching `.crouchboost`.
- Practice now prints the molotov/incendiary burn time in chat when your fire detonates (via the inferno start-burn event), alongside the other utility detonation timings.
- Fixed bot placements spawning under the map or lying prone: the saved-position file now serializes correctly, and every bot spawn is placed upright (keeps its facing without the view pitch tilting the model or clipping it through the floor).
- Fixed `.crouchboost` / `.cboost` skipping the player-validity and bot-limit checks that `.boost` already ran.

# 0.8.61

#### July 20, 2026

- Coach viewing spots reworked across the whole map pool. By default (matchzy_coaching_mode 1) each active map now ships a hand-tuned spot behind the team; maps without one fall back to a computed spot (stands behind the team with line of sight, keeps a real stand-back distance so it is never nose-to-back with a player, refuses a lower-level spot, and uses an overhead camera above the spawn when no clean ground spot exists).
- New `matchzy_coaching_mode` (default 1): 1 uses a `spawns/coach/<map>.json` spot when present (hand-tuned override) otherwise computes it, 2 always computes the coach spot behind the team and ignores the JSON files.
- Coach spawn files reworked: the old fixed per-map viewing spots were removed (they were the "always the same bad spot" complaint). Placement is computed live for every map; a `spawns/coach/<map>.json` entry is only an optional override, and `.savecoachspawn t|ct` writes/replaces one (saving your exact view angle) for any map that needs hand-tuning.
- New `.showcoachspawns` (admin): draws the coach viewing spot for both sides in-world (blue = CT, orange = T), matching `matchzy_coaching_mode`, and survives a map change instead of going invisible-but-on. Reloads the JSON each time so hand-edits show immediately.
- New `.coachtest` (admin, debug): places you like a coach on your current side right now (run again to release) so a single admin can check the coach spot on any map without a match.
- Coach placement is now silent (no landing sound), the coach can no longer be damaged/killed by teammates, and players already on a competitive spawn are never re-teleported (the reseat only moves a genuinely coach-displaced player, and near-duplicate spawns can't seat two players on top of each other).
- Coach spawn files are read/written under a case-resolved plugin path (prefers an existing `matchzy` folder over `MatchZy`), so a saved spot is found again on case-sensitive Linux.
- Practice `.bot` fix: one `bot_add` could pair-spawn a bot on the other team (and the claim was team-blind, so a CT player could get a CT bot); the wrong-team bot is now kicked and `.bot` adds exactly one bot on the opposing team.
- Fixed a server crash from `.watchme` / `.fas`: forcing the other players to spectator used the live-player team-change path (weapon strip -> other plugins' weapon hooks re-enter on a half-destroyed weapon -> crash). It now drops weapons first and switches team the safe way, the same fix `.t`/`.ct`/`.spec` already had.

# 0.8.60

#### July 19, 2026

- Fixed the coach falling out of the map with a black screen on maps where the team spawn backs onto the map edge (Mirage T): the behind-team spot is now validated with wall and floor probes and moves closer to the spawn until it is inside the world.
- Fixed players being re-teleported every round while a coach was on, even on the coachless side: anyone standing on any valid team spawn is now left alone (maps like Mirage enable more spawn points than the team size), and near-duplicate spawn points can no longer seat two players almost on top of each other.
- New `.coachtest` (admin): instantly places you like a coach on your current side and back again on the second run - lets a single admin verify coach placement on any map without bots or a match.
- Coach spawn computation is hardened against the AcceleratorCSS tracer: a failure now falls back to the fixed viewing spot instead of erroring in the spawn handler.

# 0.8.59

#### July 19, 2026

- Practice grenade library: `.shownades` toggles in-world markers for every saved lineup on the map (yours + the shared pack), `.hidenades` hides them, and `css_shownades` can be bound to a key. Markers and labels are colored by grenade type (smoke blue, flash yellow, HE red, molotov orange, decoy grey).
- Grenade library labels show the type, comment and throw style, are readable from every angle (never mirrored), and no longer clip into walls next to the lineup.
- Grenade library: press E aimed at a marker to teleport to the lineup with the right grenade equipped; lineups saved on the same spot share one marker and F cycles between them (a counter like 1/2 is shown).
- Grenade library: the marker you are standing on hides for you only (no beam blocking the throw) and reappears when you walk away - no need to run `.shownades` again.
- Grenade library shared pack: admins can promote lineups with `.libadd <name>`, remove with `.libremove <name>` and list with `.liblist` - visible to everyone on the server.
- `.savenade <name> [throwtype] <comment>` accepts an optional throw style (jump/run/walk/crouch) as the second word; `.listnades` numbers lineups and `.ln #N` loads by number. Saving without a grenade in hand is allowed (the label shows a blank type).
- New `.nades` menu: browse the grenade library by type and click a lineup to load it (requires the CS2MenuManager plugin).
- New `.warmupbots [count]` (admin): adds aim-warmup bots during the warmup/ready phase; they are removed automatically the moment the knife round or live match starts.
- Practice `.bot` fixes: the engine could pair-spawn a second bot on the other team (leaving a bot on each team) and could even hand you a bot on your OWN team; both are detected and kicked, so `.bot` adds exactly one bot on the opposing team again.
- Practice colored smoke no longer reverts to grey a few seconds after blooming, and rethrown smokes (`.rt` / `.throw`) are colored too.
- Fixed `matchzy_autostart_mode 2` being ignored after `css_plugins restart MatchZy`: the plugin read the convar before config.cfg had re-applied, so a practice server came back up in match mode.
- Dry Run no longer ends after a single round: play as many rounds as you like (with bots or friends) until an admin runs `.exitdry`, which now returns to match warmup instead of forcing practice mode (run `.prac` yourself if wanted).
- `.match` and `.scrim` now print a compact status line (Knife / DemoRec / Playout with colored Enabled/Disabled) plus a `.help` hint, and `.help` during the ready phase shows the same status block with the available commands.
- Coach overhaul: the coach now spawns behind their own team automatically on every map (no per-map file), can no longer be damaged or killed by teammates, and the players' competitive spawns are left untouched (previously everyone was re-teleported each round when a coach was on, which shuffled spawns).
- Fixed scrim.cfg never applying `mp_autoteambalance 0` (the setting was glued into a comment). Note: existing scrim.cfg files on disk keep the old text - delete the file to regenerate it.
- Fixed the first `.ma` / `.nades` menu open stalling the server for over a second (the menu library is now warmed up in the background at plugin load).
- `.shownades` failures now log the full error and a single bad marker no longer prevents the rest from drawing.
- `.matchsetup` wizard: added a Best of 2 (BO2) series option and a "Back to Admin Menu" entry at the top of the wizard.
- Removed the experimental `.predict` grenade predictor along with `matchzy_experimental_predictor` and the `matchzy_predict_*` convars.

# 0.8.58

#### July 18, 2026

- Fixed dot-prefix chat commands (`.ma`, `.match`, `.stopmatch`, ...) doing nothing during match setup: a spectator / unassigned admin is excluded from the internal player map while a match is being set up, which stopped the chat handler from resolving them, so only the `!` versions (e.g. `!ma`) worked. Dot commands now dispatch for those players too.
- Fixed `.stopmatch` and the "Stop Match" button in the `.ma` / `css_ma` admin menu not stopping a match that had been set up but was not yet live (setup / veto / warmup / knife). Stop now works in every pre-live and live state, not just once the match goes live.
- Added `.matchstop` as an alias for stopping/ending a match (alongside the existing `.stopmatch`, `.endmatch`, `.stopgame`, `.endgame`, `.forcestop`, `.forceend`, `.end`, `.exitscrim`).

# 0.8.57

#### July 17, 2026

- Fixed `.bot` spawning two bots at once: the redundant `bot_join_team` before `bot_add_t`/`bot_add_ct` spawned an extra bot, and `bot_quota_mode normal` then refilled any kicked extra. `.bot` now adds exactly one bot (quota is pinned to the tracked bot count).
- Practice now disables the round's team-damage penalties (autokick / team-damage warn/kick / TK punish), so grenade / molotov / friendly-fire testing no longer risks a kick.
- Fixed a lingering ghost body in practice: a player who disconnects mid-practice could leave an orphaned pawn (the round is kept alive so the engine does not always reap it). The disconnecting pawn is now removed on the next frame.
- The ready-status display is controlled by a single convar, `matchzy_ready_hint_style`: `0` = classic center text (default), `1` = HTML READY-UP panel with the native warmup suppressed (panel shows its own `WARMUP` badge and no native pill, at the cost of a frozen `1:00` round timer top-center). This folds in the old `matchzy_ready_hide_warmup_hud` toggle (retired) and the experimental `matchzy_ready_block_warmup_announce` (removed); both are auto-removed from an existing config.cfg on update. Note: hiding the native warmup is what forces CS2 to draw the round timer, and no server value can blank it, hence the frozen `1:00`.
- Hardened practice spawn loading (`.prac`): the spawn scan is now materialized and guarded so it cannot crash the command (fixes an `ArrayTypeMismatchException` seen when running under the AcceleratorCSS Harmony tracer).
- Fixed the ready phase after a live plugin reload (`css_plugins unload/load`): per-player ready state and the gamerules warmup state are now restored on hot reload, so the ready hint counts connected players (with the NotReady list) and displays correctly instead of reading 0/0 or staying invisible until a round restart.

# 0.8.56

#### July 16, 2026

- Practice named position slots: `.savepos <name>` saves a position under a name, `.loadpos <name>` teleports back to it, `.listpos` lists your saved names, and `.delpos <name>` removes one (up to 32 per player). `.savepos` / `.loadpos` with no name keep working as the single default slot.
- Practice flash test: `.flashtest` (or `.ft`) toggles a chat readout of your own blind duration each time you get flashed, for tuning pop-flashes and self-flashes.
- Practice self-flash: `.blind` throws a flashbang at your own face for pop-flash reaction reps (no teammate or client bind needed).
- Practice `.wipe` (or `.clearnades`) clears your grenade throw history (the source for `.last` / `.back` / `.rt` / `.throwindex`) without leaving and re-entering practice.
- Practice `.jt` (`.jumpthrow`) jumpthrows the grenade in your hand, gated behind the new convar `matchzy_experimental_jumpthrow` (default `false`). Experimental: it forces the jump and release server-side, which the engine may ignore on some CS2 builds, so it ships off by default.
- Practice `.cleanup` clears all utility currently on the map (smokes, mollies, infernos, live projectiles).
- Practice `.autoclear` toggles auto-clearing utility: when on, each time a grenade detonates the older utility is removed and only the just-detonated result is kept, for fast lineup iteration.
- Practice `.landmarker` (`.lm`) toggles a marker at each grenade's detonation point so you can see exactly where it landed.
- Practice `.arc` (`.traceline`) toggles drawing the flight path of thrown grenades as an in-world trajectory line.
- Practice saved grenade lineups are now capped at 500 per player, and `.mynades` shows how many you have saved.
- Fixed `.rt` / `.throw` (rethrow last grenade) silently doing nothing after a normal mouse1 throw: a freshly thrown grenade could be recorded with zero velocity (its velocity field lags a frame on current CS2 builds), which the rethrow's zero-velocity guard then dropped. The launch velocity is now recovered from the projectile's movement when the direct read comes back empty.
- Added an experimental grenade landing predictor: `.predict` draws the flight arc and a landing marker for the grenade in your hand, gated behind `matchzy_experimental_predictor` (default `false`). It forward-simulates the throw (with world-collision wall/floor bounces when built against a CounterStrikeSharp API that exposes the trace natives, otherwise a no-collision estimate), tunable live via `matchzy_predict_gravity` / `matchzy_predict_throwspeed` / `matchzy_predict_elasticity` / `matchzy_predict_friction`, with a `matchzy_predict_debug` readout of predicted-vs-actual landing distance for calibration.
- Fixed a countdown timer appearing during the ready phase when the HTML ready panel (`matchzy_ready_hint_style 1`) is used: hiding the native WARMUP banner also dropped `mp_warmup_pausetimer`, so the round timer counted down. The timer is now frozen during the ready phase, matching paused warmup.
- The HTML ready panel now shows a `WARMUP` badge at the top, since the native WARMUP banner is hidden while the panel is up.
- Fixed the HTML ready panel dropping lines (e.g. the NOT READY status) in languages with accented characters such as Danish and Albanian: accented text broke the center-HTML rendering, so it is now escaped to render correctly in every language.
- Practice `.delnade` can now delete multiple lineups at once: `.delnade <name1> <name2> ...` removes each, and `.delnade all` removes every lineup you saved on the current map. It reports which were deleted and which were not found.
- Fixed a rare `ArrayTypeMismatchException` when entering practice (`.prac` -> `GetSpawns`) on servers running a call-history crash tracer: the spawn lists are now pre-sized so the list-grow path that tripped it is never taken.
- Added `matchzy_ready_up_by_ping` (default `true`): set it `false` to stop pinging (middle-mouse / scroll button) from toggling your ready status, for players who ready up by accident.
- The ready panel (and classic ready hint) no longer shows during `.dryrun`, which has no ready gate.
- config.cfg now auto-removes retired convar lines on load, so an upgraded server no longer spams "Unknown command" when config.cfg execs.
- Fixed getting stuck in the spectator/observer camera for several seconds after picking a team during the ready phase (with the HTML ready panel): hiding the native warmup also disabled warmup's auto-respawn, so a fresh team-joiner was not spawned. Players are now respawned on join, with a periodic safety sweep that keeps every T/CT player spawned during the ready phase.

# 0.8.55

#### July 16, 2026

- Practice spawn markers are now interactive: with `.showspawns` active, aim at a spawn marker and press USE (E) to teleport to that spawn. `.hidespawns` (or leaving practice) disarms it.
- Practice spawn markers are now lifted slightly off the floor so they stay visible over shallow water (e.g. de_ancient) instead of sinking out of sight.
- Practice `.back` with no number now steps backward through your grenade history like CS:GO practice mode: the first `.back` jumps to your most recent nade, each further `.back` goes one older, and it stops at the oldest instead of printing a usage message. `.last` and `.back <number>` set the starting point, and the cursor resets when you throw a new nade.
- Fixed practice spawn teleports (`.spawn`, best/worst spawn) tilting the whole player model sideways at steep spawn angles after a recent CS2 update: they now keep the body upright (same fix already used for `.last` / `.back` nade lineups).
- Build now compiles against the fork's CounterStrikeSharp API DLL at `~/CounterStrikeSharp` (1.0.398) instead of the NuGet package (which tops out at 1.0.371), so the plugin matches the newer server runtime ABI. This fixes an `EntryPointNotFound` error triggered on 1.0.39x runtimes.

# 0.8.54

#### July 16, 2026

- Fixed practice rethrow (`.throw` / `.rt` / `.throwsmoke` etc.) only working for flashbangs: smoke / HE / molotov / decoy re-throws silently did nothing when their native `*_Create` signature failed to resolve from `gamedata/matchzy.json`. They now fall back to the managed entity API (like flash always has) so a rethrow always spawns, and log a clear warning when the signature was missing so a stale/undeployed gamedata file is diagnosable. Also added incendiary (CT molotov) rethrow support.
- Fixed re-thrown grenades spinning wrong: the projectile's angular velocity (spin) was being set to its linear launch velocity on rethrow. The real spin is now captured at throw time and replayed, so a rethrown nade tumbles like the original (cosmetic; landing spot was already correct).
- Added `.grt` (`.globalrethrow`, console `css_grt`) in practice: rethrows every player's last thrown grenade at once, for setting up full team executes in one command.
- Fixed `.listnades` / `.loadnade` / `.delnade` / `.importnade` throwing a `FileNotFoundException` (server error spam) on a fresh server before any lineup was saved: the missing `savednades.json` is now treated as empty instead of crashing the command.

# 0.8.53

#### July 15, 2026

- Reworked the "waiting for players" ready screen into a per-player HTML panel: title, progress bar, ready count, CT/T split, current mode (Match / Scrim / Hill / Match Setup), and each player's own READY / NOT READY status, shown in their own language. New convar `matchzy_ready_hint_style` (0 = classic center text, 1 = HTML panel, default `1`) and `matchzy_ready_hint_blink` (blink the NOT READY line to grab attention, style 1 only, default `false`).
- The native "WARMUP" HUD banner can be hidden during the ready phase (with the HTML ready panel) so it no longer overlaps the panel. A "fake warmup" keeps the pre-match ready phase playing like warmup (round never ends, respawn on death, no round-time expiry) while the banner is hidden, and the center panel no longer flashes.
- Fixed the ready panel showing the wrong mode: switching `.scrim` / `.hill` during warmup now updates the panel immediately, and `.hill` -> `.match` no longer leaves the server stuck in hill mode.
- Practice grenade spawns and `.breakrestore` now resolve their signatures by key from CounterStrikeSharp's gamedata instead of hardcoded byte patterns. MatchZy ships its own `gamedata/matchzy.json` (auto-loaded by CounterStrikeSharp, included in the release `.zip` at `addons/counterstrikesharp/gamedata/matchzy.json`), so it works on stock CounterStrikeSharp without editing the core `gamedata.json`. Missing/stale keys degrade gracefully instead of crashing.
- Added `matchzy_ready_clantag_enabled` (default `true`) to toggle the `[READY]` / `[UNREADY]` scoreboard clan tags shown during the ready phase.
- MatchZy now auto-yields the map command when a dedicated map plugin (CS2-SimpleAdmin / CS2MapChange) is installed alongside: it registers neither `css_map` nor handles the `.map` chat command, letting the other plugin own map changes. This avoids a `css_map` ConCommand conflict (which could block players from connecting) and a double map change (two plugins both firing a changelevel disconnected players). Map changes are also debounced, so a single `.map` never changes the map twice even on servers that add `.` as a chat trigger (where `.map` hits both the chat dispatch and `css_map`). `matchzy_map_console_command_enabled` (default `true`) gates the console command; set it `false` to never register `css_map`.

# 0.8.52

#### July 15, 2026

- Added optional `.map` / `css_map` admin command for changing the map. The `css_map` console command is gated by `matchzy_map_console_command_enabled` (default `true`) so it can defer to another plugin such as CS2-SimpleAdmin without a conflict; the `.map` chat command is always available. The map name is validated and resolved before the demo is stopped and bots are kicked (a typo no longer tears the server down and loses the recording), supports workshop ids via `host_workshop_map`, and resolves bare names (e.g. `mirage` -> `de_mirage`).
- Config folder is now case-agnostic: an existing `cfg/MatchZy/` or `cfg/matchzy/` is auto-detected and used for **every** MatchZy file (cfgs, `savednades.json`, `admins.json`, `whitelist.cfg`). Also fixes cfgs failing to exec on case-sensitive Linux when the folder was `MatchZy/` but paths were hardcoded lowercase. Keep only one of the two folders.
- `admins.json` is now actually loaded at startup (was never called before, so it had no effect); only valid SteamID64 entries grant admin, and a reload drops removed admins.
- Fixed practice smoke rethrow (`.rt` / `.throwsmoke`) dropping the smoke dead at the spawn origin. Smoke was excluded from the velocity-apply path.
- Fixed `.loadnade` / `.back` / `.last` auto-throwing the restored grenade (dead into a wall at tight lineups); the pose-clear now redeploys the nade without triggering a throw.
- Fixed `.savenade` storing the position 4 units above the real stance, which made loadnade lineups release from the wrong height.
- Removed the end-of-match summary panel (the center-HTML MVP / clutch / top-frag panel) and its `matchzy_match_summary_panel` and `matchzy_match_summary_panel_duration` convars.

# 0.8.51

#### July 13, 2026

- Grouped the source into concern folders (Core / Match / Practice / Stats / Pause / ...) with no namespace or build change.

# 0.8.50

#### July 11, 2026

- Bumped CounterStrikeSharp API to 1.0.371.
- Fixed a server crash and a phantom death when switching team with `.t` / `.ct` / `.spec` in practice.
- Kept the player model flat on `.last` / `.back` when the lineup was aimed at a steep pitch.
- Auto-clean the stale `publish/` folder on `dotnet publish`.

# 0.8.49

#### July 9, 2026

- Bumped for the Valve game update (build 14168).

# 0.8.48

#### July 2, 2026

- Added ClanTags support and small fixes.
- Release / CI housekeeping (untrack build artifacts, fix `release.yml`).

# 0.8.41 – 0.8.46

#### July 1, 2026

- Re-arm AutoStart on the first player connect so warmup execs after an empty-map load (0.8.46).
- Iterated on the warmup-timer pause so the HUD reliably shows a plain paused "WARMUP" (`mp_warmup_pausetimer` / `mp_warmup_online_enabled` handling) (0.8.41–0.8.45).

# 0.8.40

#### July 1, 2026

- Added the `matchzy_nade_pose_flicker_free` toggle for nade restore.

# 0.8.39

#### July 1, 2026

- Clear the stuck throw-pose on all grenade restores.

# 0.8.38

#### July 1, 2026

- Ship cfgs under `cfg/MatchZy/` in the release artifact.
- Don't create `matchzy/` when `MatchZy/` already exists.
- Added the in-game coach spawn builder.
