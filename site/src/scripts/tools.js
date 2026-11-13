// MatchZy docs tools: the match config builder (reference/match-builder), the settings generator
// (reference/config-generator) and the payload browser (reference/event-explorer). Ported from the
// old site's assets/mz-tools.js; loaded by src/components/Tool.astro.
//
// Neutral names (Tool.astro, mzt-*, #mz-cfg / #mz-pl / #mz-mcb): ad and tracker blockers (uBlock
// Origin, AdGuard, EasyPrivacy) block scripts and hide elements whose names look like analytics,
// and "event-explorer.js" / #mz-events were hit on the old site, which left the search field dead.
//
// Every tool builds its own UI from the I18N table below, so the English and Danish pages cannot
// drift apart; the pages only hold an empty root element and a fallback note for when this script
// is blocked. The settings and events come as JSON in the page, read from the plugin source at build.
/* eslint-disable no-var */

// ------------------------------------------------------------------ text

var I18N = {
  en: {
    copy: "Copy", copied: "Copied!", download: "Download", reset: "Reset", resetAll: "Reset all",
    importBtn: "Import", importLoad: "Load", importCancel: "Cancel", chooseFile: "Choose a file", toResult: "Result", nError1: "1 error", nErrors: "{0} errors", nWarning1: "1 warning", nWarnings: "{0} warnings", ready: "Ready", copyAnyway: "Copy ({0})", copyCmd: "Copy command",
    // builder
    bMatch: "Match", bMatchId: "Match id", bMatchIdHint: "0 = a new id from the database",
    bGameMode: "Game mode", bSeries: "Series", bPpt: "Players per team", bPptAuto: "from rosters",
    bMinReady: "Ready players needed per team", bMinReadyAuto: "everyone", bMinSpec: "Ready spectators needed",
    bFf: "Friendly fire", bFfMode: "Default (from the mode's cfg)", bFfOn: "On", bFfOff: "Off", bFfGren: "Grenades only",
    bClinch: "End the series once a team has won the majority of maps",
    bMaps: "Maps", bMapsHint: "Click maps in the order you want them; the number shows the order.",
    bMoreMaps: "More maps (one per line: workshop id, workshop/<id>, ws:<name>)",
    bVeto: "Veto and sides", bSkipVeto: "Skip the veto: play the maps in the order above",
    bVetoFirst: "Starts the veto", bVetoT1: "Team 1 (default)", bVetoT2: "Team 2", bRandom: "Random",
    bSides: "Sides", bSideStd: "Standard (side pick, knife on the decider)", bSideAlways: "Always knife", bSideNever: "Never knife (team1 starts CT)",
    bMapSides: "Side per map", bMapN: "Map {0}", bKnife: "Knife", bSideAuto: "From side type",
    bTeam: "Team {0}", bName: "Name", bTag: "Tag", bYourId: "Team id (optional)", bPlayers: "Players",
    bRoster: "Roster", bAny: "Anyone (open team)", bBots: "Bots",
    bRosterLabel: "One per line: SteamID64 and name (STEAM_0:1:23, [U:1:47] and profile links work too)", bBotDiff: "Bot difficulty", bCoaches: "Coaches (optional)",
    bExtras: "Spectators and cvars", bSpecs: "Spectators (optional, one per line: SteamID64 and name)",
    bCvars: "Cvars (optional, one per line: name value)",
    bResult: "Match config", bLoadWith: "Load it from the server console or RCON:", bTestWith: "Check it first, without loading it:",
    bStartFrom: "Start from", bStartHint: "Sets the match, maps and veto. Your team names and players stay.",
    tplBo1: "BO1", tplBo3: "BO3 tournament", tplBo5: "BO5 final", tplWingman: "Wingman 2v2", tpl1v1: "1v1",
    tplBots: "Against bots", tplOpen: "Open scrim", tplOpenHint: "Team 2 is open to anyone: your roster against whoever joins.",
    bAdvanced: "More options",
    vTitle: "How the veto goes", vRandomStart: "Who starts is drawn when the match loads.", vFirst: "The team that starts", vSecond: "The other team",
    vBan: "{0} bans a map", vPick: "{0} picks map {1}", vPickSide: "{0} picks map {1}; {2} picks the side",
    vDecider: "The map that is left is map {0} (decider)", vDeciderKnife: "The map that is left is map {0} (decider); knife round for the sides",
    vUnplayed: "The maps that are left are not played.",
    vSidesKnife: "Sides: knife round on every map.", vSidesNever: "Sides: {0} starts CT on every map (no knife).", vSidesRandom: "Sides: random on every map.",
    vCustom: "Custom order (veto_mode) from the imported config.", vUseDefault: "Use the default order",
    eVetoMode: "veto_mode: {0}", vePicks: "a series of {0} maps needs at least {1} picks, found {2}.", vePool: "a pool of {0} maps needs {1} bans and picks (found {2}), unless the picks match the series length.", bShare: "Share link", bShareBad: "The shared link could not be read.",
    eVanity: "{0}: a custom Steam link (/id/...) cannot be looked up here. Use the number from the /profiles/ link (SteamID64), or STEAM_0:X:Y.",
    bSummary: "{0} · map pool: {1} · {2} · team size {3}{4}",
    bSummaryVeto: "veto", bSummaryNoVeto: "no veto", bFromRosters: " (from the rosters)",
    bImportHint: "Paste a match config (JSON) to edit it here.",
    bImportBad: "Not a match config: ", bImported: "Imported. Fields the builder does not know were kept out: ",
    // builder problems
    eSteamId: "Not a SteamID64: {0}", eNoPlayers: "{0}: no players listed", eTooFewMaps: "Pick at least {0} map(s) for this series.",
    eTwoAny: "Only one team can be open to anyone.", eTwoBots: "Both teams cannot be bots.",
    eBotsVeto: "A match against bots cannot veto: pick exactly as many maps as the series, or skip the veto.",
    eCvar: "Not a valid cvar line (name value, no quotes or ;): {0}", eDupMap: "{0} is in the map list twice.",
    eNoName: "{0} has no name.",
    wBoth: "SteamID {0} is listed for both teams. It can only play for one, and the other team waits for it.",
    wSpec: "SteamID {0} plays for {1} and is also a spectator: while it sits on Spectator the team is one player short.",
    wRosterBig: "{0} lists {1} players with a team size of {2}: {2} must be on the server, the rest are substitutes.",
    wUneven: "The teams need a different number of players: {0} {1}, {2} {3}.",
    wMinReady: "min_players_to_ready {0}: that many ready players make a team ready, also while registered players are missing. Ignored on servers with matchzy_ready_per_team.",
    wMinReadyIgnored: "min_players_to_ready {0} is ignored: matchzy_ready_per_team is {1} in cvars, so {1} ready player(s) per team start the match once the team is complete.",
    eVetoShort: "The veto gives {0} map(s), but the series needs {1}: add maps or choose a shorter series.",
    wPptBig: "players_per_team {0} is more than the {1} default ({2}).",
    // generator
    gSearch: "Search settings (name or text)...", gOnlyChanged: "Only changed", gSettings: "{0} settings", gShownOf: "{0} of {1} settings", gSection: "Section",
    gYour: "Your settings", gFull: "Full config.cfg", gChanged: "Only changed", gChangedCount: "{0} changed",
    gNoneChanged: "Nothing changed yet: every setting is at its default.",
    gHeaderFull: "MatchZy config.cfg (defaults, with your changes)", gHeaderChanged: "MatchZy settings changed from the defaults",
    gDefault: "Default", gUnset: "not set", gResetOne: "Back to the default", gNoHits: "No setting matches the search.",
    gImportHint: "Paste your config.cfg (or any lines of it) to load your current values.",
    gImported1: "1 setting loaded.", gImporteds: "{0} settings loaded.", gUnknown: " Not in this version: {0}.",
    gNewSince1: " 1 setting is new since your file.", gNewSinces: " {0} settings are new since your file.", gShowNew: "Show", gOnlyNew1: "Only the setting your file does not have", gOnlyNews: "Only the {0} settings your file does not have", gShowAll: "Show all",
    qTitle: "Quick setup", qAsk: "What is this server for? Sets the settings that matter for it; everything else stays at the default.",
    qPugs: "Pugs and community games", qPanel: "Tournament with a panel", qScrims: "Scrims", qPractice: "Practice server",
    qDone1: "1 setting set for \u201c{1}\u201d. It is marked below; Reset all undoes it.", qDones: "{0} settings set for \u201c{1}\u201d. They are marked below; Reset all undoes it.", qSame: "Already set for \u201c{0}\u201d.",
    gLink: "Link to this setting", gChangedMark: "changed", gOn: "on", gOff: "off",
    // payload browser
    pSearch: "Search events or fields (e.g. steamid, pause, bomb)...", pShown: "{0} events", pAll: "All",
    pBoth: "Match flow", pServer: "Server and tickets", pGet5: "Live (Get5 format)", pLegacy: "Live (legacy format)",
    pBadgeboth: "match", pBadgeserver: "server", pBadgeget5: "Get5", pBadgelegacy: "legacy",
    pFields: "{0} fields", pField: "Field", pType: "Type", pNote: "Note", pNullable: "may be null", pExample: "Example",
    pNoHits: "No event matches the search."
  },
  da: {
    copy: "Kopiér", copied: "Kopieret!", download: "Download", reset: "Nulstil", resetAll: "Nulstil alt",
    importBtn: "Importér", importLoad: "Indlæs", importCancel: "Annullér", chooseFile: "Vælg en fil", toResult: "Resultat", nError1: "1 fejl", nErrors: "{0} fejl", nWarning1: "1 advarsel", nWarnings: "{0} advarsler", ready: "Klar", copyAnyway: "Kopiér ({0})", copyCmd: "Kopiér kommando",
    bMatch: "Kamp", bMatchId: "Kamp-id", bMatchIdHint: "0 = et nyt id fra databasen",
    bGameMode: "Game mode", bSeries: "Serie", bPpt: "Spillere pr. hold", bPptAuto: "fra rosters",
    bMinReady: "Klar-spillere krævet pr. hold", bMinReadyAuto: "alle", bMinSpec: "Klar-spectators krævet",
    bFf: "Friendly fire", bFfMode: "Standard (fra modens cfg)", bFfOn: "Til", bFfOff: "Fra", bFfGren: "Kun granater",
    bClinch: "Afslut serien, når et hold har vundet flertallet af banerne",
    bMaps: "Baner", bMapsHint: "Klik banerne i den rækkefølge, du vil have dem; tallet viser rækkefølgen.",
    bMoreMaps: "Flere baner (én pr. linje: workshop-id, workshop/<id>, ws:<navn>)",
    bVeto: "Veto og sider", bSkipVeto: "Spring vetoen over: spil banerne i rækkefølgen ovenfor",
    bVetoFirst: "Starter vetoen", bVetoT1: "Hold 1 (standard)", bVetoT2: "Hold 2", bRandom: "Tilfældig",
    bSides: "Sider", bSideStd: "Standard (sidevalg, kniv på decideren)", bSideAlways: "Altid kniv", bSideNever: "Aldrig kniv (team1 starter CT)",
    bMapSides: "Side pr. bane", bMapN: "Bane {0}", bKnife: "Kniv", bSideAuto: "Fra sidetype",
    bTeam: "Hold {0}", bName: "Navn", bTag: "Tag", bYourId: "Hold-id (valgfrit)", bPlayers: "Spillere",
    bRoster: "Roster", bAny: "Alle (åbent hold)", bBots: "Bots",
    bRosterLabel: "Én pr. linje: SteamID64 og navn (STEAM_0:1:23, [U:1:47] og profil-links virker også)", bBotDiff: "Bot-sværhedsgrad", bCoaches: "Coaches (valgfrit)",
    bExtras: "Spectators og cvars", bSpecs: "Spectators (valgfrit, én pr. linje: SteamID64 og navn)",
    bCvars: "Cvars (valgfrit, én pr. linje: navn værdi)",
    bResult: "Match config", bLoadWith: "Load den fra serverkonsollen eller RCON:", bTestWith: "Tjek den først uden at loade den:",
    bStartFrom: "Start fra", bStartHint: "Sætter kamp, baner og veto. Jeres holdnavne og spillere bliver.",
    tplBo1: "BO1", tplBo3: "BO3-turnering", tplBo5: "BO5-finale", tplWingman: "Wingman 2v2", tpl1v1: "1v1",
    tplBots: "Mod bots", tplOpen: "Åben scrim", tplOpenHint: "Hold 2 er åbent for alle: jeres roster mod dem, der joiner.",
    bAdvanced: "Flere valg",
    vTitle: "Sådan går vetoen", vRandomStart: "Hvem der starter, trækkes når kampen loades.", vFirst: "Holdet der starter", vSecond: "Det andet hold",
    vBan: "{0} banner en bane", vPick: "{0} vælger bane {1}", vPickSide: "{0} vælger bane {1}; {2} vælger side",
    vDecider: "Banen der er tilbage, er bane {0} (decider)", vDeciderKnife: "Banen der er tilbage, er bane {0} (decider); knivrunde om siderne",
    vUnplayed: "De baner, der er tilbage, spilles ikke.",
    vSidesKnife: "Sider: knivrunde på hver bane.", vSidesNever: "Sider: {0} starter CT på hver bane (ingen kniv).", vSidesRandom: "Sider: tilfældige på hver bane.",
    vCustom: "Egen rækkefølge (veto_mode) fra den importerede config.", vUseDefault: "Brug standardrækkefølgen",
    eVetoMode: "veto_mode: {0}", vePicks: "en serie på {0} baner kræver mindst {1} picks, fandt {2}.", vePool: "en pulje på {0} baner kræver {1} bans og picks (fandt {2}), medmindre picks passer til seriens længde.", bShare: "Del link", bShareBad: "Det delte link kunne ikke læses.",
    eVanity: "{0}: et brugerdefineret Steam-link (/id/...) kan ikke slås op her. Brug tallet fra /profiles/-linket (SteamID64) eller STEAM_0:X:Y.",
    bSummary: "{0} · banepulje: {1} · {2} · holdstørrelse {3}{4}",
    bSummaryVeto: "veto", bSummaryNoVeto: "ingen veto", bFromRosters: " (fra rosters)",
    bImportHint: "Indsæt en match config (JSON) for at redigere den her.",
    bImportBad: "Ikke en match config: ", bImported: "Importeret. Felter, builderen ikke kender, blev udeladt: ",
    eSteamId: "Ikke et SteamID64: {0}", eNoPlayers: "{0}: ingen spillere angivet", eTooFewMaps: "Vælg mindst {0} bane(r) til denne serie.",
    eTwoAny: "Kun ét hold kan være åbent for alle.", eTwoBots: "Begge hold kan ikke være bots.",
    eBotsVeto: "En kamp mod bots kan ikke have veto: vælg præcis lige så mange baner som serien, eller spring vetoen over.",
    eCvar: "Ikke en gyldig cvar-linje (navn værdi, ingen anførselstegn eller ;): {0}", eDupMap: "{0} står to gange i banelisten.",
    eNoName: "{0} har intet navn.",
    wBoth: "SteamID {0} står på begge hold. Den kan kun spille for ét, og det andet hold venter på den.",
    wSpec: "SteamID {0} spiller for {1} og er også spectator: mens den står på Spectator, mangler holdet en spiller.",
    wRosterBig: "{0} har {1} spillere med holdstørrelse {2}: {2} skal være på serveren, resten er reserver.",
    wUneven: "Holdene skal have forskelligt antal spillere: {0} {1}, {2} {3}.",
    wMinReady: "min_players_to_ready {0}: så mange klar-spillere gør et hold klar, også mens registrerede spillere mangler. Ignoreres på servere med matchzy_ready_per_team.",
    wMinReadyIgnored: "min_players_to_ready {0} ignoreres: matchzy_ready_per_team er {1} i cvars, så {1} klar-spiller(e) pr. hold starter kampen, når holdet er fuldt.",
    eVetoShort: "Vetoen giver {0} bane(r), men serien kræver {1}: tilføj baner eller vælg en kortere serie.",
    wPptBig: "players_per_team {0} er mere end standarden for {1} ({2}).",
    gSearch: "Søg i indstillinger (navn eller tekst)...", gOnlyChanged: "Kun ændrede", gSettings: "{0} indstillinger", gShownOf: "{0} af {1} indstillinger", gSection: "Sektion",
    gYour: "Dine indstillinger", gFull: "Hele config.cfg", gChanged: "Kun ændrede", gChangedCount: "{0} ændret",
    gNoneChanged: "Intet ændret endnu: alle indstillinger står på standard.",
    gHeaderFull: "MatchZy config.cfg (standarder, med dine ændringer)", gHeaderChanged: "MatchZy-indstillinger ændret fra standard",
    gDefault: "Standard", gUnset: "ikke sat", gResetOne: "Tilbage til standard", gNoHits: "Ingen indstilling passer til søgningen.",
    gImportHint: "Indsæt din config.cfg (eller nogle linjer fra den) for at indlæse dine nuværende værdier.",
    gImported1: "1 indstilling indlæst.", gImporteds: "{0} indstillinger indlæst.", gUnknown: " Findes ikke i denne version: {0}.",
    gNewSince1: " 1 indstilling er ny siden din fil.", gNewSinces: " {0} indstillinger er nye siden din fil.", gShowNew: "Vis", gOnlyNew1: "Kun den indstilling, din fil ikke har", gOnlyNews: "Kun de {0} indstillinger, din fil ikke har", gShowAll: "Vis alle",
    qTitle: "Hurtig opsætning", qAsk: "Hvad bruges serveren til? Sætter de indstillinger, der betyder noget for det; resten bliver på standard.",
    qPugs: "Pugs og community-spil", qPanel: "Turnering med panel", qScrims: "Scrims", qPractice: "Practice-server",
    qDone1: "1 indstilling sat til \u201c{1}\u201d. Den er markeret nedenfor; Nulstil alt fortryder det.", qDones: "{0} indstillinger sat til \u201c{1}\u201d. De er markeret nedenfor; Nulstil alt fortryder det.", qSame: "Allerede sat til \u201c{0}\u201d.",
    gLink: "Link til denne indstilling", gChangedMark: "ændret", gOn: "til", gOff: "fra",
    pSearch: "Søg i events eller felter (fx steamid, pause, bomb)...", pShown: "{0} events", pAll: "Alle",
    pBoth: "Kampforløb", pServer: "Server og tickets", pGet5: "Live (Get5-format)", pLegacy: "Live (legacy-format)",
    pBadgeboth: "kamp", pBadgeserver: "server", pBadgeget5: "Get5", pBadgelegacy: "legacy",
    pFields: "{0} felter", pField: "Felt", pType: "Type", pNote: "Note", pNullable: "kan være null", pExample: "Eksempel",
    pNoHits: "Intet event passer til søgningen."
  }
};

var T = I18N.en;
function t(key) {
  var s = T[key] != null ? T[key] : I18N.en[key] != null ? I18N.en[key] : key;
  for (var i = 1; i < arguments.length; i++) s = s.split("{" + (i - 1) + "}").join(String(arguments[i]));
  return s;
}

// ------------------------------------------------------------------ helpers

// "1 error" / "3 errors": key + "1" for one, key + "s" for more.
function count(key, n, a) { return n === 1 ? t(key + "1", n, a) : t(key + "s", n, a); }

// el("div", {class: "x", text: "y", onclick: fn}, child, child...)
function el(tag, attrs) {
  var e = document.createElement(tag);
  if (attrs) Object.keys(attrs).forEach(function (k) {
    var v = attrs[k];
    if (v == null || v === false) return;
    if (k === "text") e.textContent = v;
    else if (k === "class") e.className = v;
    else if (k.slice(0, 2) === "on") e.addEventListener(k.slice(2), v);
    else if (k in e && typeof v !== "string") e[k] = v;
    else e.setAttribute(k, v === true ? "" : v);
  });
  for (var i = 2; i < arguments.length; i++) {
    var c = arguments[i];
    if (c == null || c === false) continue;
    (Array.isArray(c) ? c : [c]).forEach(function (x) {
      if (x != null && x !== false) e.appendChild(typeof x === "string" ? document.createTextNode(x) : x);
    });
  }
  return e;
}

function field(label, control, hint) {
  return el("label", { class: "mzt-field" }, el("span", { class: "mzt-label", text: label }), control,
    hint ? el("span", { class: "mzt-hint", text: hint }) : null);
}

function select(options, value) {
  var s = el("select");
  options.forEach(function (o) { s.appendChild(el("option", { value: o[0], text: o[1] })); });
  if (value != null) s.value = value;
  return s;
}

// Clipboard: the async API needs a secure context and permission; fall back to a hidden textarea.
function copyText(text, button) {
  function done() {
    var old = button.textContent;
    button.textContent = t("copied");
    setTimeout(function () { button.textContent = old; }, 1500);
  }
  function fallback() {
    var ta = el("textarea", { class: "mzt-offscreen" });
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    try { if (document.execCommand("copy")) done(); } catch (e) { /* nothing left to try */ }
    ta.remove();
  }
  if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback);
  else fallback();
}

function downloadText(name, text, type) {
  var a = el("a", { download: name, href: URL.createObjectURL(new Blob([text], { type: type })) });
  document.body.appendChild(a);
  a.click();
  setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
}

// Browser storage can be missing or throw (private windows, blocked site data): the tools work
// without it, it only keeps the form between visits.
var store = {
  get: function (k) { try { return JSON.parse(localStorage.getItem(k) || "null"); } catch (e) { return null; } },
  set: function (k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* not kept */ } },
  del: function (k) { try { localStorage.removeItem(k); } catch (e) { /* nothing kept */ } }
};

function lines(text) {
  return String(text || "").split(/\r?\n/).map(function (l) { return l.trim(); }).filter(Boolean);
}

// A collapsible import box: a textarea, an optional file picker and Load / Cancel.
function importBox(hint, accept, onLoad) {
  var ta = el("textarea", { rows: 8, spellcheck: "false" });
  var status = el("p", { class: "mzt-status", hidden: true });
  var file = el("input", { type: "file", accept: accept, class: "mzt-visually-hidden" });
  var box = el("div", { class: "mzt-import", hidden: true },
    el("p", { class: "mzt-hint", text: hint }), ta,
    el("div", { class: "mzt-row" },
      el("button", { type: "button", class: "mzt-btn mzt-btn--primary", text: t("importLoad"), onclick: function () {
        status.hidden = false;
        var msg = onLoad(ta.value);
        status.textContent = msg.text;
        if (msg.extra) status.appendChild(msg.extra);
        status.className = "mzt-status" + (msg.ok ? " is-ok" : " is-bad");
        if (msg.ok) ta.value = "";
      } }),
      el("button", { type: "button", class: "mzt-btn", text: t("importCancel"), onclick: function () { box.hidden = true; status.hidden = true; } }),
      el("label", { class: "mzt-btn mzt-btn--quiet mzt-file" }, t("chooseFile"), file)),
    status);
  file.addEventListener("change", function () {
    var f = file.files && file.files[0];
    if (!f) return;
    f.text().then(function (txt) { ta.value = txt; file.value = ""; });
  });
  return box;
}

// JSON text -> spans (keys, strings, numbers, true/false/null). textContent stays the JSON.
function jsonNodes(json) {
  var out = [], last = 0, m;
  var re = /("(?:\\u[0-9a-fA-F]{4}|\\[^u]|[^\\"])*")(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g;
  while ((m = re.exec(json))) {
    if (m.index > last) out.push(json.slice(last, m.index));
    var cls = m[1] ? (m[2] ? "j-key" : "j-str") : m[3] ? "j-lit" : "j-num";
    out.push(el("span", { class: cls, text: m[1] || m[0] }));
    if (m[2]) out.push(m[2]);
    last = re.lastIndex;
  }
  if (last < json.length) out.push(json.slice(last));
  return out;
}
function setJson(code, json) {
  code.textContent = "";
  jsonNodes(json).forEach(function (n) { code.appendChild(typeof n === "string" ? document.createTextNode(n) : n); });
}

// Setting names, dot commands and cfg file names in a description shown as code.
var CODE_RE = /\b(?:matchzy|get5|mp|tv|sv|bot|css)_\w+|(?:^|(?<=[\s(]))\.[a-z][a-z0-9_]*(?=[\s,;:.)]|$)|\b[\w-]+\.cfg\b/g;
function codeText(text) {
  var out = [], last = 0, m;
  CODE_RE.lastIndex = 0;
  while ((m = CODE_RE.exec(text))) {
    if (m.index > last) out.push(text.slice(last, m.index));
    out.push(el("code", { text: m[0] }));
    last = CODE_RE.lastIndex;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

// A long name that may break after each "_" (narrow screens).
function breakable(name) {
  var out = [];
  name.split("_").forEach(function (part, i, all) {
    out.push(part + (i < all.length - 1 ? "_" : ""));
    if (i < all.length - 1) out.push(el("wbr"));
  });
  return out;
}

// One-column layout (phones, small windows): a bar that sticks to the bottom of the screen while
// the form scrolls, with the status and Copy; it stops above the result it belongs to.
function dock(status, copyFrom, result) {
  var copy = el("button", { type: "button", class: "mzt-btn mzt-btn--primary", text: t("copy"), onclick: function () { copyText(copyFrom().textContent, copy); } });
  var bar = el("div", { class: "mzt-dock" }, status, el("span", { class: "mzt-row" }, copy,
    el("button", { type: "button", class: "mzt-btn", text: t("toResult") + " ↓", onclick: function () { result.scrollIntoView({ behavior: "smooth", block: "start" }); } })));
  bar._copy = copy;
  return bar;
}

function mount(root) {
  if (!root || root.dataset.ready) return false;
  root.dataset.ready = "1";
  root.textContent = "";
  root.classList.add("is-live");
  return true;
}

// ------------------------------------------------------------------ match config builder

var MODES = { competitive: 5, wingman: 2, rush: 3, "1v1": 1 };
var MODE_NAMES = { competitive: "5v5 (competitive)", wingman: "Wingman (2v2)", rush: "Rush (3v3)", "1v1": "1v1" };
var MAP_POOL = ["de_ancient", "de_anubis", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_overpass", "de_train", "de_vertigo"];
var STEAMID = /^7656119\d{10}$/;
var BUILDER_KEY = "mz-builder-v2";
var SIDE_VALUES = ["knife", "team1_ct", "team1_t", "team2_ct", "team2_t"];

// "76561198000000001 name" / "76561198000000001,name" / "76561198000000001" -> [[id, name]]
// SteamID64, STEAM_0:1:23, [U:1:47] / U:1:47 or a steamcommunity.com/profiles/<id> link -> SteamID64,
// else null. Custom links (/id/<name>) need Steam's web API, so they stay an error.
var STEAM_BASE = BigInt("76561197960265728");
function steamId64(token) {
  var m;
  if (/^\d{17}$/.test(token)) return token;
  if ((m = token.match(/^STEAM_[0-5]:([01]):(\d+)$/i))) return String(STEAM_BASE + BigInt(m[2]) * BigInt(2) + BigInt(m[1]));
  if ((m = token.match(/^\[?U:1:(\d+)\]?$/i))) return String(STEAM_BASE + BigInt(m[1]));
  if ((m = token.match(/steamcommunity\.com\/profiles\/(\d{17})/i))) return m[1];
  return null;
}

function roster(text) {
  return lines(text).map(function (l) {
    // "id name", "id,name", "id;name", "id=name" or "7656...:name"; the id may be any format above.
    var m = l.match(/^(\d{17}):(.*)$/) || l.match(/^(\S+?)(?:[\s,;=]+(.*))?$/);
    var token = m ? m[1] : l, name = m && m[2] ? m[2].trim() : "";
    return [steamId64(token) || token, name];
  });
}
// Shown back in the textarea as SteamID64, so the form says what the config will contain.
function rosterNormalized(text) {
  return roster(text).map(function (e) { return e[0] + (e[1] ? " " + e[1] : ""); }).join("\n");
}
function rosterObject(entries) {
  var o = {};
  entries.forEach(function (e) { o[e[0]] = e[1] || e[0]; });
  return o;
}
function rosterText(token) {
  if (Array.isArray(token)) return token.map(String).join("\n");
  if (token && typeof token === "object") return Object.keys(token).map(function (k) { return k + " " + token[k]; }).join("\n");
  return "";
}

// The veto as the plugin runs it. vetoOrder is Veto/MapVeto.cs GenerateDefaultVetoSetup: the steps
// for a pool of `pool` maps, a series of `numMaps`, `a` starting and `b` the other team.
function vetoOrder(pool, numMaps, a, b) {
  var order = [];
  function alt(kind) { return (order.length % 2 === 0 ? a : b) + "_" + kind; }
  var i;
  if (numMaps === 1) {
    for (i = 0; i < pool - 1; i++) order.push(alt("ban"));
  } else if (numMaps === 2) {
    // ban, ban, pick, pick needs a pool of 4; with 3 maps it is ban, pick, pick.
    if (pool >= 4) order.push(a + "_ban", b + "_ban");
    else if (pool === 3) order.push(a + "_ban");
    order.push(a + "_pick", b + "_pick");
  } else if (pool >= numMaps + 2) {
    var picks = numMaps - 1, bans = pool - 1 - picks;
    var startBans = numMaps >= 5 ? bans : pool - (numMaps + 2);
    for (i = 0; i < startBans; i++) order.push(alt("ban"));
    for (i = 0; i < picks; i++) order.push(alt("pick"));
    for (i = 0; i < bans - startBans; i++) order.push(alt("ban"));
  } else {
    for (i = 0; i < numMaps; i++) order.push(alt("pick"));
  }
  return order;
}
// Logic/MatchConfigLogic.cs ValidateMapBanOrder: "" when the plugin accepts the order.
function vetoOrderProblem(order, pool, numMaps) {
  var picks = 0;
  for (var i = 0; i < order.length; i++) {
    if (order[i] === "team1_pick" || order[i] === "team2_pick") picks++;
    if (picks === numMaps || i === pool - 2) break;
  }
  if (numMaps > 1 && picks < numMaps - 1) return t("vePicks", numMaps, numMaps - 1, picks);
  if (pool - 1 !== order.length && picks !== numMaps) return t("vePool", pool, pool - 1, order.length);
  return "";
}
// Steps as HandleVetoStep plays them: stop once the series has its maps; with one map left in the
// pool, that map is added as the decider.
function vetoSteps(order, pool, numMaps) {
  var left = pool, picked = 0, steps = [];
  for (var i = 0; i < order.length && picked < numMaps; i++) {
    if (left === 1) break;
    var m = order[i].match(/^(team[12])_(ban|pick)$/);
    if (!m) continue;
    steps.push({ team: m[1], kind: m[2], map: m[2] === "pick" ? ++picked : 0 });
    left--;
  }
  var decider = picked < numMaps && left === 1 ? picked + 1 : 0;
  return { steps: steps, decider: decider, unplayed: !decider && picked >= numMaps && left > 0 };
}

// "Start from" presets: they set the match, maps and veto, never the teams' names or players.
function builderTemplates() {
  var pool = defaultBuilderState().maps;
  function base(o) {
    return Object.assign({ mode: "competitive", series: "1", ppt: "", minready: "", skipveto: false, vetofirst: "",
      sidetype: "standard", mapsides: [], maps: pool.slice(), teamModes: ["list", "list"] }, o);
  }
  return [
    ["tplBo1", base({})],
    ["tplBo3", base({ series: "3", clinch: true })],
    ["tplBo5", base({ series: "5", clinch: true })],
    ["tplWingman", base({ mode: "wingman" })],
    ["tpl1v1", base({ mode: "1v1", skipveto: true, maps: [pool[2]] })],
    ["tplBots", base({ skipveto: true, maps: [pool[4]], teamModes: ["list", "bots"] })],
    ["tplOpen", base({ skipveto: true, maps: [pool[4]], teamModes: ["list", "any"] })]
  ];
}

// Share link: the form state in the URL fragment (never sent to a server).
function encodeState(S) {
  var bytes = new TextEncoder().encode(JSON.stringify(S)), bin = "";
  bytes.forEach(function (b) { bin += String.fromCharCode(b); });
  return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
function decodeState(text) {
  var bin = atob(text.replace(/-/g, "+").replace(/_/g, "/"));
  var state = JSON.parse(new TextDecoder().decode(Uint8Array.from(bin, function (c) { return c.charCodeAt(0); })));
  if (!state || typeof state !== "object" || !Array.isArray(state.teams) || state.teams.length !== 2) throw new Error("bad state");
  return state;
}

// A state from localStorage or a #cfg= link may be old or hand-made: keep only the known keys,
// with the types the form expects, so build() cannot trip over it.
function normalizeBuilderState(raw) {
  var d = defaultBuilderState();
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return d;
  function str(v, def) { return v == null ? def : typeof v === "object" ? def : String(v); }
  function strList(v) { return Array.isArray(v) ? v.filter(function (x) { return x != null && typeof x !== "object"; }).map(String) : []; }
  ["matchid", "ppt", "minready", "minspec", "ff", "extramaps", "vetofirst", "sidetype", "specs", "cvars"].forEach(function (k) { d[k] = str(raw[k], d[k]); });
  if (MODES[raw.mode]) d.mode = raw.mode;
  if (["1", "2", "3", "5"].indexOf(String(raw.series)) >= 0) d.series = String(raw.series);
  if (raw.clinch != null) d.clinch = !!raw.clinch;
  d.skipveto = !!raw.skipveto;
  if (raw.maps != null) d.maps = strList(raw.maps);
  d.mapsides = Array.isArray(raw.mapsides) ? raw.mapsides.map(function (v) { return SIDE_VALUES.indexOf(v) >= 0 ? v : ""; }) : [];
  d.vetomode = Array.isArray(raw.vetomode) && raw.vetomode.length ? strList(raw.vetomode) : null;
  if (Array.isArray(raw.teams) && raw.teams.length === 2) {
    d.teams = d.teams.map(function (def, i) {
      var tm = raw.teams[i] && typeof raw.teams[i] === "object" ? raw.teams[i] : {}, o = {};
      Object.keys(def).forEach(function (k) { o[k] = str(tm[k], def[k]); });
      if (["list", "any", "bots"].indexOf(o.mode) < 0) o.mode = "list";
      return o;
    });
  }
  return d;
}

function defaultBuilderState() {
  return {
    matchid: "0", mode: "competitive", series: "3", ppt: "", minready: "", minspec: "", ff: "", clinch: true, vetomode: null,
    maps: ["de_ancient", "de_anubis", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_train"], extramaps: "",
    skipveto: false, vetofirst: "", sidetype: "standard", mapsides: [],
    teams: [
      { name: "Alpha", tag: "", id: "", mode: "list", players: "76561198000000001 alpha1\n76561198000000002 alpha2", difficulty: "2", coaches: "" },
      { name: "Bravo", tag: "", id: "", mode: "list", players: "76561198000000011 bravo1\n76561198000000012 bravo2", difficulty: "2", coaches: "" }
    ],
    specs: "", cvars: ""
  };
}

function initBuilder() {
  var root = document.getElementById("mz-mcb");
  if (!mount(root)) return;
  var sharedBad = false;
  if (/^#cfg=/.test(location.hash)) {
    try { store.set(BUILDER_KEY, normalizeBuilderState(decodeState(location.hash.slice(5)))); } catch (e) { sharedBad = true; }
    history.replaceState(null, "", location.pathname + location.search);
  }
  if (!root._hashBound) {
    root._hashBound = true;
    window.addEventListener("hashchange", function () { if (/^#cfg=/.test(location.hash)) { root.dataset.ready = ""; initBuilder(); } });
  }
  var S = normalizeBuilderState(store.get(BUILDER_KEY));
  var ui = { fields: {} };
  function reg(key, control) { ui.fields[key] = control; return control; }

  function bind(control, key, obj) {
    obj = obj || S;
    var isCheck = control.type === "checkbox";
    if (isCheck) control.checked = !!obj[key]; else control.value = obj[key] == null ? "" : obj[key];
    control.addEventListener(isCheck || control.tagName === "SELECT" ? "change" : "input", function () {
      obj[key] = isCheck ? control.checked : control.value;
      update();
    });
    return control;
  }

  // --- form
  ui.ppt = el("input", { type: "number", min: 1, max: 10 });
  ui.mode = select(Object.keys(MODES).map(function (m) { return [m, MODE_NAMES[m]]; }));
  var matchBox = el("fieldset", { class: "mzt-box" }, el("legend", { text: t("bMatch") }),
    el("div", { class: "mzt-grid" },
      field(t("bMatchId"), bind(el("input", { type: "number", min: 0 }), "matchid"), t("bMatchIdHint")),
      field(t("bGameMode"), bind(ui.mode, "mode")),
      field(t("bSeries"), bind(select([["1", "BO1"], ["2", "BO2"], ["3", "BO3"], ["5", "BO5"]]), "series")),
      field(t("bPpt"), bind(ui.ppt, "ppt")),
      field(t("bMinReady"), bind(el("input", { type: "number", min: 1, max: 10, placeholder: t("bMinReadyAuto") }), "minready"))),
    el("details", { class: "mzt-adv", open: !!(S.minspec || S.ff || !S.clinch) },
      el("summary", { text: t("bAdvanced") }),
      el("div", { class: "mzt-grid" },
        field(t("bMinSpec"), bind(el("input", { type: "number", min: 0, max: 10, placeholder: "0" }), "minspec")),
        field(t("bFf"), bind(select([["", t("bFfMode")], ["on", t("bFfOn")], ["off", t("bFfOff")], ["grenades", t("bFfGren")]]), "ff"))),
      el("label", { class: "mzt-check" }, bind(el("input", { type: "checkbox" }), "clinch"), t("bClinch"))));

  ui.mapGrid = reg("maps", el("div", { class: "mzt-maps" }));
  MAP_POOL.forEach(function (m) {
    var b = el("button", { type: "button", class: "mzt-map", onclick: function () {
      var i = S.maps.indexOf(m);
      if (i >= 0) S.maps.splice(i, 1); else S.maps.push(m);
      update();
    } }, el("span", { class: "mzt-map__n" }), el("span", { text: m }));
    b._map = m;
    ui.mapGrid.appendChild(b);
  });
  var mapsBox = el("fieldset", { class: "mzt-box" }, el("legend", { text: t("bMaps") }),
    el("p", { class: "mzt-hint", text: t("bMapsHint") }), ui.mapGrid,
    field(t("bMoreMaps"), reg("extramaps", bind(el("textarea", { rows: 2, spellcheck: "false", placeholder: "workshop/3070284539" }), "extramaps"))));

  ui.mapSides = el("div", { class: "mzt-grid" });
  ui.mapSidesWrap = el("div", { class: "mzt-sub" }, el("span", { class: "mzt-label", text: t("bMapSides") }), ui.mapSides);
  ui.vetoFirst = select([["", t("bVetoT1")], ["team2", t("bVetoT2")], ["random", t("bRandom")]]);
  var vetoBox = el("fieldset", { class: "mzt-box" }, el("legend", { text: t("bVeto") }),
    el("label", { class: "mzt-check" }, bind(el("input", { type: "checkbox" }), "skipveto"), t("bSkipVeto")),
    el("div", { class: "mzt-grid" },
      field(t("bVetoFirst"), bind(ui.vetoFirst, "vetofirst")),
      field(t("bSides"), bind(select([["standard", t("bSideStd")], ["always_knife", t("bSideAlways")], ["never_knife", t("bSideNever")], ["random", t("bRandom")]]), "sidetype"))),
    ui.mapSidesWrap,
    ui.vetoPreview = el("div", { class: "mzt-veto" }));

  function normalizeOnLeave(control, obj, key) {
    control.addEventListener("change", function () {
      var v = rosterNormalized(control.value);
      if (v !== lines(control.value).join("\n")) { control.value = v; obj[key] = v; update(); }
    });
    return control;
  }

  ui.teamRows = [];
  var teamsWrap = el("div", { class: "mzt-teams" });
  S.teams.forEach(function (team, i) {
    var playersRow = field(t("bRosterLabel"), reg("t" + i + ".players", normalizeOnLeave(bind(el("textarea", { rows: 5, spellcheck: "false" }), "players", team), team, "players")));
    var botsRow = field(t("bBotDiff"), bind(select([["0", "0"], ["1", "1"], ["2", "2"], ["3", "3"]]), "difficulty", team));
    ui.teamRows.push({ players: playersRow, bots: botsRow, team: team });
    teamsWrap.appendChild(el("fieldset", { class: "mzt-box" }, el("legend", { text: t("bTeam", i + 1) }),
      el("div", { class: "mzt-grid mzt-grid--3" },
        field(t("bName"), reg("t" + i + ".name", bind(el("input", { type: "text" }), "name", team))),
        field(t("bTag"), bind(el("input", { type: "text", maxlength: 12 }), "tag", team)),
        field(t("bYourId"), bind(el("input", { type: "text" }), "id", team))),
      field(t("bPlayers"), reg("t" + i + ".mode", bind(select([["list", t("bRoster")], ["any", t("bAny")], ["bots", t("bBots")]]), "mode", team))),
      playersRow, botsRow,
      field(t("bCoaches"), reg("t" + i + ".coaches", normalizeOnLeave(bind(el("textarea", { rows: 2, spellcheck: "false", placeholder: "76561198000000009 coach" }), "coaches", team), team, "coaches")))));
  });

  var extrasBox = el("details", { class: "mzt-box mzt-box--fold", open: !!(S.specs || S.cvars) }, el("summary", { text: t("bExtras") }),
    field(t("bSpecs"), reg("specs", normalizeOnLeave(bind(el("textarea", { rows: 2, spellcheck: "false" }), "specs"), S, "specs"))),
    field(t("bCvars"), reg("cvars", bind(el("textarea", { rows: 3, spellcheck: "false", placeholder: "mp_overtime_enable 1" }), "cvars"))));

  // --- "Start from" presets
  var tplBar = el("div", { class: "mzt-templates" },
    el("div", { class: "mzt-templates__head" }, el("span", { class: "mzt-label", text: t("bStartFrom") }), el("span", { class: "mzt-hint", text: t("bStartHint") })),
    el("div", { class: "mzt-row" }, builderTemplates().map(function (tp) {
      return el("button", { type: "button", class: "mzt-pick mzt-tpl", text: t(tp[0]), title: tp[0] === "tplOpen" ? t("tplOpenHint") : null, onclick: function () {
        var o = tp[1];
        ["mode", "series", "ppt", "minready", "skipveto", "vetofirst", "sidetype"].forEach(function (k) { S[k] = o[k]; });
        if (o.clinch != null) S.clinch = o.clinch;
        S.maps = o.maps.slice(); S.extramaps = ""; S.mapsides = []; S.vetomode = null;
        S.teams.forEach(function (tm, i) { tm.mode = o.teamModes[i]; });
        store.set(BUILDER_KEY, S);
        root.dataset.ready = "";
        initBuilder();
      } });
    })));

  // --- result
  ui.out = el("code");
  ui.summary = el("p", { class: "mzt-summary" });
  ui.problems = el("ul", { class: "mzt-problems", hidden: true });
  ui.status = el("span", { class: "mzt-state" });
  ui.dockStatus = el("span", { class: "mzt-state" });
  var LOAD_CMD = "matchzy_loadmatch cfg/MatchZy/match.json";
  var importPanel = importBox(t("bImportHint"), ".json,application/json", importConfig);
  var copyBtn = el("button", { type: "button", class: "mzt-btn mzt-btn--primary", text: t("copy"), onclick: function () { copyText(ui.out.textContent, copyBtn); } });
  ui.copyBtn = copyBtn;
  var TEST_CMD = "matchzy_validate_match cfg/MatchZy/match.json";
  function cmdLine(cmd) {
    var b = el("button", { type: "button", class: "mzt-btn mzt-btn--quiet mzt-btn--sm", text: t("copyCmd"), onclick: function () { copyText(cmd, b); } });
    return el("div", { class: "mzt-cmd__line" }, el("code", { text: cmd }), b);
  }
  var shareBtn = el("button", { type: "button", class: "mzt-btn mzt-btn--quiet", text: t("bShare"), onclick: function () {
    copyText(location.origin + location.pathname + "#cfg=" + encodeState(S), shareBtn);
  } });
  var result = el("div", { class: "mzt-result" },
    el("div", { class: "mzt-result__head" },
      el("span", { class: "mzt-row" }, el("strong", { text: t("bResult") }), ui.status),
      el("span", { class: "mzt-row" }, copyBtn,
        el("button", { type: "button", class: "mzt-btn", text: t("download"), onclick: function () { downloadText("match.json", ui.out.textContent + "\n", "application/json"); } }),
        el("button", { type: "button", class: "mzt-btn mzt-btn--quiet", text: t("importBtn"), onclick: function () { importPanel.hidden = !importPanel.hidden; } }),
        shareBtn,
        el("button", { type: "button", class: "mzt-btn mzt-btn--quiet", text: t("reset"), onclick: function () {
          store.del(BUILDER_KEY);
          root.dataset.ready = "";
          initBuilder();
        } }))),
    importPanel, ui.summary, ui.problems,
    el("pre", { class: "mzt-out mzt-out--json" }, ui.out),
    el("div", { class: "mzt-cmd" },
      el("span", { class: "mzt-hint", text: t("bTestWith") }), cmdLine(TEST_CMD),
      el("span", { class: "mzt-hint", text: t("bLoadWith") }), cmdLine(LOAD_CMD)));
  if (sharedBad) ui.problems.before(el("p", { class: "mzt-status is-bad", text: t("bShareBad") }));
  ui.dock = dock(ui.dockStatus, function () { return ui.out; }, result);

  root.appendChild(el("div", { class: "mzt-form" }, tplBar, matchBox, mapsBox, vetoBox, teamsWrap, extrasBox, ui.dock));
  root.appendChild(result);

  // --- import: fill the form from an existing config
  // Booleans as the plugin reads them (ParseCvarBool): true/false/1/0, string or not, any case.
  function cfgBool(v, def) {
    var x = v == null ? "" : String(v).trim().replace(/^"|"$/g, "").trim().toLowerCase();
    return x === "1" || x === "true" ? true : x === "0" || x === "false" ? false : def;
  }
  // Logic/MatchConfigLogic.cs IsBotTeamToken: true, a number > 0, or such a string.
  function botTeam(v) {
    if (typeof v === "boolean") return v;
    if (typeof v === "number") return Math.floor(v) === v && v > 0;
    if (typeof v !== "string") return false;
    var x = v.trim().toLowerCase();
    return x === "true" ? true : x === "false" ? false : /^[+-]?\d+$/.test(x) && parseInt(x, 10) > 0;
  }
  // IsOpenRosterToken: "any" (any case) or "*".
  function openRoster(v) { return typeof v === "string" && (v.trim().toLowerCase() === "any" || v.trim() === "*"); }
  function importConfig(text) {
    var c;
    try { c = JSON.parse(text); } catch (e) { return { ok: false, text: t("bImportBad") + e.message }; }
    if (!c || typeof c !== "object" || !c.team1 || !c.team2) return { ok: false, text: t("bImportBad") + "team1 / team2" };
    var known = ["matchid", "num_maps", "maplist", "game_mode", "wingman", "skip_veto", "veto_first", "side_type", "map_sides",
      "players_per_team", "min_players_to_ready", "min_spectators_to_ready", "clinch_series", "friendly_fire", "team1", "team2", "spectators", "cvars", "veto_mode"];
    var next = defaultBuilderState();
    next.matchid = String(c.matchid || 0);
    next.mode = c.game_mode && MODES[c.game_mode] ? c.game_mode : cfgBool(c.wingman, false) ? "wingman" : "competitive";
    next.series = String(c.num_maps || 1);
    next.ppt = c.players_per_team != null ? String(c.players_per_team) : "";
    next.minready = c.min_players_to_ready != null ? String(c.min_players_to_ready) : "";
    next.minspec = c.min_spectators_to_ready != null ? String(c.min_spectators_to_ready) : "";
    next.ff = c.friendly_fire === true ? "on" : c.friendly_fire === false ? "off" : c.friendly_fire === "grenades" ? "grenades" : "";
    next.clinch = cfgBool(c.clinch_series, true);
    var maps = Array.isArray(c.maplist) ? c.maplist.map(String) : [];
    next.maps = maps.filter(function (m) { return MAP_POOL.indexOf(m) >= 0; });
    next.extramaps = maps.filter(function (m) { return MAP_POOL.indexOf(m) < 0; }).join("\n");
    next.skipveto = cfgBool(c.skip_veto, false);
    next.vetofirst = c.veto_first === "team2" || c.veto_first === "random" ? c.veto_first : "";
    next.sidetype = c.side_type || "standard";
    next.mapsides = Array.isArray(c.map_sides) ? c.map_sides.map(function (v) { return SIDE_VALUES.indexOf(String(v)) >= 0 ? String(v) : ""; }) : [];
    next.vetomode = Array.isArray(c.veto_mode) && c.veto_mode.length ? c.veto_mode.map(String) : null;
    ["team1", "team2"].forEach(function (k, i) {
      var tm = c[k], d = next.teams[i];
      d.name = tm.name || ""; d.tag = tm.tag || ""; d.id = tm.id != null ? String(tm.id) : "";
      d.mode = botTeam(tm.bots) ? "bots" : openRoster(tm.players) ? "any" : "list";
      d.players = d.mode === "list" ? rosterText(tm.players) : "";
      d.difficulty = String(tm.bot_difficulty != null ? tm.bot_difficulty : 2);
      d.coaches = rosterText(tm.coaches);
    });
    next.specs = rosterText(c.spectators && c.spectators.players);
    next.cvars = c.cvars ? Object.keys(c.cvars).map(function (k) { return k + " " + c.cvars[k]; }).join("\n") : "";
    store.set(BUILDER_KEY, next);
    var unknown = Object.keys(c).filter(function (k) { return known.indexOf(k) < 0; });
    root.dataset.ready = "";
    initBuilder();
    return { ok: true, text: unknown.length ? t("bImported") + unknown.join(", ") : "OK" };
  }

  // --- build the config and the problem list from the state
  function build() {
    var errors = [], warnings = [], bad = {};
    function err(msg) {
      if (errors.indexOf(msg) < 0) errors.push(msg);
      for (var k = 1; k < arguments.length; k++) bad[arguments[k]] = true;
    }
    var mode = S.mode, numMaps = parseInt(S.series, 10) || 1, modeDefault = MODES[mode];
    var maps = S.maps.concat(lines(S.extramaps));
    var c = {};
    var matchid = parseInt(S.matchid, 10);
    c.matchid = isNaN(matchid) ? 0 : matchid;
    c.num_maps = numMaps;
    c.maplist = maps;
    if (mode !== "competitive") c.game_mode = mode;

    maps.forEach(function (m, i) { if (maps.indexOf(m) !== i) err(t("eDupMap", m), "maps", "extramaps"); });
    if (maps.length < numMaps) err(t("eTooFewMaps", numMaps), "maps");

    var rosters = [], teamsOut = [];
    S.teams.forEach(function (tm, i) {
      var o = { name: tm.name.trim() };
      if (!o.name) err(t("eNoName", t("bTeam", i + 1)), "t" + i + ".name");
      if (tm.tag.trim()) o.tag = tm.tag.trim();
      if (tm.id.trim()) o.id = tm.id.trim();
      var ids = [];
      if (tm.mode === "any") o.players = "any";
      else if (tm.mode === "bots") { o.bots = true; o.bot_difficulty = parseInt(tm.difficulty, 10); }
      else {
        var p = roster(tm.players);
        p.forEach(function (e) { if (!STEAMID.test(e[0])) err(t(/steamcommunity\.com\/id\//i.test(e[0]) ? "eVanity" : "eSteamId", e[0]), "t" + i + ".players"); });
        if (!p.length) err(t("eNoPlayers", o.name || t("bTeam", i + 1)), "t" + i + ".players");
        o.players = rosterObject(p);
        ids = p.map(function (e) { return e[0]; });
      }
      var co = roster(tm.coaches);
      co.forEach(function (e) { if (!STEAMID.test(e[0])) err(t("eSteamId", e[0]), "t" + i + ".coaches"); });
      if (co.length) o.coaches = rosterObject(co);
      var coachIds = co.map(function (e) { return e[0]; });
      // Each SteamID once (the config's players object has unique keys), coaches not counted.
      rosters.push(ids.filter(function (id, k) { return coachIds.indexOf(id) < 0 && ids.indexOf(id) === k; }));
      teamsOut.push(o);
    });
    var anyBots = S.teams.some(function (tm) { return tm.mode === "bots"; });
    if (S.teams[0].mode === "any" && S.teams[1].mode === "any") err(t("eTwoAny"), "t0.mode", "t1.mode");
    if (S.teams[0].mode === "bots" && S.teams[1].mode === "bots") err(t("eTwoBots"), "t0.mode", "t1.mode");

    var skip = S.skipveto || maps.length === numMaps;
    if (anyBots && maps.length > numMaps && !S.skipveto) err(t("eBotsVeto"), "maps");
    if (skip) c.skip_veto = true;
    var vp = "";
    if (!skip && S.vetomode) {
      c.veto_mode = S.vetomode;
      vp = vetoOrderProblem(S.vetomode, maps.length, numMaps);
      if (vp) err(t("eVetoMode", vp), "maps");
    }
    // Safety net: the veto must end with as many maps (picks + decider) as the series needs.
    if (!skip && !vp && maps.length >= numMaps) {
      var vs = vetoSteps(S.vetomode || vetoOrder(maps.length, numMaps, "team1", "team2"), maps.length, numMaps);
      var got = vs.steps.filter(function (st) { return st.kind === "pick"; }).length + (vs.decider ? 1 : 0);
      if (got < numMaps) err(t("eVetoShort", got, numMaps), "maps");
    }
    // Always written: left out, the plugin starts with the team that ended the previous veto.
    if (!skip) c.veto_first = S.vetofirst || "team1";
    if (S.sidetype !== "standard") c.side_type = S.sidetype;
    if (skip) {
      // An unset side is what the plugin would fill in from side_type. Nothing chosen: map_sides
      // is left out and the plugin fills it (random draws per map).
      var sides = [], chosen = false;
      var fill = S.sidetype === "never_knife" ? "team1_ct" : "knife";
      for (var i = 0; i < numMaps; i++) {
        var sd = SIDE_VALUES.indexOf(S.mapsides[i]) >= 0 ? S.mapsides[i] : "";
        if (sd) chosen = true;
        sides.push(sd || fill);
      }
      if (chosen) c.map_sides = sides;
    }

    // Team size as the plugin works it out: what the form says, else the larger roster
    // (coaches not counted), at most the mode's default.
    var pptSet = S.ppt !== "" && !isNaN(parseInt(S.ppt, 10));
    var largest = Math.max(rosters[0].length, rosters[1].length);
    var ppt = pptSet ? parseInt(S.ppt, 10) : largest > 0 ? Math.min(largest, modeDefault) : modeDefault;
    // Always written when filled in: left out, the plugin takes the size from the rosters.
    if (pptSet) c.players_per_team = ppt;
    var minReady = parseInt(S.minready, 10);
    if (!isNaN(minReady)) c.min_players_to_ready = minReady;
    var minSpec = parseInt(S.minspec, 10);
    if (!isNaN(minSpec) && minSpec > 0) c.min_spectators_to_ready = minSpec;
    if (!S.clinch) c.clinch_series = false;
    if (S.ff === "on") c.friendly_fire = true;
    else if (S.ff === "off") c.friendly_fire = false;
    else if (S.ff === "grenades") c.friendly_fire = "grenades";

    c.team1 = teamsOut[0];
    c.team2 = teamsOut[1];

    var specs = roster(S.specs);
    specs.forEach(function (e) { if (!STEAMID.test(e[0])) err(t("eSteamId", e[0]), "specs"); });
    if (specs.length) c.spectators = { players: rosterObject(specs) };

    var cvars = {};
    lines(S.cvars).forEach(function (l) {
      var m = l.match(/^([A-Za-z0-9_]+)\s+(.+)$/);
      // Same name rule as the plugin; these two it refuses to set from a match config.
      if (!m || /^(rcon_password|matchzy_everyone_is_admin)$/i.test(m[1])) { err(t("eCvar", l), "cvars"); return; }
      var value = m[2].replace(/^"(.*)"$/, "$1");
      if (/[;"]/.test(value)) { err(t("eCvar", l), "cvars"); return; }
      cvars[m[1]] = value;
    });
    if (Object.keys(cvars).length) c.cvars = cvars;

    // The plugin's load warnings (Logic/MatchConfigWarnings.cs), so they show up before loading.
    var names = [c.team1.name || "team1", c.team2.name || "team2"];
    rosters[0].forEach(function (id) { if (rosters[1].indexOf(id) >= 0) warnings.push(t("wBoth", id)); });
    var specIds = specs.map(function (e) { return e[0]; });
    rosters.forEach(function (r, i) { r.forEach(function (id) { if (specIds.indexOf(id) >= 0) warnings.push(t("wSpec", id, names[i])); }); });
    rosters.forEach(function (r, i) { if (r.length > ppt) warnings.push(t("wRosterBig", names[i], r.length, ppt)); });
    var size = rosters.map(function (r) { return r.length ? Math.min(r.length, ppt) : ppt; });
    if (rosters[0].length && rosters[1].length && size[0] !== size[1]) warnings.push(t("wUneven", names[0], size[0], names[1], size[1]));
    var cvarPerTeam = Object.prototype.hasOwnProperty.call(cvars, "matchzy_ready_per_team") && /^[+-]?\d+$/.test(cvars.matchzy_ready_per_team.trim())
      ? parseInt(cvars.matchzy_ready_per_team, 10) : 0;
    if (!isNaN(minReady) && cvarPerTeam > 0) warnings.push(t("wMinReadyIgnored", minReady, cvarPerTeam));
    else if (!isNaN(minReady) && minReady >= 1 && minReady < Math.max(size[0], size[1])) warnings.push(t("wMinReady", minReady));
    if (pptSet && ppt > modeDefault) warnings.push(t("wPptBig", ppt, MODE_NAMES[mode], modeDefault));

    return { config: c, errors: errors, warnings: warnings, bad: bad, ppt: ppt, pptSet: pptSet, skip: skip, numMaps: numMaps, maps: maps };
  }

  function teamName(k) { var i = k === "team1" ? 0 : 1; return (S.teams[i].name || "").trim() || t("bTeam", i + 1); }
  // With a random start the names are not known yet: "the team that starts" / "the other team".
  function vetoName(k) { return S.vetofirst === "random" ? t(k === "team1" ? "vFirst" : "vSecond") : teamName(k); }
  function renderVeto(b) {
    var box = ui.vetoPreview;
    box.hidden = b.skip || b.maps.length <= b.numMaps;
    if (box.hidden) return;
    box.textContent = "";
    var start = S.vetofirst === "team2" ? "team2" : "team1", other = start === "team1" ? "team2" : "team1";
    var order = S.vetomode || vetoOrder(b.maps.length, b.numMaps, start, other);
    var v = vetoSteps(order, b.maps.length, b.numMaps);
    var standard = S.sidetype === "standard";
    var list = el("ol", { class: "mzt-veto__steps" });
    v.steps.forEach(function (st) {
      var who = vetoName(st.team), o = vetoName(st.team === "team1" ? "team2" : "team1");
      list.appendChild(el("li", { class: "is-" + st.kind },
        el("span", { class: "mzt-veto__tag", text: st.kind === "ban" ? "BAN" : "PICK" }),
        st.kind === "ban" ? t("vBan", who) : standard ? t("vPickSide", who, st.map, o) : t("vPick", who, st.map)));
    });
    if (v.decider) list.appendChild(el("li", { class: "is-decider" }, el("span", { class: "mzt-veto__tag", text: "DECIDER" }),
      standard ? t("vDeciderKnife", v.decider) : t("vDecider", v.decider)));
    box.appendChild(el("strong", { text: t("vTitle") }));
    if (S.vetofirst === "random") box.appendChild(el("p", { class: "mzt-hint", text: t("vRandomStart") }));
    if (S.vetomode) box.appendChild(el("p", { class: "mzt-hint" }, t("vCustom"), " ",
      el("button", { type: "button", class: "mzt-btn mzt-btn--quiet mzt-btn--sm", text: t("vUseDefault"), onclick: function () { S.vetomode = null; update(); } })));
    box.appendChild(list);
    if (v.unplayed) box.appendChild(el("p", { class: "mzt-hint", text: t("vUnplayed") }));
    var sides = S.sidetype === "always_knife" ? t("vSidesKnife") : S.sidetype === "never_knife" ? t("vSidesNever", teamName("team1")) : S.sidetype === "random" ? t("vSidesRandom") : "";
    if (sides) box.appendChild(el("p", { class: "mzt-hint", text: sides }));
  }

  // A state that still breaks the form falls back to the defaults instead of a blank tool.
  function update() {
    try { render(); } catch (e) {
      if (root._recovering) throw e;
      if (window.console) console.error("match builder: state reset", e);
      root._recovering = true;
      store.del(BUILDER_KEY);
      root.dataset.ready = "";
      try { initBuilder(); } finally { root._recovering = false; }
    }
  }

  function render() {
    store.set(BUILDER_KEY, S);
    ui.ppt.placeholder = t("bPptAuto");
    ui.mapGrid.querySelectorAll(".mzt-map").forEach(function (b) {
      var i = S.maps.indexOf(b._map);
      b.classList.toggle("is-on", i >= 0);
      b.setAttribute("aria-pressed", i >= 0 ? "true" : "false");
      b.firstChild.textContent = i >= 0 ? String(i + 1) : "";
    });
    ui.teamRows.forEach(function (r) {
      r.players.hidden = r.team.mode !== "list";
      r.bots.hidden = r.team.mode !== "bots";
    });
    var b = build();
    ui.vetoFirst.disabled = b.skip;
    // Side per map: only without a veto (with one, the veto decides).
    ui.mapSidesWrap.hidden = !b.skip;
    renderVeto(b);
    if (b.skip && ui.mapSides.childElementCount !== b.numMaps) {
      ui.mapSides.textContent = "";
      for (var i = 0; i < b.numMaps; i++) {
        (function (n) {
          var s = select([["", t("bSideAuto")]].concat(SIDE_VALUES.map(function (v) { return [v, v === "knife" ? t("bKnife") : v]; })), S.mapsides[n] || "");
          s.addEventListener("change", function () { S.mapsides[n] = s.value; update(); });
          ui.mapSides.appendChild(field(t("bMapN", n + 1) + (b.maps[n] ? ": " + b.maps[n] : ""), s));
        })(i);
      }
    } else if (b.skip) {
      ui.mapSides.querySelectorAll(".mzt-label").forEach(function (lab, n) { lab.textContent = t("bMapN", n + 1) + (b.maps[n] ? ": " + b.maps[n] : ""); });
    }
    setJson(ui.out, JSON.stringify(b.config, null, 2));
    ui.summary.textContent = t("bSummary", "BO" + b.numMaps, b.maps.length, b.skip ? t("bSummaryNoVeto") : t("bSummaryVeto"),
      b.ppt + "v" + b.ppt, b.pptSet ? "" : t("bFromRosters"));
    ui.problems.textContent = "";
    b.errors.forEach(function (e) { ui.problems.appendChild(el("li", { class: "is-error", text: e })); });
    b.warnings.forEach(function (w) { ui.problems.appendChild(el("li", { class: "is-warn", text: w })); });
    ui.problems.hidden = !b.errors.length && !b.warnings.length;
    Object.keys(ui.fields).forEach(function (k) {
      ui.fields[k].classList.toggle("is-invalid", !!b.bad[k]);
      if (ui.fields[k].tagName !== "DIV") ui.fields[k].setAttribute("aria-invalid", b.bad[k] ? "true" : "false");
    });
    // Status next to the title and in the bottom bar; Copy says when the config has errors.
    [ui.status, ui.dockStatus].forEach(function (st) {
      st.className = "mzt-state " + (b.errors.length ? "is-error" : b.warnings.length ? "is-warn" : "is-ok");
      st.textContent = b.errors.length ? count("nError", b.errors.length) : b.warnings.length ? count("nWarning", b.warnings.length) : t("ready");
    });
    [ui.copyBtn, ui.dock._copy].forEach(function (btn) {
      btn.textContent = b.errors.length ? t("copyAnyway", count("nError", b.errors.length)) : t("copy");
      btn.classList.toggle("is-warn", b.errors.length > 0);
    });
  }

  update();
}

// ------------------------------------------------------------------ settings generator

var CFG_KEY = "mz-cfggen-v2";

// Quick setup: the settings each kind of server usually changes. Only names from config.cfg are
// used; a name the plugin no longer has is skipped.
var QUICK = [
  ["qPugs", {
    matchzy_autostart_mode: "1", matchzy_kick_when_no_match_loaded: "false", matchzy_match_end_auto_changelevel: "true",
    matchzy_warmup_instant_respawn: "true", matchzy_demo_live_only: "true", matchzy_demo_keep_days: "14", matchzy_backup_keep_days: "14"
  }],
  ["qPanel", {
    matchzy_autostart_mode: "1", matchzy_kick_when_no_match_loaded: "true", matchzy_match_end_auto_changelevel: "false",
    matchzy_coach_listed_only: "true", matchzy_team_clantag_enabled: "true", matchzy_hostname_format: "{TEAM1} vs {TEAM2}",
    matchzy_demo_live_only: "true", matchzy_demo_keep_days: "30", matchzy_backup_keep_days: "30"
  }],
  ["qScrims", {
    matchzy_autostart_mode: "1", matchzy_playout_enabled_default: "true", matchzy_autopause_minplayers: "3",
    matchzy_demo_live_only: "true", matchzy_demo_keep_days: "14"
  }],
  ["qPractice", {
    matchzy_autostart_mode: "2", matchzy_demo_live_only: "true", matchzy_save_nades_as_global_enabled: "true"
  }]
];

// "0 = neither, 1 = match mode, 2 = practice mode" -> [["0","neither"], ...]: shown as quick picks.
function valueChoices(s) {
  if (s.type !== "number") return [];
  var out = [], seen = {}, re = /(^|[\s(,;:])(-?\d+(?:\.\d+)?)\s*=\s*([^,.;()]+)/g, m;
  while ((m = re.exec(s.description))) {
    var label = m[3].trim().replace(/\s+/g, " ");
    if (!seen[m[2]] && label.length <= 40) { seen[m[2]] = 1; out.push([m[2], label]); }
  }
  return out.length >= 2 ? out : [];
}

function cfgLine(s, value) {
  if (s.type === "text") return s.name + ' "' + String(value).replace(/"/g, "") + '"';
  return s.name + " " + value;
}

function initGenerator() {
  var root = document.getElementById("mz-cfg");
  var data = document.getElementById("mz-cfg-data");
  if (!data || !mount(root)) return;
  var settings = JSON.parse(data.textContent);
  var byName = {};
  settings.forEach(function (s) { byName[s.name] = s; });
  var saved = store.get(CFG_KEY) || {};
  var values = saved.values || {};
  var view = saved.view === "changed" ? "changed" : "full";
  Object.keys(values).forEach(function (k) { if (!byName[k]) delete values[k]; });

  var onlyNames = null; // after an import: only the settings the imported file did not have
  function changed(s) { return Object.prototype.hasOwnProperty.call(values, s.name) && String(values[s.name]) !== s.default; }
  function current(s) { return Object.prototype.hasOwnProperty.call(values, s.name) ? String(values[s.name]) : s.default; }
  function save() { store.set(CFG_KEY, { values: values, view: view }); }

  var sections = [];
  settings.forEach(function (s) {
    if (!sections.length || sections[sections.length - 1].name !== s.section) sections.push({ name: s.section, items: [] });
    sections[sections.length - 1].items.push(s);
  });

  // --- left: search, section chips, the settings
  var search = el("input", { type: "search", placeholder: t("gSearch"), autocomplete: "off", "aria-label": t("gSearch") });
  var onlyChanged = el("input", { type: "checkbox" });
  var chips = el("nav", { class: "mzt-chips" });
  var list = el("div", { class: "mzt-cfglist" });
  var noHits = el("p", { class: "mzt-hint mzt-nohits", text: t("gNoHits"), hidden: true });
  var rows = [];

  sections.forEach(function (sec, si) {
    var id = "mzc-sec-" + si;
    var count = el("span", { class: "mzt-chip__n" });
    var chip = el("a", { class: "mzt-chip", href: "#" + id }, sec.name, " ", count);
    chips.appendChild(chip);
    var head = el("h3", { class: "mzt-sechead", id: id }, sec.name, " ", el("span", { class: "mzt-sechead__n", text: String(sec.items.length) }));
    var box = el("div", { class: "mzt-section" });
    var wrap = el("section", null, head, box);
    sec.el = wrap; sec.chip = chip; sec.count = count;
    sec.items.forEach(function (s) {
      var control, choices = valueChoices(s);
      if (s.type === "bool") {
        control = el("input", { type: "checkbox", class: "mzt-switch", role: "switch", "aria-label": s.name });
      } else {
        control = el("input", { type: s.type === "number" ? "number" : "text", step: s.type === "number" ? "any" : null,
          placeholder: s.example || "", "aria-label": s.name, class: "mzt-input" });
      }
      var picks = choices.length ? el("div", { class: "mzt-picks" }) : null;
      var resetBtn = el("button", { type: "button", class: "mzt-reset", title: t("gResetOne"), "aria-label": t("gResetOne"), text: "↺" });
      var row = el("div", { class: "mzt-setting", id: s.name },
        el("div", { class: "mzt-setting__head" + (s.type === "bool" ? " mzt-setting__head--switch" : "") },
          el("a", { class: "mzt-setting__link", href: "#" + s.name, title: t("gLink") }, el("code", { class: "mzt-setting__name" }, breakable(s.name))),
          el("div", { class: "mzt-setting__ctl" }, control, resetBtn)),
        picks,
        el("p", { class: "mzt-setting__desc" }, codeText(s.description), " ",
          el("span", { class: "mzt-setting__def", text: t("gDefault") + ": " + (s.default === "" ? (s.optional ? t("gUnset") : '""') : s.default) })));
      var r = { s: s, row: row, control: control, picks: picks, hay: (s.name + " " + s.description + " " + s.section).toLowerCase() };
      rows.push(r);
      function set(v) { if (String(v) === s.default) delete values[s.name]; else values[s.name] = String(v); refresh(); }
      if (s.type === "bool") control.addEventListener("change", function () { set(control.checked ? "true" : "false"); });
      else control.addEventListener("input", function () { set(control.value); });
      resetBtn.addEventListener("click", function () { delete values[s.name]; refresh(); });
      if (picks) choices.forEach(function (ch) {
        var b = el("button", { type: "button", class: "mzt-pick", onclick: function () { set(ch[0]); } },
          el("b", { text: ch[0] }), " " + ch[1]);
        b._v = ch[0];
        picks.appendChild(b);
      });
      box.appendChild(row);
    });
    list.appendChild(wrap);
  });

  // --- right: the result
  var out = el("code");
  var countEl = el("span", { class: "mzt-count" });
  var tabFull = el("button", { type: "button", class: "mzt-seg__btn", role: "tab", text: t("gFull"), onclick: function () { view = "full"; refresh(); } });
  var tabChanged = el("button", { type: "button", class: "mzt-seg__btn", role: "tab", text: t("gChanged"), onclick: function () { view = "changed"; refresh(); } });
  var importPanel = importBox(t("gImportHint"), ".cfg,.txt,text/plain", function (text) {
    var n = 0, unknown = [], seen = {};
    lines(text).forEach(function (l) {
      if (l.indexOf("//") === 0) return;
      // A quoted value first, so "//" inside it ("https://...") is not taken as a comment.
      var m = l.match(/^([A-Za-z0-9_]+)\s+("[^"]*"|[^"]*?)\s*(?:\/\/.*)?$/);
      if (!m) return;
      var s = byName[m[1]];
      if (!s) { if (/^(matchzy|get5)_/.test(m[1])) unknown.push(m[1]); return; }
      var v = m[2].replace(/^"(.*)"$/, "$1");
      if (s.type === "bool") v = /^(1|true)$/i.test(v) ? "true" : "false";
      if (v === s.default) delete values[s.name]; else values[s.name] = v;
      seen[s.name] = true;
      n++;
    });
    // A whole config.cfg (not a few lines): the settings it lacks were added since it was written.
    var missing = n >= 10 ? settings.filter(function (s) { return !s.optional && !seen[s.name]; }).map(function (s) { return s.name; }) : [];
    onlyNames = null;
    quickStatus.hidden = true;
    refresh();
    var extra = missing.length ? el("span", null, count("gNewSince", missing.length), " ",
      el("button", { type: "button", class: "mzt-btn mzt-btn--quiet mzt-btn--sm", text: t("gShowNew"), onclick: function () {
        onlyNames = missing; search.value = ""; onlyChanged.checked = false; refresh();
        list.scrollIntoView({ behavior: "smooth", block: "start" });
      } })) : null;
    return { ok: n > 0, text: count("gImported", n) + (unknown.length ? t("gUnknown", unknown.slice(0, 8).join(", ")) : ""), extra: extra };
  });
  var copyBtn = el("button", { type: "button", class: "mzt-btn mzt-btn--primary", text: t("copy"), onclick: function () { copyText(out.textContent, copyBtn); } });
  var result = el("div", { class: "mzt-result" },
    el("div", { class: "mzt-result__head" }, el("strong", { text: t("gYour") }), countEl),
    el("div", { class: "mzt-seg", role: "tablist" }, tabFull, tabChanged),
    el("div", { class: "mzt-row" }, copyBtn,
      el("button", { type: "button", class: "mzt-btn", text: t("download"), onclick: function () {
        downloadText(view === "full" ? "config.cfg" : "matchzy-settings.cfg", out.textContent + "\n", "text/plain");
      } }),
      el("button", { type: "button", class: "mzt-btn mzt-btn--quiet", text: t("importBtn"), onclick: function () { importPanel.hidden = !importPanel.hidden; } }),
      el("button", { type: "button", class: "mzt-btn mzt-btn--quiet", text: t("resetAll"), onclick: function () { values = {}; onlyNames = null; quickStatus.hidden = true; refresh(); } })),
    importPanel,
    el("pre", { class: "mzt-out mzt-out--cfg" }, out));

  var quickStatus = el("p", { class: "mzt-status is-ok", hidden: true });
  var quick = el("div", { class: "mzt-quick" },
    el("strong", { text: t("qTitle") }), el("p", { class: "mzt-hint", text: t("qAsk") }),
    el("div", { class: "mzt-row" }, QUICK.map(function (q) {
      return el("button", { type: "button", class: "mzt-pick mzt-tpl", text: t(q[0]), onclick: function () {
        // Profiles replace each other: what another profile set goes back to the default first, so
        // "Pugs" then "Practice server" does not leave the pug warmup on a practice server.
        var n = 0, target = {};
        QUICK.forEach(function (other) { Object.keys(other[1]).forEach(function (name) { if (byName[name]) target[name] = byName[name].default; }); });
        Object.keys(q[1]).forEach(function (name) { if (byName[name]) target[name] = q[1][name]; });
        Object.keys(target).forEach(function (name) {
          var st = byName[name];
          if (current(st) !== target[name]) n++;
          if (target[name] === st.default) delete values[name]; else values[name] = target[name];
        });
        onlyNames = null; search.value = ""; onlyChanged.checked = true;
        refresh();
        quickStatus.hidden = false;
        quickStatus.textContent = n ? count("qDone", n, t(q[0])) : t("qSame", t(q[0]));
      } });
    })), quickStatus);
  var filterNote = el("span", { class: "mzt-cursec mzt-filternote", hidden: true }, "",
    el("button", { type: "button", class: "mzt-btn mzt-btn--quiet mzt-btn--sm", text: t("gShowAll"), onclick: function () { onlyNames = null; refresh(); } }));
  var shownEl = el("span", { class: "mzt-hint mzt-shown" });
  var curSec = el("span", { class: "mzt-cursec", hidden: true });
  var toolbar = el("div", { class: "mzt-toolbar" }, search,
    el("label", { class: "mzt-check" }, onlyChanged, t("gOnlyChanged")), shownEl, curSec, filterNote);
  var dockStatus = el("span", { class: "mzt-state" });
  var dockBar = dock(dockStatus, function () { return out; }, result);
  root.appendChild(el("div", { class: "mzt-cfgmain" }, quick, toolbar, chips, list, noHits, dockBar));
  root.appendChild(result);

  // The section under the sticky toolbar, shown in it once the section heading has scrolled away.
  var secTick = false;
  function trackSection() {
    secTick = false;
    var edge = toolbar.getBoundingClientRect().bottom + 8, name = "";
    sections.forEach(function (sec) {
      if (!sec.el.hidden && sec.el.getBoundingClientRect().top < edge) name = sec.name;
    });
    var headGone = name && sections.some(function (sec) { return sec.name === name && sec.el.firstChild.getBoundingClientRect().bottom < edge; });
    curSec.hidden = !headGone;
    curSec.textContent = name;
    toolbar.classList.toggle("is-stuck", toolbar.getBoundingClientRect().top <= parseFloat(getComputedStyle(toolbar).top) + 1 && window.scrollY > 0);
  }
  window.addEventListener("scroll", function () { if (!secTick) { secTick = true; requestAnimationFrame(trackSection); } }, { passive: true });

  function renderOut() {
    out.textContent = "";
    var nChanged = settings.filter(changed).length;
    if (view === "changed") {
      if (!nChanged) { out.appendChild(el("span", { class: "c-com", text: "// " + t("gNoneChanged") })); return; }
      out.appendChild(el("span", { class: "c-com", text: "// " + t("gHeaderChanged") + "\n" }));
      settings.forEach(function (s) { if (changed(s)) out.appendChild(el("span", { class: "c-chg", text: cfgLine(s, current(s)) + "\n" })); });
      return;
    }
    out.appendChild(el("span", { class: "c-com", text: "// " + t("gHeaderFull") + "\n" }));
    sections.forEach(function (sec) {
      out.appendChild(el("span", { class: "c-com", text: "\n// ==== " + sec.name + "\n" }));
      sec.items.forEach(function (s) {
        var v = current(s);
        if (s.optional && v === "") out.appendChild(el("span", { class: "c-com", text: "// " + s.name + (s.example ? " " + s.example : "") + "\n" }));
        else if (changed(s)) out.appendChild(el("span", { class: "c-chg", text: cfgLine(s, v) + "\n" }));
        else out.appendChild(el("span", null, el("span", { class: "c-key", text: s.name }), cfgLine(s, v).slice(s.name.length) + "\n"));
      });
    });
  }

  function refresh() {
    save();
    var words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
    var visible = 0;
    rows.forEach(function (r) {
      var s = r.s, v = current(s), ch = changed(s);
      if (s.type === "bool") r.control.checked = v === "true";
      else if (document.activeElement !== r.control) r.control.value = v;
      r.row.classList.toggle("is-changed", ch);
      if (r.picks) r.picks.querySelectorAll(".mzt-pick").forEach(function (b) { b.classList.toggle("is-on", b._v === v); });
      // Every word must appear, in any order: "tech pause" finds "technical pause".
      var hit = words.every(function (w) { return r.hay.indexOf(w) !== -1; }) && (!onlyChanged.checked || ch) && (!onlyNames || onlyNames.indexOf(s.name) >= 0);
      r.row.hidden = !hit;
      if (hit) visible++;
    });
    sections.forEach(function (sec) {
      var shown = sec.items.filter(function (s, i) { return !rows.filter(function (r) { return r.s === s; })[0].row.hidden; }).length;
      var nch = sec.items.filter(changed).length;
      sec.el.hidden = !shown;
      sec.chip.hidden = !shown;
      sec.count.textContent = nch ? "· " + nch : "";
      sec.chip.classList.toggle("has-changes", nch > 0);
    });
    noHits.hidden = visible > 0;
    filterNote.hidden = !onlyNames;
    if (onlyNames) filterNote.firstChild.textContent = count("gOnlyNew", onlyNames.length) + " ";
    shownEl.textContent = visible === settings.length ? t("gSettings", settings.length) : t("gShownOf", visible, settings.length);
    var n = settings.filter(changed).length;
    countEl.textContent = n ? t("gChangedCount", n) : "";
    dockStatus.className = "mzt-state" + (n ? " is-ok" : "");
    dockStatus.textContent = n ? t("gChangedCount", n) : t("gSettings", settings.length);
    tabFull.setAttribute("aria-selected", view === "full" ? "true" : "false");
    tabFull.classList.toggle("is-active", view === "full");
    tabChanged.classList.toggle("is-active", view === "changed");
    tabFull.setAttribute("aria-selected", view === "full");
    tabChanged.setAttribute("aria-selected", view === "changed");
    renderOut();
  }

  search.addEventListener("input", function () { refresh(); trackSection(); });
  function showLinked() {
    var name;
    try { name = decodeURIComponent(location.hash.slice(1)); } catch (e) { return; }
    if (!byName[name]) return;
    onlyNames = null; search.value = ""; onlyChanged.checked = false; refresh();
    var row = document.getElementById(name);
    row.classList.remove("is-target"); void row.offsetWidth; row.classList.add("is-target");
    row.scrollIntoView({ block: "center" });
  }
  window.addEventListener("hashchange", showLinked);
  setTimeout(showLinked, 0);
  onlyChanged.addEventListener("change", refresh);
  refresh();
}

// ------------------------------------------------------------------ payload browser

function initPayloads() {
  var root = document.getElementById("mz-pl");
  var data = document.getElementById("mz-pl-data");
  if (!data || !mount(root)) return;
  var items = JSON.parse(data.textContent);
  var filter = "all";
  var search = el("input", { type: "search", placeholder: t("pSearch"), autocomplete: "off", "aria-label": t("pSearch") });
  var shown = el("span", { class: "mzt-hint" });
  var tabs = el("div", { class: "mzt-tabs" });
  var list = el("div");
  var noHits = el("p", { class: "mzt-hint mzt-nohits", text: t("pNoHits"), hidden: true });
  var groups = [["all", t("pAll")], ["both", t("pBoth")], ["server", t("pServer")], ["get5", t("pGet5")], ["legacy", t("pLegacy")]];

  groups.forEach(function (g) {
    var n = g[0] === "all" ? items.length : items.filter(function (e) { return e.format === g[0]; }).length;
    if (!n) return;
    var b = el("button", { type: "button", class: "mzt-tab" + (g[0] === "all" ? " is-active" : "") }, g[1] + " ", el("span", { class: "mzt-tab__n", text: String(n) }));
    b.addEventListener("click", function () {
      filter = g[0];
      tabs.querySelectorAll(".mzt-tab").forEach(function (x) { x.classList.toggle("is-active", x === b); });
      render();
    });
    tabs.appendChild(b);
  });

  items.forEach(function (e) {
    var json = JSON.stringify(e.example, null, 2);
    var copyBtn = el("button", { type: "button", class: "mzt-btn", text: t("copy"), onclick: function () { copyText(json, copyBtn); } });
    var tbody = el("tbody");
    e.fields.forEach(function (f) {
      var type = f.type.replace(/^Get5|^MatchZy/, "").replace(/\?$/, "");
      tbody.appendChild(el("tr", null,
        el("td", null, el("code", { text: f.name })),
        el("td", { class: "mzt-type", text: type + (/\?$/.test(f.type) ? " (" + t("pNullable") + ")" : "") }),
        el("td", { text: f.description })));
    });
    var card = el("details", { class: "mzt-card", id: "pl-" + e.name + "-" + e.format },
      el("summary", null,
        el("code", { class: "mzt-card__name", text: e.name }),
        el("span", { class: "mzt-badge mzt-badge--" + e.format, text: t("pBadge" + e.format) }),
        el("span", { class: "mzt-card__n", text: t("pFields", e.fields.length) })),
      el("div", { class: "mzt-card__body" },
        el("div", { class: "mzt-table" }, el("table", null,
          el("thead", null, el("tr", null, el("th", { text: t("pField") }), el("th", { text: t("pType") }), el("th", { text: t("pNote") }))),
          tbody)),
        el("div", { class: "mzt-result__head" }, el("strong", { text: t("pExample") }), copyBtn),
        el("pre", { class: "mzt-out mzt-out--json" }, el("code", null, jsonNodes(json)))));
    card._e = e;
    card._hay = (e.name + " " + e.class + " " + e.fields.map(function (f) { return f.name + " " + f.description; }).join(" ")).toLowerCase();
    list.appendChild(card);
  });

  root.appendChild(el("div", { class: "mzt-toolbar" }, search, shown));
  root.appendChild(tabs);
  root.appendChild(list);
  root.appendChild(noHits);

  function render() {
    var words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
    var count = 0;
    list.querySelectorAll(".mzt-card").forEach(function (card) {
      var hit = (filter === "all" || card._e.format === filter) && words.every(function (w) { return card._hay.indexOf(w) !== -1; });
      card.hidden = !hit;
      if (hit) count++;
    });
    shown.textContent = t("pShown", count);
    noHits.hidden = count > 0;
  }
  search.addEventListener("input", render);
  // A link to #pl-<name>-<format> (or the old #ev-...) opens that event.
  var hash = "";
  try { hash = decodeURIComponent(location.hash.slice(1)).replace(/^ev-/, "pl-"); } catch (e) { /* malformed hash: ignore */ }
  var target = hash && document.getElementById(hash);
  if (target && target.classList.contains("mzt-card")) { target.open = true; target.scrollIntoView(); }
  render();
}

// ------------------------------------------------------------------ start

export function initTools() {
  T = I18N[(document.documentElement.lang || "en").slice(0, 2)] || I18N.en;
  initBuilder();
  initGenerator();
  initPayloads();
}
