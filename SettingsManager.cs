using System;
using System.IO;
using System.Text.Json;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Application settings data structure
    /// </summary>
    public class AppSettings
    {
        public string ComPort { get; set; } = "COM3";
        public byte TransmissionRate { get; set; } = 0x03; // Default 1kHz
        public int TransmissionRateIndex { get; set; } = 2; // ComboBox index
        public string SaveDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Data");
        public DateTime LastSaved { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Centralized settings manager with JSON persistence
    /// </summary>
    public class SettingsManager
    {
        private const string SETTINGS_FILE = "app_settings.json";
        private static SettingsManager? _instance;
        private AppSettings _settings = new AppSettings();

        public static SettingsManager Instance => _instance ??= new SettingsManager();

        private SettingsManager()
        {
            LoadSettings();
        }

        public AppSettings Settings => _settings;

        /// <summary>
        /// Load settings from JSON file
        /// </summary>
        public void LoadSettings()
        {
            if (!File.Exists(SETTINGS_FILE))
            {
                _settings = new AppSettings();
                ProductionLogger.Instance.LogInfo("Settings file not found, using defaults", "Settings");
                try
                {
                    // Ensure default save directory exists
                    if (!Directory.Exists(_settings.SaveDirectory))
                        Directory.CreateDirectory(_settings.SaveDirectory);
                }
                catch { }
                return;
            }

            try
            {
                string json = File.ReadAllText(SETTINGS_FILE);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                    // Migrate older settings without SaveDirectory
                    if (string.IsNullOrWhiteSpace(_settings.SaveDirectory))
                        _settings.SaveDirectory = Path.Combine(AppContext.BaseDirectory, "Data");

                    // Ensure directory exists
                    try
                    {
                        if (!Directory.Exists(_settings.SaveDirectory))
                            Directory.CreateDirectory(_settings.SaveDirectory);
                    }
                    catch { }

                    ProductionLogger.Instance.LogInfo($"Settings loaded: COM={_settings.ComPort}, Rate={_settings.TransmissionRate}, SaveDir={_settings.SaveDirectory}", "Settings");
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to load settings: {ex.Message}", "Settings");
                _settings = new AppSettings();
            }
        }

        /// <summary>
        /// Save settings to JSON file
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                _settings.LastSaved = DateTime.Now;
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(SETTINGS_FILE, json);
                ProductionLogger.Instance.LogInfo("Settings saved successfully", "Settings");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to save settings: {ex.Message}", "Settings");
            }
        }

        /// <summary>
        /// Update COM port setting
        /// </summary>
        public void SetComPort(string comPort)
        {
            _settings.ComPort = comPort;
            SaveSettings();
        }

        /// <summary>
        /// Update transmission rate setting
        /// </summary>
        public void SetTransmissionRate(byte rate, int index)
        {
            _settings.TransmissionRate = rate;
            _settings.TransmissionRateIndex = index;
            SaveSettings();
        }

        /// <summary>
        /// Update save directory and ensure it exists
        /// </summary>
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
    }
}

