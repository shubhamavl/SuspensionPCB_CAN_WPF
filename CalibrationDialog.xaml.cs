using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace SuspensionPCB_CAN_WPF
{
    public partial class CalibrationDialog : Window, INotifyPropertyChanged
    {
        private LinearCalibration? _calibration;
        private DispatcherTimer? _refreshTimer;
        private int _currentRawADC = 0;
        private bool _point1Captured = false;
        private bool _point2Captured = false;
        private CANService? _canService;
        private ProductionLogger _logger = ProductionLogger.Instance;
        
        // Properties for data binding
        private string _side = "";
        public string Side
        {
            get => _side;
            set { _side = value; OnPropertyChanged(nameof(Side)); }
        }
        
        private int _point1RawADC = 0;
        public int Point1RawADC
        {
            get => _point1RawADC;
            set { _point1RawADC = value; OnPropertyChanged(nameof(Point1RawADC)); }
        }
        
        private double _point1KnownWeight = 0;
        public double Point1KnownWeight
        {
            get => _point1KnownWeight;
            set { _point1KnownWeight = value; OnPropertyChanged(nameof(Point1KnownWeight)); }
        }
        
        private int _point2RawADC = 0;
        public int Point2RawADC
        {
            get => _point2RawADC;
            set { _point2RawADC = value; OnPropertyChanged(nameof(Point2RawADC)); }
        }
        
        private double _point2KnownWeight = 0;
        public double Point2KnownWeight
        {
            get => _point2KnownWeight;
            set { _point2KnownWeight = value; OnPropertyChanged(nameof(Point2KnownWeight)); }
        }
        
        public CalibrationDialog(string side)
        {
            InitializeComponent();
            DataContext = this;
            Side = side;
            
            // Get CAN service instance
            _canService = CANService._instance;
            if (_canService != null)
            {
                _canService.RawDataReceived += OnRawDataReceived;
                _logger.LogInfo($"Calibration dialog opened for {side} side", "CalibrationDialog");
            }
            else
            {
                _logger.LogWarning("CAN service not available for calibration", "CalibrationDialog");
            }
            
            // Start refresh timer to update current raw ADC
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
            
            UpdateStatus("Ready to calibrate. Place light load and capture Point 1.");
            
            // Set default values and focus
            Point1KnownWeight = 0;    // Default to empty platform (integer)
            Point2KnownWeight = 1000; // Default to 1000kg calibration weight (integer)
            Point1WeightTxt.Focus();
        }
        
        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            // Update current raw ADC display
            CurrentRawTxt.Text = _currentRawADC.ToString();
        }
        
        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            try
            {
                // Update ADC value based on side
                if ((Side == "Left" && e.Side == 0) || (Side == "Right" && e.Side == 1))
                {
                    _currentRawADC = e.RawADCSum;
                    _logger.LogInfo($"Raw ADC updated for {Side} side: {_currentRawADC}", "CalibrationDialog");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing raw data: {ex.Message}", "CalibrationDialog");
            }
        }
        
        private void CapturePoint1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!double.TryParse(Point1WeightTxt.Text, out double knownWeight))
                {
                    MessageBox.Show("Please enter a valid weight for Point 1.\n\nExample: 0 for empty platform", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point1WeightTxt.Focus();
                    return;
                }
                
                // Validate positive integer only
                if (knownWeight < 0)
                {
                    MessageBox.Show("Weight cannot be negative.\n\nEnter 0 for empty platform.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point1WeightTxt.Focus();
                    return;
                }
                
                if (knownWeight != Math.Floor(knownWeight))
                {
                    MessageBox.Show("Weight must be a whole number (integer).\n\nExample: 0, 50, 100 (not 0.5 or 1.2)", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point1WeightTxt.Focus();
                    return;
                }
                
                if (knownWeight > 10000)
                {
                    MessageBox.Show("Weight cannot exceed 10,000 kg.\n\nEnter a reasonable weight value.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point1WeightTxt.Focus();
                    return;
                }
                
                Point1RawADC = _currentRawADC;
                Point1KnownWeight = (int)knownWeight; // Convert to integer
                _point1Captured = true;
                
                // Update UI to show captured data
                Point1RawTxt.Text = _currentRawADC.ToString();
                Point1WeightTxt.Text = ((int)knownWeight).ToString();
                
                UpdateStatus($"✓ Point 1 captured: {(int)knownWeight} kg at ADC {_currentRawADC}. Add heavy load and capture Point 2.");
                
                // Focus on Point 2 weight input
                Point2WeightTxt.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing Point 1: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CapturePoint2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!double.TryParse(Point2WeightTxt.Text, out double knownWeight))
                {
                    MessageBox.Show("Please enter a valid weight for Point 2.\n\nExample: 50 for 50kg calibration weight", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point2WeightTxt.Focus();
                    return;
                }
                
                // Validate positive integer only
                if (knownWeight < 0)
                {
                    MessageBox.Show("Weight cannot be negative.\n\nEnter a positive weight value.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point2WeightTxt.Focus();
                    return;
                }
                
                if (knownWeight != Math.Floor(knownWeight))
                {
                    MessageBox.Show("Weight must be a whole number (integer).\n\nExample: 50, 100, 200 (not 50.5 or 1.2)", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point2WeightTxt.Focus();
                    return;
                }
                
                if (knownWeight > 10000)
                {
                    MessageBox.Show("Weight cannot exceed 10,000 kg.\n\nEnter a reasonable weight value.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point2WeightTxt.Focus();
                    return;
                }
                
                if (knownWeight <= Point1KnownWeight)
                {
                    MessageBox.Show($"Point 2 weight ({(int)knownWeight} kg) must be greater than Point 1 weight ({Point1KnownWeight} kg).\n\nAdd more weight for accurate calibration.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    Point2WeightTxt.Focus();
                    return;
                }
                
                Point2RawADC = _currentRawADC;
                Point2KnownWeight = (int)knownWeight; // Convert to integer
                _point2Captured = true;
                
                // Update UI to show captured data
                Point2RawTxt.Text = _currentRawADC.ToString();
                Point2WeightTxt.Text = ((int)knownWeight).ToString();
                
                UpdateStatus($"✓ Point 2 captured: {(int)knownWeight} kg at ADC {_currentRawADC}. Click 'Calculate Calibration' to proceed.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing Point 2: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CalculateCalibration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_point1Captured || !_point2Captured)
                {
                    MessageBox.Show("Please capture both calibration points first.", "Incomplete Data", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Calculate linear calibration
                _calibration = LinearCalibration.FitTwoPoints(
                    Point1RawADC, Point1KnownWeight,
                    Point2RawADC, Point2KnownWeight
                );
                
                // Display results
                EquationTxt.Text = _calibration.GetEquationString();
                
                // Verify accuracy
                double error1 = _calibration.VerifyPoint(Point1RawADC, Point1KnownWeight);
                double error2 = _calibration.VerifyPoint(Point2RawADC, Point2KnownWeight);
                double maxError = Math.Max(error1, error2);
                
                if (maxError < 0.1)
                {
                    ErrorTxt.Text = $"✓ Excellent accuracy (max error: {maxError:F2}%)";
                    ErrorTxt.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (maxError < 1.0)
                {
                    ErrorTxt.Text = $"✓ Good accuracy (max error: {maxError:F2}%)";
                    ErrorTxt.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (maxError < 5.0)
                {
                    ErrorTxt.Text = $"⚠ Acceptable accuracy (max error: {maxError:F2}%)";
                    ErrorTxt.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else
                {
                    ErrorTxt.Text = $"✗ Poor accuracy (max error: {maxError:F2}%)";
                    ErrorTxt.Foreground = System.Windows.Media.Brushes.Red;
                }
                
                SaveBtn.IsEnabled = true;
                UpdateStatus("Calibration calculated successfully. Click 'Save Calibration' to save.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error calculating calibration: {ex.Message}", "Calculation Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveCalibration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_calibration == null)
                {
                    MessageBox.Show("No calibration to save. Please calculate calibration first.", "No Data", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                _calibration.SaveToFile(Side);
                
                MessageBox.Show($"Calibration saved successfully for {Side} side.\n\n" +
                              $"Equation: {_calibration.GetEquationString()}\n\n" +
                              "You can now use the 'Tare' button on the main window to zero-out platform weight.",
                              "Calibration Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving calibration: {ex.Message}", "Save Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void RefreshRaw_Click(object sender, RoutedEventArgs e)
        {
            // Force refresh of current raw ADC
            RefreshTimer_Tick(null, EventArgs.Empty);
        }
        
        private void UpdateStatus(string message)
        {
            StatusTxt.Text = message;
            UpdateStepVisuals();
        }
        
        private void UpdateStepVisuals()
        {
            try
            {
                // Update step visual indicators
                if (_point1Captured && !_point2Captured)
                {
                    // Point 1 completed, Point 2 active
                    Point1GroupBox.Style = (Style)FindResource("StepGroupBox");
                    Point2GroupBox.Style = (Style)FindResource("ActiveStepGroupBox");
                }
                else if (_point1Captured && _point2Captured)
                {
                    // Both points completed
                    Point1GroupBox.Style = (Style)FindResource("StepGroupBox");
                    Point2GroupBox.Style = (Style)FindResource("StepGroupBox");
                }
                else
                {
                    // Point 1 active
                    Point1GroupBox.Style = (Style)FindResource("ActiveStepGroupBox");
                    Point2GroupBox.Style = (Style)FindResource("StepGroupBox");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating step visuals: {ex.Message}", "CalibrationDialog");
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            
            // Unsubscribe from CAN service events
            if (_canService != null)
            {
                _canService.RawDataReceived -= OnRawDataReceived;
            }
            
            base.OnClosed(e);
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
