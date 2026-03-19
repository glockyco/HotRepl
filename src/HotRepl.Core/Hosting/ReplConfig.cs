namespace HotRepl.Hosting
{
    /// <summary>
    /// Configuration for the REPL engine and its WebSocket server.
    /// All values have safe defaults; override only what you need.
    /// </summary>
    public sealed class ReplConfig
    {
        /// <summary>
        /// WebSocket listen port. Default: 18590.
        /// </summary>
        public int Port { get; set; } = 18590;

        /// <summary>
        /// Maximum wall-clock time (ms) for a single evaluation before the
        /// watchdog aborts the thread. Default: 10 000 ms (10 s).
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 10_000;

        /// <summary>
        /// Maximum character length of a serialized result value.
        /// Values exceeding this are truncated with an ellipsis marker.
        /// Default: 100 000.
        /// </summary>
        public int MaxResultLength { get; set; } = 100_000;

        /// <summary>
        /// Maximum number of elements to enumerate when serializing
        /// <see cref="System.Collections.IEnumerable"/> results.
        /// Default: 100.
        /// </summary>
        public int MaxEnumerableElements { get; set; } = 100;
    }
}
