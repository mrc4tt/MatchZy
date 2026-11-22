using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    public partial class MatchZy
    {
        // The server's own remote log settings (config.cfg / console). A match config's "cvars" block
        // only changes matchConfig's copy, for that match; a new or reset match starts again from
        // these, so one match's URL and auth header no longer carry over to the next match.
        private string defaultRemoteLogURL = "";
        private string defaultRemoteLogHeaderKey = "";
        private string defaultRemoteLogHeaderValue = "";
        private string defaultRemoteLogAuthKey = "";
        private string defaultRemoteLogAuthValue = "";

        private static readonly HashSet<string> RemoteLogCvars = new(StringComparer.OrdinalIgnoreCase)
        {
            "matchzy_remote_log_url", "get5_remote_log_url",
            "matchzy_remote_log_header_key", "get5_remote_log_header_key",
            "matchzy_remote_log_header_value", "get5_remote_log_header_value",
            "matchzy_remote_log_auth_key", "matchzy_remote_log_auth_value",
        };

        // Copies the server's remote log settings into a fresh match config.
        private void ApplyDefaultRemoteLog(MatchConfig config)
        {
            config.RemoteLogURL = defaultRemoteLogURL;
            config.RemoteLogHeaderKey = defaultRemoteLogHeaderKey;
            config.RemoteLogHeaderValue = defaultRemoteLogHeaderValue;
            config.RemoteLogAuthKey = defaultRemoteLogAuthKey;
            config.RemoteLogAuthValue = defaultRemoteLogAuthValue;
        }

        // A remote log setting from a match config's "cvars": applied to this match only.
        private void ApplyMatchRemoteLogCvar(string name, string value)
        {
            string v = value.Trim().Trim('"').Trim();
            if (v == "")
                return;
            string key = name.ToLowerInvariant().Replace("get5_", "matchzy_");
            switch (key)
            {
                case "matchzy_remote_log_url":
                    if (IsValidUrl(v)) matchConfig.RemoteLogURL = v;
                    else Log($"[MatchConfigCvars] Invalid {name} {RedactUrl(v)}; ignored.");
                    break;
                case "matchzy_remote_log_header_key": matchConfig.RemoteLogHeaderKey = v; break;
                case "matchzy_remote_log_header_value": matchConfig.RemoteLogHeaderValue = v; break;
                case "matchzy_remote_log_auth_key": matchConfig.RemoteLogAuthKey = v; break;
                case "matchzy_remote_log_auth_value": matchConfig.RemoteLogAuthValue = v; break;
            }
        }

        // Scheme, host and port of a URL in lower case; "" when it is not an absolute URL.
        private static string UrlOrigin(string? url)
        {
            string v = (url ?? "").Trim().Trim('"').Trim();
            if (!Uri.TryCreate(v, UriKind.Absolute, out Uri? uri))
                return "";
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}".ToLowerInvariant();
        }

        // True when the match config's "cvars" set one of these URL settings to another host than
        // the server's own URL. A URL that cannot be read counts as another host; a value the
        // setting would not take (empty, not http/https) is not a redirect.
        private bool MatchCvarsRedirectUrl(string serverUrl, params string[] urlNames)
        {
            string serverOrigin = UrlOrigin(serverUrl);
            foreach (var cvar in matchConfig.ChangedCvars)
            {
                if (!Array.Exists(urlNames, n => n.Equals(cvar.Key, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string v = (cvar.Value ?? "").Trim().Trim('"').Trim();
                if (!IsValidUrl(v) || !IsAllowedMatchCvar(cvar.Key, cvar.Value ?? "", out _))
                    continue;
                string origin = UrlOrigin(v);
                if (origin == "" || origin != serverOrigin)
                    return true;
            }
            return false;
        }

        // True when the match config's "cvars" bring their own (applied, non-empty) value for one of these names.
        private bool MatchCvarsSupply(params string[] names)
        {
            foreach (var cvar in matchConfig.ChangedCvars)
            {
                if (!Array.Exists(names, n => n.Equals(cvar.Key, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if ((cvar.Value ?? "").Trim().Trim('"').Trim() != "" && IsAllowedMatchCvar(cvar.Key, cvar.Value ?? "", out _))
                    return true;
            }
            return false;
        }

        // The server's value of a setting from before the match config's "cvars" changed it, or null.
        private string? OriginalMatchCvar(params string[] names)
        {
            foreach (var cvar in matchConfig.OriginalCvars)
            {
                if (Array.Exists(names, n => n.Equals(cvar.Key, StringComparison.OrdinalIgnoreCase)))
                    return cvar.Value;
            }
            return null;
        }

        /// <summary>
        /// Runs after the whole "cvars" block of a match config was applied (so the order of URL and
        /// header inside the block does not matter). The server's own header / auth values are
        /// credentials for the server's own URL: when the match config points a URL at another host
        /// and does not bring its own header value, the inherited value is cleared for this match
        /// instead of being sent to that host. A panel that sends its URL together with its own
        /// header is not affected, and neither is a URL on the same host.
        /// Remote log: only matchConfig's copy is cleared; the server defaults return with the next
        /// or reset match (ApplyDefaultRemoteLog). Demo and backup upload: the server's value is kept
        /// in OriginalCvars, so ResetChangedConvars puts it back with the URL.
        /// </summary>
        private void DropInheritedCredentialsForRedirectedUrls()
        {
            if (matchConfig.ChangedCvars.Count == 0)
                return;

            if (MatchCvarsRedirectUrl(defaultRemoteLogURL, "matchzy_remote_log_url", "get5_remote_log_url"))
            {
                bool cleared = false;
                if (!string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue)
                    && !MatchCvarsSupply("matchzy_remote_log_header_value", "get5_remote_log_header_value"))
                {
                    matchConfig.RemoteLogHeaderValue = "";
                    cleared = true;
                }
                if (!string.IsNullOrEmpty(matchConfig.RemoteLogAuthValue) && !MatchCvarsSupply("matchzy_remote_log_auth_value"))
                {
                    matchConfig.RemoteLogAuthValue = "";
                    cleared = true;
                }
                if (cleared)
                    Log("[MatchConfigCvars] The match config sends remote log events to another host without its own header value: the server's remote log header/auth value is not used for this match.");
            }

            // The URL commands only run from the command buffer after this, so the server's URL is
            // the one remembered when the match config was read (the current one as a fallback).
            string serverDemoUrl = OriginalMatchCvar("matchzy_demo_upload_url", "get5_demo_upload_url") ?? demoUploadURL;
            if (demoUploadHeaderValue != ""
                && MatchCvarsRedirectUrl(serverDemoUrl, "matchzy_demo_upload_url", "get5_demo_upload_url")
                && !MatchCvarsSupply("matchzy_demo_upload_header_value", "get5_demo_upload_header_value"))
            {
                if (OriginalMatchCvar("matchzy_demo_upload_header_value", "get5_demo_upload_header_value") == null)
                    matchConfig.OriginalCvars["matchzy_demo_upload_header_value"] = demoUploadHeaderValue;
                demoUploadHeaderValue = "";
                Log("[MatchConfigCvars] The match config uploads demos to another host without its own header value: the server's demo upload header value is not used for this match.");
            }

            string serverBackupUrl = OriginalMatchCvar("matchzy_remote_backup_url", "get5_remote_backup_url") ?? backupUploadURL;
            if (backupUploadHeaderValue != ""
                && MatchCvarsRedirectUrl(serverBackupUrl, "matchzy_remote_backup_url", "get5_remote_backup_url")
                && !MatchCvarsSupply("matchzy_remote_backup_header_value", "get5_remote_backup_header_value"))
            {
                if (OriginalMatchCvar("matchzy_remote_backup_header_value", "get5_remote_backup_header_value") == null)
                    matchConfig.OriginalCvars["matchzy_remote_backup_header_value"] = backupUploadHeaderValue;
                backupUploadHeaderValue = "";
                Log("[MatchConfigCvars] The match config uploads round backups to another host without its own header value: the server's remote backup header value is not used for this match.");
            }
        }

        // These are console commands, not ConVars, so the engine cannot print their value. Without an
        // argument they report the current setting; an explicit empty value (matchzy_remote_log_url "")
        // clears it. Typing the bare name to check a setting therefore never wipes it.
        private static bool IsRemoteLogQuery(CommandInfo command) => command.ArgCount < 2;

        private static string MaskSecret(string value) => value == "" ? "(not set)" : $"(set, {value.Length} characters)";

        [ConsoleCommand("get5_remote_log_url", "If defined, all events are sent to this URL over HTTP. If no protocol is provided")]
        [ConsoleCommand("matchzy_remote_log_url", "If defined, all events are sent to this URL over HTTP. If no protocol is provided")]
        public void RemoteLogURLCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (IsRemoteLogQuery(command))
            {
                command.ReplyToCommand($"matchzy_remote_log_url = \"{RedactUrl(matchConfig.RemoteLogURL)}\"");
                return;
            }
            string url = command.ArgByIndex(1).Trim();

            // An empty value stops sending events. It used to fail the URL check and be ignored, and
            // the server default is copied into every new match, so a server kept posting events to
            // the previous match's webhook however often it was cleared.
            if (url != "" && !IsValidUrl(url))
            {
                Log($"[RemoteLogURLCommand] Invalid URL: {RedactUrl(url)}; ignored.");
                return;
            }

            // Console / config.cfg: the server's own setting, and the current match's.
            matchConfig.RemoteLogURL = url;
            defaultRemoteLogURL = url;
        }

        [ConsoleCommand("get5_remote_log_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for events")]
        [ConsoleCommand("matchzy_remote_log_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for events")]
        public void RemoteLogHeaderKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (IsRemoteLogQuery(command))
            {
                command.ReplyToCommand($"matchzy_remote_log_header_key = \"{matchConfig.RemoteLogHeaderKey}\"");
                return;
            }
            string header = command.ArgByIndex(1).Trim();
            matchConfig.RemoteLogHeaderKey = header;
            defaultRemoteLogHeaderKey = header;
        }

        [ConsoleCommand("get5_remote_log_header_value", "If defined, the value of the custom header added to the events sent over HTTP")]
        [ConsoleCommand("matchzy_remote_log_header_value", "If defined, the value of the custom header added to the events sent over HTTP")]
        public void RemoteLogHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (IsRemoteLogQuery(command))
            {
                command.ReplyToCommand($"matchzy_remote_log_header_value = {MaskSecret(matchConfig.RemoteLogHeaderValue)}");
                return;
            }
            string headerValue = command.ArgByIndex(1).Trim();
            matchConfig.RemoteLogHeaderValue = headerValue;
            defaultRemoteLogHeaderValue = headerValue;
        }

        [ConsoleCommand("matchzy_remote_log_auth_key", "If defined, an authentication header with this name is added to HTTP requests")]
        public void RemoteLogAuthKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (IsRemoteLogQuery(command))
            {
                command.ReplyToCommand($"matchzy_remote_log_auth_key = \"{matchConfig.RemoteLogAuthKey}\"");
                return;
            }
            string header = command.ArgByIndex(1).Trim();
            matchConfig.RemoteLogAuthKey = header;
            defaultRemoteLogAuthKey = header;
        }

        [ConsoleCommand("matchzy_remote_log_auth_value", "If defined, the value of the authentication header added to HTTP requests")]
        public void RemoteLogAuthValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            if (IsRemoteLogQuery(command))
            {
                command.ReplyToCommand($"matchzy_remote_log_auth_value = {MaskSecret(matchConfig.RemoteLogAuthValue)}");
                return;
            }
            string headerValue = command.ArgByIndex(1).Trim();
            matchConfig.RemoteLogAuthValue = headerValue;
            defaultRemoteLogAuthValue = headerValue;
        }
    }
}
