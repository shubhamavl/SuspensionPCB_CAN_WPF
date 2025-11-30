using System;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// Log entry for display
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string Source { get; set; } = "";
        
        public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] {Level}: {Message}";
    }
}

