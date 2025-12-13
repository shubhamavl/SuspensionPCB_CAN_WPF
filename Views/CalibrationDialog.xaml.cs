using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Services;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class CalibrationDialog : Window, INotifyPropertyChanged
    {
        private LinearCalibration? _calibration;
        private LinearCalibration? _internalCalibration;
        private LinearCalibration? _ads1115Calibration;
        private DispatcherTimer? _refreshTimer;
        private int _currentRawADC = 0;
        private CANService? _canService;
        private ProductionLogger _logger = ProductionLogger.Instance;
        private DateTime _lastRawLogTime = DateTime.MinValue;
        private int _rawSamplesSinceLog = 0;
        
        // Dual-mode tracking
        private bool _isCapturingDualMode = false;
        private byte _startingADCMode = 0;
        private CalibrationPointViewModel? _currentCapturingPoint = null;
        private bool _hasStream = false;
        private int _calibrationDelayMs = 500; // Configurable delay before capturing calibration point
        
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
        
        public CalibrationDialog(string side, byte adcMode = 0, int calibrationDelayMs = 500)
        {
            InitializeComponent();
            DataContext = this;
            Side = side;
            _adcMode = adcMode;
            _startingADCMode = adcMode;
            _calibrationDelayMs = calibrationDelayMs;
            
            // Get CAN service instance
            _canService = CANService._instance;
            if (_canService != null && _canService.IsConnected)
            {
                _canService.RawDataReceived += OnRawDataReceived;
                _hasStream = true;
                _logger.LogInfo($"Calibration dialog opened for {side} side (ADC mode: {adcMode}) - Stream available", "CalibrationDialog");
            }
            else
            {
                _hasStream = false;
                _logger.LogInfo($"Calibration dialog opened for {side} side (ADC mode: {adcMode}) - Manual entry mode (no stream)", "CalibrationDialog");
            }
            
            // Start refresh timer to update current raw ADC (only if stream available)
            if (_hasStream)
            {
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _refreshTimer.Tick += RefreshTimer_Tick;
                _refreshTimer.Start();
            }
            
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

                    // Update live ADC for all non-captured points based on current ADC mode
                    foreach (var point in Points.Where(p => !p.BothModesCaptured))
                    {
                        point.RawADC = _currentRawADC; // For backward compatibility
                        
                        // Update the appropriate ADC value based on current mode
                        if (_adcMode == 0)
                        {
                            point.InternalADC = (ushort)_currentRawADC;
                        }
                        else
                        {
                            // ADS1115: Store as signed int (can be negative)
                            point.ADS1115ADC = _currentRawADC;
                        }
                    }

                    var now = DateTime.Now;
                    if (_lastRawLogTime == DateTime.MinValue || (now - _lastRawLogTime).TotalSeconds >= 1)
                    {
                        _logger.LogInfo($"Raw ADC update ({Side}, mode={_adcMode}): {_currentRawADC} (samples since last log: {_rawSamplesSinceLog})", "CalibrationDialog");
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
                KnownWeight = 0 // Start with zero weight, user can enter any weight in any order
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
        /// Generic capture handler for any point - automatically captures both ADC modes
        /// </summary>
        private async void CapturePoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.DataContext is CalibrationPointViewModel point)
                {
                    // Prevent multiple simultaneous captures
                    if (_isCapturingDualMode)
                    {
                        MessageBox.Show("Please wait for the current capture to complete.", "Capture In Progress", 
                                      MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    
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
                    
                    // Check for duplicate weights (warning only, not error)
                    var duplicatePoints = Points.Where(p => p.BothModesCaptured && 
                                                           Math.Abs(p.KnownWeight - point.KnownWeight) < 0.01 && 
                                                           p != point).ToList();
                    if (duplicatePoints.Any())
                    {
                        var result = MessageBox.Show(
                            $"Point {point.PointNumber} weight ({point.KnownWeight:F0} kg) matches existing point(s).\n\n" +
                            "Duplicate weights may reduce calibration accuracy. Continue anyway?",
                            "Duplicate Weight Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (result == MessageBoxResult.No)
                            return;
                    }
                    
                    _isCapturingDualMode = true;
                    _currentCapturingPoint = point;
                    button.IsEnabled = false;
                    
                    if (_hasStream && _canService != null && _canService.IsConnected)
                    {
                        // Automatic dual-mode capture with stream
                        await CaptureDualModeWithStream(point);
                    }
                    else
                    {
                        // Manual entry mode - user enters ADC values manually
                        CaptureDualModeManual(point);
                    }
                    
                    button.IsEnabled = true;
                    _isCapturingDualMode = false;
                    _currentCapturingPoint = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing point: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                _isCapturingDualMode = false;
                _currentCapturingPoint = null;
            }
        }
        
        /// <summary>
        /// Enter edit mode for a calibration point
        /// </summary>
        private void EditPoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.DataContext is CalibrationPointViewModel point)
                {
                    point.IsEditing = true;
                    _logger.LogInfo($"Entered edit mode for point {point.PointNumber}", "CalibrationDialog");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error entering edit mode: {ex.Message}", "CalibrationDialog");
                MessageBox.Show($"Error entering edit mode: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Save manually entered ADC values for a calibration point
        /// </summary>
        private void SavePoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.DataContext is CalibrationPointViewModel point)
                {
                    // Validate ADC values
                    if (point.InternalADC == 0 && point.ADS1115ADC == 0)
                    {
                        MessageBox.Show("Please enter at least one ADC value (Internal or ADS1115).", 
                                      "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Validate ranges
                    if (point.InternalADC > 4095)
                    {
                        MessageBox.Show($"Internal ADC value must be between 0-4095. Current: {point.InternalADC}", 
                                      "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    if (point.ADS1115ADC < -32768 || point.ADS1115ADC > 32767)
                    {
                        MessageBox.Show($"ADS1115 ADC value must be between -32768 to +32767. Current: {point.ADS1115ADC}", 
                                      "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Validate weight input
                    if (point.KnownWeight < 0)
                    {
                        MessageBox.Show("Weight cannot be negative.\n\nEnter 0 for empty platform or a positive weight.", 
                                      "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    if (point.KnownWeight > 10000)
                    {
                        MessageBox.Show("Weight cannot exceed 10,000 kg.\n\nEnter a reasonable weight value.", 
                                      "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Mark as captured if both values are set
                    if (point.InternalADC > 0 && point.ADS1115ADC != 0)
                    {
                        point.BothModesCaptured = true;
                        point.IsCaptured = true;
                    }
                    else if (point.InternalADC > 0 || point.ADS1115ADC != 0)
                    {
                        point.IsCaptured = true;
                    }
                    
                    point.IsEditing = false;
                    UpdateCapturedCount();
                    _logger.LogInfo($"Saved manual values for point {point.PointNumber}: Internal={point.InternalADC}, ADS1115={point.ADS1115ADC}, Weight={point.KnownWeight:F1} kg", "CalibrationDialog");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving point: {ex.Message}", "CalibrationDialog");
                MessageBox.Show($"Error saving point: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Cancel edit mode for a calibration point
        /// </summary>
        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.DataContext is CalibrationPointViewModel point)
                {
                    // Exit edit mode without saving changes
                    point.IsEditing = false;
                    _logger.LogInfo($"Cancelled edit for point {point.PointNumber}", "CalibrationDialog");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cancelling edit: {ex.Message}", "CalibrationDialog");
                MessageBox.Show($"Error cancelling edit: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Capture both ADC modes automatically when stream is available
        /// Uses multi-sample averaging for improved accuracy
        /// </summary>
        private async System.Threading.Tasks.Task CaptureDualModeWithStream(CalibrationPointViewModel point)
        {
            try
            {
                // Get calibration settings
                var settings = SettingsManager.Instance.Settings;
                bool averagingEnabled = settings.CalibrationAveragingEnabled;
                
                // Step 1: Capture current mode ADC
                string modeName = _adcMode == 0 ? "Internal" : "ADS1115";
                
                ushort firstModeADC;
                CalibrationCaptureResult? firstResult = null;
                
                if (averagingEnabled)
                {
                    // Multi-sample averaging mode
                    int sampleCount = settings.CalibrationSampleCount;
                    int durationMs = settings.CalibrationCaptureDurationMs;
                    bool useMedian = settings.CalibrationUseMedian;
                    bool removeOutliers = settings.CalibrationRemoveOutliers;
                    double outlierThreshold = settings.CalibrationOutlierThreshold;
                    double maxStdDev = settings.CalibrationMaxStdDev;
                    
                    point.StatusText = $"Collecting {modeName} ADC samples...";
                    
                    firstResult = await CalibrationStatistics.CaptureAveragedADC(
                    sampleCount: sampleCount,
                    durationMs: durationMs,
                    getCurrentADC: () => _currentRawADC,
                    updateProgress: (current, total) =>
                    {
                        // Update status on UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            point.StatusText = $"Collecting {modeName} samples ({current}/{total})...";
                        });
                    },
                    useMedian: useMedian,
                    removeOutliers: removeOutliers,
                    outlierThreshold: outlierThreshold,
                        maxStdDev: maxStdDev
                    );
                    
                    firstModeADC = firstResult.AveragedValue;
                    
                    // Log first mode capture
                    _logger.LogInfo($"Captured {modeName} ADC: {firstModeADC} (mean={firstResult.Mean:F1}, median={firstResult.Median:F1}, σ={firstResult.StandardDeviation:F2}, n={firstResult.SampleCount}, outliers={firstResult.OutliersRemoved})", "CalibrationDialog");
                    
                    // Warn if unstable
                    if (!firstResult.IsStable)
                    {
                        _logger.LogWarning($"{modeName} ADC capture unstable: σ={firstResult.StandardDeviation:F2} > {settings.CalibrationMaxStdDev:F1}", "CalibrationDialog");
                    }
                }
                else
                {
                    // Single sample mode (old behavior, backward compatible)
                    point.StatusText = $"Capturing {modeName} ADC...";
                    await System.Threading.Tasks.Task.Delay(_calibrationDelayMs); // Wait for stable reading
                    firstModeADC = (ushort)_currentRawADC;
                    _logger.LogInfo($"Captured {modeName} ADC (single sample): {firstModeADC}", "CalibrationDialog");
                }
                
                // Store first mode ADC value
                if (_adcMode == 0)
                {
                    point.InternalADC = firstModeADC;
                }
                else
                {
                    point.ADS1115ADC = firstModeADC;
                }
                
                // Step 2: Switch to other ADC mode
                point.StatusText = $"Switching to {(_adcMode == 0 ? "ADS1115" : "Internal")} mode...";
                bool switchSuccess = false;
                
                if (_adcMode == 0)
                {
                    // Currently Internal, switch to ADS1115
                    switchSuccess = _canService?.SwitchToADS1115() ?? false;
                    if (switchSuccess)
                    {
                        _adcMode = 1;
                        await WaitForModeSwitch();
                    }
                }
                else
                {
                    // Currently ADS1115, switch to Internal
                    switchSuccess = _canService?.SwitchToInternalADC() ?? false;
                    if (switchSuccess)
                    {
                        _adcMode = 0;
                        await WaitForModeSwitch();
                    }
                }
                
                if (!switchSuccess)
                {
                    MessageBox.Show("Failed to switch ADC mode. Please switch manually and try again.", "Mode Switch Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    point.StatusText = "Mode switch failed";
                    return;
                }
                
                // Step 3: Capture second mode ADC
                modeName = _adcMode == 0 ? "Internal" : "ADS1115";
                
                ushort secondModeADC;
                CalibrationCaptureResult? secondResult = null;
                
                if (averagingEnabled)
                {
                    // Multi-sample averaging mode
                    int sampleCount = settings.CalibrationSampleCount;
                    int durationMs = settings.CalibrationCaptureDurationMs;
                    bool useMedian = settings.CalibrationUseMedian;
                    bool removeOutliers = settings.CalibrationRemoveOutliers;
                    double outlierThreshold = settings.CalibrationOutlierThreshold;
                    double maxStdDev = settings.CalibrationMaxStdDev;
                    
                    point.StatusText = $"Collecting {modeName} ADC samples...";
                    
                    secondResult = await CalibrationStatistics.CaptureAveragedADC(
                        sampleCount: sampleCount,
                        durationMs: durationMs,
                        getCurrentADC: () => _currentRawADC,
                        updateProgress: (current, total) =>
                        {
                            // Update status on UI thread
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                point.StatusText = $"Collecting {modeName} samples ({current}/{total})...";
                            });
                        },
                        useMedian: useMedian,
                        removeOutliers: removeOutliers,
                        outlierThreshold: outlierThreshold,
                        maxStdDev: maxStdDev
                    );
                    
                    secondModeADC = secondResult.AveragedValue;
                    
                    // Store statistics from second mode capture (for display)
                    point.CaptureMean = secondResult.Mean;
                    point.CaptureStdDev = secondResult.StandardDeviation;
                    point.CaptureSampleCount = secondResult.SampleCount;
                    
                    if (!secondResult.IsStable)
                    {
                        point.CaptureStabilityWarning = $"Unstable: σ={secondResult.StandardDeviation:F2} > {maxStdDev:F1}";
                        _logger.LogWarning($"{modeName} ADC capture unstable: σ={secondResult.StandardDeviation:F2} > {maxStdDev:F1}", "CalibrationDialog");
                        
                        // Show warning dialog
                        var warningResult = MessageBox.Show(
                            $"Warning: {modeName} ADC capture shows high variability (σ={secondResult.StandardDeviation:F2}).\n\n" +
                            "This may indicate:\n" +
                            "• Unstable weight on platform\n" +
                            "• Electrical noise\n" +
                            "• Mechanical vibration\n\n" +
                            "Continue with this calibration point?",
                            "Unstable Reading Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        
                        if (warningResult == MessageBoxResult.No)
                        {
                            point.StatusText = "Capture cancelled by user";
                            return;
                        }
                    }
                    else
                    {
                        point.CaptureStabilityWarning = "";
                    }
                    
                    // Log second mode capture
                    _logger.LogInfo($"Captured {modeName} ADC: {secondModeADC} (mean={secondResult.Mean:F1}, median={secondResult.Median:F1}, σ={secondResult.StandardDeviation:F2}, n={secondResult.SampleCount}, outliers={secondResult.OutliersRemoved})", "CalibrationDialog");
                }
                else
                {
                    // Single sample mode (old behavior, backward compatible)
                    point.StatusText = $"Capturing {modeName} ADC...";
                    await System.Threading.Tasks.Task.Delay(_calibrationDelayMs); // Wait for stable reading
                    secondModeADC = (ushort)_currentRawADC;
                    
                    // Clear statistics for single sample mode
                    point.CaptureMean = 0;
                    point.CaptureStdDev = 0;
                    point.CaptureSampleCount = 0;
                    point.CaptureStabilityWarning = "";
                    
                    _logger.LogInfo($"Captured {modeName} ADC (single sample): {secondModeADC}", "CalibrationDialog");
                }
                
                if (_adcMode == 0)
                {
                    point.InternalADC = secondModeADC;
                }
                else
                {
                    point.ADS1115ADC = secondModeADC;
                }
                
                // Step 4: Mark as captured
                point.RawADC = _adcMode == 0 ? point.InternalADC : point.ADS1115ADC; // For backward compatibility
                point.BothModesCaptured = true;
                point.IsCaptured = true;
                
                UpdateCapturedCount();
                
                // Auto-add next point if this is the last one
                if (point == Points.Last())
                {
                    AddNewPoint();
                }
                
                _logger.LogInfo($"Captured Point {point.PointNumber}: {point.KnownWeight:F0} kg @ Internal:{point.InternalADC} ADS1115:{point.ADS1115ADC}", "CalibrationDialog");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in dual-mode capture: {ex.Message}", "CalibrationDialog");
                throw;
            }
        }
        
        /// <summary>
        /// Manual entry mode - user enters ADC values for both modes
        /// </summary>
        private void CaptureDualModeManual(CalibrationPointViewModel point)
        {
            // Show dialog for manual ADC entry
            var manualDialog = new ManualADCEntryDialog(point.KnownWeight);
            if (manualDialog.ShowDialog() == true)
            {
                point.InternalADC = manualDialog.InternalADC;
                point.ADS1115ADC = manualDialog.ADS1115ADC;
                point.RawADC = point.InternalADC; // For backward compatibility
                point.BothModesCaptured = true;
                point.IsCaptured = true;
                
                UpdateCapturedCount();
                
                // Auto-add next point if this is the last one
                if (point == Points.Last())
                {
                    AddNewPoint();
                }
                
                _logger.LogInfo($"Manual entry Point {point.PointNumber}: {point.KnownWeight:F0} kg @ Internal:{point.InternalADC} ADS1115:{point.ADS1115ADC}", "CalibrationDialog");
            }
        }
        
        /// <summary>
        /// Wait for ADC mode switch to complete
        /// </summary>
        private async System.Threading.Tasks.Task WaitForModeSwitch()
        {
            // Wait for mode switch to stabilize
            await System.Threading.Tasks.Task.Delay(300);
            
            // Request system status to confirm mode switch
            _canService?.RequestSystemStatus();
            await System.Threading.Tasks.Task.Delay(200);
        }
        
        private void CalculateCalibration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Collect all captured points (must have both modes captured)
                var capturedPoints = Points.Where(p => p.BothModesCaptured).ToList();
                
                if (capturedPoints.Count == 0)
                {
                    MessageBox.Show("Please capture at least one calibration point with both ADC modes first.", "Incomplete Data", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Automatically detect zero point (weight = 0)
                var zeroPoints = capturedPoints.Where(p => Math.Abs(p.KnownWeight) < 0.01).ToList();
                string zeroInfo = "";
                if (zeroPoints.Any())
                {
                    var zeroPoint = zeroPoints.First();
                    zeroInfo = $"\nZero point detected: Weight=0 kg, Internal ADC={zeroPoint.InternalADC}, ADS1115 ADC={zeroPoint.ADS1115ADC}";
                    _logger.LogInfo($"Zero point detected: Internal ADC={zeroPoint.InternalADC}, ADS1115 ADC={zeroPoint.ADS1115ADC}", "CalibrationDialog");
                }
                else
                {
                    zeroInfo = "\nNo zero point (weight=0) found - calibration will use all points as-is";
                    _logger.LogWarning("No zero point detected in calibration points", "CalibrationDialog");
                }
                
                // Calculate Internal calibration using Internal ADC values
                // Zero point is automatically included in the calculation
                var internalPoints = capturedPoints.Select(p => p.ToCalibrationPointInternal()).ToList();
                _internalCalibration = LinearCalibration.FitMultiplePoints(internalPoints);
                _internalCalibration.ADCMode = 0;
                
                // Calculate ADS1115 calibration using ADS1115 ADC values
                // Zero point is automatically included in the calculation
                var ads1115Points = capturedPoints.Select(p => p.ToCalibrationPointADS1115()).ToList();
                _ads1115Calibration = LinearCalibration.FitMultiplePoints(ads1115Points);
                _ads1115Calibration.ADCMode = 1;
                
                // Build piecewise segments for both calibrations (if mode is Piecewise, segments will be used)
                // Segments are always built from points, but only used if Mode == Piecewise
                _internalCalibration.BuildSegmentsFromPoints();
                _ads1115Calibration.BuildSegmentsFromPoints();
                
                // Set legacy _calibration for backward compatibility (use Internal)
                _calibration = _internalCalibration;
                
                // Display results for both calibrations
                string internalEq = _internalCalibration.GetEquationString();
                string ads1115Eq = _ads1115Calibration.GetEquationString();
                
                PopupEquationTxt.Text = $"Internal ADC (12-bit):\n{internalEq}\n\nADS1115 (16-bit):\n{ads1115Eq}";
                PopupEquationTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF495057"));
                
                // Display quality metrics for both
                string qualityText = $"Internal: R²={_internalCalibration.R2:F4}, Max Error={_internalCalibration.MaxErrorPercent:F2}%\n" +
                                   $"ADS1115: R²={_ads1115Calibration.R2:F4}, Max Error={_ads1115Calibration.MaxErrorPercent:F2}%\n" +
                                   $"Points Used: {capturedPoints.Count}{zeroInfo}";
                
                // Color based on worst R²
                double worstR2 = Math.Min(_internalCalibration.R2, _ads1115Calibration.R2);
                if (worstR2 >= 0.999)
                {
                    PopupErrorTxt.Text = $"✓ {qualityText}";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
                }
                else if (worstR2 >= 0.99)
                {
                    PopupErrorTxt.Text = $"✓ {qualityText}";
                    PopupErrorTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
                }
                else if (worstR2 >= 0.95)
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
                if (_internalCalibration == null || _ads1115Calibration == null)
                {
                    MessageBox.Show("No calibration to save. Please calculate calibration first.", "No Data", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Set calibration mode from settings before saving
                var settings = SettingsManager.Instance.Settings;
                string modeStr = settings.CalibrationMode ?? "Regression";
                Core.CalibrationMode mode = modeStr == "Piecewise" ? Core.CalibrationMode.Piecewise : Core.CalibrationMode.Regression;
                _internalCalibration.Mode = mode;
                _ads1115Calibration.Mode = mode;
                
                // Save both calibrations
                _internalCalibration.SaveToFile(Side, 0);  // Internal mode
                _ads1115Calibration.SaveToFile(Side, 1);   // ADS1115 mode
                
                string internalEq = _internalCalibration.GetEquationString();
                string ads1115Eq = _ads1115Calibration.GetEquationString();
                
                MessageBox.Show($"Calibration saved successfully for {Side} side (both modes).\n\n" +
                              $"Internal ADC: {internalEq}\n" +
                              $"ADS1115: {ads1115Eq}\n\n" +
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
                
                // Restore original ADC mode if we switched it
                if (_hasStream && _canService.IsConnected && _adcMode != _startingADCMode)
                {
                    try
                    {
                        if (_startingADCMode == 0)
                        {
                            _canService.SwitchToInternalADC();
                        }
                        else
                        {
                            _canService.SwitchToADS1115();
                        }
                        _logger.LogInfo($"Restored ADC mode to {(_startingADCMode == 0 ? "Internal" : "ADS1115")}", "CalibrationDialog");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error restoring ADC mode: {ex.Message}", "CalibrationDialog");
                    }
                }
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
