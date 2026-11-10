---
title: Match config builder
new_in: 1.0.24
---

# Match config builder

Fill in the form and copy the match config, or download it as `match.json`. Load it with `matchzy_loadmatch_url` from your panel, or put it in `csgo/cfg/MatchZy/` and use `matchzy_loadmatch`. Every field is explained in [Match config JSON](match-config.md). Nothing leaves your browser.

<div id="mz-builder" class="mz-builder" data-msg-badsteamid="Not a SteamID64:" data-msg-noplayers="no players listed" data-msg-toofewmaps="Pick at least {0} map(s) for this series." data-msg-twoany="Only one team can be open to anyone." data-msg-botsveto="A match against bots cannot veto: pick exactly as many maps as the series, or skip the veto." data-msg-badcvar="Not a valid cvar line (name value, no quotes or ;):" data-msg-copied="Copied!">
<div class="mzb-form">
<fieldset>
<legend>Match</legend>
<div class="mzb-row3">
<label>Match id<input id="mzb-matchid" type="number" min="0" value="0" title="0 = take a new id from the database"></label>
<label>Game mode<select id="mzb-gamemode"><option value="competitive">5v5 (competitive)</option><option value="wingman">Wingman (2v2)</option><option value="rush">Rush (3v3)</option><option value="1v1">1v1</option></select></label>
<label>Series<select id="mzb-series"><option value="1">BO1</option><option value="2">BO2</option><option value="3" selected>BO3</option><option value="5">BO5</option></select></label>
</div>
<div class="mzb-row3">
<label>Players per team<input id="mzb-ppt" type="number" min="1" max="10" placeholder="5" title="Empty = the mode's default"></label>
<label>Min. players to ready<input id="mzb-minready" type="number" min="1" max="10" placeholder="-" title="Empty = every player must ready up"></label>
<label>Friendly fire<select id="mzb-ff"><option value="">Mode cfg</option><option value="on">On</option><option value="off">Off</option><option value="grenades">Grenades only</option></select></label>
</div>
<label class="mzb-check"><input id="mzb-clinch" type="checkbox" checked> End the series once a team has won the majority of maps</label>
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
<label>More maps (one per line: workshop id, workshop/&lt;id&gt;, ws:&lt;name&gt;)<textarea id="mzb-extramaps" rows="2" spellcheck="false" placeholder="workshop/3070284539"></textarea></label>
</fieldset>
<fieldset>
<legend>Veto and sides</legend>
<label class="mzb-check"><input id="mzb-skipveto" type="checkbox"> Skip the veto: play the maps in this order</label>
<div class="mzb-row3">
<label>Starts the veto<select id="mzb-vetofirst"><option value="">Team 1 (default)</option><option value="team2">Team 2</option><option value="random">Random</option></select></label>
<label>Sides<select id="mzb-sidetype"><option value="standard">Knife (standard)</option><option value="always_knife">Always knife</option><option value="never_knife">Never knife (team1 starts CT)</option><option value="random">Random</option></select></label>
</div>
</fieldset>
<div class="mzb-teams">
<fieldset class="mzb-team">
<legend>Team 1</legend>
<div class="mzb-row3">
<label>Name<input id="t1-name" type="text" value="Alpha"></label>
<label>Tag<input id="t1-tag" type="text" maxlength="12"></label>
<label>Your id<input id="t1-id" type="text"></label>
</div>
<label>Players<select id="t1-mode"><option value="list">Roster</option><option value="any">Anyone (open team)</option><option value="bots">Bots</option></select></label>
<label id="t1-players-row">One per line: SteamID64 and name<textarea id="t1-players" rows="5" spellcheck="false">76561198000000001 alpha1
76561198000000002 alpha2</textarea></label>
<label id="t1-bots-row" hidden>Bot difficulty<select id="t1-difficulty"><option value="0">0</option><option value="1">1</option><option value="2" selected>2</option><option value="3">3</option></select></label>
<label>Coaches (optional)<textarea id="t1-coaches" rows="2" spellcheck="false" placeholder="76561198000000009 coach"></textarea></label>
</fieldset>
<fieldset class="mzb-team">
<legend>Team 2</legend>
<div class="mzb-row3">
<label>Name<input id="t2-name" type="text" value="Bravo"></label>
<label>Tag<input id="t2-tag" type="text" maxlength="12"></label>
<label>Your id<input id="t2-id" type="text"></label>
</div>
<label>Players<select id="t2-mode"><option value="list">Roster</option><option value="any">Anyone (open team)</option><option value="bots">Bots</option></select></label>
<label id="t2-players-row">One per line: SteamID64 and name<textarea id="t2-players" rows="5" spellcheck="false">76561198000000011 bravo1
76561198000000012 bravo2</textarea></label>
<label id="t2-bots-row" hidden>Bot difficulty<select id="t2-difficulty"><option value="0">0</option><option value="1">1</option><option value="2" selected>2</option><option value="3">3</option></select></label>
<label>Coaches (optional)<textarea id="t2-coaches" rows="2" spellcheck="false" placeholder="76561198000000009 coach"></textarea></label>
</fieldset>
</div>
<fieldset>
<legend>Spectators</legend>
<label>Spectators (optional, one per line: SteamID64 and name)<textarea id="mzb-specs" rows="2" spellcheck="false"></textarea></label>
<label>Cvars (optional, one per line: name value)<textarea id="mzb-cvars" rows="3" spellcheck="false" placeholder="mp_overtime_enable 1"></textarea></label>
</fieldset>
</div>
<div class="mzb-result">
<div class="mzb-result__head"><strong>Match config</strong>
<span><button type="button" id="mzb-copy" class="md-button md-button--primary">Copy</button> <button type="button" id="mzb-download" class="md-button">Download match.json</button></span></div>
<ul id="mzb-problems" class="mzb-problems" hidden></ul>
<pre class="mzb-out"><code id="mzb-out"></code></pre>
</div>
</div>
