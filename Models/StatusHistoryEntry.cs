using System;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// System status history entry
    /// </summary>
    public class StatusHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public byte SystemStatus { get; set; }      // 0=OK, 1=Warning, 2=Error
        public byte ErrorFlags { get; set; }        // Error flags
        public byte ADCMode { get; set; }           // 0=Internal, 1=ADS1115
        
        public string StatusText => SystemStatus switch
        {
            0 => "OK",
            1 => "Warning",
            2 => "Error",
            3 => "Critical",
            _ => "Unknown"
        };
        
        public string ModeText => ADCMode switch
        {
            0 => "Internal ADC",
            1 => "ADS1115",
            _ => "Unknown"
        };
        
        public string ErrorFlagsText => $"0x{ErrorFlags:X2}";
    }
}

