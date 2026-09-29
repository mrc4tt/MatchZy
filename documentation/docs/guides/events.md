# Live events API

MatchZy POSTs a JSON event to your server whenever something happens in a match. Upstream sends the handful of events a Get5 panel needs; this fork adds a **live scorebot** feed (kills, damage, bomb, grenades, flashes, pauses) so you can build live scoreboards, Discord bots or overlays. :fontawesome-solid-code-fork:

## Setup

Set the URL from a private cfg (exec'd after `config.cfg`) or the match config's `cvars` block. The setting stays active when a match is loaded.

```text
matchzy_remote_log_url "https://example.com/matchzy/events"
matchzy_remote_log_header_key "Authorization"
matchzy_remote_log_header_value "Bearer <token>"
```

A second header pair (`matchzy_remote_log_auth_key` / `matchzy_remote_log_auth_value`) is available for services that need two. :fontawesome-solid-code-fork:

## Delivery

- `POST`, `Content-Type: application/json`, one event per request.
- 10 second timeout, no retries. Anything other than 2xx is logged on the server.
- The `event` field names the event.

## Receiving events

A minimal receiver in Node.js:

```js
import express from "express";

const app = express();
app.use(express.json());

app.post("/matchzy/events", (req, res) => {
  if (req.get("Authorization") !== "Bearer <token>") return res.sendStatus(401);

  const e = req.body;
  switch (e.event) {
    case "player_kill":
      console.log(`${e.killer_name} killed ${e.victim_name} with ${e.weapon}` + (e.headshot ? " (HS)" : ""));
      break;
    case "round_end":
      console.log(`Round ${e.round_number}: ${e.team1.score} - ${e.team2.score}`);
      break;
  }
  res.sendStatus(200);
});

app.listen(3000);
```

See the full list in the [Events reference](../reference/events.md).
