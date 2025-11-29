using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Text.Json;

namespace SuspensionPCB_CAN_WPF
{
    public partial class ConfigurationViewer : Window
    {
        private readonly SettingsManager _settingsManager;
        private readonly LinearCalibration? _leftCalibrationInternal;
        private readonly LinearCalibration? _rightCalibrationInternal;
        private readonly LinearCalibration? _leftCalibrationADS1115;
        private readonly LinearCalibration? _rightCalibrationADS1115;
        private readonly TareManager _tareManager;

        public ConfigurationViewer()
        {
            InitializeComponent();
            
            _settingsManager = SettingsManager.Instance;
            // Load calibrations for both ADC modes
            _leftCalibrationInternal = LinearCalibration.LoadFromFile("Left", 0);
            _rightCalibrationInternal = LinearCalibration.LoadFromFile("Right", 0);
            _leftCalibrationADS1115 = LinearCalibration.LoadFromFile("Left", 1);
            _rightCalibrationADS1115 = LinearCalibration.LoadFromFile("Right", 1);
            _tareManager = new TareManager();
            _tareManager.LoadFromFile();

            LoadConfigurationData();
        }

        private void LoadConfigurationData()
        {
            try
            {
                LoadApplicationSettings();
                LoadCalibrationData();
                LoadTareData();
                LoadDataDirectoryInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration data: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadApplicationSettings()
        {
            var settings = _settingsManager.Settings;
            var settingsPath = PathHelper.GetSettingsPath(); // Portable: next to executable

            SettingsFileLocation.Text = settingsPath;
            SettingsComPort.Text = settings.ComPort;
            SettingsTransmissionRate.Text = GetRateText(settings.TransmissionRate);
            SettingsSaveDirectory.Text = settings.SaveDirectory;
            SettingsAdcMode.Text = settings.LastKnownADCMode == 0 ? "Internal ADC" : "ADS1115";
            SettingsLastSaved.Text = settings.LastSaved.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void LoadCalibrationData()
        {
            // Load Internal ADC calibrations
            LoadCalibrationForMode("Left", 0, _leftCalibrationInternal, 
                LeftCalFileLocationInternal, LeftCalStatusInternal, LeftCalSlopeInternal, 
                LeftCalInterceptInternal, LeftCalZeroPointInternal, LeftCalKnownWeightInternal, 
                LeftCalCalibratedInternal);
            
            LoadCalibrationForMode("Right", 0, _rightCalibrationInternal, 
                RightCalFileLocationInternal, RightCalStatusInternal, RightCalSlopeInternal, 
                RightCalInterceptInternal, RightCalZeroPointInternal, RightCalKnownWeightInternal, 
                RightCalCalibratedInternal);
            
            // Load ADS1115 calibrations
            LoadCalibrationForMode("Left", 1, _leftCalibrationADS1115, 
                LeftCalFileLocationADS1115, LeftCalStatusADS1115, LeftCalSlopeADS1115, 
                LeftCalInterceptADS1115, LeftCalZeroPointADS1115, LeftCalKnownWeightADS1115, 
                LeftCalCalibratedADS1115);
            
            LoadCalibrationForMode("Right", 1, _rightCalibrationADS1115, 
                RightCalFileLocationADS1115, RightCalStatusADS1115, RightCalSlopeADS1115, 
                RightCalInterceptADS1115, RightCalZeroPointADS1115, RightCalKnownWeightADS1115, 
                RightCalCalibratedADS1115);
        }
        
        private void LoadCalibrationForMode(string side, byte adcMode, LinearCalibration? calibration,
            System.Windows.Controls.TextBlock fileLocation, System.Windows.Controls.TextBlock status,
            System.Windows.Controls.TextBlock slope, System.Windows.Controls.TextBlock intercept,
            System.Windows.Controls.TextBlock zeroPoint, System.Windows.Controls.TextBlock knownWeight,
            System.Windows.Controls.TextBlock calibrated)
        {
            string modeText = adcMode == 0 ? "Internal ADC" : "ADS1115";
            var calPath = PathHelper.GetCalibrationPath(side, adcMode);
            fileLocation.Text = calPath;
            
            if (calibration != null && calibration.IsValid)
            {
                status.Text = $"✓ Valid";
                slope.Text = calibration.Slope.ToString("F6");
                intercept.Text = calibration.Intercept.ToString("F6");
                
                if (calibration.Points != null && calibration.Points.Count > 0)
                {
                    var firstPoint = calibration.Points.First();
                    var lastPoint = calibration.Points.Last();
                    zeroPoint.Text = firstPoint.RawADC.ToString();
                    knownWeight.Text = lastPoint.RawADC.ToString();
                }
                else
                {
                    zeroPoint.Text = "N/A";
                    knownWeight.Text = "N/A";
                }
                calibrated.Text = calibration.CalibrationDate.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                status.Text = "⚠ Not Calibrated";
                slope.Text = "N/A";
                intercept.Text = "N/A";
                zeroPoint.Text = "N/A";
                knownWeight.Text = "N/A";
                calibrated.Text = "N/A";
            }
        }

        private void LoadTareData()
        {
            var tarePath = Path.Combine(_settingsManager.Settings.SaveDirectory, "tare_config.json");
            TareFileLocation.Text = tarePath;
            
            // Show tare status for both ADC modes
            bool leftInternalTared = _tareManager.IsTared("Left", 0);
            bool leftADS1115Tared = _tareManager.IsTared("Left", 1);
            bool rightInternalTared = _tareManager.IsTared("Right", 0);
            bool rightADS1115Tared = _tareManager.IsTared("Right", 1);
            
            // Left side: show status for both modes
            if (leftInternalTared || leftADS1115Tared)
            {
                string leftStatus = "✓ Tared (";
                if (leftInternalTared) leftStatus += "Internal";
                if (leftInternalTared && leftADS1115Tared) leftStatus += ", ";
                if (leftADS1115Tared) leftStatus += "ADS1115";
                leftStatus += ")";
                TareLeftStatus.Text = leftStatus;
                
                // Show baseline for Internal mode (or ADS1115 if Internal not tared)
                double leftBaseline = leftInternalTared 
                    ? _tareManager.GetBaselineKg("Left", 0)
                    : (leftADS1115Tared ? _tareManager.GetBaselineKg("Left", 1) : 0);
                TareLeftBaseline.Text = leftBaseline.ToString("F3") + " kg";
            }
            else
            {
                TareLeftStatus.Text = "⚠ Not Tared";
                TareLeftBaseline.Text = "0.000 kg";
            }
            
            // Right side: show status for both modes
            if (rightInternalTared || rightADS1115Tared)
            {
                string rightStatus = "✓ Tared (";
                if (rightInternalTared) rightStatus += "Internal";
                if (rightInternalTared && rightADS1115Tared) rightStatus += ", ";
                if (rightADS1115Tared) rightStatus += "ADS1115";
                rightStatus += ")";
                TareRightStatus.Text = rightStatus;
                
                // Show baseline for Internal mode (or ADS1115 if Internal not tared)
                double rightBaseline = rightInternalTared 
                    ? _tareManager.GetBaselineKg("Right", 0)
                    : (rightADS1115Tared ? _tareManager.GetBaselineKg("Right", 1) : 0);
                TareRightBaseline.Text = rightBaseline.ToString("F3") + " kg";
            }
            else
            {
                TareRightStatus.Text = "⚠ Not Tared";
                TareRightBaseline.Text = "0.000 kg";
            }
            
            // Show most recent tare time
            DateTime leftInternalTime = _tareManager.GetTareTime("Left", 0);
            DateTime leftADS1115Time = _tareManager.GetTareTime("Left", 1);
            DateTime rightInternalTime = _tareManager.GetTareTime("Right", 0);
            DateTime rightADS1115Time = _tareManager.GetTareTime("Right", 1);
            
            DateTime mostRecent = DateTime.MinValue;
            if (leftInternalTared && leftInternalTime > mostRecent) mostRecent = leftInternalTime;
            if (leftADS1115Tared && leftADS1115Time > mostRecent) mostRecent = leftADS1115Time;
            if (rightInternalTared && rightInternalTime > mostRecent) mostRecent = rightInternalTime;
            if (rightADS1115Tared && rightADS1115Time > mostRecent) mostRecent = rightADS1115Time;
            
            if (mostRecent != DateTime.MinValue)
            {
                TareLastUpdated.Text = mostRecent.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                TareLastUpdated.Text = "Never";
            }
        }

        private void LoadDataDirectoryInfo()
        {
            var dataDir = _settingsManager.Settings.SaveDirectory;
            DataDirectoryPath.Text = dataDir;
            
            try
            {
                if (Directory.Exists(dataDir))
                {
                    var csvFiles = Directory.GetFiles(dataDir, "*.csv");
                    DataCsvFiles.Text = $"{csvFiles.Length} files";
                    
                    long totalSize = 0;
                    foreach (var file in csvFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;
                    }
                    
                    DataTotalSize.Text = FormatFileSize(totalSize);
                }
                else
                {
                    DataCsvFiles.Text = "Directory not found";
                    DataTotalSize.Text = "N/A";
                }
            }
            catch (Exception ex)
            {
                DataCsvFiles.Text = $"Error: {ex.Message}";
                DataTotalSize.Text = "N/A";
            }
        }

        private string GetRateText(byte rate)
        {
            return rate switch
            {
                0x01 => "100Hz",
                0x02 => "500Hz", 
                0x03 => "1kHz",
                0x05 => "1Hz",
                _ => "Unknown"
            };
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void OpenSettingsFileBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsPath = PathHelper.GetSettingsPath(); // Portable: next to executable
                
                if (File.Exists(settingsPath))
                {
                    Process.Start("notepad.exe", settingsPath);
                }
                else
                {
                    MessageBox.Show("Settings file not found.", "File Not Found", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening settings file: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenLeftCalFileBtnInternal_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("calibration_left_internal.json");
        }

        private void OpenRightCalFileBtnInternal_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("calibration_right_internal.json");
        }
        
        private void OpenLeftCalFileBtnADS1115_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("calibration_left_ads1115.json");
        }

        private void OpenRightCalFileBtnADS1115_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("calibration_right_ads1115.json");
        }
        
        private void ResetLeftCalInternal_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration("Left", 0, "Internal ADC");
        }
        
        private void ResetRightCalInternal_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration("Right", 0, "Internal ADC");
        }
        
        private void ResetLeftCalADS1115_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration("Left", 1, "ADS1115");
        }
        
        private void ResetRightCalADS1115_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration("Right", 1, "ADS1115");
        }
        
        private void ResetCalibration(string side, byte adcMode, string modeName)
        {
            try
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the {side} side calibration for {modeName}?\n\n" +
                    "This will allow you to recalibrate this side for this ADC mode.",
                    "Confirm Reset Calibration",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    LinearCalibration.DeleteCalibration(side, adcMode);
                    MessageBox.Show(
                        $"{side} side calibration for {modeName} has been deleted.\n\n" +
                        "You can now calibrate this side again.",
                        "Calibration Reset",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    // Reload configuration data
                    LoadConfigurationData();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting calibration: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTareFileBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("tare_config.json");
        }

        private void OpenConfigFile(string filename)
        {
            try
            {
                // For calibration files, use PathHelper to get correct path
                string filePath;
                if (filename.StartsWith("calibration_"))
                {
                    // Extract side and mode from filename
                    var parts = filename.Replace(".json", "").Split('_');
                    if (parts.Length >= 3)
                    {
                        string side = parts[1]; // "left" or "right"
                        string modeStr = parts[2]; // "internal" or "ads1115"
                        byte adcMode = modeStr == "internal" ? (byte)0 : (byte)1;
                        filePath = PathHelper.GetCalibrationPath(side, adcMode);
                    }
                    else
                    {
                        filePath = Path.Combine(_settingsManager.Settings.SaveDirectory, filename);
                    }
                }
                else
                {
                    filePath = Path.Combine(_settingsManager.Settings.SaveDirectory, filename);
                }
                
                if (File.Exists(filePath))
                {
                    Process.Start("notepad.exe", filePath);
                }
                else
                {
                    MessageBox.Show($"{filename} not found.", "File Not Found", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening {filename}: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDataDirectoryBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataDir = _settingsManager.Settings.SaveDirectory;
                
                if (Directory.Exists(dataDir))
                {
                    Process.Start("explorer.exe", dataDir);
                }
                else
                {
                    MessageBox.Show("Data directory not found.", "Directory Not Found", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening data directory: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadConfigurationData();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
