#!/usr/bin/env python3
"""Checks internal links and #anchors in the built MkDocs site.

mkdocs build --strict catches links to missing pages, but not a link to a heading that does not
exist (#warmup on a page whose heading id is warmup-and-ready-check). Usage: check_links.py <site dir>
"""
import os
import sys
from html.parser import HTMLParser
from urllib.parse import unquote, urljoin, urlparse


class Page(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids, self.links = set(), []

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if a.get("id"):
            self.ids.add(a["id"])
        if tag == "a" and a.get("href"):
            self.links.append(a["href"])


site = sys.argv[1] if len(sys.argv) > 1 else "site"
pages = {}
for root, _, files in os.walk(site):
    for f in files:
        if f.endswith(".html"):
            rel = os.path.relpath(os.path.join(root, f), site).replace(os.sep, "/")
            url = "/" + (rel[: -len("index.html")] if rel.endswith("index.html") else rel)
            p = Page()
            with open(os.path.join(root, f), encoding="utf-8") as fh:
                p.feed(fh.read())
            pages[url] = p

bad = 0
for url, page in sorted(pages.items()):
    if url.startswith("/404"):
        continue
    for href in page.links:
        target = urlparse(urljoin("http://site" + url, href))
        if target.netloc != "site":
            continue
        path = unquote(target.path)
        if not path.endswith("/") and "." not in path.rsplit("/", 1)[-1]:
            path += "/"
        if path.startswith(("/assets/", "/search/")) or path.endswith((".xml", ".css", ".js", ".svg", ".png", ".zip", ".json")):
            continue
        dest = pages.get(path)
        if dest is None:
            print(f"{url}: link to a missing page: {href}")
            bad += 1
        elif target.fragment and unquote(target.fragment) not in dest.ids:
            print(f"{url}: link to a missing anchor: {href}")
            bad += 1
print(f"{bad} broken link(s) in {len(pages)} page(s)")
sys.exit(1 if bad else 0)
