using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Single HttpClient instance - avoids socket exhaustion from per-request allocation.
        // HttpClient is thread-safe and designed to be long-lived.
        private static readonly HttpClient _sharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        public async Task SendEventAsync(MatchZyEvent @event)
        {
            try
            {
                if (string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                    return;

                long eventMatchId = @event is MatchZyMatchEvent matchEvent ? matchEvent.MatchId : liveMatchId;

                // Never send events with an invalid matchId
                if (eventMatchId == -1)
                    return;

                string json = JsonSerializer.Serialize(@event, @event.GetType());
                using var request = new HttpRequestMessage(HttpMethod.Post, matchConfig.RemoteLogURL);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(matchConfig.RemoteLogHeaderKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue))
                {
                    request.Headers.TryAddWithoutValidation(matchConfig.RemoteLogHeaderKey, matchConfig.RemoteLogHeaderValue);
                }

                if (!string.IsNullOrEmpty(matchConfig.RemoteLogAuthKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogAuthValue))
                {
                    request.Headers.TryAddWithoutValidation(matchConfig.RemoteLogAuthKey, matchConfig.RemoteLogAuthValue);
                }

                var response = await _sharedHttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[SendEventAsync] {@event.EventName} failed: {response.StatusCode}");
                }
            }
            catch (TaskCanceledException)
            {
                Log($"[SendEventAsync] Request timed out for {@event.EventName}");
            }
            catch (Exception e)
            {
                Log($"[SendEventAsync FATAL] An error occurred: {e.Message}");
            }
        }
    }
}
