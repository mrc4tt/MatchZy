---
title: "Ready-systemet"
sidebar:
  order: 3
---

Før en kamp, scrim eller hill går live, venter serveren i warmup. Spillerne fortæller serveren, at de er klar, og kampen starter, når nok spillere er klar.

## Det grundlæggende

- Skriv `.ready` (eller `.r`), når du er klar, og `.unready` for at fortryde. Et ping (midterste museknap) skifter også (`matchzy_ready_up_by_ping`).
- `.readycheck` viser, hvem serveren stadig venter på. I en loadet kamp pr. hold, fx `Team A (CT): 4/5 spillere på siden, 1/1 klar` og `Mangler: alpha5`.
- Kun spillere på T eller CT melder klar. Spectators og coaches tæller aldrig med, og `.ready` eller et ping fra dem ignoreres.
- Admins kan springe det hele over med `.start`.
- `matchzy_ready_enabled false` slår spillernes klarmelding fra: kun en admin starter kampen med `.start`, også før map veto, og klar-tidsgrænsen kører ikke.

Hvor mange der skal være klar, afhænger af, om der er loadet en match config.

## Pugs (ingen match config)

Uden match config tæller serveren kun klare spillere:

- `matchzy_minimum_ready_required` (eller `.readyrequired <n>`) er, hvor mange spillere der i alt skal være klar. `0` betyder alle på serveren.
- `matchzy_ready_per_team <n>` ændrer det til `n` klare spillere på CT **og** `n` på T (`1` = én spiller pr. hold).

## Loadede kampe: holdstørrelsen kommer fra match configen {#loaded-matches}

Når en kamp er loadet (`matchzy_loadmatch_url`, `matchzy_loadmatch` eller `.matchsetup`), læser MatchZy `players`-listen for `team1` og `team2` og tæller SteamID64'erne i den. Det tal er, hvor mange spillere hvert hold skal have på serveren. Du skal ikke sætte noget op for det.

```json
"team1": {
  "name": "Hold A",
  "players": {
    "76561198000000001": "alpha1",
    "76561198000000002": "alpha2",
    "76561198000000003": "alpha3",
    "76561198000000004": "alpha4"
  }
},
"team2": {
  "name": "Hold B",
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

Med den config:

| Hold | Registrerede spillere | Skal være på serveren |
|---|---|---|
| Hold A | 4 | alle 4 |
| Hold B | 5 | alle 5 |

Kampen starter, når alle 9 registrerede spillere er på serveren og klar. Det giver en 4v5, uden at nogen skal skrive noget særligt. Hold A venter ikke på en 5. spiller, fordi der kun er 4 registreret.

Hvad "klar" betyder, afhænger af `matchzy_ready_per_team`:

| `matchzy_ready_per_team` | Et hold er klar, når |
|---|---|
| `0` (standard) | alle dets registrerede spillere er på serveren, **og** hver af dem har skrevet `.ready` |
| `1` (eller mere) | alle dets registrerede spillere er på serveren, **og** 1 (eller det antal) af dem har skrevet `.ready` |

I begge tilstande starter kampen aldrig, mens en registreret spiller mangler. Spillere, der allerede er inde, kan skrive `.ready`, mens de venter; serveren husker det, og kampen starter af sig selv et par sekunder efter, at den sidste registrerede spiller er helt connected og står på siden. Ingen skal skrive `.ready` igen.

Godt at vide:

- `min_players_to_ready` i match configen (kun med `matchzy_ready_per_team 0`; med `1` eller mere skal holdet stadig være fuldt) sænker antallet: er det lavere end det, holdet ellers skal have, gør så mange klar-spillere holdet klar, også mens registrerede spillere mangler. Med `"min_players_to_ready": 1` starter kampen, når én spiller på CT og én på T har skrevet `.ready`. Udelad det (eller sæt det til `players_per_team`) for fuld ready-up.
- Tallet tages fra hver ny match config, så næste kamp kan være 5v5 igen.
- Det følger holdet, ikke siden: det er ligegyldigt, om Hold A spiller CT eller T.
- En spiller tæller, når han er helt connected og står på sit holds side. Mens han loader ind eller står på Spectator, tæller han ikke.
- `players_per_team` er det maksimale antal, et hold kan have på serveren. Står det ikke i match configen, bruges den største roster af de to hold (højst 5 i competitive), så en 4v4-config med to rosters på 4 virker uden. En 6. registreret spiller (reserve) venter på Spectator, og et hold med 6 registreret skal have 5.
- Coaches, der står under `players`, og registrerede spillere, der coacher med `.coach`, tælles ikke med. Et hold med `"players": "any"` eller et bot-hold bruger `players_per_team`.
- Spectators (kommentatorer, admins) er ikke en del af ready-up.

## Turneringer (2v2 til 5v5) {#tournaments}

Til en liga eller turnering, hvor spillerne selv melder klar, og ingen admin skal starte kampen:

På serveren, én gang (`cfg/MatchZy/config.cfg`):

```text
matchzy_ready_per_team 1
```

I hver match config, turneringens holdstørrelse:

| Turnering | Match config |
|---|---|
| 5v5 | `"players_per_team": 5` |
| 4v4 | `"players_per_team": 4` |
| 3v3 | `"players_per_team": 3` |
| 2v2 | `"players_per_team": 2, "game_mode": "wingman"` |

Så:

1. Spillerne skriver `.ready`, når de vil, også mens en holdkammerat stadig mangler. Holdet ser, hvor mange det venter på.
2. En spiller tæller, når han er helt connected og står på holdets side. Spectators (admins, kommentatorer) og coaches tæller aldrig.
3. Når begge hold er fulde, og én spiller pr. hold har skrevet `.ready`, starter kampen af sig selv.

Godt at vide:

- Reserver kan stå under `players`: serveren venter aldrig på flere end `players_per_team`.
- Udelad `min_players_to_ready`. Med `matchzy_ready_per_team 1` bruges det alligevel ikke.
- Et hold, der ikke kan stille fuldt, kan starte med én mand mindre via `.forceready` (`matchzy_forceready_max_missing`), eller slå det fra med `matchzy_allow_force_ready false`, så kun en admin (`.start`) kan starte et hold, der mangler spillere.
- Udelades `players_per_team`, tages det fra den største `players`-liste (højst 5), så skriv præcis spillerne på i det tilfælde.
- Tjek serverkonsollen, når en kamp er loadet: `[LOADMATCH] WARNING`-linjer peger på en roster større end holdstørrelsen, en spiller, der også står som spectator, og lignende fejl, der gør, at kampen ikke starter af sig selv. `.readycheck` viser, hvem et hold venter på.

## Når en registreret spiller mangler

Venter et hold på en registreret spiller, ser holdet, hvem der mangler, i chatten og på ready-panelet, fx `4 joinet (min. 5): mangler alpha5` (panelet viser højst 2 navne; chatten viser alle, markeret `(Spectator)`, hvis de står der). En åben roster (`"any"`) viser kun antallet. Normalt venter man bare på spilleren.

Kommer spilleren ikke, kan én spiller på holdet skrive `.forceready`. Det gør hele holdet klar, og holdet spiller uden den manglende spiller.

- `.forceready` er valgfrit. Joiner den manglende spiller, er `.ready` nok som normalt.
- Et hold må højst mangle én spiller (`matchzy_forceready_max_missing`, standard `1`): med `players_per_team 5` skal holdet have mindst 4 spillere på serveren for at bruge `.forceready`. Tre spillere kan aldrig tvinge kampen i gang.
- `min_players_to_ready` i match configen kan kun gøre det strengere for `.forceready` (fx `5` betyder, at `.forceready` kræver hele holdet).
- Slå `.forceready` helt fra med `matchzy_allow_force_ready false`.

### Minimum holdstørrelse

Som standard kan et hold, der er registreret med færre spillere end `players_per_team`, eller en match config med lav `min_players_to_ready` (paneler sender tit `1`), gøre sig klar med almindelig `.ready`, selvom holdet er for lille. `matchzy_ready_max_missing` sætter en bund for alle måder at gøre sig klar på: med `1` skal et hold have mindst 4 spillere i en 5v5, uanset roster eller match config. `0` = kun fulde hold, `-1` (standard) = slået fra. En admin kan stadig starte kampen med `.start`.

## Join ready mode

`matchzy_ready_mode 1` (kun loadede kampe) fjerner `.ready` helt: et hold er klar, så snart dets spillere er joinet, og kampen starter `matchzy_join_start_delay` sekunder efter, at alle er inde (forlader nogen serveren, stopper nedtællingen). Hvor mange spillere et hold skal have, er `min_players_to_ready` fra match configen, eller holdets registrerede spillere, hvis den ikke er sat.

## Visning af ready-status

| `matchzy_ready_hint_style` | Udseende |
|---|---|
| `0` | Klassisk tekst midt på skærmen med antal klar. |
| `1` (standard) | HTML-panelet **READY-UP** for hver spiller med statuslinje, antal klar og din egen status. |
| `2` | En påmindelse i chatten med de spillere, der ikke er klar. |

I en loadet kamp viser panelet antal klar pr. side og det minimum, hver side skal bruge (`Klar (min. 4): CT 2  T 3`), og et hold, der mangler spillere, ser hvor mange der er joinet (`3 joinet (min. 4): mangler alpha4`, eller `1 mangler` uden roster, vist 30 sekunder efter at den seneste spiller joinede). Style `0` viser samme linje pr. side. Style `1` viser også den aktuelle mode på dit sprog. Det almindelige WARMUP-banner skjules, og rundeuret viser et frosset `1:00`.

Relateret: `matchzy_ready_hint_blink` får IKKE KLAR-linjen til at blinke (style 1), `matchzy_ready_clantag_enabled` viser `[READY]` / `[UNREADY]` på scoreboardet (ikke i join ready mode, hvor ingen skriver `.ready`). Når kampen er live, viser `matchzy_team_clantag_enabled` i stedet hvert holds `tag` fra match configen.

## Warmup-bots

`.warmupbots [antal]` tilføjer aim-bots, mens spillerne venter. De fjernes, i det øjeblik knife-runden eller kampen starter.
