using System;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// Application settings data structure
    /// </summary>
    public class AppSettings
    {
        public string ComPort { get; set; } = "COM3";
        public byte TransmissionRate { get; set; } = 0x03; // Default 1kHz
        public int TransmissionRateIndex { get; set; } = 2; // ComboBox index
        public string SaveDirectory { get; set; } = PathHelper.GetDataDirectory(); // Portable: relative to executable
        public DateTime LastSaved { get; set; } = DateTime.Now;
        
        // System status persistence
        public byte LastKnownADCMode { get; set; } = 0; // 0=Internal, 1=ADS1115
        public byte LastKnownSystemStatus { get; set; } = 0; // 0=OK, 1=Warning, 2=Error
        public byte LastKnownErrorFlags { get; set; } = 0;
        public DateTime LastStatusUpdate { get; set; } = DateTime.MinValue;
        
        // Weight Filtering Settings
        public string FilterType { get; set; } = "EMA"; // "EMA", "SMA", "None"
        public double FilterAlpha { get; set; } = 0.15; // EMA alpha (0.0-1.0)
        public int FilterWindowSize { get; set; } = 10; // SMA window size
        public bool FilterEnabled { get; set; } = true; // Enable/disable filtering
        
        // Display and Performance Settings
        public int WeightDisplayDecimals { get; set; } = 0; // 0=integer, 1=one decimal, 2=two decimals
        public int UIUpdateRateMs { get; set; } = 50; // UI refresh rate in milliseconds
        public int DataTimeoutSeconds { get; set; } = 5; // CAN data timeout in seconds
        
        // UI Visibility Settings (Medium Priority)
        public int StatusBannerDurationMs { get; set; } = 3000; // Status banner display duration
        public int MessageHistoryLimit { get; set; } = 1000; // Max messages stored in memory
        public bool ShowRawADC { get; set; } = true; // Show/hide raw ADC display
        public bool ShowCalibratedWeight { get; set; } = false; // Show calibrated weight (before tare)
        public bool ShowStreamingIndicators { get; set; } = true; // Show streaming status indicators
        public bool ShowCalibrationIcons { get; set; } = true; // Show calibration status icons
        
        // Advanced Settings (Low Priority)
        public int TXIndicatorFlashMs { get; set; } = 200; // TX indicator flash duration
        public string LogFileFormat { get; set; } = "CSV"; // Log format: "CSV", "JSON", "TXT" (future)
        public int BatchProcessingSize { get; set; } = 50; // Messages processed per batch
        public int ClockUpdateIntervalMs { get; set; } = 1000; // Clock refresh rate
        public int CalibrationCaptureDelayMs { get; set; } = 500; // Delay before capturing calibration point
        public bool ShowCalibrationQualityMetrics { get; set; } = true; // Display R² and error metrics
        
        // Calibration Averaging Settings
        public bool CalibrationAveragingEnabled { get; set; } = true; // Enable/disable multi-sample averaging
        public int CalibrationSampleCount { get; set; } = 50; // Number of samples to collect for averaging
        public int CalibrationCaptureDurationMs { get; set; } = 2000; // Duration to collect samples over (milliseconds)
        public bool CalibrationUseMedian { get; set; } = true; // Use median instead of mean (more robust to outliers)
        public bool CalibrationRemoveOutliers { get; set; } = true; // Remove outliers before averaging
        public double CalibrationOutlierThreshold { get; set; } = 2.0; // Standard deviations for outlier removal
        public double CalibrationMaxStdDev { get; set; } = 10.0; // Maximum acceptable standard deviation (warning threshold)
        
        // Calibration Mode Settings
        public string CalibrationMode { get; set; } = "Regression"; // "Regression" or "Piecewise" - global calibration mode
        
        // Bootloader Settings
        public bool EnableBootloaderFeatures { get; set; } = true; // Enable/disable all bootloader functionality
        
        // Suspension Test Settings
        public double SuspensionEfficiencyLimitLeft { get; set; } = 85.0; // Minimum efficiency % for Pass (Left side)
        public double SuspensionEfficiencyLimitRight { get; set; } = 85.0; // Minimum efficiency % for Pass (Right side)
        
        // Axle Weight Storage (for automatic loading in SuspensionGraphWindow)
        public double? LastAxleWeightLeft { get; set; } = null; // Last saved axle weight for Left side
        public double? LastAxleWeightRight { get; set; } = null; // Last saved axle weight for Right side
        public DateTime? LastAxleWeightSaveTime { get; set; } = null; // When axle weights were last saved
    }
}

