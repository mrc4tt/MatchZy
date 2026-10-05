# Ready system

Before a match, scrim or hill goes live, the server waits in warmup. Players tell the server they are ready, and the match starts once enough players are ready.

## The basics

- Type `.ready` (or `.r`) when you are ready, `.unready` to take it back. Pinging (middle mouse) also toggles it (`matchzy_ready_up_by_ping`).
- `.readycheck` shows who the server is still waiting for.
- Coaches never count and do not need to ready.
- Admins can skip the whole thing with `.start`.

How many players must be ready depends on whether a match config is loaded.

## Pugs (no match config)

Without a match config, the server only counts ready players:

- `matchzy_minimum_ready_required` (or `.readyrequired <n>`) is how many players must be ready in total. `0` means everyone on the server.
- `matchzy_ready_per_team <n>` changes it to `n` ready players on CT **and** `n` on T (`1` = one player per team).

## Loaded matches: team sizes come from the match config

When a match is loaded (`matchzy_loadmatch_url`, `matchzy_loadmatch` or `.matchsetup`), MatchZy reads the `players` list of `team1` and `team2` and counts the SteamID64s in it. That is the number of players each team needs on the server. You do not have to set anything for this.

```json
"team1": {
  "name": "Team A",
  "players": {
    "76561198000000001": "alpha1",
    "76561198000000002": "alpha2",
    "76561198000000003": "alpha3",
    "76561198000000004": "alpha4"
  }
},
"team2": {
  "name": "Team B",
  "players": {
    "76561198000000011": "bravo1",
    "76561198000000012": "bravo2",
    "76561198000000013": "bravo3",
    "76561198000000014": "bravo4",
    "76561198000000015": "bravo5"
  }
},
"players_per_team": 5
```

With this config:

| Team | Registered players | Needed on the server |
|---|---|---|
| Team A | 4 | all 4 |
| Team B | 5 | all 5 |

The match starts when all 9 registered players are on the server and ready. That gives a 4v5 without anyone typing anything special. Team A does not wait for a fifth player, because only 4 are registered.

What "ready" means depends on `matchzy_ready_per_team`:

| `matchzy_ready_per_team` | A team is ready when |
|---|---|
| `0` (default) | all its registered players are on the server **and** every one of them typed `.ready` |
| `1` (or more) | all its registered players are on the server **and** 1 (or that many) of them typed `.ready` |

In both modes the match never starts while a registered player is missing. Players who are already in can type `.ready` while they wait; the server remembers it.

Good to know:

- The count is taken from each new match config, so the next match can be 5v5 again.
- It follows the team, not the side: it does not matter whether Team A plays CT or T.
- `players_per_team` is the most a team can have on the server. A sixth registered player (a substitute) waits on Spectator, and a team with 6 registered needs 5.
- Coaches listed under `players` are not counted. A team with `"players": "any"` or a bot team uses `players_per_team`.
- Spectators (casters, admins) are not part of the ready-up.

## When a registered player is missing

If a team is waiting for a registered player, the team sees how many are missing, in chat and on the ready panel, for example `Team 4/5: 1 missing`. Usually you just wait for that player.

If the player will not come, one player on the team can type `.forceready`. That readies the whole team and the team plays without the missing player.

- `.forceready` is optional. If the missing player joins, `.ready` is enough as usual.
- A team may be at most one player short (`matchzy_forceready_max_missing`, default `1`): with `players_per_team 5`, a team needs at least 4 players on the server for `.forceready`. Three players can never force-start.
- `min_players_to_ready` in the match config can only make that stricter (for example `5` means `.forceready` needs the whole team). A lower value, such as the `1` many panels send, has no effect.
- Turn `.forceready` off completely with `matchzy_allow_force_ready false`.

## Join ready mode

`matchzy_ready_mode 1` (loaded matches only) removes `.ready` altogether: a team is ready as soon as its players have joined, and the match starts `matchzy_join_start_delay` seconds after everyone is in (someone leaving stops the countdown). How many players a team needs is `min_players_to_ready` from the match config, or the team's registered players when it is not set.

## Ready display

| `matchzy_ready_hint_style` | Look |
|---|---|
| `0` (default) | Classic center text with the ready count. |
| `1` | HTML **READY-UP** panel per player with a progress bar, ready count and your own status. |
| `2` | A chat reminder listing the players who are not ready. |

In a loaded match the panel counts towards what the teams need (`7 / 9` in the 4v5 above) and shows ready / needed per side. Style `1` also shows the current mode, in your language. The native WARMUP banner is hidden and the round timer shows a frozen `1:00`.

Related: `matchzy_ready_hint_blink` blinks the NOT READY line (style 1), `matchzy_ready_clantag_enabled` shows `[READY]` / `[UNREADY]` on the scoreboard (not in join ready mode, where nobody types `.ready`). Once live, `matchzy_team_clantag_enabled` shows each team's `tag` from the match config instead.

## Warmup bots

`.warmupbots [count]` adds aim bots while players wait. They are removed the moment the knife round or match starts.
