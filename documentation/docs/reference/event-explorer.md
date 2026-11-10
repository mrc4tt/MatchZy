---
title: Event explorer
new_in: 1.0.24
---

# Event explorer

Every event MatchZy can send to `matchzy_remote_log_url`, with its fields and an example payload. The list is read from the plugin's source when the site is built, so it always matches this version. How to set up the endpoint is in the [Live events API](../guides/events.md) guide, and what each event means and when it is sent is in [Events](events.md).

`matchzy_events_format` picks the format of the live scorebot events: **Get5** (default) or **legacy**. Match flow and server events are the same in both.

<!--MATCHZY_EVENTS-->

<div id="mz-events" class="mz-events" data-msg-all="All" data-msg-both="Match flow" data-msg-server="Server and tickets" data-msg-get5="Live (Get5 format)" data-msg-legacy="Live (legacy format)" data-msg-badge-both="match" data-msg-badge-server="server" data-msg-badge-get5="Get5" data-msg-badge-legacy="legacy" data-msg-fields="{0} fields" data-msg-field="Field" data-msg-type="Type" data-msg-note="Note" data-msg-nullable="may be null" data-msg-example="Example" data-msg-copy="Copy" data-msg-copied="Copied!" data-msg-shown="{0} events">
<div class="ev-tools">
<input id="ev-search" type="search" placeholder="Search events or fields (e.g. steamid, pause, bomb)..." autocomplete="off">
<span id="ev-shown" class="ev-shown"></span>
</div>
<div id="ev-tabs" class="ev-tabs"></div>
<div id="ev-list"></div>
</div>
