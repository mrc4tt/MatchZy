---
title: "Installation"
sidebar:
  order: 1
---

## Krav {#requirements}

| Komponent | Påkrævet | Bemærkninger |
|---|---|---|
| Dedikeret CS2-server | Ja | Linux eller Windows. |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) | Ja | Almindelig CounterStrikeSharp virker. Forken udvikles mod et CounterStrikeSharp-build til .NET 10. |
| [CS2MenuManager](https://github.com/mrc4tt/CS2MenuManager/releases) 1.0.42+ | Valgfri | Kun nødvendig for menuerne i spillet (`.ma`, `.matchsetup`, `.nades`, `.strats`). Uden den svarer de kommandoer med en besked. |
| MySQL / MariaDB | Valgfri | SQLite bruges som standard og kræver ingen opsætning. |

## Installer fra release-zippen

[Download MatchZy @@MATCHZY_VERSION@@ (.zip)](https://github.com/mrc4tt/MatchZy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip)
[Download](https://github.com/mrc4tt/MatchZy/releases/latest) [Changelog](https://github.com/mrc4tt/MatchZy/blob/@@MATCHZY_VERSION@@/CHANGELOG.md)

1. Installer **CounterStrikeSharp** i `game/csgo/addons/counterstrikesharp/`.
2. Download den nyeste `MatchZy-<version>.zip` (knappen ovenfor eller [releases-siden](https://github.com/mrc4tt/MatchZy/releases)).
3. Pak zippen ud i `game/csgo/`. Den indeholder:

    ```text
    addons/counterstrikesharp/plugins/MatchZy/            # pluginet og dets afhængigheder
    addons/counterstrikesharp/gamedata/matchzy.json       # signaturer til practice mode
    ```

    Zippen indeholder intet under `cfg/`. MatchZy skriver selv sine cfg-filer første gang, det loader, plus referencekopier af standarderne i `cfg/MatchZy/defaults/`.

4. *(Valgfrit)* Installer **CS2MenuManager** som sit eget plugin under `addons/counterstrikesharp/plugins/CS2MenuManager/`, hvis du vil have menuerne i spillet.
5. Genstart serveren, eller kør `css_plugins load MatchZy`.

Første gang skriver MatchZy `cfg/MatchZy/config.cfg` (alle indstillinger med kommentarer), mode-filerne (`live.cfg`, `scrim.cfg`, `prac.cfg`, ...), `admins.json` og `database.json` (SQLite). Ret i `config.cfg` for at tilpasse serveren; se [Konfiguration](/da/reference/convars/).

Senere opdateringer: se [Opdatering](/da/getting-started/updating/).

:::tip[Admins]
MatchZy accepterer CounterStrikeSharps admin-flag (fx `@css/config`, `@css/map`). Du kan også skrive SteamID64'er i `cfg/MatchZy/admins.json`; alle spillere på den liste er fulde MatchZy-admins. Se [Filer og mapper](/da/reference/files/#adminsjson).
:::

## Startparametre {#launch-options}

Tilføj dem til serverens startkommando (eller feltet til startparametre i dit panel):

```text
-dedicated +map de_dust2 +game_type 0 +game_mode 1 -allow_round_backup_files +tv_enable 1 +tv_enable_dynamic 1
```

| Parameter | Hvorfor |
|---|---|
| `-allow_round_backup_files` | **Påkrævet for round-backups.** Uden den skriver CS2 ingen backup-filer, og `.restore`, `.ma` → Restore Round og gendannelse efter et crash virker ikke. |
| `+tv_enable 1` | Slår GOTV til, så MatchZy kan optage demoer. Kan også stå i en cfg. Se [Demoer](/da/guides/demos/). |
| `+tv_enable_dynamic 1` | Anbefalet: en ændring af `tv_enable` virker med det samme i stedet for først på næste bane, så MatchZy kan hente en kicket CSTV-bot tilbage midt på banen. |
| `+game_type 0 +game_mode 1` | Competitive. Til wingman, rush og 1v1 skifter MatchZy selv spiltype. Se [Spiltyper](/da/guides/game-modes/#team-sizes). |
| `+host_workshop_map <id>` / `+host_workshop_collection <id>` | Valgfrit: start på en workshop-bane, eller gør en samlings baner tilgængelige for `.map ws:<navn>`. |

## Byg fra kildekoden

```bash
git clone https://github.com/mrc4tt/MatchZy.git
cd MatchZy
dotnet build -c Release
# output: bin/Release/net10.0/MatchZy.dll (+ afhængigheder)
```

Kopier build-outputtet til `addons/counterstrikesharp/plugins/MatchZy/` og `gamedata/matchzy.json` til `addons/counterstrikesharp/gamedata/`.

Projektet kompileres mod en lokal CounterStrikeSharp API-DLL. Peg på dit eget build med `-p:CssApiDir=/sti/til/CounterStrikeSharp.API/bin/Release/net10.0`. Byg mod den samme CounterStrikeSharp-version, som din server kører; en forskel viser sig som `MissingMethodException`, `TypeLoadException` eller `EntryPointNotFoundException`, når pluginet loader.

## Navnet på config-mappen

MatchZy virker med både `cfg/MatchZy/` og `cfg/matchzy/`. Det finder selv ud af, hvilken der findes (en mappe med præcis `matchzy` med små bogstaver vinder), bruger den til alle filer og skriver den valgte mappe i serverloggen ved opstart. Hav kun én af de to.
