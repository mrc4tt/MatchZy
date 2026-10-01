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

        [ConsoleCommand("get5_remote_log_url", "If defined, all events are sent to this URL over HTTP. If no protocol is provided")]
        [ConsoleCommand("matchzy_remote_log_url", "If defined, all events are sent to this URL over HTTP. If no protocol is provided")]
        public void RemoteLogURLCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            string url = command.ArgByIndex(1);

            if (!IsValidUrl(url))
            {
                //Log($"[RemoteLogURLCommand] Invalid URL: {url}. Please provide a valid URL!");
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
            string header = command.ArgByIndex(1).Trim();

            if (header != "")
            {
                matchConfig.RemoteLogHeaderKey = header;
                defaultRemoteLogHeaderKey = header;
            }
        }

        [ConsoleCommand("get5_remote_log_header_value", "If defined, the value of the custom header added to the events sent over HTTP")]
        [ConsoleCommand("matchzy_remote_log_header_value", "If defined, the value of the custom header added to the events sent over HTTP")]
        public void RemoteLogHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            string headerValue = command.ArgByIndex(1).Trim();

            if (headerValue != "")
            {
                matchConfig.RemoteLogHeaderValue = headerValue;
                defaultRemoteLogHeaderValue = headerValue;
            }
        }

        [ConsoleCommand("matchzy_remote_log_auth_key", "If defined, an authentication header with this name is added to HTTP requests")]
        public void RemoteLogAuthKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            string header = command.ArgByIndex(1).Trim();

            if (header != "")
            {
                matchConfig.RemoteLogAuthKey = header;
                defaultRemoteLogAuthKey = header;
            }
        }

        [ConsoleCommand("matchzy_remote_log_auth_value", "If defined, the value of the authentication header added to HTTP requests")]
        public void RemoteLogAuthValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            string headerValue = command.ArgByIndex(1).Trim();

            if (headerValue != "")
            {
                matchConfig.RemoteLogAuthValue = headerValue;
                defaultRemoteLogAuthValue = headerValue;
            }
        }
    }
}
