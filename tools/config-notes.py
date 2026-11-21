#!/usr/bin/env python3
"""Release note lines for cfg/config.cfg: settings added and defaults changed since the last release.

Usage: tools/config-notes.py <version> <owner/repo>

Compares cfg/config.cfg in the working tree with the same file at the newest release tag before
<version> and prints GitHub markdown, or nothing when the file did not change (or there is no
earlier tag, as in a shallow checkout). An updated server keeps its own config.cfg, so its admin
only learns about new settings from here.
"""
import hashlib
import re
import subprocess
import sys

CFG = "cfg/config.cfg"
SHOWN = 12  # settings listed by name before the rest is only counted


def git(*args):
    r = subprocess.run(["git", *args], capture_output=True, text=True)
    return r.stdout if r.returncode == 0 else None


def settings(text):
    found = {}
    for line in text.splitlines():
        m = re.match(r"\s*(matchzy_\w+)\s*(.*?)\s*(?://.*)?$", line)
        if m:
            found[m.group(1)] = m.group(2).strip()
    return found


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    version, repo = sys.argv[1], sys.argv[2]

    tags = git("tag", "--list", "[0-9]*", "--sort=-v:refname", "--merged", "HEAD")
    prev = next((t for t in (tags or "").split() if t != version), None)
    if not prev:
        return
    old_text = git("show", f"{prev}:{CFG}")
    if old_text is None:
        return
    old, new = settings(old_text), settings(open(CFG, encoding="utf-8").read())

    added = [f"`{k} {v}`".replace(" `", "`") for k, v in new.items() if k not in old]
    changed = [f"`{k}` {old[k] or '(empty)'} -> {v or '(empty)'}" for k, v in new.items() if k in old and old[k] != v]
    removed = [f"`{k}`" for k in old if k not in new]
    if not (added or changed or removed):
        return

    def listed(items):
        more = len(items) - SHOWN
        return ", ".join(items[:SHOWN]) + (f" and {more} more" if more > 0 else "")

    anchor = hashlib.sha256(CFG.encode()).hexdigest()
    url = f"https://github.com/{repo}/compare/{prev}...{version}#diff-{anchor}"
    print(f"**config.cfg since {prev}** ([see the diff]({url}))")
    print()
    if added:
        print(f"- New settings: {listed(added)}")
    if changed:
        print(f"- Changed defaults: {listed(changed)}")
    if removed:
        print(f"- Removed: {listed(removed)}")
    print()
    print("Your own config.cfg is not touched by an update: add the new lines you want, or compare "
          "with the current defaults MatchZy writes to `cfg/MatchZy/defaults/` on every load.")


if __name__ == "__main__":
    main()
