using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using SuspensionPCB_CAN_WPF.Services;
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Adapters;
using Microsoft.Win32;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class SuspensionGraphWindow : Window
    {
        // Data collection for graph series (single-side display)
        private readonly ObservableCollection<double> _values = new();

        // Event-driven data collection (thread-safe)
        private readonly ConcurrentQueue<double> _pendingWeights = new();
        private readonly ConcurrentQueue<DateTime> _pendingTimestamps = new();

        // Timers
        private DispatcherTimer? _uiTimer;      // UI update timer (20 Hz)

        // Services
        private readonly CANService? _canService;
        private readonly WeightProcessor? _weightProcessor;

        // Chart control
        private CartesianChart _suspensionChart = null!;

        // State
        private bool _isPaused = false;
        private bool _isLeftSide = true;  // Default to Left side
        private const int MAX_SAMPLES = 21000;    // Sliding window size (matching AVL LMSV1.0)
        private int _sampleCount = 0;
        
        // Y-axis auto-scaling
        private bool _yAxisAutoScale = true;  // Default to auto-scale
        private const double MIN_Y_RANGE = 50.0;  // Minimum Y-axis range to prevent zero-range issues
        private const double Y_PADDING_PERCENT = 0.1;  // 10% padding above and below
        private double _yAxisFixedMin = 0.0;
        private double _yAxisFixedMax = 1000.0;
        
        // X-axis mode (Samples vs Time)
        private bool _xAxisTimeBased = false;  // Default to sample-based
        private readonly List<DateTime> _timestamps = new();
        
        // Zoom/Pan state
        private double _originalXMin = 0;
        private double _originalXMax = MAX_SAMPLES;
        private double _originalYMin = 0;
        private double _originalYMax = 1000;

        // Data rate monitoring
        private int _dataPointsCollected = 0;
        private DateTime _lastRateCheck = DateTime.Now;
        
        // Connection status update throttling
        private DateTime _lastConnectionStatusUpdate = DateTime.MinValue;
        private const int CONNECTION_STATUS_UPDATE_INTERVAL_MS = 500;

        // Transmission rate tracking
        private byte _currentTransmissionRate = 0x03; // Default 1kHz
        private const byte RATE_1KHZ = 0x03; // 1kHz rate code
        
        // Simulator adapter reference (for direct pattern weight access)
        private SimulatorCanAdapter? _simulatorAdapter;

        // Min/Max tracking (like AVL LMSV1.0)
        private double _minWeight = double.MaxValue;
        private double _maxWeight = double.MinValue;
        private bool _hasMinMaxData = false;

        // Weight-based Y-axis scaling (like AVL LMSV1.0)
        private double? _initialWeight = null;  // Initial weight when test starts
        private bool _useWeightBasedYAxis = false;  // Toggle between weight-based and auto-scale
        private const double WEIGHT_BASED_Y_RANGE = 400.0;  // ±400 kg range (like AVL)

        // Efficiency calculation
        private double? _efficiency = null;  // Calculated efficiency percentage

        // Pass/Fail validation
        private string _testResult = "Not Tested";  // "Pass", "Fail", or "Not Tested"
        private double _efficiencyLimit = 85.0;  // Configurable efficiency limit (loaded from settings)
        private string _limitsString = "";  // Limit string for display (e.g., "≥85.0%")

        // Test data for saving
        private DateTime? _testStartTime = null;
        private DateTime? _testEndTime = null;

        public SuspensionGraphWindow(CANService? canService, WeightProcessor? weightProcessor, byte transmissionRate = 0x03)
        {
            InitializeComponent();
            _canService = canService;
            _weightProcessor = weightProcessor;
            _currentTransmissionRate = transmissionRate;
            
            // Get simulator adapter reference if available (for direct pattern access)
            _simulatorAdapter = _canService?.GetSimulatorAdapter();

            // Load efficiency limits from settings
            LoadEfficiencyLimits();

            InitializeGraph();
            InitializeTimers();
            UpdateConnectionStatus();
            UpdateStatusDisplays(); // Initialize status display for Left side

            // Subscribe to event-driven data collection
            if (_canService != null)
            {
                _canService.RawDataReceived += OnRawDataReceived;
                _canService.MessageReceived += CanService_MessageReceived;
            }
        }

        /// <summary>
        /// Load efficiency limits from settings
        /// </summary>
        private void LoadEfficiencyLimits()
        {
            try
            {
                var settings = Services.SettingsManager.Instance.Settings;
                // Use side-specific limit based on current side
                _efficiencyLimit = _isLeftSide ? settings.SuspensionEfficiencyLimitLeft : settings.SuspensionEfficiencyLimitRight;
                _limitsString = $"≥{_efficiencyLimit:F1}%";
                ProductionLogger.Instance.LogInfo($"Loaded efficiency limit: {_efficiencyLimit:F1}% for {(_isLeftSide ? "Left" : "Right")} side", "SuspensionGraph");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Failed to load efficiency limits: {ex.Message}", "SuspensionGraph");
                // Use default
                _efficiencyLimit = 85.0;
                _limitsString = "≥85.0%";
            }
        }

        /// <summary>
        /// Update transmission rate (called when rate changes)
        /// </summary>
        public void UpdateTransmissionRate(byte rate)
        {
            _currentTransmissionRate = rate;
            // Update status display to reflect new rate
            Dispatcher.BeginInvoke(() => UpdateConnectionStatus());
        }

        private void InitializeGraph()
        {
            // Create chart control programmatically
            _suspensionChart = new CartesianChart();
            chartContainer.Children.Add(_suspensionChart);

            // Configure single series (Left or Right, switchable)
            _suspensionChart.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _values,
                    Name = "Left Side",
                    Fill = null,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(39, 174, 96), 2), // Green
                    LineSmoothness = 0
                }
            };

            // X-Axis Configuration (Samples)
            _suspensionChart.XAxes = new Axis[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MaxLimit = MAX_SAMPLES,
                    Name = "Samples",
                    TextSize = 12
                }
            };

            // Y-Axis Configuration (Weight in kg)
            _suspensionChart.YAxes = new Axis[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MaxLimit = 1000, // Adjust based on actual weight range
                    Name = "Weight (kg)",
                    TextSize = 12
                }
            };
            
            // Zoom and pan are handled via button controls (ZoomInBtn, ZoomOutBtn, ResetViewBtn)
            // Chart zoom/pan can be enabled via ZoomMode property if needed, but we use manual controls
            
            // Configure legend
            _suspensionChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Top;
            
            // Store original axis limits for reset
            _originalXMin = 0;
            _originalXMax = MAX_SAMPLES;
            _originalYMin = 0;
            _originalYMax = 1000;
        }

        private void InitializeTimers()
        {
            // UI Update Timer (20 Hz - 50ms) - Batch processes pending data on UI thread
            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(50);
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();
        }

        /// <summary>
        /// Event-driven data collection handler (replaces polling loop)
        /// Collects data from CAN messages at 1kHz rate (1000 samples/sec)
        /// </summary>
        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            if (_isPaused) return;

            try
            {
                // Only collect data for the active side
                bool isLeftMessage = e.Side == 0;
                if ((isLeftMessage && !_isLeftSide) || (!isLeftMessage && _isLeftSide))
                    return; // Skip if not the active side

                // Get weight based on mode
                double weight = GetWeightForSide(e.Side);

                // Enqueue to thread-safe queue (event handler may be on different thread)
                _pendingWeights.Enqueue(weight);
                _pendingTimestamps.Enqueue(DateTime.Now);
                Interlocked.Increment(ref _dataPointsCollected);
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Event-driven data collection error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Get weight for a specific side (handles both simulator and real CAN)
        /// </summary>
        private double GetWeightForSide(byte side)
        {
            bool isSimulator = _canService?.IsSimulatorAdapter() ?? false;
            bool isLeft = side == 0;

            if (isSimulator && _simulatorAdapter != null)
            {
                // DIRECT PATH: Get pattern weights directly from simulator (bypasses WeightProcessor)
                return _simulatorAdapter.GetCurrentPatternWeight(isLeft);
            }
            else if (_weightProcessor != null)
            {
                // REAL CAN PATH: Get processed weights from WeightProcessor (with calibration/filtering/tare)
                return isLeft ? _weightProcessor.LatestLeft.TaredWeight : _weightProcessor.LatestRight.TaredWeight;
            }

            return 0.0;
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPaused)
                return;

            try
            {
                // Calculate data rate every second
                var now = DateTime.Now;
                if ((now - _lastRateCheck).TotalSeconds >= 1.0)
                {
                    DataRateText.Text = $"Rate: {_dataPointsCollected} pts/sec";
                    _dataPointsCollected = 0;
                    _lastRateCheck = now;
                }

                // Batch dequeue all pending data from ConcurrentQueue (thread-safe, no lock needed)
                var batch = new List<double>();
                var timestampBatch = new List<DateTime>();
                
                while (_pendingWeights.TryDequeue(out var weight))
                {
                    batch.Add(weight);
                }
                
                while (_pendingTimestamps.TryDequeue(out var timestamp))
                {
                    timestampBatch.Add(timestamp);
                }

                // Process batch data
                if (batch.Count > 0)
                {
                    ProcessBatchData(_values, batch.ToArray());
                    
                    // Store timestamps if time-based mode
                    if (_xAxisTimeBased && timestampBatch.Count == batch.Count)
                    {
                        foreach (var ts in timestampBatch)
                        {
                            _timestamps.Add(ts);
                        }
                        // Maintain sliding window
                        while (_timestamps.Count > MAX_SAMPLES)
                            _timestamps.RemoveAt(0);
                    }
                }

                // Recalculate efficiency if we have initial weight and min/max data
                if (_initialWeight.HasValue && _hasMinMaxData)
                {
                    CalculateEfficiency();
                }

                // Update status displays
                UpdateStatusDisplays();

                // Auto-scroll X-axis only when count changes
                int currentCount = _values.Count;
                if (currentCount != _sampleCount)
                {
                    var xAxis = _suspensionChart.XAxes.First();
                    xAxis.MinLimit = Math.Max(0, currentCount - MAX_SAMPLES);
                    xAxis.MaxLimit = currentCount;
                    _sampleCount = currentCount;
                }
                
                // Update Y-axis based on mode
                if (_yAxisAutoScale || _useWeightBasedYAxis)
                {
                    UpdateYAxisAutoScale();
                }
                else
                {
                    // Use fixed range
                    var yAxis = _suspensionChart.YAxes.First();
                    yAxis.MinLimit = _yAxisFixedMin;
                    yAxis.MaxLimit = _yAxisFixedMax;
                }
                
                // Update X-axis based on mode
                UpdateXAxisMode();
                
                // Update Y-axis range display
                UpdateYAxisRangeDisplay();
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"UI update error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Efficiently process batch data for ObservableCollection
        /// Optimized to reduce UI notifications by batching operations
        /// Also tracks Min/Max values for efficiency calculation (like AVL LMSV1.0)
        /// </summary>
        private void ProcessBatchData(ObservableCollection<double> collection, double[] newData)
        {
            if (newData.Length == 0) return;

            int addCount = newData.Length;
            int currentCount = collection.Count;
            int totalAfterAdd = currentCount + addCount;
            int removeCount = Math.Max(0, totalAfterAdd - MAX_SAMPLES);

            // Optimize: If we need to remove items, do it in one batch operation
            // by clearing and rebuilding, or use more efficient approach
            if (removeCount > 0)
            {
                // Remove excess items from beginning
                for (int i = 0; i < removeCount; i++)
                {
                    collection.RemoveAt(0);
                }
            }

            // Add all new data items and track Min/Max
            // Note: ObservableCollection doesn't support AddRange, so we add individually
            // but we've already removed excess items, so this is more efficient
            for (int i = 0; i < addCount; i++)
            {
                double weight = newData[i];
                collection.Add(weight);

                // Track Min/Max for efficiency calculation (like AVL LMSV1.0)
                if (!double.IsNaN(weight) && !double.IsInfinity(weight))
                {
                    if (weight < _minWeight)
                        _minWeight = weight;
                    if (weight > _maxWeight)
                        _maxWeight = weight;
                    _hasMinMaxData = true;
                }
            }
        }

        private void UpdateStatusDisplays()
        {
            try
            {
                // Update current weight display for active side only
                if (_weightProcessor != null)
                {
                    double weight = _isLeftSide 
                        ? _weightProcessor.LatestLeft.TaredWeight 
                        : _weightProcessor.LatestRight.TaredWeight;

                    // Update active side display
                    if (_isLeftSide)
                    {
                        if (ActiveSideLabel != null) ActiveSideLabel.Text = "Left Side:";
                        if (ActiveSideIndicator != null) ActiveSideIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96)); // Green
                        LeftWeightText.Text = $"{weight:F1} kg";
                        RightWeightText.Text = "-- kg";
                    }
                    else
                    {
                        if (ActiveSideLabel != null) ActiveSideLabel.Text = "Right Side:";
                        if (ActiveSideIndicator != null) ActiveSideIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)); // Blue
                        LeftWeightText.Text = "-- kg";
                        RightWeightText.Text = $"{weight:F1} kg";
                    }
                }

                // Show actual data count
                SampleCountText.Text = _values.Count.ToString();

                // Update Min/Max display (if available)
                if (MinWeightText != null && MaxWeightText != null)
                {
                    if (_hasMinMaxData)
                    {
                        MinWeightText.Text = $"Min: {_minWeight:F1} kg";
                        MaxWeightText.Text = $"Max: {_maxWeight:F1} kg";
                    }
                    else
                    {
                        MinWeightText.Text = "Min: --";
                        MaxWeightText.Text = "Max: --";
                    }
                }

                // Update Efficiency and Pass/Fail display (if calculated)
                if (EfficiencyText != null)
                {
                    if (_efficiency.HasValue)
                    {
                        // Show efficiency with Pass/Fail status and limit
                        string resultColor = _testResult == "Pass" ? "Green" : (_testResult == "Fail" ? "Red" : "Gray");
                        EfficiencyText.Text = $"Efficiency: {_efficiency.Value:F1}% (Limit: {_limitsString}, Result: {_testResult})";
                        EfficiencyText.Foreground = new System.Windows.Media.SolidColorBrush(
                            _testResult == "Pass" ? System.Windows.Media.Color.FromRgb(39, 174, 96) : // Green
                            _testResult == "Fail" ? System.Windows.Media.Color.FromRgb(220, 53, 69) : // Red
                            System.Windows.Media.Color.FromRgb(108, 117, 125)); // Gray
                    }
                    else
                    {
                        EfficiencyText.Text = "Efficiency: --";
                        EfficiencyText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125)); // Gray
                    }
                }

                // Update Pass/Fail indicator (if available)
                if (TestResultText != null)
                {
                    if (_testResult != "Not Tested")
                    {
                        TestResultText.Text = $"Result: {_testResult}";
                        TestResultText.Foreground = new System.Windows.Media.SolidColorBrush(
                            _testResult == "Pass" ? System.Windows.Media.Color.FromRgb(39, 174, 96) : // Green
                            System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                    }
                    else
                    {
                        TestResultText.Text = "Result: Not Tested";
                        TestResultText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125)); // Gray
                    }
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Status update error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Update Y-axis limits based on visible data with auto-scaling
        /// Supports both weight-based scaling (like AVL LMSV1.0) and general auto-scaling
        /// </summary>
        private void UpdateYAxisAutoScale()
        {
            try
            {
                var yAxis = _suspensionChart.YAxes.First();

                // Weight-based Y-axis scaling (like AVL LMSV1.0: center around initial weight ±400 kg)
                if (_useWeightBasedYAxis && _initialWeight.HasValue)
                {
                    double centerWeight = _initialWeight.Value;
                    yAxis.MinLimit = Math.Max(0, centerWeight - WEIGHT_BASED_Y_RANGE);
                    yAxis.MaxLimit = centerWeight + WEIGHT_BASED_Y_RANGE;
                    return;
                }

                // General auto-scaling mode
                if (_values.Count == 0)
                {
                    // No data - use default range
                    yAxis.MinLimit = 0.0;
                    yAxis.MaxLimit = 1000.0;
                    return;
                }

                // Get visible data (last MAX_SAMPLES values)
                var visibleValues = _values.Skip(Math.Max(0, _values.Count - MAX_SAMPLES));
                var allValues = visibleValues.Where(v => !double.IsNaN(v) && !double.IsInfinity(v));

                if (!allValues.Any())
                {
                    // No valid data - use default range
                    yAxis.MinLimit = 0.0;
                    yAxis.MaxLimit = 1000.0;
                    return;
                }

                double minWeight = allValues.Min();
                double maxWeight = allValues.Max();
                double range = maxWeight - minWeight;

                // Ensure minimum range to prevent zero-range issues
                if (range < MIN_Y_RANGE)
                {
                    double center = (minWeight + maxWeight) / 2.0;
                    minWeight = center - MIN_Y_RANGE / 2.0;
                    maxWeight = center + MIN_Y_RANGE / 2.0;
                    range = MIN_Y_RANGE;
                }

                // Add 10% padding above and below
                double padding = range * Y_PADDING_PERCENT;
                minWeight = Math.Max(0, minWeight - padding);  // Don't go below 0
                maxWeight = maxWeight + padding;

                // Update Y-axis
                yAxis.MinLimit = minWeight;
                yAxis.MaxLimit = maxWeight;
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Y-axis auto-scale error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Update Y-axis range display in status panel
        /// </summary>
        private void UpdateYAxisRangeDisplay()
        {
            try
            {
                if (YAxisRangeText != null)
                {
                    var yAxis = _suspensionChart.YAxes.First();
                    double minLimit = yAxis.MinLimit ?? 0.0;
                    double maxLimit = yAxis.MaxLimit ?? 1000.0;
                    YAxisRangeText.Text = $"Y: {minLimit:F1}-{maxLimit:F1} kg";
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Y-axis range display error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Update X-axis based on current mode (Samples or Time)
        /// </summary>
        private void UpdateXAxisMode()
        {
            try
            {
                var xAxis = _suspensionChart.XAxes.First();
                
                if (_xAxisTimeBased)
                {
                    // Time-based X-axis
                    if (_timestamps.Count > 0)
                    {
                        var allTimestamps = _timestamps.Where(t => t != default);
                        if (allTimestamps.Any())
                        {
                            var minTime = allTimestamps.Min();
                            var maxTime = allTimestamps.Max();
                            var timeSpan = maxTime - minTime;
                            
                            // Add small padding
                            xAxis.MinLimit = minTime.AddSeconds(-timeSpan.TotalSeconds * 0.05).Ticks;
                            xAxis.MaxLimit = maxTime.AddSeconds(timeSpan.TotalSeconds * 0.05).Ticks;
                        }
                    }
                    xAxis.Name = "Time";
                }
                else
                {
                    // Sample-based X-axis
                    int currentCount = _values.Count;
                    xAxis.MinLimit = Math.Max(0, currentCount - MAX_SAMPLES);
                    xAxis.MaxLimit = currentCount;
                    xAxis.Name = "Samples";
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"X-axis mode update error: {ex.Message}", "SuspensionGraph");
            }
        }

        private void UpdateConnectionStatus()
        {
            try
            {
                bool isConnected = _canService?.IsConnected ?? false;
                bool isSimulator = _canService?.IsSimulatorAdapter() ?? false;
                bool is1kHz = _currentTransmissionRate == RATE_1KHZ;
                
                if (ConnectionIndicator != null)
                {
                    ConnectionIndicator.Fill = isConnected 
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96)) // Green
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                }

                if (ConnectionStatusText != null)
                {
                    if (!isConnected)
                    {
                        ConnectionStatusText.Text = "Disconnected";
                    }
                    else if (isSimulator)
                    {
                        ConnectionStatusText.Text = "Connected (Simulator - Event-driven graphs active)";
                    }
                    else if (!is1kHz)
                    {
                        ConnectionStatusText.Text = $"Connected ({GetRateText(_currentTransmissionRate)} - Graphs disabled, 1kHz required)";
                    }
                    else
                    {
                        ConnectionStatusText.Text = "Connected (1kHz - Event-driven graphs active)";
                    }
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Connection status update error: {ex.Message}", "SuspensionGraph");
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
                _ => "Unknown"
            };
        }

        private void CanService_MessageReceived(CANMessage message)
        {
            // Throttle connection status updates to prevent excessive UI updates
            var now = DateTime.Now;
            if ((now - _lastConnectionStatusUpdate).TotalMilliseconds >= CONNECTION_STATUS_UPDATE_INTERVAL_MS)
            {
                _lastConnectionStatusUpdate = now;
                Dispatcher.BeginInvoke(() => UpdateConnectionStatus());
            }
        }

        private void StartTestBtn_Click(object sender, RoutedEventArgs e)
        {
            StartTest();
            StartTestBtn.IsEnabled = false;
            StopTestBtn.IsEnabled = true;
        }

        private void StopTestBtn_Click(object sender, RoutedEventArgs e)
        {
            StopTest();
            StartTestBtn.IsEnabled = true;
            StopTestBtn.IsEnabled = false;
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = true;
            PauseBtn.IsEnabled = false;
            ResumeBtn.IsEnabled = true;
            ProductionLogger.Instance.LogInfo("Graph paused", "SuspensionGraph");
        }

        private void ResumeBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = false;
            PauseBtn.IsEnabled = true;
            ResumeBtn.IsEnabled = false;
            ProductionLogger.Instance.LogInfo("Graph resumed", "SuspensionGraph");
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear all collections (no lock needed with ConcurrentQueue)
                _values.Clear();
                while (_pendingWeights.TryDequeue(out _)) { }
                while (_pendingTimestamps.TryDequeue(out _)) { }
                _timestamps.Clear();
                _sampleCount = 0;

                // Reset Min/Max tracking
                _minWeight = double.MaxValue;
                _maxWeight = double.MinValue;
                _hasMinMaxData = false;
                _efficiency = null;
                _testResult = "Not Tested";
                _initialWeight = null;

                // Reset X-axis
                var xAxis = _suspensionChart.XAxes.First();
                xAxis.MinLimit = 0;
                xAxis.MaxLimit = MAX_SAMPLES;
                
                // Reset Y-axis
                var yAxis = _suspensionChart.YAxes.First();
                yAxis.MinLimit = _originalYMin;
                yAxis.MaxLimit = _originalYMax;

                SampleCountText.Text = "0";
                UpdateStatusDisplays(); // Update Min/Max/Efficiency displays
                ProductionLogger.Instance.LogInfo("Graph cleared", "SuspensionGraph");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Clear error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Switch between Left and Right side display
        /// </summary>
        private void SwitchSide_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Toggle side
                _isLeftSide = !_isLeftSide;
                
                // Reload efficiency limit for new side
                LoadEfficiencyLimits();
                
                // Clear current data
                _values.Clear();
                while (_pendingWeights.TryDequeue(out _)) { }
                while (_pendingTimestamps.TryDequeue(out _)) { }
                _timestamps.Clear();
                _sampleCount = 0;
                
                // Reset test state for new side
                _minWeight = double.MaxValue;
                _maxWeight = double.MinValue;
                _hasMinMaxData = false;
                _efficiency = null;
                _testResult = "Not Tested";
                _initialWeight = null;

                // Update series name and color
                var series = _suspensionChart.Series.First() as LineSeries<double>;
                if (series != null)
                {
                    series.Name = _isLeftSide ? "Left Side" : "Right Side";
                    series.Stroke = new SolidColorPaint(
                        _isLeftSide 
                            ? new SKColor(39, 174, 96)  // Green for Left
                            : new SKColor(52, 152, 219), // Blue for Right
                        2);
                }

                // Update UI labels
                if (ActiveSideLabel != null)
                    ActiveSideLabel.Text = _isLeftSide ? "Left Side:" : "Right Side:";
                
                if (ActiveSideIndicator != null)
                    ActiveSideIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                        _isLeftSide 
                            ? System.Windows.Media.Color.FromRgb(39, 174, 96)  // Green
                            : System.Windows.Media.Color.FromRgb(52, 152, 219)); // Blue

                // Reset X-axis
                var xAxis = _suspensionChart.XAxes.First();
                xAxis.MinLimit = 0;
                xAxis.MaxLimit = MAX_SAMPLES;

                // Update status displays
                UpdateStatusDisplays();
                
                ProductionLogger.Instance.LogInfo($"Switched to {(_isLeftSide ? "Left" : "Right")} side", "SuspensionGraph");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Switch side error: {ex.Message}", "SuspensionGraph");
            }
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var exportBtn = sender as System.Windows.Controls.Button;
            if (exportBtn == null) return;

            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = $"SuspensionGraph_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    // Disable button during export
                    exportBtn.IsEnabled = false;
                    exportBtn.Content = "💾 Exporting...";

                    try
                    {
                        await ExportToCSVAsync(saveDialog.FileName);
                        MessageBox.Show("Graph data exported successfully!", "Export Complete", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    finally
                    {
                        // Re-enable button
                        exportBtn.IsEnabled = true;
                        exportBtn.Content = "💾 Export CSV";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export error: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ProductionLogger.Instance.LogError($"Export error: {ex.Message}", "SuspensionGraph");
                
                // Re-enable button on error
                if (exportBtn != null)
                {
                    exportBtn.IsEnabled = true;
                    exportBtn.Content = "💾 Export CSV";
                }
            }
        }

        private async Task ExportToCSVAsync(string filePath)
        {
            // Run file writing on background thread to avoid blocking UI
            await Task.Run(() =>
            {
                try
                {
                    using var writer = new System.IO.StreamWriter(filePath);
                    
                    // Write header with active side name
                    string sideName = _isLeftSide ? "Left Side" : "Right Side";
                    writer.WriteLine($"Sample,{sideName} (kg)");

                    // Copy data to array (ObservableCollection is accessed on UI thread, safe to copy)
                    double[] data = _values.ToArray();

                    // Write data
                    for (int i = 0; i < data.Length; i++)
                    {
                        writer.WriteLine($"{i},{data[i]:F2}");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to write CSV file: {ex.Message}", ex);
                }
            });
        }

        // Event handlers for new controls
        private void YAxisModeToggle_Click(object sender, RoutedEventArgs e)
        {
            // Disable weight-based mode when toggling auto/fixed
            if (_useWeightBasedYAxis)
            {
                _useWeightBasedYAxis = false;
                if (WeightBasedYAxisToggle != null)
                {
                    WeightBasedYAxisToggle.Content = "Y: Weight-Based";
                    WeightBasedYAxisToggle.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7));
                }
            }

            _yAxisAutoScale = !_yAxisAutoScale;
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null)
            {
                btn.Content = _yAxisAutoScale ? "Y: Auto" : "Y: Fixed";
                btn.Background = _yAxisAutoScale 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96))  // Green
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125));  // Gray
            }
            ProductionLogger.Instance.LogInfo($"Y-axis mode: {(_yAxisAutoScale ? "Auto" : "Fixed")}", "SuspensionGraph");
        }

        private void XAxisModeToggle_Click(object sender, RoutedEventArgs e)
        {
            _xAxisTimeBased = !_xAxisTimeBased;
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null)
            {
                btn.Content = _xAxisTimeBased ? "X: Time" : "X: Samples";
                btn.Background = _xAxisTimeBased 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96))  // Green
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125));  // Gray
            }
            ProductionLogger.Instance.LogInfo($"X-axis mode: {(_xAxisTimeBased ? "Time" : "Samples")}", "SuspensionGraph");
        }

        private void LegendToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suspensionChart.LegendPosition == LiveChartsCore.Measure.LegendPosition.Hidden)
            {
                _suspensionChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Top;
            }
            else
            {
                _suspensionChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
            }
            
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null)
            {
                btn.Content = _suspensionChart.LegendPosition != LiveChartsCore.Measure.LegendPosition.Hidden 
                    ? "Legend: On" : "Legend: Off";
            }
        }

        private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var xAxis = _suspensionChart.XAxes.First();
                var yAxis = _suspensionChart.YAxes.First();
                
                double xMin = xAxis.MinLimit ?? 0;
                double xMax = xAxis.MaxLimit ?? MAX_SAMPLES;
                double yMin = yAxis.MinLimit ?? 0;
                double yMax = yAxis.MaxLimit ?? 1000;
                
                double xRange = xMax - xMin;
                double yRange = yMax - yMin;
                
                xAxis.MinLimit = xMin + xRange * 0.1;
                xAxis.MaxLimit = xMax - xRange * 0.1;
                yAxis.MinLimit = yMin + yRange * 0.1;
                yAxis.MaxLimit = yMax - yRange * 0.1;
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Zoom in error: {ex.Message}", "SuspensionGraph");
            }
        }

        private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var xAxis = _suspensionChart.XAxes.First();
                var yAxis = _suspensionChart.YAxes.First();
                
                double xMin = xAxis.MinLimit ?? 0;
                double xMax = xAxis.MaxLimit ?? MAX_SAMPLES;
                double yMin = yAxis.MinLimit ?? 0;
                double yMax = yAxis.MaxLimit ?? 1000;
                
                double xRange = xMax - xMin;
                double yRange = yMax - yMin;
                
                xAxis.MinLimit = xMin - xRange * 0.1;
                xAxis.MaxLimit = xMax + xRange * 0.1;
                yAxis.MinLimit = Math.Max(0, yMin - yRange * 0.1);
                yAxis.MaxLimit = yMax + yRange * 0.1;
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Zoom out error: {ex.Message}", "SuspensionGraph");
            }
        }

        private void ResetViewBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var xAxis = _suspensionChart.XAxes.First();
                var yAxis = _suspensionChart.YAxes.First();
                
                // Reset to original limits
                xAxis.MinLimit = _originalXMin;
                xAxis.MaxLimit = _originalXMax;
                yAxis.MinLimit = _originalYMin;
                yAxis.MaxLimit = _originalYMax;
                
                ProductionLogger.Instance.LogInfo("View reset to original", "SuspensionGraph");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Reset view error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Start test tracking - captures initial weight for efficiency calculation and weight-based Y-axis
        /// </summary>
        public void StartTest()
        {
            try
            {
                // Capture initial weight for weight-based Y-axis and efficiency calculation
                if (_weightProcessor != null)
                {
                    double currentWeight = _isLeftSide 
                        ? _weightProcessor.LatestLeft.TaredWeight 
                        : _weightProcessor.LatestRight.TaredWeight;
                    
                    if (currentWeight > 0)
                    {
                        _initialWeight = currentWeight;
                        _testStartTime = DateTime.Now;
                        
                        // Reset Min/Max for new test
                        _minWeight = double.MaxValue;
                        _maxWeight = double.MinValue;
                        _hasMinMaxData = false;
                        _efficiency = null;
                        _testResult = "Not Tested";
                        
                        // Reload efficiency limit for current side
                        LoadEfficiencyLimits();
                        
                        ProductionLogger.Instance.LogInfo($"Test started - Initial weight: {_initialWeight:F1} kg ({(_isLeftSide ? "Left" : "Right")} side), Limit: {_efficiencyLimit:F1}%", "SuspensionGraph");
                    }
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Start test error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Stop test tracking - calculates efficiency and Pass/Fail (like AVL LMSV1.0)
        /// Efficiency = (Min Weight / Initial Weight) × 100%
        /// Pass/Fail = Efficiency >= EfficiencyLimit
        /// </summary>
        public void StopTest()
        {
            try
            {
                _testEndTime = DateTime.Now;
                
                // Reload efficiency limit in case it changed or side switched
                LoadEfficiencyLimits();
                
                // Calculate efficiency if we have initial weight and min/max data
                if (_initialWeight.HasValue && _initialWeight.Value > 0 && _hasMinMaxData)
                {
                    // Efficiency = (Min Weight / Initial Weight) × 100% (like AVL LMSV1.0)
                    double efficiencyPercent = Math.Abs((_minWeight / _initialWeight.Value) * 100.0);
                    _efficiency = efficiencyPercent;
                    
                    // Calculate Pass/Fail based on efficiency limit
                    CalculateTestResult();
                    
                    ProductionLogger.Instance.LogInfo($"Test stopped - Efficiency: {_efficiency:F1}% (Limit: {_efficiencyLimit:F1}%), Result: {_testResult}", "SuspensionGraph");
                }
                else
                {
                    ProductionLogger.Instance.LogWarning("Cannot calculate efficiency - missing initial weight or min/max data", "SuspensionGraph");
                    _testResult = "Not Tested";
                }
                
                UpdateStatusDisplays(); // Update efficiency and Pass/Fail display
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Stop test error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Calculate Pass/Fail result based on efficiency and limit (like AVL LMSV1.0)
        /// </summary>
        private void CalculateTestResult()
        {
            try
            {
                if (_efficiency.HasValue && _efficiencyLimit > 0)
                {
                    if (_efficiency.Value >= _efficiencyLimit)
                    {
                        _testResult = "Pass";
                    }
                    else
                    {
                        _testResult = "Fail";
                    }
                }
                else
                {
                    _testResult = "Not Tested";
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Pass/Fail calculation error: {ex.Message}", "SuspensionGraph");
                _testResult = "Not Tested";
            }
        }

        /// <summary>
        /// Calculate efficiency from current data (can be called anytime)
        /// Also calculates Pass/Fail if efficiency limit is set
        /// </summary>
        private void CalculateEfficiency()
        {
            try
            {
                if (_initialWeight.HasValue && _initialWeight.Value > 0 && _hasMinMaxData)
                {
                    double efficiencyPercent = Math.Abs((_minWeight / _initialWeight.Value) * 100.0);
                    _efficiency = efficiencyPercent;
                    
                    // Also calculate Pass/Fail if limit is set
                    if (_efficiencyLimit > 0)
                    {
                        CalculateTestResult();
                    }
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Efficiency calculation error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Toggle weight-based Y-axis scaling (like AVL LMSV1.0: center around initial weight ±400 kg)
        /// </summary>
        private void WeightBasedYAxisToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _useWeightBasedYAxis = !_useWeightBasedYAxis;
                
                // Disable regular auto-scale when weight-based is enabled
                if (_useWeightBasedYAxis)
                {
                    _yAxisAutoScale = false;
                    if (YAxisModeToggle != null)
                    {
                        YAxisModeToggle.Content = "Y: Fixed";
                        YAxisModeToggle.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125));
                    }
                }
                
                var btn = sender as System.Windows.Controls.Button;
                if (btn != null)
                {
                    btn.Content = _useWeightBasedYAxis ? "Y: Weight-Based ✓" : "Y: Weight-Based";
                    btn.Background = _useWeightBasedYAxis 
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7))  // Yellow/Orange
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125));  // Gray
                }
                
                if (_useWeightBasedYAxis && !_initialWeight.HasValue)
                {
                    // Auto-capture initial weight if not set
                    StartTest();
                }
                
                ProductionLogger.Instance.LogInfo($"Weight-based Y-axis: {(_useWeightBasedYAxis ? "Enabled" : "Disabled")}", "SuspensionGraph");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Weight-based Y-axis toggle error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Save test data to JSON file (database-like functionality)
        /// </summary>
        private async void SaveTestDataBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create test data object
                var testData = new SuspensionTestDataModel
                {
                    TestId = Guid.NewGuid().ToString(),
                    TestStartTime = _testStartTime ?? DateTime.Now,
                    TestEndTime = _testEndTime ?? DateTime.Now,
                    Side = _isLeftSide ? "Left" : "Right",
                    InitialWeight = _initialWeight ?? 0.0,
                    MinWeight = _hasMinMaxData ? _minWeight : 0.0,
                    MaxWeight = _hasMinMaxData ? _maxWeight : 0.0,
                    Efficiency = _efficiency ?? 0.0,
                    EfficiencyLimit = _efficiencyLimit,
                    TestResult = _testResult,
                    Limits = _limitsString,
                    SampleCount = _values.Count,
                    TransmissionRate = GetRateText(_currentTransmissionRate),
                    DataPoints = _values.ToArray()
                };

                // Show save dialog
                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"SuspensionTest_{testData.Side}_{testData.TestStartTime:yyyyMMdd_HHmmss}.json",
                    DefaultExt = "json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await SaveTestDataToFile(testData, saveDialog.FileName);
                    string resultMessage = _testResult != "Not Tested" 
                        ? $"Test data saved successfully!\n\nSide: {testData.Side}\nEfficiency: {testData.Efficiency:F1}%\nLimit: {testData.Limits}\nResult: {testData.TestResult}\nSamples: {testData.SampleCount}"
                        : $"Test data saved successfully!\n\nSide: {testData.Side}\nEfficiency: {testData.Efficiency:F1}%\nSamples: {testData.SampleCount}";
                    MessageBox.Show(resultMessage, 
                        "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Save test data error: {ex.Message}", "SuspensionGraph");
                MessageBox.Show($"Failed to save test data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Save test data to JSON file
        /// </summary>
        private async Task SaveTestDataToFile(SuspensionTestDataModel testData, string filePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Create a serializable version (without full data array for file size)
                    var serializableData = new
                    {
                        TestId = testData.TestId,
                        TestStartTime = testData.TestStartTime,
                        TestEndTime = testData.TestEndTime,
                        Side = testData.Side,
                        InitialWeight = testData.InitialWeight,
                        MinWeight = testData.MinWeight,
                        MaxWeight = testData.MaxWeight,
                        Efficiency = testData.Efficiency,
                        EfficiencyLimit = testData.EfficiencyLimit,
                        TestResult = testData.TestResult,
                        Limits = testData.Limits,
                        SampleCount = testData.SampleCount,
                        TransmissionRate = testData.TransmissionRate,
                        DataPointCount = testData.DataPoints?.Length ?? 0,
                        // Optionally include first/last few data points for reference
                        FirstDataPoints = testData.DataPoints?.Take(10).ToArray() ?? Array.Empty<double>(),
                        LastDataPoints = testData.DataPoints?.TakeLast(10).ToArray() ?? Array.Empty<double>()
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(serializableData, new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    
                    System.IO.File.WriteAllText(filePath, json);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to write JSON file: {ex.Message}", ex);
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Stop UI timer
                _uiTimer?.Stop();

                // Unsubscribe from events
                if (_canService != null)
                {
                    _canService.RawDataReceived -= OnRawDataReceived;
                    _canService.MessageReceived -= CanService_MessageReceived;
                }

                // Clear queues
                while (_pendingWeights.TryDequeue(out _)) { }
                while (_pendingTimestamps.TryDequeue(out _)) { }

                ProductionLogger.Instance.LogInfo("SuspensionGraphWindow closed", "SuspensionGraph");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Window close error: {ex.Message}", "SuspensionGraph");
            }

            base.OnClosed(e);
        }
    }
}

