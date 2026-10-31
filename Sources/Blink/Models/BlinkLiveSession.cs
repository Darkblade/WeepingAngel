namespace Blink.Models
{
    /// <summary>Private relay parameters returned by the authorized live-view request.</summary>
    public sealed class BlinkLiveSession
    {
        /// <summary>Relay URI; must not be logged or sent to browser clients.</summary>
        public string Server { get; set; } = "";
        /// <summary>Command ID used for session polling and teardown.</summary>
        public long CommandId { get; set; }
        /// <summary>Camera serial required by the relay handshake.</summary>
        public string Serial { get; set; } = "";
        /// <summary>Suggested command poll interval.</summary>
        public int PollingSeconds { get; set; } = 5;
    }
}
