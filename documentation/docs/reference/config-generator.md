---
title: Settings generator
new_in: 1.0.24
---

# Settings generator

Every setting in `cfg/MatchZy/config.cfg`, with its explanation and default. Search, change the values you need, and copy the result: only the changed settings. Paste the lines into `config.cfg` in place of the old ones, or into a private cfg that is executed after it. The list is built from the plugin's own `config.cfg`, so it always matches this version. Nothing leaves your browser.

Every setting, including the ones that are not in `config.cfg`, is in [Configuration (convars)](convars.md).

<!--MATCHZY_SETTINGS-->

<div id="mz-cfggen" class="mz-cfggen" data-msg-header="MatchZy settings changed from the defaults" data-msg-empty="Change a setting on the left to see it here." data-msg-changedcount="{0} changed" data-msg-reset="Back to the default" data-msg-default="Default" data-msg-unset="not set" data-msg-copied="Copied!">
<div class="cfg-main">
<div class="cfg-tools">
<input id="cfg-search" type="search" placeholder="Search settings (name or text)..." autocomplete="off">
<label class="cfg-only"><input id="cfg-changed" type="checkbox"> Only changed</label>
</div>
<div id="cfg-list"></div>
</div>
<div class="cfg-result">
<div class="cfg-result__head"><strong>Your settings</strong> <span id="cfg-count" class="cfg-count"></span></div>
<div class="cfg-buttons"><button type="button" id="cfg-copy" class="md-button md-button--primary">Copy</button> <button type="button" id="cfg-download" class="md-button">Download</button> <button type="button" id="cfg-resetall" class="md-button">Reset all</button></div>
<pre class="cfg-out"><code id="cfg-out"></code></pre>
</div>
</div>
