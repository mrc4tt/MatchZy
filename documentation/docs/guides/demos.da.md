# Demoer (GOTV)

Når GOTV er slået til (`tv_enable 1`, fra startparametrene eller en cfg), optager MatchZy en demo af hver live-bane.

| Indstilling | Standard | Formål |
|---|---|---|
| `matchzy_demo_path` | `demos/` | Mappe i forhold til `csgo/`. |
| `matchzy_demo_name_format` | `{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}` | Filnavn. Også `{MAPNUMBER}`. |

## Pålidelig optagelse { #reliable-recording }

Demo-optagelse i CS2 kan fejle uden varsel på flere måder (et `mp_restartgame` der stopper optagelsen, et andet plugin der skifter bane, dynamisk CSTV der fjerner botten). Denne fork beskytter mod dem:

- Optagelsen starter, når go-live-genstarten er faldet på plads, i match, scrim og hill.
- Filen tjekkes for at **vokse** på disken, ikke bare for at findes, og den overvåges hele banen. En optagelse, der går i stå, startes igen i en ny fil (op til tre forsøg).
- En genstartet optagelse overskriver aldrig en tidligere fil: den får `_part2`, `_part3`, ..., og alle dele af banen uploades.
- `tv_record_immediate` bruges, så en demo overlever et servercrash.
- `tv_enable_dynamic 0` sættes, så CSTV-botten ikke fjernes, mens ingen ser med.
- Practice-bots tager aldrig CSTV-pladsen.
- "CSTV Recording..." vises først i chatten, når filen findes. Fejler optagelsen, står det i chatten.
- Hver start, bekræftelse, nyt forsøg og stop skrives i serverloggen.

## Upload { #uploading }

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
