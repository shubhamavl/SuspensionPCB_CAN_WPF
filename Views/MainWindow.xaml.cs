using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
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
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Adapters;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer? _uiUpdateTimer;
        private DispatcherTimer? _clockTimer;

        private volatile int _totalMessages, _txMessages, _rxMessages;
        private readonly object _dataLock = new object();
        private DateTime _lastStatusUpdateTime = DateTime.MinValue;
        private double _statusUpdateRate = 0.0;
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
                    
                    // Update filter settings UI when panel is opened
                    if (FilterTypeCombo != null && FilterTypeCombo.SelectedItem == null)
                    {
                        LoadFilterSettings();
                    }
                    // Load display settings when panel opens
                    LoadDisplaySettings();
                    // Load UI visibility settings when panel opens
                    LoadUIVisibilitySettings();
                    // Load advanced settings when panel opens
                    LoadAdvancedSettings();
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
                // Check for Ctrl modifier
                bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                if (ctrlPressed)
                {
                    // Connection Management
                    if (e.Key == Key.K)
                    {
                        ConnectionToggle_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    // Streaming Control
                    else if (e.Key == Key.L)
                    {
                        RequestLeftBtn_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    else if (e.Key == Key.R)
                    {
                        RequestRightBtn_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    else if (e.Key == Key.S)
                    {
                        StopAll_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    // ADC Mode Switching
                    else if (e.Key == Key.I)
                    {
                        InternalADCBtn_Click(sender, e);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.A)
                    {
                        ADS1115Btn_Click(sender, e);
                        e.Handled = true;
                    }
                    // Window Management
                    else if (e.Key == Key.T)
                    {
                        SettingsToggle_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    else if (e.Key == Key.M)
                    {
                        OpenMonitorWindow_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    else if (e.Key == Key.P)
                    {
                        OpenLogFilesManager_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                }
                // Function Keys
                else if (e.Key == Key.F1)
                {
                    KeyboardShortcutsBtn_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.F5)
                {
                    if (_canService?.IsConnected == false)
                    {
                        ConnectionToggle_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
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
                // Use Dispatcher to ensure UI thread access
                Dispatcher.Invoke(() =>
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
                });
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
                    
                    // Flash back to red after configured duration
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    int flashMs = _settingsManager.Settings.TXIndicatorFlashMs;
                    timer.Interval = TimeSpan.FromMilliseconds(flashMs);
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

        private void PcanChannelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                // Save configuration immediately when PCAN channel changes
                SaveConfiguration();
                _logger.LogInfo("PCAN channel setting saved", "Settings");
            }
            catch (Exception ex)
            {
                _logger.LogError($"PCAN channel selection error: {ex.Message}", "Settings");
            }
        }

        private void BaudRateCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                // Save configuration immediately when baud rate changes
                SaveConfiguration();
                _logger.LogInfo("Baud rate setting saved", "Settings");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Baud rate selection error: {ex.Message}", "Settings");
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
                    
                    // Only query bootloader info if bootloader features are enabled
                    if (_settingsManager.Settings.EnableBootloaderFeatures)
                    {
                        _canService.RequestBootloaderInfo();
                    }
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
                        
                        // Ensure status is processed even if event doesn't fire
                        var statusArgs = new SystemStatusEventArgs
                        {
                            SystemStatus = systemStatus,
                            ErrorFlags = errorFlags,
                            ADCMode = adcMode,
                            Timestamp = DateTime.Now
                        };
                        HandleSystemStatus(_canService, statusArgs);
                    }
                    break;
            }
        }
        
        private void ProcessPendingMessages()
        {
            int processed = 0;
            int batchSize = _settingsManager.Settings.BatchProcessingSize;
            int messageLimit = _settingsManager.Settings.MessageHistoryLimit;
            while (_messageQueue.TryDequeue(out var vm) && processed < batchSize)
            {
                Messages.Add(vm);
                processed++;
                if (Messages.Count > messageLimit) Messages.RemoveAt(0);
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
                
                // Update weight display (formatted based on settings)
                if (WeightDisplayTxt != null)
                {
                    if (isCalibrated)
                    {
                        // Get weight display decimals from settings
                        int decimals = _settingsManager.Settings.WeightDisplayDecimals;
                        string format = decimals switch
                        {
                            1 => "F1",
                            2 => "F2",
                            _ => "F0"
                        };
                        
                        // Round and format based on setting
                        double roundedWeight = Math.Round(currentData.TaredWeight, decimals);
                        WeightDisplayTxt.Text = $"{roundedWeight.ToString(format)} kg";
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
            
            // Reset filters when calibration changes
            _weightProcessor.ResetFilters();
            
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
                
                // Reset filters when tare changes
                _weightProcessor.ResetFilters();
                
                UpdateWeightDisplays();
                string modeText = _activeADCMode == 0 ? "Internal" : "ADS1115";
                int roundedBaseline = (int)Math.Round(currentCalibratedKg);
                MessageBox.Show($"{_activeSide} side ({modeText}) tared successfully.\nBaseline: {roundedBaseline} kg", 
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
                
                // Reset filters when tare is reset
                _weightProcessor.ResetFilters();
                
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
                // Determine which side to calibrate
                string sideToCalibrate = _activeSide;
                
                // If no active side, ask user to select
                if (string.IsNullOrEmpty(sideToCalibrate))
                {
                    var sideDialog = new SideSelectionDialog();
                    if (sideDialog.ShowDialog() == true)
                    {
                        sideToCalibrate = sideDialog.SelectedSide;
                    }
                    else
                    {
                        return; // User cancelled
                    }
                }
                
                // Check if stream for selected side is already running
                bool streamRunning = sideToCalibrate == "Left" ? _leftStreamRunning : _rightStreamRunning;
                
                // Try to auto-start stream if not running and CAN is connected
                if (!streamRunning && _canService?.IsConnected == true)
                {
                    bool success = false;
                    if (sideToCalibrate == "Left")
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
                        _logger.LogInfo($"Auto-started {sideToCalibrate} stream for calibration at rate {_currentTransmissionRate}", "CAN");
                        UpdateStreamingIndicators();
                        
                        // Wait briefly for first data packet
                        System.Threading.Thread.Sleep(200);
                    }
                    else
                    {
                        _logger.LogWarning($"Could not auto-start {sideToCalibrate} stream - calibration will use manual entry mode", "CAN");
                    }
                }
                
                // Get current ADC mode (or default to Internal)
                byte currentADCMode = !string.IsNullOrEmpty(_activeSide) ? _activeADCMode : _currentADCMode;
                if (currentADCMode == 0 && SettingsManager.Instance != null)
                {
                    currentADCMode = SettingsManager.Instance.GetLastKnownADCMode();
                }
                
                // Open calibration dialog (works with or without stream)
                int calibrationDelayMs = _settingsManager.Settings.CalibrationCaptureDelayMs;
                var calibrationDialog = new CalibrationDialog(sideToCalibrate, currentADCMode, calibrationDelayMs);
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
            
            // Load filter settings
            LoadFilterSettings();
            
            // Load display settings
            LoadDisplaySettings();
            
            // Load UI visibility settings
            LoadUIVisibilitySettings();
            
            // Load advanced settings
            LoadAdvancedSettings();
            
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
            
            // Update bootloader UI visibility based on settings
            UpdateBootloaderUIVisibility();

            try
            {
                _canService = new CANService();
                
                // Apply data timeout from settings
                int dataTimeoutSeconds = _settingsManager.Settings.DataTimeoutSeconds;
                _canService.SetTimeout(TimeSpan.FromSeconds(dataTimeoutSeconds));
                
                _canService.MessageReceived += OnCANMessageReceived;
                _canService.RawDataReceived += OnRawDataReceived;
                _canService.SystemStatusReceived += HandleSystemStatus;
                _canService.DataTimeout += OnDataTimeout;
                
                // Only subscribe to bootloader events and initialize firmware service if enabled
                if (_settingsManager.Settings.EnableBootloaderFeatures)
                {
                    _canService.BootStatusReceived += OnBootStatusReceived;
                    _firmwareUpdateService = new FirmwareUpdateService(_canService);
                }
                _logger.LogInfo("CAN Service initialized successfully", "CANService");
            }
            catch (Exception ex)
            {
                _logger.LogError($"CAN Service initialization error: {ex.Message}", "CANService");
                MessageBox.Show($"CAN Service initialization failed: {ex.Message}",
                               "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // UI update timer - use setting from configuration (default 50ms)
            int uiUpdateRateMs = _settingsManager.Settings.UIUpdateRateMs;
            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(uiUpdateRateMs) };
            _uiUpdateTimer.Tick += (s, e) =>
        {
            try
            {
                    UpdateUI();
                    ProcessPendingMessages();
                    
                    // Check if status rate should be reset (no updates for 5 seconds)
                    if (_lastStatusUpdateTime != DateTime.MinValue && DataRateTxt != null)
                    {
                        TimeSpan timeSinceLastUpdate = DateTime.Now - _lastStatusUpdateTime;
                        if (timeSinceLastUpdate.TotalSeconds > 5.0)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                DataRateTxt.Text = "Rate: --";
                                _statusUpdateRate = 0.0;
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"UI Update error: {ex.Message}", "UI");
                }
            };
            _uiUpdateTimer.Start();

            int clockIntervalMs = _settingsManager.Settings.ClockUpdateIntervalMs;
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(clockIntervalMs) };
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
            
            // Initialize logging UI state
            InitializeLoggingUI();

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
                    // Show dialog with option to view version history
                    var noUpdateResult = MessageBox.Show(
                        $"You are already running the latest version.\n\nCurrent version: {info.CurrentVersion}\n\n" +
                        $"Would you like to view all available versions?",
                        "No Updates Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    
                    if (noUpdateResult == MessageBoxResult.Yes)
                    {
                        await ShowVersionHistoryDialogAsync();
                    }
                    return;
                }

                // Show custom dialog with options: Install Latest or View All Versions
                var updateOptionsWindow = new Window
                {
                    Title = "Update Available",
                    Width = 450,
                    Height = 250,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
                };

                var stackPanel = new StackPanel { Margin = new Thickness(20) };
                
                var titleText = new TextBlock
                {
                    Text = "Update Available",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stackPanel.Children.Add(titleText);

                var messageText = new TextBlock
                {
                    Text = $"Current version: {info.CurrentVersion}\nLatest version:  {info.LatestVersion}",
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 20),
                    TextWrapping = TextWrapping.Wrap
                };
                stackPanel.Children.Add(messageText);

                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                
                var installLatestBtn = new Button
                {
                    Content = "Install Latest",
                    Width = 140,
                    Height = 35,
                    Margin = new Thickness(5),
                    Background = new SolidColorBrush(Color.FromRgb(40, 167, 69)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand
                };
                installLatestBtn.Click += async (s, e) =>
                {
                    updateOptionsWindow.DialogResult = true;
                    updateOptionsWindow.Close();
                    await StartUpdateDownloadAsync(info);
                };
                buttonPanel.Children.Add(installLatestBtn);

                var viewAllBtn = new Button
                {
                    Content = "View All Versions",
                    Width = 140,
                    Height = 35,
                    Margin = new Thickness(5),
                    Background = new SolidColorBrush(Color.FromRgb(27, 94, 150)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand
                };
                viewAllBtn.Click += async (s, e) =>
                {
                    updateOptionsWindow.DialogResult = false;
                    updateOptionsWindow.Close();
                    await ShowVersionHistoryDialogAsync();
                };
                buttonPanel.Children.Add(viewAllBtn);

                var cancelBtn = new Button
                {
                    Content = "Cancel",
                    Width = 100,
                    Height = 35,
                    Margin = new Thickness(5),
                    Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand
                };
                cancelBtn.Click += (s, e) =>
                {
                    updateOptionsWindow.DialogResult = false;
                    updateOptionsWindow.Close();
                };
                buttonPanel.Children.Add(cancelBtn);

                stackPanel.Children.Add(buttonPanel);
                updateOptionsWindow.Content = stackPanel;

                updateOptionsWindow.ShowDialog();
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

        /// <summary>
        /// Shows a warning dialog when user attempts to downgrade to an older version.
        /// Returns true if user confirms the downgrade, false otherwise.
        /// </summary>
        private MessageBoxResult ShowDowngradeWarningDialog(Version currentVersion, Version targetVersion)
        {
            string message = 
                $"⚠️ You are about to downgrade from version {currentVersion} to version {targetVersion}.\n\n" +
                $"This may cause compatibility issues:\n" +
                $"• Settings or calibration data format may have changed\n" +
                $"• Features available in newer versions will be unavailable\n" +
                $"• Some data may not be compatible with the older version\n\n" +
                $"⚠️ Recommendation: Backup your settings and calibration data before proceeding.\n\n" +
                $"Do you want to continue with the downgrade?";

            return MessageBox.Show(message, 
                "Downgrade Warning", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
        }

        /// <summary>
        /// Shows the version history dialog and handles version selection and installation.
        /// </summary>
        private async Task ShowVersionHistoryDialogAsync()
        {
            try
            {
                var dialog = new VersionSelectionDialog
                {
                    Owner = this
                };

                bool? result = dialog.ShowDialog();
                
                if (result == true && dialog.SelectedVersionInfo != null)
                {
                    var selectedInfo = dialog.SelectedVersionInfo;
                    var currentVersion = GetCurrentVersion();
                    
                    // Check if this is a downgrade
                    if (selectedInfo.LatestVersion < currentVersion)
                    {
                        var warningResult = ShowDowngradeWarningDialog(currentVersion, selectedInfo.LatestVersion);
                        if (warningResult != MessageBoxResult.Yes)
                        {
                            _logger.LogInfo("User cancelled downgrade after warning", "Update");
                            return;
                        }
                    }
                    
                    // Proceed with download/install
                    await StartUpdateDownloadAsync(selectedInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing version history dialog: {ex.Message}", "Update");
                MessageBox.Show($"Error opening version history: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Version GetCurrentVersion()
        {
            try
            {
                var assembly = typeof(App).Assembly;
                var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion)
                    && Version.TryParse(infoAttr.InformationalVersion.Split('+')[0], out var infoVersion))
                {
                    return infoVersion;
                }

                var asmVersion = assembly.GetName().Version;
                if (asmVersion != null)
                    return asmVersion;
            }
            catch
            {
                // Fall through to default
            }

            return new Version(1, 0, 0, 0);
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

        private void LoadFilterSettings()
        {
            try
            {
                var settings = _settingsManager.Settings;
                
                if (FilterEnabledCheckBox != null)
                {
                    FilterEnabledCheckBox.IsChecked = settings.FilterEnabled;
                }
                
                // Set filter type
                if (FilterTypeCombo != null)
                {
                    foreach (System.Windows.Controls.ComboBoxItem item in FilterTypeCombo.Items)
                    {
                        if (item.Tag?.ToString() == settings.FilterType)
                        {
                            FilterTypeCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                if (EmaAlphaSlider != null)
                {
                    EmaAlphaSlider.Value = settings.FilterAlpha;
                }
                
                if (SmaWindowSlider != null)
                {
                    SmaWindowSlider.Value = settings.FilterWindowSize;
                }
                
                // Apply settings to WeightProcessor (runtime only, don't save again)
                bool enabled = settings.FilterEnabled;
                FilterType type = settings.FilterType switch
                {
                    "EMA" => FilterType.EMA,
                    "SMA" => FilterType.SMA,
                    _ => FilterType.None
                };
                _weightProcessor.ConfigureFilter(type, settings.FilterAlpha, settings.FilterWindowSize, enabled);
                
                _logger.LogInfo($"Filter settings loaded: Type={settings.FilterType}, Alpha={settings.FilterAlpha}, Window={settings.FilterWindowSize}, Enabled={settings.FilterEnabled}", "Settings");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading filter settings: {ex.Message}", "Settings");
            }
        }

        private void LoadDisplaySettings()
        {
            try
            {
                var settings = _settingsManager.Settings;
                
                // Set weight display format
                if (WeightFormatCombo != null)
                {
                    foreach (System.Windows.Controls.ComboBoxItem item in WeightFormatCombo.Items)
                    {
                        if (item.Tag?.ToString() == settings.WeightDisplayDecimals.ToString())
                        {
                            WeightFormatCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                // Set UI update rate
                if (UIUpdateRateSlider != null)
                {
                    UIUpdateRateSlider.Value = settings.UIUpdateRateMs;
                    // Update display text
                    if (UIUpdateRateValueTxt != null)
                    {
                        double rateHz = 1000.0 / settings.UIUpdateRateMs;
                        UIUpdateRateValueTxt.Text = $"{settings.UIUpdateRateMs} ms ({rateHz:F1} Hz)";
                    }
                }
                
                // Set data timeout
                if (DataTimeoutSlider != null)
                {
                    DataTimeoutSlider.Value = settings.DataTimeoutSeconds;
                    // Update display text
                    if (DataTimeoutValueTxt != null)
                    {
                        int timeoutSeconds = settings.DataTimeoutSeconds;
                        DataTimeoutValueTxt.Text = $"{timeoutSeconds} second{(timeoutSeconds == 1 ? "" : "s")}";
                    }
                }
                
                // Apply settings to components (but don't save - already loaded from file)
                // Only apply to runtime components, not save again
                if (_uiUpdateTimer != null)
                {
                    _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(settings.UIUpdateRateMs);
                }
                
                if (_canService != null)
                {
                    _canService.SetTimeout(TimeSpan.FromSeconds(settings.DataTimeoutSeconds));
                }
                
                // Update weight display immediately if active
                UpdateWeightDisplays();
                
                _logger.LogInfo($"Display settings loaded: WeightDecimals={settings.WeightDisplayDecimals}, UIUpdateRate={settings.UIUpdateRateMs}ms, DataTimeout={settings.DataTimeoutSeconds}s", "Settings");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading display settings: {ex.Message}", "Settings");
            }
        }

        private void WeightFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyDisplaySettings();
        }

        private void UIUpdateRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (UIUpdateRateValueTxt != null)
                {
                    int rateMs = (int)e.NewValue;
                    double rateHz = 1000.0 / rateMs;
                    UIUpdateRateValueTxt.Text = $"{rateMs} ms ({rateHz:F1} Hz)";
                }
                ApplyDisplaySettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"UI update rate slider error: {ex.Message}", "Settings");
            }
        }

        private void DataTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (DataTimeoutValueTxt != null)
                {
                    int timeoutSeconds = (int)e.NewValue;
                    DataTimeoutValueTxt.Text = $"{timeoutSeconds} second{(timeoutSeconds == 1 ? "" : "s")}";
                }
                ApplyDisplaySettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Data timeout slider error: {ex.Message}", "Settings");
            }
        }


        // UI Visibility Settings Methods
        private void LoadUIVisibilitySettings()
        {
            try
            {
                var settings = _settingsManager.Settings;
                
                // Set status banner duration
                if (StatusBannerDurationSlider != null)
                {
                    StatusBannerDurationSlider.Value = settings.StatusBannerDurationMs / 1000.0; // Convert ms to seconds
                    if (StatusBannerDurationValueTxt != null)
                    {
                        int seconds = settings.StatusBannerDurationMs / 1000;
                        StatusBannerDurationValueTxt.Text = $"{seconds} second{(seconds == 1 ? "" : "s")}";
                    }
                }
                
                // Set message history limit
                if (MessageHistoryLimitSlider != null)
                {
                    MessageHistoryLimitSlider.Value = settings.MessageHistoryLimit;
                    if (MessageHistoryLimitValueTxt != null)
                    {
                        MessageHistoryLimitValueTxt.Text = $"{settings.MessageHistoryLimit} messages";
                    }
                }
                
                // Set show/hide checkboxes
                if (ShowRawADCCheckBox != null)
                    ShowRawADCCheckBox.IsChecked = settings.ShowRawADC;
                if (ShowCalibratedWeightCheckBox != null)
                    ShowCalibratedWeightCheckBox.IsChecked = settings.ShowCalibratedWeight;
                if (ShowStreamingIndicatorsCheckBox != null)
                    ShowStreamingIndicatorsCheckBox.IsChecked = settings.ShowStreamingIndicators;
                if (ShowCalibrationIconsCheckBox != null)
                    ShowCalibrationIconsCheckBox.IsChecked = settings.ShowCalibrationIcons;
                
                // Apply visibility to UI elements (runtime only, don't save again)
                if (RawTxt != null)
                {
                    var parent = RawTxt.Parent as FrameworkElement;
                    if (parent != null)
                    {
                        parent.Visibility = settings.ShowRawADC ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                
                // Apply visibility to streaming indicators
                if (StreamIndicator != null)
                {
                    StreamIndicator.Visibility = settings.ShowStreamingIndicators ? Visibility.Visible : Visibility.Collapsed;
                }
                if (StreamStatusTxt != null)
                {
                    StreamStatusTxt.Visibility = settings.ShowStreamingIndicators ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Apply visibility to calibration icons
                if (CalStatusIcon != null)
                {
                    CalStatusIcon.Visibility = settings.ShowCalibrationIcons ? Visibility.Visible : Visibility.Collapsed;
                }
                if (CalStatusText != null)
                {
                    CalStatusText.Visibility = settings.ShowCalibrationIcons ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load UI visibility settings: {ex.Message}", "Settings");
            }
        }

        private void ApplyUIVisibilitySettings()
        {
            try
            {
                int statusBannerDuration = (int)(StatusBannerDurationSlider?.Value ?? 3) * 1000; // Convert to ms
                int messageHistoryLimit = (int)(MessageHistoryLimitSlider?.Value ?? 1000);
                bool showRawADC = ShowRawADCCheckBox?.IsChecked ?? true;
                bool showCalibratedWeight = ShowCalibratedWeightCheckBox?.IsChecked ?? false;
                bool showStreamingIndicators = ShowStreamingIndicatorsCheckBox?.IsChecked ?? true;
                bool showCalibrationIcons = ShowCalibrationIconsCheckBox?.IsChecked ?? true;
                
                // Save settings
                _settingsManager.SetUIVisibilitySettings(statusBannerDuration, messageHistoryLimit, 
                    showRawADC, showCalibratedWeight, showStreamingIndicators, showCalibrationIcons);
                
                // Apply visibility to UI elements
                if (RawTxt != null)
                {
                    var parent = RawTxt.Parent as FrameworkElement;
                    if (parent != null)
                    {
                        parent.Visibility = showRawADC ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                
                // Apply visibility to streaming indicators
                if (StreamIndicator != null)
                {
                    StreamIndicator.Visibility = showStreamingIndicators ? Visibility.Visible : Visibility.Collapsed;
                }
                if (StreamStatusTxt != null)
                {
                    StreamStatusTxt.Visibility = showStreamingIndicators ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Apply visibility to calibration icons
                if (CalStatusIcon != null)
                {
                    CalStatusIcon.Visibility = showCalibrationIcons ? Visibility.Visible : Visibility.Collapsed;
                }
                if (CalStatusText != null)
                {
                    CalStatusText.Visibility = showCalibrationIcons ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Note: Calibrated weight display would need a UI element - for now just save the setting
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to apply UI visibility settings: {ex.Message}", "Settings");
            }
        }

        private void StatusBannerDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (StatusBannerDurationValueTxt != null)
                {
                    int seconds = (int)e.NewValue;
                    StatusBannerDurationValueTxt.Text = $"{seconds} second{(seconds == 1 ? "" : "s")}";
                }
                ApplyUIVisibilitySettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Status banner duration slider error: {ex.Message}", "Settings");
            }
        }

        private void MessageHistoryLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (MessageHistoryLimitValueTxt != null)
                {
                    int limit = (int)e.NewValue;
                    MessageHistoryLimitValueTxt.Text = $"{limit} messages";
                }
                ApplyUIVisibilitySettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Message history limit slider error: {ex.Message}", "Settings");
            }
        }

        private void ShowRawADCCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyUIVisibilitySettings();
        }

        private void ShowCalibratedWeightCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyUIVisibilitySettings();
        }

        private void ShowStreamingIndicatorsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyUIVisibilitySettings();
        }

        private void ShowCalibrationIconsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyUIVisibilitySettings();
        }

        // Advanced Settings Methods
        private void LoadAdvancedSettings()
        {
            try
            {
                var settings = _settingsManager.Settings;
                
                // Set TX indicator flash duration
                if (TXIndicatorFlashSlider != null)
                {
                    TXIndicatorFlashSlider.Value = settings.TXIndicatorFlashMs;
                    if (TXIndicatorFlashValueTxt != null)
                    {
                        TXIndicatorFlashValueTxt.Text = $"{settings.TXIndicatorFlashMs} ms";
                    }
                }
                
                // Set batch processing size
                if (BatchProcessingSizeSlider != null)
                {
                    BatchProcessingSizeSlider.Value = settings.BatchProcessingSize;
                    if (BatchProcessingSizeValueTxt != null)
                    {
                        BatchProcessingSizeValueTxt.Text = $"{settings.BatchProcessingSize} messages";
                    }
                }
                
                // Set clock update interval
                if (ClockUpdateIntervalSlider != null)
                {
                    ClockUpdateIntervalSlider.Value = settings.ClockUpdateIntervalMs;
                    if (ClockUpdateIntervalValueTxt != null)
                    {
                        ClockUpdateIntervalValueTxt.Text = $"{settings.ClockUpdateIntervalMs} ms";
                    }
                }
                
                // Set calibration capture delay
                if (CalibrationCaptureDelaySlider != null)
                {
                    CalibrationCaptureDelaySlider.Value = settings.CalibrationCaptureDelayMs;
                    if (CalibrationCaptureDelayValueTxt != null)
                    {
                        CalibrationCaptureDelayValueTxt.Text = $"{settings.CalibrationCaptureDelayMs} ms";
                    }
                }
                
                // Set log format
                if (LogFormatCombo != null)
                {
                    foreach (System.Windows.Controls.ComboBoxItem item in LogFormatCombo.Items)
                    {
                        if (item.Tag?.ToString() == settings.LogFileFormat)
                        {
                            LogFormatCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                // Set show calibration quality metrics
                if (ShowCalibrationQualityMetricsCheckBox != null)
                    ShowCalibrationQualityMetricsCheckBox.IsChecked = settings.ShowCalibrationQualityMetrics;
                
                // Set enable bootloader features
                if (EnableBootloaderFeaturesCheckBox != null)
                    EnableBootloaderFeaturesCheckBox.IsChecked = settings.EnableBootloaderFeatures;
                
                // Set calibration averaging enabled
                if (CalibrationAveragingEnabledCheckBox != null)
                    CalibrationAveragingEnabledCheckBox.IsChecked = settings.CalibrationAveragingEnabled;
                
                // Set calibration averaging settings
                if (CalibrationSampleCountSlider != null)
                {
                    CalibrationSampleCountSlider.Value = settings.CalibrationSampleCount;
                    if (CalibrationSampleCountValueTxt != null)
                    {
                        CalibrationSampleCountValueTxt.Text = $"{settings.CalibrationSampleCount}";
                    }
                }
                
                if (CalibrationCaptureDurationSlider != null)
                {
                    CalibrationCaptureDurationSlider.Value = settings.CalibrationCaptureDurationMs;
                    if (CalibrationCaptureDurationValueTxt != null)
                    {
                        CalibrationCaptureDurationValueTxt.Text = $"{settings.CalibrationCaptureDurationMs} ms";
                    }
                }
                
                if (CalibrationUseMedianCheckBox != null)
                    CalibrationUseMedianCheckBox.IsChecked = settings.CalibrationUseMedian;
                
                if (CalibrationRemoveOutliersCheckBox != null)
                    CalibrationRemoveOutliersCheckBox.IsChecked = settings.CalibrationRemoveOutliers;
                
                if (CalibrationOutlierThresholdSlider != null)
                {
                    CalibrationOutlierThresholdSlider.Value = settings.CalibrationOutlierThreshold;
                    if (CalibrationOutlierThresholdValueTxt != null)
                    {
                        CalibrationOutlierThresholdValueTxt.Text = $"{settings.CalibrationOutlierThreshold:F1} σ";
                    }
                }
                
                if (CalibrationMaxStdDevSlider != null)
                {
                    CalibrationMaxStdDevSlider.Value = settings.CalibrationMaxStdDev;
                    if (CalibrationMaxStdDevValueTxt != null)
                    {
                        CalibrationMaxStdDevValueTxt.Text = $"{settings.CalibrationMaxStdDev:F1}";
                    }
                }
                
                // Apply runtime settings (timer intervals, etc.) without saving
                if (_clockTimer != null)
                {
                    _clockTimer.Interval = TimeSpan.FromMilliseconds(settings.ClockUpdateIntervalMs);
                }
                
                // Update bootloader UI visibility
                UpdateBootloaderUIVisibility();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load advanced settings: {ex.Message}", "Settings");
            }
        }

        private void ApplyAdvancedSettings()
        {
            try
            {
                int txFlashMs = (int)(TXIndicatorFlashSlider?.Value ?? 200);
                string logFormat = "CSV"; // Default to CSV for now
                if (LogFormatCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem formatItem)
                {
                    logFormat = formatItem.Tag?.ToString() ?? "CSV";
                }
                int batchSize = (int)(BatchProcessingSizeSlider?.Value ?? 50);
                int clockInterval = (int)(ClockUpdateIntervalSlider?.Value ?? 1000);
                int calibrationDelay = (int)(CalibrationCaptureDelaySlider?.Value ?? 500);
                bool showQualityMetrics = ShowCalibrationQualityMetricsCheckBox?.IsChecked ?? true;
                
                // Save settings
                _settingsManager.SetAdvancedSettings(txFlashMs, logFormat, batchSize, clockInterval, calibrationDelay, showQualityMetrics);
                
                // Save bootloader setting
                bool enableBootloader = EnableBootloaderFeaturesCheckBox?.IsChecked ?? true;
                _settingsManager.SetBootloaderFeaturesEnabled(enableBootloader);
                
                // Save calibration averaging settings
                bool averagingEnabled = CalibrationAveragingEnabledCheckBox?.IsChecked ?? true;
                int sampleCount = (int)(CalibrationSampleCountSlider?.Value ?? 50);
                int durationMs = (int)(CalibrationCaptureDurationSlider?.Value ?? 2000);
                bool useMedian = CalibrationUseMedianCheckBox?.IsChecked ?? true;
                bool removeOutliers = CalibrationRemoveOutliersCheckBox?.IsChecked ?? true;
                double outlierThreshold = CalibrationOutlierThresholdSlider?.Value ?? 2.0;
                double maxStdDev = CalibrationMaxStdDevSlider?.Value ?? 10.0;
                _settingsManager.SetCalibrationAveragingSettings(averagingEnabled, sampleCount, durationMs, useMedian, removeOutliers, outlierThreshold, maxStdDev);
                
                // Update bootloader UI visibility
                UpdateBootloaderUIVisibility();
                
                // Apply clock update interval to timer
                if (_clockTimer != null)
                {
                    _clockTimer.Interval = TimeSpan.FromMilliseconds(clockInterval);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to apply advanced settings: {ex.Message}", "Settings");
            }
        }

        private void TXIndicatorFlashSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (TXIndicatorFlashValueTxt != null)
                {
                    int flashMs = (int)e.NewValue;
                    TXIndicatorFlashValueTxt.Text = $"{flashMs} ms";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"TX indicator flash slider error: {ex.Message}", "Settings");
            }
        }

        private void BatchProcessingSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (BatchProcessingSizeValueTxt != null)
                {
                    int batchSize = (int)e.NewValue;
                    BatchProcessingSizeValueTxt.Text = $"{batchSize} messages";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Batch processing size slider error: {ex.Message}", "Settings");
            }
        }

        private void ClockUpdateIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (ClockUpdateIntervalValueTxt != null)
                {
                    int intervalMs = (int)e.NewValue;
                    ClockUpdateIntervalValueTxt.Text = $"{intervalMs} ms";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Clock update interval slider error: {ex.Message}", "Settings");
            }
        }

        private void CalibrationCaptureDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (CalibrationCaptureDelayValueTxt != null)
                {
                    int delayMs = (int)e.NewValue;
                    CalibrationCaptureDelayValueTxt.Text = $"{delayMs} ms";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Calibration capture delay slider error: {ex.Message}", "Settings");
            }
        }

        private void LogFormatCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ApplyAdvancedSettings();
        }

        private void ShowCalibrationQualityMetricsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAdvancedSettings();
        }

        private void EnableBootloaderFeaturesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAdvancedSettings();
        }

        private void CalibrationAveragingEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAdvancedSettings();
        }

        private void CalibrationSampleCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (CalibrationSampleCountValueTxt != null)
                {
                    int sampleCount = (int)e.NewValue;
                    CalibrationSampleCountValueTxt.Text = $"{sampleCount}";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Calibration sample count slider error: {ex.Message}", "Settings");
            }
        }

        private void CalibrationCaptureDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (CalibrationCaptureDurationValueTxt != null)
                {
                    int durationMs = (int)e.NewValue;
                    CalibrationCaptureDurationValueTxt.Text = $"{durationMs} ms";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Calibration capture duration slider error: {ex.Message}", "Settings");
            }
        }

        private void CalibrationUseMedianCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAdvancedSettings();
        }

        private void CalibrationRemoveOutliersCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAdvancedSettings();
        }

        private void CalibrationOutlierThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (CalibrationOutlierThresholdValueTxt != null)
                {
                    double threshold = e.NewValue;
                    CalibrationOutlierThresholdValueTxt.Text = $"{threshold:F1} σ";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Calibration outlier threshold slider error: {ex.Message}", "Settings");
            }
        }

        private void CalibrationMaxStdDevSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (CalibrationMaxStdDevValueTxt != null)
                {
                    double maxStdDev = e.NewValue;
                    CalibrationMaxStdDevValueTxt.Text = $"{maxStdDev:F1}";
                }
                ApplyAdvancedSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Calibration max std dev slider error: {ex.Message}", "Settings");
            }
        }

        private void EnableBootloaderFeaturesInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Bootloader Features:\n\n" +
                "When enabled:\n" +
                "• Firmware Update section is visible\n" +
                "• Bootloader info is queried automatically on connection\n" +
                "• Bootloader status events are processed\n" +
                "• Firmware update service is initialized\n\n" +
                "When disabled:\n" +
                "• All bootloader UI is hidden\n" +
                "• No automatic bootloader queries\n" +
                "• Bootloader events are ignored\n" +
                "• Firmware update service is not initialized\n\n" +
                "Note: Restart the application or reconnect after changing this setting for full effect.",
                "Bootloader Features Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void UpdateBootloaderUIVisibility()
        {
            try
            {
                bool enabled = _settingsManager.Settings.EnableBootloaderFeatures;
                
                Dispatcher.Invoke(() =>
                {
                    if (FirmwareUpdateGroupBox != null)
                    {
                        FirmwareUpdateGroupBox.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to update bootloader UI visibility: {ex.Message}", "UI");
            }
        }

        // Info Button Event Handlers
        private void WeightFilteringInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            string content = @"WEIGHT FILTERING SETTINGS

These settings help reduce noise and jitter in weight measurements from ADC (Analog-to-Digital Converter) readings.

ENABLE WEIGHT FILTERING
• Purpose: Master switch to enable or disable all filtering
• When to use: Disable if you need raw, unfiltered data for analysis
• Default: Enabled
• Impact: When disabled, weight values will show more variation due to ADC noise

FILTER TYPE
• None: No filtering applied - shows raw calibrated values
  → Use when: You need maximum responsiveness and can tolerate noise
  → Best for: Real-time analysis, debugging, or when external filtering is used

• EMA (Exponential Moving Average): Recommended for most cases
  → How it works: Gives more weight to recent values, responds quickly to changes
  → Alpha value: Controls smoothing (0.0 = very smooth/slow, 1.0 = no filtering)
  → Default Alpha: 0.15 (good balance)
  → Lower Alpha (0.05-0.10): Smoother, slower response - good for stable loads
  → Higher Alpha (0.20-0.30): Faster response, more noise - good for dynamic loads
  → Use when: You want smooth display with quick response to weight changes
  → Best for: General purpose use, most applications

• SMA (Simple Moving Average): Simple averaging of last N samples
  → How it works: Averages the last N weight values equally
  → Window Size: Number of samples to average (more = smoother but slower)
  → Default Window: 10 samples
  → Small Window (5-10): Faster response, some noise
  → Large Window (20-50): Very smooth, slower response
  → Use when: You want consistent smoothing regardless of recent values
  → Best for: Static loads, when consistent smoothing is more important than responsiveness

WHY FILTERING IS NEEDED
ADC readings naturally fluctuate due to:
• Electrical noise in the system
• Temperature variations
• Power supply ripple
• Load cell signal conditioning
• ADC quantization errors

Without filtering, weight values can jump ±0.1-0.5 kg even when the load is stable.

FILTERING RECOMMENDATIONS
• For stable loads (static weighing): EMA with Alpha 0.10-0.15, or SMA with Window 15-20
• For dynamic loads (moving objects): EMA with Alpha 0.20-0.30, or SMA with Window 5-10
• For maximum accuracy: EMA with Alpha 0.10-0.15 (recommended)
• For debugging: Disable filtering to see raw ADC behavior

FILTER RESET
Filters automatically reset when:
• Tare is applied
• Tare is reset
• Calibration is updated

This ensures filters start fresh with new calibration or tare values.";

            ShowSettingsInfo("Weight Filtering Settings", content);
        }

        private void DisplaySettingsInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            string content = @"DISPLAY SETTINGS

These settings control how weight values are displayed and how the UI updates.

WEIGHT DISPLAY FORMAT
• Integer (50 kg): Rounds to nearest whole number
  → Use when: You need simple, easy-to-read values
  → Best for: General use, when precision beyond 1 kg isn't needed
  → Example: 50.7 kg displays as ""51 kg""

• One Decimal (50.3 kg): Shows one decimal place
  → Use when: You need moderate precision
  → Best for: Most applications, good balance of readability and precision
  → Example: 50.67 kg displays as ""50.7 kg""

• Two Decimals (50.25 kg): Shows two decimal places
  → Use when: You need high precision for analysis
  → Best for: Calibration, testing, when small differences matter
  → Example: 50.678 kg displays as ""50.68 kg""

Note: The actual precision depends on your calibration accuracy and filtering settings.

UI UPDATE RATE
Controls how often the weight display refreshes on screen.

• Range: 10ms (100 Hz) to 200ms (5 Hz)
• Default: 50ms (20 Hz)
• Lower values (10-30ms): Faster updates, smoother display
  → Use when: You want real-time feel, high-frequency monitoring
  → Trade-off: Higher CPU usage, may cause UI lag on slower systems

• Medium values (40-80ms): Balanced performance
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of responsiveness and performance

• Higher values (100-200ms): Better performance, less frequent updates
  → Use when: System performance is critical, or updates aren't critical
  → Trade-off: Less responsive feel, but smoother on slower systems

Note: This only affects UI display rate. Data processing still happens at full speed (1kHz).

DATA TIMEOUT
Controls how long the system waits before reporting ""No Data"" when CAN messages stop.

• Range: 1-30 seconds
• Default: 5 seconds
• Short timeout (1-3 seconds): Quick detection of connection issues
  → Use when: You need immediate feedback if data stops
  → Trade-off: May trigger false alarms during brief interruptions

• Medium timeout (4-8 seconds): Balanced detection
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of responsiveness and tolerance

• Long timeout (10-30 seconds): Tolerant of brief interruptions
  → Use when: Network may have occasional delays, or you want fewer alerts
  → Trade-off: Slower detection of real connection problems";

            ShowSettingsInfo("Display Settings", content);
        }

        private void UIVisibilityInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            string content = @"UI VISIBILITY SETTINGS

These settings control which UI elements are visible, helping you customize the interface to your needs.

STATUS BANNER DURATION
Controls how long status messages (success/error notifications) are displayed.

• Range: 1-10 seconds
• Default: 3 seconds
• Short duration (1-2 seconds): Quick notifications, less screen clutter
  → Use when: You want brief, non-intrusive notifications
  → Trade-off: May miss notifications if you're not watching

• Medium duration (3-5 seconds): Balanced visibility
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of visibility and screen space

• Long duration (6-10 seconds): Maximum visibility
  → Use when: You want to ensure you see all notifications
  → Trade-off: Notifications stay longer, may cover content

MESSAGE HISTORY LIMIT
Controls how many CAN messages are stored in memory for the message history display.

• Range: 100-5000 messages
• Default: 1000 messages
• Low limit (100-500): Less memory usage, faster scrolling
  → Use when: You only need recent messages, or have limited memory
  → Trade-off: Older messages are discarded quickly

• Medium limit (800-1500): Balanced storage
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of history and performance

• High limit (2000-5000): Maximum history retention
  → Use when: You need to analyze long message sequences
  → Trade-off: Higher memory usage, may slow down scrolling

Note: When limit is reached, oldest messages are automatically removed.

SHOW RAW ADC DISPLAY
Controls visibility of the raw ADC value display.

• Enabled: Shows raw ADC value (e.g., ""1850"")
  → Use when: You need to see raw sensor readings for debugging or analysis
  → Useful for: Calibration, troubleshooting, understanding ADC behavior

• Disabled: Hides raw ADC display
  → Use when: You only care about weight values, want cleaner interface
  → Useful for: Production use, simplified display

SHOW CALIBRATED WEIGHT (BEFORE TARE)
Controls visibility of calibrated weight before tare is applied.

• Enabled: Shows both calibrated and tared weight
  → Use when: You need to see weight before and after tare
  → Useful for: Understanding tare effect, debugging tare issues

• Disabled: Only shows tared weight
  → Use when: You only care about final weight after tare
  → Useful for: Normal operation, simplified display

Note: This setting is saved for future UI enhancements.

SHOW STREAMING STATUS INDICATORS
Controls visibility of streaming status indicators (green/gray dot and ""Left Active"" / ""Right Active"" text).

• Enabled: Shows streaming indicators
  → Use when: You want visual confirmation of active streams
  → Useful for: Quick status check, debugging stream issues

• Disabled: Hides streaming indicators
  → Use when: You want minimal UI, or status isn't needed
  → Useful for: Clean interface, when you know stream status from other sources

SHOW CALIBRATION STATUS ICONS
Controls visibility of calibration status icons (✓ for calibrated, ⚠ for uncalibrated).

• Enabled: Shows calibration icons
  → Use when: You want quick visual confirmation of calibration status
  → Useful for: Ensuring calibration before use, troubleshooting

• Disabled: Hides calibration icons
  → Use when: You want minimal UI, or calibration status isn't needed
  → Useful for: Clean interface, when calibration is always done

CUSTOMIZATION TIPS
• For production use: Hide raw ADC, hide calibrated weight, show indicators
• For debugging: Show everything
• For minimal UI: Hide all optional elements
• For analysis: Show raw ADC and calibrated weight";

            ShowSettingsInfo("UI Visibility Settings", content);
        }

        private void AdvancedSettingsInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            string content = @"ADVANCED SETTINGS

These settings control low-level behavior and are typically only adjusted for specific needs or troubleshooting.

TX INDICATOR FLASH DURATION
Controls how long the TX (transmit) indicator flashes white when sending CAN messages.

• Range: 50-500 milliseconds
• Default: 200ms
• Short flash (50-100ms): Brief visual feedback
  → Use when: You want subtle, quick feedback
  → Trade-off: May be hard to see

• Medium flash (150-250ms): Balanced visibility
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of visibility and speed

• Long flash (300-500ms): Maximum visibility
  → Use when: You want clear visual confirmation of transmissions
  → Trade-off: Longer visual feedback

BATCH PROCESSING SIZE
Controls how many CAN messages are processed per UI update cycle.

• Range: 10-100 messages
• Default: 50 messages
• Small batch (10-30): More frequent processing, smoother UI
  → Use when: You want responsive UI, or have low message rates
  → Trade-off: More processing overhead

• Medium batch (40-60): Balanced processing
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of responsiveness and efficiency

• Large batch (70-100): Maximum efficiency, less frequent updates
  → Use when: You have high message rates (1kHz), or want maximum throughput
  → Trade-off: Less frequent UI updates, but better performance

Note: This is critical for 1kHz data rates. Too small batches may cause message queue buildup.

CLOCK UPDATE INTERVAL
Controls how often the clock display (timestamp) is updated.

• Range: 500-5000 milliseconds
• Default: 1000ms (1 second)
• Fast update (500-800ms): More frequent clock updates
  → Use when: You want precise time display
  → Trade-off: Slightly more CPU usage

• Medium update (1000-2000ms): Standard clock updates
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of accuracy and performance

• Slow update (3000-5000ms): Less frequent updates
  → Use when: Clock accuracy isn't critical, or you want to save CPU
  → Trade-off: Clock may appear ""stale""

CALIBRATION CAPTURE DELAY
Controls how long the system waits before capturing an ADC reading during calibration.

• Range: 100-2000 milliseconds
• Default: 500ms
• Short delay (100-300ms): Faster calibration
  → Use when: ADC readings stabilize quickly, or you're in a hurry
  → Trade-off: May capture unstable readings

• Medium delay (400-800ms): Balanced capture timing
  → Use when: General purpose use (recommended)
  → Trade-off: Good balance of speed and stability

• Long delay (1000-2000ms): Maximum stability
  → Use when: ADC readings take time to stabilize, or you want maximum accuracy
  → Trade-off: Slower calibration process

Why delay is needed: After placing a weight, ADC readings need time to stabilize due to:
• Mechanical settling of load cells
• Electrical signal conditioning
• ADC sampling and filtering

LOG FILE FORMAT
Controls the format of saved log files (currently CSV only, JSON/TXT coming soon).

• CSV (Comma Separated Values): Standard spreadsheet format
  → Use when: You want to analyze data in Excel, Python, or other tools
  → Best for: Most applications, data analysis
  → Format: Columns separated by commas, one row per data point

• JSON (JavaScript Object Notation): Structured data format (coming soon)
  → Use when: You want structured, parseable data
  → Best for: Programmatic analysis, web applications

• TXT (Plain Text): Human-readable text format (coming soon)
  → Use when: You want simple, readable logs
  → Best for: Quick review, simple analysis

SHOW CALIBRATION QUALITY METRICS
Controls whether calibration quality metrics (R² coefficient, error percentage) are displayed.

• Enabled: Shows R² and maximum error percentage
  → Use when: You want to verify calibration quality
  → Useful for: Ensuring accurate calibration, troubleshooting

• Disabled: Hides quality metrics
  → Use when: You trust your calibration, or want simpler display
  → Useful for: Clean interface, when metrics aren't needed

R² (R-squared) indicates how well the calibration fits:
• R² > 0.99: Excellent fit
• R² > 0.95: Good fit
• R² < 0.95: May need more calibration points or check for issues

WHEN TO ADJUST ADVANCED SETTINGS
• Performance issues: Adjust batch processing size or UI update rate
• Calibration problems: Adjust calibration capture delay
• Visual feedback issues: Adjust TX indicator flash duration
• Clock accuracy needs: Adjust clock update interval

Most users should keep default values unless experiencing specific issues.";

            ShowSettingsInfo("Advanced Settings", content);
        }

        private void ShowSettingsInfo(string title, string content)
        {
            try
            {
                var infoDialog = new SettingsInfoDialog(title, content);
                infoDialog.Owner = this;
                infoDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing settings info: {ex.Message}", "Settings");
                MessageBox.Show($"Error displaying settings information: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Individual Setting Info Button Handlers
        private void FilterEnabledInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Enable Weight Filtering", 
                "ENABLE WEIGHT FILTERING\n\n" +
                "Master switch to enable or disable all filtering.\n\n" +
                "• Enabled: Weight values are filtered to reduce noise and jitter\n" +
                "• Disabled: Shows raw, unfiltered calibrated values\n\n" +
                "When to disable:\n" +
                "• When you need raw data for analysis\n" +
                "• When debugging ADC behavior\n" +
                "• When external filtering is used\n\n" +
                "Default: Enabled (recommended for most cases)");
        }

        private void FilterTypeInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Filter Type", 
                "FILTER TYPE SELECTION\n\n" +
                "Choose the filtering algorithm:\n\n" +
                "EMA (Exponential Moving Average) - RECOMMENDED\n" +
                "• Fast response to weight changes\n" +
                "• Smooth output with minimal delay\n" +
                "• Adjustable sensitivity via Alpha parameter\n" +
                "• Best for: Most applications, dynamic loads\n\n" +
                "SMA (Simple Moving Average)\n" +
                "• Very smooth output\n" +
                "• Slower response to changes\n" +
                "• Adjustable via Window Size\n" +
                "• Best for: Static loads, when consistency is priority\n\n" +
                "None (No Filtering)\n" +
                "• Shows raw calibrated values\n" +
                "• Maximum responsiveness\n" +
                "• More noise/jitter visible\n" +
                "• Best for: Debugging, analysis, when external filtering used");
        }

        private void EmaAlphaInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("EMA Alpha Parameter", 
                "EMA ALPHA PARAMETER\n\n" +
                "Controls how much weight is given to new values vs. previous filtered values.\n\n" +
                "Range: 0.05 to 0.5\n" +
                "Default: 0.15\n\n" +
                "Lower Alpha (0.05-0.10):\n" +
                "• Smoother output\n" +
                "• Slower response to changes\n" +
                "• Less noise visible\n" +
                "• Best for: Stable loads, when smoothness is priority\n\n" +
                "Medium Alpha (0.15-0.20):\n" +
                "• Balanced smoothing and responsiveness\n" +
                "• Recommended for most cases\n" +
                "• Good compromise\n\n" +
                "Higher Alpha (0.25-0.5):\n" +
                "• Faster response to changes\n" +
                "• More noise visible\n" +
                "• Best for: Dynamic loads, when responsiveness is priority\n\n" +
                "Formula: filtered_value = alpha × new_value + (1-alpha) × previous_filtered_value");
        }

        private void SmaWindowInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("SMA Window Size", 
                "SMA WINDOW SIZE\n\n" +
                "Number of recent samples to average together.\n\n" +
                "Range: 5 to 50 samples\n" +
                "Default: 10 samples\n\n" +
                "Small Window (5-10):\n" +
                "• Faster response to changes\n" +
                "• Some noise still visible\n" +
                "• Best for: Dynamic loads\n\n" +
                "Medium Window (10-20):\n" +
                "• Balanced smoothing and responsiveness\n" +
                "• Recommended for most cases\n\n" +
                "Large Window (25-50):\n" +
                "• Very smooth output\n" +
                "• Slower response to changes\n" +
                "• Best for: Static loads\n\n" +
                "Note: More samples = smoother but slower response. " +
                "At 1kHz data rate, 10 samples = 10ms delay.");
        }

        private void WeightFormatInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Weight Display Format", 
                "WEIGHT DISPLAY FORMAT\n\n" +
                "Controls how many decimal places are shown in weight displays.\n\n" +
                "Integer (50 kg):\n" +
                "• Rounds to nearest whole number\n" +
                "• Simple, easy to read\n" +
                "• Example: 50.7 kg → \"51 kg\"\n" +
                "• Best for: General use, when precision beyond 1 kg isn't needed\n\n" +
                "One Decimal (50.3 kg):\n" +
                "• Shows one decimal place\n" +
                "• Good balance of readability and precision\n" +
                "• Example: 50.67 kg → \"50.7 kg\"\n" +
                "• Best for: Most applications (recommended)\n\n" +
                "Two Decimals (50.25 kg):\n" +
                "• Shows two decimal places\n" +
                "• High precision display\n" +
                "• Example: 50.678 kg → \"50.68 kg\"\n" +
                "• Best for: Calibration, testing, when small differences matter\n\n" +
                "Note: Actual precision depends on calibration accuracy and filtering settings.");
        }

        private void UIUpdateRateInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("UI Update Rate", 
                "UI UPDATE RATE\n\n" +
                "Controls how often the weight display refreshes on screen.\n\n" +
                "Range: 10ms (100 Hz) to 200ms (5 Hz)\n" +
                "Default: 50ms (20 Hz)\n\n" +
                "Fast Updates (10-30ms):\n" +
                "• Very responsive, smooth display\n" +
                "• Higher CPU usage\n" +
                "• May cause UI lag on slower systems\n" +
                "• Best for: High-frequency monitoring, real-time feel\n\n" +
                "Medium Updates (40-80ms):\n" +
                "• Balanced performance\n" +
                "• Recommended for most cases\n" +
                "• Good responsiveness without excessive CPU usage\n\n" +
                "Slow Updates (100-200ms):\n" +
                "• Better performance\n" +
                "• Less frequent updates\n" +
                "• Best for: When system performance is critical\n\n" +
                "Note: This only affects UI display rate. Data processing still happens at full speed (1kHz).");
        }

        private void DataTimeoutInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Data Timeout", 
                "DATA TIMEOUT\n\n" +
                "How long to wait before reporting \"No Data\" when CAN messages stop.\n\n" +
                "Range: 1-30 seconds\n" +
                "Default: 5 seconds\n\n" +
                "Short Timeout (1-3 seconds):\n" +
                "• Quick detection of connection issues\n" +
                "• May trigger false alarms during brief interruptions\n" +
                "• Best for: When immediate feedback is needed\n\n" +
                "Medium Timeout (4-8 seconds):\n" +
                "• Balanced detection\n" +
                "• Recommended for most cases\n" +
                "• Good balance of responsiveness and tolerance\n\n" +
                "Long Timeout (10-30 seconds):\n" +
                "• Tolerant of brief interruptions\n" +
                "• Slower detection of real connection problems\n" +
                "• Best for: Networks with occasional delays\n\n" +
                "Note: At 1kHz data rate, even 1 second timeout allows for 1000 missed messages.");
        }


        private void StatusBannerDurationInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Status Banner Duration", 
                "STATUS BANNER DURATION\n\n" +
                "How long status messages (success/error notifications) are displayed.\n\n" +
                "Range: 1-10 seconds\n" +
                "Default: 3 seconds\n\n" +
                "Short Duration (1-2 seconds):\n" +
                "• Quick notifications, less screen clutter\n" +
                "• May miss notifications if not watching\n" +
                "• Best for: When you want brief, non-intrusive notifications\n\n" +
                "Medium Duration (3-5 seconds):\n" +
                "• Balanced visibility\n" +
                "• Recommended for most cases\n" +
                "• Good balance of visibility and screen space\n\n" +
                "Long Duration (6-10 seconds):\n" +
                "• Maximum visibility\n" +
                "• Notifications stay longer, may cover content\n" +
                "• Best for: When you want to ensure you see all notifications");
        }

        private void MessageHistoryLimitInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Message History Limit", 
                "MESSAGE HISTORY LIMIT\n\n" +
                "Maximum number of CAN messages stored in memory for the message history display.\n\n" +
                "Range: 100-5000 messages\n" +
                "Default: 1000 messages\n\n" +
                "Low Limit (100-500):\n" +
                "• Less memory usage\n" +
                "• Faster scrolling\n" +
                "• Older messages discarded quickly\n" +
                "• Best for: When you only need recent messages\n\n" +
                "Medium Limit (800-1500):\n" +
                "• Balanced storage\n" +
                "• Recommended for most cases\n" +
                "• Good balance of history and performance\n\n" +
                "High Limit (2000-5000):\n" +
                "• Maximum history retention\n" +
                "• Higher memory usage\n" +
                "• May slow down scrolling\n" +
                "• Best for: When you need to analyze long message sequences\n\n" +
                "Note: When limit is reached, oldest messages are automatically removed.");
        }

        private void ShowRawADCInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Show Raw ADC Display", 
                "SHOW RAW ADC DISPLAY\n\n" +
                "Controls visibility of the raw ADC value display.\n\n" +
                "Enabled:\n" +
                "• Shows raw ADC value (e.g., \"1850\")\n" +
                "• Useful for debugging and analysis\n" +
                "• Best for: Calibration, troubleshooting, understanding ADC behavior\n\n" +
                "Disabled:\n" +
                "• Hides raw ADC display\n" +
                "• Cleaner interface\n" +
                "• Best for: Production use, when you only care about weight values\n\n" +
                "Note: Raw ADC values are the direct readings from the ADC before calibration.");
        }

        private void ShowCalibratedWeightInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Show Calibrated Weight", 
                "SHOW CALIBRATED WEIGHT (BEFORE TARE)\n\n" +
                "Controls visibility of calibrated weight before tare is applied.\n\n" +
                "Enabled:\n" +
                "• Shows both calibrated and tared weight\n" +
                "• Useful for understanding tare effect\n" +
                "• Best for: Debugging tare issues, understanding calibration\n\n" +
                "Disabled:\n" +
                "• Only shows tared weight\n" +
                "• Simplified display\n" +
                "• Best for: Normal operation\n\n" +
                "Note: This setting is saved for future UI enhancements. " +
                "Calibrated weight = weight after calibration but before tare. " +
                "Tared weight = calibrated weight minus tare offset.");
        }

        private void ShowStreamingIndicatorsInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Show Streaming Status Indicators", 
                "SHOW STREAMING STATUS INDICATORS\n\n" +
                "Controls visibility of streaming status indicators (green/gray dot and \"Left Active\" / \"Right Active\" text).\n\n" +
                "Enabled:\n" +
                "• Shows visual confirmation of active streams\n" +
                "• Quick status check\n" +
                "• Best for: When you want visual confirmation, debugging stream issues\n\n" +
                "Disabled:\n" +
                "• Hides streaming indicators\n" +
                "• Minimal UI\n" +
                "• Best for: Clean interface, when you know stream status from other sources\n\n" +
                "Indicators show:\n" +
                "• Green dot + \"Left Active\" when left stream is running\n" +
                "• Green dot + \"Right Active\" when right stream is running\n" +
                "• Gray dot + \"Stopped\" when no stream is active");
        }

        private void ShowCalibrationIconsInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Show Calibration Status Icons", 
                "SHOW CALIBRATION STATUS ICONS\n\n" +
                "Controls visibility of calibration status icons.\n\n" +
                "Enabled:\n" +
                "• Shows ✓ for calibrated, ⚠ for uncalibrated\n" +
                "• Quick visual confirmation of calibration status\n" +
                "• Best for: Ensuring calibration before use, troubleshooting\n\n" +
                "Disabled:\n" +
                "• Hides calibration icons\n" +
                "• Minimal UI\n" +
                "• Best for: Clean interface, when calibration is always done\n\n" +
                "Icons indicate:\n" +
                "• ✓ (Green checkmark): Side is calibrated and ready\n" +
                "• ⚠ (Yellow warning): Side needs calibration");
        }

        private void TXIndicatorFlashInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("TX Indicator Flash Duration", 
                "TX INDICATOR FLASH DURATION\n\n" +
                "How long the TX (transmit) indicator flashes white when sending CAN messages.\n\n" +
                "Range: 50-500 milliseconds\n" +
                "Default: 200ms\n\n" +
                "Short Flash (50-100ms):\n" +
                "• Brief visual feedback\n" +
                "• May be hard to see\n" +
                "• Best for: When you want subtle, quick feedback\n\n" +
                "Medium Flash (150-250ms):\n" +
                "• Balanced visibility\n" +
                "• Recommended for most cases\n" +
                "• Good balance of visibility and speed\n\n" +
                "Long Flash (300-500ms):\n" +
                "• Maximum visibility\n" +
                "• Clear visual confirmation\n" +
                "• Best for: When you want clear visual confirmation of transmissions\n\n" +
                "Note: The TX indicator flashes white when a CAN message is sent, then returns to red.");
        }

        private void BatchProcessingSizeInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Batch Processing Size", 
                "BATCH PROCESSING SIZE\n\n" +
                "Number of CAN messages processed per UI update cycle.\n\n" +
                "Range: 10-100 messages\n" +
                "Default: 50 messages\n\n" +
                "Small Batch (10-30):\n" +
                "• More frequent processing\n" +
                "• Smoother UI\n" +
                "• More processing overhead\n" +
                "• Best for: Low message rates, when responsive UI is priority\n\n" +
                "Medium Batch (40-60):\n" +
                "• Balanced processing\n" +
                "• Recommended for most cases\n" +
                "• Good balance of responsiveness and efficiency\n\n" +
                "Large Batch (70-100):\n" +
                "• Maximum efficiency\n" +
                "• Less frequent UI updates\n" +
                "• Best for: High message rates (1kHz), maximum throughput\n\n" +
                "CRITICAL: This is important for 1kHz data rates. " +
                "Too small batches may cause message queue buildup and data loss.");
        }

        private void ClockUpdateIntervalInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Clock Update Interval", 
                "CLOCK UPDATE INTERVAL\n\n" +
                "How often the clock display (timestamp) is updated.\n\n" +
                "Range: 500-5000 milliseconds\n" +
                "Default: 1000ms (1 second)\n\n" +
                "Fast Update (500-800ms):\n" +
                "• More frequent clock updates\n" +
                "• Slightly more CPU usage\n" +
                "• Best for: When you want precise time display\n\n" +
                "Medium Update (1000-2000ms):\n" +
                "• Standard clock updates\n" +
                "• Recommended for most cases\n" +
                "• Good balance of accuracy and performance\n\n" +
                "Slow Update (3000-5000ms):\n" +
                "• Less frequent updates\n" +
                "• Lower CPU usage\n" +
                "• Clock may appear \"stale\"\n" +
                "• Best for: When clock accuracy isn't critical\n\n" +
                "Note: This only affects the displayed clock, not data timestamps.");
        }

        private void CalibrationCaptureDelayInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Calibration Capture Delay", 
                "CALIBRATION CAPTURE DELAY\n\n" +
                "How long to wait before capturing an ADC reading during calibration.\n\n" +
                "Range: 100-2000 milliseconds\n" +
                "Default: 500ms\n\n" +
                "Short Delay (100-300ms):\n" +
                "• Faster calibration\n" +
                "• May capture unstable readings\n" +
                "• Best for: When ADC readings stabilize quickly\n\n" +
                "Medium Delay (400-800ms):\n" +
                "• Balanced capture timing\n" +
                "• Recommended for most cases\n" +
                "• Good balance of speed and stability\n\n" +
                "Long Delay (1000-2000ms):\n" +
                "• Maximum stability\n" +
                "• Slower calibration process\n" +
                "• Best for: When ADC readings take time to stabilize\n\n" +
                "Why delay is needed:\n" +
                "After placing a weight, ADC readings need time to stabilize due to:\n" +
                "• Mechanical settling of load cells\n" +
                "• Electrical signal conditioning\n" +
                "• ADC sampling and filtering\n\n" +
                "Too short delay may result in inaccurate calibration points.");
        }

        private void LogFormatInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Log File Format", 
                "LOG FILE FORMAT\n\n" +
                "Format of saved log files (currently CSV only, JSON/TXT coming soon).\n\n" +
                "CSV (Comma Separated Values) - CURRENTLY AVAILABLE\n" +
                "• Standard spreadsheet format\n" +
                "• Easy to open in Excel, Python, or other tools\n" +
                "• Best for: Most applications, data analysis\n" +
                "• Format: Columns separated by commas, one row per data point\n\n" +
                "JSON (JavaScript Object Notation) - COMING SOON\n" +
                "• Structured data format\n" +
                "• Easy to parse programmatically\n" +
                "• Best for: Programmatic analysis, web applications\n\n" +
                "TXT (Plain Text) - COMING SOON\n" +
                "• Human-readable text format\n" +
                "• Simple, readable logs\n" +
                "• Best for: Quick review, simple analysis\n\n" +
                "Note: Currently only CSV format is available. JSON and TXT formats will be added in future updates.");
        }

        private void ShowCalibrationQualityMetricsInfoBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsInfo("Show Calibration Quality Metrics", 
                "SHOW CALIBRATION QUALITY METRICS\n\n" +
                "Controls whether calibration quality metrics are displayed in calibration dialogs.\n\n" +
                "Enabled:\n" +
                "• Shows R² coefficient and maximum error percentage\n" +
                "• Useful for verifying calibration quality\n" +
                "• Best for: Ensuring accurate calibration, troubleshooting\n\n" +
                "Disabled:\n" +
                "• Hides quality metrics\n" +
                "• Simpler display\n" +
                "• Best for: When you trust your calibration\n\n" +
                "R² (R-squared) Coefficient:\n" +
                "Indicates how well the calibration fits the data points.\n" +
                "• R² > 0.99: Excellent fit - calibration is very accurate\n" +
                "• R² > 0.95: Good fit - calibration is acceptable\n" +
                "• R² < 0.95: Poor fit - may need more calibration points or check for issues\n\n" +
                "Maximum Error Percentage:\n" +
                "Shows the largest deviation between calibration points and the fitted line.\n" +
                "Lower is better. High error may indicate:\n" +
                "• Insufficient calibration points\n" +
                "• Non-linear load cell behavior\n" +
                "• Measurement errors during calibration");
        }

        private void ApplyDisplaySettings()
        {
            try
            {
                int weightDecimals = 0;
                if (WeightFormatCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem formatItem)
                {
                    weightDecimals = int.Parse(formatItem.Tag?.ToString() ?? "0");
                }
                
                int uiUpdateRate = (int)(UIUpdateRateSlider?.Value ?? 50);
                int dataTimeout = (int)(DataTimeoutSlider?.Value ?? 5);
                
                // Apply UI update rate to timer
                if (_uiUpdateTimer != null)
                {
                    _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(uiUpdateRate);
                }
                
                // Apply data timeout to CAN service
                if (_canService != null)
                {
                    _canService.SetTimeout(TimeSpan.FromSeconds(dataTimeout));
                }
                
                // Save to settings
                _settingsManager.SetDisplaySettings(weightDecimals, uiUpdateRate, dataTimeout);
                
                // Update weight display immediately if active
                UpdateWeightDisplays();
                
                _logger.LogInfo($"Display settings applied: WeightDecimals={weightDecimals}, UIUpdateRate={uiUpdateRate}ms, DataTimeout={dataTimeout}s", "Settings");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error applying display settings: {ex.Message}", "Settings");
            }
        }

        private void FilterEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilterSettings();
        }

        private void FilterTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (FilterTypeCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
                {
                    string filterType = item.Tag?.ToString() ?? "EMA";
                    
                    // Show/hide appropriate settings panels
                    if (EmaSettingsPanel != null)
                    {
                        EmaSettingsPanel.Visibility = filterType == "EMA" ? Visibility.Visible : Visibility.Collapsed;
                    }
                    
                    if (SmaSettingsPanel != null)
                    {
                        SmaSettingsPanel.Visibility = filterType == "SMA" ? Visibility.Visible : Visibility.Collapsed;
                    }
                    
                    ApplyFilterSettings();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Filter type selection error: {ex.Message}", "Settings");
            }
        }

        private void EmaAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (EmaAlphaValueTxt != null)
                {
                    EmaAlphaValueTxt.Text = e.NewValue.ToString("F2");
                }
                ApplyFilterSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"EMA alpha slider error: {ex.Message}", "Settings");
            }
        }

        private void SmaWindowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (SmaWindowValueTxt != null)
                {
                    SmaWindowValueTxt.Text = $"{(int)e.NewValue} samples";
                }
                ApplyFilterSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError($"SMA window slider error: {ex.Message}", "Settings");
            }
        }

        private void ApplyFilterSettings()
        {
            try
            {
                bool enabled = FilterEnabledCheckBox?.IsChecked ?? true;
                string filterType = "EMA";
                
                if (FilterTypeCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
                {
                    filterType = item.Tag?.ToString() ?? "EMA";
                }
                
                double alpha = EmaAlphaSlider?.Value ?? 0.15;
                int windowSize = (int)(SmaWindowSlider?.Value ?? 10);
                
                FilterType type = filterType switch
                {
                    "EMA" => FilterType.EMA,
                    "SMA" => FilterType.SMA,
                    _ => FilterType.None
                };
                
                _weightProcessor.ConfigureFilter(type, alpha, windowSize, enabled);
                
                // Save to settings
                _settingsManager.SetFilterSettings(filterType, alpha, windowSize, enabled);
                
                _logger.LogInfo($"Filter settings applied: {filterType}, Alpha={alpha}, Window={windowSize}, Enabled={enabled}", "Settings");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error applying filter settings: {ex.Message}", "Settings");
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
                    
                    // Auto-dismiss after configured duration
                    var timer = new DispatcherTimer();
                    int durationMs = _settingsManager.Settings.StatusBannerDurationMs;
                    timer.Interval = TimeSpan.FromMilliseconds(durationMs);
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
                    _canService.DataTimeout -= OnDataTimeout;
                    if (_settingsManager.Settings.EnableBootloaderFeatures)
                    {
                        _canService.BootStatusReceived -= OnBootStatusReceived;
                    }
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
                    
                    // Update logging status indicator
                    if (LoggingStatusIndicator != null)
                    {
                        LoggingStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                    }
                    
                    // Update LogsWindow checkbox if open
                    _logsWindow?.UpdateLoggingState(true);
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
                
                // Update logging status indicator
                if (LoggingStatusIndicator != null)
                {
                    LoggingStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                }
                
                // Update LogsWindow checkbox if open
                _logsWindow?.UpdateLoggingState(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping logging: {ex.Message}", "Logging Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeLoggingUI()
        {
            try
            {
                // Initialize logging UI state based on actual logging status
                if (_dataLogger.IsLogging)
                {
                    StartLoggingBtn.IsEnabled = false;
                    StopLoggingBtn.IsEnabled = true;
                    LoggingStatusTxt.Text = $"Logging to: {_dataLogger.GetLogFilePath()}";
                    LoggingStatusTxt.Foreground = System.Windows.Media.Brushes.Green;
                    
                    // Update logging status indicator
                    if (LoggingStatusIndicator != null)
                    {
                        LoggingStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                    }
                    
                    // Update LogsWindow checkbox if open
                    _logsWindow?.UpdateLoggingState(true);
                }
                else
                {
                    StartLoggingBtn.IsEnabled = true;
                    StopLoggingBtn.IsEnabled = false;
                    LoggingStatusTxt.Text = "Not logging";
                    LoggingStatusTxt.Foreground = System.Windows.Media.Brushes.Gray;
                    
                    // Update logging status indicator
                    if (LoggingStatusIndicator != null)
                    {
                        LoggingStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                    }
                    
                    // Update LogsWindow checkbox if open
                    _logsWindow?.UpdateLoggingState(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize logging UI: {ex.Message}", "UI");
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
                // Close popup
                if (ToolsMenuPopup != null)
                {
                    ToolsMenuPopup.IsOpen = false;
                }

                var monitorWindow = new MonitorWindow(_canService);
                monitorWindow.Owner = this;
                monitorWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening monitor window: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenGraphWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Close popup
                if (ToolsMenuPopup != null)
                {
                    ToolsMenuPopup.IsOpen = false;
                }

                // If window already exists, bring it to front
                if (_graphWindow != null && _graphWindow.IsLoaded)
                {
                    _graphWindow.Activate();
                    _graphWindow.Focus();
                    return;
                }

                _graphWindow = new SuspensionGraphWindow(_canService, _weightProcessor, _currentTransmissionRate);
                _graphWindow.Owner = this;
                _graphWindow.Closed += (s, args) => { _graphWindow = null; };
                _graphWindow.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening graph window: {ex.Message}", "UI");
                MessageBox.Show($"Error opening graph window: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private LMVAxleWeightWindow? _lmvAxleWindow;

        private void OpenLMVAxleWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Close popup
                if (ToolsMenuPopup != null)
                {
                    ToolsMenuPopup.IsOpen = false;
                }

                // If window already exists, bring it to front
                if (_lmvAxleWindow != null && _lmvAxleWindow.IsLoaded)
                {
                    _lmvAxleWindow.Activate();
                    _lmvAxleWindow.Focus();
                    return;
                }

                _lmvAxleWindow = new LMVAxleWeightWindow(_canService, _weightProcessor);
                _lmvAxleWindow.Owner = this;
                _lmvAxleWindow.Closed += (s, args) => { _lmvAxleWindow = null; };
                _lmvAxleWindow.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error opening LMV axle window: {ex.Message}", "UI");
                MessageBox.Show($"Error opening LMV axle window: {ex.Message}", "Error",
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

        private LogsWindow? _logsWindow;
        private SuspensionGraphWindow? _graphWindow;

        private void OpenLogsWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Close popup
                if (ToolsMenuPopup != null)
                {
                    ToolsMenuPopup.IsOpen = false;
                }

                // If window already exists, bring it to front
                if (_logsWindow != null && _logsWindow.IsLoaded)
                {
                    _logsWindow.Activate();
                    _logsWindow.Focus();
                    return;
                }

                _logsWindow = new LogsWindow(_logger, _dataLogger);
                _logsWindow.Owner = this;
                _logsWindow.Closed += (s, args) => { _logsWindow = null; };
                _logsWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening logs window: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private LogFilesManagerWindow? _logFilesManagerWindow;

        private void ToolsMenuBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ToolsMenuPopup != null)
                {
                    ToolsMenuPopup.IsOpen = !ToolsMenuPopup.IsOpen;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Tools menu error: {ex.Message}", "UI");
            }
        }

        private void OpenLogFilesManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Close popup
                if (ToolsMenuPopup != null)
                {
                    ToolsMenuPopup.IsOpen = false;
                }

                // If window already exists, bring it to front
                if (_logFilesManagerWindow != null && _logFilesManagerWindow.IsLoaded)
                {
                    _logFilesManagerWindow.Activate();
                    _logFilesManagerWindow.Focus();
                    return;
                }

                _logFilesManagerWindow = new LogFilesManagerWindow();
                _logFilesManagerWindow.Owner = this;
                _logFilesManagerWindow.Closed += (s, args) => { _logFilesManagerWindow = null; };
                _logFilesManagerWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening log files manager: {ex.Message}", "Error", 
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
                
                // Update System Status UI panel
                UpdateSystemStatusUI(e);
            }
            catch (Exception ex)
            {
                _logger.LogError($"System status handler error: {ex.Message}", "CAN");
            }
        }

        private void UpdateSystemStatusUI(SystemStatusEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Update status indicator color based on system status
                    if (SystemStatusIndicator != null)
                    {
                        SystemStatusIndicator.Fill = e.SystemStatus switch
                        {
                            0 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96)),   // Green - OK
                            1 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)),   // Yellow - Warning
                            2 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)),   // Red - Error
                            3 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 0, 0)),     // Dark Red - Critical
                            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128)) // Gray - Unknown
                        };
                    }
                    
                    // Update status text
                    if (SystemStatusText != null)
                    {
                        SystemStatusText.Text = e.SystemStatus switch
                        {
                            0 => "OK",
                            1 => "Warning",
                            2 => "Error",
                            3 => "Critical",
                            _ => "Unknown"
                        };
                    }
                    
                    // Update error flags display
                    if (ErrorCountTxt != null)
                    {
                        if (e.ErrorFlags == 0)
                        {
                            ErrorCountTxt.Text = "Errors: None";
                        }
                        else
                        {
                            ErrorCountTxt.Text = $"Errors: 0x{e.ErrorFlags:X2}";
                        }
                    }
                    
                    // Update last update time
                    if (LastUpdateTxt != null)
                    {
                        LastUpdateTxt.Text = $"Updated: {e.Timestamp:HH:mm:ss}";
                    }
                    
                    // Calculate and update data rate
                    if (_lastStatusUpdateTime != DateTime.MinValue)
                    {
                        TimeSpan timeSinceLastUpdate = e.Timestamp - _lastStatusUpdateTime;
                        if (timeSinceLastUpdate.TotalSeconds > 0 && timeSinceLastUpdate.TotalSeconds < 10.0) // Sanity check: between 0.1Hz and reasonable max
                        {
                            _statusUpdateRate = 1.0 / timeSinceLastUpdate.TotalSeconds;
                        }
                        else
                        {
                            _statusUpdateRate = 0.0; // Reset if time gap is too large
                        }
                    }
                    _lastStatusUpdateTime = e.Timestamp;
                    
                    // Update data rate display
                    if (DataRateTxt != null)
                    {
                        if (_statusUpdateRate > 0 && _statusUpdateRate < 1000.0) // Reasonable range: 0.1 Hz to 1000 Hz
                        {
                            DataRateTxt.Text = $"Rate: {_statusUpdateRate:F2} Hz";
                        }
                        else
                        {
                            DataRateTxt.Text = "Rate: --";
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"System status UI update error: {ex.Message}", "UI");
            }
        }

        private void OnDataTimeout(object? sender, string timeoutMessage)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Stop all streams if any are running
                    if (_leftStreamRunning || _rightStreamRunning)
                    {
                        _canService?.StopAllStreams();
                        _leftStreamRunning = false;
                        _rightStreamRunning = false;
                        
                        _logger.LogWarning("Data timeout: Stopped all streams due to no data received", "CAN");
                    }
                    
                    // Update UI status
                    UpdateStreamingIndicators();
                    ShowStatusBanner("⚠ No Data Received - Streams Stopped", false);
                    ShowInlineStatus("Data timeout: No messages received. Streams stopped.", true);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Data timeout handler error: {ex.Message}", "CAN");
            }
        }

        private void InitializeADCModeFromSettings()
        {
            try
            {
                var (adcMode, systemStatus, errorFlags, lastUpdate) = SettingsManager.Instance.GetLastKnownSystemStatus();
                
                // Initialize System Status UI with last known status
                if (lastUpdate != DateTime.MinValue)
                {
                    var statusArgs = new SystemStatusEventArgs
                    {
                        SystemStatus = systemStatus,
                        ErrorFlags = errorFlags,
                        ADCMode = adcMode,
                        Timestamp = lastUpdate
                    };
                    UpdateSystemStatusUI(statusArgs);
                }
                
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
                    
                    // Update graph window if open
                    if (_graphWindow != null && _graphWindow.IsLoaded)
                    {
                        _graphWindow.UpdateTransmissionRate(_currentTransmissionRate);
                    }
                    
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