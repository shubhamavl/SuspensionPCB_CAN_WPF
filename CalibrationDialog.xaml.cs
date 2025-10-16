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
            
            // Start refresh timer to update current raw ADC
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
            
            UpdateStatus("Ready to calibrate. Place light load and capture Point 1.");
        }
        
        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            // TODO: Get current raw ADC from CAN service
            // For now, simulate with random values
            var random = new Random();
            _currentRawADC = random.Next(1500, 2500);
            CurrentRawTxt.Text = _currentRawADC.ToString();
        }
        
        private void CapturePoint1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!double.TryParse(Point1WeightTxt.Text, out double knownWeight))
                {
                    MessageBox.Show("Please enter a valid weight for Point 1.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (knownWeight < 0)
                {
                    MessageBox.Show("Weight cannot be negative.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                Point1RawADC = _currentRawADC;
                Point1KnownWeight = knownWeight;
                _point1Captured = true;
                
                UpdateStatus("Point 1 captured. Add heavy load and capture Point 2.");
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
                    MessageBox.Show("Please enter a valid weight for Point 2.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (knownWeight <= Point1KnownWeight)
                {
                    MessageBox.Show("Point 2 weight must be greater than Point 1 weight.", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                Point2RawADC = _currentRawADC;
                Point2KnownWeight = knownWeight;
                _point2Captured = true;
                
                UpdateStatus("Point 2 captured. Click 'Calculate Calibration' to proceed.");
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
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            base.OnClosed(e);
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
