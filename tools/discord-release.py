#!/usr/bin/env python3
"""Print the Discord webhook payload (JSON) that announces a release.

Usage: discord-release.py <version> <owner/repo> [whats-new-section.md]

The section file is the one release.yml writes for the GitHub release body (the Action needed
box, the highlights and the count of the rest). Its GitHub markdown is turned into what a
Discord embed renders. Without the file the embed only carries the links.
"""
import json
import re
import sys

SITE = "https://matchzy.miksen.me/"
LIMIT = 3800  # an embed description holds 4096 characters


def discord_md(text):
    out = []
    for line in text.splitlines():
        if line.startswith("> [!WARNING]"):
            continue
        if line.startswith("> "):
            line = line[2:]
        # "- **New:** text" reads better in an embed without the label on every line.
        line = re.sub(r"^- \*\*(New|Changed):\*\* ", "- ", line)
        out.append(line)
    return "\n".join(out).strip()


def main():
    ver, repo = sys.argv[1], sys.argv[2]
    section = ""
    if len(sys.argv) > 3:
        try:
            section = discord_md(open(sys.argv[3], encoding="utf-8").read())
        except OSError:
            section = ""
    if len(section) > LIMIT:
        section = section[:LIMIT].rsplit("\n", 1)[0] + "\n..."

    base = f"https://github.com/{repo}"
    zip_url = f"{base}/releases/download/{ver}/MatchZy-{ver}.zip"
    links = (
        f"[Download MatchZy-{ver}.zip]({zip_url})\n"
        f"[What's new]({SITE}whats-new/) · [Changelog]({base}/blob/{ver}/CHANGELOG.md) · "
        f"[Docs]({SITE})"
    )
    embed = {
        "title": f"MatchZy {ver} is out",
        "url": f"{base}/releases/tag/{ver}",
        "color": 0x3FB950,
        "description": section,
        "fields": [
            {"name": "Get it", "value": links},
            {"name": "Update", "value": "Extract the zip into `game/csgo/`. Your `cfg/` files are not touched."},
        ],
    }
    if not section:
        del embed["description"]
    print(json.dumps({"username": "MatchZy", "embeds": [embed]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
