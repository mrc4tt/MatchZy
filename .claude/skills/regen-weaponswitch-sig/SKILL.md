---
name: regen-weaponswitch-sig
description: Re-confirm / update the CCSPlayer_WeaponServices::SelectItem vtable offset in the forked CounterStrikeSharp gamedata.json after a CS2 update. Drives the ida-pro-mcp server against server.so (linux) / server.dll (windows). Use when the plugin logs "[MatchZy] CCSPlayer_WeaponServices_SelectItem offset missing" or .last/.back/.ln stops putting the nade in hand.
---

# Regenerate the SelectItem vtable offset

MatchZy's `TeleportAndClearPose` (Utility.cs → `SwitchWeaponNative`) deploys an
already-owned grenade on `.last` / `.back` / `.ln` by calling the engine
**`CCSPlayer_WeaponServices::SelectItem(this, CBasePlayerWeapon* weapon, int subType)`**
— it holsters the current weapon and deploys the target (redraws the viewmodel +
plays the deploy anim, which also clears the frozen throw pose).

It is a **virtual function**, called via its **vtable index**, read from the forked
CounterStrikeSharp gamedata key `CCSPlayer_WeaponServices_SelectItem`:

```json
  "CCSPlayer_WeaponServices_SelectItem": {
    "offsets": {
      "windows": 27,
      "linux": 28
    }
  },
```

File: `~/CounterStrikeSharp/configs/addons/counterstrikesharp/gamedata/gamedata.json`
(source of truth; build/ and out/ are generated). The code calls
`GameData.GetOffset("CCSPlayer_WeaponServices_SelectItem")`. If the key is missing
or the index is wrong, the plugin logs `[MatchZy] CCSPlayer_WeaponServices_SelectItem
offset missing ...` (or the nade equips in schema but viewmodel/legs are wrong) and
falls back to a non-deploying pointer switch.

A CS2 update can shift the **vtable index**. This skill re-confirms it. Vtable
indices move far less than byte signatures, so most updates need no change — but
verify.

## Preconditions

- IDA Pro open on the target binary with the **ida-pro-mcp** server running.
  - Linux: `server.so` / `libserver.so`.
  - Windows: `server.dll`.
- `mcp__ida-pro-mcp__server_health` → check the `module` field. The MCP talks to
  ONE IDB at a time — do linux and windows in separate passes.

## Procedure

1. **Find the slot by fingerprint (slot-agnostic).** Don't trust a hardcoded index
   — scan the vtable and identify SelectItem by behavior, so it survives an index
   shift. `mcp__ida-pro-mcp__py_eval`:
   ```python
   import idc, ida_hexrays
   # Auto-detect platform by which vtable symbol resolves, and apply the right
   # layout: Itanium (linux) vptr points past offset_to_top+typeinfo (+16); MSVC
   # (windows) vptr points at slot0 (+0).
   ea = idc.get_name_ea_simple('_ZTV24CCSPlayer_WeaponServices')   # linux
   if ea != idc.BADADDR:
       base = ea + 16
   else:
       ea = idc.get_name_ea_simple('??_7CCSPlayer_WeaponServices@@6B@')  # windows
       base = ea
   # SelectItem fingerprint: 3rd arg (subType) compared to 4 (the reselect /
   # holster-only branch) AND >=2 indirect vtable calls through the weapon arg.
   hits = []
   for i in range(16, 40):
       fn = idc.get_qword(base + i*8)
       try:
           c = str(ida_hexrays.decompile(fn))
       except Exception:
           continue
       if 'a3 == 4' in c and (c.count(')(a2)') + c.count(')(a2,') + c.count('a2 +')) >= 2:
           hits.append([i, hex(fn)])
   print({'vtable': hex(ea), 'matches': hits})   # expect exactly one match
   ```
   The matched `i` IS the gamedata offset for this platform; `fn` is the function.
   Sanity-check against the known build-14165 values: linux 28, windows 27. If zero
   matches, Hex-Rays may have renamed the args — widen by also accepting `== 4` and
   a high `a2`-vtable-call count, or fall back to step 2 on the 28/27 slots.

2. **Verify it is SelectItem.** `mcp__ida-pro-mcp__decompile` on `fn`. It must look
   like a weapon select: 3 args `(this, weapon_ptr, int)`; gets the active weapon;
   the `subType == 4` branch; on the different-weapon path it calls a Holster vfunc
   on the current weapon, a SetActiveWeapon on the services (`*this + 176` linux /
   `*this + 168` windows), and a Deploy vfunc on the target weapon (`*weapon + 2664`
   linux / `+ 2600` windows). Cross-check CS2Fixes gamedata
   `CCSPlayer_WeaponServices::SelectItem` (ships the same offset, win 27 / lin 28).

3. **Write the offset** into
   `~/CounterStrikeSharp/configs/addons/counterstrikesharp/gamedata/gamedata.json`
   under `CCSPlayer_WeaponServices_SelectItem` → `offsets` → `linux` / `windows`.
   Confirm the index found in step 1/2 for each platform.

4. **Deploy the fork gamedata** to the server's
   `addons/counterstrikesharp/gamedata/gamedata.json` (it ships with the forked CSS
   runtime, NOT with the plugin), then `css_plugins reload MatchZy` (or restart).

5. **Verify on the server.** Throw a nade, `.last`. Success = no
   `[MatchZy] ... SelectItem offset missing` line, nade visibly in hand, body flat,
   legs clean (molotov included — it knife-bounces).

## Notes

- The offset lives in the **forked CSS gamedata**, not the plugin. On stock CSS
  (no entry) the plugin guards itself: `GetOffset` throws → native path disabled →
  pointer-switch fallback, no crash. So MatchZy still loads on stock CSS; it just
  won't deploy the nade into the hand.
- Both binaries keep the vtable symbol (`_ZTV24CCSPlayer_WeaponServices` /
  `??_7CCSPlayer_WeaponServices@@6B@`). Mind the layout: Itanium prepends
  offset_to_top + typeinfo (slot0 = vtable+16); MSVC does not (slot0 = vtable+0).
- Reference (build 14165):
  - linux:   SelectItem @ `0x1444470`,   vtable `_ZTV24CCSPlayer_WeaponServices` @ `0x2350290`,   slot 28.
  - windows: SelectItem @ `0x180A957A0`, vtable `??_7CCSPlayer_WeaponServices@@6B@` @ `0x181734958`, slot 27.
- If you ever prefer a byte-signature instead of the offset (e.g. shipping a
  plugin-local gamedata for stock CSS), `mcp__ida-pro-mcp__make_signature_for_function`
  on `fn` emits a unique pattern in CSS format.
