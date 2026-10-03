# Commands

Commands are grouped by **where they work**. Type them in chat with a dot (`.ready`). The CounterStrikeSharp forms `!ready`, `/ready` and the console form `css_ready` work too. A few commands only exist in the `!` form; they are written that way below.

:fontawesome-solid-code-fork: = added by this fork, not in upstream MatchZy.

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
| **Map/prac** | `@css/map` or `@custom/prac` (flag `g`). Can switch modes and change maps, but not control a running match. |
| **Chat** | `@css/chat` (flag `j`). |
| **Root** | `@css/root`, SteamIDs in MatchZy's `admins.json`, or the server console. |

`@css/root` and `matchzy_everyone_is_admin true` pass every check. CounterStrikeSharp `command_overrides` are honored.

---

## Anywhere

| Command | Who | What it does |
|---|---|---|
| `.help` | Player | Lists the commands that make sense in the current phase (practice, warmup, live...). |
| `.mhelp` :fontawesome-solid-code-fork: | Any admin | Chat: the admin commands you can run, grouped by category. Console: the full list with a check mark per command and the permission you are missing. |
| `.version` | Player | Shows the CS2 build and MatchZy version. |
| `!color <0-4>` :fontawesome-solid-code-fork: | Player | Picks your teammate color on the radar and scoreboard (0 blue, 1 green, 2 yellow, 3 orange, 4 purple). |
| `.asay <message>` | Chat | Sends an announcement to everyone with the admin prefix. |
| `.map <map>` | Map/prac | Changes map before a match has started. Accepts `mirage`, `de_mirage`, a workshop id, `ws/<id>`, or `ws:<name>` for a map in the server's workshop collection :fontawesome-solid-code-fork:. The name is checked first, so a typo does not stop the demo or kick bots. Stands aside automatically if CS2-SimpleAdmin or CS2MapChange is installed. |
| `.rmap` | Root | Reloads the current map. |

### Switching modes (admin)

These start a mode from warmup or from another mode. They also remember the mode across map changes (`matchzy_autostart_mode`).

| Command | Who | What it does |
|---|---|---|
| `.match` | Map/prac | Match mode: warmup and ready check, then knife round, then the live match. |
| `.scrim` `.playout` `.po` :fontawesome-solid-code-fork: | Map/prac | Scrim mode: no knife round and every round is played (no early win). Teams are named after a player on each side. |
| `.hill` :fontawesome-solid-code-fork: | Map/prac | Hill mode: like a match without the knife round, using `hill.cfg`. |
| `.prac` `.tactics` | Map/prac | Practice mode. See [Practice mode](#practice-mode). |
| `.exitprac` | Map/prac | Leaves practice and goes back to match warmup. |
| `.dryrun` `.dry` | Map/prac | Starts a dryrun. See [Dryrun](#dryrun). |
| `.warmup` :fontawesome-solid-code-fork: | Config | Goes back to warmup (not once a match has started). |
| `.sleep` | Map/prac | Idle mode for an empty server. |

### Admin menus :fontawesome-solid-code-fork:

Need the optional [CS2MenuManager](../getting-started/installation.md#requirements) plugin. Without it these reply with a notice and nothing else changes.

| Command | Who | What it does |
|---|---|---|
| `.ma` `.matchadmin` | Config | Opens the admin menu (navigate with ++w++ ++s++, select with ++e++): set up a match, force start, knife toggle, restart round, stop match, pause and unpause, switch modes. |
| `.matchsetup` | Config | Opens a wizard that builds a match (teams, series length, maps, veto) and loads it. See [Admin menus](../guides/menus.md). |

---

## Warmup and ready check

Before a match goes live, players ready up. The match starts when enough players are ready (`matchzy_minimum_ready_required`).

| Command | Who | What it does |
|---|---|---|
| `.ready` `.r` `.rdy` | Player | Marks you ready. Pinging (middle mouse) also toggles ready :fontawesome-solid-code-fork:. |
| `.unready` `.ur` `.notready` | Player | Takes back your ready. |
| `.readycheck` `.rc` :fontawesome-solid-code-fork: | Player | Shows how many players are ready, how many are needed, and how many are missing. |
| `.addreadytime <seconds>` | Admin | Gives the teams more time before the ready-up time limit (`matchzy_forfeit_ready_timeout`) runs out, at most the full time. Also `matchzy_add_ready_time` / `get5_add_ready_time` from the console. |
| `.forceready` | Player | Readies your whole team at once. Only in a loaded match config, only when your team has enough players, and only if `matchzy_allow_force_ready` is on. |
| `.start` `.forcestart` | Config | Starts the match now, skipping the ready check. |
| `.readyrequired <n>` `.teamsize <n>` | Config | Sets how many ready players are needed. `0` = everyone connected. |
| `.knife` `.rk` `.kniferound` | Config | Turns the knife round on or off for this match. |
| `.team1 <name>` `.ctname` / `.team2 <name>` `.tname` | Config | Sets the team names on the scoreboard. |
| `.whitelist` | Config | Turns the whitelist on or off (`whitelist.cfg`). |
| `.settings` `.configs` | Config | Shows the current knife, mode and playout settings. |
| `.warmupbots [count]` :fontawesome-solid-code-fork: | Map/prac | Adds bots to shoot at while you wait (default 4, max 10). They leave by themselves when the knife round or the match starts. Run again to remove them. |
| `.coach` | Player | Become coach. See [Live match](#coaching). |

## Map veto

Runs after a match config with `"skip_veto": false` is loaded. The chat tells each team when it is their turn.

| Command | Who | What it does |
|---|---|---|
| `.ban <map>` | Player (team on turn) | Removes a map from the pool. |
| `.pick <map>` | Player (team on turn) | Picks a map to play. |
| `.ct` / `.t` | Player (team on turn) | Chooses the starting side on a map the other team picked. |
| `.skipveto` `.sv` | Config | Skips the veto and plays the maps in the listed order. |

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
| `.pause` `.p` | Player | Pauses the match in the next freeze time. Unpausing needs both teams. If `matchzy_use_pause_command_for_tactical_pause` is on, this calls a tactical timeout instead. |
| `.unpause` `.up` | Player | Your team's vote to unpause. The game resumes when both teams have voted. |
| `.tech` | Player | Technical pause for connection or hardware trouble. Each team gets `matchzy_max_tech_pauses_allowed` per map; each one ends on its own after `matchzy_tech_pause_duration` seconds. |
| `.tac` | Player | Tactical timeout from your team's timeout budget. |
| `.stop` | Player | Vote to replay the current round from its start. Both teams must type it. With `matchzy_stop_command_no_damage`, it is refused once someone has damaged an opponent. |
| `!gg` :fontawesome-solid-code-fork: | Player | Surrender vote. Your team must be 6 or more rounds behind and all but one of its players must vote. Single-map matches only. |

### Coaching

| Command | Who | What it does |
|---|---|---|
| `.coach` | Player | Become your own team's coach. The coach watches from a spot behind the team during freeze time and is killed right before the round starts, so they spectate the round. With a `coaches` list in the match config, only listed SteamIDs can coach that team :fontawesome-solid-code-fork:. |
| `.uncoach` `.play` | Coach | Go back to playing. |

More in [Coaching](../guides/coaching.md).

### Admins

| Command | Who | What it does |
|---|---|---|
| `.forcepause` `.fp` | Config | Admin pause. Only an admin can lift it. |
| `.forceunpause` `.fup` | Config | Lifts any pause at once. |
| `.restore <round>` | Config | Restores the match to the start of that round (score, money, equipment). |
| `.restorelast` `.rl` :fontawesome-solid-code-fork: | Config | Restores the previous round. |
| `.restorecurrent` `.rrestore` `.rr` :fontawesome-solid-code-fork: | Config | Restarts the current round from its start. Also `!rr` and `!restartround`. (In practice, `.rr` restarts the practice round.) |
| `.backups` `.backup` `.backupmenu` :fontawesome-solid-code-fork: | Config | Lists this match's backups with score and a restore hint. Outside a live match it lists the newest backup files on disk, handy after a server crash. |
| `!loadbackup <file>` :fontawesome-solid-code-fork: | Config | Restores a backup file by name. |
| `.listbackups [matchid]` | Config | Lists every backup of a match. |
| `.restart` | Config | Resets the match back to warmup. |
| `.stopmatch` `.endmatch` `.end` `.matchstop` | Config | Stops the match and resets it. Works in setup, veto, warmup, knife and live :fontawesome-solid-code-fork:. The match gets an end time and no winner in the database. |
| `.forceend` | Config | Cancels the match (like `get5_endmatch`). From the console, `get5_endmatch team1` / `team2` ends the series with that team as winner. |
| `.matchgg` `.surrender` :fontawesome-solid-code-fork: | Config | Ends the match as a surrender. |
| `.autopause` :fontawesome-solid-code-fork: | Root | Turns autopause (pause when a team is short) on or off. Also `!autopause_minplayers <n>`, `!autopause_delay <s>` and `!autopause_status`. |

---

## Practice mode

Everything here works **only in practice mode** (`.prac`). No admin rights needed unless noted.

### Teams and positions

| Command | What it does |
|---|---|
| `.ct` / `.t` / `.spec` | Switches your team. You respawn on the new side and it does not count as a death. |
| `.fas` `.watchme` | Moves every other player to spectator so they can watch you. Admin only. |
| `.spawn <n>` `.sp` | Teleports you to your team's competitive spawn number N. `.ctspawn <n>` / `.tspawn <n>` pick the side. |
| `.bestspawn` / `.worstspawn` | Teleports you to the spawn nearest to / furthest from where you stand. Side variants: `.bestctspawn`, `.besttspawn`, `.worstctspawn`, `.worsttspawn`. |
| `.showspawns` / `.hidespawns` | Draws markers on all competitive spawns. Aim at one and press ++e++ to teleport to it :fontawesome-solid-code-fork:. |
| `.savepos [name]` / `.loadpos [name]` | Saves your position and view, and teleports you back. With a name :fontawesome-solid-code-fork: you get up to 32 slots. |
| `.listpos` / `.delpos <name>` :fontawesome-solid-code-fork: | Lists / deletes your named positions. |

### Bots

| Command | What it does |
|---|---|
| `.bot` | Spawns a bot on the other team at your position, looking where you look. `.tbot` / `.ctbot` choose the team. |
| `.crouchbot` `.cbot` | Same, crouched. `.tcrouchbot` / `.ctcrouchbot` :fontawesome-solid-code-fork: choose the team. |
| `.boost` / `.crouchboost` `.cboost` | Spawns a bot and puts you on its head, for boost spots. |
| `.nobot` :fontawesome-solid-code-fork: | Removes the bot nearest to you. |
| `.nobots` | Removes all practice bots. |
| `.savebotpos <name>` `.sbp` :fontawesome-solid-code-fork: | Saves where you stand (and if you crouch) as a named bot spot for this map. |
| `.loadbotpos [name]` `.lbp` :fontawesome-solid-code-fork: | Spawns a bot at a saved spot. Without a name it spawns every saved spot on the map, so a whole setup comes back in one command. |
| `.listbotpos` / `.delbotpos <name>` / `.showbotpos` :fontawesome-solid-code-fork: | Lists / deletes / shows markers for saved bot spots. |
| `.botjiggle` :fontawesome-solid-code-fork: | Bots strafe left and right, for peek and aim practice. Width: `matchzy_botjiggle_range`. |

### Grenade history and rethrow

Every grenade you throw is recorded (position, view, how it was thrown).

| Command | What it does |
|---|---|
| `.last` | Teleports you to where you threw your last grenade, with that grenade in hand. |
| `.back [n]` | Steps back through your history: each `.back` goes one older. `.back 3` jumps to entry 3. |
| `.rethrow` `.rt` `.throw` | Throws your last grenade again from the same spot, without you moving. |
| `.grt` `.globalrethrow` :fontawesome-solid-code-fork: | Rethrows the last grenade of **every** player at once, to see a full team execute. Admin only. |
| `.throwsmoke` / `.throwflash` / `.thrownade` / `.throwmolotov` / `.throwdecoy` | Rethrows your last grenade of that type. |
| `.throwindex <n...>` / `.lastindex` | Throws history entries by number / shows the number of your last one. |
| `.delay <seconds>` | Adds a delay to your last grenade when rethrown (for timing executes with `.grt`). |
| `.wipe` `.clearnades` :fontawesome-solid-code-fork: | Clears your throw history. |
| `.timer` | Stopwatch: type once to start, again to stop. |

### Saved lineups and the grenade library

| Command | What it does |
|---|---|
| `.savenade <name> [jump\|run\|walk\|crouch] [comment]` `.sn` | Saves your current lineup. The optional throw style :fontawesome-solid-code-fork: is shown on the marker. Up to 500 per player. |
| `.loadnade <name>` `.ln` | Teleports you to a saved lineup with the grenade in hand. `.ln #3` loads by number :fontawesome-solid-code-fork:. |
| `.listnades [filter]` `.lin` | Numbered list of your lineups. |
| `.delnade <name> [name2...]` `.dn` | Deletes lineups. Several at once, or `all` for this map :fontawesome-solid-code-fork:. |
| `.importnade <code>` | Imports a lineup from a shared code. |
| `.mynades` :fontawesome-solid-code-fork: | How many lineups you have saved. |
| `.nades` :fontawesome-solid-code-fork: | Menu to browse lineups by grenade type and load one (needs CS2MenuManager). |
| `.shownades` / `.hidenades` :fontawesome-solid-code-fork: | Colored markers with labels at every lineup on the map. Aim at one and press ++e++ to load it; press ++f++ to cycle lineups that share a spot. |
| `.liblist` :fontawesome-solid-code-fork: | Lists the shared lineup library for this map. |
| `.libadd <name>` / `.libremove <name>` :fontawesome-solid-code-fork: | Config admins: add one of your lineups to the shared library, or remove one. |

### Utility and visuals

| Command | What it does |
|---|---|
| `.clear` | Removes grenades that are currently active. |
| `.cleanup` :fontawesome-solid-code-fork: | Removes all utility on the map: smokes, fires and flying grenades. |
| `.autoclear` :fontawesome-solid-code-fork: | Each new detonation removes the older utility, so you only see the latest result. |
| `.landmarker` `.lm` :fontawesome-solid-code-fork: | Marks the spot where each grenade detonated. |
| `.arc` `.traceline` :fontawesome-solid-code-fork: | Draws the flight path of thrown grenades in the world. |
| `.traj` `.pip` `.cam` | Picture-in-picture camera that follows your grenade. |
| `.impacts` | Turns bullet impact markers off / on for you. On by default :fontawesome-solid-code-fork:. |
| `.noflash` | Flashbangs do not blind you. |
| `.flashtest` `.ft` :fontawesome-solid-code-fork: | Prints how long you were blinded each time you get flashed. |
| `.blind` :fontawesome-solid-code-fork: | Throws a flashbang at your face, for pop-flash reaction practice. |
| `.god` | You take no damage. |
| `.solid` | Toggles whether teammates block each other. |
| `.ff` `.fastforward` | Speeds up time for 10 seconds (wait out smokes and fires). |
| `.break` | Breaks all breakable glass, doors and props. |
| `.breakrestore` :fontawesome-solid-code-fork: | Restores everything `.break` destroyed. |
| `.rs` `.rr` :fontawesome-solid-code-fork: | Restarts the practice round for everyone. Admin only. |

## Dryrun

A dryrun plays real rounds (buy time, round timer, money) without a match, for running executes with your team or bots. It keeps going until an admin ends it.

| Command | Who | What it does |
|---|---|---|
| `.dryrun` `.dry` | Map/prac | Starts a dryrun, or restarts it with a clean scoreboard. |
| `.exitdry` :fontawesome-solid-code-fork: | Map/prac | Ends the dryrun and goes back to match warmup. |

## Coach spot tools :fontawesome-solid-code-fork:

For admins tuning where coaches stand on a map.

| Command | Who | What it does |
|---|---|---|
| `.showcoachspawns` | Config | Shows the coach spot for both sides (blue CT, orange T). |
| `.savecoachspawn [t\|ct]` | Config | Saves your position and view as this map's coach spot. |
| `.listcoachspawns` / `.clearcoachspawns` | Config | Lists / removes the saved coach spots for this map. |
| `.coachtest` | Map/prac | Places you like a coach on your side right now. Run again to release. |

---

## Server console

Run from the server console, RCON or a panel. Players cannot use these.

| Command | What it does |
|---|---|
| `matchzy_loadmatch <file>` | Loads a [match config](match-config.md) file (path relative to `csgo/`). |
| `matchzy_loadmatch_url <url> [header] [value]` | Loads a match config from a URL, optionally with an auth header. Alias `get5_loadmatch_url`. |
| `matchzy_addplayer <steamid64> <team1\|team2\|spec> "<name>"` | Adds a player to the loaded match. Alias `get5_addplayer`. |
| `matchzy_removeplayer <steamid64>` | Removes a player. Alias `get5_removeplayer`. |
| `matchzy_loadbackup <file>` | Restores a round backup. Alias `get5_loadbackup`. |
| `matchzy_listbackups [matchid]` | Lists backups. Alias `get5_listbackups`. |
| `get5_endmatch [team1\|team2]` | Cancels the match, or ends the series with the given team as winner (Get5 behavior). |
| `get5_status` / `get5_web_available` | Status replies for Get5 panels. |
| `matchzy_version` | Version info. Alias `mikzy_version`. |

Settings that can be set from the console are in [Configuration](convars.md).
