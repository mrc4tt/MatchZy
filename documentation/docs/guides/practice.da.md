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

`.shownades` sætter en lysende grøn figur på hvert gemt lineup-sted på banen, synlig gennem vægge, så du kan finde lineups på tværs af banen. `.shownades smoke` (eller `flash`, `he`, `molly`, `mine` eller en del af et navn) viser kun dem.

- Sigt på en markør, og tryk ++e++ for at teleportere dertil med den rigtige granat.
- Flere lineups på samme sted deler en markør; tryk ++f++ for at skifte mellem dem.
- Markøren, du står på, skjules for dig, så den aldrig er i vejen for dit kast.
- Hvert sted har en lysende grøn figur. Tæt på et sted viser et rødt mål, hvor hver af dets lineups sigter, det valgte lyser grønt (tryk ++f++ for at skifte). Stil dig på stedet (ingen tast), så siger en hint-boks nederst på skærmen, hvordan du kaster, fx "Venstreklik + hop". Kun du ser dine mål og dit hint. Gem kastetypen med lineuppen, fx `.savenade window jumpthrow`. En lineup indlæst med `.nades` eller `.loadnade` får de samme mål og hint.
- `.nades` åbner en menu, hvor du kan gennemse lineups efter type.
- Admins tilføjer lineups til en fælles pakke for hele serveren med `.libadd <navn>` (`.libremove`, `.liblist`).

## Holdudførelser { #team-executes }

- `.delay <sekunder>` sætter timingen på din sidste granat.
- `.grt` kaster **alles** sidste granat igen på én gang, hver med sin forsinkelse, så en hel udførelse kører med én kommando.
- `.dryrun` spiller derefter udførelsen i rigtige runder.

## Bots { #bots }

- `.bot` placerer en bot, hvor du står, og kigger samme vej som dig. `.crouchbot`, `.boost` og `.crouchboost` til andre opstillinger.
- `.savebotpos <navn>` gemmer et bot-spot; `.loadbotpos` uden navn spawner alle gemte spots på banen og genskaber en hel opstilling med det samme.
- `.showbotpos` viser de gemte spots; `.botjiggle` får bots til at strafe, til peek-træning (`.botjigglerange`, `.botjigglerandom`).
- `.botspawn <navn>` bygger en pulje af pladser; `.placebot <antal> <navn>` sætter bots på tilfældige pladser fra den, så hver gentagelse får en ny opstilling. `.showbotspawn` viser pladserne (stil dig i en + ++e++ sletter den).
- `.botshoot` får bots til at skyde igen fra deres plads efter `.botreactiontime` (standard 500 ms). Slå din livsregenerering fra med `.liferegon` (eller `.allliferegon false` for alle), så deres skud tæller. `.botrespawn` og `.botlifereg` styrer, hvad der sker, når du rammer dem.
- Sigt på en bot: `.kevlar` / `.armor` tager dens armor af eller giver den igen, `.botgun awp` skifter dens våben, `.dismiss` fjerner den (`.dismissall` fjerner alle bots), `.runboost` lader dig ride på den, mens den følger dine bevægelsestaster.
- `.startround` nulstiller gentagelsen: utility ryddet, tilfældige spawns til alle spillere, bots tilbage på deres plads, 5 sekunders frys.
- Bot-kommandoerne respekterer `-nobots` og tager aldrig CSTV-pladsen, så en practice-session kan ikke stoppe en kørende GOTV.

## Optag og afspil { #record-and-replay }

1. `.rec` (eller `.record`): du fryses under en nedtælling på 3 sekunder, og derefter optages dine bevægelser og granatkast (op til 60 sekunder).
2. Stop med `.rec` igen, `.stoprecord`, et dobbelt højreklik eller ++shift+e++.
3. `.play` (eller `.replay t` / `.replay ct loop`): en bot går din rute og kaster de samme granater. `.replay stop` stopper den.
4. `.showrec` tegner ruten på banen, kun for dig. `.saverec <navn>` gemmer den til banen, `.loadrec <navn>` loader den igen (`.listrec`, `.delrec`).

## Strategier { #strategies }

En strategi er et helt holds execute, afspillet af én bot pr. rolle.

**Med dit hold:**

1. Alle stiller sig på deres startposition.
2. `.stratrec`: efter en nedtælling på 3 sekunder optages alle levende spillere på dit hold fra samme øjeblik (op til 120 sekunder). `.stratrec` igen eller `.stoprecord` stopper den.
3. `.strat` afspiller den: én bot pr. rolle, alle starter samtidig. `.strat <navn> me 2` lader dig selv spille rolle 2 med de andres timing.
4. `.savestrat <navn>` gemmer den til banen (`.liststrat`, `.delstrat`). Gemte strategier ligger i `strats/<map>.json`.

**Alene, én rolle ad gangen:**

1. `.createstrat <navn>` opretter en tom strategi.
2. `.stratslot <navn> 1` optager rolle 1. Derefter `.stratslot <navn> 2`: botten for rolle 1 spiller samtidig, så du kan time rolle 2 mod den. Gentag op til 5. Optager du en rolle igen, erstattes den.
3. `.strat <navn>` afspiller hele executen.

`.strats` åbner en menu til det hele: vælg en strategi, start eller stop den, optag, afspil eller ryd en rolle, slet den. `.strat stop` stopper en afspilning og fjerner dens bots.

## Utility-hjælpere { #utility-helpers }

- `.expire`: smokes og molotovs løber ikke ud, før du skriver det igen.
- `.coloredsmokes`: smokes i kasterens holdfarve, til eller fra.
- `.xray`: alle spillere og bots lyser gennem vægge (T orange, CT blå), for alle på serveren.
- `.noflash`, `.god`, `.solid`, `.break` / `.repair`: se [Utility og visning](../reference/commands.md#utility-and-visuals).

## Positioner og spawns { #positions-and-spawns }

- `.savepos <navn>` / `.loadpos <navn>`: op til 32 navngivne positioner. `.slp` / `.tlp` / `.dlp` bruger pladsen uden navn, og efter en død respawner du der.
- `.showspawns`: en nummereret boks på gulvet ved hver konkurrence-spawn (blå CT, orange T); sigt på en boks (den bliver grøn) eller stil dig i en og tryk ++e++ for at teleportere dertil. Nummeret er det, `.spawn <n>` bruger.
- `.spawn <n>`, `.bestspawn`, `.worstspawn` til at øve timings, der afhænger af spawnet.

## Skift hold { #team-switching }

`.t`, `.ct` og `.spec` skifter side uden at tælle som et dødsfald. Skift fra spectator bruger spillets eget join-forløb, så du spawner med det samme.
