---
title: Match config-builder
new_in: 1.0.24
---

# Match config-builder

Udfyld formularen, og kopiér match configen, eller download den som `match.json`. Load den med `matchzy_loadmatch_url` fra jeres panel, eller læg den i `csgo/cfg/MatchZy/`, og brug `matchzy_loadmatch`. Alle felter er forklaret i [Match config JSON](match-config.md). Intet forlader din browser.

<div id="mz-builder" class="mz-builder" data-msg-badsteamid="Ikke et SteamID64:" data-msg-noplayers="ingen spillere angivet" data-msg-toofewmaps="Vælg mindst {0} map(s) til denne serie." data-msg-twoany="Kun ét hold kan være åbent for alle." data-msg-botsveto="En kamp mod bots kan ikke have veto: vælg præcis lige så mange maps som serien, eller spring vetoen over." data-msg-badcvar="Ikke en gyldig cvar-linje (navn værdi, uden anførselstegn eller ;):" data-msg-copied="Kopieret!">
<div class="mzb-form">
<fieldset>
<legend>Kamp</legend>
<div class="mzb-row3">
<label>Match id<input id="mzb-matchid" type="number" min="0" value="0" title="0 = tag et nyt id fra databasen"></label>
<label>Spiltype<select id="mzb-gamemode"><option value="competitive">5v5 (competitive)</option><option value="wingman">Wingman (2v2)</option><option value="rush">Rush (3v3)</option><option value="1v1">1v1</option></select></label>
<label>Serie<select id="mzb-series"><option value="1">BO1</option><option value="2">BO2</option><option value="3" selected>BO3</option><option value="5">BO5</option></select></label>
</div>
<div class="mzb-row3">
<label>Spillere pr. hold<input id="mzb-ppt" type="number" min="1" max="10" placeholder="5" title="Tom = spiltypens standard"></label>
<label>Min. spillere for at være klar<input id="mzb-minready" type="number" min="1" max="10" placeholder="-" title="Tom = alle spillere skal være klar"></label>
<label>Friendly fire<select id="mzb-ff"><option value="">Spiltypens cfg</option><option value="on">Til</option><option value="off">Fra</option><option value="grenades">Kun granater</option></select></label>
</div>
<label class="mzb-check"><input id="mzb-clinch" type="checkbox" checked> Afslut serien, når et hold har vundet flertallet af mapsene</label>
</fieldset>
<fieldset>
<legend>Maps</legend>
<div class="mzb-maps">
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_ancient" checked> de_ancient</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_anubis" checked> de_anubis</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_dust2" checked> de_dust2</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_inferno" checked> de_inferno</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_mirage" checked> de_mirage</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_nuke" checked> de_nuke</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_overpass"> de_overpass</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_train" checked> de_train</label>
<label class="mzb-check"><input type="checkbox" class="mzb-map" value="de_vertigo"> de_vertigo</label>
</div>
<label>Flere maps (én pr. linje: workshop-id, workshop/&lt;id&gt;, ws:&lt;navn&gt;)<textarea id="mzb-extramaps" rows="2" spellcheck="false" placeholder="workshop/3070284539"></textarea></label>
</fieldset>
<fieldset>
<legend>Veto og sider</legend>
<label class="mzb-check"><input id="mzb-skipveto" type="checkbox"> Spring vetoen over: spil mapsene i denne rækkefølge</label>
<div class="mzb-row3">
<label>Starter vetoen<select id="mzb-vetofirst"><option value="">Hold 1 (standard)</option><option value="team2">Hold 2</option><option value="random">Tilfældigt</option></select></label>
<label>Sider<select id="mzb-sidetype"><option value="standard">Knife (standard)</option><option value="always_knife">Altid knife</option><option value="never_knife">Aldrig knife (hold 1 starter CT)</option><option value="random">Tilfældigt</option></select></label>
</div>
</fieldset>
<div class="mzb-teams">
<fieldset class="mzb-team">
<legend>Hold 1</legend>
<div class="mzb-row3">
<label>Navn<input id="t1-name" type="text" value="Alpha"></label>
<label>Tag<input id="t1-tag" type="text" maxlength="12"></label>
<label>Jeres id<input id="t1-id" type="text"></label>
</div>
<label>Spillere<select id="t1-mode"><option value="list">Roster</option><option value="any">Alle (åbent hold)</option><option value="bots">Bots</option></select></label>
<label id="t1-players-row">Én pr. linje: SteamID64 og navn<textarea id="t1-players" rows="5" spellcheck="false">76561198000000001 alpha1
76561198000000002 alpha2</textarea></label>
<label id="t1-bots-row" hidden>Bot-sværhedsgrad<select id="t1-difficulty"><option value="0">0</option><option value="1">1</option><option value="2" selected>2</option><option value="3">3</option></select></label>
<label>Coaches (valgfrit)<textarea id="t1-coaches" rows="2" spellcheck="false" placeholder="76561198000000009 coach"></textarea></label>
</fieldset>
<fieldset class="mzb-team">
<legend>Hold 2</legend>
<div class="mzb-row3">
<label>Navn<input id="t2-name" type="text" value="Bravo"></label>
<label>Tag<input id="t2-tag" type="text" maxlength="12"></label>
<label>Jeres id<input id="t2-id" type="text"></label>
</div>
<label>Spillere<select id="t2-mode"><option value="list">Roster</option><option value="any">Alle (åbent hold)</option><option value="bots">Bots</option></select></label>
<label id="t2-players-row">Én pr. linje: SteamID64 og navn<textarea id="t2-players" rows="5" spellcheck="false">76561198000000011 bravo1
76561198000000012 bravo2</textarea></label>
<label id="t2-bots-row" hidden>Bot-sværhedsgrad<select id="t2-difficulty"><option value="0">0</option><option value="1">1</option><option value="2" selected>2</option><option value="3">3</option></select></label>
<label>Coaches (valgfrit)<textarea id="t2-coaches" rows="2" spellcheck="false" placeholder="76561198000000009 coach"></textarea></label>
</fieldset>
</div>
<fieldset>
<legend>Tilskuere</legend>
<label>Tilskuere (valgfrit, én pr. linje: SteamID64 og navn)<textarea id="mzb-specs" rows="2" spellcheck="false"></textarea></label>
<label>Cvars (valgfrit, én pr. linje: navn værdi)<textarea id="mzb-cvars" rows="3" spellcheck="false" placeholder="mp_overtime_enable 1"></textarea></label>
</fieldset>
</div>
<div class="mzb-result">
<div class="mzb-result__head"><strong>Match config</strong>
<span><button type="button" id="mzb-copy" class="md-button md-button--primary">Kopiér</button> <button type="button" id="mzb-download" class="md-button">Download match.json</button></span></div>
<ul id="mzb-problems" class="mzb-problems" hidden></ul>
<pre class="mzb-out"><code id="mzb-out"></code></pre>
</div>
</div>
