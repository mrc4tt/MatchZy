# Updating

Extract the new release zip over the old install and restart the server (or `css_plugins reload MatchZy`).

The release zip contains nothing under `cfg/`, so an update never overwrites anything you edited and never creates a second `cfg/MatchZy/` folder next to a `cfg/matchzy/` one:

| File | What happens on update |
|---|---|
| `config.cfg` | Not in the zip. New settings are **appended** to your existing file; your edits are never changed. See below. |
| `database.json` | Not in the zip, so your MySQL login is kept. A reference copy is in `cfg/MatchZy/defaults/database.json.example`. |
| `live.cfg`, `scrim.cfg`, `prac.cfg`, ... | Not in the zip, never touched. They are only written when missing. See below. |
| `<mode>_override.cfg` | Not in the zip and never written by MatchZy. The safest place for your own changes to a mode. See [Server-specific settings](../reference/files.md#server-specific-settings). |
| `savednades.json`, `botpositions.json`, `whitelist.cfg`, `admins.json` | Not in the zip, never touched. |
| `defaults/` | Rewritten on every load. Reference copies of the current default cfgs, never executed. Do not edit them; your changes would be lost. |
| `gamedata/matchzy.json` | Replaced. Always deploy it together with the plugin. |

- **`config.cfg`**: new settings go under a `// --- Added by MatchZy update ---` header. A setting you commented out is not added back. Retired settings are removed automatically so the server does not log "Unknown command".
- **`live.cfg`, `scrim.cfg`, `prac.cfg`, ...**: when a release changes a default in one of them, the changelog says so. Compare your file with the copy in `cfg/MatchZy/defaults/` and copy the lines you want, or delete your file to get the new default on the next restart.

!!! warning "After a CS2 update"
    Valve updates can shift the native signatures in `gamedata/matchzy.json`. If practice rethrows, `.breakrestore` or `.t`/`.ct` from spectator stop working after a game update, update to the latest MatchZy release. Missing or stale signatures only disable the affected feature; they never crash the server. See [Gamedata](gamedata.md).

## Keeping your own changes

Edits to `warmup.cfg`, `live.cfg` and the other mode cfgs survive every update. The catch: when a release changes a default in one of those files, a server that edited its own copy does not get the new default.

To keep your changes and still get new defaults, leave the mode cfgs as they are and put your changes in an override file instead:

1. Create `<mode>_override.cfg` in the same folder as the mode cfg, for example `cfg/MatchZy/warmup_override.cfg` (or `cfg/matchzy/` if your server uses that folder).
2. Add only the lines you changed, not a copy of the whole file. A full copy pins every value, so you would miss new defaults again. To find your changes, compare your file with the reference copy: `diff warmup.cfg defaults/warmup.cfg`.
3. It runs right after `warmup.cfg`, so your values win.

```cfg
// cfg/MatchZy/warmup_override.cfg
ammo_grenade_limit_flashbang 0
mp_respawn_immunitytime 5
```

Once your changes are in the override, you can delete `warmup.cfg` to get the current defaults on the next restart, without losing your changes. See [Server-specific settings](../reference/files.md#server-specific-settings).

Check the [changelog](../changelog.md) for anything that needs action before updating.
