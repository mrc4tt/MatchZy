---
title: Event-explorer
new_in: 1.0.24
---

# Event-explorer

Alle events, MatchZy kan sende til `matchzy_remote_log_url`, med felter og et eksempel. Listen læses fra plugin'ets kildekode, når siden bygges, så den altid passer til denne version. Opsætning af endpointet står i guiden [Live events API](../guides/events.md), og hvad hvert event betyder, og hvornår det sendes, står i [Events](events.md).

`matchzy_events_format` vælger formatet for live scorebot-events: **Get5** (standard) eller **legacy**. Kamp- og server-events er ens i begge.

<!--MATCHZY_EVENTS-->

<div id="mz-events" class="mz-events" data-msg-all="Alle" data-msg-both="Kampforløb" data-msg-server="Server og tickets" data-msg-get5="Live (Get5-format)" data-msg-legacy="Live (legacy-format)" data-msg-badge-both="kamp" data-msg-badge-server="server" data-msg-badge-get5="Get5" data-msg-badge-legacy="legacy" data-msg-fields="{0} felter" data-msg-field="Felt" data-msg-type="Type" data-msg-note="Note" data-msg-nullable="kan være null" data-msg-example="Eksempel" data-msg-copy="Kopiér" data-msg-copied="Kopieret!" data-msg-shown="{0} events">
<div class="ev-tools">
<input id="ev-search" type="search" placeholder="Søg i events eller felter (fx steamid, pause, bomb)..." autocomplete="off">
<span id="ev-shown" class="ev-shown"></span>
</div>
<div id="ev-tabs" class="ev-tabs"></div>
<div id="ev-list"></div>
</div>
