using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Windows.Input;
using System.Windows.Controls;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SuspensionPCB_CAN_WPF
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer? _uiUpdateTimer;
        private DispatcherTimer? _clockTimer;

        private volatile int _totalMessages, _txMessages, _rxMessages;
        private readonly object _dataLock = new object();
        private CANService? _canService;  // Changed from USBCANManager to CANService
        private readonly object _statisticsLock = new object();

        // v0.7 Calibration and Tare functionality
        private LinearCalibration? _leftCalibration;
        private LinearCalibration? _rightCalibration;
        private TareManager _tareManager = new TareManager();
        private DataLogger _dataLogger = new DataLogger();
        
        // Current raw ADC data (from STM32)
        private int _leftRawADC = 0;
        private int _rightRawADC = 0;
        
        // Stream state tracking
        private bool _leftStreamRunning = false;
        private bool _rightStreamRunning = false;

        // Thread-safe collections for better performance
        private readonly ConcurrentQueue<CANMessageViewModel> _messageQueue = new ConcurrentQueue<CANMessageViewModel>();
        public ObservableCollection<CANMessageViewModel> Messages { get; set; } = new ObservableCollection<CANMessageViewModel>();
        public ObservableCollection<CANMessageViewModel> FilteredMessages { get; set; } = new ObservableCollection<CANMessageViewModel>();

        // Production logging
        private ProductionLogger _logger = ProductionLogger.Instance;

        // Settings and performance management
        private SettingsManager _settingsManager = SettingsManager.Instance;
        private WeightProcessor _weightProcessor = new WeightProcessor();

        // v0.7 Protocol - Only semantic IDs
        private readonly HashSet<uint> _rxMessageIds = new HashSet<uint> {
            0x200,  // Left side raw ADC data
            0x201,  // Right side raw ADC data
            0x300   // System status (on-demand)
        };

        private readonly HashSet<uint> _txMessageIds = new HashSet<uint> {
            0x040,  // Start left side streaming
            0x041,  // Start right side streaming
            0x044,  // Stop all streams
            0x030,  // Switch to Internal ADC mode
            0x031   // Switch to ADS1115 mode
        };

        // Current transmission state
        private byte _currentTransmissionRate = 3; // Default 500Hz

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            InitializeApplication();
            
            // Add keyboard shortcuts
            this.KeyDown += Window_KeyDown;
        }

        private void SettingsToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SettingsPanel.Visibility == Visibility.Visible)
                {
                    SettingsPanel.Visibility = Visibility.Collapsed;
                    SettingsToggleBtn.Content = "⚙ Settings";
                }
                else
                {
                    SettingsPanel.Visibility = Visibility.Visible;
                    SettingsToggleBtn.Content = "⚙ Hide";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Settings toggle error: {ex.Message}", "UI");
            }
        }

        private void TransmissionRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (TransmissionRateCombo?.SelectedIndex >= 0)
                {
                    _currentTransmissionRate = TransmissionRateCombo.SelectedIndex switch
                    {
                        0 => 0x01, // 100Hz
                        1 => 0x02, // 500Hz
                        2 => 0x03, // 1kHz
                        3 => 0x05, // 1Hz
                        _ => 0x03  // Default 1kHz
                    };
                    
                    // Save to settings
                    _settingsManager.SetTransmissionRate(_currentTransmissionRate, TransmissionRateCombo.SelectedIndex);
                    
                    _logger.LogInfo($"Transmission rate changed and saved: {GetRateText(_currentTransmissionRate)}", "Settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Rate selection error: {ex.Message}", "Settings");
            }
            }

        private void Window_KeyDown(object sender, KeyEventArgs e)
            {
                try
                {
                if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    RequestLeftBtn_Click(sender, new RoutedEventArgs());
                }
                else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    RequestRightBtn_Click(sender, new RoutedEventArgs());
                }
                else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    StopAll_Click(sender, new RoutedEventArgs());
                }
                else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    ResetTares_Click(sender, new RoutedEventArgs());
                }
                else if (e.Key == Key.F5)
                {
                    if (_canService?.IsConnected == false)
                    {
                        ConnectBtn_Click(sender, new RoutedEventArgs());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Keyboard shortcut error: {ex.Message}", "UI");
            }
        }

        private void StopAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use v0.7 semantic stream control to stop all streams
                bool stopped = _canService?.StopAllStreams() ?? false;
                
                // Flash TX indicator to show message was sent
                FlashTxIndicator();

                if (stopped)
                {
                    _leftStreamRunning = false;
                    _rightStreamRunning = false;
                    UpdateStreamingIndicators();
                    _logger.LogInfo("Stopped all streams", "CAN");
                    ShowInlineStatus("✓ Stop all streams command sent");
                }
                else
                {
                    _logger.LogError("Failed to stop all streams", "CAN");
                    ShowInlineStatus("✗ Failed to send stop command", true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Stop all error: {ex.Message}", "CAN");
                ShowInlineStatus($"✗ Stop Error: {ex.Message}", true);
            }
        }

        private void UpdateStreamingIndicators()
        {
            try
            {
                // Update left side indicator
                if (LeftStreamIndicator != null)
                {
                    LeftStreamIndicator.Fill = _leftStreamRunning ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
                
                if (LeftStreamStatusTxt != null)
                {
                    LeftStreamStatusTxt.Text = _leftStreamRunning ? "Active" : "Inactive";
                    LeftStreamStatusTxt.Foreground = _leftStreamRunning ? 
                        System.Windows.Media.Brushes.Green : 
                        System.Windows.Media.Brushes.Gray;
                }
                
                // Update right side indicator
                if (RightStreamIndicator != null)
                {
                    RightStreamIndicator.Fill = _rightStreamRunning ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
                
                if (RightStreamStatusTxt != null)
                {
                    RightStreamStatusTxt.Text = _rightStreamRunning ? "Active" : "Inactive";
                    RightStreamStatusTxt.Foreground = _rightStreamRunning ? 
                        System.Windows.Media.Brushes.Green : 
                        System.Windows.Media.Brushes.Gray;
                }
                
                // Update status bar
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Streaming indicator update error: {ex.Message}", "UI");
            }
        }

        private void UpdateStatusBar()
        {
            try
            {
                if (StreamStatusBar != null)
                {
                    if (_leftStreamRunning && _rightStreamRunning)
                    {
                        StreamStatusBar.Text = "Both Active";
                    }
                    else if (_leftStreamRunning)
                    {
                        StreamStatusBar.Text = "Left Active";
                    }
                    else if (_rightStreamRunning)
                    {
                        StreamStatusBar.Text = "Right Active";
                    }
                    else
                    {
                        StreamStatusBar.Text = "Idle";
                    }
                }
                
                if (MessageCountBar != null)
                {
                    MessageCountBar.Text = $"RX: {_rxMessages}";
                }
                
                if (TxCountBar != null)
                {
                    TxCountBar.Text = _txMessages.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Status bar update error: {ex.Message}", "UI");
            }
        }
        
        private void FlashTxIndicator()
        {
            try
            {
                if (TxIndicator != null)
                {
                    TxIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                    
                    // Flash back to red after 200ms
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(200);
                    timer.Tick += (s, e) =>
                    {
                        TxIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        timer.Stop();
                    };
                    timer.Start();
                }
                }
                catch (Exception ex)
                {
                _logger.LogError($"TX indicator flash error: {ex.Message}", "UI");
            }
        }
        
        private void ShowInlineStatus(string message, bool isError = false)
        {
            try
            {
                if (StatusBarText != null)
                {
                    StatusBarText.Text = message;
                    StatusBarText.Foreground = isError ? 
                        System.Windows.Media.Brushes.Red : 
                        System.Windows.Media.Brushes.White;
                    
                    // Reset to default after 3 seconds
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(3);
                    timer.Tick += (s, e) =>
                    {
                        StatusBarText.Text = "Ready | CAN v0.7 @ 250 kbps";
                        StatusBarText.Foreground = System.Windows.Media.Brushes.White;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Inline status error: {ex.Message}", "UI");
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
                _ => "1kHz"
            };
        }

        private ushort GetBaudRateValue()
        {
            return 250;
        }

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            StopAllBtn.IsEnabled = true;
            try
            {
                if (_canService == null)
                {
                    MessageBox.Show("CAN Service not initialized.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string comPort = "COM3"; // Auto-detected by CANService
                
                // Save COM port selection
                _settingsManager.SetComPort(comPort);

                bool connected = _canService.Connect(0, GetBaudRateValue());
                if (connected)
                {
                    UpdateConnectionStatus(true);
                    ResetStatistics();
                    MessageBox.Show("USB-CAN Connected Successfully.\n\nProtocol: CAN v0.7\nExpected responses:\n• 0x200 (Left side weights)\n• 0x201 (Right side weights)\n• 0x300 (System status)",
                                  "Connected", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Connection Failed. Check USB-CAN device.", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetStatistics()
        {
            lock (_statisticsLock)
            {
                _totalMessages = 0;
                _txMessages = 0;
                _rxMessages = 0;
            }
        }
        
        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            try
            {
                // This method is kept for compatibility but WeightProcessor handles the data now
                Interlocked.Increment(ref _rxMessages);
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Raw data received error: {ex.Message}", "CAN");
            }
        }

        private void OnCANMessageReceived(CANMessage message)
        {
            try
            {
                if (message == null) return;

                // REMOVED: Verbose logging for 1kHz performance - too slow!
                // _logger.LogInfo($"Processing CAN message: ID=0x{message.ID:X3}, Data={BitConverter.ToString(message.Data)}", "CAN");

                var vm = new CANMessageViewModel(message, _rxMessageIds, _txMessageIds);
                _messageQueue.Enqueue(vm);

                lock (_statisticsLock)
                {
                    _totalMessages++;
                    if (vm.Direction == "RX") _rxMessages++;
                    else if (vm.Direction == "TX") _txMessages++;
                }

                // Process weight and calibration data according to protocol v0.7
                lock (_dataLock)
                {
                    ProcessProtocolMessage(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"CAN message received error: {ex.Message}", "CAN");
            }
        }
        
        private void ProcessProtocolMessage(CANMessage message)
        {
            switch (message.ID)
            {
                case 0x200: // Left Side Raw ADC Data
                    if (message.Data?.Length >= 2)
                    {
                        _leftRawADC = (ushort)(message.Data[0] | (message.Data[1] << 8));
                        // REMOVED: Verbose logging for 1kHz performance
                        // _logger.LogInfo($"Left side raw ADC: {_leftRawADC}", "CAN");
                        
                        // Confirm left stream is active
                        if (!_leftStreamRunning)
                        {
                            _leftStreamRunning = true;
                            UpdateStreamingIndicators();
                            _logger.LogInfo("Left stream confirmed active via 0x200", "CAN");
                        }
                        
                        // Send to WeightProcessor for calibration
                        _weightProcessor.EnqueueRawData(0, (ushort)_leftRawADC);
                    }
                    break;

                case 0x201: // Right Side Raw ADC Data
                    if (message.Data?.Length >= 2)
                    {
                        _rightRawADC = (ushort)(message.Data[0] | (message.Data[1] << 8));
                        // REMOVED: Verbose logging for 1kHz performance
                        // _logger.LogInfo($"Right side raw ADC: {_rightRawADC}", "CAN");
                        
                        // Confirm right stream is active
                        if (!_rightStreamRunning)
                        {
                            _rightStreamRunning = true;
                            UpdateStreamingIndicators();
                            _logger.LogInfo("Right stream confirmed active via 0x201", "CAN");
                        }
                        
                        // Send to WeightProcessor for calibration
                        _weightProcessor.EnqueueRawData(1, (ushort)_rightRawADC);
                    }
                    break;

                case 0x300: // System Status (on-demand) - Keep logging for infrequent messages
                    if (message.Data?.Length >= 3)
                    {
                        byte systemStatus = message.Data[0];
                        byte errorFlags = message.Data[1];
                        byte adcMode = message.Data[2];
                        _logger.LogInfo($"System Status: {systemStatus}, Errors: 0x{errorFlags:X2}, ADC Mode: {adcMode}", "CAN");
                    }
                    break;
            }
        }
        
        private void ProcessPendingMessages()
        {
            int processed = 0;
            // Increased batch size from 10 to 50 for better 1kHz performance
            while (_messageQueue.TryDequeue(out var vm) && processed < 50)
            {
                Messages.Add(vm);
                processed++;
                if (Messages.Count > 1000) Messages.RemoveAt(0);
            }

            if (processed > 0) ApplyMessageFilter();
        }
        
        private void ApplyMessageFilter()
        {
            try
            {
                FilteredMessages.Clear();
                var filtered = Messages.AsEnumerable();

                foreach (var msg in filtered.TakeLast(200))
                    FilteredMessages.Add(msg);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Filter application error: {ex.Message}", "UI");
            }
        }

        private void UpdateUI()
        {
            try
            {
                // Update weight displays if UI elements exist
                UpdateWeightDisplays();
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                _logger.LogError($"UI update error: {ex.Message}", "UI");
            }
        }

        private void UpdateWeightDisplays()
        {
            try
            {
                // Read latest processed data from WeightProcessor (lock-free)
                var leftData = _weightProcessor.LatestLeft;
                var rightData = _weightProcessor.LatestRight;
                
                // Update Left Side Display
                if (LeftRawTxt != null) LeftRawTxt.Text = leftData.RawADC.ToString();
                
                bool leftCalibrated = _leftCalibration != null && _leftCalibration.IsValid;
                
                if (leftCalibrated)
                {
                    if (LeftCalibratedTxt != null) LeftCalibratedTxt.Text = $"{(int)leftData.CalibratedWeight} kg";
                    if (LeftDisplayTxt != null) LeftDisplayTxt.Text = $"{(int)leftData.TaredWeight} kg";
                }
                else
                {
                    if (LeftCalibratedTxt != null) LeftCalibratedTxt.Text = "Not Calibrated";
                    if (LeftDisplayTxt != null) LeftDisplayTxt.Text = "Calibration Required";
                }
                
                if (LeftTareStatusTxt != null) LeftTareStatusTxt.Text = _tareManager.GetTareStatusText(true);
                
                // Update Right Side Display
                if (RightRawTxt != null) RightRawTxt.Text = rightData.RawADC.ToString();
                
                bool rightCalibrated = _rightCalibration != null && _rightCalibration.IsValid;
                
                if (rightCalibrated)
                {
                    if (RightCalibratedTxt != null) RightCalibratedTxt.Text = $"{(int)rightData.CalibratedWeight} kg";
                    if (RightDisplayTxt != null) RightDisplayTxt.Text = $"{(int)rightData.TaredWeight} kg";
                }
                else
                {
                    if (RightCalibratedTxt != null) RightCalibratedTxt.Text = "Not Calibrated";
                    if (RightDisplayTxt != null) RightDisplayTxt.Text = "Calibration Required";
                }
                
                if (RightTareStatusTxt != null) RightTareStatusTxt.Text = _tareManager.GetTareStatusText(false);
                
                // Log data if logging is active
                if (_dataLogger.IsLogging)
                {
                    double leftTareBaseline = _tareManager.LeftIsTared ? _tareManager.LeftBaselineKg : 0.0;
                    double rightTareBaseline = _tareManager.RightIsTared ? _tareManager.RightBaselineKg : 0.0;
                    
                    _dataLogger.LogDataPoint("Left", leftData.RawADC, leftData.CalibratedWeight, leftData.TaredWeight, 
                                           leftTareBaseline, _leftCalibration?.Slope ?? 0, _leftCalibration?.Intercept ?? 0, 0);
                    
                    _dataLogger.LogDataPoint("Right", rightData.RawADC, rightData.CalibratedWeight, rightData.TaredWeight, 
                                           rightTareBaseline, _rightCalibration?.Slope ?? 0, _rightCalibration?.Intercept ?? 0, 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Weight display update error: {ex.Message}", "UI");
            }
        }

        private void UpdateStatistics()
        {
            try
            {
                lock (_statisticsLock)
                {
                    if (MessageCountBar != null)
                    {
                        MessageCountBar.Text = $"RX: {_rxMessages}";
                    }
                    
                    if (TxCountBar != null)
                    {
                        TxCountBar.Text = _txMessages.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Statistics update error: {ex.Message}", "UI");
            }
        }

        private void LoadCalibrations()
        {
            _leftCalibration = LinearCalibration.LoadFromFile("Left");
            _rightCalibration = LinearCalibration.LoadFromFile("Right");
            
            // Update WeightProcessor with new calibrations
            _weightProcessor.SetCalibration(_leftCalibration, _rightCalibration);
        }
        
        private void TareLeft_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_leftCalibration == null || !_leftCalibration.IsValid)
                {
                    MessageBox.Show("Please calibrate the Left side first before taring.", "Calibration Required", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                double currentCalibratedKg = _leftCalibration.RawToKg(_leftRawADC);
                _tareManager.TareLeft(currentCalibratedKg);
                _tareManager.SaveToFile();
                
                UpdateWeightDisplays();
                MessageBox.Show($"Left side tared successfully.\nBaseline: {(int)currentCalibratedKg} kg", 
                              "Tare Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error taring Left side: {ex.Message}", "Tare Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void TareRight_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_rightCalibration == null || !_rightCalibration.IsValid)
                {
                    MessageBox.Show("Please calibrate the Right side first before taring.", "Calibration Required", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                double currentCalibratedKg = _rightCalibration.RawToKg(_rightRawADC);
                _tareManager.TareRight(currentCalibratedKg);
                _tareManager.SaveToFile();
                
                UpdateWeightDisplays();
                MessageBox.Show($"Right side tared successfully.\nBaseline: {(int)currentCalibratedKg} kg", 
                              "Tare Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error taring Right side: {ex.Message}", "Tare Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ResetTares_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _tareManager.ResetBoth();
                _tareManager.SaveToFile();
                UpdateWeightDisplays();
                MessageBox.Show("All tares reset successfully.", "Reset Complete", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting tares: {ex.Message}", "Reset Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CalibrateLeft_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if left stream is already running
                if (!_leftStreamRunning)
                {
                    if (_canService?.IsConnected == true)
                    {
                        // Auto-start left stream for calibration
                        bool success = _canService.StartLeftStream(_currentTransmissionRate);
                        if (success)
                        {
                            _leftStreamRunning = true;
                            _logger.LogInfo($"Auto-started left stream for calibration at rate {_currentTransmissionRate}", "CAN");
                            
                            // Wait briefly for first data packet
                            System.Threading.Thread.Sleep(200);
                        }
                        else
                        {
                            _logger.LogError("Failed to auto-start left stream for calibration", "CAN");
                            MessageBox.Show("Failed to start left stream for calibration.", "Stream Error", 
                                          MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Please connect to CAN device first.", "Connection Required", 
                                      MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                
                // Open calibration dialog
                var calibrationDialog = new CalibrationDialog("Left");
                calibrationDialog.Owner = this;
                if (calibrationDialog.ShowDialog() == true)
                {
                    // Reload calibrations after successful calibration
                    LoadCalibrations();
                    UpdateWeightDisplays();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Calibration error: {ex.Message}", "UI");
                MessageBox.Show($"Calibration error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CalibrateRight_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if right stream is already running
                if (!_rightStreamRunning)
                {
                    if (_canService?.IsConnected == true)
                    {
                        // Auto-start right stream for calibration
                        bool success = _canService.StartRightStream(_currentTransmissionRate);
                        if (success)
                        {
                            _rightStreamRunning = true;
                            _logger.LogInfo($"Auto-started right stream for calibration at rate {_currentTransmissionRate}", "CAN");
                            
                            // Wait briefly for first data packet
                            System.Threading.Thread.Sleep(200);
                        }
                        else
                        {
                            _logger.LogError("Failed to auto-start right stream for calibration", "CAN");
                            MessageBox.Show("Failed to start right stream for calibration.", "Stream Error", 
                                          MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Please connect to CAN device first.", "Connection Required", 
                                      MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                
                // Open calibration dialog
                var calibrationDialog = new CalibrationDialog("Right");
                calibrationDialog.Owner = this;
                if (calibrationDialog.ShowDialog() == true)
                {
                    // Reload calibrations after successful calibration
                    LoadCalibrations();
                    UpdateWeightDisplays();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Calibration error: {ex.Message}", "UI");
                MessageBox.Show($"Calibration error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void RequestLeftBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService?.IsConnected != true)
                {
                    ShowInlineStatus("✗ CAN service not connected", true);
                    return;
                }

                // Check if already streaming
                if (_leftStreamRunning)
                {
                    ShowInlineStatus($"⚠ Left side already streaming at {GetRateText(_currentTransmissionRate)}", true);
                    _logger.LogWarning("Attempted to start left stream while already running", "CAN");
                    return;
                }

                // If right is streaming, auto-stop it first (STM32 single-side limitation)
                if (_rightStreamRunning)
                {
                    _canService?.StopAllStreams();
                    _rightStreamRunning = false;
                    _logger.LogInfo("Auto-stopped right stream to start left", "CAN");
                }

                bool success = _canService?.StartLeftStream(_currentTransmissionRate) ?? false;
                FlashTxIndicator();
                
                if (success)
                {
                    _leftStreamRunning = true;
                    UpdateStreamingIndicators();
                    _logger.LogInfo($"Started left side streaming at rate {_currentTransmissionRate}", "CAN");
                    ShowInlineStatus($"✓ Left stream started at {GetRateText(_currentTransmissionRate)}");
                }
                else
                {
                    _leftStreamRunning = false;
                    _logger.LogError("Failed to start left side streaming", "CAN");
                    ShowInlineStatus("✗ Failed to start left side streaming", true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Left stream start error: {ex.Message}", "CAN");
                ShowInlineStatus($"✗ Stream Error: {ex.Message}", true);
            }
        }

        private void RequestRightBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService?.IsConnected != true)
                {
                    ShowInlineStatus("✗ CAN service not connected", true);
                    return;
                }

                // Check if already streaming
                if (_rightStreamRunning)
                {
                    ShowInlineStatus($"⚠ Right side already streaming at {GetRateText(_currentTransmissionRate)}", true);
                    _logger.LogWarning("Attempted to start right stream while already running", "CAN");
                    return;
                }

                // If left is streaming, auto-stop it first (STM32 single-side limitation)
                if (_leftStreamRunning)
                {
                    _canService?.StopAllStreams();
                    _leftStreamRunning = false;
                    _logger.LogInfo("Auto-stopped left stream to start right", "CAN");
                }

                bool success = _canService?.StartRightStream(_currentTransmissionRate) ?? false;
                FlashTxIndicator();
                
                if (success)
                {
                    _rightStreamRunning = true;
                    UpdateStreamingIndicators();
                    _logger.LogInfo($"Started right side streaming at rate {_currentTransmissionRate}", "CAN");
                    ShowInlineStatus($"✓ Right stream started at {GetRateText(_currentTransmissionRate)}");
                }
                else
                {
                    _rightStreamRunning = false;
                    _logger.LogError("Failed to start right side streaming", "CAN");
                    ShowInlineStatus("✗ Failed to start right side streaming", true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Right stream start error: {ex.Message}", "CAN");
                ShowInlineStatus($"✗ Stream Error: {ex.Message}", true);
            }
        }

        private void InitializeApplication()
        {
            // Load settings first
            _settingsManager.LoadSettings();
            
            // Apply transmission rate from settings
            _currentTransmissionRate = _settingsManager.Settings.TransmissionRate;
            if (TransmissionRateCombo != null)
            {
                TransmissionRateCombo.SelectedIndex = _settingsManager.Settings.TransmissionRateIndex;
            }
            
            // Apply COM port from settings
            // Note: COM port is auto-detected by CANService, no UI control needed
            
            _logger.LogInfo($"Settings loaded: COM={_settingsManager.Settings.ComPort}, Rate={GetRateText(_currentTransmissionRate)}", "Settings");
            
            // Load existing calibrations and tare settings
            LoadCalibrations();
            _tareManager.LoadFromFile();

            // Initialize WeightProcessor
            _weightProcessor.SetCalibration(_leftCalibration, _rightCalibration);
            _weightProcessor.SetTareManager(_tareManager);
            _weightProcessor.Start();

            try
            {
                _canService = new CANService();
                _canService.MessageReceived += OnCANMessageReceived;
                _canService.RawDataReceived += OnRawDataReceived;
                _logger.LogInfo("CAN Service initialized successfully", "CANService");
            }
            catch (Exception ex)
            {
                _logger.LogError($"CAN Service initialization error: {ex.Message}", "CANService");
                MessageBox.Show($"CAN Service initialization failed: {ex.Message}",
                               "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // UI update timer at 50ms intervals (20Hz) for better performance
            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _uiUpdateTimer.Tick += (s, e) =>
            {
                try
                {
                    UpdateUI();
                    ProcessPendingMessages();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"UI Update error: {ex.Message}", "UI");
                }
            };
            _uiUpdateTimer.Start();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();

            // Initialize settings panel
            if (TransmissionRateCombo != null)
            {
                TransmissionRateCombo.SelectionChanged += TransmissionRateCombo_SelectionChanged;
            }

            // Initialize save directory UI
            try
            {
                if (SaveDirectoryTxt != null)
                {
                    SaveDirectoryTxt.Text = _settingsManager.Settings.SaveDirectory;
                }
            }
            catch { }

            // Initialize streaming indicators
            UpdateStreamingIndicators();
        }

        private void BrowseSaveDirBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select folder to store logs and configs",
                    InitialDirectory = _settingsManager.Settings.SaveDirectory
                };
                
                var result = dialog.ShowDialog();
                if (result == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    _settingsManager.SetSaveDirectory(dialog.FolderName);
                    if (SaveDirectoryTxt != null)
                        SaveDirectoryTxt.Text = dialog.FolderName;
                    ShowInlineStatus($"✓ Save folder set: {dialog.FolderName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Browse folder error: {ex.Message}", "Settings");
                ShowInlineStatus("✗ Failed to set save folder", true);
            }
        }

        private void UpdateClock()
        {
            try
            {
                if (TimestampText != null)
                {
                    TimestampText.Text = DateTime.Now.ToString("HH:mm:ss");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Clock update error: {ex.Message}", "UI");
            }
        }

        private void UpdateConnectionStatus(bool connected)
        {
            try
            {
                if (ConnectBtn != null)
                {
                    ConnectBtn.Content = connected ? "Disconnect" : "Connect";
                    ConnectBtn.Background = connected ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                }
                
                // COM port is auto-detected, no UI control to enable/disable
                
                if (RequestLeftBtn != null)
                {
                    RequestLeftBtn.IsEnabled = connected;
                }
                
                if (RequestRightBtn != null)
                {
                    RequestRightBtn.IsEnabled = connected;
                }
                
                if (StopAllBtn != null)
                {
                    StopAllBtn.IsEnabled = connected;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Connection status update error: {ex.Message}", "UI");
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            try
            {
                _uiUpdateTimer?.Stop();
                _clockTimer?.Stop();
                _weightProcessor?.Stop();
                _canService?.Disconnect();
                _logger.LogInfo("Application closing", "Main");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during application close: {ex.Message}", "Main");
            }
        }

        private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _canService?.Disconnect();
                UpdateConnectionStatus(false);
                _leftStreamRunning = false;
                _rightStreamRunning = false;
                UpdateStreamingIndicators();
                _logger.LogInfo("Disconnected from CAN device", "CAN");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Disconnect error: {ex.Message}", "CAN");
            }
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Messages.Clear();
                FilteredMessages.Clear();
                _logger.LogInfo("Cleared message log", "UI");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Clear log error: {ex.Message}", "UI");
            }
        }
        
        private void StartLogging_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dataLogger.StartLogging())
                {
                    StartLoggingBtn.IsEnabled = false;
                    StopLoggingBtn.IsEnabled = true;
                    LoggingStatusTxt.Text = $"Logging to: {_dataLogger.GetLogFilePath()}";
                    LoggingStatusTxt.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    MessageBox.Show("Failed to start data logging.", "Logging Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting logging: {ex.Message}", "Logging Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void StopLogging_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _dataLogger.StopLogging();
                StartLoggingBtn.IsEnabled = true;
                StopLoggingBtn.IsEnabled = false;
                LoggingStatusTxt.Text = $"Stopped. Logged {_dataLogger.GetLogLineCount()} lines.";
                LoggingStatusTxt.Foreground = System.Windows.Media.Brushes.Orange;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping logging: {ex.Message}", "Logging Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ExportLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"suspension_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };
                
                if (saveDialog.ShowDialog() == true)
                {
                    if (_dataLogger.ExportToCSV(saveDialog.FileName))
                    {
                        MessageBox.Show($"Data exported successfully to:\n{saveDialog.FileName}", 
                                      "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to export data.", "Export Error", 
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export error: {ex.Message}", "Export Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenMonitorWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var monitorWindow = new MonitorWindow(_canService, _logger, _tareManager, _leftCalibration, _rightCalibration);
                monitorWindow.Owner = this;
                monitorWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening monitor window: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenLogsWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logsWindow = new LogsWindow(_logger);
                logsWindow.Owner = this;
                logsWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening logs window: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}