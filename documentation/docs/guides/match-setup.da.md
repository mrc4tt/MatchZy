# Kør en kamp

Der er tre måder at køre en kamp på, fra hurtig pug til fuldt styret turnering.

=== "Pug (ingen config)"

    1. En admin skriver `.match`.
    2. Spillerne joiner et vilkårligt hold og skriver `.ready`.
    3. Når `matchzy_minimum_ready_required` spillere er klar, starter knife-runden.
    4. Vinderne af knife-runden skriver `.stay` eller `.switch`, og kampen går live.

    Holdene er ikke låst; alle kan joine begge sider.

=== "Guide i spillet"

    En admin skriver `.matchsetup` (kræver [CS2MenuManager](../getting-started/installation.md#krav)).
    Guiden går igennem seriens længde (BO1, BO2, BO3, BO5), baner, veto og hold og loader derefter
    kampen præcis som en match config. Se [Admin-menuer](menus.md).

=== "Match config (panel / turnering)"

    Skriv en [match config JSON](../reference/match-config.md), og load den fra konsollen, RCON eller et panel:

    ```text
    matchzy_loadmatch cfg/MatchZy/match.json
    matchzy_loadmatch_url "https://example.com/match.json" "Authorization" "Bearer <token>"
    ```

    Spillere kan kun joine deres eget hold fra holdmenuen, alle der ikke er med i kampen flyttes
    til Spectator, reserver venter, mens deres side har `players_per_team` spillere, vetoet
    kører, hvis det er sat op, og events sendes til din [remote log URL](events.md).

    Hvor mange spillere hvert hold skal have inde, før kampen kan starte, tælles automatisk ud
    fra holdets `players`-liste. Se [Ready-systemet](ready-system.md#loadede-kampe).

## Serier og veto

- `num_maps` bestemmer seriens længde. `maplist` er banepuljen.
- En pulje med præcis `num_maps` baner, eller `"skip_veto": true`, spiller banerne i rækkefølge.
- Ellers skriver holdene `.ban` og `.pick` i chatten (Get5-standard). Sæt rækkefølgen med `veto_mode`, eller lad MatchZy lave en (`veto_first` vælger, hvilket hold der starter). Forlader en kaptajn serveren, overtager en holdkammerat.
- `map_sides` bestemmer startsiden pr. bane (`team1_ct`, `team2_t`, `knife`, ...).
- `clinch_series` afslutter en BO3 ved 2-0.

Workshop-baner virker alle steder, hvor et banenavn gør: `workshop/<id>`, `ws/<id>` eller `ws:<navn>` (hostet collection).

## Tidsgrænser til turneringer

| Indstilling | Hvad den gør |
|---|---|
| `matchzy_forfeit_ready_timeout 600` | Et hold, der ikke er klar 10 minutter efter ready-fasen startede, taber på walkover. |
| `matchzy_forfeit_veto_ready_timeout 300` | Det samme for ready-up før map-vetoet (`-1` = samme som ovenfor, `0` = ingen grænse). |
| `matchzy_forfeit_leave_timeout 300` | Et hold uden nogen på serveren i 5 minutter under en live-bane taber på walkover. |
| `matchzy_veto_step_timeout 60` | En kaptajn, der ikke banner, picker eller vælger side inden for 60 sekunder, får et tilfældigt valg. |

Er ingen af holdene klar i tide, ender serien uafgjort. Admins kan give mere tid med `.addreadytime <sekunder>`. Get5-navne: `get5_time_to_start` og `get5_time_to_start_veto`.

Alle tre er slået fra (`0`) som standard. En walkover afslutter serien med det andet hold som vinder (`series_end`), ligesom `get5_endmatch team1|team2`.

## Lås serveren til kampen

| Indstilling | Effekt |
|---|---|
| `matchzy_kick_when_no_match_loaded true` | Spillere, der ikke er på den loadede kamps hold, kickes, når de joiner. Alle kickes, når ingen kamp er loadet. Admins er undtaget. |
| `matchzy_whitelist_enabled_default true` | Kun SteamID'er i `whitelist.cfg` må joine. |

## Spillere mod bots

Det ene hold kan være `"players": "any"` (alle mennesker) og det andet `"bots": true`. Bots fyldes op til `players_per_team`, bliver på deres side gennem halvleg og overtid, er altid klar og registreres i statistikken. Banelisten skal være fast. Eksempel i [match config-referencen](../reference/match-config.md#players-vs-bots).

## Efter kampen

- Statistik skrives til databasen og en CSV. Se [Statistik](stats.md).
- Demoen stopper og uploades, hvis det er sat op. Se [Demoer](demos.md).
- Med `matchzy_match_end_auto_changelevel` skifter serveren selv til næste bane. Kampe loadet fra et panel (`matchzy_loadmatch_url`) overlader baneskiftet til panelet.
- Cvars fra match configens `cvars`-blok sættes tilbage (`matchzy_reset_cvars_on_series_end`).

## Stop en kamp

`.stopmatch` (eller knappen Stop Match i `.ma`) stopper kampen i enhver tilstand, fra opsætning til live, og går tilbage til warmup. Databasen får et sluttidspunkt og ingen vinder, så stoppede kampe er lette at kende.
