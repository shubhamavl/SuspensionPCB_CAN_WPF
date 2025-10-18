using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
        public ObservableCollection<CANMessage> AllMessages { get; set; }
        public ObservableCollection<CANMessage> FilteredMessages { get; set; }

        // Weight data
        private int _leftRawADC = 0;
        private int _rightRawADC = 0;

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
            AllMessages = new ObservableCollection<CANMessage>();
            FilteredMessages = new ObservableCollection<CANMessage>();

            // Set data context
            DataContext = this;

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
            Dispatcher.Invoke(() =>
            {
                AllMessages.Insert(0, message);
                
                // Keep only last 1000 messages
                while (AllMessages.Count > 1000)
                {
                    AllMessages.RemoveAt(AllMessages.Count - 1);
                }

                FilterChanged(null!, null!);
            });
        }

        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            Dispatcher.Invoke(() =>
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

        private void UpdateWeightDisplay()
        {
            try
            {
                // Left side
                LeftRawTxt.Text = _leftRawADC.ToString();
                
                if (_leftCalibration != null && _leftCalibration.IsValid && _tareManager != null)
                {
                    double leftCalibrated = _leftCalibration.RawToKg(_leftRawADC);
                    LeftCalibratedTxt.Text = $"{leftCalibrated:F1} kg";
                    
                    double leftDisplay = _tareManager.ApplyTare(leftCalibrated, true);
                    LeftDisplayTxt.Text = $"{leftDisplay:F1} kg";
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
                    RightCalibratedTxt.Text = $"{rightCalibrated:F1} kg";
                    
                    double rightDisplay = _tareManager.ApplyTare(rightCalibrated, false);
                    RightDisplayTxt.Text = $"{rightDisplay:F1} kg";
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
                    TotalWeightTxt.Text = $"{total:F1} kg";
                    
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

                var filtered = AllMessages.Where(msg =>
                {
                    // Direction filter - CANMessage doesn't have Direction property, skip for now
                    // if (!showTx && msg.Direction == "TX") return false;
                    // if (!showRx && msg.Direction == "RX") return false;

                    // ID filter
                    if (!string.IsNullOrEmpty(filterId))
                    {
                        if (filterId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(filterId.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hexId))
                            {
                                if (msg.ID != (uint)hexId) return false;
                            }
                        }
                        else if (int.TryParse(filterId, out int decId))
                        {
                            if (msg.ID != (uint)decId) return false;
                        }
                        else
                        {
                            if (!msg.ID.ToString().Contains(filterId, StringComparison.OrdinalIgnoreCase)) return false;
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
