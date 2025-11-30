using System;
using System.IO;
using System.Text.Json;
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Services
{
    /// <summary>
    /// Centralized settings manager with JSON persistence
    /// </summary>
    public class SettingsManager
    {
        private static SettingsManager? _instance;
        private static readonly object _lock = new object();
        private AppSettings _settings = new AppSettings();
        private readonly string _settingsPath;

        private SettingsManager()
        {
            _settingsPath = PathHelper.GetSettingsPath(); // Portable: next to executable
            
            LoadSettings();
        }

        public static SettingsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SettingsManager();
                    }
                }
                return _instance;
            }
        }

        public AppSettings Settings => _settings;

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    _settings = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to load settings: {ex.Message}", "Settings");
                _settings = new AppSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                _settings.LastSaved = DateTime.Now;
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                
                string? directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                File.WriteAllText(_settingsPath, json);
                ProductionLogger.Instance.LogInfo("Settings saved successfully", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to save settings: {ex.Message}", "Settings");
            }
        }

        public void SetComPort(string comPort)
        {
            if (string.IsNullOrWhiteSpace(comPort)) return;
            try
            {
                _settings.ComPort = comPort;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"COM port set to: {comPort}", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to set COM port: {ex.Message}", "Settings");
            }
        }

        public void SetTransmissionRate(byte rate, int index)
        {
            try
            {
                _settings.TransmissionRate = rate;
                _settings.TransmissionRateIndex = index;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"Transmission rate set to: 0x{rate:X2} (index: {index})", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to set transmission rate: {ex.Message}", "Settings");
            }
        }

        public void SetSaveDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            try
            {
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                _settings.SaveDirectory = directory;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"Save directory set to: {directory}", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to set save directory: {ex.Message}", "Settings");
            }
        }
        
        /// <summary>
        /// Update system status in settings
        /// </summary>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <param name="systemStatus">System status (0=OK, 1=Warning, 2=Error)</param>
        /// <param name="errorFlags">Error flags</param>
        public void UpdateSystemStatus(byte adcMode, byte systemStatus, byte errorFlags)
        {
            try
            {
                _settings.LastKnownADCMode = adcMode;
                _settings.LastKnownSystemStatus = systemStatus;
                _settings.LastKnownErrorFlags = errorFlags;
                _settings.LastStatusUpdate = DateTime.Now;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"System status updated: ADC={adcMode}, Status={systemStatus}, Errors=0x{errorFlags:X2}", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to update system status: {ex.Message}", "Settings");
            }
        }
        
        /// <summary>
        /// Get last known ADC mode
        /// </summary>
        /// <returns>ADC mode (0=Internal, 1=ADS1115)</returns>
        public byte GetLastKnownADCMode()
        {
            return _settings.LastKnownADCMode;
        }
        
        /// <summary>
        /// Get last known system status
        /// </summary>
        /// <returns>System status info</returns>
        public (byte adcMode, byte systemStatus, byte errorFlags, DateTime lastUpdate) GetLastKnownSystemStatus()
        {
            return (_settings.LastKnownADCMode, _settings.LastKnownSystemStatus, 
                   _settings.LastKnownErrorFlags, _settings.LastStatusUpdate);
        }
        
        /// <summary>
        /// Set filter settings
        /// </summary>
        public void SetFilterSettings(string filterType, double filterAlpha, int filterWindowSize, bool filterEnabled)
        {
            try
            {
                _settings.FilterType = filterType;
                _settings.FilterAlpha = filterAlpha;
                _settings.FilterWindowSize = filterWindowSize;
                _settings.FilterEnabled = filterEnabled;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"Filter settings saved: {filterType}, Alpha={filterAlpha}, Window={filterWindowSize}, Enabled={filterEnabled}", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to save filter settings: {ex.Message}", "Settings");
            }
        }
        
        /// <summary>
        /// Set display and performance settings
        /// </summary>
        public void SetDisplaySettings(int weightDecimals, int uiUpdateRate, int dataTimeout)
        {
            try
            {
                _settings.WeightDisplayDecimals = weightDecimals;
                _settings.UIUpdateRateMs = uiUpdateRate;
                _settings.DataTimeoutSeconds = dataTimeout;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"Display settings saved: WeightDecimals={weightDecimals}, UIUpdateRate={uiUpdateRate}ms, DataTimeout={dataTimeout}s", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to save display settings: {ex.Message}", "Settings");
            }
        }
        
        /// <summary>
        /// Set UI visibility settings
        /// </summary>
        public void SetUIVisibilitySettings(int statusBannerDuration, int messageHistoryLimit, bool showRawADC, bool showCalibratedWeight, bool showStreamingIndicators, bool showCalibrationIcons)
        {
            try
            {
                _settings.StatusBannerDurationMs = statusBannerDuration;
                _settings.MessageHistoryLimit = messageHistoryLimit;
                _settings.ShowRawADC = showRawADC;
                _settings.ShowCalibratedWeight = showCalibratedWeight;
                _settings.ShowStreamingIndicators = showStreamingIndicators;
                _settings.ShowCalibrationIcons = showCalibrationIcons;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"UI visibility settings saved: StatusBanner={statusBannerDuration}ms, MessageLimit={messageHistoryLimit}, ShowRawADC={showRawADC}, ShowCalibrated={showCalibratedWeight}, ShowIndicators={showStreamingIndicators}, ShowIcons={showCalibrationIcons}", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to save UI visibility settings: {ex.Message}", "Settings");
            }
        }
        
        /// <summary>
        /// Set advanced settings
        /// </summary>
        public void SetAdvancedSettings(int txFlashMs, string logFormat, int batchSize, int clockInterval, int calibrationDelay, bool showQualityMetrics)
        {
            try
            {
                _settings.TXIndicatorFlashMs = txFlashMs;
                _settings.LogFileFormat = logFormat;
                _settings.BatchProcessingSize = batchSize;
                _settings.ClockUpdateIntervalMs = clockInterval;
                _settings.CalibrationCaptureDelayMs = calibrationDelay;
                _settings.ShowCalibrationQualityMetrics = showQualityMetrics;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"Advanced settings saved: TXFlash={txFlashMs}ms, LogFormat={logFormat}, BatchSize={batchSize}, ClockInterval={clockInterval}ms, CalDelay={calibrationDelay}ms, ShowQuality={showQualityMetrics}", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to save advanced settings: {ex.Message}", "Settings");
            }
        }
        
        /// <summary>
        /// Set bootloader feature enable/disable
        /// </summary>
        public void SetBootloaderFeaturesEnabled(bool enabled)
        {
            try
            {
                _settings.EnableBootloaderFeatures = enabled;
                SaveSettings();
                ProductionLogger.Instance.LogInfo($"Bootloader features {(enabled ? "enabled" : "disabled")}", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to save bootloader setting: {ex.Message}", "Settings");
            }
        }
    }
}