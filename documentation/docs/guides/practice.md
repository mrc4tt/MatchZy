# Practice mode

`.prac` turns the server into a practice server: unlimited money, buy anywhere, endless rounds, no team-damage kicks, and bullet impacts on. Every command below is listed in full in [Commands > Practice mode](../reference/commands.md#practice-mode).

## Learn a lineup

1. Throw the grenade.
2. `.last` puts you back where you threw it, grenade in hand. Keep typing `.back` to step through older throws.
3. `.rt` rethrows it from the same spot without you moving, so you can watch it land.
4. Happy with it? `.savenade <name> jump Smoke for CT` stores it (the throw style is optional). :fontawesome-solid-code-fork:
5. Later: `.ln <name>` or `.ln #3` loads it again.

Helpers while learning:

- `.arc` draws the flight path, `.landmarker` marks the detonation point. :fontawesome-solid-code-fork:
- `.autoclear` keeps only the latest utility on the map; `.cleanup` removes it all. :fontawesome-solid-code-fork:
- `.ff` fast-forwards 10 seconds; `.timer` is a stopwatch.
- `.flashtest` shows your blind time; `.blind` pops a flash in your face for reaction reps. :fontawesome-solid-code-fork:
- Detonation times, including molotov burn time, are printed in chat.

## Grenade library :fontawesome-solid-code-fork:

`.shownades` draws a colored marker at every saved lineup on the map (smoke blue, flash yellow, HE red, molotov orange, decoy grey) with a label showing type, comment and throw style.

- Aim at a marker and press ++e++ to teleport there with the right grenade.
- Several lineups on one spot share a marker; press ++f++ to cycle them.
- The marker you stand on hides for you, so it never blocks your throw.
- `.nades` opens a menu to browse lineups by type.
- Admins add lineups to a shared server-wide pack with `.libadd <name>` (`.libremove`, `.liblist`).

## Team executes

- `.delay <seconds>` sets the timing of your last grenade.
- `.grt` rethrows **everyone's** last grenade at once, each with its delay, so a whole execute plays out in one command. :fontawesome-solid-code-fork:
- `.dryrun` then plays the execute in real rounds.

## Bots

- `.bot` places a bot where you stand, looking where you look. `.crouchbot`, `.boost`, `.crouchboost` for other setups.
- `.savebotpos <name>` saves a bot spot; `.loadbotpos` with no name spawns every saved spot on the map, rebuilding a full setup instantly. :fontawesome-solid-code-fork:
- `.showbotpos` shows the saved spots; `.botjiggle` makes bots strafe for peek practice. :fontawesome-solid-code-fork:
- Bot commands respect `-nobots` and never take the CSTV slot, so a practice session cannot kill a running GOTV. :fontawesome-solid-code-fork:

## Positions and spawns

- `.savepos <name>` / `.loadpos <name>`: up to 32 named positions. :fontawesome-solid-code-fork:
- `.showspawns`: markers on every competitive spawn; aim + ++e++ teleports you there. :fontawesome-solid-code-fork:
- `.spawn <n>`, `.bestspawn`, `.worstspawn` to practice spawn-dependent timings.

## Team switching

`.t`, `.ct` and `.spec` switch sides without counting a death. Switching from spectator uses the game's own join flow, so you spawn right away. :fontawesome-solid-code-fork:
