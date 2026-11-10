---
title: Indstillings-generator
new_in: 1.0.24
---

# Indstillings-generator

Alle indstillinger i `cfg/MatchZy/config.cfg` med forklaring og standardværdi. Søg, ændr de værdier, du har brug for, og kopiér resultatet: kun de ændrede indstillinger. Sæt linjerne ind i `config.cfg` i stedet for de gamle, eller i en privat cfg, der køres efter den. Listen bygges fra plugin'ets egen `config.cfg`, så den altid passer til denne version. Forklaringerne er på engelsk, som i filen. Intet forlader din browser.

Alle indstillinger, også dem der ikke står i `config.cfg`, findes i [Konfiguration (convars)](convars.md).

<!--MATCHZY_SETTINGS-->

<div id="mz-cfggen" class="mz-cfggen" data-msg-header="MatchZy-indstillinger ændret fra standard" data-msg-empty="Ændr en indstilling til venstre for at se den her." data-msg-changedcount="{0} ændret" data-msg-reset="Tilbage til standard" data-msg-default="Standard" data-msg-unset="ikke sat" data-msg-copied="Kopieret!">
<div class="cfg-main">
<div class="cfg-tools">
<input id="cfg-search" type="search" placeholder="Søg i indstillinger (navn eller tekst)..." autocomplete="off">
<label class="cfg-only"><input id="cfg-changed" type="checkbox"> Kun ændrede</label>
</div>
<div id="cfg-list"></div>
</div>
<div class="cfg-result">
<div class="cfg-result__head"><strong>Dine indstillinger</strong> <span id="cfg-count" class="cfg-count"></span></div>
<div class="cfg-buttons"><button type="button" id="cfg-copy" class="md-button md-button--primary">Kopiér</button> <button type="button" id="cfg-download" class="md-button">Download</button> <button type="button" id="cfg-resetall" class="md-button">Nulstil alle</button></div>
<pre class="cfg-out"><code id="cfg-out"></code></pre>
</div>
</div>
