---
title: "Demoer (GOTV)"
sidebar:
  order: 8
---

Når GOTV er slået til (`tv_enable 1`, fra startparametrene eller en cfg), optager MatchZy en demo af hver live-bane.

MatchZy optager kun live-baner (match, scrim, hill), aldrig practice mode eller en tom server. CS2's egen `tv_autorecord 1` er noget andet: den optager hver bane fra den loader, også en tom server i timevis, og nogle hosts slår den til som standard. Vil du kun have match-demoer, så sæt `matchzy_demo_live_only true`: MatchZy sætter så `tv_autorecord 0` og stopper enhver optagelse uden for en live-bane (tjekkes, når den slås til, og et par sekunder efter hver bane-load, da `server.cfg` kan slå `tv_autorecord` til igen). Med `false` (standard) lader MatchZy `tv_autorecord` stå, som du har sat den. At fjerne `tv_autorecord 1` fra `server.cfg` og startparametrene virker også og undgår de få sekunders optagelse ved hver bane-load.

| Indstilling | Standard | Formål |
|---|---|---|
| `matchzy_demo_path` | `demos/` | Mappe i forhold til `csgo/`. |
| `matchzy_demo_name_format` | `{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}` | Filnavn. Også `{MAPNUMBER}`. Bogstaver uden for ASCII omskrives (`æ` til `ae`, `é` til `e`) eller fjernes, så upload-headerne er gyldige. |

Gamle demoer kan slettes automatisk: `matchzy_demo_keep_days 7` (eller et andet antal dage) fjerner `.dem`-filer i `matchzy_demo_path`, der er ældre end det, når pluginet loader og ved series end. Slået fra (`0`) som standard. Demoen, der optages, og demoer, der stadig uploades, slettes aldrig.

## Pålidelig optagelse {#reliable-recording}

Demo-optagelse i CS2 kan fejle uden varsel på flere måder (et `mp_restartgame` der stopper optagelsen, et andet plugin der skifter bane, dynamisk CSTV der fjerner botten). Denne fork beskytter mod dem:

- Optagelsen starter i første runde efter go-live-genstarten, i match, scrim og hill (cirka et sekund efter LIVE).
- Filen tjekkes for at **vokse** på disken, ikke bare for at findes, og den overvåges hele banen. En optagelse, der går i stå, startes igen i en ny fil (op til tre forsøg).
- En genstartet optagelse overskriver aldrig en tidligere fil: den får `_part2`, `_part3`, ..., og alle dele af banen uploades.
- `tv_record_immediate` bruges, så en demo overlever et servercrash.
- Mangler CSTV-botten, når optagelsen skal starte (fx kicket af en ændring i `bot_quota`), genstarter MatchZy GOTV med `tv_enable 0` / `tv_enable 1`. Det virker kun med `tv_enable_dynamic 1`, som får en ændring af `tv_enable` til at virke med det samme i stedet for først på næste bane. Tilføj `+tv_enable_dynamic 1` til startparametrene.
- Practice-bots tager aldrig CSTV-pladsen.
- "CSTV optager kampen." vises først i chatten, når filen findes. Fejler optagelsen, står det i chatten.
- Når banen slutter, skriver chatten, at optagelsen er afsluttet, med demoens filnavn, og om den bliver uploadet. Det står der også, når optagelsen stopper før tid (`.endmatch`, en reset eller et mapskift).
- Hver start, bekræftelse, nyt forsøg og stop skrives i serverloggen.

## Upload {#uploading}

Sæt en upload-URL fra en privat cfg (den kan indeholde et token):

```text
matchzy_demo_upload_url "https://example.com/upload"
matchzy_demo_upload_header_key "Authorization"
matchzy_demo_upload_header_value "Bearer <token>"
```

Uploaden starter kort efter, at banen er slut. To tilstande:

| Tilstand | Forespørgsel |
|---|---|
| Standard | `POST` med demoen som body, plus headerne `MatchZy-FileName`, `MatchZy-MatchId`, `MatchZy-MapNumber`, `MatchZy-RoundNumber` (sendes også som `Get5-*`). |
| S3 (`matchzy_demo_upload_s3 true`) | `PUT` med den rå `.dem` som body og kun din egen header, til S3-kompatible presigned URLs. Signér URL'en med `Content-Type: application/octet-stream`. |

Efter hver upload melder et `demo_upload_ended`-event filnavnet, og om det lykkedes.
