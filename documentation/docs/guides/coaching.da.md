# Coaching

En coach er en del af et hold, men spiller ikke. I denne fork står coachen på en udsigtsplads bag holdet i freeze time (til den taktiske snak), er usynlig og kan ikke tage skade, og dræbes til allersidst i freeze time, så han ser runden som spectator.

## Hvem kan være coach { #who-can-coach }

Reglerne afhænger af, om match configen giver holdet en `coaches`-liste.

| Situation | Hvem kan skrive `.coach` for holdet |
|---|---|
| Ingen kamp loadet (pug, scrim) | Alle på holdets side. |
| Match config, hold **uden** `coaches` | Spillere på holdets `players`-liste, mens de står på holdets side. Med `matchzy_coach_listed_only true`: ingen. |
| Match config, hold **med** `coaches` | Kun SteamID'erne på holdets `coaches`-liste. |
| Altid | Aldrig modstanderholdet. At være admin giver ingen undtagelse. |
| `matchzy_coach_enabled false` | Ingen (heller ikke coaches på listen). |

!!! tip "Turneringer"
    - **Ingen coaching overhovedet:** `matchzy_coach_enabled false` i `config.cfg`, eller pr. kamp i match configens `cvars`-blok (`"matchzy_coach_enabled": "false"`, sættes tilbage efter serien).
    - **Kun godkendte coaches:** `matchzy_coach_listed_only true`, og skriv så hvert holds coaches i `"coaches"`. Et hold uden liste har ingen coach.

- `.coach` virker kun for det hold, du står på.
- `.coach` virker i warmup og freeze time, ikke midt i en live-runde.
- `.uncoach` går tilbage til at spille (ikke for en coach, der kun står på `coaches`-listen).
- En person, der ikke står på nogen liste i en loadet kamp, flyttes til Spectator og kan ikke være coach, heller ikke admins. Skal en admin kunne coache, så sæt hans SteamID på holdets `players` eller `coaches`.

## Coaches i match configen { #coaches-in-the-match-config }

Skriv coaches pr. hold på samme måde som spillere (et objekt med SteamID64 som nøgle, eller en liste af SteamID64'er):

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

Hvad det gør:

- CoachA vælger Team A's side i holdmenuen (den eneste side, han må vælge) og bliver automatisk holdets coach. Kun CoachA kan coache Team A; Team A's spillere (også Substitute1) kan ikke skrive `.coach`.
- Spilleren med SteamID `...019` er Team B's coach (listeformen virker på samme måde).
- Coaches på listen må joine, også med `matchzy_kick_when_no_match_loaded`, tæller ikke med i `players_per_team` eller ready-check og skal ikke skrive `.ready`.
- Fjern et holds `coaches`-liste, hvis holdets spillere i stedet skal kunne coache.

## Hvad spillerne mærker { #what-players-notice }

Ingenting. Coachen tager ikke et konkurrence-spawn, så de fem spillere spawner altid på de normale pladser. Coachen har ingen holdfarve, vises ikke i killfeeden, når han fjernes til sidst i freeze time, og går aldrig forbi Spectator-holdet.

## Udsigtspladser { #viewing-spots }

| `matchzy_coaching_mode` | Plads |
|---|---|
| `1` (standard) | Håndplaceret plads fra `spawns/coach/<bane>.json`, når banen har en, ellers beregnet. |
| `2` | Altid beregnet: bag holdet med frit udsyn, tjekket mod vægge og gulve, med et kamera ovenfra som sidste udvej. |

Håndplacerede pladser følger med til ancient, ancient_night, anubis, cache, dust2, inferno, mirage, nuke, overpass, train og vertigo. Admins kan justere en bane i spillet:

1. `.coachtest` placerer dig som en coach lige nu (kør igen for at slippe).
2. `.showcoachspawns` viser begge siders pladser.
3. Stil dig, hvor coachen skal stå, kig den rigtige vej, og skriv `.savecoachspawn t` eller `.savecoachspawn ct`.
