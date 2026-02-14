using System.Text;
using System.Text.Json;

namespace MatchZy
{
    public partial class MatchZy
    {
        public async Task SendEventAsync(MatchZyEvent @event)
        {
            try
            {
                if (string.IsNullOrEmpty(matchConfig.RemoteLogURL)) return;

                Log(
                    $"[SendEventAsync] Sending Event: {@event.EventName} for matchId: {liveMatchId} mapNumber: {matchConfig.CurrentMapNumber} on {matchConfig.RemoteLogURL}");

                using var httpClient = new HttpClient();
                using var jsonContent = new StringContent(JsonSerializer.Serialize(@event, @event.GetType()),
                    Encoding.UTF8, "application/json");

                string jsonString = await jsonContent.ReadAsStringAsync();

                if (!string.IsNullOrEmpty(matchConfig.RemoteLogHeaderKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue))
                {
                    httpClient.DefaultRequestHeaders.Add(matchConfig.RemoteLogHeaderKey,
                        matchConfig.RemoteLogHeaderValue);
                }

                // Add authentication header if configured
                if (!string.IsNullOrEmpty(matchConfig.RemoteLogAuthKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogAuthValue))
                {
                    httpClient.DefaultRequestHeaders.Add(matchConfig.RemoteLogAuthKey,
                        matchConfig.RemoteLogAuthValue);
                }

                var httpResponseMessage = await httpClient.PostAsync(matchConfig.RemoteLogURL, jsonContent);

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    Log(
                        $"[SendEventAsync] Sending {@event.EventName} for matchId: {liveMatchId} mapNumber: {matchConfig.CurrentMapNumber} successful with status code: {httpResponseMessage.StatusCode}");
                }
                else
                {
                    Log(
                        $"[SendEventAsync] Sending {@event.EventName} for matchId: {liveMatchId} mapNumber: {matchConfig.CurrentMapNumber} failed with status code: {httpResponseMessage.StatusCode}, ResponseContent: {await httpResponseMessage.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception e)
            {
                Log($"[SendEventAsync FATAL] An error occurred: {e.Message}");
            }
        }
    }
}
