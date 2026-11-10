"""MkDocs hook: fills in values read from the plugin source, so the site never shows stale ones.

  @@MATCHZY_VERSION@@     ModuleVersion from Core/MatchZy.cs (the release workflow also does this)
  @@STAT_COMMANDS@@       chat commands (dot commands in Core/MatchZy.cs, aliases included)
  @@STAT_SETTINGS@@       matchzy_* convars in Config/ConfigConvars.cs
  @@STAT_EVENTS@@         event types in Core/Events.cs and Core/Get5Events.cs
  @@STAT_LANGUAGES@@      lang/*.json files

Pages with "new_in: 1.0.24" in their front matter get a "New in 1.0.24" badge under the title and the
"new" marker in the navigation, for as long as that version is at most NEW_IN_PATCHES patch releases
old. config.extra.matchzy_version is the version for the templates (the release banner).

It also replaces <!--MATCHZY_SETTINGS--> in a page's HTML with the settings of cfg/config.cfg as JSON
(section, name, description, default, type) for the settings generator (reference/config-generator).
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
import events_catalog  # noqa: E402

_values = {}
_settings_json = "[]"
_events_json = "[]"
_version = ""
_language = "en"
NEW_IN_PATCHES = 2


def _version_tuple(v):
    return tuple(int(x) for x in re.findall(r"\d+", str(v))[:3])


def _is_recent(new_in):
    """True while new_in is the current version or at most NEW_IN_PATCHES patch releases older."""
    cur, new = _version_tuple(_version), _version_tuple(new_in)
    if len(cur) < 3 or len(new) < 3 or cur[:2] != new[:2]:
        return False
    return 0 <= cur[2] - new[2] <= NEW_IN_PATCHES

_SETTING = re.compile(r'^(matchzy_\w+|get5_\w+)\s+(.*?)\s*$')
_OPTIONAL = re.compile(r'^//\s*(matchzy_\w+|get5_\w+)\s+("[^"]*"|\S+)\s*$')


def _unquote(value):
    return value[1:-1] if len(value) >= 2 and value[0] == value[-1] == '"' else value


def parse_config_cfg(text):
    """Settings of cfg/config.cfg: comment block, "Default: x" line, then the setting (or a
    commented-out example such as // matchzy_demo_upload_url "https://...")."""
    settings, section, comment = [], "", []
    # Several commented-out examples under one comment (matchzy_demo_upload_url / _header_key /
    # _header_value): the comment belongs to the first; the others point to it.
    shared_from = None
    lines = text.splitlines()
    for i, raw in enumerate(lines):
        line = raw.strip()
        if not line:
            comment, shared_from = [], None
            continue
        if line.startswith("// ===="):
            continue
        prev_rule = i > 0 and lines[i - 1].strip().startswith("// ====")
        next_rule = i + 1 < len(lines) and lines[i + 1].strip().startswith("// ====")
        if prev_rule and next_rule:
            section, comment = line[2:].strip(), []
            continue
        active = _SETTING.match(line)
        optional = None if active else _OPTIONAL.match(line)
        if active or optional:
            m = active or optional
            name, value = m.group(1), _unquote(m.group(2))
            default, desc = None, []
            for c in comment:
                d = re.match(r"^Default:\s*(.*)$", c)
                if d:
                    default = _unquote(d.group(1).strip())
                else:
                    desc.append(c)
            default = value if active and default is None else (default if default is not None else "")
            kind = "bool" if default in ("true", "false") else "number" if re.fullmatch(r"-?\d+(\.\d+)?", default) else "text"
            description = re.sub(r"\s*Syntax:$", "", " ".join(desc))
            if optional and shared_from:
                description = f"Goes with {shared_from} (see there)."
            elif optional:
                shared_from = name
            settings.append({
                "section": section, "name": name, "default": default, "type": kind,
                "optional": bool(optional), "example": value if optional else "",
                "description": description,
            })
            if active:
                comment = []
            continue
        if line.startswith("//"):
            comment.append(line[2:].strip())
    return settings


def on_config(config, **kwargs):
    root = os.path.normpath(os.path.join(os.path.dirname(config["config_file_path"]), ".."))

    def read(path):
        with open(os.path.join(root, path), encoding="utf-8") as f:
            return f.read()

    core = read("Core/MatchZy.cs")
    events = read("Core/Events.cs") + read("Core/Get5Events.cs")
    version = re.search(r'ModuleVersion => "([^"]+)"', core)
    global _settings_json, _events_json, _version, _language
    _events_json = json.dumps(events_catalog.build_catalog(root), ensure_ascii=False).replace("<", "\\u003c")
    _settings_json = json.dumps(parse_config_cfg(read("cfg/config.cfg")), ensure_ascii=False).replace("<", "\\u003c")
    _version = version.group(1) if version else "dev"
    config["extra"]["matchzy_version"] = _version
    _values.update({
        "@@MATCHZY_VERSION@@": _version,
        "@@STAT_COMMANDS@@": str(len(set(re.findall(r'\{ "\.([a-z0-9_]+)",', core)))),
        "@@STAT_SETTINGS@@": str(len(re.findall(r'new\("matchzy_', read("Config/ConfigConvars.cs")))),
        "@@STAT_EVENTS@@": str(len(set(re.findall(r'base\("([a-z_]+)"\)', events)))),
        "@@STAT_LANGUAGES@@": str(len([f for f in os.listdir(os.path.join(root, "lang")) if f.endswith(".json")])),
    })
    return config


def _page_language(config):
    """The language being built. The i18n plugin does not run on_config again for the Danish
    build, but it sets theme.language before the pages are rendered."""
    lang = config["theme"]["language"] if config is not None and "language" in config["theme"] else None
    return lang or "en"


def on_page_markdown(markdown, page=None, config=None, **kwargs):
    global _language
    _language = _page_language(config)
    if config is not None:
        config["extra"]["status"] = {"new": "Nyligt tilføjet" if _language == "da" else "Recently added"}
    new_in = page.meta.get("new_in") if page is not None else None
    if new_in and _is_recent(new_in):
        page.meta["status"] = "new"
    for key, value in _values.items():
        markdown = markdown.replace(key, value)
    return markdown


def on_page_content(html, page=None, config=None, **kwargs):
    global _language
    _language = _page_language(config)
    new_in = page.meta.get("new_in") if page is not None else None
    if new_in and _is_recent(new_in):
        label = ("Ny i" if _language == "da" else "New in") + f" {new_in}"
        badge = f'<p class="mz-newin"><span>{label}</span></p>'
        html = re.sub(r"(</h1>)", r"\1" + badge, html, count=1)
    if "<!--MATCHZY_EVENTS-->" in html:
        html = html.replace(
            "<!--MATCHZY_EVENTS-->",
            f'<script type="application/json" id="mz-pl-data">{_events_json}</script>',
        )
    if "<!--MATCHZY_SETTINGS-->" in html:
        html = html.replace(
            "<!--MATCHZY_SETTINGS-->",
            f'<script type="application/json" id="mz-cfg-data">{_settings_json}</script>',
        )
    return html


_en_404 = None


def on_post_template(output_content, template_name=None, config=None, **kwargs):
    """GitHub Pages serves only the root 404.html, and the i18n plugin's Danish build writes its 404
    there too, over the English one (Danish menu on an English-first site). Keep the English page;
    its text is in both languages anyway."""
    global _en_404
    if template_name != "404.html":
        return output_content
    if _page_language(config) == "en":
        _en_404 = output_content
    elif _en_404 is not None:
        return _en_404
    return output_content
