using System;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// Raw weight data from CAN
    /// </summary>
    public class RawWeightData
    {
        public byte Side { get; set; } // 0=Left, 1=Right
        public ushort RawADC { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Processed weight data with calibration and tare applied
    /// </summary>
    public class ProcessedWeightData
    {
        public ushort RawADC { get; set; }
        public double CalibratedWeight { get; set; }
        public double TaredWeight { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

