---
title: Match config-builder
new_in: 1.0.24
hide:
  - toc
---

# Match config-builder

Udfyld formularen, og kopiér eller download `match.json`. Intet forlader din browser.

??? note "Sådan virker det"
    1. **Udfyld** kampen, banerne og de to hold. Configen til højre opdateres, mens du skriver, og de problemer, plugin'et ville advare om ved load, vises over den.
    2. **Kopiér** eller **download** configen.
    3. **Load den** på serveren: fra jeres panel med `matchzy_loadmatch_url`, eller læg den i `csgo/cfg/MatchZy/`, og kør `matchzy_loadmatch cfg/MatchZy/match.json`.

    **Importér** indlæser en eksisterende match config, så du kan ændre den. Formularen gemmes i denne browser, indtil du trykker **Nulstil**. Alle felter er forklaret i [Match config JSON](match-config.md), og der er færdige [eksempler](match-examples.md).

<div id="mz-mcb" class="mzt-tool mzt-tool--match-builder">
<p class="mzt-noscript">Builderen kræver JavaScript. Kommer der intet frem her, blokerer en ad- eller tracker-blocker sandsynligvis <code>assets/mz-tools.js</code>: tillad denne side i den, eller brug <a href="../match-config/">Match config JSON</a> og <a href="../match-examples/">eksemplerne</a>.</p>
</div>
