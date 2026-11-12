using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MatchZy
{
    public partial class MatchZy
    {
        // matchzy_validate_match <file|url> [header name] [header value]: checks a match config
        // without loading it, so a panel or an admin can test what they send before a tournament.
        // Prints the same errors the loader would refuse it for, a short summary (teams, team size,
        // maps) and the load warnings (MatchConfigWarnings). Server console / RCON only, and it works
        // while a match is loaded (nothing is changed).
        [ConsoleCommand("matchzy_validate_match", "Checks a match config (file relative to csgo/, or URL) without loading it: matchzy_validate_match <file|url> [header name] [header value]")]
        public void OnValidateMatchCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null)
                return;
            string source = command.ArgByIndex(1);
            if (string.IsNullOrWhiteSpace(source))
            {
                ValidateReply("Usage: matchzy_validate_match <file relative to csgo/ | URL> [header name] [header value]");
                return;
            }

            if (!IsValidUrl(source))
            {
                string filePath = Path.Join(Server.GameDirectory, "csgo", source);
                if (!File.Exists(filePath))
                {
                    ValidateReply($"File not found: {filePath}");
                    return;
                }
                string text;
                try { text = File.ReadAllText(filePath); }
                catch (Exception e) { ValidateReply($"Cannot read {filePath}: {e.Message}"); return; }
                ReportMatchValidation(source, text);
                return;
            }

            string headerName = command.ArgCount > 3 ? command.ArgByIndex(2) : "";
            string headerValue = command.ArgCount > 3 ? command.ArgByIndex(3) : "";
            string shownUrl = RedactUrl(source);
            ValidateReply($"Fetching {shownUrl} ... (the result follows in the console and the log)");
            Task.Run(async () =>
            {
                string? text = null;
                string? error = null;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, source);
                    if (headerName != "")
                        request.Headers.TryAddWithoutValidation(headerName, headerValue);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    using var response = await _sharedHttpClient.SendAsync(request, timeout.Token);
                    if (response.IsSuccessStatusCode)
                        text = await response.Content.ReadAsStringAsync(timeout.Token);
                    else
                        error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                Server.NextFrame(() =>
                {
                    if (text == null)
                        ValidateReply($"Could not fetch {shownUrl}: {error}");
                    else
                        ReportMatchValidation(shownUrl, text);
                });
            });
        }

        private void ReportMatchValidation(string source, string text)
        {
            // Console/RCON handler and URL NextFrame callback: an odd value (num_maps "three") threw
            // out of the validation helpers and out of the command.
            try
            {
                ReportMatchValidationCore(source, text);
            }
            catch (Exception e)
            {
                ValidateReply($"{source}: ERROR, the config could not be checked: {e.Message}");
            }
        }

        private void ReportMatchValidationCore(string source, string text)
        {
            JObject json;
            try
            {
                json = JObject.Parse(text);
            }
            catch (JsonException e)
            {
                ValidateReply($"{source}: not valid JSON ({e.Message}).");
                return;
            }

            string error = MatchConfigLogic.ValidateMatchJson(json);
            if (error != "")
            {
                ValidateReply($"{source}: ERROR, the match would not load: {error}");
                return;
            }

            var input = MatchConfigWarnings.FromJson(json, readyPerTeam.Value);
            var warnings = MatchConfigWarnings.Collect(input);
            string Roster(IReadOnlyList<string> ids, string key) =>
                MatchConfigLogic.IsBotTeamToken(json[key]) ? "bots"
                : MatchConfigLogic.IsOpenRosterToken(json[key]?["players"]) ? "any player"
                : ids.Count == 0 ? "no roster" : $"{ids.Count} players";
            string maps = string.Join(", ", (json["maplist"] as JArray)?.Select(m => m.ToString()) ?? Enumerable.Empty<string>());

            ValidateReply($"{source}: OK, the match loads.");
            ValidateReply($"  {input.Team1Name}: {Roster(input.Team1Players, "team1")}; {input.Team2Name}: {Roster(input.Team2Players, "team2")}");
            ValidateReply($"  players_per_team {input.PlayersPerTeam}{(input.PlayersPerTeamSet ? "" : " (from the rosters)")}, num_maps {json["num_maps"]}, maplist: {maps}");
            ValidateReply($"  Ready-up: {(input.ReadyPerTeam > 0 ? $"{input.ReadyPerTeam} ready player(s) per team once the team is complete" : input.MinPlayersToReadySet ? $"min_players_to_ready {input.MinPlayersToReady} per team" : "every player on both teams")}");
            if (warnings.Count == 0)
                ValidateReply("  No warnings.");
            foreach (string warning in warnings)
                ValidateReply($"  WARNING: {warning}");
        }

        // Console and log: RCON shows the console reply of a file check; a URL check answers after
        // the fetch, which RCON no longer waits for, so it is in the log too.
        private void ValidateReply(string line)
        {
            Server.PrintToConsole($"[MatchZy validate] {line}");
            Log($"[ValidateMatch] {line}");
        }
    }
}
