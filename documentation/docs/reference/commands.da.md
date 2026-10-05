# Kommandoer

Kommandoerne er grupperet efter, **hvor de virker**. Skriv dem i chatten med et punktum (`.ready`). CounterStrikeSharp-formerne `!ready`, `/ready` og konsolformen `css_ready` virker også. Nogle få kommandoer findes kun i `!`-formen; de er skrevet sådan nedenfor. Nogle korte aliaser (fx `.rdy`, `.knife`, `.libadd`) findes kun som punktum-kommandoer.

Argumenter i `<vinkelparenteser>` er påkrævede, argumenter i `[firkantede parenteser]` er valgfrie. Store og små bogstaver er ligegyldige i kommandonavne; argumenter som holdnavne beholder det, du skriver.

!!! tip "I tvivl om, hvad du kan bruge?"
    `.help` viser kommandoerne til den fase, serveren er i lige nu. Admins kan skrive `.mhelp` for at se alle de admin-kommandoer, de har lov til at bruge, og hvilken rettighed der mangler til resten.

## Hvor kommandoerne virker { #where-commands-work }

| Fase / mode | Hvad spillerne gør der | Gå til |
|---|---|---|
| Overalt | Hjælp, version, admin-beskeder, baneskift | [Overalt](#anywhere) |
| Warmup og ready-check | Gør sig klar før kampen | [Warmup](#warmup-and-ready-check) |
| Map-veto | Ban og pick af baner | [Veto](#map-veto) |
| Knife-runde | Vinderen vælger side | [Knife-runde](#knife-round) |
| Live-kamp | Pauser, genspil af runder, coaching, overgivelse | [Live-kamp](#live-match) |
| Practice mode | Spawns, bots, granater, lineups | [Practice mode](#practice-mode) |
| Dryrun | Live-lignende runder uden en kamp | [Dryrun](#dryrun) |
| Serverkonsol / RCON | Loade kampe, panel-integration | [Serverkonsol](#server-console) |

## Rettigheder { #permissions }

| Betegnelse | Hvem har den |
|---|---|
| **Player** | Alle. |
| **Config** | `@css/config` (SourceMod-flag `i`). Kamp-admins. |
| **Map/prac** | `@css/map` eller `@custom/prac` (flag `g`). Kan skifte mode, men ikke styre en kørende kamp. |
| **Map** | Kun `@css/map`. Bruges af `.map`. |
| **Chat** | `@css/chat` (flag `j`). |
| **Root** | `@css/root`, SteamID'er i MatchZys `admins.json` eller serverkonsollen. |

`@css/root`, SteamID'er i MatchZys `admins.json` og `matchzy_everyone_is_admin true` består alle tjek. CounterStrikeSharps `command_overrides` respekteres.

---

## Overalt { #anywhere }

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.help` | Player | Viser de kommandoer, der giver mening i den nuværende fase (practice, warmup, live...). Også `!matchhelp` og `!matchzyhelp`. |
| `.mhelp` | Enhver admin | Chat: de admin-kommandoer, du kan bruge, grupperet efter kategori. Konsol: hele listen med et flueben pr. kommando og den rettighed, du mangler. |
| `.version` | Player | Viser CS2-build og MatchZy-version. Også `!matchzy_version` og `!mikzy_version`. |
| `!color <0-4>` | Player | Vælger din holdfarve på radaren og scoreboardet (0 blå, 1 grøn, 2 gul, 3 orange, 4 lilla). |
| `.asay <besked>` | Chat | Sender en meddelelse til hele chatten med admin-præfikset. `.asay` virker altid. |
| `.map <bane>` | Map | Skifter bane, før en kamp er startet. Tager `mirage`, `de_mirage`, et workshop-id, `ws/<id>` eller `ws:<navn>`. |
| `.rmap` | Root | Genindlæser den nuværende bane. |

`!asay`-formen kan slås fra med `matchzy_asay_console_enabled`, hvis et andet admin-plugin bruger den.

!!! note "`.map`"
    `ws:<navn>` loader en bane fra serverens workshop-collection. Navnet tjekkes først, så en tastefejl ikke stopper demoen eller kicker bots. Kommandoen træder automatisk til side, hvis CS2-SimpleAdmin eller CS2MapChange er installeret, eller når `matchzy_map_console_command_enabled` er slået fra.

### Skift mode (admin) { #switching-modes-admin }

Disse starter en mode fra warmup eller fra en anden mode og afvises, når en kamp er startet. `.match`, `.scrim`, `.prac` og `.sleep` husker også moden på tværs af baneskift (`matchzy_autostart_mode`).

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.match` | Map/prac | Match-mode: warmup og ready-check, derefter knife-runde og så live-kampen. |
| `.scrim` `.playout` `.po` | Map/prac | Scrim-mode: ingen knife-runde, og alle runder spilles (ingen tidlig sejr). Holdene opkaldes efter en spiller på hver side. |
| `.hill` | Map/prac | Hill-mode: som en kamp uden knife-runde, med `hill.cfg`. |
| `.prac` `.tactics` `.training` | Map/prac | Practice mode. Se [Practice mode](#practice-mode). |
| `.exitprac` `.noprac` `.exittraining` | Map/prac | Forlader practice og går tilbage til match-warmup. |
| `.dryrun` `.dry` | Map/prac | Starter en dryrun. Se [Dryrun](#dryrun). |
| `.warmup` | Config | Går tilbage til warmup (ikke når en kamp er startet, og ikke mens `matchzy_warmup_enabled` er slået fra). |
| `.sleep` | Map/prac | Hviletilstand til en tom server. |

### Admin-menuer { #admin-menus }

Kræver det valgfrie plugin [CS2MenuManager](../getting-started/installation.md#requirements). Uden det svarer de med en besked, og intet andet ændres.

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.ma` `.matchadmin` | Config | Åbner admin-menuen (naviger med ++w++ ++s++, vælg med ++e++): sæt en kamp op, tving start, knife til/fra, genstart runde, stop kamp, pause og genoptag, skift mode. |
| `.matchsetup` | Config | Åbner en guide, der bygger en kamp (hold, seriens længde, baner, veto) og loader den. Se [Admin-menuer](../guides/menus.md). |

### Eksempler { #examples }

```text
.help                          vis kommandoerne til den nuværende fase
.asay Kampen starter om 5 minutter
.map de_mirage                 skift til Mirage
.map anubis                    "de_" tilføjes, når navnet alene ikke er en bane
.map 3070284539                load en workshop-bane efter id
.map ws:aim_botz               skift til en bane i serverens workshop-collection
!color 2                       gul holdfarve
.scrim                         skift til scrim-mode
```

---

## Warmup og ready-check { #warmup-and-ready-check }

Før en kamp går live, gør spillerne sig klar. Hvor mange der skal være klar, står i [Ready-systemet](../guides/ready-system.md).

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.ready` `.r` `.rdy` `.gaben` | Player | Markerer dig som klar. Et ping (midterste museknap) skifter også. Mens en live-kamp er pauset, stemmer `.r` i stedet for at genoptage. |
| `.unready` `.ur` `.notready` `.nr` `.urdy` | Player | Fortryder din klar-status. |
| `.readycheck` `.rc` `.rcheck` | Player | Viser, hvor mange der er klar, og hvor mange der stadig mangler. |
| `.forceready` | Player | Gør hele dit hold klar på én gang. Kun loadede match configs. |
| `.addreadytime <sekunder>` | Config | Giver holdene mere tid, før tidsgrænsen for ready-up (`matchzy_forfeit_ready_timeout`) udløber. Aldrig over den fulde grænse. |
| `.start` `.force` `.forcestart` | Config | Starter kampen nu og springer ready-check over. Ikke i practice eller dryrun. |
| `.readyrequired [n]` `.teamsize [n]` | Config | Sætter, hvor mange klare spillere der skal til (0 til 32). `0` = alle på serveren. Uden et tal vises den nuværende værdi. |
| `.knife` `.rk` `.kr` `.kniferound` | Config | Slår knife-runden til eller fra for denne kamp. Også `!roundknife`. |
| `.friendlyfire` `.ffire` `.ff` | Config | Slår friendly fire til eller fra for den kommende kamp, scrim eller hill, før den starter. Gælder alle baner i serien. I practice mode er `.ff` i stedet fast-forward. |
| `.team1 <navn>` `.ctname <navn>` | Config | Sætter navnet på holdet, der lige nu er CT. |
| `.team2 <navn>` `.tname <navn>` | Config | Sætter navnet på holdet, der lige nu er T. |
| `.whitelist` | Config | Slår whitelisten til eller fra (`whitelist.cfg`). Også `!wl`. |
| `.globalnades` | Config | Slår fælles lineups til eller fra: mens den er slået til, gemmes nye `.savenade`-lineups for alle i stedet for pr. spiller. |
| `.settings` `.configs` `.config` | Config | Viser de nuværende indstillinger for knife, match-mode og scrim. Også `!options`. |
| `.warmupbots [antal]` | Map/prac | Tilføjer bots at skyde på, mens I venter (standard 4, 1 til 10). De forsvinder selv, når knife-runden eller kampen starter. Kør igen for at fjerne dem. |
| `.coach [t\|ct]` | Player | Bliv coach. Se [Coaching](#coaching). |

`.forceready` kræver også, at `matchzy_allow_force_ready` er slået til, og at der er nok spillere på dit hold: højst `matchzy_forceready_max_missing` (standard 1) færre end `players_per_team`, og mindst `min_players_to_ready`. Den bruges, når et hold vil starte uden en registreret spiller, der ikke er dukket op: med 4 af 5 registrerede spillere inde venter `.ready` alene, mens `.forceready` starter som 4. Et hold med kun 4 registreret har ikke brug for den; `.ready` er nok.

`.addreadytime` virker kun i en loadet kamp med en tidsgrænse for ready-up, og den resterende tid kan aldrig blive højere end den fulde grænse.

### Eksempler { #examples_1 }

```text
.ready                         marker dig som klar
.rc                            "4/10 klar, venter på 6"
.addreadytime 120              to minutter mere til ready-up
.teamsize 5                    5 klare spillere kræves
.team1 Astralis                navngiv CT-holdet
.tname Team Vitality           navngiv T-holdet (mellemrum er fint)
.warmupbots 6                  seks aim-bots til warmup
```

## Map-veto { #map-veto }

Kører, når en match config med `"skip_veto": false` er loadet. MatchZy vælger én veto-kaptajn pr. hold, og chatten fortæller hvert hold, når det er deres tur. Kun den kaptajns kommandoer tæller.

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.ban <bane>` | Kaptajnen, der har tur | Fjerner en bane fra puljen. |
| `.pick <bane>` | Kaptajnen, der har tur | Vælger en bane, der skal spilles. |
| `.ct` / `.t` | Kaptajnen, der har tur | Vælger startsiden på en bane, det andet hold har pickét. |
| `.skipveto` `.sv` | Config | Springer vetoet over og spiller banerne i listens rækkefølge. Ikke når kampen er startet. |

### Eksempler { #examples_2 }

```text
.ban de_vertigo
.pick de_inferno
.ct                            start som CT på banen, det andet hold pickede
```

## Knife-runde { #knife-round }

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.stay` | Vinderne af knife-runden | Bliv på jeres nuværende side og gå live. |
| `.switch` `.swap` | Vinderne af knife-runden | Byt side og gå live. |
| `.ct` / `.t` | Vinderne af knife-runden | Vælg en side ved navn (bliver eller bytter efter behov). |

---

## Live-kamp { #live-match }

### Spillere { #players }

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.pause` `.p` | Player | Pauser kampen i næste freeze time (også i knife-runden). Begge hold skal genoptage. |
| `.unpause` `.up` | Player | Dit holds stemme for at genoptage. Spillet fortsætter, når begge hold har stemt. En pause, en admin har startet, kan kun hæves af en admin. `.r` virker også under en pause. |
| `.tech` | Player | Teknisk pause ved forbindelses- eller hardwareproblemer. Hvert hold får `matchzy_max_tech_pauses_allowed` pr. bane. |
| `.tac` | Player | Taktisk timeout fra dit holds timeout-budget. |
| `.stop` | Player | Stem for at spille den nuværende runde om fra start. En spiller fra hvert hold skal skrive den inden for 30 sekunder. |
| `!gg` | Player | Stem for at give op. Dit hold skal være mindst 6 runder bagud, og alle på holdet undtagen én skal stemme (alle på et hold med 2 eller færre). Kun kampe med én bane. |

- **`.pause`**: afvises, mens `matchzy_allow_pause` er slået fra. Er `matchzy_use_pause_command_for_tactical_pause` slået til, kalder den i stedet en taktisk timeout.
- **`.tech`**: `matchzy_max_tech_pauses_allowed 0` betyder ingen tekniske pauser, eller ubegrænset med `matchzy_tech_pause_mode 1`. Som standard slutter en teknisk pause af sig selv efter `matchzy_tech_pause_duration` sekunder. Med `matchzy_tech_pause_mode 1` slutter den aldrig af sig selv, men efter den tid kan hvert hold genoptage alene.
- **`.stop`**: kræver `matchzy_stop_command_available`. Med `matchzy_stop_command_no_damage` afvises den, når nogen har skadet en modstander.

### Coaching { #coaching }

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.coach [t\|ct]` | Player | Bliv coach for dit eget hold. Virker i warmup og freeze time, ikke i en live-runde eller i practice mode. |
| `.uncoach` `.play` | Coach | Gå tilbage til at spille. En spiller, der kun står som coach i match configen, kan ikke. |

- **`.coach`**: uden en side bruges det hold, du står på; med en side skal det være dit eget. Coachen ser med fra en plads bag holdet i freeze time og dræbes lige før runden starter, så han ser runden som spectator. Med en `coaches`-liste i match configen kan kun de SteamID'er, der står på listen, coache det hold.

Mere under [Coaching](../guides/coaching.md).

### Admins { #admins }

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.forcepause` `.fp` | Config | Admin-pause. Kun en admin kan hæve den. |
| `.forceunpause` `.fup` | Config | Hæver enhver pause med det samme. |
| `.restore <runde>` | Config | Sætter kampen tilbage til starten af den runde (score, penge, udstyr). Brug `R`-nummeret, som `.backups` viser. |
| `.restorelast` `.rl` | Config | Gendanner den forrige runde. |
| `.restorecurrent` `.rrestore` `.rr` | Config | Genstarter den nuværende runde fra start. Også `!restartround`. (I practice genstarter `.rr` i stedet practice-runden.) |
| `.backups` `.backup` `.backupmenu` | Config | Viser op til 10 backups af denne kamp, nyeste runde først, med score og `!restore`-kommandoen til hver. |
| `!loadbackup <fil>` | Config | Gendanner en backup-fil ved navn (fra `csgo/MatchZyDataBackup`). Genopbygger kampen ud fra filen, også med baneskift, hvis det er nødvendigt. |
| `.listbackups [matchid]` | Config | Viser alle backups af en kamp. Uden et match-id bruges den nuværende kamp. |
| `.restart` `.abort` | Config | Sætter kampen tilbage til warmup. Ikke i practice eller dryrun. |
| `.stopmatch` `.endmatch` `.end` `.matchstop` `.forcestop` `.endgame` `.stopgame` `.endscrim` `.exitscrim` | Config | Stopper kampen og nulstiller den. Virker i opsætning, veto, warmup, knife og live. Kampen får et sluttidspunkt og ingen vinder i databasen. |
| `.forceend` | Config | Annullerer kampen (som `get5_endmatch`). Fra konsollen afslutter `get5_endmatch team1` / `team2` serien med det hold som vinder. |
| `.matchgg` `.surrender` | Config | Afslutter en live-kamp som en overgivelse. |
| `.autopause` | Root | Slår autopause (pause, når et hold mangler spillere) til eller fra. |
| `!autopause_minplayers <1-5>` | Root | Antal spillere et hold skal have, før autopause slår til. |
| `!autopause_delay <0-30>` | Root | Sekunder der ventes, før spillet genoptages, når begge hold er fulde igen. |
| `!autopause_status` / `!autopause_check` | Root | Viser autopause-indstillingerne / kører tjekket én gang nu. |

`.restore`-nummeret er antallet af runder, der allerede var spillet, da backuppen blev taget.

`.backups` uden for en live-kamp viser de 5 nyeste backup-filer på disken med et `!loadbackup`-tip, praktisk efter et servercrash.

### Eksempler { #examples_3 }

```text
.pause                         pause i næste freeze time
.tech                          teknisk pause
.unpause                       dit holds stemme for at genoptage
.stop                          begge hold skriver den for at spille runden om
.coach                         coach det hold, du står på
.coach ct                      det samme, mens du står på CT
.restore 12                    tilbage til runden efter 12 spillede runder (R12 i .backups)
.rl                            tilbage til starten af den forrige runde
.backups                       vis backups med deres !restore-kommando
!loadbackup matchzy_27_1_round05.json
.listbackups 27                alle backups af kamp 27
!autopause_minplayers 4
```

---

## Practice mode { #practice-mode }

Alt her virker **kun i practice mode** (`.prac`). Ingen admin-rettigheder kræves, medmindre det står.

### Hold og positioner { #teams-and-positions }

| Kommando | Hvad den gør |
|---|---|
| `.ct` / `.t` / `.spec` | Skifter dit hold. Du respawner på den nye side, og det tæller ikke som et dødsfald. |
| `.fas` `.watchme` | Flytter alle andre spillere til spectator, så de kan se dig. Kun Map/prac-admins. |
| `.spawn <n>` `.sp <n>` | Teleporterer dig til dit holds konkurrence-spawn nummer N. `.ctspawn <n>` (`.cts`) / `.tspawn <n>` (`.ts`) vælger siden. |
| `.bestspawn` / `.worstspawn` | Teleporterer dig til spawnet nærmest / længst fra, hvor du står. Varianter pr. side: `.bestctspawn`, `.besttspawn`, `.worstctspawn`, `.worsttspawn`. |
| `.showspawns` / `.hidespawns` | Tegner markører på alle konkurrence-spawns. Sigt på en og tryk ++e++ for at teleportere dertil. |
| `.savepos [navn]` / `.loadpos [navn]` | Gemmer din position og synsvinkel og teleporterer dig tilbage. Uden navn bruges én standardplads; med navn får du op til 32 pladser. Navne beholder bogstaver, tal, `_` og `-`. |
| `.listpos` / `.delpos <navn>` | Viser / sletter dine navngivne positioner. |

### Bots { #bots }

| Kommando | Hvad den gør |
|---|---|
| `.bot` | Spawner en bot på det andet hold, hvor du står, og kigger samme vej som dig. `.tbot` / `.ctbot` vælger holdet. |
| `.crouchbot` `.cbot` | Det samme, men på hug. `.tcrouchbot` / `.ctcrouchbot` vælger holdet. Også `!duckbot`. |
| `.boost` / `.crouchboost` `.cboost` | Spawner en bot og sætter dig på dens hoved, til boost-spots. Også `!duckboost`. |
| `.nobot` `.nb` `.removebot` `.kickbot` `.unbot` | Fjerner den bot, der er nærmest dig. |
| `.nobots` `.nbots` `.nbs` `.nbts` `.kbots` `.kickbots` `.clearbots` `.removebots` | Fjerner alle practice-bots. |
| `.savebotpos <navn>` `.sbp <navn>` | Gemmer, hvor du står (og om du er på hug), som et navngivet bot-spot på denne bane. |
| `.loadbotpos [navn]` `.lbp [navn]` | Spawner en bot på et gemt spot (det navn, der passer bedst). Uden navn spawnes alle gemte spots på banen, så en hel opstilling kommer tilbage med én kommando. |
| `.listbotpos` `.listbp` | Viser de gemte bot-spots på denne bane. |
| `.delbotpos <navn>` `.dbp <navn>` | Sletter et gemt bot-spot. |
| `.showbotpos` `.showbp` | Viser markører for gemte bot-spots. |
| `.botjiggle` | Bots strafer til venstre og højre, til peek- og aim-træning. Kør igen for at stoppe. Bredde: `matchzy_botjiggle_range`. |

### Granathistorik og rethrow { #grenade-history-and-rethrow }

Hver granat, du kaster, gemmes (position, synsvinkel, hvordan den blev kastet). Historikken er nummereret fra 1 (ældste).

| Kommando | Hvad den gør |
|---|---|
| `.last` | Teleporterer dig til, hvor du kastede din sidste granat, med den granat i hånden. |
| `.back [n]` | Går tilbage gennem din historik: første `.back` går til din nyeste granat, hver ny `.back` én ældre. `.back 3` hopper til nummer 3. |
| `.rethrow` `.rt` `.throw` | Kaster din sidste granat igen fra samme sted, uden at du flytter dig. |
| `.grt` `.globalrethrow` | Kaster den sidste granat fra **alle** spillere igen på én gang, så I kan se en hel holdudførelse. Kun Map/prac-admins. |
| `.throwsmoke` / `.throwflash` / `.thrownade` / `.throwmolotov` / `.throwdecoy` | Kaster din sidste granat af den type igen. |
| `.throwindex <n...>` `.throwidx` | Kaster en eller flere numre fra historikken. |
| `.lastindex` | Viser nummeret på din sidste granat i historikken. |
| `.delay <sekunder>` | Lægger en forsinkelse på din sidste granat, når den kastes igen (til timing af udførelser med `.grt`). `.delay 0` fjerner den. |
| `.wipe` `.clearnades` | Rydder din kastehistorik. |
| `.timer` | Stopur: skriv én gang for at starte, igen for at stoppe. |

Kommandoerne til rethrow pr. type virker også som `.rethrowsmoke`, `.rethrowflash`, `.rethrownade` (`.throwgrenade`, `.rethrowgrenade`), `.rethrowmolotov` og `.rethrowdecoy`.

### Gemte lineups og granatbiblioteket { #saved-lineups-and-the-grenade-library }

| Kommando | Hvad den gør |
|---|---|
| `.savenade <navn> [kast] [kommentar]` `.sn` | Gemmer dit nuværende lineup med en valgfri kastemåde og kommentar. Op til 500 pr. spiller. |
| `.loadnade <navn>` `.ln` | Teleporterer dig til et gemt lineup med granaten i hånden. Det navn, der passer bedst, bruges. `.ln #3` loader efter nummeret fra `.listnades`. |
| `.listnades [filter]` `.lin` | Nummereret liste over dine lineups og de fælles på denne bane. Med et filter kun navne, der indeholder det. |
| `.delnade <navn> [navn2...]` `.dn` `.deletenade` | Sletter lineups på denne bane. Flere på én gang, eller `all` for alle dine lineups på banen. |
| `.importnade <navn> <x> <y> <z> <pitch> <yaw> <roll>` | Importerer et lineup ud fra en position og synsvinkel (fx kopieret fra `getpos`). Også `!in`. |
| `.mynades` | Hvor mange lineups du har gemt. |
| `.nades` | Menu til at gennemse lineups efter granattype og loade et (kræver CS2MenuManager). |
| `.shownades` / `.hidenades` | Farvede markører med navne ved alle lineups på banen. Sigt på en og tryk ++e++ for at loade den. |
| `.liblist` | Viser det fælles lineup-bibliotek for denne bane. |
| `.libadd <navn>` / `.libremove <navn>` | Config-admins: tilføj et af dine lineups på denne bane til det fælles bibliotek, eller fjern et. |

- **`.savenade`**: kastemåden (`normal`, `jump`/`jt`, `run`, `walk`, `crouch`/`duck`) vises på markøren; alt efter den er en kommentar.
- **`.shownades`**: tryk ++f++ (eller `.nadetoggle`) for at skifte mellem lineups på samme sted. Bind det med `bind g "css_shownades"`.

### Utility og visning { #utility-and-visuals }

| Kommando | Hvad den gør |
|---|---|
| `.cleanup` `.clear` | Fjerner al utility på banen: smokes, ild og granater i luften. |
| `.autoclear` | Hver ny detonation fjerner den ældre utility, så du kun ser det seneste resultat. |
| `.landmarker` `.lm` | Markerer stedet, hvor hver granat detonerede. |
| `.arc` `.traceline` | Tegner flyvebanen for kastede granater i verden. |
| `.traj` `.pip` `.cam` `.nadecam` `.previewnade` `.nadepreview` | Billede-i-billede-kamera, der følger din granat. |
| `.impacts` | Slår markører for kugleskud fra / til for dig. Slået til som standard. |
| `.noflash` `.noblind` | Flashbangs blænder dig ikke. |
| `.flashtest` `.ft` | Skriver, hvor længe du var blændet, hver gang du bliver flashet. |
| `.blind` | Kaster en flashbang i hovedet på dig, til træning af reaktion på pop-flashes. |
| `.god` | Du tager ingen skade. Skriv igen for at slå det fra. |
| `.solid` | Skifter, om holdkammerater blokerer hinanden. |
| `.ff` `.fastforward` | Spoler tiden frem i 10 sekunder (vent smokes og ild af). Spillerne er frosset imens. Uden for practice slår `.ff` i stedet friendly fire til/fra (se `.friendlyfire`). |
| `.break` | Smadrer alt glas, døre og objekter, der kan gå i stykker. |
| `.breakrestore` `.nobreak` | Genskaber alt, `.break` har ødelagt. |
| `.rs` `.rr` | Genstarter practice-runden for alle. Kun Map/prac-admins. |

### Eksempler { #examples_4 }

```text
.spawn 3                       dit holds spawn 3
.ctspawn 1                     CT-spawn 1
.savepos ramp                  gem dette sted som "ramp"
.loadpos ramp                  tilbage til "ramp"
.ctbot                         CT-bot, hvor du står
.savebotpos apps_close         gem et bot-spot
.lbp                           spawn alle gemte bot-spots på banen
.back                          til din nyeste granat; igen for den før
.back 3                        til nummer 3 i historikken
.throwindex 2 4 5              kast nummer 2, 4 og 5 samtidig
.delay 1.5                     din sidste granat venter 1,5 s ved rethrow
.savenade ctsmoke jump fra T-spawn, sigt på antennen
.ln ctsmoke                    load den med smoken i hånden
.ln #3                         load lineup 3 fra .listnades
.listnades mid                 kun lineups med "mid" i navnet
.delnade ctsmoke window        slet to lineups
.importnade xbox -301.5 -1340.2 -160.0 -31.6 98.9 0.0
.libadd ctsmoke                del det i biblioteket (Config-admins)
```

## Dryrun { #dryrun }

En dryrun spiller rigtige runder (købetid, rundeur, penge) uden en kamp, til at øve udførelser med dit hold eller bots. Den kører, indtil en admin stopper den.

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.dryrun` `.dry` | Map/prac | Starter en dryrun, eller genstarter den med et rent scoreboard. Ikke når en kamp er startet. |
| `.exitdry` `.exitdryrun` `.stopdry` `.enddry` | Map/prac | Stopper dryrunnen og går tilbage til match-warmup. |

## Værktøjer til coach-pladser { #coach-spot-tools }

Til admins, der finjusterer, hvor coaches står på en bane.

| Kommando | Hvem | Hvad den gør |
|---|---|---|
| `.showcoachspawns` | Config | Viser coach-pladsen for begge sider (blå CT, orange T). |
| `.savecoachspawn [t\|ct]` | Config | Gemmer din position og synsvinkel som banens coach-plads. Uden side bruges det hold, du står på. |
| `.listcoachspawns` / `.clearcoachspawns` | Config | Viser / fjerner de gemte coach-pladser på denne bane. |
| `.coachtest` | Map/prac | Placerer dig som en coach på din side lige nu. Kør igen for at slippe. |

### Eksempler { #examples_5 }

```text
.savecoachspawn ct             gem CT-coach-pladsen, hvor du står
.showcoachspawns               tjek begge pladser
.coachtest                     prøv pladsen, som en coach ser den
```

---

## Serverkonsol { #server-console }

Køres fra serverkonsollen, RCON eller et panel. Spillere kan ikke bruge dem.

| Kommando | Hvad den gør |
|---|---|
| `matchzy_loadmatch <fil>` | Loader en [match config](match-config.md)-fil (sti i forhold til `csgo/`). Afvises, mens en anden kamp er loadet. |
| `matchzy_loadmatch_url <url> [header] [værdi]` | Loader en match config fra en URL, evt. med én HTTP-header (sendes kun, når både headernavn og værdi er angivet). Alias `get5_loadmatch_url`. |
| `matchzy_addplayer <steamid64> <team1\|team2\|spec> "<navn>"` | Tilføjer en spiller til den loadede kamp. Ikke i halvlegen. Alias `get5_addplayer`. |
| `matchzy_removeplayer <steamid64>` | Fjerner en spiller fra den loadede kamp. Ikke i halvlegen. Alias `get5_removeplayer`. |
| `matchzy_add_ready_time <sekunder>` | Det samme som `.addreadytime`. Alias `get5_add_ready_time`. |
| `matchzy_loadbackup <fil>` | Gendanner en backup af en runde. Aliaser `get5_loadbackup`, `css_loadbackup`. |
| `matchzy_listbackups [matchid]` | Viser backups. Aliaser `get5_listbackups`, `css_listbackups`. |
| `get5_endmatch [team1\|team2]` | Annullerer kampen, eller afslutter serien med det angivne hold som vinder (som i Get5). Det samme som `css_forceend`. |
| `get5_status` / `get5_web_available` | Statussvar til Get5-paneler. |
| `matchzy_version` | Versionsinfo. Aliaser `mikzy_version`, `css_matchzy_version`, `css_mikzy_version`, `css_version`. |

De fleste admin-kommandoer fra chatten virker også fra konsollen i deres `css_`-form (fx `css_forcepause`, `css_restore 12`, `css_start`), og konsollen består alle rettighedstjek.

### Eksempler { #examples_6 }

```text
matchzy_loadmatch cfg/MatchZy/match.json
matchzy_loadmatch_url "https://panel.example/match.json"
matchzy_loadmatch_url "https://panel.example/match.json" "Authorization" "Bearer <token>"
matchzy_addplayer 76561198000000001 team1 "PlayerOne"
matchzy_removeplayer 76561198000000001
matchzy_add_ready_time 300
matchzy_loadbackup matchzy_27_1_round05.json
get5_endmatch team2            afslut serien med team2 som vinder
css_asay Serveren genstarter efter denne bane
```

Indstillinger, der kan sættes fra konsollen, står under [Konfiguration](convars.md).
