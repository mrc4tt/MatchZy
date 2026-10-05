# Statistik og database

## Database { #database }

SQLite virker uden opsætning (`matchzy.db` i plugin-mappen). Til MySQL / MariaDB skal du rette `cfg/MatchZy/database.json`:

```json
{
  "DatabaseType": "MySQL",
  "MySqlHost": "127.0.0.1",
  "MySqlDatabase": "matchzy",
  "MySqlUsername": "matchzy",
  "MySqlPassword": "secret",
  "MySqlPort": 3306
}
```

Tabellerne oprettes automatisk:

| Tabel | Indhold |
|---|---|
| `matchzy_stats_matches` | Én række pr. kamp: start- og sluttid, vinder, serietype (`BO3`), holdnavne og seriestilling, serveradresse. |
| `matchzy_stats_maps` | Én række pr. bane: banenavn, start- og sluttid, vinder, score. |
| `matchzy_stats_players` | Én række pr. spiller pr. bane: kills, deaths, assists, skade, multi kills, utility- og flash-statistik, 1v1 / 1v2, entries, headshots, penge. Opdateres hver runde. |

En stoppet kamp har et sluttidspunkt og en tom vinder.

Sæt `matchzy_stats_include_bots true` for også at registrere bots (hver bot får et fast id ud fra sit navn).

## CSV-eksport { #csv-export }

Efter hver bane: `csgo/MatchZy_Stats/<matchid>/match_data_map<N>_<matchid>.csv`, spillertabellen for den bane.

## Avanceret statistik { #advanced-stats }

Når banen er slut, skriver MatchZy også et scoreboard i HLTV-stil til `csgo/MatchZy_Stats/<matchid>/<demonavn>_stats.json` (skrives, når der er optaget en demo). Pr. spiller:

| Stat | Betydning |
|---|---|
| `rating` | Rating i 2.0-stil (forenklet): KAST, kills, deaths, impact og ADR. |
| `kast` | % af runder med et kill, en assist, overlevelse eller at blive tradet. |
| `adr`, `hs_percent` | Skade pr. runde, headshot-procent. |
| `opening_kills`, `opening_deaths` | Første kill / første død i en runde. |
| `trade_kills` | Kills på en modstander inden for 5 sekunder efter, at han dræbte en holdkammerat. |
| `1v1`, `1v2` | Clutches som `vundet/forsøg`. |
| `5k`, `4k`, `3k`, `multi_kills` | Runder med flere kills. |

Spillerne er sorteret efter rating. Filen indeholder også bane, dato, antal runder, vinder og holdenes score.

## Skaderapport { #damage-report }

Efter hver runde får spillerne en opsummering i chatten af skade givet og taget (`matchzy_enable_damage_report`).
