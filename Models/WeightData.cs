using System;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// Raw weight data from CAN
    /// Supports both Internal ADC (unsigned 0-8190) and ADS1115 (signed -65536 to +65534)
    /// </summary>
    public class RawWeightData
    {
        public byte Side { get; set; } // 0=Left, 1=Right
        public int RawADC { get; set; }  // Changed from ushort to int for ADS1115 signed support
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Processed weight data with calibration and tare applied
    /// </summary>
    public class ProcessedWeightData
    {
        public int RawADC { get; set; }  // Changed from ushort to int for ADS1115 signed support
        public double CalibratedWeight { get; set; }
        public double TaredWeight { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

