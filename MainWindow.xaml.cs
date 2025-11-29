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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SuspensionPCB_CAN_WPF.Services;

namespace SuspensionPCB_CAN_WPF
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer? _uiUpdateTimer;
        private DispatcherTimer? _clockTimer;

        private volatile int _totalMessages, _txMessages, _rxMessages;
        private readonly object _dataLock = new object();
        private CANService? _canService;  // Changed from USBCANManager to CANService
        private FirmwareUpdateService? _firmwareUpdateService;
        private CancellationTokenSource? _firmwareUpdateCts;
        private string? _selectedFirmwarePath;
        private bool _firmwareUpdateInProgress;
        private readonly object _statisticsLock = new object();

        // v0.7 Calibration and Tare functionality - Mode-specific calibrations
        private LinearCalibration? _leftCalibrationInternal;
        private LinearCalibration? _leftCalibrationADS1115;
        private LinearCalibration? _rightCalibrationInternal;
        private LinearCalibration? _rightCalibrationADS1115;
        private TareManager _tareManager = new TareManager();
        private byte _currentADCMode = 0; // Track current ADC mode (0=Internal, 1=ADS1115)
        
        // Active mode tracking for single dashboard
        private string _activeSide = ""; // "Left" or "Right" or ""
        private byte _activeADCMode = 0; // 0=Internal, 1=ADS1115
        private DataLogger _dataLogger = new DataLogger();
        private StatusHistoryManager _statusHistoryManager = new StatusHistoryManager(100);
        
        // Current raw ADC data (from STM32)
        private int _leftRawADC = 0;
        private int _rightRawADC = 0;
        private int _currentRawADC = 0; // Current active stream raw ADC
        
        // Stream state tracking
        private bool _leftStreamRunning = false;
        private bool _rightStreamRunning = false;

        // Thread-safe collections for better performance
        private readonly ConcurrentQueue<CANMessageViewModel> _messageQueue = new ConcurrentQueue<CANMessageViewModel>();
        public ObservableCollection<CANMessageViewModel> Messages { get; set; } = new ObservableCollection<CANMessageViewModel>();
        public ObservableCollection<CANMessageViewModel> FilteredMessages { get; set; } = new ObservableCollection<CANMessageViewModel>();

        // Production logging
        private ProductionLogger _logger = ProductionLogger.Instance;

        // Update service for WiFi-based auto-updates
        private readonly UpdateService _updateService = new UpdateService();

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
                    ResetTare_Click(sender, new RoutedEventArgs());
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
                    _activeSide = ""; // Clear active side when streams stop
                    UpdateStreamingIndicators();
                    UpdateDashboardMode();
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
                // Update single dashboard indicator based on active stream
                bool isStreamActive = _leftStreamRunning || _rightStreamRunning;
                string activeSideName = _leftStreamRunning ? "Left" : (_rightStreamRunning ? "Right" : "");
                
                if (StreamIndicator != null)
                {
                    StreamIndicator.Fill = isStreamActive ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
                
                if (StreamStatusTxt != null)
                {
                    if (isStreamActive)
                    {
                        StreamStatusTxt.Text = $"{activeSideName} Active";
                        StreamStatusTxt.Foreground = System.Windows.Media.Brushes.Green;
                    }
                    else
                    {
                        StreamStatusTxt.Text = "Stopped";
                        StreamStatusTxt.Foreground = System.Windows.Media.Brushes.Gray;
                    }
                }
                
                // Update dashboard mode when stream status changes
                UpdateDashboardMode();
                
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

        private void ShowDownloadStatus(string message)
        {
            try
            {
                if (DownloadStatusText != null)
                {
                    DownloadStatusText.Text = message;
                    DownloadStatusText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download status error: {ex.Message}", "UI");
            }
        }

        private void HideDownloadStatus()
        {
            try
            {
                if (DownloadStatusText != null)
                {
                    DownloadStatusText.Visibility = Visibility.Collapsed;
                    DownloadStatusText.Text = "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Hide download status error: {ex.Message}", "UI");
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
            try
            {
                if (BaudRateCombo?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string baudRate = selectedItem.Content?.ToString() ?? "250 kbps";
                    return baudRate switch
                    {
                        "1 Mbps" => 1000,
                        "500 kbps" => 500,
                        "250 kbps" => 250,
                        "125 kbps" => 125,
                        _ => 250
                    };
                }
            }
            catch { }
            return 250;
        }

        private void AdapterTypeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (AdapterTypeCombo?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string adapterType = selectedItem.Tag?.ToString() ?? "USB";
                    if (adapterType == "PCAN")
                    {
                        if (PcanChannelCombo != null)
                        {
                            PcanChannelCombo.Visibility = Visibility.Visible;
                            RefreshPcanChannels();
                        }
                        if (AdapterHintTxt != null)
                        {
                            AdapterHintTxt.Text = "PCAN adapter selected. Make sure PCANBasic.dll is available and PCAN driver is installed.";
                        }
                        if (PcanStatusTxt != null)
                        {
                            PcanStatusTxt.Visibility = Visibility.Visible;
                            CheckPcanAvailability();
                        }
                    }
                    else if (adapterType == "SIM")
                    {
                        if (PcanChannelCombo != null)
                        {
                            PcanChannelCombo.Visibility = Visibility.Collapsed;
                        }
                        if (AdapterHintTxt != null)
                        {
                            AdapterHintTxt.Text = "Simulator mode enabled. No hardware required - automatic weight data generation.";
                        }
                        if (PcanStatusTxt != null)
                        {
                            PcanStatusTxt.Visibility = Visibility.Collapsed;
                        }
                        if (OpenSimulatorControlBtn != null)
                        {
                            OpenSimulatorControlBtn.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        if (PcanChannelCombo != null)
                        {
                            PcanChannelCombo.Visibility = Visibility.Collapsed;
                        }
                        if (AdapterHintTxt != null)
                        {
                            AdapterHintTxt.Text = "USB-CAN-A Serial adapter selected. Uses COM port communication.";
                        }
                        if (PcanStatusTxt != null)
                        {
                            PcanStatusTxt.Visibility = Visibility.Collapsed;
                        }
                        if (OpenSimulatorControlBtn != null)
                        {
                            OpenSimulatorControlBtn.Visibility = Visibility.Collapsed;
                        }
                    }
                    SaveConfiguration();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Adapter type selection error: {ex.Message}", "UI");
            }
            }

        private string GetSelectedAdapterType()
            {
                try
                {
                if (AdapterTypeCombo?.SelectedItem is ComboBoxItem selectedItem)
                {
                    return selectedItem.Content?.ToString() ?? "USB-CAN-A Serial";
                }
            }
            catch { }
            return "USB-CAN-A Serial";
        }

        private CanAdapterConfig? GetAdapterConfig()
        {
            try
            {
                if (AdapterTypeCombo?.SelectedItem is ComboBoxItem selectedItem)
                {
                    string adapterType = selectedItem.Tag?.ToString() ?? "USB";
                    
                    if (adapterType == "PCAN")
                    {
                        ushort channel = PcanCanAdapter.PCAN_USBBUS1;
                        if (PcanChannelCombo?.SelectedItem is ComboBoxItem channelItem)
                        {
                            string channelTag = channelItem.Tag?.ToString() ?? "0x51";
                            channel = Convert.ToUInt16(channelTag, 16);
                        }

                        ushort bitrate = GetPcanBitrate();
                        return new PcanCanAdapterConfig
                        {
                            Channel = channel,
                            PcanBitrate = bitrate,
                            BitrateKbps = GetBaudRateValue()
                        };
                    }
                    else if (adapterType == "SIM")
                    {
                        return new SimulatorCanAdapterConfig
                        {
                            BitrateKbps = GetBaudRateValue()
                        };
                    }
                    else // USB-CAN-A Serial
                    {
                        return new UsbSerialCanAdapterConfig
                        {
                            PortName = string.Empty, // Auto-detect
                            SerialBaudRate = 2000000,
                            BitrateKbps = GetBaudRateValue()
                        };
                    }
                }
                }
                catch (Exception ex)
                {
                _logger.LogError($"Get adapter config error: {ex.Message}", "UI");
            }
            return null;
        }

        private ushort GetPcanBitrate()
        {
            return GetBaudRateValue() switch
            {
                1000 => PcanCanAdapter.PCAN_BAUD_1M,
                500 => PcanCanAdapter.PCAN_BAUD_500K,
                250 => PcanCanAdapter.PCAN_BAUD_250K,
                125 => PcanCanAdapter.PCAN_BAUD_125K,
                _ => PcanCanAdapter.PCAN_BAUD_500K
            };
        }

        private void RefreshPcanChannels()
        {
            try
            {
                var adapter = new PcanCanAdapter();
                string[] availableChannels = adapter.GetAvailableOptions();
                
                if (PcanChannelCombo != null)
                {
                    PcanChannelCombo.Items.Clear();
                    foreach (string channel in availableChannels)
                    {
                        ushort channelValue = channel switch
                        {
                            "USB1" => PcanCanAdapter.PCAN_USBBUS1,
                            "USB2" => PcanCanAdapter.PCAN_USBBUS2,
                            "USB3" => PcanCanAdapter.PCAN_USBBUS3,
                            "USB4" => PcanCanAdapter.PCAN_USBBUS4,
                            "USB5" => PcanCanAdapter.PCAN_USBBUS5,
                            "USB6" => PcanCanAdapter.PCAN_USBBUS6,
                            "USB7" => PcanCanAdapter.PCAN_USBBUS7,
                            "USB8" => PcanCanAdapter.PCAN_USBBUS8,
                            _ => PcanCanAdapter.PCAN_USBBUS1
                        };
                        
                        var item = new ComboBoxItem
                        {
                            Content = channel,
                            Tag = $"0x{channelValue:X2}"
                        };
                        PcanChannelCombo.Items.Add(item);
                    }
                    
                    if (PcanChannelCombo.Items.Count > 0)
                        PcanChannelCombo.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error refreshing PCAN channels: {ex.Message}", "UI");
                if (PcanStatusTxt != null)
                {
                    PcanStatusTxt.Text = $"PCAN not available: {ex.Message}";
                    PcanStatusTxt.Visibility = Visibility.Visible;
                }
            }
        }

        private void CheckPcanAvailability()
        {
            try
            {
                var adapter = new PcanCanAdapter();
                string[] channels = adapter.GetAvailableOptions();
                if (PcanStatusTxt != null)
                {
                    if (channels.Length > 0)
                    {
                        PcanStatusTxt.Text = $"✓ PCAN available ({channels.Length} channel(s) detected)";
                        PcanStatusTxt.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                    }
                    else
                    {
                        PcanStatusTxt.Text = "⚠ PCAN driver not detected. Please install PEAK PCAN driver from PEAK-System website.";
                        PcanStatusTxt.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                    }
                }
            }
            catch (Exception ex)
            {
                if (PcanStatusTxt != null)
                {
                    PcanStatusTxt.Text = $"✗ PCAN error: {ex.Message}";
                    PcanStatusTxt.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                }
            }
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

                // Check if already connected - toggle to disconnect
                if (_canService.IsConnected)
                {
                    _canService.Disconnect();
                    UpdateConnectionStatus(false);
                    ShowStatusBanner("Disconnected", false);
                    ShowInlineStatus("Disconnected from CAN adapter", false);
                    if (ConnectionToggle != null) ConnectionToggle.IsChecked = false;
                    return;
                }

                // Get adapter configuration
                CanAdapterConfig? config = GetAdapterConfig();
                if (config == null)
                {
                    ShowInlineStatus("Invalid adapter configuration.", true);
                    return;
                }

                bool connected = _canService.Connect(config, out string errorMessage);
                if (connected)
                {
                    UpdateConnectionStatus(true);
                    ResetStatistics();
                    string adapterName = GetSelectedAdapterType();
                    ShowStatusBanner("✓ Connected Successfully", true);
                    ShowInlineStatus($"{adapterName} Connected Successfully. Protocol: CAN v0.7", false);
                    if (ConnectionToggle != null) ConnectionToggle.IsChecked = true;
                    SaveConfiguration(); // Save adapter settings
                    _canService.RequestBootloaderInfo();
                }
                else
                {
                    UpdateConnectionStatus(false);
                    ShowStatusBanner("✗ Connection Failed", false);
                    ShowInlineStatus($"Connection Failed: {errorMessage}", true);
                    if (ConnectionToggle != null) ConnectionToggle.IsChecked = false;
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false);
                ShowStatusBanner($"✗ Connection Error: {ex.Message}", false);
                ShowInlineStatus($"Connection Error: {ex.Message}", true);
                if (ConnectionToggle != null) ConnectionToggle.IsChecked = false;
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
                        _currentRawADC = _leftRawADC;
                        
                        // Set active side to Left
                        if (_activeSide != "Left")
                        {
                            _activeSide = "Left";
                            _activeADCMode = _currentADCMode; // Use current system ADC mode
                            UpdateDashboardMode();
                            UpdateWeightProcessorCalibration(); // This already sets ADC mode for active side
                            _logger.LogInfo($"Active side set to Left (ADC mode: {_activeADCMode})", "CAN");
                        }
                        
                        // Confirm left stream is active
                        if (!_leftStreamRunning)
                        {
                            _leftStreamRunning = true;
                            _rightStreamRunning = false; // Ensure right is stopped (only one can run)
                            UpdateStreamingIndicators();
                            UpdateDashboardMode();
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
                        _currentRawADC = _rightRawADC;
                        
                        // Set active side to Right
                        if (_activeSide != "Right")
                        {
                            _activeSide = "Right";
                            _activeADCMode = _currentADCMode; // Use current system ADC mode
                            UpdateDashboardMode();
                            UpdateWeightProcessorCalibration(); // This already sets ADC mode for active side
                            _logger.LogInfo($"Active side set to Right (ADC mode: {_activeADCMode})", "CAN");
                        }
                        
                        // Confirm right stream is active
                        if (!_rightStreamRunning)
                        {
                            _rightStreamRunning = true;
                            _leftStreamRunning = false; // Ensure left is stopped (only one can run)
                            UpdateStreamingIndicators();
                            UpdateDashboardMode();
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
                
                // Get current calibration based on active side and ADC mode
                var currentCalibration = GetCurrentCalibration();
                
                // Determine which data to display based on active side
                ProcessedWeightData currentData;
                bool isLeft = _activeSide == "Left";
                
                if (string.IsNullOrEmpty(_activeSide))
                {
                    // No active stream - show placeholder
                    if (WeightDisplayTxt != null) WeightDisplayTxt.Text = "No Active Stream";
                    if (RawTxt != null) RawTxt.Text = "0";
                    if (StreamStatusTxt != null) StreamStatusTxt.Text = "Stopped";
                    if (CalStatusText != null) CalStatusText.Text = "N/A";
                    if (TareStatusTxt != null) TareStatusTxt.Text = "N/A";
                    return;
                }
                
                // Get data for active side
                currentData = isLeft ? leftData : rightData;
                _currentRawADC = isLeft ? _leftRawADC : _rightRawADC;
                
                // Update Raw ADC display
                if (RawTxt != null) RawTxt.Text = _currentRawADC.ToString();
                
                // Check if calibrated
                bool isCalibrated = currentCalibration != null && currentCalibration.IsValid;
                
                // Update weight display
                if (WeightDisplayTxt != null)
                {
                    if (isCalibrated)
                    {
                        WeightDisplayTxt.Text = $"{(int)currentData.TaredWeight} kg";
                    }
                    else
                    {
                        WeightDisplayTxt.Text = "Calibration Required";
                    }
                }
                
                // Update tare status (mode-specific)
                if (TareStatusTxt != null)
                {
                    bool isTared = _tareManager.IsTared(_activeSide, _activeADCMode);
                    TareStatusTxt.Text = isTared ? "Tare:✓" : "Tare:✗";
                }
                
                // Update calibration status
                if (CalStatusIcon != null && CalStatusText != null)
                {
                    if (isCalibrated)
                    {
                        CalStatusIcon.Text = "✓";
                        CalStatusIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96)); // Green
                        CalStatusText.Text = "Calibrated";
                    }
                    else
                    {
                        CalStatusIcon.Text = "⚠";
                        CalStatusIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)); // Yellow
                        CalStatusText.Text = "Not Calibrated";
                    }
                }
                
                // Log data if logging is active (only log active stream)
                if (_dataLogger.IsLogging && !string.IsNullOrEmpty(_activeSide))
                {
                    var dataToLog = isLeft ? leftData : rightData;
                    double tareBaseline = _tareManager.IsTared(_activeSide, _activeADCMode) 
                        ? _tareManager.GetBaselineKg(_activeSide, _activeADCMode) 
                        : 0.0;
                    
                    _dataLogger.LogDataPoint(_activeSide, dataToLog.RawADC, dataToLog.CalibratedWeight, dataToLog.TaredWeight, 
                                           tareBaseline, currentCalibration?.Slope ?? 0, currentCalibration?.Intercept ?? 0, _activeADCMode);
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

        /// <summary>
        /// Get current calibration based on active side and ADC mode
        /// </summary>
        private LinearCalibration? GetCurrentCalibration()
        {
            if (string.IsNullOrEmpty(_activeSide))
                return null;
            
            if (_activeSide == "Left")
            {
                return _activeADCMode == 0 ? _leftCalibrationInternal : _leftCalibrationADS1115;
            }
            else if (_activeSide == "Right")
            {
                return _activeADCMode == 0 ? _rightCalibrationInternal : _rightCalibrationADS1115;
            }
            
            return null;
        }

        private void LoadCalibrations()
        {
            // Load all 4 calibrations (Left/Right × Internal/ADS1115)
            _leftCalibrationInternal = LinearCalibration.LoadFromFile("Left", 0);
            _leftCalibrationADS1115 = LinearCalibration.LoadFromFile("Left", 1);
            _rightCalibrationInternal = LinearCalibration.LoadFromFile("Right", 0);
            _rightCalibrationADS1115 = LinearCalibration.LoadFromFile("Right", 1);
            
            // Update WeightProcessor with calibrations for current active mode
            UpdateWeightProcessorCalibration();
            
            // Update UI calibration status icons
            UpdateCalibrationStatusIcons();
        }

        /// <summary>
        /// Update WeightProcessor with calibrations for current ADC mode
        /// Uses active ADC mode if side is active, otherwise uses current system ADC mode
        /// Only updates ADC mode for the active side to prevent incorrect tare application
        /// </summary>
        private void UpdateWeightProcessorCalibration()
        {
            // Use active ADC mode if side is active, otherwise use current system ADC mode
            byte adcModeToUse = !string.IsNullOrEmpty(_activeSide) ? _activeADCMode : _currentADCMode;
            
            LinearCalibration? leftCal = adcModeToUse == 0 ? _leftCalibrationInternal : _leftCalibrationADS1115;
            LinearCalibration? rightCal = adcModeToUse == 0 ? _rightCalibrationInternal : _rightCalibrationADS1115;
            _weightProcessor.SetCalibration(leftCal, rightCal);
            
            // Only update ADC mode for the active side to ensure correct tare baseline is used
            // This prevents applying tare from wrong ADC mode when switching modes
            if (_activeSide == "Left")
            {
                _weightProcessor.SetADCMode(true, _activeADCMode);
            }
            else if (_activeSide == "Right")
            {
                _weightProcessor.SetADCMode(false, _activeADCMode);
            }
            // If no active side, don't update ADC mode (keep previous values)
        }
        
        private void Tare_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_activeSide))
                {
                    MessageBox.Show("Please start a stream (Left or Right) before taring.", "No Active Stream", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                var currentCalibration = GetCurrentCalibration();
                if (currentCalibration == null || !currentCalibration.IsValid)
                {
                    MessageBox.Show($"Please calibrate the {_activeSide} side first before taring.", "Calibration Required", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                double currentCalibratedKg = currentCalibration.RawToKg(_currentRawADC);
                
                // Use mode-specific tare (active ADC mode)
                _tareManager.Tare(_activeSide, currentCalibratedKg, _activeADCMode);
                
                _tareManager.SaveToFile();
                
                UpdateWeightDisplays();
                string modeText = _activeADCMode == 0 ? "Internal" : "ADS1115";
                MessageBox.Show($"{_activeSide} side ({modeText}) tared successfully.\nBaseline: {(int)currentCalibratedKg} kg", 
                              "Tare Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error taring {_activeSide} side: {ex.Message}", "Tare Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ResetTare_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_activeSide))
                {
                    MessageBox.Show("Please start a stream (Left or Right) before resetting tare.", "No Active Stream", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Use mode-specific reset (active ADC mode)
                _tareManager.Reset(_activeSide, _activeADCMode);
                
                _tareManager.SaveToFile();
                UpdateWeightDisplays();
                string modeText = _activeADCMode == 0 ? "Internal" : "ADS1115";
                MessageBox.Show($"{_activeSide} side ({modeText}) tare reset successfully.", "Reset Complete", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting tare: {ex.Message}", "Reset Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Calibrate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_activeSide))
                {
                    MessageBox.Show("Please start a stream (Left or Right) before calibrating.", "No Active Stream", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Check if stream for active side is already running
                bool streamRunning = _activeSide == "Left" ? _leftStreamRunning : _rightStreamRunning;
                
                if (!streamRunning)
                {
                    if (_canService?.IsConnected == true)
                    {
                        // Auto-start stream for calibration
                        bool success = false;
                        if (_activeSide == "Left")
                        {
                            success = _canService.StartLeftStream(_currentTransmissionRate);
                            if (success) _leftStreamRunning = true;
                        }
                        else
                        {
                            success = _canService.StartRightStream(_currentTransmissionRate);
                            if (success) _rightStreamRunning = true;
                        }
                        
                        if (success)
                        {
                            _logger.LogInfo($"Auto-started {_activeSide} stream for calibration at rate {_currentTransmissionRate}", "CAN");
                            UpdateStreamingIndicators();
                            
                            // Wait briefly for first data packet
                            System.Threading.Thread.Sleep(200);
                        }
                        else
                        {
                            _logger.LogError($"Failed to auto-start {_activeSide} stream for calibration", "CAN");
                            MessageBox.Show($"Failed to start {_activeSide} stream for calibration.", "Stream Error", 
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
                
                // Open calibration dialog with active side and current ADC mode
                var calibrationDialog = new CalibrationDialog(_activeSide, _activeADCMode);
                calibrationDialog.Owner = this;
                if (calibrationDialog.ShowDialog() == true)
                {
                    // Reload calibrations after successful calibration
                    LoadCalibrations();
                    UpdateWeightDisplays();
                    UpdateCalibrationStatusIcons();
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
                    _rightStreamRunning = false; // Ensure right is stopped
                    _activeSide = "Left";
                    _activeADCMode = _currentADCMode;
                    UpdateStreamingIndicators();
                    UpdateDashboardMode();
                    UpdateWeightProcessorCalibration();
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
                    _leftStreamRunning = false; // Ensure left is stopped
                    _activeSide = "Right";
                    _activeADCMode = _currentADCMode;
                    UpdateStreamingIndicators();
                    UpdateDashboardMode();
                    UpdateWeightProcessorCalibration();
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
            
            // Load adapter configuration
            LoadConfiguration();
            
            // Load existing calibrations and tare settings
            LoadCalibrations();
            _tareManager.LoadFromFile();

            // Initialize WeightProcessor with calibrations for current ADC mode
            UpdateWeightProcessorCalibration();
            _weightProcessor.SetTareManager(_tareManager);
            _weightProcessor.Start();

            // Initialize ADC mode from settings
            InitializeADCModeFromSettings();
            
            // Initialize dashboard mode display
            UpdateDashboardMode();

            try
            {
                _canService = new CANService();
                _canService.MessageReceived += OnCANMessageReceived;
                _canService.RawDataReceived += OnRawDataReceived;
                _canService.SystemStatusReceived += HandleSystemStatus;
                _canService.BootStatusReceived += OnBootStatusReceived;
                _firmwareUpdateService = new FirmwareUpdateService(_canService);
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

            // Background check for updates (non-blocking, best-effort)
            _ = CheckForUpdatesInBackground();
        }

        private async Task CheckForUpdatesInBackground()
        {
            try
            {
                var result = await _updateService.CheckForUpdateAsync();
                if (!result.IsSuccess || result.Info == null || !result.Info.IsUpdateAvailable)
                    return;

                _logger.LogInfo($"Update available. Current={result.Info.CurrentVersion}, Latest={result.Info.LatestVersion}", "Update");

                // Show popup notification on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    string message =
                        $"A new version is available!\n\n" +
                        $"Current version: {result.Info.CurrentVersion}\n" +
                        $"Latest version:  {result.Info.LatestVersion}\n\n" +
                        $"Would you like to download and install it now?";

                    var dialogResult = MessageBox.Show(message, "Update Available",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (dialogResult == MessageBoxResult.Yes)
                    {
                        // Start download directly without showing popup again
                        _ = StartUpdateDownloadAsync(result.Info);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Background update check failed: {ex.Message}", "Update");
            }
        }

        private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckUpdatesBtn.IsEnabled = false;
                ShowDownloadStatus("Checking for updates...");

                var result = await _updateService.CheckForUpdateAsync();
                if (!result.IsSuccess)
                {
                    HideDownloadStatus();
                    string errorMessage = result.ErrorMessage ?? "Could not check for updates.";
                    
                    if (result.IsNetworkError)
                    {
                        errorMessage += "\n\nPlease verify your internet connection and try again.";
                    }
                    
                    MessageBox.Show(errorMessage,
                        "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                HideDownloadStatus();

                var info = result.Info;
                if (info == null)
                {
                    MessageBox.Show("Update check completed, but no version information was available.",
                        "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!info.IsUpdateAvailable)
                {
                    HideDownloadStatus();
                    MessageBox.Show(
                        $"You are already running the latest version.\n\nCurrent version: {info.CurrentVersion}",
                        "No Updates Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string updateMessage =
                    $"A new version is available.\n\n" +
                    $"Current version: {info.CurrentVersion}\n" +
                    $"Latest version:  {info.LatestVersion}\n\n" +
                    $"Do you want to download and install it now?";

                var dialogResult = MessageBox.Show(updateMessage, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (dialogResult != MessageBoxResult.Yes)
                {
                    HideDownloadStatus();
                    _logger.LogInfo("User declined update installation.", "Update");
                    return;
                }

                // Start download process
                await StartUpdateDownloadAsync(info);
            }
            catch (Exception ex)
            {
                HideDownloadStatus();
                _logger.LogError($"Update workflow error: {ex.Message}", "Update");
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (CheckUpdatesBtn != null)
                    CheckUpdatesBtn.IsEnabled = true;
            }
        }

        private async Task StartUpdateDownloadAsync(UpdateService.UpdateInfo info)
        {
            try
            {
                var progress = new Progress<double>(p =>
                {
                    ShowDownloadStatus($"Downloading update... {p:0}%");
                });

                var downloadResult = await _updateService.DownloadUpdateAsync(info, progress);
                
                if (!downloadResult.IsSuccess)
                {
                    HideDownloadStatus();
                    string errorMessage = downloadResult.ErrorMessage ?? "Failed to download update package.";
                    
                    if (downloadResult.IsNetworkError)
                    {
                        errorMessage += "\n\nPlease check your internet connection and try again.";
                    }
                    else if (downloadResult.IsHashMismatch)
                    {
                        errorMessage += "\n\nThe downloaded file may be corrupted. Please try again.";
                    }
                    
                    MessageBox.Show(errorMessage, "Update Download Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string? packagePath = downloadResult.FilePath;
                if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
                {
                    HideDownloadStatus();
                    MessageBox.Show("Download completed but file was not found.", "Update Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                HideDownloadStatus();

                // Verify package file before proceeding
                var packageInfo = new FileInfo(packagePath);
                if (!packageInfo.Exists || packageInfo.Length == 0)
                {
                    MessageBox.Show(
                        $"Downloaded package is invalid or empty.\n\nFile: {packagePath}\nSize: {packageInfo.Length} bytes",
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                _logger.LogInfo($"Package verified: {packagePath} ({packageInfo.Length} bytes)", "Update");

                string updaterPath = PathHelper.GetUpdaterExecutablePath();
                if (!File.Exists(updaterPath))
                {
                    // Check if running in development mode (dotnet run or Debug build)
                    var mainModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                    string? exePath = mainModule?.FileName;
                    string appDir = PathHelper.ApplicationDirectory;
                    
                    bool isDevelopment = 
                        (exePath != null && (exePath.Contains("dotnet") || exePath.Contains("\\Debug\\"))) ||
                        appDir.Contains("\\Debug\\") ||
                        appDir.Contains("\\bin\\Debug\\") ||
                        !File.Exists(System.IO.Path.Combine(appDir, "SuspensionPCB_CAN_WPF.exe"));
                    
                    string updaterMessage = isDevelopment
                        ? "Auto-update is not available when running in development/debug mode.\n\n" +
                          "To test the auto-update feature:\n" +
                          "1. Run: build-portable.bat\n" +
                          "2. Run the EXE from: bin\\Release\\net8.0-windows\\win-x64\\publish\\\n\n" +
                          $"Downloaded package location:\n{packagePath}\n\n" +
                          $"You can manually extract the ZIP file to test the update, but the updater tool is only included in published releases."
                        : "Updater tool not found next to the application.\n\n" +
                          "Please download the latest full package from GitHub Releases and update manually.\n\n" +
                          $"Downloaded package location:\n{packagePath}\n\n" +
                          $"You can manually extract the ZIP file to update the application.";
                    
                    MessageBox.Show(updaterMessage,
                        isDevelopment ? "Development Mode" : "Updater Missing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Verify updater executable
                var updaterInfo = new FileInfo(updaterPath);
                if (!updaterInfo.Exists || updaterInfo.Length == 0)
                {
                    MessageBox.Show(
                        $"Updater executable is invalid or corrupted.\n\nFile: {updaterPath}",
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                _logger.LogInfo($"Starting updater: {updaterPath}", "Update");
                _logger.LogInfo($"Package: {packagePath}", "Update");
                _logger.LogInfo($"Target directory: {PathHelper.ApplicationDirectory}", "Update");

                // Start external updater and exit this process
                try
                {
                    string mainExeName = System.IO.Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "SuspensionPCB_CAN_WPF.exe");
                    
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = updaterPath,
                        WorkingDirectory = PathHelper.ApplicationDirectory,
                        UseShellExecute = true,
                        ArgumentList =
                        {
                            PathHelper.ApplicationDirectory,
                            packagePath,
                            mainExeName
                        }
                    };

                    _logger.LogInfo($"Launching updater with arguments: {PathHelper.ApplicationDirectory}, {packagePath}, {mainExeName}", "Update");
                    
                    var updaterProcess = System.Diagnostics.Process.Start(startInfo);
                    if (updaterProcess == null)
                    {
                        throw new Exception("Failed to start updater process. Process.Start returned null.");
                    }

                    _logger.LogInfo($"Updater process started (PID: {updaterProcess.Id}). Shutting down main application.", "Update");
                    
                    // Give the updater a moment to start, then shutdown
                    await System.Threading.Tasks.Task.Delay(500);
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to launch updater: {ex.Message}\n{ex.StackTrace}", "Update");
                    MessageBox.Show(
                        $"Failed to launch updater: {ex.Message}\n\n" +
                        $"You can still update manually:\n" +
                        $"1. Close this application\n" +
                        $"2. Extract the ZIP file: {packagePath}\n" +
                        $"3. Copy all files to: {PathHelper.ApplicationDirectory}\n" +
                        $"4. Restart the application",
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                HideDownloadStatus();
                _logger.LogError($"Update download error: {ex.Message}", "Update");
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                
                // Enable/disable simulator control button based on connection and adapter type
                if (OpenSimulatorControlBtn != null)
                {
                    bool isSimulator = GetSelectedAdapterType() == "Simulator";
                    OpenSimulatorControlBtn.IsEnabled = connected && isSimulator;
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

        private bool EnsureCanConnection(string actionDescription)
        {
            if (_canService?.IsConnected != true)
            {
                ShowInlineStatus($"{actionDescription} requires an active CAN connection.", true);
                return false;
            }
            return true;
        }

        private void SetFirmwareControlsEnabled(bool enabled)
        {
            if (BrowseFirmwareBtn != null) BrowseFirmwareBtn.IsEnabled = enabled;
            if (StartFirmwareUpdateBtn != null) StartFirmwareUpdateBtn.IsEnabled = enabled;
            if (EnterBootloaderBtn != null) EnterBootloaderBtn.IsEnabled = enabled;
            if (QueryBootInfoBtn != null) QueryBootInfoBtn.IsEnabled = enabled;
        }

        private void OnBootStatusReceived(object? sender, BootStatusEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var status = e.RawData.Length > 0
                        ? (BootloaderProtocol.BootloaderStatus)e.RawData[0]
                        : BootloaderProtocol.BootloaderStatus.Idle;
                    uint detail = e.RawData.Length >= 5 ? BitConverter.ToUInt32(e.RawData, 1) : 0;

                    if (FirmwareStatusText != null)
                    {
                        string message = $"Status: {BootloaderProtocol.DescribeStatus(status)}";
                        if (status == BootloaderProtocol.BootloaderStatus.InProgress)
                        {
                            message += detail > 0 ? $" ({detail}%)" : " (working)";
                        }
                        else if (detail != 0)
                        {
                            message += $" (code 0x{detail:X})";
                        }
                        FirmwareStatusText.Text = message;
                    }

                    if (FirmwareProgressBar != null && FirmwareProgressLabel != null)
                    {
                        if (status == BootloaderProtocol.BootloaderStatus.InProgress)
                        {
                            FirmwareProgressBar.Value = Math.Min(100, detail);
                            FirmwareProgressLabel.Text = $"{Math.Min(100, detail)}%";
                        }
                        else if (status == BootloaderProtocol.BootloaderStatus.Success)
                        {
                            FirmwareProgressBar.Value = 100;
                            FirmwareProgressLabel.Text = "100%";
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Boot status handling error: {ex.Message}", "FW");
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
                _firmwareUpdateCts?.Cancel();
                if (_canService != null)
                {
                    _canService.BootStatusReceived -= OnBootStatusReceived;
                }
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
                _activeSide = ""; // Clear active side on disconnect
                UpdateStreamingIndicators();
                UpdateDashboardMode();
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

        private void BrowseFirmwareBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Firmware Binary (*.bin)|*.bin|All Files (*.*)|*.*",
                    Title = "Select Firmware Binary"
                };
                if (dialog.ShowDialog() == true)
                {
                    _selectedFirmwarePath = dialog.FileName;
                    if (FirmwareFilePathText != null)
                    {
                        FirmwareFilePathText.Text = $"Selected file: {Path.GetFileName(dialog.FileName)}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Firmware browse error: {ex.Message}", "FW");
                ShowInlineStatus($"Failed to select firmware: {ex.Message}", true);
            }
        }

        private async void StartFirmwareUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_firmwareUpdateService == null)
            {
                ShowInlineStatus("Firmware update service not ready.", true);
                return;
            }
            if (!EnsureCanConnection("Firmware update"))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(_selectedFirmwarePath) || !File.Exists(_selectedFirmwarePath))
            {
                ShowInlineStatus("Select a firmware .bin file first.", true);
                return;
            }
            if (_firmwareUpdateInProgress)
            {
                ShowInlineStatus("Firmware update already in progress.", true);
                return;
            }

            _firmwareUpdateInProgress = true;
            _firmwareUpdateCts = new CancellationTokenSource();
            SetFirmwareControlsEnabled(false);
            if (FirmwareProgressBar != null) FirmwareProgressBar.Value = 0;
            if (FirmwareProgressLabel != null) FirmwareProgressLabel.Text = "0%";
            if (FirmwareStatusText != null) FirmwareStatusText.Text = "Status: Sending...";

            var progress = new Progress<FirmwareProgress>(p =>
            {
                if (FirmwareProgressBar != null) FirmwareProgressBar.Value = p.Percentage;
                if (FirmwareProgressLabel != null) FirmwareProgressLabel.Text = $"{p.Percentage:0}% ({p.ChunksSent}/{p.TotalChunks})";
            });

            try
            {
                bool success = await _firmwareUpdateService.UpdateFirmwareAsync(_selectedFirmwarePath, progress, _firmwareUpdateCts.Token);
                ShowStatusBanner(success ? "✓ Firmware transfer complete. Awaiting bootloader verification." : "✗ Firmware transfer failed.", success);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Firmware update error: {ex.Message}", "FW");
                ShowStatusBanner($"✗ Firmware update error: {ex.Message}", false);
            }
            finally
            {
                _firmwareUpdateInProgress = false;
                _firmwareUpdateCts?.Dispose();
                _firmwareUpdateCts = null;
                SetFirmwareControlsEnabled(true);
            }
        }

        private void QueryBootInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCanConnection("Bootloader query"))
                return;
            bool sent = _canService?.RequestBootloaderInfo() ?? false;
            ShowInlineStatus(sent ? "Boot info requested." : "Failed to request boot info.", !sent);
        }

        private void EnterBootloaderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCanConnection("Bootloader entry"))
                return;
            bool sent = _canService?.RequestEnterBootloader() ?? false;
            ShowInlineStatus(sent ? "Bootloader entry requested." : "Failed to request bootloader entry.", !sent);
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

        private void OpenSimulatorControl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_canService == null)
                {
                    MessageBox.Show("CAN Service not initialized.", "Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!_canService.IsConnected)
                {
                    MessageBox.Show("Please connect to simulator first.", "Connection Required", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var simulatorAdapter = _canService.GetSimulatorAdapter();
                if (simulatorAdapter == null)
                {
                    MessageBox.Show("Simulator adapter not available. Please select Simulator adapter and connect.", 
                                  "Simulator Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var simulatorWindow = new SimulatorControlWindow();
                simulatorWindow.Owner = this;
                simulatorWindow.Initialize(simulatorAdapter);
                simulatorWindow.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening simulator control window: {ex.Message}", "UI");
                MessageBox.Show($"Error opening simulator control window: {ex.Message}", "Error", 
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
                    _currentADCMode = 0;
                    _activeADCMode = 0; // Update active mode
                    UpdateAdcModeIndicators("Internal", "#FF2196F3");
                    UpdateWeightProcessorCalibration();
                    UpdateDashboardMode();
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
                    _currentADCMode = 1;
                    _activeADCMode = 1; // Update active mode
                    UpdateAdcModeIndicators("ADS1115", "#FF4CAF50");
                    UpdateWeightProcessorCalibration();
                    UpdateDashboardMode();
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
                
                // Check if ADC mode changed - reload calibrations if it did
                if (_currentADCMode != e.ADCMode)
                {
                    _currentADCMode = e.ADCMode;
                    _activeADCMode = e.ADCMode; // Update active mode
                    _logger.LogInfo($"ADC mode changed to {mode}, reloading calibrations", "MainWindow");
                    LoadCalibrations();
                    UpdateWeightProcessorCalibration();
                    UpdateDashboardMode();
                }
                
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
                
                // Set current ADC mode for calibration loading
                _currentADCMode = adcMode;
                _activeADCMode = adcMode; // Initialize active mode
                
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
        /// Updates calibration status icon for current active mode
        /// </summary>
        private void UpdateCalibrationStatusIcons()
        {
            try
            {
                if (CalStatusIcon == null || CalStatusText == null) return;
                
                var currentCalibration = GetCurrentCalibration();
                
                if (currentCalibration != null && currentCalibration.IsValid)
                {
                    CalStatusIcon.Text = "✓";
                    CalStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Green
                    CalStatusText.Text = "Calibrated";
                }
                else
                {
                    CalStatusIcon.Text = "⚠";
                    CalStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow/Warning
                    CalStatusText.Text = "Not Calibrated";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating calibration status icons: {ex.Message}", "UI");
            }
        }
        
        private void ResetCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeSide))
            {
                MessageBox.Show("Please start a stream (Left or Right) before resetting calibration.", "No Active Stream", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            ResetCalibration(_activeSide, _activeADCMode);
        }
        
        private void ResetCalibration(string side, byte adcMode)
        {
            try
            {
                string modeName = adcMode == 0 ? "Internal ADC" : "ADS1115";
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the {side} side calibration for {modeName}?\n\n" +
                    "This will allow you to recalibrate this side for this ADC mode.",
                    "Confirm Reset Calibration",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    LinearCalibration.DeleteCalibration(side, adcMode);
                    
                    // Reload calibrations
                    LoadCalibrations();
                    
                    // Update UI
                    UpdateWeightDisplays();
                    UpdateCalibrationStatusIcons();
                    
                    MessageBox.Show(
                        $"{side} side calibration for {modeName} has been deleted.\n\n" +
                        "You can now calibrate this side again.",
                        "Calibration Reset",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting calibration: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError($"Reset calibration error: {ex.Message}", "MainWindow");
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
        /// Update dashboard mode header to show current active side and ADC mode
        /// </summary>
        private void UpdateDashboardMode()
        {
            try
            {
                if (DashboardModeHeader == null) return;
                
                if (string.IsNullOrEmpty(_activeSide))
                {
                    DashboardModeHeader.Text = "No Active Stream";
                }
                else
                {
                    string adcModeText = _activeADCMode == 0 ? "Internal ADC (12-bit)" : "ADS1115 (16-bit)";
                    DashboardModeHeader.Text = $"{_activeSide} - {adcModeText}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating dashboard mode: {ex.Message}", "UI");
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
                        _currentADCMode = 1;
                        _activeADCMode = 1;
                        UpdateAdcModeIndicators("ADS1115");
                        UpdateWeightProcessorCalibration();
                        UpdateDashboardMode();
                        _logger.LogInfo("Switched to ADS1115 mode", "ADC");
                    }
                }
                else
                {
                    // Switch to Internal ADC
                    if (_canService != null)
                    {
                        _canService.SwitchToInternalADC();
                        _currentADCMode = 0;
                        _activeADCMode = 0;
                        UpdateAdcModeIndicators("Internal");
                        UpdateWeightProcessorCalibration();
                        UpdateDashboardMode();
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

        private void LoadConfiguration()
        {
            try
            {
                string configPath = System.IO.Path.Combine(Environment.CurrentDirectory, "Suspension_Config.json");
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                    if (config != null)
                    {
                        // Load adapter settings
                        if (config.ContainsKey("AdapterType") && AdapterTypeCombo != null)
                        {
                            string adapterType = config["AdapterType"].ToString() ?? "USB";
                            for (int i = 0; i < AdapterTypeCombo.Items.Count; i++)
                            {
                                if (AdapterTypeCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == adapterType)
                                {
                                    AdapterTypeCombo.SelectedIndex = i;
                                    // Trigger selection changed to update UI
                                    AdapterTypeCombo_SelectionChanged(AdapterTypeCombo, new System.Windows.Controls.SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
                                    break;
                                }
                            }
                        }
                        
                        if (config.ContainsKey("PcanChannel") && PcanChannelCombo != null)
                        {
                            string channel = config["PcanChannel"].ToString() ?? "USB1";
                            for (int i = 0; i < PcanChannelCombo.Items.Count; i++)
                            {
                                if (PcanChannelCombo.Items[i] is ComboBoxItem item && item.Content?.ToString() == channel)
                                {
                                    PcanChannelCombo.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                        
                        if (config.ContainsKey("BaudRate") && BaudRateCombo != null)
                        {
                            string baudRate = config["BaudRate"].ToString() ?? "500 kbps";
                            for (int i = 0; i < BaudRateCombo.Items.Count; i++)
                            {
                                if (BaudRateCombo.Items[i] is ComboBoxItem item && item.Content?.ToString() == baudRate)
                                {
                                    BaudRateCombo.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not load adapter configuration: {ex.Message}", "Settings");
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                string configPath = System.IO.Path.Combine(Environment.CurrentDirectory, "Suspension_Config.json");
                var config = new Dictionary<string, object>();
                
                // Load existing config if it exists
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    var existingConfig = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                    if (existingConfig != null)
                    {
                        config = existingConfig;
                    }
                }
                
                // Save adapter settings
                if (AdapterTypeCombo?.SelectedItem is ComboBoxItem adapterItem)
                {
                    config["AdapterType"] = adapterItem.Tag?.ToString() ?? "USB";
                }
                
                if (PcanChannelCombo?.SelectedItem is ComboBoxItem channelItem)
                {
                    config["PcanChannel"] = channelItem.Content?.ToString() ?? "USB1";
                }
                
                if (BaudRateCombo?.SelectedItem is ComboBoxItem baudItem)
                {
                    config["BaudRate"] = baudItem.Content?.ToString() ?? "500 kbps";
                }
                
                string jsonStringOut = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, jsonStringOut);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not save adapter configuration: {ex.Message}", "Settings");
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}