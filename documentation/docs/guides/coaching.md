# Coaching

A coach is part of a team but does not play. In this fork the coach stands at a viewing spot behind the team during freeze time (for the tactical talk), is invisible and cannot be damaged, and is killed at the very end of freeze time so they spectate the round.

## Who can coach

The rules depend on whether the match config gives the team a `coaches` list.

| Situation | Who can `.coach` the team |
|---|---|
| No match loaded (pug, scrim) | Anyone on that team's side. |
| Match config, team **without** `coaches` | Players on that team's `players` roster, while on the team's side. With `matchzy_coach_listed_only true`: nobody. |
| Match config, team **with** `coaches` | Only the SteamIDs in that team's `coaches` list. |
| Any case | Never the opposing team. Being an admin gives no exception. |
| `matchzy_coach_enabled false` | Nobody (also not listed coaches). |

!!! tip "Tournaments"
    - **No coaching at all:** `matchzy_coach_enabled false` in `config.cfg`, or per match in the match config's `cvars` block (`"matchzy_coach_enabled": "false"`, restored after the series).
    - **Only whitelisted coaches:** `matchzy_coach_listed_only true`, then list each team's coaches in `"coaches"`. A team without a list has no coach.

- `.coach` only works for the team you are on.
- `.coach` works in warmup and during freeze time, not in the middle of a live round.
- `.uncoach` goes back to playing (not for a coach who is only in the `coaches` list).
- Someone who is in no roster of a loaded match is moved to Spectator and cannot coach, admins included. To let an admin coach, put their SteamID in the team's `players` or `coaches`.

## Coaches in the match config

List coaches per team, the same way as players (an object keyed by SteamID64, or an array of SteamID64s):

```json
{
  "matchid": 1001,
  "num_maps": 1,
  "maplist": ["de_inferno"],
  "map_sides": ["team1_ct"],
  "players_per_team": 5,
  "min_players_to_ready": 5,
  "team1": {
    "id": "100",
    "name": "Team A",
    "players": {
      "76561198000000001": "Player1",
      "76561198000000002": "Player2",
      "76561198000000003": "Player3",
      "76561198000000004": "Player4",
      "76561198000000005": "Player5",
      "76561198000000006": "Substitute1"
    },
    "coaches": { "76561198000000009": "CoachA" }
  },
  "team2": {
    "id": "200",
    "name": "Team B",
    "players": {
      "76561198000000011": "Player1",
      "76561198000000012": "Player2",
      "76561198000000013": "Player3",
      "76561198000000014": "Player4",
      "76561198000000015": "Player5"
    },
    "coaches": ["76561198000000019"]
  },
  "spectators": {
    "players": { "76561198000000099": "Caster" }
  }
}
```

What this does:

- CoachA picks Team A's side in the team menu (the only side allowed for them) and becomes its coach automatically. Only CoachA can coach Team A; Team A's players (including Substitute1) cannot `.coach`.
- The player with SteamID `...019` is Team B's coach (array form works the same).
- Listed coaches may join even with `matchzy_kick_when_no_match_loaded`, do not count toward `players_per_team` or the ready check, and do not need to `.ready`.
- Remove a team's `coaches` list to let its rostered players coach instead.

## What players notice

Nothing. The coach does not take a competitive spawn, so the five players always spawn on the normal spots. The coach has no teammate color, does not show up in the kill feed when removed at the end of freeze time, and never passes through the Spectator team.

## Viewing spots

| `matchzy_coaching_mode` | Spot |
|---|---|
| `1` (default) | Hand-tuned spot from `spawns/coach/<map>.json` when the map has one, otherwise computed. |
| `2` | Always computed: behind the team with line of sight, validated against walls and floors, with an overhead camera as a last resort. |

Hand-tuned spots ship for ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train and vertigo. Admins can tune a map in game:

1. `.coachtest` places you like a coach right now (run again to release).
2. `.showcoachspawns` shows both sides' spots.
3. Stand where the coach should be, look the right way, and type `.savecoachspawn t` or `.savecoachspawn ct`.
