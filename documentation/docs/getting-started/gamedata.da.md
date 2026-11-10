# Gamedata

MatchZy finder nogle få native spilfunktioner ved navn i CounterStrikeSharps gamedata. CounterStrikeSharp samler alle `*.json` i sin `gamedata/`-mappe, så den medfølgende `gamedata/matchzy.json` leverer dem uden at ændre i kerne-filen `gamedata.json`. Det er det, der gør, at pluginet kører på almindelig CounterStrikeSharp.

| Nøgle | Type | Bruges af |
|---|---|---|
| `CSmokeGrenadeProjectile_Create` | signatur | Rethrow af granater i practice (`.rt`, `.last`, `.back`) |
| `CHEGrenadeProjectile_Create` | signatur | Rethrow af granater i practice |
| `CMolotovProjectile_Create` | signatur | Rethrow af granater i practice (molotov og incendiary) |
| `CDecoyProjectile_Create` | signatur | Rethrow af granater i practice |
| `CCSGameRules_PostCleanUp` | signatur | `.breakrestore` (genskaber objekter, der kan gå i stykker) |
| `CBasePlayerController_HandleCommand_JoinTeam` | signatur | `.t` / `.ct` fra spectator i practice (det ældre navn `CCSPlayerController_HandleCommandJoinTeam` accepteres også) |
| `CCSPlayer_WeaponServices_SelectItem` | vtable-offset | Lægger en gendannet granat eller et våben i hånden (`.last`, `.back`, `.ln`) |

## Når en nøgle bliver forældet { #when-a-key-goes-stale }

Valve-opdateringer kan flytte disse funktioner. En forældet eller manglende nøgle giver kun en mindre funktion:

- Rethrow af granater falder tilbage til en anden måde at spawne på og logger en advarsel med navnet på den manglende nøgle.
- `.breakrestore`, holdskiftet fra spectator og valget af våben gør ingenting (holdskiftet falder tilbage til den gamle opførsel plus et tip om holdmenuen).
- Loggen viser linjer som `CCSPlayer_WeaponServices_SelectItem offset missing`.

Løsningen er en ny `matchzy.json` til det nye CS2-build (Linux `libserver.so`, Windows `server.dll`). Opdater MatchZy, eller generér signaturerne selv og erstat filen.


## Automatiske opdateringer (Miksens CounterStrikeSharp-fork) { #automatic-updates-miksens-counterstrikesharp-fork }

På den CounterStrikeSharp-fork, MatchZy bygges mod, retter core'en selv en forældet `matchzy.json` efter en CS2-opdatering, før en ny MatchZy-release er ude. Det er slået til som standard (`PluginGamedataAutoUpdate` i `configs/core.json`) og bruger gamedata-feedet i `AutoUpdateURL`.

- Kun signaturer, der ikke længere virker på serveren, bliver skiftet ud, og kun med en signatur fra feedet, der virker her. Fungerende nøgler, offsets og nøgler, feedet ikke kender, bliver som de er.
- Feedets manifest er signeret, og hver fil tjekkes mod sin sha256. Hver ændring gemmes som backup, og en ændring, serveren dør af, rulles tilbage ved næste start.
- `addons/counterstrikesharp/logs/gamedata-status.json` viser, hvad der virker efter starten.
- `PluginGamedataFeeds` i `core.json` angiver feed-filen for en plugin-fil, når core'en ikke selv kan afgøre det (fx en omdøbt kopi).

På almindelig CounterStrikeSharp: opdatér MatchZy (eller udskift `matchzy.json`) som beskrevet ovenfor.
