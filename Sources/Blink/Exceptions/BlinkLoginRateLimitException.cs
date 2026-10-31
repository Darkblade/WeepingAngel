using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Blink.Exceptions
{
    public sealed class BlinkLoginRateLimitException : BlinkClientException
    {
        public DateTimeOffset RetryAtUtc { get; }
        public bool ServerSpecified { get; }
        public string TimingSource { get; internal set; } = "server retry value";
        public BlinkLoginRateLimitException(DateTimeOffset retryAtUtc, bool serverSpecified)
            : base("Blink returned HTTP 429. Login attempts are paused until the cooldown ends.")
        { RetryAtUtc=retryAtUtc; ServerSpecified=serverSpecified; }
        internal static async Task<BlinkLoginRateLimitException> FromResponseAsync(HttpResponseMessage response)
        {
            var now=DateTimeOffset.UtcNow;
            DateTimeOffset? until=null;
            string timingSource="app fallback: no valid server retry value";
            var retry=response.Headers.RetryAfter;
            if(retry?.Delta != null) { until=now+retry.Delta.Value; timingSource=$"Retry-After delta: {retry.Delta.Value.TotalSeconds} seconds"; }
            else if(retry?.Date != null) { until=now+(retry.Date.Value-(response.Headers.Date??now)); timingSource=$"Retry-After date: {retry.Date.Value:O}; server Date: {response.Headers.Date:O}"; }
            try {
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if(json.RootElement.TryGetProperty("next_time_in_secs",out var value) && value.TryGetDouble(out double seconds) && seconds>0 && seconds<31536000)
                { var bodyUntil=now.AddSeconds(seconds); if(until==null||bodyUntil>until){until=bodyUntil;timingSource=$"response next_time_in_secs: {seconds} seconds";} }
            } catch(JsonException) { }
            var result=new BlinkLoginRateLimitException(until>now ? until.Value : now.AddMinutes(10), until>now);
            result.TimingSource=until>now ? timingSource : "app fallback: no valid server retry value";
            return result;
        }
    }
    public sealed class BlinkLoginCredentialsException : BlinkClientException
    { public BlinkLoginCredentialsException() : base("Blink sign-in returned HTTP 401: the account credentials were rejected. Check the email and password.") { } }
}
