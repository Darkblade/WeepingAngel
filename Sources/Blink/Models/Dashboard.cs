using System;
using System.Text.Json.Serialization;

namespace Blink.Models
{
    /// <summary>
    /// Dashboard
    /// </summary>
    public class Dashboard
    {
        [JsonPropertyName("networks")]
        public System.Text.Json.JsonElement[] Networks {get;set;} = Array.Empty<System.Text.Json.JsonElement>();
        /// <summary>Camera summaries supplied by the account homescreen.</summary>
        [JsonPropertyName("cameras")]
        public System.Text.Json.JsonElement[] Cameras { get; set; } = Array.Empty<System.Text.Json.JsonElement>();
        /// <summary>
        /// Sync modules of the Blink system
        /// </summary>
        [JsonPropertyName("sync_modules")]
        public SyncModule[] SyncModules { get; set; } = Array.Empty<SyncModule>();
    }
}
