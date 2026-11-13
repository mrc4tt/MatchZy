# MatchZy documentation site

Astro + Starlight. Served at https://matchzy.miksen.me from the RE server (see `deploy/SETUP.md`).

```sh
npm install
npm run dev        # http://localhost:4321
npm run build      # dist/
./deploy.sh        # build and publish (--list, --rollback ID)
```

The build reads the plugin source one folder up, so the site always matches this checkout:

| What | From | Where |
| --- | --- | --- |
| Version, landing page numbers | `Core/MatchZy.cs`, `Core/Events.cs`, `Config/ConfigConvars.cs`, `lang/` | `src/data/plugin.ts` |
| Settings generator | `cfg/config.cfg` | `src/data/settings.mjs` |
| Event explorer | `Core/Events.cs`, `Core/Get5Events.cs` (+ every `.cs` for event names) | `src/data/events.mjs` |
| Changelog, What's new | `CHANGELOG.md`, `documentation/docs/whats-new.md` | `src/data/changelog.ts` |
| `@@MATCHZY_VERSION@@` in pages | `Core/MatchZy.cs` | `src/plugins/remark-docs.mjs` |

Pages are in `src/content/docs/` (English) and `src/content/docs/da/` (Danish; a page without a
Danish copy is shown in English). Until the old MkDocs site in `documentation/` is retired,
`npm run convert` regenerates `getting-started/`, `guides/` and `reference/` from it.

Page syntax beyond Markdown: Starlight asides (`:::note`), `## Heading {#custom-id}`, and
` ```mermaid ` blocks (drawn in the browser). `new_in: x.y.z` in the front matter shows a "New"
badge while that version is recent.
