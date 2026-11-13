---
title: "Admin-menuer"
sidebar:
  order: 7
---

Menuerne kræver det valgfrie plugin [CS2MenuManager](https://github.com/mrc4tt/CS2MenuManager/releases) (1.0.42+). Uden det kører MatchZy normalt, og menukommandoerne svarer med en besked.

Naviger med <kbd>W</kbd> / <kbd>S</kbd>, vælg med <kbd>E</kbd>, og gå tilbage med tilbage-punktet.

## Admin-menuen `.ma` {#ma-admin-menu}

Rettighed: `@css/config`.

| Sektion | Punkter |
|---|---|
| New Match Setup | Åbner opsætningsguiden nedenfor. |
| Match Control | Tving start, knife til/fra, genstart runde, gendan en runde fra en liste, genstart kamp, tving afslutning, stop kamp. |
| Pause / Unpause | Pause, genoptag, tving pause, tving genoptagelse, taktisk, teknisk. |
| Modes | Warmup, match, practice, forlad practice, dryrun, forlad dryrun. |

## Guiden `.matchsetup` {#matchsetup-wizard}

Rettighed: `@css/config`. Bygger en match config i spillet og loader den, ligesom `matchzy_loadmatch`.

1. **Serie**: BO1, BO2, BO3 eller BO5.
2. **Spiltype**: competitive (5v5), wingman (2v2), rush (3v3) eller 1v1. Se [holdstørrelser](/da/guides/game-modes/#team-sizes).
3. **Baner**: vælg banerne på forhånd (intet veto) fra `matchzymaps.cfg`, eller veto fra hele puljen. Rush viser kun rush-baner (`rush_001`, hvis `matchzymaps.cfg` ikke har nogen); de andre spiltyper udelader rush-baner.
4. **Sider**: knife-runden afgør det, eller spring knife over med hold 1 som CT.
5. **Bekræft**: gennemse det hele, sæt holdnavne med `.team1 <navn>` / `.team2 <navn>` (menuen opdateres), og tryk så **START MATCH**.

Kampen går derefter til veto eller warmup som enhver loadet config. Holdene er ikke låst (alle kan joine begge hold).

## Granatbiblioteket `.nades` {#nades-grenade-library}

Alle spillere i practice mode. Gennemse lineups på den nuværende bane efter type (All, Smoke, Flash, HE, Molotov, Decoy), og vælg et for at loade det.

## Strategier `.strats` {#strats-strategies}

Alle spillere i practice mode. Viser de gemte strategier på den nuværende bane: start eller stop en afspilning, optag, afspil eller ryd hver rolle, eller slet strategien. Se [Strategier](/da/guides/practice/#strategies).
