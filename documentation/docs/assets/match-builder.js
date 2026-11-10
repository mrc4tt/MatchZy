// Match config builder (reference/match-builder): a form that writes a MatchZy / Get5 match config.
// All visible text comes from the page (data-msg-* on #mz-builder), so the English and Danish pages
// share this script. Runs again on every page view (Material's instant navigation: document$).
(function () {
  "use strict";

  var DEFAULT_PLAYERS = { competitive: 5, wingman: 2, rush: 3, "1v1": 1 };
  var STEAMID = /^7656119\d{10}$/;

  function lines(text) {
    return text.split(/\r?\n/).map(function (l) { return l.trim(); }).filter(Boolean);
  }

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

  function init() {
    var root = document.getElementById("mz-builder");
    if (!root || root.dataset.ready) return;
    root.dataset.ready = "1";
    var msg = function (k) { return root.getAttribute("data-msg-" + k) || k; };
    var $ = function (id) { return root.querySelector("#" + id); };
    var out = $("mzb-out"), problems = $("mzb-problems");

    function teamFrom(prefix) {
      var mode = $(prefix + "-mode").value;
      var team = { name: $(prefix + "-name").value.trim() || (prefix === "t1" ? "Team 1" : "Team 2") };
      var tag = $(prefix + "-tag").value.trim();
      var id = $(prefix + "-id").value.trim();
      if (tag) team.tag = tag;
      if (id) team.id = id;
      var errors = [];
      if (mode === "any") {
        team.players = "any";
      } else if (mode === "bots") {
        team.bots = true;
        team.bot_difficulty = parseInt($(prefix + "-difficulty").value, 10);
      } else {
        var players = roster($(prefix + "-players").value);
        players.forEach(function (p) { if (!STEAMID.test(p[0])) errors.push(msg("badsteamid") + " " + p[0]); });
        team.players = rosterObject(players);
        if (!players.length) errors.push(team.name + ": " + msg("noplayers"));
      }
      var coaches = roster($(prefix + "-coaches").value);
      coaches.forEach(function (p) { if (!STEAMID.test(p[0])) errors.push(msg("badsteamid") + " " + p[0]); });
      if (coaches.length) team.coaches = rosterObject(coaches);
      return { team: team, mode: mode, errors: errors };
    }

    function build() {
      var errors = [];
      var gameMode = $("mzb-gamemode").value;
      var numMaps = parseInt($("mzb-series").value, 10);
      var maps = Array.prototype.map.call(root.querySelectorAll(".mzb-map:checked"), function (c) { return c.value; })
        .concat(lines($("mzb-extramaps").value));
      var config = {};
      var matchid = parseInt($("mzb-matchid").value, 10);
      config.matchid = isNaN(matchid) ? 0 : matchid;
      config.num_maps = numMaps;
      config.maplist = maps;
      if (gameMode !== "competitive") config.game_mode = gameMode;

      var t1 = teamFrom("t1"), t2 = teamFrom("t2");
      errors = errors.concat(t1.errors, t2.errors);
      var bots = t1.mode === "bots" || t2.mode === "bots";
      var skip = $("mzb-skipveto").checked || maps.length === numMaps;
      if (maps.length < numMaps) errors.push(msg("toofewmaps").replace("{0}", numMaps));
      if (t1.mode === "any" && t2.mode === "any") errors.push(msg("twoany"));
      if (bots && maps.length > numMaps && !$("mzb-skipveto").checked) errors.push(msg("botsveto"));

      if (skip) config.skip_veto = true;
      var vetoFirst = $("mzb-vetofirst").value;
      if (!skip && vetoFirst) config.veto_first = vetoFirst;
      var sideType = $("mzb-sidetype").value;
      if (sideType !== "standard") config.side_type = sideType;

      var ppt = parseInt($("mzb-ppt").value, 10);
      if (!isNaN(ppt) && ppt !== DEFAULT_PLAYERS[gameMode]) config.players_per_team = ppt;
      var minReady = parseInt($("mzb-minready").value, 10);
      if (!isNaN(minReady)) config.min_players_to_ready = minReady;
      if (!$("mzb-clinch").checked) config.clinch_series = false;
      var ff = $("mzb-ff").value;
      if (ff === "on") config.friendly_fire = true;
      else if (ff === "off") config.friendly_fire = false;
      else if (ff === "grenades") config.friendly_fire = "grenades";

      config.team1 = t1.team;
      config.team2 = t2.team;

      var specs = roster($("mzb-specs").value);
      specs.forEach(function (p) { if (!STEAMID.test(p[0])) errors.push(msg("badsteamid") + " " + p[0]); });
      if (specs.length) config.spectators = { players: rosterObject(specs) };

      var cvars = {};
      lines($("mzb-cvars").value).forEach(function (l) {
        var m = l.match(/^([A-Za-z0-9_]+)\s+(.+)$/);
        if (!m) { errors.push(msg("badcvar") + " " + l); return; }
        var value = m[2].replace(/^"(.*)"$/, "$1");
        if (/[;"]/.test(value)) { errors.push(msg("badcvar") + " " + l); return; }
        cvars[m[1]] = value;
      });
      if (Object.keys(cvars).length) config.cvars = cvars;

      out.textContent = JSON.stringify(config, null, 2);
      problems.innerHTML = "";
      problems.hidden = !errors.length;
      errors.forEach(function (e) {
        var li = document.createElement("li");
        li.textContent = e;
        problems.appendChild(li);
      });
    }

    function syncTeam(prefix) {
      var mode = $(prefix + "-mode").value;
      $(prefix + "-players-row").hidden = mode !== "list";
      $(prefix + "-bots-row").hidden = mode !== "bots";
    }

    $("mzb-gamemode").addEventListener("change", function () {
      $("mzb-ppt").placeholder = DEFAULT_PLAYERS[this.value];
    });
    ["t1", "t2"].forEach(function (p) {
      $(p + "-mode").addEventListener("change", function () { syncTeam(p); });
      syncTeam(p);
    });
    root.addEventListener("input", build);
    root.addEventListener("change", build);

    $("mzb-copy").addEventListener("click", function () {
      var btn = this;
      navigator.clipboard.writeText(out.textContent).then(function () {
        var old = btn.textContent;
        btn.textContent = msg("copied");
        setTimeout(function () { btn.textContent = old; }, 1500);
      });
    });
    $("mzb-download").addEventListener("click", function () {
      var blob = new Blob([out.textContent + "\n"], { type: "application/json" });
      var a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = "match.json";
      a.click();
      setTimeout(function () { URL.revokeObjectURL(a.href); }, 1000);
    });
    build();
  }

  if (window.document$ && typeof window.document$.subscribe === "function") {
    window.document$.subscribe(init);
  } else {
    document.addEventListener("DOMContentLoaded", init);
  }
})();
