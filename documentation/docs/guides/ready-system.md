# Ready system

In match, scrim and hill mode the server waits in warmup until enough players are ready.

- Players type `.ready` / `.unready`, or ping (middle mouse) to toggle (`matchzy_ready_up_by_ping`).
- `matchzy_minimum_ready_required` (or `.readyrequired <n>`) sets how many must be ready. `0` = everyone connected.
- `matchzy_ready_mode 1` (loaded matches): nobody types `.ready`. A team is ready once `min_players_to_ready` of its players are on its side, and the match starts `matchzy_join_start_delay` seconds after everyone has joined (someone leaving stops the countdown).
- `matchzy_ready_per_team <n>` makes it per team instead: the match starts once `n` players on CT and `n` on T are ready (`1` = one player per team). In a loaded match it replaces the rule that every player must ready up.
- `.readycheck` shows who is missing.
- With a match config, `.forceready` readies a whole team once it has enough players.
- Coaches do not count and do not need to ready.
- Admins can skip it all with `.start`.

## Ready display

| `matchzy_ready_hint_style` | Look |
|---|---|
| `0` (default) | Classic center text with the ready count. |
| `1` | HTML **READY-UP** panel per player with a progress bar, ready count and your own status. |

Style `1` also shows the CT/T split and the current mode, in your language. The native WARMUP banner is hidden and the round timer shows a frozen `1:00`.

Related: `matchzy_ready_hint_blink` blinks the NOT READY line (style 1), `matchzy_ready_clantag_enabled` shows `[READY]` / `[UNREADY]` on the scoreboard (not in join ready mode, where nobody types `.ready`). Once live, `matchzy_team_clantag_enabled` shows each team's `tag` from the match config instead.

## Warmup bots

`.warmupbots [count]` adds aim bots while players wait. They are removed the moment the knife round or match starts.
