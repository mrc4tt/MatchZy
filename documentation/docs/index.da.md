---
hide:
  - navigation
  - toc
---

<div class="mz-hero" markdown>

# MatchZy <span class="mz-pill">fork · @@MATCHZY_VERSION@@</span>

<p class="mz-lead">Kampstyring, practice mode, coaching, live events og statistik til CS2-servere med CounterStrikeSharp. Bygget på MatchZy og udvidet til hostede konkurrenceservere.</p>

[:material-download: Download @@MATCHZY_VERSION@@](https://github.com/mrc4tt/MatchZy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip){ .md-button .md-button--primary }
[Kom i gang](getting-started/installation.md){ .md-button }
[Kommandoer](reference/commands.md){ .md-button }

<small>[Download](https://github.com/mrc4tt/MatchZy/releases/latest) · [Changelog](https://github.com/mrc4tt/MatchZy/blob/@@MATCHZY_VERSION@@/CHANGELOG.md) · [Alle releases](https://github.com/mrc4tt/MatchZy/releases) · [Rapportér en fejl](https://github.com/mrc4tt/MatchZy/issues/new/choose)</small>

</div>

<div class="grid cards" markdown>

-   :material-trophy:{ .lg .middle } **Kampe på din måde**

    ---

    Pugs med ready-check, match configs i Get5-stil med veto og BO1 til BO5, scrim- og hill-mode, spillere mod bots eller en opsætningsguide i spillet.

    [:octicons-arrow-right-24: Kør en kamp](guides/match-setup.md)

-   :material-crosshairs-gps:{ .lg .middle } **Seriøs practice mode**

    ---

    Granathistorik og rethrow, et granatbibliotek med markører på banen, rethrows for hele holdet, flyvebaner, navngivne bot-pladser og dryruns.

    [:octicons-arrow-right-24: Practice mode](guides/practice.md)

-   :material-whistle:{ .lg .middle } **Coaching der virker**

    ---

    Coaches sat i match configen, en udsigtsplads bag holdet på hver bane, og spillere der aldrig mærker, at der er en coach.

    [:octicons-arrow-right-24: Coaching](guides/coaching.md)

-   :material-broadcast:{ .lg .middle } **Live events**

    ---

    Webhooks for hvert kill, skade, bombe, granat, flash, pause og runde. Byg scoreboards, Discord-bots eller overlays.

    [:octicons-arrow-right-24: Live events API](guides/events.md)

-   :material-chart-bar:{ .lg .middle } **Statistik**

    ---

    SQLite eller MySQL, CSV-eksport og et scoreboard i HLTV-stil med rating, KAST, opening duels og clutches.

    [:octicons-arrow-right-24: Statistik og database](guides/stats.md)

-   :material-shield-check:{ .lg .middle } **Bygget til hosting**

    ---

    Pålidelig demo-optagelse og S3-upload, gendannelse af runder efter et crash, auto-pause, sikre config-opdateringer og sameksistens med CS2-SimpleAdmin.

    [:octicons-arrow-right-24: Opdatering](getting-started/updating.md)

</div>

## Hurtig start

```text
1. Installer CounterStrikeSharp på din CS2-server.
2. Pak MatchZy-<version>.zip ud i game/csgo/.
3. Genstart serveren.
4. I spillet (som admin): .match  → spillerne skriver .ready
```

Hele vejledningen står under [Installation](getting-started/installation.md).

Ikke alle sider er oversat endnu. Sider uden dansk version vises på engelsk.

## Tak til

Bygget på [MatchZy](https://github.com/shobhit-pathak/MatchZy) af Shobhit Pathak. Vedligeholdt af Miksen. Kommandonavne og match config-formatet er uændrede, så eksisterende guides og Get5-paneler virker stadig.
