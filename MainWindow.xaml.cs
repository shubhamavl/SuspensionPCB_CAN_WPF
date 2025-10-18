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

namespace SuspensionPCB_CAN_WPF
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer? _uiUpdateTimer;
        private DispatcherTimer? _autoRequestTimer;
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
        }

        private void StopAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Change to loading state
                StopAllBtn.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                StopAllBtn.Content = "Stopping...";

                // Use v0.7 semantic stream control to stop all streams
                bool stopped = _canService?.StopAllStreams() ?? false;

                if (stopped)
                {
                    _leftStreamRunning = false;
                    _rightStreamRunning = false;
                    _logger.LogInfo("Stopped all streams", "CAN");
                    // Success state - Green
                    StopAllBtn.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                    StopAllBtn.Content = "Stopped ✓";
                    MessageBox.Show("All streams stopped successfully.", "Streams Stopped", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _logger.LogError("Failed to stop all streams", "CAN");
                    // Error state - Red
                    StopAllBtn.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                    StopAllBtn.Content = "Failed ✗";
                    MessageBox.Show("Failed to stop all streams.", "Stream Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // Reset to default after 2 seconds
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() => {
                            StopAllBtn.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));
                        StopAllBtn.Content = "Stop All Streams";
                        });
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Stop streams error: {ex.Message}", "CAN");
                MessageBox.Show($"Stream Error: {ex.Message}", "CAN Transmission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Reset button state on error
                StopAllBtn.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));
                StopAllBtn.Content = "Stop All Streams";
            }
        }

        private void OpenCalibrationBtn_Click(object sender, RoutedEventArgs e)
        {
            // Open new v0.7 calibration dialog
            var calibrationDialog = new CalibrationDialog("Left");
            calibrationDialog.Owner = this;
            if (calibrationDialog.ShowDialog() == true)
            {
                // Reload calibrations after successful calibration
                LoadCalibrations();
                UpdateWeightDisplays();
            }
        }

        // Calibration verification is now handled by CalibrationDialog

        private void InitializeApplication()
        {
            LoadConfiguration();
            
            // Load existing calibrations and tare settings
            LoadCalibrations();
            _tareManager.LoadFromFile();

            try
            {
                _canService = new CANService();  // Use new CANService
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

            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
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

            UpdateConnectionStatus(false);
            _logger.LogInfo("Suspension Application initialized - Protocol v0.7", "Application");
        }

        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            try
            {
                lock (_dataLock)
                {
                    if (e.Side == 0) // Left side
                    {
                        _leftRawADC = e.RawADCSum;
                    }
                    else if (e.Side == 1) // Right side
                    {
                        _rightRawADC = e.RawADCSum;
                    }
                }
                
                _logger.LogInfo($"Raw data received: Side={e.Side}, Raw={e.RawADCSum}", "CAN");
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

                _logger.LogInfo($"Processing CAN message: ID=0x{message.ID:X3}, Data={BitConverter.ToString(message.Data)}", "CAN");

                var vm = new CANMessageViewModel(message, _rxMessageIds, _txMessageIds);
                _messageQueue.Enqueue(vm);

                lock (_statisticsLock)
                {
                    _totalMessages++;
                    if (vm.Direction == "RX") _rxMessages++;
                    else if (vm.Direction == "TX") _txMessages++;
                }

                // Process weight and calibration data according to protocol v0.5
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
                        _logger.LogInfo($"Left side raw ADC: {_leftRawADC}", "CAN");
                    }
                    break;

                case 0x201: // Right Side Raw ADC Data
                    if (message.Data?.Length >= 2)
                    {
                        _rightRawADC = (ushort)(message.Data[0] | (message.Data[1] << 8));
                        _logger.LogInfo($"Right side raw ADC: {_rightRawADC}", "CAN");
                    }
                    break;

                case 0x300: // System Status (on-demand)
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
            while (_messageQueue.TryDequeue(out var vm) && processed < 10)
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
            lock (_dataLock)
            {
                try
                {
                    // Update Left Side Display with Smart Logic
                    if (LeftRawTxt != null) LeftRawTxt.Text = _leftRawADC.ToString();
                    
                    bool leftCalibrated = _leftCalibration != null && _leftCalibration.IsValid;
                    bool leftTared = _tareManager.LeftIsTared;
                    
                    double leftCalibratedKg = 0.0;
                    double leftCalSlope = 0.0;
                    double leftCalIntercept = 0.0;
                    
                    if (leftCalibrated)
                    {
                        leftCalibratedKg = _leftCalibration!.RawToKg(_leftRawADC);
                        leftCalSlope = _leftCalibration.Slope;
                        leftCalIntercept = _leftCalibration.Intercept;
                        if (LeftCalibratedTxt != null) LeftCalibratedTxt.Text = $"{leftCalibratedKg:F1} kg";
                    }
                    else
                    {
                        if (LeftCalibratedTxt != null) LeftCalibratedTxt.Text = "Not Calibrated";
                    }
                    
                    // Smart Display Logic
                    if (leftCalibrated)
                    {
                        double leftDisplayKg = _tareManager.ApplyTare(leftCalibratedKg, true);
                        if (LeftDisplayTxt != null) LeftDisplayTxt.Text = $"{leftDisplayKg:F1} kg";
                    }
                    else
                    {
                        if (LeftDisplayTxt != null) LeftDisplayTxt.Text = "Calibration Required";
                    }
                    
                    if (LeftTareStatusTxt != null) LeftTareStatusTxt.Text = _tareManager.GetTareStatusText(true);
                    
                    // Log left side data
                    if (_dataLogger.IsLogging)
                    {
                        double leftTareBaseline = _tareManager.LeftIsTared ? _tareManager.LeftBaselineKg : 0.0;
                        _dataLogger.LogDataPoint("Left", _leftRawADC, leftCalibratedKg, leftCalibrated ? _tareManager.ApplyTare(leftCalibratedKg, true) : 0.0, 
                                               leftTareBaseline, leftCalSlope, leftCalIntercept, 0);
                    }
                    
                    // Update Right Side Display with Smart Logic
                    if (RightRawTxt != null) RightRawTxt.Text = _rightRawADC.ToString();
                    
                    bool rightCalibrated = _rightCalibration != null && _rightCalibration.IsValid;
                    bool rightTared = _tareManager.RightIsTared;
                    
                    double rightCalibratedKg = 0.0;
                    double rightCalSlope = 0.0;
                    double rightCalIntercept = 0.0;
                    
                    if (rightCalibrated)
                    {
                        rightCalibratedKg = _rightCalibration!.RawToKg(_rightRawADC);
                        rightCalSlope = _rightCalibration.Slope;
                        rightCalIntercept = _rightCalibration.Intercept;
                        if (RightCalibratedTxt != null) RightCalibratedTxt.Text = $"{rightCalibratedKg:F1} kg";
                    }
                    else
                    {
                        if (RightCalibratedTxt != null) RightCalibratedTxt.Text = "Not Calibrated";
                    }
                    
                    // Smart Display Logic
                    if (rightCalibrated)
                    {
                        double rightDisplayKg = _tareManager.ApplyTare(rightCalibratedKg, false);
                        if (RightDisplayTxt != null) RightDisplayTxt.Text = $"{rightDisplayKg:F1} kg";
                    }
                    else
                    {
                        if (RightDisplayTxt != null) RightDisplayTxt.Text = "Calibration Required";
                    }
                    
                    if (RightTareStatusTxt != null) RightTareStatusTxt.Text = _tareManager.GetTareStatusText(false);
                    
                    // Log right side data
                    if (_dataLogger.IsLogging)
                    {
                        double rightTareBaseline = _tareManager.RightIsTared ? _tareManager.RightBaselineKg : 0.0;
                        _dataLogger.LogDataPoint("Right", _rightRawADC, rightCalibratedKg, rightCalibrated ? _tareManager.ApplyTare(rightCalibratedKg, false) : 0.0, 
                                               rightTareBaseline, rightCalSlope, rightCalIntercept, 0);
                    }
                    
                    // Update Summary with Smart Logic
                    if (leftCalibrated && rightCalibrated)
                    {
                        double leftDisplayKg = _tareManager.ApplyTare(leftCalibratedKg, true);
                        double rightDisplayKg = _tareManager.ApplyTare(rightCalibratedKg, false);
                        double total = leftDisplayKg + rightDisplayKg;
                        
                        if (TotalWeightTxt != null) TotalWeightTxt.Text = $"{total:F1} kg";
                        
                        if (total > 0)
                        {
                            double leftPercent = (leftDisplayKg / total) * 100;
                            double rightPercent = (rightDisplayKg / total) * 100;
                            if (BalanceTxt != null) BalanceTxt.Text = $"{leftPercent:F0}% L / {rightPercent:F0}% R";
                        }
                        else
                        {
                            if (BalanceTxt != null) BalanceTxt.Text = "50% L / 50% R";
                        }
                    }
                    else
                    {
                        if (TotalWeightTxt != null) TotalWeightTxt.Text = "Calibration Required";
                        if (BalanceTxt != null) BalanceTxt.Text = "Calibration Required";
                    }

                    // Update System Status
                    if (DataRateTxt != null) DataRateTxt.Text = "Data Rate: 1kHz";
                    if (LastUpdateTxt != null) LastUpdateTxt.Text = $"Last Update: {DateTime.Now:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Weight display update error: {ex.Message}", "UI");
                }
            }
        }

        private void UpdateStatistics()
        {
            lock (_statisticsLock)
            {
                // Update message statistics if UI elements exist
                // TotalMessagesTxt?.Text = _totalMessages.ToString();
                // TxMessagesTxt?.Text = _txMessages.ToString();
                // RxMessagesTxt?.Text = _rxMessages.ToString();
            }
        }

        private void UpdateClock()
        {
            try
            {
                if (TimestampText != null)
                    TimestampText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
                if (StatusIndicator != null && StatusText != null && StatusBarText != null)
                {
                    if (connected)
                    {
                        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                        StatusText.Text = "Connected";
                        StatusBarText.Text = $"Connected - USB-CAN - {GetSelectedBaudRate()} - Suspension Protocol v0.5";
                    }
                    else
                    {
                        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                        StatusText.Text = "Disconnected";
                        StatusBarText.Text = "Disconnected - Check USB-CAN Connection";
                    }
                }

                if (ConnectBtn != null) ConnectBtn.IsEnabled = !connected;
                if (DisconnectBtn != null) DisconnectBtn.IsEnabled = connected;
                if (RequestSuspensionBtn != null) RequestSuspensionBtn.IsEnabled = connected;
                if (RequestAxleBtn != null) RequestAxleBtn.IsEnabled = connected;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Connection status update error: {ex.Message}", "UI");
            }
        }

        private string GetSelectedBaudRate()
        {
            try
            {
                return (BaudRateCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "250 kbps";
            }
            catch
            {
                return "250 kbps";
            }
        }

        private ushort GetBaudRateValue()
        {
            return GetSelectedBaudRate() switch
            {
                "1 Mbps" => 1000,
                "500 kbps" => 500,
                "250 kbps" => 250,
                "125 kbps" => 125,
                _ => 250
            };
        }

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            StopAllBtn.IsEnabled = true;  // stop 
            try
            {
                if (_canService == null)
                {
                    MessageBox.Show("CAN Service not initialized.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool connected = _canService.Connect(0, GetBaudRateValue());
                if (connected)
                {
                    UpdateConnectionStatus(true);
                    ResetStatistics();
                    MessageBox.Show("USB-CAN Connected Successfully.\n\nProtocol: CAN v0.5\nExpected responses:\n• 0x200 (Suspension weights)\n• 0x201 (Axle weights)\n• 0x400/0x401 (Calibration)\n• 0x402 (Errors)",
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
                _totalMessages = _txMessages = _rxMessages = 0;
            }

            lock (_dataLock)
            {
                _leftRawADC = 0;
                _rightRawADC = 0;
            }
        }

        private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _autoRequestTimer?.Stop();
                _canService?.Disconnect();
                UpdateConnectionStatus(false);
                StopAllBtn.IsEnabled = false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Disconnect error: {ex.Message}", "CAN");
            }
        }

        private void RequestSuspensionBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService?.IsConnected == true)
                {
                    // Start streaming left side at current rate using v0.7 semantic ID
                    bool success = _canService.StartLeftStream(_currentTransmissionRate);
                    if (success)
                    {
                        _leftStreamRunning = true;
                        _logger.LogInfo($"Started left side streaming at rate {_currentTransmissionRate}", "CAN");
                        MessageBox.Show($"Left side streaming started at {GetRateText(_currentTransmissionRate)}", 
                                      "Stream Started", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _leftStreamRunning = false;
                        _logger.LogError("Failed to start left side streaming", "CAN");
                        MessageBox.Show("Failed to start left side streaming.", "Stream Error", 
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("CAN service not connected.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Left stream start error: {ex.Message}", "CAN");
                MessageBox.Show($"Stream Error: {ex.Message}", "CAN Transmission Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RequestAxleBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService?.IsConnected == true)
                {
                    // Start streaming right side at current rate using v0.7 semantic ID
                    bool success = _canService.StartRightStream(_currentTransmissionRate);
                    if (success)
                    {
                        _rightStreamRunning = true;
                        _logger.LogInfo($"Started right side streaming at rate {_currentTransmissionRate}", "CAN");
                        MessageBox.Show($"Right side streaming started at {GetRateText(_currentTransmissionRate)}", 
                                      "Stream Started", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _rightStreamRunning = false;
                        _logger.LogError("Failed to start right side streaming", "CAN");
                        MessageBox.Show("Failed to start right side streaming.", "Stream Error", 
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("CAN service not connected.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Right stream start error: {ex.Message}", "CAN");
                MessageBox.Show($"Stream Error: {ex.Message}", "CAN Transmission Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                _ => $"Unknown Rate (0x{rate:X2})"
            };
        }

        // Old calibration system removed - now using CalibrationDialog

        private void AutoRequestChk_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IntervalTxt != null && int.TryParse(IntervalTxt.Text, out int interval) && interval > 0)
                {
                    _autoRequestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
                    _autoRequestTimer.Tick += (s, args) =>
                    {
                        if (_autoRequestTimer?.Tag == null)
                        {
                            RequestSuspensionBtn_Click(this, new RoutedEventArgs());
                            _autoRequestTimer!.Tag = "axle";
                        }
                        else
                        {
                            RequestAxleBtn_Click(this, new RoutedEventArgs());
                            _autoRequestTimer!.Tag = null;
                        }
                    };
                    _autoRequestTimer.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Auto request timer error: {ex.Message}", "UI");
            }
        }

        private void AutoRequestChk_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _autoRequestTimer?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Auto request stop error: {ex.Message}", "UI");
            }
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Messages.Clear();
                FilteredMessages.Clear();
                while (_messageQueue.TryDequeue(out _)) { }
                ResetStatistics();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Clear log error: {ex.Message}", "UI");
            }
        }

        private void FilterIdTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                ApplyMessageFilter();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Filter text change error: {ex.Message}", "UI");
            }
        }


        private void LoadConfiguration()
        {
            try
            {
                string configPath = System.IO.Path.Combine(Environment.CurrentDirectory, "Suspension_Config.json");
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                    if (config != null && config.ContainsKey("AutoRequestInterval") && IntervalTxt != null)
                    {
                        IntervalTxt.Text = config["AutoRequestInterval"].ToString();
                    }
                    if (config != null && config.ContainsKey("TransmissionRate"))
                    {
                        _currentTransmissionRate = byte.Parse(config["TransmissionRate"].ToString() ?? "2");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not load configuration: {ex.Message}", "Config");
            }
        }

        // v0.7 Calibration and Tare Methods
        private void LoadCalibrations()
        {
            _leftCalibration = LinearCalibration.LoadFromFile("Left");
            _rightCalibration = LinearCalibration.LoadFromFile("Right");
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
                MessageBox.Show($"Left side tared successfully.\nBaseline: {currentCalibratedKg:F1} kg", 
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
                MessageBox.Show($"Right side tared successfully.\nBaseline: {currentCalibratedKg:F1} kg", 
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
                        MessageBox.Show("CAN service not connected. Cannot start stream for calibration.", 
                                      "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                
                var calibrationDialog = new CalibrationDialog("Left");
                calibrationDialog.Owner = this;
                if (calibrationDialog.ShowDialog() == true)
                {
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
                        MessageBox.Show("CAN service not connected. Cannot start stream for calibration.", 
                                      "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                
                var calibrationDialog = new CalibrationDialog("Right");
                calibrationDialog.Owner = this;
                if (calibrationDialog.ShowDialog() == true)
                {
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
                        MessageBox.Show("Failed to export data. No log file found.", "Export Error", 
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Export Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                // Stop all timers
                _uiUpdateTimer?.Stop();
                _autoRequestTimer?.Stop();
                _clockTimer?.Stop();

                // Stop data transmission if active
                if (_canService?.IsConnected == true)
                {
                // Stop all streams using v0.7 semantic stream control
                _canService.StopAllStreams();  // Stop all streams
                }

                // Disconnect CAN service
                _canService?.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Window closing error: {ex.Message}", "Application");
            }
            finally
            {
                base.OnClosing(e);
            }
        }

        // Window Access Event Handlers
        private void OpenMonitorWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var monitorWindow = new MonitorWindow(_canService, _logger, _tareManager, _leftCalibration, _rightCalibration);
                monitorWindow.Show();
                _logger?.LogInfo("Monitor window opened", "UI");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error opening monitor window: {ex.Message}", "UI");
                MessageBox.Show($"Error opening monitor window: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenLogsWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logsWindow = new LogsWindow(_logger);
                logsWindow.Show();
                _logger?.LogInfo("Logs window opened", "UI");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error opening logs window: {ex.Message}", "UI");
                MessageBox.Show($"Error opening logs window: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


}