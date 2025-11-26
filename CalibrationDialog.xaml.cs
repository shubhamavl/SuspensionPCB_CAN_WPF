using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;

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
        private DateTime _lastRawLogTime = DateTime.MinValue;
        private int _rawSamplesSinceLog = 0;
        
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
            // Update live ADC displays
            if (Point1LiveADCTxt != null)
            {
                Point1LiveADCTxt.Text = _currentRawADC.ToString();
            }
            if (Point2LiveADCTxt != null)
            {
                Point2LiveADCTxt.Text = _currentRawADC.ToString();
            }
        }
        
        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            try
            {
                // Update ADC value based on side
                if ((Side == "Left" && e.Side == 0) || (Side == "Right" && e.Side == 1))
                {
                    _currentRawADC = e.RawADCSum;
                    _rawSamplesSinceLog++;

                    var now = DateTime.Now;
                    if (_lastRawLogTime == DateTime.MinValue || (now - _lastRawLogTime).TotalSeconds >= 1)
                    {
                        _logger.LogInfo($"Raw ADC update ({Side}): {_currentRawADC} (samples since last log: {_rawSamplesSinceLog})", "CalibrationDialog");
                        _rawSamplesSinceLog = 0;
                        _lastRawLogTime = now;
                    }
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
                
                // Enable Step 2 button and update status
                CapturePoint2Btn.IsEnabled = true;
                Point1StatusTxt.Text = $"✓ Captured: {(int)knownWeight} kg at ADC {_currentRawADC}";
                Point1StatusTxt.Foreground = System.Windows.Media.Brushes.Green;
                Point2StatusTxt.Text = "Ready to capture";
                Point2StatusTxt.Foreground = System.Windows.Media.Brushes.Green;
                
                // Update step visuals
                UpdateStepVisuals();
                
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
                
                // Update status
                Point2StatusTxt.Text = $"✓ Captured: {(int)knownWeight} kg at ADC {_currentRawADC}";
                Point2StatusTxt.Foreground = System.Windows.Media.Brushes.Green;
                
                // Update step visuals
                UpdateStepVisuals();
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
                
                // Display results (only in popup now)
                PopupEquationTxt.Text = _calibration.GetEquationString();
                PopupEquationTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF495057"));
                
                // Verify accuracy
                double error1 = _calibration.VerifyPoint(Point1RawADC, Point1KnownWeight);
                double error2 = _calibration.VerifyPoint(Point2RawADC, Point2KnownWeight);
                double maxError = Math.Max(error1, error2);
                
                if (maxError < 0.1)
                {
                    PopupErrorTxt.Text = $"✓ Excellent accuracy (max error: {maxError:F2}%)";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
                }
                else if (maxError < 1.0)
                {
                    PopupErrorTxt.Text = $"✓ Good accuracy (max error: {maxError:F2}%)";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
                }
                else if (maxError < 5.0)
                {
                    PopupErrorTxt.Text = $"⚠ Acceptable accuracy (max error: {maxError:F2}%)";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFC107"));
                }
                else
                {
                    PopupErrorTxt.Text = $"✗ Poor accuracy (max error: {maxError:F2}%)";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDC3545"));
                }
                
                SaveBtn.IsEnabled = true;
                
                // Enable View Results button
                ViewResultsBtn.IsEnabled = true;
                
                // Update status messages
                Point1StatusTxt.Text = $"✓ Point 1: {Point1KnownWeight} kg @ ADC {Point1RawADC}";
                Point2StatusTxt.Text = $"✓ Point 2: {Point2KnownWeight} kg @ ADC {Point2RawADC}";
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
            // Status is now shown in individual step status text blocks
            // This method is kept for compatibility but doesn't do much in the new design
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
                    if (Step1Circle != null) Step1Circle.Style = (Style)FindResource("StepperCircleCompleted");
                    if (Step1Line != null) Step1Line.Style = (Style)FindResource("StepperLineCompleted");
                    if (Step2Circle != null) Step2Circle.Style = (Style)FindResource("StepperCircleActive");
                    
                    // Update step headers
                    UpdateStepHeaders(true, false);
                }
                else if (_point1Captured && _point2Captured)
                {
                    // Both points completed
                    if (Step1Circle != null) Step1Circle.Style = (Style)FindResource("StepperCircleCompleted");
                    if (Step1Line != null) Step1Line.Style = (Style)FindResource("StepperLineCompleted");
                    if (Step2Circle != null) Step2Circle.Style = (Style)FindResource("StepperCircleCompleted");
                    
                    // Update step headers
                    UpdateStepHeaders(true, true);
                }
                else
                {
                    // Point 1 active
                    if (Step1Circle != null) Step1Circle.Style = (Style)FindResource("StepperCircleActive");
                    if (Step1Line != null) Step1Line.Style = (Style)FindResource("StepperLine");
                    if (Step2Circle != null) Step2Circle.Style = (Style)FindResource("StepperCircle");
                    
                    // Update step headers
                    UpdateStepHeaders(false, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating step visuals: {ex.Message}", "CalibrationDialog");
            }
        }
        
        private void UpdateStepHeaders(bool step1Completed, bool step2Completed)
        {
            try
            {
                // Update step header ellipses and text colors
                var step1Ellipse = FindName("Step1Ellipse") as Ellipse;
                var step2Ellipse = FindName("Step2Ellipse") as Ellipse;
                
                if (step1Ellipse != null)
                {
                    step1Ellipse.Fill = step1Completed ? 
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745")) : 
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1B5E96"));
                }
                
                if (step2Ellipse != null)
                {
                    step2Ellipse.Fill = step2Completed ? 
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745")) : 
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6C757D"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStepHeaders error: {ex.Message}");
            }
        }
        
        private void UpdateStepTextColors(bool step1Completed, bool step2Completed)
        {
            try
            {
                // Find the text blocks within the stepper grids
                var step1Grid = Step1Circle?.Parent as Grid;
                var step2Grid = Step2Circle?.Parent as Grid;
                
                if (step1Grid != null)
                {
                    var step1Text = step1Grid.Children.OfType<TextBlock>().FirstOrDefault();
                    if (step1Text != null)
                    {
                        step1Text.Foreground = Brushes.White; // Always white for visibility
                    }
                }
                
                if (step2Grid != null)
                {
                    var step2Text = step2Grid.Children.OfType<TextBlock>().FirstOrDefault();
                    if (step2Text != null)
                    {
                        step2Text.Foreground = step2Completed ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6C757D"));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStepTextColors error: {ex.Message}");
            }
        }
        
        private void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            // Show instructions popup
            InstructionsPopup.IsOpen = true;
        }
        
        private void CloseInstructionsPopup_Click(object sender, RoutedEventArgs e)
        {
            InstructionsPopup.IsOpen = false;
        }
        
        private void ViewResultsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Show results popup - always allow clicking
            // If calibration not calculated, show grey/disabled results
            if (!ViewResultsBtn.IsEnabled)
            {
                // Show disabled/grey results
                PopupEquationTxt.Text = "No calibration data available";
                PopupEquationTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB0B0B0"));
                PopupErrorTxt.Text = "Please complete calibration first";
                PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB0B0B0"));
            }
            else
            {
                // Show normal results (already calculated)
                PopupEquationTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF495057"));
                PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDC3545"));
            }
            
            ResultsPopup.IsOpen = true;
        }
        
        private void CloseResultsPopup_Click(object sender, RoutedEventArgs e)
        {
            ResultsPopup.IsOpen = false;
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
