# Coaching

A coach is part of a team but does not play. In this fork the coach stands at a viewing spot behind the team during freeze time (for the tactical talk), is invisible and cannot be damaged, and is killed at the very end of freeze time so they spectate the round.

## Becoming a coach

- Type `.coach` while on the team you want to coach. `.coach` only works for your own team. :fontawesome-solid-code-fork:
- `.uncoach` goes back to playing.
- `matchzy_coach_enabled false` turns coaching off. :fontawesome-solid-code-fork:

## Coaches in the match config :fontawesome-solid-code-fork:

List coaches per team, the same way as players (object or array of SteamID64s):

```json
"team1": {
  "name": "Team A",
  "players": { "76561198000000001": "Player1", "76561198000000002": "Player2" },
  "coaches": { "76561198000000009": "CoachA" }
}
```

- Listed coaches may join the team, are not kicked by `matchzy_kick_when_no_match_loaded`, and become coach automatically.
- When a team has a `coaches` list, only those SteamIDs can `.coach` it.
- Listed-only coaches cannot `.uncoach` into a player slot.
- Coaches do not count toward `players_per_team` in the ready check and do not need to `.ready`.

Upstream MatchZy cannot set coaches in the match config.

## What players notice

Nothing. The coach does not take a competitive spawn, so the five players always spawn on the normal spots. The coach has no teammate color, does not show up in the kill feed when removed at the end of freeze time, and never passes through the Spectator team. :fontawesome-solid-code-fork:

## Viewing spots :fontawesome-solid-code-fork:

| `matchzy_coaching_mode` | Spot |
|---|---|
| `1` (default) | Hand-tuned spot from `spawns/coach/<map>.json` when the map has one, otherwise computed. |
| `2` | Always computed: behind the team with line of sight, validated against walls and floors, with an overhead camera as a last resort. |

Hand-tuned spots ship for ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train and vertigo. Admins can tune a map in game:

1. `.coachtest` places you like a coach right now (run again to release).
2. `.showcoachspawns` shows both sides' spots.
3. Stand where the coach should be, look the right way, and type `.savecoachspawn t` or `.savecoachspawn ct`.
