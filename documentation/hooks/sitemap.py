"""MkDocs hook: rewrites sitemap.xml after the build and writes robots.txt.

MkDocs' own sitemap gives every page today's date and "changefreq daily", and the i18n plugin lists
the English fallback pages under /da/ as Danish versions (duplicate content for search engines).
This keeps the same URLs but:

  - <lastmod> is the date the page's source file (or a file it includes, such as CHANGELOG.md)
    last changed in git (its mtime when uncommitted);
  - no <changefreq> (search engines ignore it, and "daily" was wrong);
  - /da/ pages only where a real Danish page (x.da.md) exists, linked both ways with hreflang,
    plus hreflang="x-default" on the English page;
  - robots.txt points to the sitemap.
"""
import datetime
import gzip
import os
import re
import subprocess
from xml.sax.saxutils import escape

_docs_dir = ""
_site_url = ""
_snippet_base = ""


def on_config(config, **kwargs):
    global _docs_dir, _site_url, _snippet_base
    _docs_dir = config["docs_dir"]
    _site_url = (config.get("site_url") or "").rstrip("/") + "/"
    # pymdownx.snippets base_path ".." (from documentation/): the repository root.
    _snippet_base = os.path.normpath(os.path.join(os.path.dirname(config["config_file_path"]), ".."))
    return config


def _lastmod(path):
    """Date the file last changed in git, or its modification time when it is not committed yet."""
    try:
        out = subprocess.run(
            ["git", "log", "-1", "--format=%cs", "--", os.path.basename(path)],
            cwd=os.path.dirname(path), capture_output=True, text=True, timeout=10,
        ).stdout.strip()
        dirty = subprocess.run(
            ["git", "status", "--porcelain", "--", os.path.basename(path)],
            cwd=os.path.dirname(path), capture_output=True, text=True, timeout=10,
        ).stdout.strip()
        if out and not dirty:
            return out
    except (OSError, subprocess.SubprocessError):
        pass
    return datetime.date.fromtimestamp(os.path.getmtime(path)).isoformat()


def _page_lastmod(path):
    """A page's date: the newest of its own file and the files it includes with --8<-- (the
    changelog page is only an include of the repository's CHANGELOG.md)."""
    dates = [_lastmod(path)]
    with open(path, encoding="utf-8") as f:
        for name in re.findall(r'^--8<--\s+"([^"]+)"', f.read(), flags=re.M):
            included = os.path.join(_snippet_base, name)
            if os.path.exists(included):
                dates.append(_lastmod(included))
    return max(dates)


def _url(rel_md):
    """'guides/ready-system.md' -> 'guides/ready-system/', 'index.md' -> ''."""
    stem = rel_md[: -len(".md")]
    if stem == "index":
        return ""
    if stem.endswith("/index"):
        return stem[: -len("index")]
    return stem + "/"


def on_post_build(config, **kwargs):
    # The i18n plugin runs this hook once per language build; the root (English) build writes the
    # sitemap for both languages, the Danish one (site_dir .../da) does nothing.
    site_dir = config["site_dir"]
    if os.path.basename(os.path.normpath(site_dir)) == "da" or not _site_url:
        return

    entries = []
    for root, _, files in os.walk(_docs_dir):
        for f in sorted(files):
            if not f.endswith(".md") or f.endswith(".da.md"):
                continue
            rel = os.path.relpath(os.path.join(root, f), _docs_dir).replace(os.sep, "/")
            en_src = os.path.join(root, f)
            da_src = en_src[: -len(".md")] + ".da.md"
            url = _url(rel)
            has_da = os.path.exists(da_src)
            entries.append((url, en_src, da_src if has_da else None))
    entries.sort(key=lambda e: (e[0] != "", e[0]))

    def alternates(url, has_da):
        if not has_da:
            return ""
        en, da = _site_url + url, _site_url + "da/" + url
        return (
            f'\n        <xhtml:link rel="alternate" hreflang="en" href="{escape(en)}"/>'
            f'\n        <xhtml:link rel="alternate" hreflang="da" href="{escape(da)}"/>'
            f'\n        <xhtml:link rel="alternate" hreflang="x-default" href="{escape(en)}"/>'
        )

    urls = []
    for url, en_src, da_src in entries:
        alt = alternates(url, da_src is not None)
        urls.append(
            f"    <url>\n        <loc>{escape(_site_url + url)}</loc>\n"
            f"        <lastmod>{_page_lastmod(en_src)}</lastmod>{alt}\n    </url>"
        )
        if da_src:
            urls.append(
                f"    <url>\n        <loc>{escape(_site_url + 'da/' + url)}</loc>\n"
                f"        <lastmod>{_page_lastmod(da_src)}</lastmod>{alt}\n    </url>"
            )

    xml = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" '
        'xmlns:xhtml="http://www.w3.org/1999/xhtml">\n' + "\n".join(urls) + "\n</urlset>\n"
    )
    with open(os.path.join(site_dir, "sitemap.xml"), "w", encoding="utf-8") as f:
        f.write(xml)
    with gzip.open(os.path.join(site_dir, "sitemap.xml.gz"), "wb", compresslevel=9) as f:
        f.write(xml.encode("utf-8"))
    # The same sitemap under /da/, replacing the Danish build's own (which lists the English
    # fallback pages as Danish). Material's language switcher fetches the target language's
    # sitemap.xml to open the same page in the other language; without it every page logged a
    # 404 and the switch fell back to the Danish home page. robots.txt still names only the root one.
    da_dir = os.path.join(site_dir, "da")
    if os.path.isdir(da_dir):
        with open(os.path.join(da_dir, "sitemap.xml"), "w", encoding="utf-8") as f:
            f.write(xml)
        with gzip.open(os.path.join(da_dir, "sitemap.xml.gz"), "wb", compresslevel=9) as f:
            f.write(xml.encode("utf-8"))
    with open(os.path.join(site_dir, "robots.txt"), "w", encoding="utf-8") as f:
        f.write(f"User-agent: *\nAllow: /\n\nSitemap: {_site_url}sitemap.xml\n")
