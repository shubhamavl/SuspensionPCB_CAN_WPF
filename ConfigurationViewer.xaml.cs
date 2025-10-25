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
        private readonly LinearCalibration? _leftCalibration;
        private readonly LinearCalibration? _rightCalibration;
        private readonly TareManager _tareManager;

        public ConfigurationViewer()
        {
            InitializeComponent();
            
            _settingsManager = SettingsManager.Instance;
            _leftCalibration = LinearCalibration.LoadFromFile("Left");
            _rightCalibration = LinearCalibration.LoadFromFile("Right");
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
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SuspensionSystem",
                "settings.json"
            );

            SettingsFileLocation.Text = settingsPath;
            SettingsComPort.Text = settings.ComPort;
            SettingsTransmissionRate.Text = GetRateText(settings.TransmissionRate);
            SettingsSaveDirectory.Text = settings.SaveDirectory;
            SettingsAdcMode.Text = settings.LastKnownADCMode == 0 ? "Internal ADC" : "ADS1115";
            SettingsLastSaved.Text = settings.LastSaved.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void LoadCalibrationData()
        {
            // Left Calibration
            var leftCalPath = Path.Combine(_settingsManager.Settings.SaveDirectory, "calibration_left.json");
            LeftCalFileLocation.Text = leftCalPath;
            
            if (_leftCalibration != null && _leftCalibration.IsValid)
            {
                LeftCalStatus.Text = "✓ Valid";
                LeftCalSlope.Text = _leftCalibration.Slope.ToString("F6");
                LeftCalIntercept.Text = _leftCalibration.Intercept.ToString("F6");
                LeftCalZeroPoint.Text = _leftCalibration.Point1.RawADC.ToString();
                LeftCalKnownWeight.Text = _leftCalibration.Point2.RawADC.ToString();
                LeftCalCalibrated.Text = _leftCalibration.CalibrationDate.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                LeftCalStatus.Text = "⚠ Not Calibrated";
                LeftCalSlope.Text = "N/A";
                LeftCalIntercept.Text = "N/A";
                LeftCalZeroPoint.Text = "N/A";
                LeftCalKnownWeight.Text = "N/A";
                LeftCalCalibrated.Text = "N/A";
            }

            // Right Calibration
            var rightCalPath = Path.Combine(_settingsManager.Settings.SaveDirectory, "calibration_right.json");
            RightCalFileLocation.Text = rightCalPath;
            
            if (_rightCalibration != null && _rightCalibration.IsValid)
            {
                RightCalStatus.Text = "✓ Valid";
                RightCalSlope.Text = _rightCalibration.Slope.ToString("F6");
                RightCalIntercept.Text = _rightCalibration.Intercept.ToString("F6");
                RightCalZeroPoint.Text = _rightCalibration.Point1.RawADC.ToString();
                RightCalKnownWeight.Text = _rightCalibration.Point2.RawADC.ToString();
                RightCalCalibrated.Text = _rightCalibration.CalibrationDate.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                RightCalStatus.Text = "⚠ Not Calibrated";
                RightCalSlope.Text = "N/A";
                RightCalIntercept.Text = "N/A";
                RightCalZeroPoint.Text = "N/A";
                RightCalKnownWeight.Text = "N/A";
                RightCalCalibrated.Text = "N/A";
            }
        }

        private void LoadTareData()
        {
            var tarePath = Path.Combine(_settingsManager.Settings.SaveDirectory, "tare_config.json");
            TareFileLocation.Text = tarePath;
            
            TareLeftStatus.Text = _tareManager.LeftIsTared ? "✓ Tared" : "⚠ Not Tared";
            TareLeftBaseline.Text = _tareManager.LeftBaselineKg.ToString("F3") + " kg";
            TareRightStatus.Text = _tareManager.RightIsTared ? "✓ Tared" : "⚠ Not Tared";
            TareRightBaseline.Text = _tareManager.RightBaselineKg.ToString("F3") + " kg";
            TareLastUpdated.Text = _tareManager.LeftTareTime.ToString("yyyy-MM-dd HH:mm:ss");
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
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SuspensionSystem",
                    "settings.json"
                );
                
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

        private void OpenLeftCalFileBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("calibration_left.json");
        }

        private void OpenRightCalFileBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("calibration_right.json");
        }

        private void OpenTareFileBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigFile("tare_config.json");
        }

        private void OpenConfigFile(string filename)
        {
            try
            {
                var filePath = Path.Combine(_settingsManager.Settings.SaveDirectory, filename);
                
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
