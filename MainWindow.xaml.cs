using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Data;
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
        private StatusHistoryManager _statusHistoryManager = new StatusHistoryManager(100);
        
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
            0x031,  // Switch to ADS1115 mode
            0x032   // Request system status
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
                else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    InternalADCBtn_Click(sender, e);
                    e.Handled = true;
                }
                else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    ADS1115Btn_Click(sender, e);
                    e.Handled = true;
                }
                else if (e.Key == Key.F5)
                {
                    if (_canService?.IsConnected == false)
                    {
                        ConnectionToggle_Click(sender, new RoutedEventArgs());
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

        private void ConnectionToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService == null)
                {
                    ShowInlineStatus("CAN Service not initialized.", true);
                    return;
                }

                if (ConnectionToggle != null) ConnectionToggle.IsEnabled = false;

                string comPort = "COM3"; // Auto-detected by CANService
                
                _settingsManager.SetComPort(comPort);

                bool connected = _canService.Connect(0, GetBaudRateValue());
                if (connected)
                {
                    UpdateConnectionStatus(true);
                    ResetStatistics();
                    ShowStatusBanner("✓ Connected Successfully", true);
                    ShowInlineStatus("USB-CAN Connected Successfully. Protocol: CAN v0.7", false);
                }
                else
                {
                    UpdateConnectionStatus(false);
                    ShowStatusBanner("✗ Connection Failed", false);
                    ShowInlineStatus("Connection Failed. Check USB-CAN device.", true);
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false);
                ShowStatusBanner($"✗ Connection Error: {ex.Message}", false);
                ShowInlineStatus($"Connection Error: {ex.Message}", true);
            }
            finally
            {
                if (ConnectionToggle != null) ConnectionToggle.IsEnabled = true;
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
                    if (LeftDisplayTxt != null) LeftDisplayTxt.Text = $"{(int)leftData.TaredWeight} kg";
                }
                else
                {
                    if (LeftDisplayTxt != null) LeftDisplayTxt.Text = "Calibration Required";
                }
                
                if (LeftTareStatusTxt != null) LeftTareStatusTxt.Text = _tareManager.GetTareStatusText(true);
                
                // Update Right Side Display
                if (RightRawTxt != null) RightRawTxt.Text = rightData.RawADC.ToString();
                
                bool rightCalibrated = _rightCalibration != null && _rightCalibration.IsValid;
                
                if (rightCalibrated)
                {
                    if (RightDisplayTxt != null) RightDisplayTxt.Text = $"{(int)rightData.TaredWeight} kg";
                }
                else
                {
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
                
                // Update calibration status icons
                UpdateCalibrationStatusIcons();
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
            
            // Update UI calibration status icons
            UpdateCalibrationStatusIcons();
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
            if (HeaderRateCombo != null)
            {
                HeaderRateCombo.SelectedIndex = _settingsManager.Settings.TransmissionRateIndex;
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

            // Initialize ADC mode from settings
            InitializeADCModeFromSettings();

            try
            {
                _canService = new CANService();
                _canService.MessageReceived += OnCANMessageReceived;
                _canService.RawDataReceived += OnRawDataReceived;
                _canService.SystemStatusReceived += HandleSystemStatus;
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

            // Initialize header transmission rate dropdown
            if (HeaderRateCombo != null)
            {
                HeaderRateCombo.SelectionChanged += HeaderRateCombo_SelectionChanged;
                // Set default to 1kHz (index 2)
                HeaderRateCombo.SelectedIndex = 2;
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

        private void ShowStatusBanner(string message, bool isSuccess)
        {
            try
            {
                if (StatusBanner != null && StatusBannerText != null)
                {
                    StatusBannerText.Text = message;
                    StatusBanner.Background = isSuccess 
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)) // green
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // red
                    
                    StatusBanner.Visibility = Visibility.Visible;
                    StatusBanner.Height = 40;
                    
                    // Start slide down animation
                    var slideDown = (Storyboard)FindResource("SlideDownAnimation");
                    slideDown.Begin(StatusBanner);
                    
                    // Auto-dismiss after 3 seconds
                    var timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(3);
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        var slideUp = (Storyboard)FindResource("SlideUpAnimation");
                        slideUp.Completed += (sender, args) =>
                        {
                            StatusBanner.Visibility = Visibility.Collapsed;
                        };
                        slideUp.Begin(StatusBanner);
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Status banner error: {ex.Message}", "UI");
            }
        }

        private void UpdateConnectionStatus(bool connected)
        {
            try
            {
                if (ConnectionToggle != null)
                {
                    ConnectionToggle.IsChecked = connected;
                    ConnectionToggle.Content = connected ? "🔌 Disconnect" : "🔌 Connect";
                }
                
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

                if (StatusIndicator != null)
                {
                    StatusIndicator.Fill = connected
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)) // green
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // red
                }

                if (StatusText != null)
                {
                    StatusText.Text = connected ? "Connected" : "Disconnected";
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
                if (ConnectionToggle != null) ConnectionToggle.IsEnabled = false;
                
                _canService?.Disconnect();
                UpdateConnectionStatus(false);
                _leftStreamRunning = false;
                _rightStreamRunning = false;
                UpdateStreamingIndicators();
                ShowStatusBanner("✓ Disconnected Successfully", true);
                _logger.LogInfo("Disconnected from CAN device", "CAN");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Disconnect error: {ex.Message}", "CAN");
                ShowStatusBanner($"✗ Disconnect Error: {ex.Message}", false);
            }
            finally
            {
                if (ConnectionToggle != null) ConnectionToggle.IsEnabled = true;
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
                var monitorWindow = new MonitorWindow();
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

        private void InternalADCBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService?.IsConnected != true)
                {
                    ShowInlineStatus("✗ CAN service not connected", true);
                    return;
                }

                bool success = _canService.SwitchToInternalADC();
                FlashTxIndicator();
                
                if (success)
                {
                    UpdateAdcModeIndicators("Internal", "#FF2196F3");
                    _logger.LogInfo("Switched to Internal ADC mode (12-bit)", "Mode");
                    ShowInlineStatus("✓ Switched to Internal ADC mode");
                }
                else
                {
                    _logger.LogError("Failed to switch to Internal ADC mode", "Mode");
                    ShowInlineStatus("✗ Failed to switch ADC mode", true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Internal ADC switch error: {ex.Message}", "Mode");
            }
        }

        private void ADS1115Btn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService?.IsConnected != true)
                {
                    ShowInlineStatus("✗ CAN service not connected", true);
                    return;
                }

                bool success = _canService.SwitchToADS1115();
                FlashTxIndicator();
                
                if (success)
                {
                    UpdateAdcModeIndicators("ADS1115", "#FF4CAF50");
                    _logger.LogInfo("Switched to ADS1115 mode (16-bit)", "Mode");
                    ShowInlineStatus("✓ Switched to ADS1115 mode (16-bit precision)");
                }
                else
                {
                    _logger.LogError("Failed to switch to ADS1115 mode", "Mode");
                    ShowInlineStatus("✗ Failed to switch ADC mode", true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"ADS1115 switch error: {ex.Message}", "Mode");
            }
        }

        private void UpdateAdcModeIndicators(string mode, string colorHex)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    string displayText = mode == "Internal" 
                        ? "Internal ADC (12-bit)" 
                        : "ADS1115 (16-bit)";
                    
                    // Update header ADC mode display
                    if (HeaderAdcModeTxt != null)
                        HeaderAdcModeTxt.Text = displayText;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Update mode indicators error: {ex.Message}", "UI");
            }
        }

        private void HandleSystemStatus(object? sender, SystemStatusEventArgs e)
        {
            try
            {
                string mode = e.ADCMode == 0 ? "Internal" : "ADS1115";
                string color = e.ADCMode == 0 ? "#FF2196F3" : "#FF4CAF50";
                UpdateAdcModeIndicators(mode, color);
                
                // Update data logger with system status
                _dataLogger?.UpdateSystemStatus(e.SystemStatus, e.ErrorFlags);
                
                // Update settings with system status
                SettingsManager.Instance.UpdateSystemStatus(e.ADCMode, e.SystemStatus, e.ErrorFlags);
                
                // Add to status history
                _statusHistoryManager.AddStatusEntry(e.SystemStatus, e.ErrorFlags, e.ADCMode);
            }
            catch (Exception ex)
            {
                _logger.LogError($"System status handler error: {ex.Message}", "CAN");
            }
        }

        private void InitializeADCModeFromSettings()
        {
            try
            {
                var (adcMode, systemStatus, errorFlags, lastUpdate) = SettingsManager.Instance.GetLastKnownSystemStatus();
                
                if (lastUpdate != DateTime.MinValue)
                {
                    string mode = adcMode == 0 ? "Internal" : "ADS1115";
                    string color = adcMode == 0 ? "#FF2196F3" : "#FF4CAF50";
                    UpdateAdcModeIndicators(mode, color);
                    
                    _logger.LogInfo($"Initialized ADC mode from settings: {mode} (last update: {lastUpdate:yyyy-MM-dd HH:mm:ss})", "Settings");
                }
                else
                {
                    _logger.LogInfo("No previous ADC mode found in settings, using default", "Settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize ADC mode from settings: {ex.Message}", "Settings");
            }
        }

        private void RequestStatusBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService?.IsConnected != true)
                {
                    ShowInlineStatus("✗ CAN service not connected", true);
                    return;
                }

                bool success = _canService.RequestSystemStatus();
                FlashTxIndicator();
                
                if (success)
                {
                    _logger.LogInfo("Requested system status from STM32", "Status");
                    ShowInlineStatus("✓ Status request sent to STM32");
                }
                else
                {
                    _logger.LogError("Failed to send status request", "Status");
                    ShowInlineStatus("✗ Failed to request status", true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Status request error: {ex.Message}", "Status");
                ShowInlineStatus($"✗ Status Error: {ex.Message}", true);
            }
        }

        private void StatusHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var historyWindow = new Window
                {
                    Title = "System Status History",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Title and statistics
                var titlePanel = new StackPanel { Margin = new Thickness(20, 20, 20, 10) };
                
                var title = new TextBlock
                {
                    Text = "System Status History",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                titlePanel.Children.Add(title);

                // Statistics
                var (totalEntries, okCount, warningCount, errorCount, firstEntry, lastEntry) = _statusHistoryManager.GetStatistics();
                var statsText = $"Total Entries: {totalEntries} | OK: {okCount} | Warnings: {warningCount} | Errors: {errorCount}";
                if (firstEntry.HasValue && lastEntry.HasValue)
                {
                    statsText += $" | Range: {firstEntry.Value:yyyy-MM-dd HH:mm} - {lastEntry.Value:yyyy-MM-dd HH:mm}";
                }
                
                var statsBlock = new TextBlock
                {
                    Text = statsText,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
                };
                titlePanel.Children.Add(statsBlock);

                grid.Children.Add(titlePanel);
                Grid.SetRow(titlePanel, 0);

                // Data grid
                var dataGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    IsReadOnly = true,
                    GridLinesVisibility = DataGridGridLinesVisibility.All,
                    HeadersVisibility = DataGridHeadersVisibility.All,
                    Margin = new Thickness(20, 0, 20, 0)
                };

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Timestamp",
                    Binding = new Binding("Timestamp") { StringFormat = "yyyy-MM-dd HH:mm:ss" },
                    Width = 150
                });
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Status",
                    Binding = new Binding("StatusText"),
                    Width = 80
                });
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "ADC Mode",
                    Binding = new Binding("ModeText"),
                    Width = 100
                });
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Error Flags",
                    Binding = new Binding("ErrorFlagsText"),
                    Width = 80
                });

                dataGrid.ItemsSource = _statusHistoryManager.GetAllEntries();
                grid.Children.Add(dataGrid);
                Grid.SetRow(dataGrid, 1);

                // Buttons
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 10, 20, 20)
                };

                var refreshBtn = new Button
                {
                    Content = "🔄 Refresh",
                    Width = 100,
                    Height = 30,
                    Margin = new Thickness(5)
                };
                refreshBtn.Click += (s, args) => dataGrid.ItemsSource = _statusHistoryManager.GetAllEntries();

                var clearBtn = new Button
                {
                    Content = "🗑️ Clear",
                    Width = 100,
                    Height = 30,
                    Margin = new Thickness(5)
                };
                clearBtn.Click += (s, args) =>
                {
                    _statusHistoryManager.ClearHistory();
                    dataGrid.ItemsSource = _statusHistoryManager.GetAllEntries();
                };

                var closeBtn = new Button
                {
                    Content = "Close",
                    Width = 100,
                    Height = 30,
                    Margin = new Thickness(5)
                };
                closeBtn.Click += (s, args) => historyWindow.Close();

                buttonPanel.Children.Add(refreshBtn);
                buttonPanel.Children.Add(clearBtn);
                buttonPanel.Children.Add(closeBtn);

                grid.Children.Add(buttonPanel);
                Grid.SetRow(buttonPanel, 2);

                historyWindow.Content = grid;
                historyWindow.ShowDialog();

                _logger.LogInfo("Status history dialog opened", "UI");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Status history dialog error: {ex.Message}", "UI");
                MessageBox.Show($"Error opening status history: {ex.Message}", 
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void KeyboardShortcutsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var shortcutsWindow = new Window
                {
                    Title = "Keyboard Shortcuts",
                    Width = 500,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(20)
                };

                var stackPanel = new StackPanel();

                // Title
                var title = new TextBlock
                {
                    Text = "Keyboard Shortcuts",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 15)
                };
                stackPanel.Children.Add(title);

                // Shortcuts list
                var shortcuts = new[]
                {
                    ("Connection", new[] {
                        ("Ctrl+C", "Connect to CAN bus"),
                        ("Ctrl+D", "Disconnect from CAN bus")
                    }),
                    ("Streaming Control", new[] {
                        ("Ctrl+L", "Start left side streaming"),
                        ("Ctrl+R", "Start right side streaming"),
                        ("Ctrl+S", "Stop all streams")
                    }),
                    ("ADC Mode", new[] {
                        ("Ctrl+I", "Switch to Internal ADC mode (12-bit)"),
                        ("Ctrl+A", "Switch to ADS1115 mode (16-bit)")
                    }),
                    ("Windows", new[] {
                        ("Ctrl+T", "Toggle settings panel"),
                        ("Ctrl+M", "Open monitor window"),
                        ("Ctrl+P", "Open production logs")
                    }),
                    ("Help", new[] {
                        ("F1", "Show help")
                    })
                };

                foreach (var (category, items) in shortcuts)
                {
                    // Category header
                    var categoryHeader = new TextBlock
                    {
                        Text = category,
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 10, 0, 5),
                        Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219))
                    };
                    stackPanel.Children.Add(categoryHeader);

                    // Shortcuts in category
                    foreach (var (key, description) in items)
                    {
                        var shortcutPanel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(10, 3, 0, 3)
                        };

                        var keyBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(8, 2, 8, 2),
                            Margin = new Thickness(0, 0, 10, 0),
                            MinWidth = 80
                        };

                        var keyText = new TextBlock
                        {
                            Text = key,
                            FontFamily = new FontFamily("Consolas"),
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        keyBorder.Child = keyText;

                        var descText = new TextBlock
                        {
                            Text = description,
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        shortcutPanel.Children.Add(keyBorder);
                        shortcutPanel.Children.Add(descText);
                        stackPanel.Children.Add(shortcutPanel);
                    }
                }

                // Close button
                var closeButton = new Button
                {
                    Content = "Close",
                    Width = 100,
                    Height = 30,
                    Margin = new Thickness(0, 20, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                closeButton.Click += (s, args) => shortcutsWindow.Close();
                stackPanel.Children.Add(closeButton);

                scrollViewer.Content = stackPanel;
                shortcutsWindow.Content = scrollViewer;
                shortcutsWindow.ShowDialog();

                _logger.LogInfo("Keyboard shortcuts dialog opened", "UI");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Keyboard shortcuts dialog error: {ex.Message}", "UI");
                MessageBox.Show($"Error opening shortcuts dialog: {ex.Message}", 
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region UI Helper Methods

        /// <summary>
        /// Updates calibration status icons for both sides
        /// </summary>
        private void UpdateCalibrationStatusIcons()
        {
            try
            {
                // Left side
                if (_leftCalibration != null && _leftCalibration.IsValid)
                {
                    LeftCalStatusIcon.Text = "✓";
                    LeftCalStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Green
                }
                else
                {
                    LeftCalStatusIcon.Text = "⚠";
                    LeftCalStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red
                }
                
                // Right side
                if (_rightCalibration != null && _rightCalibration.IsValid)
                {
                    RightCalStatusIcon.Text = "✓";
                    RightCalStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                }
                else
                {
                    RightCalStatusIcon.Text = "⚠";
                    RightCalStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating calibration status icons: {ex.Message}", "UI");
            }
        }

        /// <summary>
        /// Updates ADC mode indicators in both control panel and header
        /// </summary>
        private void UpdateAdcModeIndicators(string mode)
        {
            try
            {
                if (mode == "Internal")
                {
                    InternalModeIndicator.Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
                    ADS1115ModeIndicator.Fill = new SolidColorBrush(Color.FromRgb(204, 204, 204)); // Gray
                    HeaderAdcModeTxt.Text = "Internal 12-bit";
                }
                else if (mode == "ADS1115")
                {
                    InternalModeIndicator.Fill = new SolidColorBrush(Color.FromRgb(204, 204, 204));
                    ADS1115ModeIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    HeaderAdcModeTxt.Text = "ADS1115 16-bit";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating ADC mode indicators: {ex.Message}", "UI");
            }
        }

        /// <summary>
        /// Unified mode switch handler for the Switch Mode button
        /// </summary>
        private void SwitchModeBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Toggle between modes based on current header text
                if (HeaderAdcModeTxt.Text.Contains("Internal"))
                {
                    // Switch to ADS1115
                    if (_canService != null)
                    {
                        _canService.SwitchToADS1115();
                        UpdateAdcModeIndicators("ADS1115");
                        _logger.LogInfo("Switched to ADS1115 mode", "ADC");
                    }
                }
                else
                {
                    // Switch to Internal ADC
                    if (_canService != null)
                    {
                        _canService.SwitchToInternalADC();
                        UpdateAdcModeIndicators("Internal");
                        _logger.LogInfo("Switched to Internal ADC mode", "ADC");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error switching ADC mode: {ex.Message}", "ADC");
                MessageBox.Show($"Error switching ADC mode: {ex.Message}", 
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Updates sample counter display
        /// </summary>
        private void UpdateSampleCount()
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var sampleCount = _dataLogger.GetLogLineCount();
                    SampleCountTxt.Text = $"{sampleCount:N0} samples";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating sample count: {ex.Message}", "UI");
            }
        }

        /// <summary>
        /// Updates transmission rate display in header
        /// </summary>
        private void UpdateHeaderTransmissionRate()
        {
            try
            {
                // This method is no longer needed since we removed TransmissionRateCombo
                // Header rate is now managed directly by HeaderRateCombo_SelectionChanged
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating header transmission rate: {ex.Message}", "UI");
            }
        }

        #endregion

        #region Configuration Viewer

        /// <summary>
        /// Opens the Configuration Viewer window
        /// </summary>
        private void ViewConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configViewer = new ConfigurationViewer();
                configViewer.Owner = this;
                configViewer.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening configuration viewer: {ex.Message}", "UI");
                MessageBox.Show($"Error opening configuration viewer: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handles transmission rate changes from header dropdown
        /// </summary>
        private void HeaderRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (HeaderRateCombo?.SelectedIndex >= 0)
                {
                    _currentTransmissionRate = HeaderRateCombo.SelectedIndex switch
                    {
                        0 => 0x01, // 100Hz
                        1 => 0x02, // 500Hz
                        2 => 0x03, // 1kHz
                        3 => 0x05, // 1Hz
                        _ => 0x03  // Default 1kHz
                    };
                    
                    // Save to settings
                    _settingsManager.SetTransmissionRate(_currentTransmissionRate, HeaderRateCombo.SelectedIndex);
                    
                    _logger.LogInfo($"Transmission rate changed from header: {GetRateText(_currentTransmissionRate)}", "Settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Header rate selection error: {ex.Message}", "Settings");
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}