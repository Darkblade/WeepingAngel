using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Blink.Exceptions;
using Blink.Models;

namespace Blink
{
    public partial class BlinkClient
    {
        /// <summary>Read a live command using the current account-scoped commands endpoint.</summary>
        public async Task<bool> IsLiveCommandRunningAsync(int networkId, long commandId)
        {
            string url = $"/accounts/{RequireAccountId()}/networks/{networkId}/commands/{commandId}";
            var data = await GetJsonDictionaryAsync(url, "Live command polling failed");
            if (data.TryGetValue("complete", out var complete) && complete.ValueKind == JsonValueKind.True) return false;
            string status = data.TryGetValue("status", out var state) ? state.ToString() : "";
            return !new[] { "complete", "completed", "failed", "done", "cancelled" }.Contains(status.ToLowerInvariant());
        }
        private async Task<JsonElement> CameraSummaryAsync(int networkId, int cameraId)
        {
            var dashboard = await GetDashboardAsync();
            foreach (var camera in dashboard.Cameras)
                if (camera.TryGetProperty("id", out var id) && id.ToString() == cameraId.ToString()
                    && camera.TryGetProperty("network_id", out var network) && network.ToString() == networkId.ToString()) return camera;
            throw new BlinkClientException("Camera was not found on the account homescreen.");
        }

        /// <summary>Get a camera's current thumbnail, optionally requesting a new capture first.</summary>
        public async Task<byte[]> GetCameraThumbnailBytesAsync(int networkId, int cameraId, bool capture = false)
        {
            if (capture)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (!await RequestNewImageAsync(networkId, cameraId)) throw new BlinkClientException("Camera did not complete the snapshot command.");
                        break;
                    }
                    catch (BlinkClientException ex) when (attempt < 3 && ex.Message.IndexOf("System is busy", StringComparison.OrdinalIgnoreCase) >= 0)
                    { await Task.Delay(2000 * (attempt + 1)); }
                }
            }
            var camera = await CameraSummaryAsync(networkId, cameraId);
            if (!camera.TryGetProperty("thumbnail", out var thumb) || string.IsNullOrWhiteSpace(thumb.ToString()))
                throw new BlinkClientException("Camera has no thumbnail yet. Request a new snapshot.");
            string value = thumb.ToString();
            string path;
            if (long.TryParse(value, out _))
            {
                string type = camera.TryGetProperty("type", out var kind) ? kind.ToString() : camera.TryGetProperty("product_type", out kind) ? kind.ToString() : "cameras";
                path = $"/api/v3/media/accounts/{RequireAccountId()}/networks/{networkId}/{Uri.EscapeDataString(type)}/{cameraId}/thumbnail/thumbnail.jpg?ts={value}&ext=";
            }
            else path = value.Contains(".jpg") || value.Contains("?") ? value : value + ".jpg";
            var http = await GetHttpClientAsync();
            var uri = new Uri(http.BaseAddress!, path);
            if (uri.Scheme != "https" || uri.Host != http.BaseAddress!.Host) throw new BlinkClientException("Unexpected camera thumbnail host.");
            using var response = await http.GetAsync(uri);
            if (!response.IsSuccessStatusCode || !(response.Content.Headers.ContentType?.MediaType ?? "").StartsWith("image/"))
                throw new BlinkClientException($"Camera thumbnail request failed (HTTP {(int)response.StatusCode}).");
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>Negotiate a live session without waiting for the live command to finish.</summary>
        public async Task<BlinkLiveSession> OpenLiveSessionAsync(int networkId, int cameraId)
        {
            var camera = await CameraSummaryAsync(networkId, cameraId);
            if (!camera.TryGetProperty("serial", out var serial) || string.IsNullOrWhiteSpace(serial.ToString()))
                throw new BlinkClientException("Camera serial is missing from the homescreen response.");
            var http = await GetHttpClientAsync();
            string url = $"/api/v6/accounts/{RequireAccountId()}/networks/{networkId}/cameras/{cameraId}/liveview";
            string payload = "";
            for (int attempt = 0; ; attempt++)
            {
                using var response = await http.PostAsync(url, JsonContent.Create(new { intent = "liveview", motion_event_start_time = (string?)null }));
                payload = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) break;
                string detail = "";
                bool busy = false;
                try
                {
                    using var error = JsonDocument.Parse(payload);
                    string code = error.RootElement.TryGetProperty("code", out var codeValue) ? codeValue.ToString() : "unknown";
                    string message = error.RootElement.TryGetProperty("message", out var messageValue) ? messageValue.ToString() : "No message";
                    detail = $" Blink code {code}: {message.Substring(0, Math.Min(message.Length, 180))}";
                    busy = code == "307" || message.IndexOf("System is busy", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch (JsonException) { }
                if (busy && attempt < 3) { await Task.Delay(2000 * (attempt + 1)); continue; }
                throw new BlinkClientException($"Live session request failed (HTTP {(int)response.StatusCode}).{detail}");
            }
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("server", out var server) || !root.TryGetProperty("command_id", out var command)
                || !long.TryParse(command.ToString(), out long id)) throw new BlinkClientException("Live session response lacks server or command ID.");
            int interval = root.TryGetProperty("polling_interval", out var polling) && int.TryParse(polling.ToString(), out int number) ? number : 5;
            return new BlinkLiveSession { Server = server.ToString(), CommandId = id, Serial = serial.ToString(), PollingSeconds = Math.Max(1, interval) };
        }
    }
}
