// Event explorer (reference/event-explorer): every event MatchZy sends, read from Core/Events.cs and
// Core/Get5Events.cs at build time (hooks/events_catalog.py), with its fields and an example payload.
// Text comes from data-msg-* on #mz-events, so English and Danish share this script.
(function () {
  "use strict";

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function init() {
    var root = document.getElementById("mz-events");
    var data = document.getElementById("mz-events-data");
    if (!root || !data || root.dataset.ready) return;
    root.dataset.ready = "1";
    var msg = function (k) { return root.getAttribute("data-msg-" + k) || k; };
    var events = JSON.parse(data.textContent);
    var list = root.querySelector("#ev-list"), search = root.querySelector("#ev-search");
    var tabs = root.querySelector("#ev-tabs"), shown = root.querySelector("#ev-shown");
    var filter = "all";
    var groups = [["all", msg("all")], ["both", msg("both")], ["server", msg("server")], ["get5", msg("get5")], ["legacy", msg("legacy")]];

    groups.forEach(function (g) {
      var n = g[0] === "all" ? events.length : events.filter(function (e) { return e.format === g[0]; }).length;
      var b = el("button", "ev-tab" + (g[0] === "all" ? " is-active" : ""), g[1] + " ");
      b.type = "button";
      b.appendChild(el("span", "ev-tab__n", String(n)));
      b.addEventListener("click", function () {
        filter = g[0];
        tabs.querySelectorAll(".ev-tab").forEach(function (t) { t.classList.toggle("is-active", t === b); });
        render();
      });
      tabs.appendChild(b);
    });

    events.forEach(function (e) {
      var card = el("details", "ev-card");
      card._event = e;
      card.id = "ev-" + e.name + "-" + e.format;
      var sum = el("summary");
      sum.appendChild(el("code", "ev-name", e.name));
      sum.appendChild(el("span", "ev-badge ev-badge--" + e.format, msg("badge-" + e.format)));
      sum.appendChild(el("span", "ev-count", msg("fields").replace("{0}", e.fields.length)));
      card.appendChild(sum);
      var body = el("div", "ev-body");
      var table = el("table");
      var head = el("tr");
      [msg("field"), msg("type"), msg("note")].forEach(function (h) { head.appendChild(el("th", null, h)); });
      var thead = el("thead"); thead.appendChild(head); table.appendChild(thead);
      var tbody = el("tbody");
      e.fields.forEach(function (f) {
        var tr = el("tr");
        var td = el("td"); td.appendChild(el("code", null, f.name)); tr.appendChild(td);
        var type = f.type.replace(/^Get5|^MatchZy/, "").replace(/\?$/, "");
        tr.appendChild(el("td", "ev-type", type + (f.type.endsWith("?") ? " (" + msg("nullable") + ")" : "")));
        tr.appendChild(el("td", null, f.description));
        tbody.appendChild(tr);
      });
      table.appendChild(tbody);
      var wrap = el("div", "ev-table"); wrap.appendChild(table);
      body.appendChild(wrap);
      var exHead = el("div", "ev-exhead");
      exHead.appendChild(el("strong", null, msg("example")));
      var copy = el("button", "md-button", msg("copy"));
      copy.type = "button";
      var json = JSON.stringify(e.example, null, 2);
      copy.addEventListener("click", function () {
        navigator.clipboard.writeText(json).then(function () {
          copy.textContent = msg("copied");
          setTimeout(function () { copy.textContent = msg("copy"); }, 1500);
        });
      });
      exHead.appendChild(copy);
      body.appendChild(exHead);
      var pre = el("pre", "ev-example"); pre.appendChild(el("code", null, json));
      body.appendChild(pre);
      card.appendChild(body);
      list.appendChild(card);
    });

    function render() {
      var words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
      var count = 0;
      list.querySelectorAll(".ev-card").forEach(function (card) {
        var e = card._event;
        var hay = (e.name + " " + e.class + " " + e.fields.map(function (f) { return f.name + " " + f.description; }).join(" ")).toLowerCase();
        var hit = (filter === "all" || e.format === filter) && words.every(function (w) { return hay.indexOf(w) !== -1; });
        card.hidden = !hit;
        if (hit) count++;
      });
      shown.textContent = msg("shown").replace("{0}", count);
    }

    search.addEventListener("input", render);
    // A link to #ev-<name>-<format> opens that event.
    var target = location.hash && document.getElementById(decodeURIComponent(location.hash.slice(1)));
    if (target && target.classList.contains("ev-card")) { target.open = true; target.scrollIntoView(); }
    render();
  }

  if (window.document$ && typeof window.document$.subscribe === "function") {
    window.document$.subscribe(init);
  } else {
    document.addEventListener("DOMContentLoaded", init);
  }
})();
