# Opdatering

Pak den nye release-zip ud oven i den gamle installation, og genstart serveren (eller `css_plugins reload MatchZy`).

Release-zippen indeholder intet under `cfg/`. En opdatering overskriver derfor aldrig noget, du har rettet i, og laver aldrig en ekstra `cfg/MatchZy/`-mappe ved siden af en `cfg/matchzy/`:

| Fil | Hvad sker der ved en opdatering |
|---|---|
| `config.cfg` | Ikke i zippen. Nye indstillinger **tilføjes** nederst i din fil; dine egne ændringer røres aldrig. Se nedenfor. |
| `database.json` | Ikke i zippen, så dit MySQL-login bevares. En referencekopi ligger i `cfg/MatchZy/defaults/database.json.example`. |
| `live.cfg`, `scrim.cfg`, `prac.cfg`, ... | Ikke i zippen og røres aldrig. De skrives kun, hvis de mangler. Se nedenfor. |
| `<mode>_override.cfg` | Ikke i zippen og skrives aldrig af MatchZy. Det sikreste sted til dine egne ændringer af en mode. Se [Serverens egne indstillinger](../reference/files.md#serverens-egne-indstillinger). |
| `savednades.json`, `botpositions.json`, `whitelist.cfg`, `admins.json` | Ikke i zippen og røres aldrig. |
| `defaults/` | Skrives forfra ved hver opstart. Referencekopier af de nuværende standard-cfg'er, som aldrig køres. Ret ikke i dem; dine ændringer ville gå tabt. |
| `gamedata/matchzy.json` | Erstattes. Læg den altid på serveren sammen med pluginet. |

- **`config.cfg`**: nye indstillinger kommer under overskriften `// --- Added by MatchZy update ---`. En indstilling, du har kommenteret ud, tilføjes ikke igen. Udgåede indstillinger fjernes automatisk, så serveren ikke skriver "Unknown command".
- **`live.cfg`, `scrim.cfg`, `prac.cfg`, ...**: når en release ændrer en standardværdi i en af dem, står det i changelog'en. Sammenlign din fil med kopien i `cfg/MatchZy/defaults/` og kopier de linjer, du vil have, eller slet din fil for at få den nye standard ved næste genstart. Indstillinger, din fil helt mangler, udfyldes automatisk fra MatchZys indbyggede standard, så en ældre fil aldrig mangler noget.

!!! warning "Efter en CS2-opdatering"
    Valve-opdateringer kan flytte de native signaturer i `gamedata/matchzy.json`. Holder practice-rethrows, `.breakrestore` eller `.t`/`.ct` fra spectator op med at virke efter en spilopdatering, så opdater til den nyeste MatchZy-release. Manglende eller forældede signaturer slår kun den berørte funktion fra; de crasher aldrig serveren. Se [Gamedata](gamedata.md).

## Behold dine egne ændringer

Ændringer i `warmup.cfg`, `live.cfg` og de andre mode-filer overlever alle opdateringer. Ulempen: når en release ændrer en standardværdi i en af de filer, får en server med sin egen rettede kopi ikke den nye standard.

For at beholde dine ændringer og stadig få nye standarder skal du lade mode-filerne være, som de er, og i stedet lægge dine ændringer i en override-fil:

1. Opret `<mode>_override.cfg` i samme mappe som mode-filen, fx `cfg/MatchZy/warmup_override.cfg` (eller `cfg/matchzy/`, hvis din server bruger den mappe).
2. Skriv kun de linjer, du har ændret, ikke en kopi af hele filen. En fuld kopi låser alle værdier, så du igen går glip af nye standarder. Find dine ændringer ved at sammenligne med referencekopien: `diff warmup.cfg defaults/warmup.cfg`.
3. Filen køres lige efter `warmup.cfg`, så dine værdier vinder.

```cfg
// cfg/MatchZy/warmup_override.cfg
ammo_grenade_limit_flashbang 0
mp_respawn_immunitytime 5
```

Når dine ændringer ligger i override-filen, kan du slette `warmup.cfg` for at få de nuværende standarder ved næste genstart, uden at miste dine ændringer. Se [Serverens egne indstillinger](../reference/files.md#serverens-egne-indstillinger).

Tjek [changelog'en](../changelog.md) for ting, der kræver handling før en opdatering.
