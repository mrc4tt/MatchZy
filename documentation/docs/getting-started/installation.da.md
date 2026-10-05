# Installation

## Krav { #requirements }

| Komponent | Påkrævet | Bemærkninger |
|---|---|---|
| Dedikeret CS2-server | Ja | Linux eller Windows. |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) | Ja | Almindelig CounterStrikeSharp virker. Forken udvikles mod et CounterStrikeSharp-build til .NET 10. |
| [CS2MenuManager](https://git.miksen.me/mikkel/CS2MenuManager/releases) 1.0.42+ | Valgfri | Kun nødvendig for menuerne i spillet (`.ma`, `.matchsetup`, `.nades`). Uden den svarer de kommandoer med en besked. |
| MySQL / MariaDB | Valgfri | SQLite bruges som standard og kræver ingen opsætning. |

## Installer fra release-zippen

[:material-download: Download MatchZy @@MATCHZY_VERSION@@ (.zip)](https://github.com/mrc4tt/MatchZy/releases/download/@@MATCHZY_VERSION@@/MatchZy-@@MATCHZY_VERSION@@.zip){ .md-button .md-button--primary }
[Download](https://github.com/mrc4tt/MatchZy/releases/latest){ .md-button } [Changelog](https://github.com/mrc4tt/MatchZy/blob/@@MATCHZY_VERSION@@/CHANGELOG.md){ .md-button }

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

Første gang skriver MatchZy `cfg/MatchZy/config.cfg` (alle indstillinger med kommentarer), mode-filerne (`live.cfg`, `scrim.cfg`, `prac.cfg`, ...), `admins.json` og `database.json` (SQLite). Ret i `config.cfg` for at tilpasse serveren; se [Konfiguration](../reference/convars.md).

Senere opdateringer: se [Opdatering](updating.md).

!!! tip "Admins"
    MatchZy accepterer CounterStrikeSharps admin-flag (fx `@css/config`, `@css/map`). Du kan også skrive SteamID64'er i `cfg/MatchZy/admins.json`; alle spillere på den liste er fulde MatchZy-admins. Se [Filer og mapper](../reference/files.md#adminsjson).

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
