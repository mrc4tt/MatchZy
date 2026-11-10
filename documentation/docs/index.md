---
title: MatchZy
hide:
  - navigation
  - toc
---

<div class="mz-hero" markdown>
<div class="mz-hero__text" markdown>

[<span class="mz-dot"></span> Latest release **@@MATCHZY_VERSION@@**](https://github.com/mrc4tt/MatchZy/releases/tag/@@MATCHZY_VERSION@@){ .mz-badge }

<h1 class="mz-title">Run CS2 matches <span class="mz-grad">without the hassle.</span></h1>

<p class="mz-lead">Match management, practice mode, coaching, live events and stats for CS2 servers running CounterStrikeSharp. Built on MatchZy, extended for hosted competitive servers.</p>

[:material-download: Download @@MATCHZY_VERSION@@](https://github.com/mrc4tt/MatchZy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip){ .md-button .md-button--primary }
[Get started](getting-started/installation.md){ .md-button }
[Commands](reference/commands.md){ .md-button }

<small>[Changelog](https://github.com/mrc4tt/MatchZy/blob/@@MATCHZY_VERSION@@/CHANGELOG.md) · [All releases](https://github.com/mrc4tt/MatchZy/releases) · [Report an issue](https://github.com/mrc4tt/MatchZy/issues/new)</small>

</div>
<div class="mz-panel" aria-hidden="true">
<div class="mz-panel__glow"></div>
<div class="mz-panel__hud">
<div class="mz-p-warm">● WARMUP</div>
<div class="mz-p-title">READY UP TO START</div>
<div class="mz-p-mode">Mode: Competitive</div>
<div class="mz-p-bar"><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i></i><i></i><i></i><i></i></div>
<div class="mz-p-sides"><span class="mz-p-lbl">Ready (min. 4):</span> <span class="mz-p-ct">CT 2</span> <span class="mz-p-t">T 3</span></div>
<div class="mz-p-you">✔ You are READY</div>
<div class="mz-p-short">3 joined (min. 4): 1 missing</div>
<div class="mz-p-wait">Waiting on: playerA, playerB</div>
</div>
<div class="mz-panel__caption">matchzy_ready_hint_style 1</div>
</div>
</div>

<div class="mz-stats">
<div><b>@@STAT_COMMANDS@@</b><span>chat commands</span></div>
<div><b>@@STAT_SETTINGS@@</b><span>settings</span></div>
<div><b>@@STAT_EVENTS@@</b><span>event types</span></div>
<div><b>@@STAT_LANGUAGES@@</b><span>languages</span></div>
</div>

<div class="grid cards mz-features" markdown>

-   :material-trophy:{ .lg .middle } **Matches your way**

    ---

    Pugs with a ready check, Get5-style match configs with veto and BO1 to BO5, scrim and hill modes, players vs bots, or an in-game setup wizard.

    [:octicons-arrow-right-24: Running a match](guides/match-setup.md)

-   :material-account-group:{ .lg .middle } **5v5, wingman, rush and 1v1**

    ---

    Every team size from one command (`.5v5`, `.wingman`, `.1v1`) or one match config key. Team size, ready count and live cfg follow the mode.

    [:octicons-arrow-right-24: Game modes](guides/game-modes.md)

-   :material-crosshairs-gps:{ .lg .middle } **Serious practice mode**

    ---

    Grenade history and rethrow, a grenade library with in-world markers, bots that shoot back, path recording, whole-team strategy replays and dryruns.

    [:octicons-arrow-right-24: Practice mode](guides/practice.md)

-   :material-whistle:{ .lg .middle } **Coaching that works**

    ---

    Coaches set in the match config, a viewing spot behind the team on every map, and players who never notice a coach is there.

    [:octicons-arrow-right-24: Coaching](guides/coaching.md)

-   :material-broadcast:{ .lg .middle } **Live events**

    ---

    Webhooks for every kill, damage, bomb, grenade, flash, pause and round. Build scoreboards, Discord bots or overlays.

    [:octicons-arrow-right-24: Live events API](guides/events.md)

-   :material-chart-bar:{ .lg .middle } **Stats**

    ---

    SQLite or MySQL, CSV export, and an HLTV-style scoreboard with rating, KAST, opening duels and clutches.

    [:octicons-arrow-right-24: Stats and database](guides/stats.md)

-   :material-shield-check:{ .lg .middle } **Built for hosting**

    ---

    Reliable demo recording and S3 upload, round restores after a crash, auto-pause, safe config updates, and coexistence with CS2-SimpleAdmin.

    [:octicons-arrow-right-24: Installation](getting-started/installation.md) · [Troubleshooting](getting-started/troubleshooting.md)

-   :material-tournament:{ .lg .middle } **Tournament ready**

    ---

    2v2 to 5v5 leagues without an admin: the team size comes from the match config, one `.ready` per team starts it, and the server names who a team is still waiting for.

    [:octicons-arrow-right-24: Hosting tournaments](guides/ready-system.md#tournaments)

</div>

## Quick start

<div class="mz-steps" markdown>

1. Install [CounterStrikeSharp](https://docs.cssharp.dev/) on your CS2 server.
2. Extract `MatchZy-@@MATCHZY_VERSION@@.zip` into `game/csgo/`.
3. Restart the server. `cfg/MatchZy/config.cfg` is created with every setting and its default.
4. In game (as admin): `.match`, then the players type `.ready`.

</div>

Full steps in [Installation](getting-started/installation.md).

## Credits

Based on [MatchZy](https://github.com/shobhit-pathak/MatchZy) by Shobhit Pathak. Maintained by Miksen. Command names and the match config format are unchanged, so existing guides and Get5 panels keep working.
