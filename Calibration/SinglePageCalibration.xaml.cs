using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SuspensionPCB_CAN_WPF
{
    public partial class SinglePageCalibration : Window
    {
        #region Private Fields
        // Calibration state
        private bool _calibrationActive = false;
        private int _currentPoint = 0;
        private int _pointsCollected = 0;
        private double _targetWeight = 0.0;
        private double _currentWeight = 0.0;
        private bool _isStable = false;
        private byte _channelMask = 0x0F; // All 4 channels by default
        private double _maxWeight = 1000.0;

        // Hardware data
        private int _frontLeftADC = 0;

        // Calibration points management
        private List<CalibrationPointItem> _calibrationPoints = new List<CalibrationPointItem>();
        private int _frontRightADC = 0;
        private int _rearLeftADC = 0;
        private int _rearRightADC = 0;
        private double _frontLeftVoltage = 0.0;
        private double _frontRightVoltage = 0.0;
        private double _rearLeftVoltage = 0.0;
        private double _rearRightVoltage = 0.0;

        // Stability tracking
        private Queue<double> _weightHistory = new Queue<double>();
        private DateTime _lastDataReceived = DateTime.Now;
        private const int COMMUNICATION_TIMEOUT_MS = 5000;

        // Timers
        private DispatcherTimer _updateTimer;
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _responseTimeoutTimer;

        // Quality metrics
        private double _accuracyPercentage = 0.0;
        private double _maxErrorKg = 0.0;
        private byte _qualityGrade = 0;
        private byte _recommendation = 0;
        #endregion

        #region Constructor
        public SinglePageCalibration()
        {
            InitializeComponent();
            InitializeCalibration();
            StartTimers();
        }
        #endregion

        #region Initialization
        private void InitializeCalibration()
        {
            // Subscribe to CAN events
            CANService.WeightDataReceived += OnWeightDataReceived;
            CANService.ADCDataReceived += OnADCDataReceived;
            CANService.CommunicationError += OnCommunicationError;
            CANService.CalibrationDataReceived += OnCalibrationDataReceived;
            CANService.CalibrationQualityReceived += OnCalibrationQualityReceived;

            // Initialize UI state
            UpdateConnectionStatus();
            UpdateUI();
            UpdateStatusMessage("Ready to start calibration");
        }

        private void StartTimers()
        {
            // UI update timer
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // Clock timer
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();
        }
        #endregion

        #region Event Handlers - CAN Data
        private void OnWeightDataReceived(object sender, WeightDataEventArgs e)
        {
            if ((e.ChannelMask & _channelMask) != 0)
            {
                _lastDataReceived = DateTime.Now;

                Dispatcher.Invoke(() =>
                {
                    // Use total weight directly from event data
                    _currentWeight = e.TotalVehicleWeight;

                    // Add to stability tracking
                    AddWeightSample(_currentWeight);
                    CheckStability();

                    UpdateUI();
                });
            }
        }

        private void OnADCDataReceived(object sender, ADCDataEventArgs e)
        {
            _lastDataReceived = DateTime.Now;

            Dispatcher.Invoke(() =>
            {
                // Store ADC values
                _frontLeftADC = e.FrontLeftADC;
                _frontRightADC = e.FrontRightADC;
                _rearLeftADC = e.RearLeftADC;
                _rearRightADC = e.RearRightADC;

                // Store voltage values
                _frontLeftVoltage = e.FrontLeftVoltage;
                _frontRightVoltage = e.FrontRightVoltage;
                _rearLeftVoltage = e.RearLeftVoltage;
                _rearRightVoltage = e.RearRightVoltage;

                UpdateUI();
            });
        }

        private void OnCommunicationError(object sender, CANErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatusMessage($"Communication Error: {e.ErrorMessage}");
                _isStable = false;
            });
        }

        private void OnCalibrationDataReceived(object sender, CalibrationDataEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Handle 0x400 response - calibration point confirmation
                if (e.PointStatus == 0x00) // Valid point
                {
                    _pointsCollected++;
                    _currentPoint = e.PointIndex;
                    
                    // Add point to the list
                    AddCalibrationPoint(e.PointIndex, e.ReferenceWeight, e.ADCValue);
                    
                    UpdateStatusMessage($"Point {e.PointIndex} captured successfully - Weight: {e.ReferenceWeight:F1}kg, ADC: {e.ADCValue}");
                    UpdateUI();
                }
                else if (e.PointStatus == 0x80) // Point deleted (last)
                {
                    _pointsCollected = e.PointIndex; // New count after deletion
                    
                    // Remove last point from the list
                    if (_calibrationPoints.Count > 0)
                    {
                        _calibrationPoints.RemoveAt(_calibrationPoints.Count - 1);
                        UpdateCalibrationPointsList();
                    }
                    
                    UpdateStatusMessage($"Last point deleted. Points remaining: {_pointsCollected}");
                    UpdateUI();
                }
                else if (e.PointStatus == 0x81) // Session reset
                {
                    _pointsCollected = 0;
                    _currentPoint = 0;
                    _calibrationActive = false;
                    
                    // Clear all points from the list
                    ClearCalibrationPoints();
                    
                    UpdateStatusMessage("Calibration session reset");
                    UpdateUI();
                }
                else if (e.PointStatus == 0x82) // Specific point deleted
                {
                    _pointsCollected = e.PointIndex; // New count after deletion
                    
                    // Note: The specific point was already removed by the firmware
                    // We need to refresh the list by requesting current points
                    // For now, just update the count
                    UpdateStatusMessage($"Point deleted. Points remaining: {_pointsCollected}");
                    UpdateUI();
                }
                else
                {
                    UpdateStatusMessage($"Calibration point error: Status 0x{e.PointStatus:X2}");
                }
            });
        }

        private void OnCalibrationQualityReceived(object sender, CalibrationQualityEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Stop timeout timer
                _responseTimeoutTimer?.Stop();

                // Store quality metrics
                _accuracyPercentage = e.AccuracyPercentage;
                _maxErrorKg = e.MaxErrorKg;
                _qualityGrade = e.QualityGrade;
                _recommendation = e.Recommendation;

                // Update UI
                UpdateQualityDisplay();

                // Show completion message
                string gradeText = GetQualityGradeText(_qualityGrade);
                string message = $"Calibration Complete!\n\n" +
                               $"Accuracy: {_accuracyPercentage:F1}%\n" +
                               $"Max Error: {_maxErrorKg:F2}kg\n" +
                               $"Quality Grade: {gradeText}\n\n" +
                               $"Calibration has been saved to flash memory.";

                MessageBoxImage icon = _qualityGrade <= 2 ? MessageBoxImage.Information : MessageBoxImage.Warning;
                MessageBox.Show(message, "Calibration Complete", MessageBoxButton.OK, icon);

                // Reset calibration state
                ResetCalibrationState();
            });
        }
        #endregion

        #region UI Event Handlers
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateUI();
            WeightInput.Focus();
        }

        private void WeightInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SetTarget_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                WeightInput.Text = "";
                e.Handled = true;
            }
        }

        private void WeightInput_GotFocus(object sender, RoutedEventArgs e)
        {
            WeightInput.SelectAll();
            WeightInput.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }

        private void WeightInput_LostFocus(object sender, RoutedEventArgs e)
        {
            WeightInput.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        private void StartCalibration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check connection first
                if (!IsConnected())
                {
                    MessageBox.Show("Device is disconnected. Please connect to hardware first.", 
                                  "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Validate inputs
                if (!double.TryParse(MaxWeightInput.Text, out _maxWeight) || _maxWeight <= 0)
                {
                    MessageBox.Show("Please enter a valid maximum weight.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Update channel mask
                UpdateChannelMask();

                if (_channelMask == 0)
                {
                    MessageBox.Show("Please select at least one channel.", "No Channels Selected", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Start calibration session
                _calibrationActive = true;
                _currentPoint = 0;
                _pointsCollected = 0;
                _targetWeight = 0.0;

                UpdateUI();
                UpdateStatusMessage("Calibration started - Enter target weight and capture points");

                // Request weight data
                RequestWeightData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting calibration: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetTarget_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(WeightInput.Text, out double weight))
            {
                if (weight >= 0 && weight <= _maxWeight)
                {
                    _targetWeight = weight;
                    WeightInput.Text = "";
                    UpdateUI();
                    
                    // Check if this weight is already captured
                    if (IsDuplicateWeight(weight))
                    {
                        UpdateStatusMessage($"Target weight set to {weight:F1}kg - WARNING: Already captured!");
                    }
                    else
                    {
                        UpdateStatusMessage($"Target weight set to {weight:F1}kg - Ready to capture");
                    }
                }
                else
                {
                    MessageBox.Show($"Weight must be between 0 and {_maxWeight}kg", 
                                  "Invalid Weight", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show("Please enter a valid numeric weight value", 
                              "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CapturePoint_Click(object sender, RoutedEventArgs e)
        {
            if (!_calibrationActive)
            {
                MessageBox.Show("Please start calibration first.", "No Active Calibration", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_targetWeight < 0)
            {
                MessageBox.Show("Please set a target weight first.", "No Target Weight", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check for duplicate weight points
            if (IsDuplicateWeight(_targetWeight))
            {
                MessageBox.Show($"A calibration point with weight {_targetWeight:F1}kg already exists.\n\nPlease use a different weight or delete the existing point first.", 
                              "Duplicate Weight Point", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Send 0x022 - Set Weight Point command
                SendSetWeightPointCommand(_targetWeight);
                UpdateStatusMessage($"Capturing point with weight {_targetWeight:F1}kg...");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing point: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteLast_Click(object sender, RoutedEventArgs e)
        {
            if (!_calibrationActive || _pointsCollected == 0)
            {
                MessageBox.Show("No points to delete.", "No Points", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("Are you sure you want to delete the last captured point?", 
                                       "Delete Last Point", MessageBoxButton.YesNo, 
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Send 0x02A - Manage Calibration Points (Delete Last)
                    SendManageCalibrationPointsCommand(0x01); // Delete Last operation
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting last point: {ex.Message}", "Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ResetSession_Click(object sender, RoutedEventArgs e)
        {
            if (!_calibrationActive)
            {
                MessageBox.Show("No active calibration session to reset.", "No Active Session", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("Are you sure you want to reset the calibration session?\n\nAll captured points will be lost.", 
                                       "Reset Calibration Session", MessageBoxButton.YesNo, 
                                       MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Send 0x02A - Manage Calibration Points (Reset Session)
                    SendManageCalibrationPointsCommand(0x02); // Reset Session operation
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error resetting session: {ex.Message}", "Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CompleteCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (!_calibrationActive)
            {
                MessageBox.Show("No active calibration to complete.", "No Active Calibration", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_pointsCollected < 2)
            {
                MessageBox.Show("At least 2 points are required to complete calibration.", 
                              "Insufficient Points", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Complete calibration with {_pointsCollected} captured points?", 
                                       "Complete Calibration", MessageBoxButton.YesNo, 
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Send 0x024 - Complete Variable Calibration
                    SendCompleteCalibrationCommand();
                    UpdateStatusMessage("Completing calibration - Please wait for analysis...");

                    // Set up timeout for 0x401 response
                    SetupResponseTimeout();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error completing calibration: {ex.Message}", "Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CancelCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (!_calibrationActive)
            {
                MessageBox.Show("No active calibration to cancel.", "No Active Calibration", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("Are you sure you want to cancel the calibration?\n\nAll progress will be lost.", 
                                       "Cancel Calibration", MessageBoxButton.YesNo, 
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Send reset session command to firmware to clear calibration data
                    SendManageCalibrationPointsCommand(0x02); // Reset Session
                    UpdateStatusMessage("Calibration cancelled - firmware session reset");
                }
                catch (Exception ex)
                {
                    UpdateStatusMessage($"Error cancelling calibration: {ex.Message}");
                }
                
                ResetCalibrationState();
            }
        }

        private void DeleteSpecificPoint_Click(object sender, RoutedEventArgs e)
        {
            if (!_calibrationActive)
            {
                MessageBox.Show("No active calibration session.", "No Active Calibration", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (sender is Button button && button.Tag is int pointIndex)
            {
                var result = MessageBox.Show($"Are you sure you want to delete point {pointIndex}?", 
                                           "Delete Calibration Point", MessageBoxButton.YesNo, 
                                           MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Check connection first
                        if (!IsConnected())
                        {
                            MessageBox.Show("Device is disconnected. Please connect to hardware first.", 
                                          "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // Send delete specific point command to firmware
                        bool success = CANService.ManageCalibrationPoints(_channelMask, 
                                                                        CANService.MANAGE_CAL_OP_DELETE_SPECIFIC, 
                                                                        (byte)pointIndex);
                        
                        if (success)
                        {
                            UpdateStatusMessage($"Deleting point {pointIndex}...");
                        }
                        else
                        {
                            UpdateStatusMessage("Failed to send delete command - device may be disconnected");
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateStatusMessage($"Error deleting point: {ex.Message}");
                    }
                }
            }
        }
        #endregion

        #region Timer Events
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateUI();
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            TimestampText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        #endregion

        #region CAN Communication
        private void RequestWeightData()
        {
            try
            {
                // Send 0x030 - Request Suspension Weight Data
                byte[] data = { 0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                CANService.SendStaticMessage(0x030, data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to request weight data: {ex.Message}");
            }
        }

        private void SendSetWeightPointCommand(double weight)
        {
            // Check connection before sending
            if (!IsConnected())
            {
                throw new Exception("Device is disconnected. Please connect to hardware first.");
            }

            try
            {
                // 0x022 - Set Weight Calibration Point
                byte[] data = new byte[8];
                data[0] = 0x03; // Command Type: Set Weight Point
                data[1] = 0x00; // Reserved (point index auto-assigned by firmware)
                
                ushort weightValue = (ushort)(weight * 10); // Convert to protocol format
                data[2] = (byte)(weightValue & 0xFF);
                data[3] = (byte)((weightValue >> 8) & 0xFF);
                
                data[4] = _channelMask; // Channel mask
                data[5] = 0x00; // Reserved
                data[6] = 0x00; // Reserved
                data[7] = 0x00; // Reserved

                bool success = CANService.SendStaticMessage(0x022, data);
                if (!success)
                {
                    throw new Exception("Failed to send CAN message - device may be disconnected");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send weight calibration point: {ex.Message}");
            }
        }

        private void SendManageCalibrationPointsCommand(byte operation)
        {
            try
            {
                // 0x02A - Manage Calibration Points
                byte[] data = new byte[8];
                data[0] = 0x0A; // Command Type: Manage Calibration Points
                data[1] = _channelMask; // Channel mask
                data[2] = operation; // Operation: 0x01=Delete Last, 0x02=Reset Session, 0x03=Get Count
                data[3] = 0x00; // Reserved
                data[4] = 0x00; // Reserved
                data[5] = 0x00; // Reserved
                data[6] = 0x00; // Reserved
                data[7] = 0x00; // Reserved

                CANService.SendStaticMessage(0x02A, data);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send manage calibration points command: {ex.Message}");
            }
        }

        private void SendCompleteCalibrationCommand()
        {
            try
            {
                // 0x024 - Complete Variable Calibration
                byte[] data = new byte[8];
                data[0] = 0x05; // Command Type: Complete Calibration
                data[1] = _channelMask; // Channel mask
                data[2] = 0x01; // Auto Analyze = true
                data[3] = 0x01; // Auto Save = true
                data[4] = 0x00; // Reserved
                data[5] = 0x00; // Reserved
                data[6] = 0x00; // Reserved
                data[7] = 0x00; // Reserved

                CANService.SendStaticMessage(0x024, data);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send complete calibration command: {ex.Message}");
            }
        }
        #endregion

        #region Helper Methods
        private bool IsConnected()
        {
            return CANService._instance?._connected ?? false;
        }

        private void UpdateChannelMask()
        {
            _channelMask = 0;
            if (Ch1Check.IsChecked == true) _channelMask |= 0x01;
            if (Ch2Check.IsChecked == true) _channelMask |= 0x02;
            if (Ch3Check.IsChecked == true) _channelMask |= 0x04;
            if (Ch4Check.IsChecked == true) _channelMask |= 0x08;
        }


        private void AddWeightSample(double weight)
        {
            _weightHistory.Enqueue(weight);
            if (_weightHistory.Count > 20)
            {
                _weightHistory.Dequeue();
            }
        }

        private void CheckStability()
        {
            if (_weightHistory.Count < 10)
            {
                _isStable = false;
                return;
            }

            // Calculate standard deviation
            double[] samples = _weightHistory.ToArray();
            double average = samples.Average();
            double variance = samples.Select(s => Math.Pow(s - average, 2)).Average();
            double stdDev = Math.Sqrt(variance);

            // Check if readings are stable
            _isStable = stdDev < 0.1; // 0.1kg stability threshold
        }

        private void UpdateConnectionStatus()
        {
            bool connected = CANService._instance?._connected ?? false;
            
            if (connected)
            {
                ConnectionStatus.Fill = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                ConnectionStatusText.Text = "Connected";
                CANStatusText.Text = "Connected";
                CANStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            }
            else
            {
                ConnectionStatus.Fill = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                ConnectionStatusText.Text = "Disconnected";
                CANStatusText.Text = "Disconnected";
                CANStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69));
            }
        }

        private void UpdateUI()
        {
            try
            {
                // Update connection status
                UpdateConnectionStatus();

                // Update current weight display
                CurrentWeightText.Text = _currentWeight.ToString("F1");

                // Load cell data section removed - no longer needed

                // Update target weight display
                if (_targetWeight > 0)
                {
                    TargetWeightText.Text = $"{_targetWeight:F1} kg";
                    TargetWeightBorder.Visibility = Visibility.Visible;
                    
                    // Check if this weight is already captured
                    if (IsDuplicateWeight(_targetWeight))
                    {
                        TargetWeightBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205)); // Light yellow
                        TargetWeightBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow border
                        TargetWeightText.Text = $"{_targetWeight:F1} kg (Already Captured)";
                        TargetWeightText.Foreground = new SolidColorBrush(Color.FromRgb(133, 100, 4)); // Dark yellow text
                    }
                    else
                    {
                        TargetWeightBorder.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)); // Light green
                        TargetWeightBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green border
                        TargetWeightText.Text = $"{_targetWeight:F1} kg";
                        TargetWeightText.Foreground = new SolidColorBrush(Color.FromRgb(21, 87, 36)); // Dark green text
                    }
                }
                else
                {
                    TargetWeightBorder.Visibility = Visibility.Collapsed;
                }

                // Update progress
                if (_calibrationActive)
                {
                    double progress = _pointsCollected > 0 ? ((double)_pointsCollected / 20.0) * 100.0 : 0;
                    CalibrationProgress.Value = Math.Min(progress, 100);
                    ProgressText.Text = $"Points: {_pointsCollected}/20";
                }
                else
                {
                    CalibrationProgress.Value = 0;
                    ProgressText.Text = "Ready to start calibration";
                }

                // Update button states
                SetTargetBtn.IsEnabled = true; // Always enabled - can set target anytime
                
                bool canCapture = _calibrationActive && _targetWeight > 0 && _isStable;
                CapturePointBtn.IsEnabled = canCapture;

                bool canComplete = _calibrationActive && _pointsCollected >= 2;
                CompleteCalibrationBtn.IsEnabled = canComplete;

                CancelCalibrationBtn.IsEnabled = _calibrationActive;

                // Update stability indicator
                if (_isStable)
                {
                    StabilityText.Text = "● Ready to Capture";
                    StabilityText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                    CurrentWeightBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                    CurrentWeightBorder.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                }
                else
                {
                    StabilityText.Text = "● Waiting for Stable Reading";
                    StabilityText.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                    CurrentWeightBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                    CurrentWeightBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205));
                }

                // Update data rate and last update
                DataRateText.Text = "500Hz";
                LastUpdateText.Text = _lastDataReceived.ToString("HH:mm:ss");

                // Update calibration points list
                UpdateCalibrationPointsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UI update error: {ex.Message}");
            }
        }

        private void UpdateStatusMessage(string message)
        {
            StatusMessageText.Text = message;
            CalibrationStatusText.Text = message;
        }

        private void UpdateCalibrationPointsList()
        {
            try
            {
                // Update the ListView with current calibration points
                CalibrationPointsList.ItemsSource = _calibrationPoints.ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating calibration points list: {ex.Message}");
            }
        }

        private void AddCalibrationPoint(int pointIndex, double weight, int adcValue)
        {
            try
            {
                var point = new CalibrationPointItem(pointIndex, weight, adcValue);
                _calibrationPoints.Add(point);
                UpdateCalibrationPointsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding calibration point: {ex.Message}");
            }
        }

        private void RemoveCalibrationPoint(int pointIndex)
        {
            try
            {
                // Remove the point with the specified index
                _calibrationPoints.RemoveAll(p => p.PointIndex == pointIndex);
                
                // Renumber remaining points to maintain sequential indexing
                for (int i = 0; i < _calibrationPoints.Count; i++)
                {
                    _calibrationPoints[i].PointIndex = i + 1;
                }
                
                UpdateCalibrationPointsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing calibration point: {ex.Message}");
            }
        }

        private void ClearCalibrationPoints()
        {
            try
            {
                _calibrationPoints.Clear();
                UpdateCalibrationPointsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing calibration points: {ex.Message}");
            }
        }

        private bool IsDuplicateWeight(double weight)
        {
            try
            {
                // Check if a point with this weight already exists
                // Use a tolerance of 0.1kg to account for floating point precision
                const double tolerance = 0.1;
                
                return _calibrationPoints.Any(point => 
                    Math.Abs(point.Weight - weight) < tolerance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for duplicate weight: {ex.Message}");
                return false; // If error, allow capture (fail-safe)
            }
        }

        private void UpdateQualityDisplay()
        {
            AccuracyText.Text = $"{_accuracyPercentage:F1}%";
            MaxErrorText.Text = $"{_maxErrorKg:F2}kg";
            GradeText.Text = GetQualityGradeText(_qualityGrade);

            QualityStatusBorder.Visibility = Visibility.Visible;
            QualityStatusText.Text = GetRecommendationText(_recommendation);
        }

        private string GetQualityGradeText(byte grade)
        {
            return grade switch
            {
                1 => "Excellent",
                2 => "Good",
                3 => "Acceptable",
                4 => "Poor",
                5 => "Failed",
                _ => "Unknown"
            };
        }

        private string GetRecommendationText(byte recommendation)
        {
            return recommendation switch
            {
                1 => "Accept Calibration",
                2 => "Retry Calibration",
                3 => "Add More Points",
                _ => "No Recommendation"
            };
        }

        private void ResetCalibrationState()
        {
            _calibrationActive = false;
            _currentPoint = 0;
            _pointsCollected = 0;
            _targetWeight = 0.0;
            _weightHistory.Clear();
            _isStable = false;

            // Reset quality metrics
            _accuracyPercentage = 0.0;
            _maxErrorKg = 0.0;
            _qualityGrade = 0;
            _recommendation = 0;

            // Clear calibration points list
            ClearCalibrationPoints();

            UpdateUI();
        }

        private void SetupResponseTimeout()
        {
            _responseTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _responseTimeoutTimer.Tick += (s, e) =>
            {
                _responseTimeoutTimer.Stop();
                MessageBox.Show("No calibration analysis response received!\n\nThe calibration command was sent but hardware did not respond.", 
                              "Response Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetCalibrationState();
            };
            _responseTimeoutTimer.Start();
        }
        #endregion

        #region Cleanup
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Stop timers
                _updateTimer?.Stop();
                _clockTimer?.Stop();
                _responseTimeoutTimer?.Stop();

                // Unsubscribe from events
                CANService.WeightDataReceived -= OnWeightDataReceived;
                CANService.ADCDataReceived -= OnADCDataReceived;
                CANService.CommunicationError -= OnCommunicationError;
                CANService.CalibrationDataReceived -= OnCalibrationDataReceived;
                CANService.CalibrationQualityReceived -= OnCalibrationQualityReceived;

                System.Diagnostics.Debug.WriteLine("SinglePageCalibration: Cleanup completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
        #endregion
    }

    /// <summary>
    /// Data model for calibration points display
    /// </summary>
    public class CalibrationPointItem
    {
        public int PointIndex { get; set; }
        public double Weight { get; set; }
        public int ADCValue { get; set; }
        public DateTime Timestamp { get; set; }

        public CalibrationPointItem(int pointIndex, double weight, int adcValue)
        {
            PointIndex = pointIndex;
            Weight = weight;
            ADCValue = adcValue;
            Timestamp = DateTime.Now;
        }
    }
}
