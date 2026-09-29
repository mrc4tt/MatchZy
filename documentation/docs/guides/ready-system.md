# Ready system

In match, scrim and hill mode the server waits in warmup until enough players are ready.

- Players type `.ready` / `.unready`, or ping (middle mouse) to toggle :fontawesome-solid-code-fork: (`matchzy_ready_up_by_ping`).
- `matchzy_minimum_ready_required` (or `.readyrequired <n>`) sets how many must be ready. `0` = everyone connected.
- `.readycheck` :fontawesome-solid-code-fork: shows who is missing.
- With a match config, `.forceready` readies a whole team once it has enough players.
- Coaches do not count and do not need to ready. :fontawesome-solid-code-fork:
- Admins can skip it all with `.start`.

## Ready display :fontawesome-solid-code-fork:

| `matchzy_ready_hint_style` | Look |
|---|---|
| `0` (default) | Classic center text with the ready count. |
| `1` | HTML **READY-UP** panel per player: progress bar, ready count, CT/T split, current mode, and your own READY / NOT READY status, in your language. The native WARMUP banner is hidden; the round timer shows a frozen `1:00`. |

Related: `matchzy_ready_hint_blink` blinks the NOT READY line (style 1), `matchzy_ready_clantag_enabled` shows `[READY]` / `[UNREADY]` on the scoreboard.

## Warmup bots :fontawesome-solid-code-fork:

`.warmupbots [count]` adds aim bots while players wait. They are removed the moment the knife round or match starts.
