// MatchZy docs tools: the match config builder (reference/match-builder), the settings generator
// (reference/config-generator) and the payload browser (reference/event-explorer).
//
// One file with a neutral name and neutral element ids: ad and tracker blockers (uBlock Origin,
// AdGuard, EasyPrivacy) block scripts and hide elements whose names look like analytics, and
// "event-explorer.js" / #mz-events were hit, which left the search field dead.
//
// Every tool builds its own UI from the I18N table below, so the English and Danish pages cannot
// drift apart; the pages only hold an empty root element and a fallback note for when this script
// is blocked. Runs on every page view (Material's instant navigation: document$).
(function () {
  "use strict";

  // ------------------------------------------------------------------ text

  var I18N = {
    en: {
      copy: "Copy", copied: "Copied!", download: "Download", reset: "Reset", resetAll: "Reset all",
      importBtn: "Import", importLoad: "Load", importCancel: "Cancel", chooseFile: "or choose a file",
      // builder
      bMatch: "Match", bMatchId: "Match id", bMatchIdHint: "0 = a new id from the database",
      bGameMode: "Game mode", bSeries: "Series", bPpt: "Players per team", bPptAuto: "from rosters",
      bMinReady: "Min. players to ready", bMinReadyAuto: "everyone", bMinSpec: "Min. spectators to ready",
      bFf: "Friendly fire", bFfMode: "Mode cfg", bFfOn: "On", bFfOff: "Off", bFfGren: "Grenades only",
      bClinch: "End the series once a team has won the majority of maps",
      bMaps: "Maps", bMapsHint: "Click maps in the order you want them; the number shows the order.",
      bMoreMaps: "More maps (one per line: workshop id, workshop/<id>, ws:<name>)",
      bVeto: "Veto and sides", bSkipVeto: "Skip the veto: play the maps in the order above",
      bVetoFirst: "Starts the veto", bVetoT1: "Team 1 (default)", bVetoT2: "Team 2", bRandom: "Random",
      bSides: "Sides", bSideStd: "Knife (standard)", bSideAlways: "Always knife", bSideNever: "Never knife (team1 starts CT)",
      bMapSides: "Side per map", bMapN: "Map {0}", bKnife: "Knife",
      bTeam: "Team {0}", bName: "Name", bTag: "Tag", bYourId: "Your id", bPlayers: "Players",
      bRoster: "Roster", bAny: "Anyone (open team)", bBots: "Bots",
      bRosterLabel: "One per line: SteamID64 and name", bBotDiff: "Bot difficulty", bCoaches: "Coaches (optional)",
      bExtras: "Spectators and cvars", bSpecs: "Spectators (optional, one per line: SteamID64 and name)",
      bCvars: "Cvars (optional, one per line: name value)",
      bResult: "Match config", bLoadWith: "Load it from the server console or RCON:",
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
      wPptBig: "players_per_team {0} is more than the {1} default ({2}).",
      // generator
      gSearch: "Search settings (name or text)...", gOnlyChanged: "Only changed", gSettings: "{0} settings",
      gYour: "Your settings", gFull: "Full config.cfg", gChanged: "Only changed", gChangedCount: "{0} changed",
      gNoneChanged: "Nothing changed yet: every setting is at its default.",
      gHeaderFull: "MatchZy config.cfg (defaults, with your changes)", gHeaderChanged: "MatchZy settings changed from the defaults",
      gDefault: "Default", gUnset: "not set", gResetOne: "Back to the default", gNoHits: "No setting matches the search.",
      gImportHint: "Paste your config.cfg (or any lines of it) to load your current values.",
      gImported: "{0} setting(s) loaded.", gUnknown: " Unknown: {0}", gChangedMark: "changed", gOn: "on", gOff: "off",
      // payload browser
      pSearch: "Search events or fields (e.g. steamid, pause, bomb)...", pShown: "{0} events", pAll: "All",
      pBoth: "Match flow", pServer: "Server and tickets", pGet5: "Live (Get5 format)", pLegacy: "Live (legacy format)",
      pBadgeboth: "match", pBadgeserver: "server", pBadgeget5: "Get5", pBadgelegacy: "legacy",
      pFields: "{0} fields", pField: "Field", pType: "Type", pNote: "Note", pNullable: "may be null", pExample: "Example",
      pNoHits: "No event matches the search."
    },
    da: {
      copy: "Kopiér", copied: "Kopieret!", download: "Download", reset: "Nulstil", resetAll: "Nulstil alt",
      importBtn: "Importér", importLoad: "Indlæs", importCancel: "Annullér", chooseFile: "eller vælg en fil",
      bMatch: "Kamp", bMatchId: "Kamp-id", bMatchIdHint: "0 = et nyt id fra databasen",
      bGameMode: "Game mode", bSeries: "Serie", bPpt: "Spillere pr. hold", bPptAuto: "fra rosters",
      bMinReady: "Min. spillere til ready", bMinReadyAuto: "alle", bMinSpec: "Min. spectators til ready",
      bFf: "Friendly fire", bFfMode: "Mode-cfg", bFfOn: "Til", bFfOff: "Fra", bFfGren: "Kun granater",
      bClinch: "Afslut serien, når et hold har vundet flertallet af banerne",
      bMaps: "Baner", bMapsHint: "Klik banerne i den rækkefølge, du vil have dem; tallet viser rækkefølgen.",
      bMoreMaps: "Flere baner (én pr. linje: workshop-id, workshop/<id>, ws:<navn>)",
      bVeto: "Veto og sider", bSkipVeto: "Spring vetoen over: spil banerne i rækkefølgen ovenfor",
      bVetoFirst: "Starter vetoen", bVetoT1: "Hold 1 (standard)", bVetoT2: "Hold 2", bRandom: "Tilfældig",
      bSides: "Sider", bSideStd: "Kniv (standard)", bSideAlways: "Altid kniv", bSideNever: "Aldrig kniv (team1 starter CT)",
      bMapSides: "Side pr. bane", bMapN: "Bane {0}", bKnife: "Kniv",
      bTeam: "Hold {0}", bName: "Navn", bTag: "Tag", bYourId: "Jeres id", bPlayers: "Spillere",
      bRoster: "Roster", bAny: "Alle (åbent hold)", bBots: "Bots",
      bRosterLabel: "Én pr. linje: SteamID64 og navn", bBotDiff: "Bot-sværhedsgrad", bCoaches: "Coaches (valgfrit)",
      bExtras: "Spectators og cvars", bSpecs: "Spectators (valgfrit, én pr. linje: SteamID64 og navn)",
      bCvars: "Cvars (valgfrit, én pr. linje: navn værdi)",
      bResult: "Match config", bLoadWith: "Load den fra serverkonsollen eller RCON:",
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
      wPptBig: "players_per_team {0} er mere end standarden for {1} ({2}).",
      gSearch: "Søg i indstillinger (navn eller tekst)...", gOnlyChanged: "Kun ændrede", gSettings: "{0} indstillinger",
      gYour: "Dine indstillinger", gFull: "Hele config.cfg", gChanged: "Kun ændrede", gChangedCount: "{0} ændret",
      gNoneChanged: "Intet ændret endnu: alle indstillinger står på standard.",
      gHeaderFull: "MatchZy config.cfg (standarder, med dine ændringer)", gHeaderChanged: "MatchZy-indstillinger ændret fra standard",
      gDefault: "Standard", gUnset: "ikke sat", gResetOne: "Tilbage til standard", gNoHits: "Ingen indstilling passer til søgningen.",
      gImportHint: "Indsæt din config.cfg (eller nogle linjer fra den) for at indlæse dine nuværende værdier.",
      gImported: "{0} indstilling(er) indlæst.", gUnknown: " Ukendte: {0}", gChangedMark: "ændret", gOn: "til", gOff: "fra",
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
    var file = el("input", { type: "file", accept: accept });
    var box = el("div", { class: "mzt-import", hidden: true },
      el("p", { class: "mzt-hint", text: hint }), ta,
      el("div", { class: "mzt-row" },
        el("button", { type: "button", class: "md-button md-button--primary", text: t("importLoad"), onclick: function () {
          status.hidden = false;
          var msg = onLoad(ta.value);
          status.textContent = msg.text;
          status.className = "mzt-status" + (msg.ok ? " is-ok" : " is-bad");
          if (msg.ok) ta.value = "";
        } }),
        el("button", { type: "button", class: "md-button", text: t("importCancel"), onclick: function () { box.hidden = true; status.hidden = true; } }),
        el("label", { class: "mzt-file" }, t("chooseFile"), file)),
      status);
    file.addEventListener("change", function () {
      var f = file.files && file.files[0];
      if (!f) return;
      f.text().then(function (txt) { ta.value = txt; file.value = ""; });
    });
    return box;
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
  function roster(text) {
    return lines(text).map(function (l) {
      var m = l.match(/^(\d+)[\s,;:=]*(.*)$/);
      return m ? [m[1], m[2].trim()] : [l, ""];
    });
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

  function defaultBuilderState() {
    return {
      matchid: "0", mode: "competitive", series: "3", ppt: "", minready: "", minspec: "", ff: "", clinch: true,
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
    var S = Object.assign(defaultBuilderState(), store.get(BUILDER_KEY) || {});
    var ui = {};

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
        field(t("bMinReady"), bind(el("input", { type: "number", min: 1, max: 10, placeholder: t("bMinReadyAuto") }), "minready")),
        field(t("bMinSpec"), bind(el("input", { type: "number", min: 0, max: 10, placeholder: "0" }), "minspec")),
        field(t("bFf"), bind(select([["", t("bFfMode")], ["on", t("bFfOn")], ["off", t("bFfOff")], ["grenades", t("bFfGren")]]), "ff"))),
      el("label", { class: "mzt-check" }, bind(el("input", { type: "checkbox" }), "clinch"), t("bClinch")));

    ui.mapGrid = el("div", { class: "mzt-maps" });
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
      field(t("bMoreMaps"), bind(el("textarea", { rows: 2, spellcheck: "false", placeholder: "workshop/3070284539" }), "extramaps")));

    ui.mapSides = el("div", { class: "mzt-grid" });
    ui.mapSidesWrap = el("div", { class: "mzt-sub" }, el("span", { class: "mzt-label", text: t("bMapSides") }), ui.mapSides);
    ui.vetoFirst = select([["", t("bVetoT1")], ["team2", t("bVetoT2")], ["random", t("bRandom")]]);
    var vetoBox = el("fieldset", { class: "mzt-box" }, el("legend", { text: t("bVeto") }),
      el("label", { class: "mzt-check" }, bind(el("input", { type: "checkbox" }), "skipveto"), t("bSkipVeto")),
      el("div", { class: "mzt-grid" },
        field(t("bVetoFirst"), bind(ui.vetoFirst, "vetofirst")),
        field(t("bSides"), bind(select([["standard", t("bSideStd")], ["always_knife", t("bSideAlways")], ["never_knife", t("bSideNever")], ["random", t("bRandom")]]), "sidetype"))),
      ui.mapSidesWrap);

    ui.teamRows = [];
    var teamsWrap = el("div", { class: "mzt-teams" });
    S.teams.forEach(function (team, i) {
      var playersRow = field(t("bRosterLabel"), bind(el("textarea", { rows: 5, spellcheck: "false" }), "players", team));
      var botsRow = field(t("bBotDiff"), bind(select([["0", "0"], ["1", "1"], ["2", "2"], ["3", "3"]]), "difficulty", team));
      ui.teamRows.push({ players: playersRow, bots: botsRow, team: team });
      teamsWrap.appendChild(el("fieldset", { class: "mzt-box" }, el("legend", { text: t("bTeam", i + 1) }),
        el("div", { class: "mzt-grid mzt-grid--3" },
          field(t("bName"), bind(el("input", { type: "text" }), "name", team)),
          field(t("bTag"), bind(el("input", { type: "text", maxlength: 12 }), "tag", team)),
          field(t("bYourId"), bind(el("input", { type: "text" }), "id", team))),
        field(t("bPlayers"), bind(select([["list", t("bRoster")], ["any", t("bAny")], ["bots", t("bBots")]]), "mode", team)),
        playersRow, botsRow,
        field(t("bCoaches"), bind(el("textarea", { rows: 2, spellcheck: "false", placeholder: "76561198000000009 coach" }), "coaches", team))));
    });

    var extrasBox = el("fieldset", { class: "mzt-box" }, el("legend", { text: t("bExtras") }),
      field(t("bSpecs"), bind(el("textarea", { rows: 2, spellcheck: "false" }), "specs")),
      field(t("bCvars"), bind(el("textarea", { rows: 3, spellcheck: "false", placeholder: "mp_overtime_enable 1" }), "cvars")));

    // --- result
    ui.out = el("code");
    ui.summary = el("p", { class: "mzt-summary" });
    ui.problems = el("ul", { class: "mzt-problems", hidden: true });
    ui.loadCmd = el("code");
    var importPanel = importBox(t("bImportHint"), ".json,application/json", importConfig);
    var copyBtn = el("button", { type: "button", class: "md-button md-button--primary", text: t("copy"), onclick: function () { copyText(ui.out.textContent, copyBtn); } });
    var result = el("div", { class: "mzt-result" },
      el("div", { class: "mzt-result__head" }, el("strong", { text: t("bResult") }),
        el("span", { class: "mzt-row" }, copyBtn,
          el("button", { type: "button", class: "md-button", text: t("download"), onclick: function () { downloadText("match.json", ui.out.textContent + "\n", "application/json"); } }),
          el("button", { type: "button", class: "md-button", text: t("importBtn"), onclick: function () { importPanel.hidden = !importPanel.hidden; } }),
          el("button", { type: "button", class: "md-button", text: t("reset"), onclick: function () {
            store.del(BUILDER_KEY);
            root.dataset.ready = "";
            initBuilder();
          } }))),
      importPanel, ui.summary, ui.problems,
      el("pre", { class: "mzt-out" }, ui.out),
      el("p", { class: "mzt-hint" }, t("bLoadWith"), " ", ui.loadCmd));

    root.appendChild(el("div", { class: "mzt-form" }, matchBox, mapsBox, vetoBox, teamsWrap, extrasBox));
    root.appendChild(result);

    // --- import: fill the form from an existing config
    function importConfig(text) {
      var c;
      try { c = JSON.parse(text); } catch (e) { return { ok: false, text: t("bImportBad") + e.message }; }
      if (!c || typeof c !== "object" || !c.team1 || !c.team2) return { ok: false, text: t("bImportBad") + "team1 / team2" };
      var known = ["matchid", "num_maps", "maplist", "game_mode", "wingman", "skip_veto", "veto_first", "side_type", "map_sides",
        "players_per_team", "min_players_to_ready", "min_spectators_to_ready", "clinch_series", "friendly_fire", "team1", "team2", "spectators", "cvars"];
      var next = defaultBuilderState();
      next.matchid = String(c.matchid || 0);
      next.mode = c.game_mode && MODES[c.game_mode] ? c.game_mode : c.wingman ? "wingman" : "competitive";
      next.series = String(c.num_maps || 1);
      next.ppt = c.players_per_team != null ? String(c.players_per_team) : "";
      next.minready = c.min_players_to_ready != null ? String(c.min_players_to_ready) : "";
      next.minspec = c.min_spectators_to_ready != null ? String(c.min_spectators_to_ready) : "";
      next.ff = c.friendly_fire === true ? "on" : c.friendly_fire === false ? "off" : c.friendly_fire === "grenades" ? "grenades" : "";
      next.clinch = c.clinch_series !== false;
      var maps = Array.isArray(c.maplist) ? c.maplist.map(String) : [];
      next.maps = maps.filter(function (m) { return MAP_POOL.indexOf(m) >= 0; });
      next.extramaps = maps.filter(function (m) { return MAP_POOL.indexOf(m) < 0; }).join("\n");
      next.skipveto = !!c.skip_veto;
      next.vetofirst = c.veto_first === "team2" || c.veto_first === "random" ? c.veto_first : "";
      next.sidetype = c.side_type || "standard";
      next.mapsides = Array.isArray(c.map_sides) ? c.map_sides.map(String) : [];
      ["team1", "team2"].forEach(function (k, i) {
        var tm = c[k], d = next.teams[i];
        d.name = tm.name || ""; d.tag = tm.tag || ""; d.id = tm.id != null ? String(tm.id) : "";
        d.mode = tm.bots ? "bots" : tm.players === "any" ? "any" : "list";
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
      var errors = [], warnings = [];
      var mode = S.mode, numMaps = parseInt(S.series, 10) || 1, modeDefault = MODES[mode];
      var maps = S.maps.concat(lines(S.extramaps));
      var c = {};
      var matchid = parseInt(S.matchid, 10);
      c.matchid = isNaN(matchid) ? 0 : matchid;
      c.num_maps = numMaps;
      c.maplist = maps;
      if (mode !== "competitive") c.game_mode = mode;

      maps.forEach(function (m, i) { if (maps.indexOf(m) !== i && errors.indexOf(t("eDupMap", m)) < 0) errors.push(t("eDupMap", m)); });
      if (maps.length < numMaps) errors.push(t("eTooFewMaps", numMaps));

      var rosters = [], teamsOut = [];
      S.teams.forEach(function (tm, i) {
        var o = { name: tm.name.trim() };
        if (!o.name) errors.push(t("eNoName", t("bTeam", i + 1)));
        if (tm.tag.trim()) o.tag = tm.tag.trim();
        if (tm.id.trim()) o.id = tm.id.trim();
        var ids = [];
        if (tm.mode === "any") o.players = "any";
        else if (tm.mode === "bots") { o.bots = true; o.bot_difficulty = parseInt(tm.difficulty, 10); }
        else {
          var p = roster(tm.players);
          p.forEach(function (e) { if (!STEAMID.test(e[0])) errors.push(t("eSteamId", e[0])); });
          if (!p.length) errors.push(t("eNoPlayers", o.name || t("bTeam", i + 1)));
          o.players = rosterObject(p);
          ids = p.map(function (e) { return e[0]; });
        }
        var co = roster(tm.coaches);
        co.forEach(function (e) { if (!STEAMID.test(e[0])) errors.push(t("eSteamId", e[0])); });
        if (co.length) o.coaches = rosterObject(co);
        var coachIds = co.map(function (e) { return e[0]; });
        rosters.push(ids.filter(function (id) { return coachIds.indexOf(id) < 0; }));
        teamsOut.push(o);
      });
      var anyBots = S.teams.some(function (tm) { return tm.mode === "bots"; });
      if (S.teams[0].mode === "any" && S.teams[1].mode === "any") errors.push(t("eTwoAny"));
      if (S.teams[0].mode === "bots" && S.teams[1].mode === "bots") errors.push(t("eTwoBots"));

      var skip = S.skipveto || maps.length === numMaps;
      if (anyBots && maps.length > numMaps && !S.skipveto) errors.push(t("eBotsVeto"));
      if (skip) c.skip_veto = true;
      if (!skip && S.vetofirst) c.veto_first = S.vetofirst;
      if (S.sidetype !== "standard") c.side_type = S.sidetype;
      if (skip) {
        var sides = [];
        for (var i = 0; i < numMaps; i++) sides.push(SIDE_VALUES.indexOf(S.mapsides[i]) >= 0 ? S.mapsides[i] : "knife");
        if (sides.some(function (s) { return s !== "knife"; })) c.map_sides = sides;
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
      specs.forEach(function (e) { if (!STEAMID.test(e[0])) errors.push(t("eSteamId", e[0])); });
      if (specs.length) c.spectators = { players: rosterObject(specs) };

      var cvars = {};
      lines(S.cvars).forEach(function (l) {
        var m = l.match(/^([A-Za-z0-9_.]+)\s+(.+)$/);
        if (!m) { errors.push(t("eCvar", l)); return; }
        var value = m[2].replace(/^"(.*)"$/, "$1");
        if (/[;"]/.test(value)) { errors.push(t("eCvar", l)); return; }
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
      if (!isNaN(minReady) && minReady >= 1 && minReady < Math.max(size[0], size[1])) warnings.push(t("wMinReady", minReady));
      if (pptSet && ppt > modeDefault) warnings.push(t("wPptBig", ppt, MODE_NAMES[mode], modeDefault));

      return { config: c, errors: errors, warnings: warnings, ppt: ppt, pptSet: pptSet, skip: skip, numMaps: numMaps, maps: maps };
    }

    function update() {
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
      if (b.skip && ui.mapSides.childElementCount !== b.numMaps) {
        ui.mapSides.textContent = "";
        for (var i = 0; i < b.numMaps; i++) {
          (function (n) {
            var s = select(SIDE_VALUES.map(function (v) { return [v, v === "knife" ? t("bKnife") : v]; }), S.mapsides[n] || "knife");
            s.addEventListener("change", function () { S.mapsides[n] = s.value; update(); });
            ui.mapSides.appendChild(field(t("bMapN", n + 1) + (b.maps[n] ? ": " + b.maps[n] : ""), s));
          })(i);
        }
      } else if (b.skip) {
        ui.mapSides.querySelectorAll(".mzt-label").forEach(function (lab, n) { lab.textContent = t("bMapN", n + 1) + (b.maps[n] ? ": " + b.maps[n] : ""); });
      }
      ui.out.textContent = JSON.stringify(b.config, null, 2);
      ui.summary.textContent = t("bSummary", "BO" + b.numMaps, b.maps.length, b.skip ? t("bSummaryNoVeto") : t("bSummaryVeto"),
        b.ppt + "v" + b.ppt, b.pptSet ? "" : t("bFromRosters"));
      ui.problems.textContent = "";
      b.errors.forEach(function (e) { ui.problems.appendChild(el("li", { class: "is-error", text: e })); });
      b.warnings.forEach(function (w) { ui.problems.appendChild(el("li", { class: "is-warn", text: w })); });
      ui.problems.hidden = !b.errors.length && !b.warnings.length;
      ui.loadCmd.textContent = "matchzy_loadmatch cfg/MatchZy/match.json";
    }

    update();
  }

  // ------------------------------------------------------------------ settings generator

  var CFG_KEY = "mz-cfggen-v2";

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
        var row = el("div", { class: "mzt-setting" },
          el("div", { class: "mzt-setting__head" },
            el("code", { class: "mzt-setting__name", text: s.name }),
            el("div", { class: "mzt-setting__ctl" }, control, resetBtn)),
          picks,
          el("p", { class: "mzt-setting__desc" }, s.description, " ",
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
    var tabFull = el("button", { type: "button", class: "mzt-tab", text: t("gFull"), onclick: function () { view = "full"; refresh(); } });
    var tabChanged = el("button", { type: "button", class: "mzt-tab", text: t("gChanged"), onclick: function () { view = "changed"; refresh(); } });
    var importPanel = importBox(t("gImportHint"), ".cfg,.txt,text/plain", function (text) {
      var n = 0, unknown = [];
      lines(text).forEach(function (l) {
        if (l.indexOf("//") === 0) return;
        var m = l.match(/^([A-Za-z0-9_]+)\s+(.*?)\s*(?:\/\/.*)?$/);
        if (!m) return;
        var s = byName[m[1]];
        if (!s) { if (/^(matchzy|get5)_/.test(m[1])) unknown.push(m[1]); return; }
        var v = m[2].replace(/^"(.*)"$/, "$1");
        if (s.type === "bool") v = /^(1|true)$/i.test(v) ? "true" : "false";
        if (v === s.default) delete values[s.name]; else values[s.name] = v;
        n++;
      });
      refresh();
      return { ok: n > 0, text: t("gImported", n) + (unknown.length ? t("gUnknown", unknown.slice(0, 8).join(", ")) : "") };
    });
    var copyBtn = el("button", { type: "button", class: "md-button md-button--primary", text: t("copy"), onclick: function () { copyText(out.textContent, copyBtn); } });
    var result = el("div", { class: "mzt-result" },
      el("div", { class: "mzt-result__head" }, el("strong", { text: t("gYour") }), countEl),
      el("div", { class: "mzt-tabs", role: "tablist" }, tabFull, tabChanged),
      el("div", { class: "mzt-row" }, copyBtn,
        el("button", { type: "button", class: "md-button", text: t("download"), onclick: function () {
          downloadText(view === "full" ? "config.cfg" : "matchzy-settings.cfg", out.textContent + "\n", "text/plain");
        } }),
        el("button", { type: "button", class: "md-button", text: t("importBtn"), onclick: function () { importPanel.hidden = !importPanel.hidden; } }),
        el("button", { type: "button", class: "md-button", text: t("resetAll"), onclick: function () { values = {}; refresh(); } })),
      importPanel,
      el("pre", { class: "mzt-out mzt-out--cfg" }, out));

    root.appendChild(el("div", { class: "mzt-cfgmain" },
      el("div", { class: "mzt-toolbar" }, search,
        el("label", { class: "mzt-check" }, onlyChanged, t("gOnlyChanged")),
        el("span", { class: "mzt-hint", text: t("gSettings", settings.length) })),
      chips, list, noHits));
    root.appendChild(result);

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
          else out.appendChild(el("span", { text: cfgLine(s, v) + "\n" }));
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
        var hit = words.every(function (w) { return r.hay.indexOf(w) !== -1; }) && (!onlyChanged.checked || ch);
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
      var n = settings.filter(changed).length;
      countEl.textContent = n ? t("gChangedCount", n) : "";
      tabFull.classList.toggle("is-active", view === "full");
      tabChanged.classList.toggle("is-active", view === "changed");
      tabFull.setAttribute("aria-selected", view === "full");
      tabChanged.setAttribute("aria-selected", view === "changed");
      renderOut();
    }

    search.addEventListener("input", refresh);
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
      var copyBtn = el("button", { type: "button", class: "md-button", text: t("copy"), onclick: function () { copyText(json, copyBtn); } });
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
          el("pre", { class: "mzt-out" }, el("code", { text: json }))));
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
    var hash = decodeURIComponent(location.hash.slice(1)).replace(/^ev-/, "pl-");
    var target = hash && document.getElementById(hash);
    if (target && target.classList.contains("mzt-card")) { target.open = true; target.scrollIntoView(); }
    render();
  }

  // ------------------------------------------------------------------ start

  function init() {
    T = I18N[(document.documentElement.lang || "en").slice(0, 2)] || I18N.en;
    initBuilder();
    initGenerator();
    initPayloads();
  }

  if (window.document$ && typeof window.document$.subscribe === "function") window.document$.subscribe(init);
  else if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})();
