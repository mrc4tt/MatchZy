# Practice mode

`.prac` gør serveren til en practice-server: ubegrænsede penge, køb overalt, uendelige runder, ingen kicks for holdskade, og markører for kugleskud slået til. Alle kommandoer nedenfor står samlet under [Kommandoer > Practice mode](../reference/commands.md#practice-mode).

## Lær et lineup { #learn-a-lineup }

1. Kast granaten.
2. `.last` sætter dig tilbage, hvor du kastede den, med granaten i hånden. Skriv `.back` igen og igen for at gå gennem ældre kast.
3. `.rt` kaster den igen fra samme sted, uden at du flytter dig, så du kan se den lande.
4. Tilfreds? `.savenade <navn> jump Smoke til CT` gemmer den (kastemåden er valgfri).
5. Senere: `.ln <navn>` eller `.ln #3` loader den igen.

Hjælp, mens du lærer:

- `.arc` tegner flyvebanen, `.landmarker` markerer, hvor den detonerede.
- `.autoclear` beholder kun den seneste utility på banen; `.cleanup` fjerner det hele.
- `.ff` spoler 10 sekunder frem; `.timer` er et stopur.
- `.flashtest` viser, hvor længe du var blændet; `.blind` kaster en flash i hovedet på dig, så du kan øve reaktionen.
- Detonationstider, også hvor længe en molotov brænder, skrives i chatten.

## Granatbiblioteket { #grenade-library }

`.shownades` tegner en farvet markør ved hvert gemt lineup på banen (smoke blå, flash gul, HE rød, molotov orange, decoy grå) med en tekst, der viser type, kommentar og kastemåde.

- Sigt på en markør, og tryk ++e++ for at teleportere dertil med den rigtige granat.
- Flere lineups på samme sted deler en markør; tryk ++f++ for at skifte mellem dem.
- Markøren, du står på, skjules for dig, så den aldrig er i vejen for dit kast.
- `.nades` åbner en menu, hvor du kan gennemse lineups efter type.
- Admins tilføjer lineups til en fælles pakke for hele serveren med `.libadd <navn>` (`.libremove`, `.liblist`).

## Holdudførelser { #team-executes }

- `.delay <sekunder>` sætter timingen på din sidste granat.
- `.grt` kaster **alles** sidste granat igen på én gang, hver med sin forsinkelse, så en hel udførelse kører med én kommando.
- `.dryrun` spiller derefter udførelsen i rigtige runder.

## Bots { #bots }

- `.bot` placerer en bot, hvor du står, og kigger samme vej som dig. `.crouchbot`, `.boost` og `.crouchboost` til andre opstillinger.
- `.savebotpos <navn>` gemmer et bot-spot; `.loadbotpos` uden navn spawner alle gemte spots på banen og genskaber en hel opstilling med det samme.
- `.showbotpos` viser de gemte spots; `.botjiggle` får bots til at strafe, til peek-træning.
- Bot-kommandoerne respekterer `-nobots` og tager aldrig CSTV-pladsen, så en practice-session kan ikke stoppe en kørende GOTV.

## Positioner og spawns { #positions-and-spawns }

- `.savepos <navn>` / `.loadpos <navn>`: op til 32 navngivne positioner.
- `.showspawns`: markører på alle konkurrence-spawns; sigt + ++e++ teleporterer dig dertil.
- `.spawn <n>`, `.bestspawn`, `.worstspawn` til at øve timings, der afhænger af spawnet.

## Skift hold { #team-switching }

`.t`, `.ct` og `.spec` skifter side uden at tælle som et dødsfald. Skift fra spectator bruger spillets eget join-forløb, så du spawner med det samme.
