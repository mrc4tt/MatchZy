#!/usr/bin/env python3
"""Print one version's section of whats-new.md as GitHub markdown.

Usage: whats-new-section.py <version>

The "Action needed" box in full (as a GitHub alert), then at most 5 highlights (the New items,
else the Changed ones) and a count of the rest. Docs links point to the docs site (relative .md
links break on GitHub). Prints nothing when the section is missing. Used for the release body
(release.yml) and the Discord announcement (discord-release.yml).
"""
import re, sys
ver = sys.argv[1]
site = "https://matchzy.miksen.me/"
text = open("whats-new.md", encoding="utf-8").read()
m = re.search(r"^## " + re.escape(ver) + r" <small>.*?</small>\n(.*?)(?=^## |\Z)", text, re.S | re.M)
if not m:
    sys.exit(0)

def gh(line):
    line = re.sub(r"\+\+([^+\s][^\s]*?)\+\+", lambda k: k.group(1).replace("+", " + ").title(), line)
    def link(k):
        path, anchor = k.group(1), k.group(2) or ""
        path = re.sub(r"(^|/)index\.md$", r"\1", path)
        path = re.sub(r"\.md$", "/", path)
        return "](" + site + path + anchor + ")"
    return re.sub(r"\]\((?!https?:)([^)#]*?\.md)(#[^)]*)?\)", link, line)

alert, items = [], []
in_alert = False
for line in m.group(1).strip().splitlines():
    if line.startswith('!!! warning "Action needed"'):
        alert += ["> [!WARNING]", "> **Action needed**"]
        in_alert = True
    elif in_alert and line.startswith("    "):
        alert.append("> " + gh(line[4:]))
    elif line.startswith("- **"):
        in_alert = False
        label = re.match(r"- \*\*(\w+):\*\*", line)
        items.append((label.group(1) if label else "", gh(line)))
    else:
        in_alert = False

picks = [i for i in items if i[0] == "New"] or [i for i in items if i[0] == "Changed"] or items
picks = picks[:5]
rest = [i for i in items if i not in picks]
out = alert + ([""] if alert else [])
if picks:
    out.append("**Highlights**")
    out.append("")
    out += [p[1] for p in picks]
if rest:
    counts = {}
    for label, _ in rest:
        counts[label.lower() or "other"] = counts.get(label.lower() or "other", 0) + 1
    out.append("")
    out.append("Plus " + str(len(rest)) + " more (" + ", ".join(f"{n} {k}" for k, n in counts.items()) + ").")
print("\n".join(out))
