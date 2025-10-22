using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Collections.Concurrent;
using System.Windows.Threading;

namespace SuspensionPCB_CAN_WPF
{
    public partial class MonitorWindow : Window
    {
        private readonly CANService? _canService;
        private readonly ProductionLogger? _logger;
        private readonly TareManager? _tareManager;
        private readonly LinearCalibration? _leftCalibration;
        private readonly LinearCalibration? _rightCalibration;

        // Message filtering
        public ObservableCollection<CANMessageViewModel> AllMessages { get; set; }
        public ObservableCollection<CANMessageViewModel> FilteredMessages { get; set; }

        // High-performance message processing
        private readonly ConcurrentQueue<CANMessageViewModel> _messageQueue = new ConcurrentQueue<CANMessageViewModel>();
        private readonly DispatcherTimer _batchTimer;

        // Weight data
        private int _leftRawADC = 0;
        private int _rightRawADC = 0;

        // Message statistics
        private int _txCount = 0;
        private int _rxCount = 0;
        private DateTime _lastMessageTime = DateTime.MinValue;
        private DateTime _rateStartTime = DateTime.Now;
        private int _rateMessageCount = 0;

        public MonitorWindow(CANService? canService, ProductionLogger? logger, TareManager? tareManager, 
                           LinearCalibration? leftCalibration, LinearCalibration? rightCalibration)
        {
            InitializeComponent();
            
            _canService = canService;
            _logger = logger;
            _tareManager = tareManager;
            _leftCalibration = leftCalibration;
            _rightCalibration = rightCalibration;

            // Initialize collections
            AllMessages = new ObservableCollection<CANMessageViewModel>();
            FilteredMessages = new ObservableCollection<CANMessageViewModel>();

            // Set data context
            DataContext = this;

            // Initialize batch processing timer for high-performance updates
            _batchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; // 20Hz
            _batchTimer.Tick += ProcessMessageBatch;
            _batchTimer.Start();

            // Subscribe to CAN events
            if (_canService != null)
            {
                _canService.MessageReceived += OnMessageReceived;
                _canService.RawDataReceived += OnRawDataReceived;
            }

            // Initialize UI
            InitializeUI();
        }

        private void InitializeUI()
        {
            // Set up filtering
            FilterChanged(null!, null!);
        }

        private void OnMessageReceived(CANMessage message)
        {
            // Non-blocking: enqueue message for batch processing
            var viewModel = new CANMessageViewModel(message, 
                new HashSet<uint> { 0x200, 0x201, 0x300 }, // RX IDs
                new HashSet<uint> { 0x040, 0x041, 0x044, 0x030, 0x031 }); // TX IDs
            
            _messageQueue.Enqueue(viewModel);
        }

        private void ProcessMessageBatch(object? sender, EventArgs e)
        {
            try
            {
                int processed = 0;
                const int maxBatchSize = 50; // Process up to 50 messages per batch
                
                while (_messageQueue.TryDequeue(out var message) && processed < maxBatchSize)
                {
                    AllMessages.Insert(0, message);
                    
                    // Update statistics
                    if (message.Direction == "TX")
                    {
                        _txCount++;
                    }
                    else if (message.Direction == "RX")
                    {
                        _rxCount++;
                    }
                    
                    _lastMessageTime = message.Message.Timestamp;
                    _rateMessageCount++;
                    
                    processed++;
                    
                    // Keep only last 1000 messages
                    while (AllMessages.Count > 1000)
                    {
                        AllMessages.RemoveAt(AllMessages.Count - 1);
                    }
                }
                
                if (processed > 0)
                {
                    // Update statistics display
                    UpdateStatistics();
                    
                    // Apply filters
                    FilterChanged(null!, null!);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error processing message batch: {ex.Message}", "MonitorWindow");
            }
        }

        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (e.Side == 0) // Left side
                {
                    _leftRawADC = e.RawADCSum;
                    UpdateWeightDisplay();
                }
                else if (e.Side == 1) // Right side
                {
                    _rightRawADC = e.RawADCSum;
                    UpdateWeightDisplay();
                }
            });
        }

        private void UpdateStatistics()
        {
            try
            {
                if (TxCountTxt != null)
                {
                    TxCountTxt.Text = _txCount.ToString();
                }
                if (RxCountTxt != null)
                {
                    RxCountTxt.Text = _rxCount.ToString();
                }
                if (LastMessageTxt != null)
                {
                    if (_lastMessageTime != DateTime.MinValue)
                    {
                        var timeDiff = DateTime.Now - _lastMessageTime;
                        if (timeDiff.TotalSeconds < 60)
                        {
                            LastMessageTxt.Text = $"{timeDiff.TotalSeconds:F1}s ago";
                        }
                        else
                        {
                            LastMessageTxt.Text = _lastMessageTime.ToString("HH:mm:ss");
                        }
                    }
                    else
                    {
                        LastMessageTxt.Text = "Never";
                    }
                }
                if (MessageRateTxt != null)
                {
                    var timeDiff = DateTime.Now - _rateStartTime;
                    if (timeDiff.TotalSeconds > 0)
                    {
                        var rate = _rateMessageCount / timeDiff.TotalSeconds;
                        MessageRateTxt.Text = $"{rate:F1} msg/s";
                    }
                    else
                    {
                        MessageRateTxt.Text = "0 msg/s";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Statistics update error: {ex.Message}", "MonitorWindow");
            }
        }

        private void UpdateWeightDisplay()
        {
            try
            {
                // Left side
                LeftRawTxt.Text = _leftRawADC.ToString();
                
                if (_leftCalibration != null && _leftCalibration.IsValid && _tareManager != null)
                {
                    double leftCalibrated = _leftCalibration.RawToKg(_leftRawADC);
                    LeftCalibratedTxt.Text = $"{(int)leftCalibrated} kg";
                    
                    double leftDisplay = _tareManager.ApplyTare(leftCalibrated, true);
                    LeftDisplayTxt.Text = $"{(int)leftDisplay} kg";
                    LeftTareStatusTxt.Text = _tareManager.LeftIsTared ? "- Tared" : "- Not Tared";
                }
                else
                {
                    LeftCalibratedTxt.Text = "Not Calibrated";
                    LeftDisplayTxt.Text = "Not Calibrated";
                    LeftTareStatusTxt.Text = "- Not Calibrated";
                }

                // Right side
                RightRawTxt.Text = _rightRawADC.ToString();
                
                if (_rightCalibration != null && _rightCalibration.IsValid && _tareManager != null)
                {
                    double rightCalibrated = _rightCalibration.RawToKg(_rightRawADC);
                    RightCalibratedTxt.Text = $"{(int)rightCalibrated} kg";
                    
                    double rightDisplay = _tareManager.ApplyTare(rightCalibrated, false);
                    RightDisplayTxt.Text = $"{(int)rightDisplay} kg";
                    RightTareStatusTxt.Text = _tareManager.RightIsTared ? "- Tared" : "- Not Tared";
                }
                else
                {
                    RightCalibratedTxt.Text = "Not Calibrated";
                    RightDisplayTxt.Text = "Not Calibrated";
                    RightTareStatusTxt.Text = "- Not Calibrated";
                }

                // Summary
                if (_leftCalibration != null && _leftCalibration.IsValid && 
                    _rightCalibration != null && _rightCalibration.IsValid && _tareManager != null)
                {
                    double leftCalibrated = _leftCalibration.RawToKg(_leftRawADC);
                    double rightCalibrated = _rightCalibration.RawToKg(_rightRawADC);
                    double leftDisplay = _tareManager.ApplyTare(leftCalibrated, true);
                    double rightDisplay = _tareManager.ApplyTare(rightCalibrated, false);
                    
                    double total = leftDisplay + rightDisplay;
                    TotalWeightTxt.Text = $"{(int)total} kg";
                    
                    if (total > 0)
                    {
                        double leftPercent = (leftDisplay / total) * 100;
                        double rightPercent = (rightDisplay / total) * 100;
                        BalanceTxt.Text = $"{leftPercent:F0}% L / {rightPercent:F0}% R";
                    }
                    else
                    {
                        BalanceTxt.Text = "50% L / 50% R";
                    }
                }
                else
                {
                    TotalWeightTxt.Text = "Not Calibrated";
                    BalanceTxt.Text = "Not Calibrated";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Weight display update error: {ex.Message}", "MonitorWindow");
            }
        }

        private void FilterIdTxt_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterChanged(sender, e);
        }

        private void FilterChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                FilteredMessages.Clear();

                string filterId = FilterIdTxt.Text.Trim();
                bool showTx = ShowTxChk.IsChecked == true;
                bool showRx = ShowRxChk.IsChecked == true;

                var filtered = AllMessages.Take(200).Where(msg =>
                {
                    // Direction filter - now using CANMessageViewModel
                    if (!showTx && msg.Direction == "TX") return false;
                    if (!showRx && msg.Direction == "RX") return false;

                    // ID filter
                    if (!string.IsNullOrEmpty(filterId))
                    {
                        if (filterId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(filterId.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hexId))
                            {
                                if (msg.Message.ID != (uint)hexId) return false;
                            }
                        }
                        else if (int.TryParse(filterId, out int decId))
                        {
                            if (msg.Message.ID != (uint)decId) return false;
                        }
                        else
                        {
                            if (!msg.Message.ID.ToString().Contains(filterId, StringComparison.OrdinalIgnoreCase)) return false;
                        }
                    }

                    return true;
                });

                foreach (var msg in filtered)
                {
                    FilteredMessages.Add(msg);
                }

                MessageCountTxt.Text = FilteredMessages.Count.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Message filtering error: {ex.Message}", "MonitorWindow");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Stop batch processing timer
            _batchTimer?.Stop();
            
            // Unsubscribe from events
            if (_canService != null)
            {
                _canService.MessageReceived -= OnMessageReceived;
                _canService.RawDataReceived -= OnRawDataReceived;
            }

            base.OnClosed(e);
        }
    }
}
