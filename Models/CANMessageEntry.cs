namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// CAN message entry for monitoring display
    /// </summary>
    public class CANMessageEntry
    {
        public string Timestamp { get; set; } = "";
        public string Direction { get; set; } = "";
        public string CanId { get; set; } = "";
        public string Data { get; set; } = "";
        public string Description { get; set; } = "";
    }
}

