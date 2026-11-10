// Settings generator (reference/config-generator): every setting of cfg/config.cfg (embedded by
// hooks/values.py), searchable, grouped by section; changed values become cfg lines to paste.
// Text comes from data-msg-* on #mz-cfggen, so English and Danish share this script.
(function () {
  "use strict";

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function cfgValue(s, value) {
    if (s.type === "text") return '"' + value.replace(/"/g, "") + '"';
    return value;
  }

  function init() {
    var root = document.getElementById("mz-cfggen");
    var data = document.getElementById("mz-settings-data");
    if (!root || !data || root.dataset.ready) return;
    root.dataset.ready = "1";
    var msg = function (k) { return root.getAttribute("data-msg-" + k) || k; };
    var settings = JSON.parse(data.textContent);
    var list = root.querySelector("#cfg-list"), out = root.querySelector("#cfg-out");
    var search = root.querySelector("#cfg-search"), onlyChanged = root.querySelector("#cfg-changed");
    var count = root.querySelector("#cfg-count");
    var values = {};

    var sections = [];
    settings.forEach(function (s) {
      if (!sections.length || sections[sections.length - 1].name !== s.section)
        sections.push({ name: s.section, items: [] });
      sections[sections.length - 1].items.push(s);
    });

    function changed(s) {
      return Object.prototype.hasOwnProperty.call(values, s.name) && values[s.name] !== s.default;
    }

    function render() {
      var lines = [];
      settings.forEach(function (s) { if (changed(s)) lines.push(s.name + " " + cfgValue(s, values[s.name])); });
      out.textContent = lines.length ? "// " + msg("header") + "\n" + lines.join("\n") : "// " + msg("empty");
      count.textContent = lines.length ? msg("changedcount").replace("{0}", lines.length) : "";
      var words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
      list.querySelectorAll(".cfg-item").forEach(function (row) {
        var s = row._setting;
        var hay = (s.name + " " + s.description + " " + s.section).toLowerCase();
        // Every word must appear, in any order: "tech pause" finds "technical pause".
        var hit = words.every(function (w) { return hay.indexOf(w) !== -1; });
        row.hidden = !hit || (onlyChanged.checked && !changed(s));
        row.classList.toggle("is-changed", changed(s));
      });
      list.querySelectorAll(".cfg-section").forEach(function (sec) {
        sec.hidden = !sec.querySelector(".cfg-item:not([hidden])");
      });
    }

    sections.forEach(function (section) {
      var sec = el("section", "cfg-section");
      sec.appendChild(el("h3", null, section.name));
      section.items.forEach(function (s) {
        var row = el("div", "cfg-item");
        row._setting = s;
        var head = el("div", "cfg-head");
        head.appendChild(el("code", "cfg-name", s.name));
        var input;
        if (s.type === "bool") {
          input = el("select");
          ["true", "false"].forEach(function (v) { var o = el("option", null, v); o.value = v; input.appendChild(o); });
          input.value = s.default;
        } else {
          input = el("input");
          input.type = s.type === "number" ? "number" : "text";
          if (s.type === "number") input.step = "any";
          input.value = s.default;
          if (s.example) input.placeholder = s.example;
        }
        input.setAttribute("aria-label", s.name);
        input.addEventListener("input", function () { values[s.name] = input.value; render(); });
        input.addEventListener("change", function () { values[s.name] = input.value; render(); });
        head.appendChild(input);
        var reset = el("button", "cfg-reset", "↺");
        reset.type = "button";
        reset.title = msg("reset");
        reset.addEventListener("click", function () { input.value = s.default; delete values[s.name]; render(); });
        head.appendChild(reset);
        row.appendChild(head);
        var d = el("p", "cfg-desc", s.description);
        var def = el("span", "cfg-default", " " + msg("default") + ": " + (s.default === "" ? (s.optional ? msg("unset") : '""') : s.default));
        d.appendChild(def);
        row.appendChild(d);
        sec.appendChild(row);
      });
      list.appendChild(sec);
    });

    search.addEventListener("input", render);
    onlyChanged.addEventListener("change", render);
    root.querySelector("#cfg-copy").addEventListener("click", function () {
      var btn = this;
      navigator.clipboard.writeText(out.textContent).then(function () {
        var old = btn.textContent;
        btn.textContent = msg("copied");
        setTimeout(function () { btn.textContent = old; }, 1500);
      });
    });
    root.querySelector("#cfg-download").addEventListener("click", function () {
      var blob = new Blob([out.textContent + "\n"], { type: "text/plain" });
      var a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = "matchzy-settings.cfg";
      a.click();
      setTimeout(function () { URL.revokeObjectURL(a.href); }, 1000);
    });
    root.querySelector("#cfg-resetall").addEventListener("click", function () {
      values = {};
      list.querySelectorAll(".cfg-item").forEach(function (row) {
        var input = row.querySelector("input, select");
        input.value = row._setting.default;
      });
      render();
    });
    render();
  }

  if (window.document$ && typeof window.document$.subscribe === "function") {
    window.document$.subscribe(init);
  } else {
    document.addEventListener("DOMContentLoaded", init);
  }
})();
