using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SuspensionPCB_CAN_WPF
{
    public partial class CalibrationDialog : Window, INotifyPropertyChanged
    {
        private LinearCalibration? _calibration;
        private DispatcherTimer? _refreshTimer;
        private int _currentRawADC = 0;
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
        
        // Observable collection for dynamic calibration points
        public ObservableCollection<CalibrationPointViewModel> Points { get; set; } = new ObservableCollection<CalibrationPointViewModel>();
        
        private int _capturedPointCount = 0;
        public int CapturedPointCount
        {
            get => _capturedPointCount;
            set { _capturedPointCount = value; OnPropertyChanged(nameof(CapturedPointCount)); }
        }
        
        private byte _adcMode = 0;
        
        public CalibrationDialog(string side, byte adcMode = 0)
        {
            InitializeComponent();
            DataContext = this;
            Side = side;
            _adcMode = adcMode;
            
            // Get CAN service instance
            _canService = CANService._instance;
            if (_canService != null)
            {
                _canService.RawDataReceived += OnRawDataReceived;
                _logger.LogInfo($"Calibration dialog opened for {side} side (ADC mode: {adcMode})", "CalibrationDialog");
            }
            else
            {
                _logger.LogWarning("CAN service not available for calibration", "CalibrationDialog");
            }
            
            // Start refresh timer to update current raw ADC
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
            
            // Add first point by default
            AddNewPoint();
            
            // Bind points collection to ItemsControl
            PointsList.ItemsSource = Points;
        }
        
        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            // Update live ADC for all points (will be handled by binding in XAML)
            // This method is kept for compatibility but live updates are handled per-point
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

                    // Update live ADC for all non-captured points
                    foreach (var point in Points.Where(p => !p.IsCaptured))
                    {
                        point.RawADC = _currentRawADC;
                    }

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
        
        /// <summary>
        /// Add a new calibration point to the list
        /// </summary>
        public void AddNewPoint()
        {
            var newPoint = new CalibrationPointViewModel
            {
                PointNumber = Points.Count + 1,
                KnownWeight = Points.Count == 0 ? 0 : (Points.Last().KnownWeight + 500) // Suggest next weight
            };
            Points.Add(newPoint);
            UpdatePointNumbers();
        }
        
        /// <summary>
        /// Remove a calibration point
        /// </summary>
        public void RemovePoint(CalibrationPointViewModel point)
        {
            if (Points.Count <= 1)
            {
                MessageBox.Show("At least one calibration point is required.", "Cannot Remove", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (MessageBox.Show($"Remove Point {point.PointNumber}?", "Confirm Removal", 
                              MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Points.Remove(point);
                UpdatePointNumbers();
                UpdateCapturedCount();
            }
        }
        
        /// <summary>
        /// Update point numbers after add/remove
        /// </summary>
        private void UpdatePointNumbers()
        {
            for (int i = 0; i < Points.Count; i++)
            {
                Points[i].PointNumber = i + 1;
            }
        }
        
        /// <summary>
        /// Update captured point count
        /// </summary>
        private void UpdateCapturedCount()
        {
            CapturedPointCount = Points.Count(p => p.IsCaptured);
            
            // Enable Calculate button when at least 1 point is captured
            if (CalculateBtn != null)
            {
                CalculateBtn.IsEnabled = CapturedPointCount >= 1;
            }
        }
        
        /// <summary>
        /// Generic capture handler for any point
        /// </summary>
        private void CapturePoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.DataContext is CalibrationPointViewModel point)
                {
                    // Validate weight input
                    if (point.KnownWeight < 0)
                    {
                        MessageBox.Show("Weight cannot be negative.\n\nEnter 0 for empty platform or a positive weight.", "Invalid Input", 
                                      MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    if (point.KnownWeight > 10000)
                    {
                        MessageBox.Show("Weight cannot exceed 10,000 kg.\n\nEnter a reasonable weight value.", "Invalid Input", 
                                      MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Check if weight is increasing (warning only, not error)
                    var previousPoints = Points.Take(point.PointNumber - 1).Where(p => p.IsCaptured).ToList();
                    if (previousPoints.Any() && point.KnownWeight <= previousPoints.Max(p => p.KnownWeight))
                    {
                        var result = MessageBox.Show(
                            $"Point {point.PointNumber} weight ({point.KnownWeight:F0} kg) is not greater than previous points.\n\n" +
                            "For best accuracy, weights should increase. Continue anyway?",
                            "Weight Order Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (result == MessageBoxResult.No)
                            return;
                    }
                    
                    // Capture the point
                    point.RawADC = _currentRawADC;
                    point.IsCaptured = true;
                    
                    UpdateCapturedCount();
                    
                    // Update the point's RawADC display (it's now captured, so freeze the value)
                    point.RawADC = _currentRawADC; // Keep the captured value
                    
                    // Auto-add next point if this is the last one
                    if (point == Points.Last())
                    {
                        AddNewPoint();
                    }
                    
                    _logger.LogInfo($"Captured Point {point.PointNumber}: {point.KnownWeight:F0} kg @ ADC {point.RawADC}", "CalibrationDialog");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing point: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CalculateCalibration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Collect all captured points
                var capturedPoints = Points.Where(p => p.IsCaptured).ToList();
                
                if (capturedPoints.Count == 0)
                {
                    MessageBox.Show("Please capture at least one calibration point first.", "Incomplete Data", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Convert to CalibrationPoint list
                var calibrationPoints = capturedPoints.Select(p => p.ToCalibrationPoint()).ToList();
                
                // Calculate linear calibration using least-squares regression
                _calibration = LinearCalibration.FitMultiplePoints(calibrationPoints);
                
                // Set ADC mode from current settings
                byte currentADCMode = _adcMode;
                if (currentADCMode == 0 && SettingsManager.Instance != null)
                {
                    currentADCMode = SettingsManager.Instance.GetLastKnownADCMode();
                }
                _calibration.ADCMode = currentADCMode;
                
                // Display results
                PopupEquationTxt.Text = _calibration.GetEquationString();
                PopupEquationTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF495057"));
                
                // Display quality metrics
                string qualityText = $"Quality: {_calibration.GetQualityAssessment()}\n" +
                                   $"R² = {_calibration.R2:F4}\n" +
                                   $"Max Error: {_calibration.MaxErrorPercent:F2}%\n" +
                                   $"Points Used: {capturedPoints.Count}";
                
                // Color based on R²
                if (_calibration.R2 >= 0.999)
                {
                    PopupErrorTxt.Text = $"✓ {qualityText}";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
                }
                else if (_calibration.R2 >= 0.99)
                {
                    PopupErrorTxt.Text = $"✓ {qualityText}";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
                }
                else if (_calibration.R2 >= 0.95)
                {
                    PopupErrorTxt.Text = $"⚠ {qualityText}";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFC107"));
                }
                else
                {
                    PopupErrorTxt.Text = $"✗ {qualityText}";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDC3545"));
                }
                
                SaveBtn.IsEnabled = true;
                ViewResultsBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error calculating calibration: {ex.Message}", "Calculation Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            AddNewPoint();
        }
        
        private void RemovePoint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is CalibrationPointViewModel point)
            {
                RemovePoint(point);
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
                
                // Use ADC mode passed to constructor (or fallback to settings)
                byte currentADCMode = _adcMode;
                if (currentADCMode == 0 && SettingsManager.Instance != null)
                {
                    currentADCMode = SettingsManager.Instance.GetLastKnownADCMode();
                }
                _calibration.SaveToFile(Side, currentADCMode);
                
                string modeText = currentADCMode == 0 ? "Internal ADC" : "ADS1115";
                MessageBox.Show($"Calibration saved successfully for {Side} side ({modeText}).\n\n" +
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
