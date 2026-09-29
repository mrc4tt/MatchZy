# Updating

Extract the new release zip over the old install and restart the server (or `css_plugins reload MatchZy`).

The release zip is built so an update never overwrites anything you edited:

| File | What happens on update |
|---|---|
| `config.cfg` | Not in the zip. New settings from the update are **appended** to your existing file under a `// --- Added by MatchZy update ---` header. Your edits are never changed, and a setting you commented out is not added back. Retired settings are removed automatically so the server does not log "Unknown command". |
| `database.json` | Not in the zip, so your MySQL login is kept. `database.json.example` is shipped for reference. |
| `live.cfg`, `scrim.cfg`, `prac.cfg`, ... | Replaced by the new versions. Keep custom mode settings in your own cfg if you do not want them overwritten. |
| `savednades.json`, `botpositions.json`, `whitelist.cfg`, `admins.json` | Not in the zip, never touched. |
| `gamedata/matchzy.json` | Replaced. Always deploy it together with the plugin. |

!!! warning "After a CS2 update"
    Valve updates can shift the native signatures in `gamedata/matchzy.json`. If practice rethrows, `.breakrestore` or `.t`/`.ct` from spectator stop working after a game update, update to the latest MatchZy release. Missing or stale signatures only disable the affected feature; they never crash the server. See [Gamedata](gamedata.md).

Check the [changelog](../changelog.md) for anything that needs action before updating.
