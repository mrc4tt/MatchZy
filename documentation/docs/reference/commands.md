# Commands

Commands are grouped by **where they work**. Type them in chat with a dot (`.ready`). The CounterStrikeSharp forms `!ready`, `/ready` and the console form `css_ready` work too. A few commands only exist in the `!` form; they are written that way below. Some short aliases (for example `.rdy`, `.knife`, `.libadd`) only exist as dot commands.

Arguments in `<angle brackets>` are required, arguments in `[square brackets]` are optional. Command words are not case-sensitive; arguments such as team names keep the case you type.

!!! tip "Not sure what you can use?"
    `.help` lists the commands for the phase the server is in right now. Admins can type `.mhelp` to see every admin command they are allowed to run, and the permission for the ones they are not.

## Where commands work

| Phase / mode | What players do there | Jump to |
|---|---|---|
| Anywhere | Help, version, admin announcements, map change | [Anywhere](#anywhere) |
| Warmup and ready check | Ready up before the match | [Warmup](#warmup-and-ready-check) |
| Map veto | Ban and pick maps | [Veto](#map-veto) |
| Knife round | Winner chooses a side | [Knife round](#knife-round) |
| Live match | Pauses, round replays, coaching, surrender | [Live match](#live-match) |
| Practice mode | Spawns, bots, grenades, lineups | [Practice mode](#practice-mode) |
| Dryrun | Live-like rounds without a match | [Dryrun](#dryrun) |
| Server console / RCON | Loading matches, panel integration | [Server console](#server-console) |

## Permissions

| Label | Who has it |
|---|---|
| **Player** | Everyone. |
| **Config** | `@css/config` (SourceMod flag `i`). Match admins. |
| **Map/prac** | `@css/map` or `@custom/prac` (flag `g`). Can switch modes, but not control a running match. |
| **Map** | `@css/map` only. Used by `.map`. |
| **Chat** | `@css/chat` (flag `j`). |
| **Root** | `@css/root`, SteamIDs in MatchZy's `admins.json`, or the server console. |

`@css/root`, SteamIDs in MatchZy's `admins.json` and `matchzy_everyone_is_admin true` pass every check. CounterStrikeSharp `command_overrides` are honored.

---

## Anywhere

| Command | Who | What it does |
|---|---|---|
| `.help` | Player | Lists the commands that make sense in the current phase (practice, warmup, live...). Also `!matchhelp` and `!matchzyhelp`. |
| `.mhelp` | Any admin | Chat: the admin commands you can run, grouped by category. Console: the full list with a check mark per command and the permission you are missing. |
| `.version` | Player | Shows the CS2 build and MatchZy version. Also `!matchzy_version` and `!mikzy_version`. |
| `!color <0-4>` | Player | Picks your teammate color on the radar and scoreboard (0 blue, 1 green, 2 yellow, 3 orange, 4 purple). |
| `.asay <message>` | Chat | Sends an announcement to all chat with the admin prefix. The `!asay` form can be turned off with `matchzy_asay_console_enabled` when another admin plugin owns it; `.asay` always works. |
| `.map <map>` | Map | Changes map before a match has started. Accepts `mirage`, `de_mirage`, a workshop id, `ws/<id>`, or `ws:<name>` for a map in the server's workshop collection. The name is checked first, so a typo does not stop the demo or kick bots. Stands aside automatically if CS2-SimpleAdmin or CS2MapChange is installed, or when `matchzy_map_console_command_enabled` is off. |
| `.rmap` | Root | Reloads the current map. |

### Switching modes (admin)

These start a mode from warmup or from another mode, and are refused once a match has started. `.match`, `.scrim`, `.prac` and `.sleep` also remember the mode across map changes (`matchzy_autostart_mode`).

| Command | Who | What it does |
|---|---|---|
| `.match` | Map/prac | Match mode: warmup and ready check, then knife round, then the live match. |
| `.scrim` `.playout` `.po` | Map/prac | Scrim mode: no knife round and every round is played (no early win). Teams are named after a player on each side. |
| `.hill` | Map/prac | Hill mode: like a match without the knife round, using `hill.cfg`. |
| `.prac` `.tactics` `.training` | Map/prac | Practice mode. See [Practice mode](#practice-mode). |
| `.exitprac` `.noprac` `.exittraining` | Map/prac | Leaves practice and goes back to match warmup. |
| `.dryrun` `.dry` | Map/prac | Starts a dryrun. See [Dryrun](#dryrun). |
| `.warmup` | Config | Goes back to warmup (not once a match has started, and not while `matchzy_warmup_enabled` is off). |
| `.sleep` | Map/prac | Idle mode for an empty server. |

### Admin menus

Need the optional [CS2MenuManager](../getting-started/installation.md#requirements) plugin. Without it these reply with a notice and nothing else changes.

| Command | Who | What it does |
|---|---|---|
| `.ma` `.matchadmin` | Config | Opens the admin menu (navigate with ++w++ ++s++, select with ++e++): set up a match, force start, knife toggle, restart round, stop match, pause and unpause, switch modes. |
| `.matchsetup` | Config | Opens a wizard that builds a match (teams, series length, maps, veto) and loads it. See [Admin menus](../guides/menus.md). |

### Examples

```text
.help                          list the commands for the current phase
.asay Match starts in 5 minutes
.map de_mirage                 change to Mirage
.map anubis                    "de_" is added when the bare name is not a map
.map 3070284539                load a workshop map by id
.map ws:aim_botz               change to a map in the server's workshop collection
!color 2                       yellow teammate color
.scrim                         switch to scrim mode
```

---

## Warmup and ready check

Before a match goes live, players ready up. The match starts when enough players are ready (`matchzy_minimum_ready_required`).

| Command | Who | What it does |
|---|---|---|
| `.ready` `.r` `.rdy` `.gaben` | Player | Marks you ready. Pinging (middle mouse) also toggles ready. While a live match is paused, `.r` votes to unpause instead. |
| `.unready` `.ur` `.notready` `.nr` `.urdy` | Player | Takes back your ready. |
| `.readycheck` `.rc` `.rcheck` | Player | Shows how many players are ready and how many are still missing. |
| `.forceready` | Player | Readies your whole team at once. Only in a loaded match config, only when your team has at least `min_players_to_ready` players, and only if `matchzy_allow_force_ready` is on. |
| `.addreadytime <seconds>` | Config | Gives the teams more time before the ready-up time limit (`matchzy_forfeit_ready_timeout`) runs out. Only in a loaded match with a time limit; the time left can never go above the full limit. |
| `.start` `.force` `.forcestart` | Config | Starts the match now, skipping the ready check. Not in practice or dryrun. |
| `.readyrequired [n]` `.teamsize [n]` | Config | Sets how many ready players are needed (0 to 32). `0` = everyone connected. Without a number it shows the current value. |
| `.knife` `.rk` `.kr` `.kniferound` | Config | Turns the knife round on or off for this match. Also `!roundknife`. |
| `.team1 <name>` `.ctname <name>` | Config | Sets the name of the team currently on CT. |
| `.team2 <name>` `.tname <name>` | Config | Sets the name of the team currently on T. |
| `.whitelist` | Config | Turns the whitelist on or off (`whitelist.cfg`). Also `!wl`. |
| `.globalnades` | Config | Turns shared lineups on or off: while on, new `.savenade` lineups are saved for everyone instead of per player. |
| `.settings` `.configs` `.config` | Config | Shows the current knife, match mode and scrim settings. Also `!options`. |
| `.warmupbots [count]` | Map/prac | Adds bots to shoot at while you wait (default 4, 1 to 10). They leave by themselves when the knife round or the match starts. Run again to remove them. |
| `.coach [t\|ct]` | Player | Become coach. See [Coaching](#coaching). |

### Examples

```text
.ready                         mark yourself ready
.rc                            "4/10 ready, waiting for 6"
.addreadytime 120              two more minutes to ready up
.teamsize 5                    5 ready players needed
.team1 Astralis                name the CT team
.tname Team Vitality           name the T team (spaces are fine)
.warmupbots 6                  six aim bots for warmup
```

## Map veto

Runs after a match config with `"skip_veto": false` is loaded. MatchZy picks one veto captain per team, and the chat tells each team when it is their turn. Only that captain's commands count.

| Command | Who | What it does |
|---|---|---|
| `.ban <map>` | Captain on turn | Removes a map from the pool. |
| `.pick <map>` | Captain on turn | Picks a map to play. |
| `.ct` / `.t` | Captain on turn | Chooses the starting side on a map the other team picked. |
| `.skipveto` `.sv` | Config | Skips the veto and plays the maps in the listed order. Not once the match has started. |

### Examples

```text
.ban de_vertigo
.pick de_inferno
.ct                            start on CT on the map the other team picked
```

## Knife round

| Command | Who | What it does |
|---|---|---|
| `.stay` | Knife winners | Keep your current side and go live. |
| `.switch` `.swap` | Knife winners | Swap sides and go live. |
| `.ct` / `.t` | Knife winners | Pick a side by name (stays or swaps as needed). |

---

## Live match

### Players

| Command | Who | What it does |
|---|---|---|
| `.pause` `.p` | Player | Pauses the match in the next freeze time (also in the knife round). Unpausing needs both teams. Refused while `matchzy_allow_pause` is off. If `matchzy_use_pause_command_for_tactical_pause` is on, this calls a tactical timeout instead. |
| `.unpause` `.up` | Player | Your team's vote to unpause. The game resumes when both teams have voted. A pause called by an admin can only be lifted by an admin. `.r` also works during a pause. |
| `.tech` | Player | Technical pause for connection or hardware trouble. Each team gets `matchzy_max_tech_pauses_allowed` per map (`0` = none, or unlimited with `matchzy_tech_pause_mode 1`). By default each one ends on its own after `matchzy_tech_pause_duration` seconds; with `matchzy_tech_pause_mode 1` it never ends by itself, but after that time either team can unpause alone. |
| `.tac` | Player | Tactical timeout from your team's timeout budget. |
| `.stop` | Player | Vote to replay the current round from its start. A player from each team must type it within 30 seconds. Needs `matchzy_stop_command_available`. With `matchzy_stop_command_no_damage`, it is refused once someone has damaged an opponent. |
| `!gg` | Player | Surrender vote. Your team must be 6 or more rounds behind and all but one of its players must vote (all of them in a team of 2 or fewer). Single-map matches only. |

### Coaching

| Command | Who | What it does |
|---|---|---|
| `.coach [t\|ct]` | Player | Become your own team's coach. Without a side it uses the team you are on; with a side it must be your own. Works in warmup and freeze time, not in the middle of a live round. The coach watches from a spot behind the team during freeze time and is killed right before the round starts, so they spectate the round. With a `coaches` list in the match config, only listed SteamIDs can coach that team. Not in practice mode. |
| `.uncoach` `.play` | Coach | Go back to playing. A player listed only as a coach in the match config cannot. |

More in [Coaching](../guides/coaching.md).

### Admins

| Command | Who | What it does |
|---|---|---|
| `.forcepause` `.fp` | Config | Admin pause. Only an admin can lift it. |
| `.forceunpause` `.fup` | Config | Lifts any pause at once. |
| `.restore <round>` | Config | Restores the match to the start of that round (score, money, equipment). The number is the count of rounds already played when the backup was taken, the same `R` number `.backups` shows. |
| `.restorelast` `.rl` | Config | Restores the previous round. |
| `.restorecurrent` `.rrestore` `.rr` | Config | Restarts the current round from its start. Also `!restartround`. (In practice, `.rr` restarts the practice round instead.) |
| `.backups` `.backup` `.backupmenu` | Config | Lists up to 10 backups of this match, latest round first, with score and the `!restore` command for each. Outside a live match it lists the 5 newest backup files on disk with a `!loadbackup` hint, handy after a server crash. |
| `!loadbackup <file>` | Config | Restores a backup file by name (from `csgo/MatchZyDataBackup`). Rebuilds the match from the file, including a map change if needed. |
| `.listbackups [matchid]` | Config | Lists every backup of a match. Without a match id it uses the current match. |
| `.restart` `.abort` | Config | Resets the match back to warmup. Not in practice or dryrun. |
| `.stopmatch` `.endmatch` `.end` `.matchstop` `.forcestop` `.endgame` `.stopgame` `.endscrim` `.exitscrim` | Config | Stops the match and resets it. Works in setup, veto, warmup, knife and live. The match gets an end time and no winner in the database. |
| `.forceend` | Config | Cancels the match (like `get5_endmatch`). From the console, `get5_endmatch team1` / `team2` ends the series with that team as winner. |
| `.matchgg` `.surrender` | Config | Ends a live match as a surrender. |
| `.autopause` | Root | Turns autopause (pause when a team is short) on or off. |
| `!autopause_minplayers <1-5>` | Root | Players a team needs before autopause kicks in. |
| `!autopause_delay <0-30>` | Root | Seconds to wait before resuming once both teams are full again. |
| `!autopause_status` / `!autopause_check` | Root | Shows the autopause settings / runs the check once now. |

### Examples

```text
.pause                         pause in the next freeze time
.tech                          technical pause
.unpause                       your team's unpause vote
.stop                          both teams type it to replay the round
.coach                         coach the team you are on
.coach ct                      same, while you are on CT
.restore 12                    back to the round played after 12 rounds (R12 in .backups)
.rl                            back to the start of the previous round
.backups                       list backups with their !restore command
!loadbackup matchzy_27_1_round05.json
.listbackups 27                every backup of match 27
!autopause_minplayers 4
```

---

## Practice mode

Everything here works **only in practice mode** (`.prac`). No admin rights needed unless noted.

### Teams and positions

| Command | What it does |
|---|---|
| `.ct` / `.t` / `.spec` | Switches your team. You respawn on the new side and it does not count as a death. |
| `.fas` `.watchme` | Moves every other player to spectator so they can watch you. Map/prac admins only. |
| `.spawn <n>` `.sp <n>` | Teleports you to your team's competitive spawn number N. `.ctspawn <n>` (`.cts`) / `.tspawn <n>` (`.ts`) pick the side. |
| `.bestspawn` / `.worstspawn` | Teleports you to the spawn nearest to / furthest from where you stand. Side variants: `.bestctspawn`, `.besttspawn`, `.worstctspawn`, `.worsttspawn`. |
| `.showspawns` / `.hidespawns` | Draws markers on all competitive spawns. Aim at one and press ++e++ to teleport to it. |
| `.savepos [name]` / `.loadpos [name]` | Saves your position and view, and teleports you back. Without a name it uses one default slot; with a name you get up to 32 slots. Names keep letters, digits, `_` and `-`. |
| `.listpos` / `.delpos <name>` | Lists / deletes your named positions. |

### Bots

| Command | What it does |
|---|---|
| `.bot` | Spawns a bot on the other team at your position, looking where you look. `.tbot` / `.ctbot` choose the team. |
| `.crouchbot` `.cbot` | Same, crouched. `.tcrouchbot` / `.ctcrouchbot` choose the team. Also `!duckbot`. |
| `.boost` / `.crouchboost` `.cboost` | Spawns a bot and puts you on its head, for boost spots. Also `!duckboost`. |
| `.nobot` `.nb` `.removebot` `.kickbot` `.unbot` | Removes the bot nearest to you. |
| `.nobots` `.nbots` `.nbs` `.nbts` `.kbots` `.kickbots` `.clearbots` `.removebots` | Removes all practice bots. |
| `.savebotpos <name>` `.sbp <name>` | Saves where you stand (and if you crouch) as a named bot spot for this map. |
| `.loadbotpos [name]` `.lbp [name]` | Spawns a bot at a saved spot (the closest matching name). Without a name it spawns every saved spot on the map, so a whole setup comes back in one command. |
| `.listbotpos` `.listbp` | Lists the saved bot spots on this map. |
| `.delbotpos <name>` `.dbp <name>` | Deletes a saved bot spot. |
| `.showbotpos` `.showbp` | Shows markers for saved bot spots. |
| `.botjiggle` | Bots strafe left and right, for peek and aim practice. Run again to stop. Width: `matchzy_botjiggle_range`. |

### Grenade history and rethrow

Every grenade you throw is recorded (position, view, how it was thrown). History entries are numbered from 1 (oldest).

| Command | What it does |
|---|---|
| `.last` | Teleports you to where you threw your last grenade, with that grenade in hand. |
| `.back [n]` | Steps back through your history: the first `.back` goes to your newest grenade, each further `.back` one older. `.back 3` jumps to entry 3. |
| `.rethrow` `.rt` `.throw` | Throws your last grenade again from the same spot, without you moving. |
| `.grt` `.globalrethrow` | Rethrows the last grenade of **every** player at once, to see a full team execute. Map/prac admins only. |
| `.throwsmoke` / `.throwflash` / `.thrownade` / `.throwmolotov` / `.throwdecoy` | Rethrows your last grenade of that type. Also as `.rethrowsmoke`, `.rethrowflash`, `.rethrownade` (`.throwgrenade`, `.rethrowgrenade`), `.rethrowmolotov`, `.rethrowdecoy`. |
| `.throwindex <n...>` `.throwidx` | Throws one or more history entries by number. |
| `.lastindex` | Shows the number of your last grenade in the history. |
| `.delay <seconds>` | Adds a delay to your last grenade when rethrown (for timing executes with `.grt`). `.delay 0` clears it. |
| `.wipe` `.clearnades` | Clears your throw history. |
| `.timer` | Stopwatch: type once to start, again to stop. |

### Saved lineups and the grenade library

| Command | What it does |
|---|---|
| `.savenade <name> [throw] [comment]` `.sn` | Saves your current lineup. The optional throw style (`normal`, `jump`/`jt`, `run`, `walk`, `crouch`/`duck`) is shown on the marker; anything after it is a comment. Up to 500 per player. |
| `.loadnade <name>` `.ln` | Teleports you to a saved lineup with the grenade in hand. The closest matching name is used. `.ln #3` loads by the number from `.listnades`. |
| `.listnades [filter]` `.lin` | Numbered list of your lineups and the shared ones on this map. With a filter, only names that contain it. |
| `.delnade <name> [name2...]` `.dn` `.deletenade` | Deletes lineups on this map. Several at once, or `all` for every lineup you have on this map. |
| `.importnade <name> <x> <y> <z> <pitch> <yaw> <roll>` | Imports a lineup from a position and view (for example copied from `getpos`). Also `!in`. |
| `.mynades` | How many lineups you have saved. |
| `.nades` | Menu to browse lineups by grenade type and load one (needs CS2MenuManager). |
| `.shownades` / `.hidenades` | Colored markers with labels at every lineup on the map. Aim at one and press ++e++ to load it; press ++f++ (or `.nadetoggle`) to cycle lineups that share a spot. Bind it with `bind g "css_shownades"`. |
| `.liblist` | Lists the shared lineup library for this map. |
| `.libadd <name>` / `.libremove <name>` | Config admins: add one of your lineups on this map to the shared library, or remove one. |

### Utility and visuals

| Command | What it does |
|---|---|
| `.cleanup` `.clear` | Removes all utility on the map: smokes, fires and flying grenades. |
| `.autoclear` | Each new detonation removes the older utility, so you only see the latest result. |
| `.landmarker` `.lm` | Marks the spot where each grenade detonated. |
| `.arc` `.traceline` | Draws the flight path of thrown grenades in the world. |
| `.traj` `.pip` `.cam` `.nadecam` `.previewnade` `.nadepreview` | Picture-in-picture camera that follows your grenade. |
| `.impacts` | Turns bullet impact markers off / on for you. On by default. |
| `.noflash` `.noblind` | Flashbangs do not blind you. |
| `.flashtest` `.ft` | Prints how long you were blinded each time you get flashed. |
| `.blind` | Throws a flashbang at your face, for pop-flash reaction practice. |
| `.god` | You take no damage. Type again to turn it off. |
| `.solid` | Toggles whether teammates block each other. |
| `.ff` `.fastforward` | Speeds up time for 10 seconds (wait out smokes and fires). Players are frozen meanwhile. |
| `.break` | Breaks all breakable glass, doors and props. |
| `.breakrestore` `.nobreak` | Restores everything `.break` destroyed. |
| `.rs` `.rr` | Restarts the practice round for everyone. Map/prac admins only. |

### Examples

```text
.spawn 3                       your team's spawn 3
.ctspawn 1                     CT spawn 1
.savepos ramp                  save this spot as "ramp"
.loadpos ramp                  back to "ramp"
.ctbot                         CT bot where you stand
.savebotpos apps_close         save a bot spot
.lbp                           spawn every saved bot spot on this map
.back                          to your newest grenade; again for the one before
.back 3                        to history entry 3
.throwindex 2 4 5              throw entries 2, 4 and 5 together
.delay 1.5                     your last grenade waits 1.5 s on rethrow
.savenade ctsmoke jump from T spawn, aim at the antenna
.ln ctsmoke                    load it with the smoke in hand
.ln #3                         load lineup 3 from .listnades
.listnades mid                 only lineups with "mid" in the name
.delnade ctsmoke window        delete two lineups
.importnade xbox -301.5 -1340.2 -160.0 -31.6 98.9 0.0
.libadd ctsmoke                share it in the library (Config admins)
```

## Dryrun

A dryrun plays real rounds (buy time, round timer, money) without a match, for running executes with your team or bots. It keeps going until an admin ends it.

| Command | Who | What it does |
|---|---|---|
| `.dryrun` `.dry` | Map/prac | Starts a dryrun, or restarts it with a clean scoreboard. Not once a match has started. |
| `.exitdry` `.exitdryrun` `.stopdry` `.enddry` | Map/prac | Ends the dryrun and goes back to match warmup. |

## Coach spot tools

For admins tuning where coaches stand on a map.

| Command | Who | What it does |
|---|---|---|
| `.showcoachspawns` | Config | Shows the coach spot for both sides (blue CT, orange T). |
| `.savecoachspawn [t\|ct]` | Config | Saves your position and view as this map's coach spot. Without a side it uses the team you are on. |
| `.listcoachspawns` / `.clearcoachspawns` | Config | Lists / removes the saved coach spots for this map. |
| `.coachtest` | Map/prac | Places you like a coach on your side right now. Run again to release. |

### Examples

```text
.savecoachspawn ct             save the CT coach spot where you stand
.showcoachspawns               check both spots
.coachtest                     try the spot as a coach would see it
```

---

## Server console

Run from the server console, RCON or a panel. Players cannot use these.

| Command | What it does |
|---|---|
| `matchzy_loadmatch <file>` | Loads a [match config](match-config.md) file (path relative to `csgo/`). Refused while another match is loaded. |
| `matchzy_loadmatch_url <url> [header] [value]` | Loads a match config from a URL, optionally with one HTTP header (sent only when both header name and value are given). Alias `get5_loadmatch_url`. |
| `matchzy_addplayer <steamid64> <team1\|team2\|spec> "<name>"` | Adds a player to the loaded match. Not during halftime. Alias `get5_addplayer`. |
| `matchzy_removeplayer <steamid64>` | Removes a player from the loaded match. Not during halftime. Alias `get5_removeplayer`. |
| `matchzy_add_ready_time <seconds>` | Same as `.addreadytime`. Alias `get5_add_ready_time`. |
| `matchzy_loadbackup <file>` | Restores a round backup. Aliases `get5_loadbackup`, `css_loadbackup`. |
| `matchzy_listbackups [matchid]` | Lists backups. Aliases `get5_listbackups`, `css_listbackups`. |
| `get5_endmatch [team1\|team2]` | Cancels the match, or ends the series with the given team as winner (Get5 behavior). Same as `css_forceend`. |
| `get5_status` / `get5_web_available` | Status replies for Get5 panels. |
| `matchzy_version` | Version info. Aliases `mikzy_version`, `css_matchzy_version`, `css_mikzy_version`, `css_version`. |

Most chat admin commands also work from the console in their `css_` form (for example `css_forcepause`, `css_restore 12`, `css_start`), and the console passes every permission check.

### Examples

```text
matchzy_loadmatch cfg/MatchZy/match.json
matchzy_loadmatch_url "https://panel.example/match.json"
matchzy_loadmatch_url "https://panel.example/match.json" "Authorization" "Bearer <token>"
matchzy_addplayer 76561198000000001 team1 "PlayerOne"
matchzy_removeplayer 76561198000000001
matchzy_add_ready_time 300
matchzy_loadbackup matchzy_27_1_round05.json
get5_endmatch team2            end the series with team2 as winner
css_asay Server restarts after this map
```

Settings that can be set from the console are in [Configuration](convars.md).
