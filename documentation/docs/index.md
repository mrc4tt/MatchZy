---
hide:
  - navigation
  - toc
---

<div class="mz-hero" markdown>

# MikZy <span class="mz-pill">MatchZy fork · @@MATCHZY_VERSION@@</span>

<p class="mz-lead">Match management, practice mode, coaching, live events and stats for CS2 servers running CounterStrikeSharp. Built on MatchZy, extended for hosted competitive servers.</p>

[:material-download: Download @@MATCHZY_VERSION@@](https://git.miksen.me/mikkel/matchzy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip){ .md-button .md-button--primary }
[Get started](getting-started/installation.md){ .md-button }
[What the fork adds](comparison.md){ .md-button }
[Commands](reference/commands.md){ .md-button }

<small>[Download](https://git.miksen.me/mikkel/matchzy/releases/latest) · [Changelog](https://git.miksen.me/mikkel/matchzy/src/branch/main/CHANGELOG.md) · [All releases](https://git.miksen.me/mikkel/matchzy/releases)</small>

</div>

<div class="grid cards" markdown>

-   :material-trophy:{ .lg .middle } **Matches your way**

    ---

    Pugs with a ready check, Get5-style match configs with veto and BO1 to BO5, scrim and hill modes, players vs bots, or an in-game setup wizard.

    [:octicons-arrow-right-24: Running a match](guides/match-setup.md)

-   :material-crosshairs-gps:{ .lg .middle } **Serious practice mode**

    ---

    Grenade history and rethrow, a grenade library with in-world markers, team-wide rethrows, flight arcs, named bot spots and dryruns.

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

    [:octicons-arrow-right-24: Fork vs upstream](comparison.md)

</div>

## Quick start

```text
1. Install CounterStrikeSharp on your CS2 server.
2. Extract MatchZy-<version>.zip into game/csgo/.
3. Restart the server.
4. In game (as admin): .match  → players type .ready
```

Full steps in [Installation](getting-started/installation.md).

## Credits

MikZy is based on MatchZy, created by [Shobhit Pathak](https://github.com/shobhit-pathak/MatchZy). It is maintained by Miksen and keeps the upstream command names and match config format, so upstream guides and Get5 panels keep working.
