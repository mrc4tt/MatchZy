# Ready-systemet

Før en kamp, scrim eller hill går live, venter serveren i warmup. Spillerne fortæller serveren, at de er klar, og kampen starter, når nok spillere er klar.

## Det grundlæggende

- Skriv `.ready` (eller `.r`), når du er klar, og `.unready` for at fortryde. Et ping (midterste museknap) skifter også (`matchzy_ready_up_by_ping`).
- `.readycheck` viser, hvem serveren stadig venter på.
- Coaches tæller aldrig med og skal ikke skrive `.ready`.
- Admins kan springe det hele over med `.start`.

Hvor mange der skal være klar, afhænger af, om der er loadet en match config.

## Pugs (ingen match config)

Uden match config tæller serveren kun klare spillere:

- `matchzy_minimum_ready_required` (eller `.readyrequired <n>`) er, hvor mange spillere der i alt skal være klar. `0` betyder alle på serveren.
- `matchzy_ready_per_team <n>` ændrer det til `n` klare spillere på CT **og** `n` på T (`1` = én spiller pr. hold).

## Loadede kampe: holdstørrelsen kommer fra match configen { #loaded-matches }

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

I begge tilstande starter kampen aldrig, mens en registreret spiller mangler. Spillere, der allerede er inde, kan skrive `.ready`, mens de venter; serveren husker det.

Godt at vide:

- Tallet tages fra hver ny match config, så næste kamp kan være 5v5 igen.
- Det følger holdet, ikke siden: det er ligegyldigt, om Hold A spiller CT eller T.
- En spiller tæller, når han er helt connected og står på sit holds side. Mens han loader ind eller står på Spectator, tæller han ikke.
- `players_per_team` er det maksimale antal, et hold kan have på serveren. En 6. registreret spiller (reserve) venter på Spectator, og et hold med 6 registreret skal have 5.
- Coaches, der står under `players`, tælles ikke med. Et hold med `"players": "any"` eller et bot-hold bruger `players_per_team`.
- Spectators (kommentatorer, admins) er ikke en del af ready-up.

## Når en registreret spiller mangler

Venter et hold på en registreret spiller, ser holdet, hvor mange der mangler, i chatten og på ready-panelet, fx `Hold 4/5: mangler 1`. Normalt venter man bare på spilleren.

Kommer spilleren ikke, kan én spiller på holdet skrive `.forceready`. Det gør hele holdet klar, og holdet spiller uden den manglende spiller.

- `.forceready` er valgfrit. Joiner den manglende spiller, er `.ready` nok som normalt.
- Et hold må højst mangle én spiller (`matchzy_forceready_max_missing`, standard `1`): med `players_per_team 5` skal holdet have mindst 4 spillere på serveren for at bruge `.forceready`. Tre spillere kan aldrig tvinge kampen i gang.
- `min_players_to_ready` i match configen kan kun gøre det strengere (fx `5` betyder, at `.forceready` kræver hele holdet). En lavere værdi, som det `1` mange paneler sender, har ingen effekt.
- Slå `.forceready` helt fra med `matchzy_allow_force_ready false`.

## Join ready mode

`matchzy_ready_mode 1` (kun loadede kampe) fjerner `.ready` helt: et hold er klar, så snart dets spillere er joinet, og kampen starter `matchzy_join_start_delay` sekunder efter, at alle er inde (forlader nogen serveren, stopper nedtællingen). Hvor mange spillere et hold skal have, er `min_players_to_ready` fra match configen, eller holdets registrerede spillere, hvis den ikke er sat.

## Visning af ready-status

| `matchzy_ready_hint_style` | Udseende |
|---|---|
| `0` (standard) | Klassisk tekst midt på skærmen med antal klar. |
| `1` | HTML-panelet **READY-UP** for hver spiller med statuslinje, antal klar og din egen status. |
| `2` | En påmindelse i chatten med de spillere, der ikke er klar. |

I en loadet kamp tæller panelet mod det, holdene skal bruge (`7 / 9` i 4v5-eksemplet ovenfor), og viser klar / påkrævet pr. side. Style `1` viser også den aktuelle mode på dit sprog. Det almindelige WARMUP-banner skjules, og rundeuret viser et frosset `1:00`.

Relateret: `matchzy_ready_hint_blink` får IKKE KLAR-linjen til at blinke (style 1), `matchzy_ready_clantag_enabled` viser `[READY]` / `[UNREADY]` på scoreboardet (ikke i join ready mode, hvor ingen skriver `.ready`). Når kampen er live, viser `matchzy_team_clantag_enabled` i stedet hvert holds `tag` fra match configen.

## Warmup-bots

`.warmupbots [antal]` tilføjer aim-bots, mens spillerne venter. De fjernes, i det øjeblik knife-runden eller kampen starter.
