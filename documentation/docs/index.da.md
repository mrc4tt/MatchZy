---
title: MatchZy
hide:
  - navigation
  - toc
---

<div class="mz-hero" markdown>
<div class="mz-hero__text" markdown>

[<span class="mz-dot"></span> Seneste version **@@MATCHZY_VERSION@@**](https://github.com/mrc4tt/MatchZy/releases/tag/@@MATCHZY_VERSION@@){ .mz-badge }

<h1 class="mz-title">Kør CS2-kampe <span class="mz-grad">uden besvær.</span></h1>

<p class="mz-lead">Kampstyring, practice mode, coaching, live events og statistik til CS2-servere med CounterStrikeSharp. Bygget på MatchZy og udvidet til hostede konkurrenceservere.</p>

[:material-download: Download @@MATCHZY_VERSION@@](https://github.com/mrc4tt/MatchZy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip){ .md-button .md-button--primary }
[Kom i gang](getting-started/installation.md){ .md-button }
[Kommandoer](reference/commands.md){ .md-button }

<small>[Changelog](https://github.com/mrc4tt/MatchZy/blob/@@MATCHZY_VERSION@@/CHANGELOG.md) · [Alle releases](https://github.com/mrc4tt/MatchZy/releases) · [Rapportér en fejl](https://github.com/mrc4tt/MatchZy/issues/new)</small>

</div>
<div class="mz-panel" aria-hidden="true">
<div class="mz-panel__glow"></div>
<div class="mz-panel__hud">
<div class="mz-p-warm">● WARMUP</div>
<div class="mz-p-title">READY UP TO START</div>
<div class="mz-p-mode">Mode: Competitive</div>
<div class="mz-p-bar"><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i class="on"></i><i></i><i></i><i></i><i></i></div>
<div class="mz-p-sides"><span class="mz-p-lbl">Klar (min. 4):</span> <span class="mz-p-ct">CT 2</span> <span class="mz-p-t">T 3</span></div>
<div class="mz-p-you">✔ Du er KLAR</div>
<div class="mz-p-short">3 joinet (min. 4): 1 mangler</div>
<div class="mz-p-wait">Venter på: playerA, playerB</div>
</div>
<div class="mz-panel__caption">matchzy_ready_hint_style 1</div>
</div>
</div>

<div class="mz-stats">
<div><b>@@STAT_COMMANDS@@</b><span>chat-kommandoer</span></div>
<div><b>@@STAT_SETTINGS@@</b><span>indstillinger</span></div>
<div><b>@@STAT_EVENTS@@</b><span>event-typer</span></div>
<div><b>@@STAT_LANGUAGES@@</b><span>sprog</span></div>
</div>

<div class="grid cards mz-features" markdown>

-   :material-trophy:{ .lg .middle } **Kampe på din måde**

    ---

    Pugs med ready-check, match configs i Get5-stil med veto og BO1 til BO5, scrim- og hill-mode, spillere mod bots eller en opsætningsguide i spillet.

    [:octicons-arrow-right-24: Kør en kamp](guides/match-setup.md)

-   :material-account-group:{ .lg .middle } **5v5, wingman, rush og 1v1**

    ---

    Alle holdstørrelser med én kommando (`.5v5`, `.wingman`, `.1v1`) eller én nøgle i match configen. Holdstørrelse, ready-antal og live-cfg følger spiltypen.

    [:octicons-arrow-right-24: Spiltyper](guides/game-modes.md)

-   :material-crosshairs-gps:{ .lg .middle } **Seriøs practice mode**

    ---

    Granathistorik og rethrow, et granatbibliotek med markører på banen, bots der skyder igen, optagelse af ruter, afspilning af hele holdets strategier og dryruns.

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

    [:octicons-arrow-right-24: Installation](getting-started/installation.md) · [Fejlfinding](getting-started/troubleshooting.md)

-   :material-tournament:{ .lg .middle } **Klar til turneringer**

    ---

    2v2 til 5v5-ligaer uden en admin: holdstørrelsen kommer fra match configen, én `.ready` pr. hold starter kampen, og serveren viser, hvem et hold stadig venter på.

    [:octicons-arrow-right-24: Turneringer](guides/ready-system.md#tournaments)

</div>

## Hurtig start

<div class="mz-steps" markdown>

1. Installér [CounterStrikeSharp](https://docs.cssharp.dev/) på din CS2-server.
2. Pak `MatchZy-@@MATCHZY_VERSION@@.zip` ud i `game/csgo/`.
3. Genstart serveren. `cfg/MatchZy/config.cfg` oprettes med alle indstillinger og deres standard.
4. I spillet (som admin): `.match`, og derefter skriver spillerne `.ready`.

</div>

Hele vejledningen står under [Installation](getting-started/installation.md).

Ikke alle sider er oversat endnu. Sider uden dansk version vises på engelsk.

## Tak til

Bygget på [MatchZy](https://github.com/shobhit-pathak/MatchZy) af Shobhit Pathak. Vedligeholdt af Miksen. Kommandonavne og match config-formatet er uændrede, så eksisterende guides og Get5-paneler virker stadig.
