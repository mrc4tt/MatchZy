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
                if (string.IsNullOrEmpty(matchConfig.RemoteLogURL))
                    return;

                // Use the event's own MatchId for logging (not the field, which may have been reset by now)
                long eventMatchId = @event is MatchZyMatchEvent matchEvent
                    ? matchEvent.MatchId
                    : liveMatchId;
                int eventMapNumber = @event is MatchZyMapEvent mapEvent
                    ? mapEvent.MapNumber
                    : matchConfig.CurrentMapNumber;

                // Never send events with an invalid matchId
                if (eventMatchId == -1)
                {
                    //Log($"[SendEventAsync] Skipping event {@event.EventName} — matchId is -1 (match not initialized or already reset)");
                    return;
                }

                //Log($"[SendEventAsync] Sending Event: {@event.EventName} for matchId: {eventMatchId} mapNumber: {eventMapNumber} on {matchConfig.RemoteLogURL}");

                using var httpClient = new HttpClient();
                using var jsonContent = new StringContent(
                    JsonSerializer.Serialize(@event, @event.GetType()),
                    Encoding.UTF8,
                    "application/json"
                );

                string jsonString = await jsonContent.ReadAsStringAsync();

                if (
                    !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderKey)
                    && !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue)
                )
                {
                    httpClient.DefaultRequestHeaders.Add(
                        matchConfig.RemoteLogHeaderKey,
                        matchConfig.RemoteLogHeaderValue
                    );
                }

                // Add authentication header if configured
                if (
                    !string.IsNullOrEmpty(matchConfig.RemoteLogAuthKey)
                    && !string.IsNullOrEmpty(matchConfig.RemoteLogAuthValue)
                )
                {
                    httpClient.DefaultRequestHeaders.Add(
                        matchConfig.RemoteLogAuthKey,
                        matchConfig.RemoteLogAuthValue
                    );
                }

                var httpResponseMessage = await httpClient.PostAsync(
                    matchConfig.RemoteLogURL,
                    jsonContent
                );

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    //Log($"[SendEventAsync] Sending {@event.EventName} for matchId: {eventMatchId} mapNumber: {eventMapNumber} successful with status code: {httpResponseMessage.StatusCode}");
                }
                else
                {
                    //Log($"[SendEventAsync] Sending {@event.EventName} for matchId: {eventMatchId} mapNumber: {eventMapNumber} failed with status code: {httpResponseMessage.StatusCode}, ResponseContent: {await httpResponseMessage.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception e)
            {
                Log($"[SendEventAsync FATAL] An error occurred: {e.Message}");
            }
        }
    }
}
