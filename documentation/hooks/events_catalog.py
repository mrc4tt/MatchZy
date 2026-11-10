"""Reads MatchZy's event classes (Core/Events.cs, Core/Get5Events.cs) into a catalog for the event
explorer page: every event name, its format, its fields (inherited ones included) with type and
comment, and an example payload. Used by hooks/values.py; kept free of MkDocs so it can be run alone:

    python3 -I documentation/hooks/events_catalog.py <repo root>
"""
import glob
import json
import os
import re
import sys

_CLASS = re.compile(r"^public (?:abstract |sealed )?class (\w+)(?:\s*:\s*(\w+))?", re.M)
_PROP = re.compile(r"^\s*public (required )?([\w<>\[\]?, ]+?) (\w+) \{ get;")
_JSON_NAME = re.compile(r'\[JsonPropertyName\("([^"]+)"\)\]')

# Classes whose event name is passed in by a helper rather than at "new Class(...)".
_NAME_HELPERS = {
    "MatchPhaseEvent": r'PublishPhaseEvent\("([a-z_]+)"',
    "Get5BombCarrierEvent": r'SendBombCarrierEvent\("([a-z_]+)"',
    "BombCarrierLiveEvent": r'SendBombCarrierEvent\("([a-z_]+)"',
}
# Named "...LiveEvent" but sent in both event formats.
_FORMAT_OVERRIDES = {"GoingLiveEvent": "both", "RoundStartLiveEvent": "both"}

_SAMPLES = {
    "matchid": 1234, "map_number": 0, "round_number": 7, "round_time": 41250, "map_name": "de_mirage",
    "steamid": "76561198000000001", "steamid64": "76561198000000001", "player_steamid": "76561198000000001",
    "attacker_steamid": "76561198000000001", "victim_steamid": "76561198000000002", "name": "Mio",
    "player_name": "Mio", "attacker_name": "Mio", "victim_name": "Bravo1", "team": "team1",
    "side": "ct", "winner": "team1", "weapon": "ak47", "plugin_version": "1.0.24",
    "thread_id": "8f2c0c1e5b1a4a0e9d7c2b6a1f3e4d5c", "call_id": "c41d9a7e2f6b4e1a8b3c5d7e9f0a1b2c",
    "message": "gg", "filename": "1234_0_de_mirage.dem", "demo_filename": "1234_0_de_mirage.dem",
    "hostname": "Liga #3", "address": "0.0.0.0:27015", "reason": "admin", "kind": "admin",
    "called_at": "2026-11-09T18:02:11Z", "sent_at": "2026-11-09T18:03:40Z", "closed_at": "2026-11-09T18:10:00Z",
    "team1_side": "t", "site": "A", "pause_type": "technical", "next_map": "de_inferno", "trigger": "changelevel",
    "command": "say", "ip_address": "203.0.113.7", "team_name": "Alpha",
}


def _strip_comments(body):
    return re.sub(r"/\*.*?\*/", "", body, flags=re.S)


def parse_classes(text):
    """{class: {"base", "props": [(json, type, comment, required)], "names": [literal base() names],
    "param_ctor": bool}}"""
    classes = {}
    starts = list(_CLASS.finditer(text))
    for i, m in enumerate(starts):
        body = text[m.end(): starts[i + 1].start() if i + 1 < len(starts) else len(text)]
        props, comment, json_name = [], [], None
        for line in body.splitlines():
            s = line.strip()
            if s.startswith("//"):
                c = s.lstrip("/").strip()
                if c and not set(c) <= set("═─="):
                    comment.append(c)
                continue
            jn = _JSON_NAME.search(s)
            if jn:
                json_name = jn.group(1)
                continue
            if s.startswith("[JsonIgnore"):
                continue
            pm = _PROP.match(line)
            if pm and json_name:
                props.append((json_name, pm.group(2).strip(), " ".join(comment), bool(pm.group(1))))
            if s and not s.startswith("["):
                comment, json_name = ([], None) if not pm else ([], None)
        names = re.findall(r'base\("([a-z_]+)"\)', body)
        classes[m.group(1)] = {
            "base": m.group(2), "props": props, "names": names,
            "param_ctor": bool(re.search(r"base\(eventName\)", body)),
        }
    return classes


def _construction_names(cls, sources):
    """Event names given where a class with an eventName constructor is created:
    new PlayerReadyChangedEvent(ready ? "player_ready" : "player_unready"), PublishPhaseEvent("x", ...)."""
    names = set()
    for src in sources:
        for m in re.finditer(r"new " + cls + r"\(([^;{]*?)\)\s*(?:\{|;)", src):
            names.update(re.findall(r'"([a-z_]+)"', m.group(1)))
        if cls in _NAME_HELPERS:
            names.update(re.findall(_NAME_HELPERS[cls], src))
    return sorted(names)


def _sample(name, ctype, classes, depth=0, parent=""):
    t = ctype.replace("?", "").strip()
    if parent == "weapon" and name == "name":
        return "ak47"
    if t == "string" and name.endswith("_team"):
        return "CT"
    if t == "string" and name.endswith("_side"):
        return "ct"
    if name in _SAMPLES and t in ("string", "long", "int"):
        v = _SAMPLES[name]
        return str(v) if t == "string" and not isinstance(v, str) else v
    if t.startswith("List<") or t.endswith("[]"):
        inner = t[5:-1] if t.startswith("List<") else t[:-2]
        return [_sample(name, inner, classes, depth + 1, parent)] if depth < 3 else []
    if t.startswith("Dictionary<"):
        return {}
    if t == "string":
        return name.replace("_", " ") if name else "text"
    if t in ("int", "long", "short"):
        return 1
    if t in ("float", "double", "decimal"):
        return 1.5
    if t == "bool":
        return False
    if t in classes and depth < 4:
        return {p[0]: _sample(p[0], p[1], classes, depth + 1, name) for p in _all_props(t, classes)}
    return None


def _all_props(cls, classes):
    chain, c = [], cls
    while c in classes:
        chain.append(c)
        c = classes[c]["base"]
    props, seen = [], set()
    for c in reversed(chain):
        for p in classes[c]["props"]:
            if p[0] not in seen:
                seen.add(p[0])
                props.append(p)
    return props


def _derives(cls, root, classes):
    c = cls
    while c in classes:
        if c == root:
            return True
        c = classes[c]["base"]
    return False


def build_catalog(repo_root):
    read = lambda p: open(os.path.join(repo_root, p), encoding="utf-8").read()
    text = _strip_comments(read("Core/Events.cs")) + "\n" + _strip_comments(read("Core/Get5Events.cs"))
    # Keep the // comments (descriptions), only block comments were dropped.
    classes = parse_classes(text)
    sources = [open(f, encoding="utf-8").read() for f in glob.glob(os.path.join(repo_root, "**", "*.cs"), recursive=True)
               if "/tests/" not in f.replace(os.sep, "/") and "/bin/" not in f and "/obj/" not in f]
    events = {}
    for cls, info in classes.items():
        if not _derives(cls, "MatchZyEvent", classes) or cls == "MatchZyEvent":
            continue
        names = list(info["names"])
        if info["param_ctor"]:
            names += _construction_names(cls, sources)
        if not names:
            continue
        if cls in _FORMAT_OVERRIDES:
            fmt = _FORMAT_OVERRIDES[cls]
        elif cls.startswith("Get5"):
            fmt = "get5"
        elif cls.endswith("LiveEvent"):
            fmt = "legacy"
        elif _derives(cls, "ServerLifecycleEvent", classes):
            fmt = "server"
        else:
            fmt = "both"
        props = _all_props(cls, classes)
        for name in sorted(set(names)):
            example = {"event": name}
            for p in props:
                if p[0] != "event":
                    example[p[0]] = _sample(p[0], p[1], classes)
            key = (name, fmt)
            if key in events:
                continue
            events[key] = {
                "name": name, "format": fmt, "class": cls,
                "fields": [{"name": p[0], "type": p[1], "description": p[2], "required": p[3]} for p in props if p[0] != "event"],
                "example": example,
            }
    order = {"both": 0, "server": 1, "get5": 2, "legacy": 3}
    return sorted(events.values(), key=lambda e: (order[e["format"]], e["name"]))


if __name__ == "__main__":
    cat = build_catalog(sys.argv[1] if len(sys.argv) > 1 else ".")
    print(json.dumps([{k: e[k] for k in ("name", "format", "class")} for e in cat], indent=1))
    print(len(cat), "events")
