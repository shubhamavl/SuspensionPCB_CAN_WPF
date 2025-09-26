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
    public partial class WeightCalibrationPoint : Window
    {
        #region Private Fields - ACTUAL HARDWARE
        private int currentPoint = 1;
        private int totalPoints;
        private double currentWeight = 0.0;
        private double targetWeight = 0.0;
        private bool isStable = false;
        private DispatcherTimer updateTimer;

        // ACTUAL ADC Values from STM32 hardware
        private int frontLeftADC = 0;     // STM32 ADC Channel 0
        private int frontRightADC = 0;    // STM32 ADC Channel 1  
        private int rearLeftADC = 0;      // STM32 ADC Channel 2
        private int rearRightADC = 0;     // STM32 ADC Channel 3

        private double frontLeftVoltage = 0.0;
        private double frontRightVoltage = 0.0;
        private double rearLeftVoltage = 0.0;
        private double rearRightVoltage = 0.0;

        // Individual load cell weights
        private double frontLeftWeight = 0.0;
        private double frontRightWeight = 0.0;
        private double rearLeftWeight = 0.0;
        private double rearRightWeight = 0.0;

        // Stability tracking - Dynamic parameters
        private Queue<double> weightHistory = new Queue<double>();
        private DateTime lastStableTime = DateTime.MinValue;
        private int stabilityRequiredSamples = 20;
        private double stabilityThresholdKg = 0.05;
        private int stabilityDurationMs = 2000;

        // ACTUAL CALIBRATION CONFIGURATION - Protocol driven
        private byte polynomialOrder;
        private double maxWeight;
        private byte channelMask;

        // Communication timeout
        private DateTime lastDataReceived = DateTime.Now;
        private const int COMMUNICATION_TIMEOUT_MS = 5000; // 5 seconds

        // Response timeout timer
        private DispatcherTimer responseTimeoutTimer;
        #endregion

        #region Constructor
        public WeightCalibrationPoint()
        {
            InitializeComponent();

            // ADDED: WINDOW FOCUS MANAGEMENT
             this.Topmost = true; // Always on top
            this.ShowInTaskbar = true; // Show in taskbar
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen; // Center on screen

            // Handle window activation events
            //this.Activated += WeightCalibrationPoint_Activated;
           // this.Deactivated += WeightCalibrationPoint_Deactivated;


            InitializeCalibration();
            StartRealTimeUpdates();
        }


        // ADDED: Window focus event handlers
        //private void WeightCalibrationPoint_Activated(object sender, EventArgs e)
        //{
        //    System.Diagnostics.Debug.WriteLine("WeightCalibrationPoint activated - bringing to front");
        //    this.BringIntoView();
        //}

        //private void WeightCalibrationPoint_Deactivated(object sender, EventArgs e)
        //{
        //    System.Diagnostics.Debug.WriteLine("WeightCalibrationPoint deactivated");
        //}

        // ADDED: Manual focus method
        //public void BringToFront()
        //{
        //    try
        //    {
        //        if (this.WindowState == WindowState.Minimized)
        //        {
        //            this.WindowState = WindowState.Normal;
        //        }

        //        //this.Activate();
        //        //this.Focus();
        //        //this.Topmost = true;
        //        //this.Topmost = false; // Trick to bring to front
        //        //this.BringIntoView();

        //        System.Diagnostics.Debug.WriteLine("WeightCalibrationPoint brought to front");
        //    }
        //    catch (Exception ex)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"BringToFront error: {ex.Message}");
        //    }
        //}






        public WeightCalibrationPoint(int totalPoints, double maxKg, byte channels, byte polyOrder)
        {
            InitializeComponent();

            // Set protocol parameters
            this.totalPoints = totalPoints;
            this.maxWeight = maxKg;
            this.channelMask = channels;
            this.polynomialOrder = polyOrder;

            // Calculate adaptive stability parameters
            CalculateStabilityParameters();

            this.currentPoint = 1;
            System.Diagnostics.Debug.WriteLine($"WeightCalibrationPoint created: totalPoints={totalPoints}, maxWeight={maxKg}kg, channels=0x{channels:X2}");

            InitializeCalibration();
            StartRealTimeUpdates();
        }

        // Legacy constructor for backward compatibility
        public WeightCalibrationPoint(int totalPoints, int startPoint = 1)
        {
            InitializeComponent();
            this.totalPoints = totalPoints;
            this.currentPoint = startPoint;

            // Set default values for missing protocol parameters
            this.maxWeight = 1000.0;
            this.channelMask = 0x0F;
            this.polynomialOrder = 2;

            CalculateStabilityParameters();

            System.Diagnostics.Debug.WriteLine($"WeightCalibrationPoint created (legacy): totalPoints={totalPoints}, startPoint={startPoint}");
            InitializeCalibration();
            StartRealTimeUpdates();
        }

        private void CalculateStabilityParameters()
        {
            // Adaptive parameters based on protocol values
            stabilityThresholdKg = Math.Max(maxWeight * 0.001, 0.01); // 0.1% of max weight or 10g minimum
            stabilityRequiredSamples = Math.Min(30, Math.Max(15, totalPoints * 2));
            stabilityDurationMs = maxWeight > 1000 ? 3000 : 2000; // Longer for heavy loads

            System.Diagnostics.Debug.WriteLine($"Calculated stability: threshold={stabilityThresholdKg:F3}kg, samples={stabilityRequiredSamples}, duration={stabilityDurationMs}ms");
        }
        #endregion

        #region Initialization
        private void InitializeCalibration()
        {
            // Calculate target weight for current point
            targetWeight = CalculateTargetWeight(currentPoint);

            // Subscribe to ACTUAL CAN data updates
            CANService.WeightDataReceived += OnWeightDataReceived;
            CANService.ADCDataReceived += OnADCDataReceived;
            CANService.CommunicationError += OnCommunicationError;
            CANService.CalibrationDataReceived += OnCalibrationDataReceived;

            // FIXED: Subscribe to calibration quality response (0x401)
            CANService.CalibrationQualityReceived += OnCalibrationQualityReceived;

            // Set initial values
            UpdatePointCounter();
            UpdateTargetWeight();
            UpdateProgress();
            UpdateStatus("READY_TO_CAPTURE", "Enter target weight and click Capture Point");

            // Set focus to weight input for immediate keyboard entry
            ManualWeightInput.Focus();
        }

        private void StartRealTimeUpdates()
        {
            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // 10Hz updates for real hardware
            };
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }
        #endregion

        #region Keyboard Input Event Handlers
        private void ManualWeightInput_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter key press pe Set Target Weight action
            if (e.Key == Key.Enter)
            {
                ApplyManualWeight_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Escape key pe clear input
            if (e.Key == Key.Escape)
            {
                ManualWeightInput.Text = "";
                e.Handled = true;
                return;
            }

            // Only allow numbers, decimal point, and control keys
            if (!IsNumericInput(e.Key))
            {
                e.Handled = true;
            }
        }

        private void ManualWeightInput_GotFocus(object sender, RoutedEventArgs e)
        {
            // Visual feedback when focused - select all text for easy replacement
            ManualWeightInput.SelectAll();
            ManualWeightInput.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }

        private void ManualWeightInput_LostFocus(object sender, RoutedEventArgs e)
        {
            // Reset background when focus lost
            ManualWeightInput.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)); // Transparent
        }

        private bool IsNumericInput(Key key)
        {
            return (key >= Key.D0 && key <= Key.D9) ||        // Numbers 0-9
                   (key >= Key.NumPad0 && key <= Key.NumPad9) || // Numpad 0-9
                   key == Key.Decimal || key == Key.OemPeriod ||  // Decimal points
                   key == Key.Back || key == Key.Delete ||        // Editing keys
                   key == Key.Tab || key == Key.Enter ||          // Navigation
                   key == Key.Left || key == Key.Right ||         // Arrow keys
                   key == Key.Home || key == Key.End ||           // Home/End
                   key == Key.Escape;                             // Escape
        }
        #endregion

        #region Real-time Updates
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Always update UI regardless of hardware status
            UpdateCurrentReading();
            UpdateADCValues();
            UpdateStabilityIndicator();
        }

        private void UpdateCurrentReading()
        {
            CurrentWeightText.Text = currentWeight.ToString("F1");
        }

        private void OnCalibrationDataReceived(object sender, CalibrationDataEventArgs e)
        {
            Dispatcher.Invoke(() => {
                System.Diagnostics.Debug.WriteLine($"CalibrationDataReceived: PointIndex={e.PointIndex}, Status=0x{e.PointStatus:X2}, Weight={e.ReferenceWeight:F1}kg, ADC={e.ADCValue}");
                
                // SIMPLIFIED: Just log the response, no complex synchronization
                switch (e.PointStatus)
                {
                    case 0x80: // Calibration started
                        System.Diagnostics.Debug.WriteLine("*** CALIBRATION STARTED (0x80) ***");
                        UpdateStatus("READY_TO_CAPTURE", "Enter target weight and click Capture Point");
                        break;
                        
                    case 0x00: // Valid point
                        System.Diagnostics.Debug.WriteLine($"*** VALID POINT RECEIVED: Point {e.PointIndex}, Weight: {e.ReferenceWeight:F1}kg ***");
                        break;
                        
                    case 0x82: // Calibration completed
                        System.Diagnostics.Debug.WriteLine("*** CALIBRATION COMPLETED (0x82) ***");
                        UpdateStatus("CALIBRATION_COMPLETE", "Calibration completed successfully");
                        break;
                        
                    default:
                        System.Diagnostics.Debug.WriteLine($"*** RESPONSE RECEIVED: Status=0x{e.PointStatus:X2}, Point={e.PointIndex}, Weight={e.ReferenceWeight:F1}kg ***");
                        break;
                }
            });
        }

        // FIXED: Event handler for 0x401 calibration quality response
        private void OnCalibrationQualityReceived(object sender, CalibrationQualityEventArgs e)
        {
            Dispatcher.Invoke(() => {
                System.Diagnostics.Debug.WriteLine($"*** 0x401 CALIBRATION QUALITY RECEIVED! ***");
                System.Diagnostics.Debug.WriteLine($"Accuracy: {e.AccuracyPercentage:F1}%");
                System.Diagnostics.Debug.WriteLine($"Max Error: {e.MaxErrorKg:F2}kg");
                System.Diagnostics.Debug.WriteLine($"Quality Grade: {e.QualityGrade}");
                System.Diagnostics.Debug.WriteLine($"Recommendation: {e.Recommendation}");

                string gradeText = e.QualityGrade switch
                {
                    1 => "Excellent",
                    2 => "Good",
                    3 => "Fair",
                    4 => "Poor",
                    _ => "Unknown"
                };

                string recommendationText = e.Recommendation switch
                {
                    0 => "Calibration Accepted",
                    1 => "Recalibration Recommended",
                    2 => "Check Load Cells",
                    3 => "Check Connections",
                    _ => "Unknown Recommendation"
                };

                string message = $"Calibration Analysis Complete!\n\n" +
                                $"Accuracy: {e.AccuracyPercentage:F1}%\n" +
                                $"Max Error: {e.MaxErrorKg:F2}kg\n" +
                                $"Quality Grade: {gradeText}\n" +
                                $"Recommendation: {recommendationText}\n\n" +
                                $"Calibration has been saved to flash memory.";

                MessageBoxImage icon = e.QualityGrade <= 2 ? MessageBoxImage.Information : MessageBoxImage.Warning;

                MessageBox.Show(message, "Calibration Complete", MessageBoxButton.OK, icon);

                UpdateStatus("CALIBRATION_ANALYSIS_COMPLETE", $"Quality: {gradeText} ({e.AccuracyPercentage:F1}%)");

                // Close window after showing results
                CleanupAndClose();
            });
        }

        private void UpdateADCValues()
        {
            // Show ACTUAL hardware values from all 4 load cells
            FrontLeftADCText.Text = frontLeftADC.ToString();
            FrontRightADCText.Text = frontRightADC.ToString();
            RearLeftADCText.Text = rearLeftADC.ToString();
            RearRightADCText.Text = rearRightADC.ToString();

            FrontLeftVoltageText.Text = frontLeftVoltage.ToString("F2") + "V";
            FrontRightVoltageText.Text = frontRightVoltage.ToString("F2") + "V";
            RearLeftVoltageText.Text = rearLeftVoltage.ToString("F2") + "V";
            RearRightVoltageText.Text = rearRightVoltage.ToString("F2") + "V";

            // Update individual weight displays
            FrontLeftWeightText.Text = $"FL: {frontLeftWeight:F1}kg";
            FrontRightWeightText.Text = $"FR: {frontRightWeight:F1}kg";
            RearLeftWeightText.Text = $"RL: {rearLeftWeight:F1}kg";
            RearRightWeightText.Text = $"RR: {rearRightWeight:F1}kg";
        }

        private void UpdateStabilityIndicator()
        {
            // Always enable capture button regardless of hardware status
            CapturePointBtn.IsEnabled = true;

            // Set styling based on connection status
            if (isStable || (DateTime.Now - lastDataReceived).TotalMilliseconds > COMMUNICATION_TIMEOUT_MS)
            {
                // Green styling - ready to capture
                StabilityIndicator.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                CurrentReadingBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                CurrentReadingBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));

                if (targetWeight == 0.0)
                {
                    StabilityIndicator.Text = "● Ready to Capture (Zero Point)";
                }
                else
                {
                    StabilityIndicator.Text = "● Ready to Capture";
                }

                // Only update status if no target weight is set (don't override user-set status)
                if (targetWeight <= 0.0)
                {
                    UpdateStatus("READY_TO_CAPTURE", "Click capture button when ready");
                }
            }
            else
            {
                // Yellow styling - waiting
                StabilityIndicator.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                CurrentReadingBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                CurrentReadingBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205));
                StabilityIndicator.Text = "● Waiting for Weight";
            }
        }

        private void UpdatePointCounter()
        {
            PointCounterText.Text = $"Point {currentPoint} of {totalPoints}";

            if (AutoGeneratedPointText != null)
            {
                AutoGeneratedPointText.Text = $"System calculated optimal weight for Point {currentPoint}";
            }
        }

        private void UpdateTargetWeight()
        {
            TargetWeightText.Text = targetWeight.ToString("F1");

            // Show target display when weight is set
            if (targetWeight > 0)
            {
                TargetDisplayBorder.Visibility = Visibility.Visible;
            }
        }

        private void UpdateProgress()
        {
            double progressPercentage = ((double)(currentPoint - 1) / totalPoints) * 100;
            CalibrationProgress.Value = progressPercentage;
        }

        private void UpdateStatus(string statusCode, string message)
        {
            StatusTitle.Text = $"Point Status: {statusCode.Replace("_", " ")}";
            StatusMessage.Text = message;

            // Update colors based on status
            if (statusCode == "READY_TO_CAPTURE" || statusCode.Contains("TARED"))
            {
                StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(21, 87, 36));
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(21, 87, 36));
            }
            else if (statusCode.Contains("ERROR") || statusCode.Contains("TIMEOUT"))
            {
                StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(114, 28, 36));
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(114, 28, 36));
            }
            else
            {
                StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(13, 58, 78));
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(13, 58, 78));
            }
        }
        #endregion

        #region ACTUAL HARDWARE EVENT HANDLERS
        private void OnWeightDataReceived(object sender, WeightDataEventArgs e)
        {
            // Process ACTUAL weight data from CAN protocol
            if ((e.ChannelMask & channelMask) != 0)
            {
                lastDataReceived = DateTime.Now;

                // Store individual load cell weights
                frontLeftWeight = e.FrontLeftWeight;   // STM32 ADC Channel 0
                frontRightWeight = e.FrontRightWeight; // STM32 ADC Channel 1
                rearLeftWeight = e.RearLeftWeight;     // STM32 ADC Channel 2
                rearRightWeight = e.RearRightWeight;   // STM32 ADC Channel 3

                // Calculate total vehicle weight from all 4 load cells
                double totalWeight = CalculateTotalVehicleWeight();

                // Add to stability tracking
                AddWeightSample(totalWeight);

                Dispatcher.Invoke(() => {
                    currentWeight = totalWeight;
                    CheckStability();
                });
            }
        }

        private void OnADCDataReceived(object sender, ADCDataEventArgs e)
        {
            // Process ACTUAL ADC data from STM32 hardware
            lastDataReceived = DateTime.Now;

            Dispatcher.Invoke(() => {
                // ACTUAL STM32 ADC values (12-bit, 0-4095, 3.3V reference)
                frontLeftADC = e.FrontLeftADC;      // STM32 ADC Channel 0
                frontRightADC = e.FrontRightADC;    // STM32 ADC Channel 1
                rearLeftADC = e.RearLeftADC;        // STM32 ADC Channel 2  
                rearRightADC = e.RearRightADC;      // STM32 ADC Channel 3

                frontLeftVoltage = e.FrontLeftVoltage;
                frontRightVoltage = e.FrontRightVoltage;
                rearLeftVoltage = e.RearLeftVoltage;
                rearRightVoltage = e.RearRightVoltage;
            });
        }

        private void OnCommunicationError(object sender, CANErrorEventArgs e)
        {
            Dispatcher.Invoke(() => {
                UpdateStatus("CAN_ERROR", $"Communication error: {e.ErrorMessage}");
                isStable = false;
            });
        }
        #endregion

        #region Weight Processing - ACTUAL HARDWARE
        private double CalculateTotalVehicleWeight()
        {
            double totalWeight = 0.0;
            int activeChannels = 0;

            // Sum weights from all active load cells
            if ((channelMask & 0x01) != 0) // Front Left Channel 0
            {
                totalWeight += frontLeftWeight;
                activeChannels++;
            }

            if ((channelMask & 0x02) != 0) // Front Right Channel 1
            {
                totalWeight += frontRightWeight;
                activeChannels++;
            }

            if ((channelMask & 0x04) != 0) // Rear Left Channel 2
            {
                totalWeight += rearLeftWeight;
                activeChannels++;
            }

            if ((channelMask & 0x08) != 0) // Rear Right Channel 3
            {
                totalWeight += rearRightWeight;
                activeChannels++;
            }

            return activeChannels > 0 ? totalWeight : 0.0;
        }

        private void AddWeightSample(double weight)
        {
            weightHistory.Enqueue(weight);

            if (weightHistory.Count > stabilityRequiredSamples)
            {
                weightHistory.Dequeue();
            }
        }

        private void CheckStability()
        {
            if (weightHistory.Count < stabilityRequiredSamples)
            {
                isStable = false;
                return;
            }

            // Calculate standard deviation of recent samples
            double[] samples = weightHistory.ToArray();
            double average = samples.Average();
            double variance = samples.Select(s => Math.Pow(s - average, 2)).Average();
            double stdDev = Math.Sqrt(variance);

            // Check if readings are stable and within target range
            double targetTolerance = Math.Max(targetWeight * 0.02, 0.1); // 2% or 0.1kg minimum
            bool withinRange = Math.Abs(currentWeight - targetWeight) < targetTolerance;
            bool stableReadings = stdDev < stabilityThresholdKg;

            bool currentlyStable = stableReadings && withinRange;

            if (currentlyStable)
            {
                if (lastStableTime == DateTime.MinValue)
                {
                    lastStableTime = DateTime.Now;
                }

                // Check if stable for required duration
                if ((DateTime.Now - lastStableTime).TotalMilliseconds >= stabilityDurationMs)
                {
                    isStable = true;
                }
            }
            else
            {
                isStable = false;
                lastStableTime = DateTime.MinValue;
            }
        }
        #endregion

        #region Button Event Handlers
        private void CapturePoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if target weight is set
                if (targetWeight < 0)
                {
                    MessageBox.Show("Please enter a weight value in the input field and click 'Set Target Weight' first.",
                                   "No Target Weight Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Send the target weight that user has set
                SendCANMessage_SetWeightPoint(targetWeight);

                // SIMPLIFIED: Just assume success and move on
                UpdateStatus("POINT_CAPTURED", $"Point {currentPoint} captured with weight {targetWeight:F1}kg");
                NextPointBtn.IsEnabled = true;
                
                System.Diagnostics.Debug.WriteLine($"*** POINT CAPTURED: {currentPoint}, Weight: {targetWeight:F1}kg ***");

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing calibration point: {ex.Message}",
                               "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SkipPoint_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to skip this calibration point?\n\nSkipping points may affect calibration accuracy.",
                                        "Skip Calibration Point",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                UpdateStatus("POINT_SKIPPED", $"Point {currentPoint} skipped by user");

                if (currentPoint < totalPoints)
                {
                    MoveToNextPoint();
                }
                else
                {
                    CompleteCalibration();
                }
            }
        }

        private void RetryReading_Click(object sender, RoutedEventArgs e)
        {
            // Clear weight history and restart stability tracking
            weightHistory.Clear();
            isStable = false;
            lastStableTime = DateTime.MinValue;

            // Request fresh data from ACTUAL hardware
            RequestCurrentWeightData();

            UpdateStatus("READING_RETRIED", "Weight reading restarted - please wait for stable reading");
        }

        private void ApplyManualWeight_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(ManualWeightInput.Text, out double manualWeight))
            {
                if (manualWeight >= 0 && manualWeight <= maxWeight)
                {
                    targetWeight = manualWeight;

                    // Debug output
                    System.Diagnostics.Debug.WriteLine($"Target weight set: {targetWeight:F1}kg");

                    UpdateTargetWeight();
                    ManualWeightInput.Text = "";
                    UpdateStatus("TARGET_WEIGHT_SET", $"Target weight set to {manualWeight:F1}kg - ready to capture");
                }
                else
                {
                    MessageBox.Show($"Weight must be between 0 and {maxWeight}kg",
                                   "Invalid Weight", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show("Please enter a valid numeric weight value",
                               "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PreviousPoint_Click(object sender, RoutedEventArgs e)
        {
            if (currentPoint > 1)
            {
                currentPoint--;
                targetWeight = CalculateTargetWeight(currentPoint);

                // Reset stability tracking
                ResetStabilityTracking();

                UpdatePointCounter();
                UpdateTargetWeight();
                UpdateProgress();
                UpdateStatus("PREVIOUS_POINT", $"Moved to point {currentPoint}");

                // Update manual input
                ManualWeightInput.Text = targetWeight.ToString("F1");

                // Enable/disable navigation buttons
                NextPointBtn.IsEnabled = true;
                PreviousPointBtn.IsEnabled = currentPoint > 1;
            }
        }

        private void NextPoint_Click(object sender, RoutedEventArgs e)
        {
            MoveToNextPoint();
        }

        private void CancelCalibration_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to cancel the calibration?\n\nAll progress will be lost.",
                                        "Cancel Calibration",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                CleanupAndClose();
            }
        }

        // FIXED: CompleteCalibrationManually - Don't close immediately
        private void CompleteCalibrationManually_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Complete calibration with {currentPoint - 1} captured points?\n\nRemaining {totalPoints - currentPoint + 1} points will be skipped.",
                "Complete Calibration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SendCANMessage_CompleteCalibration();
                    UpdateStatus("CALIBRATION_COMPLETING", "Calibration completed - analysis in progress...");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error completing calibration: {ex.Message}",
                                   "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        #endregion

        #region Navigation Methods
        private void MoveToNextPoint()
        {
            if (currentPoint < totalPoints)
            {
                currentPoint++;
                targetWeight = CalculateTargetWeight(currentPoint);

                // Reset stability tracking
                ResetStabilityTracking();
                UpdatePointCounter();
                UpdateTargetWeight();
                UpdateProgress();
                UpdateStatus("READY_FOR_NEXT_POINT", $"Ready for point {currentPoint} - enter target weight: {targetWeight:F1}kg");

                // Set suggested weight in input field
                ManualWeightInput.Text = targetWeight.ToString("F1");
                ManualWeightInput.Focus();
                ManualWeightInput.SelectAll();

                // Enable/disable navigation buttons
                PreviousPointBtn.IsEnabled = true;
                NextPointBtn.IsEnabled = false; // Disabled until current point is captured
            }
        }

        private void ResetStabilityTracking()
        {
            weightHistory.Clear();
            isStable = false;
            lastStableTime = DateTime.MinValue;
        }

        private double CalculateTargetWeight(int pointIndex)
        {
            // Auto-spacing algorithm: Equal distribution from 0 to maxWeight
            if (totalPoints <= 1) return 0.0;

            double step = maxWeight / (totalPoints - 1);
            return (pointIndex - 1) * step;
        }
        #endregion

        #region ACTUAL CAN COMMUNICATION - Protocol Implementation
        private void RequestCurrentWeightData()
        {
            try
            {
                // Protocol 3.4.1: Request Suspension Weight Data (0x030)
                byte[] requestData = new byte[8];
                requestData[0] = 0x01; // Request Type: Start
                requestData[1] = 0x02; // Transmission Rate: 500Hz
                requestData[2] = 0x00; // Reserved
                requestData[3] = 0x00; // Reserved
                requestData[4] = 0x00; // Reserved
                requestData[5] = 0x00; // Reserved
                requestData[6] = 0x00; // Reserved
                requestData[7] = 0x00; // Reserved

                CANService.SendStaticMessage(0x030, requestData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to request weight data: {ex.Message}");
            }
        }

        private void SendCANMessage_SetWeightPoint(double weightToCapture)
        {
            try
            {
                byte pointIndex = (byte)(currentPoint - 1);

                // Use the ACTUAL target weight from UI
                double actualWeight = targetWeight;
                ushort weightValue = (ushort)(actualWeight * 10);

                System.Diagnostics.Debug.WriteLine($"Capturing Point {pointIndex}: Target={actualWeight:F1}kg, Encoded={weightValue}");

                byte[] canData = new byte[8];
                canData[0] = 0x01; // Command Type: Set Weight Point (CORRECTED)
                canData[1] = pointIndex;
                canData[2] = (byte)(weightValue & 0xFF);
                canData[3] = (byte)((weightValue >> 8) & 0xFF);
                canData[4] = channelMask;
                canData[5] = 0x00;
                canData[6] = 0x00;
                canData[7] = 0x00;

                System.Diagnostics.Debug.WriteLine($"Sending 0x022 SetWeightPoint: Command=0x01, Point={pointIndex}, Weight={weightValue}, ChannelMask=0x{channelMask:X2}");
                CANService.SendStaticMessage(0x022, canData);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send weight calibration point: {ex.Message}");
            }
        }

        private void SendCANMessage_CompleteCalibration()
        {
            try
            {
                // Protocol 3.3.4: Complete Variable Calibration (0x024)
                byte[] canData = new byte[8];
                canData[0] = 0x05; // Command Type: Complete Calibration
                canData[1] = channelMask; // Channel Mask (0x0F = All channels)
                canData[2] = 0x01; // Auto Analyze = true
                canData[3] = 0x01; // Auto Save = true
                canData[4] = 0x00; // Reserved
                canData[5] = 0x00; // Reserved
                canData[6] = 0x00; // Reserved
                canData[7] = 0x00; // Reserved
                CANService.SendStaticMessage(0x024, canData);

                System.Diagnostics.Debug.WriteLine($"CAN TX: Complete Variable Calibration command sent");
                System.Diagnostics.Debug.WriteLine($"CAN TX: ID=0x024, Data=[{string.Join(",", Array.ConvertAll(canData, b => $"0x{b:X2}"))}]");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send Complete Calibration command: {ex.Message}");
            }
        }

        // SIMPLIFIED: Removed timeout timers - just send commands and assume success
        #endregion

        #region Calibration Completion
        // FIXED: CompleteCalibration - Don't close immediately
        private void CompleteCalibration()
        {
            var result = MessageBox.Show("All calibration points have been captured!\n\nProceed to complete calibration and save to flash?",
                                        "Calibration Complete",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    SendCANMessage_CompleteCalibration();
                    UpdateStatus("CALIBRATION_COMPLETING", "Calibration completed - analysis in progress...");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error completing calibration: {ex.Message}",
                                   "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        #endregion

        #region Cleanup
        private void CleanupAndClose()
        {
            try
            {
                // SIMPLIFIED: No timeout timers to stop

                // Stop weight data transmission if needed
                // byte[] stopData = new byte[8];
                // stopData[0] = 0x00; // Request Type: Stop
                // CANService.SendStaticMessage(0x030, stopData);
            }
            catch { }

            // Unsubscribe from ALL events
            CANService.WeightDataReceived -= OnWeightDataReceived;
            CANService.ADCDataReceived -= OnADCDataReceived;
            CANService.CommunicationError -= OnCommunicationError;
            CANService.CalibrationDataReceived -= OnCalibrationDataReceived;

            // FIXED: Unsubscribe from calibration quality event
            CANService.CalibrationQualityReceived -= OnCalibrationQualityReceived;

            // ADDED: Unsubscribe window focus events
            //this.Activated -= WeightCalibrationPoint_Activated;   
            //this.Deactivated -= WeightCalibrationPoint_Deactivated;

            // Stop timer
            updateTimer?.Stop();

            System.Diagnostics.Debug.WriteLine("WeightCalibrationPoint: Cleanup completed, closing window");

            // Close window
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Ensure cleanup is called
            CleanupAndClose();
            base.OnClosed(e);
        }
        #endregion
    }
}