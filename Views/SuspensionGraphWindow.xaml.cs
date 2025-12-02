using System;
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
        // Data collections for graph series
        private readonly ObservableCollection<double> _leftValues = new();
        private readonly ObservableCollection<double> _rightValues = new();

        // Batch processing collections (non-UI thread safe)
        private readonly List<double> _pendingLeft = new();
        private readonly List<double> _pendingRight = new();
        private readonly List<DateTime> _pendingLeftTimestamps = new();
        private readonly List<DateTime> _pendingRightTimestamps = new();
        private readonly object _lock = new object();

        // Timers
        private DispatcherTimer? _uiTimer;      // UI update timer (20 Hz)
        
        // Background data collection
        private Task? _dataCollectionTask;       // Background thread for data polling
        private CancellationTokenSource? _dataCollectionCts;

        // Services
        private readonly CANService? _canService;
        private readonly WeightProcessor? _weightProcessor;

        // Chart control
        private CartesianChart _suspensionChart = null!;

        // State
        private bool _isPaused = false;
        private const int MAX_SAMPLES = 4000;    // Sliding window size
        private int _sampleCount = 0;
        
        // Y-axis auto-scaling
        private bool _yAxisAutoScale = true;  // Default to auto-scale
        private const double MIN_Y_RANGE = 50.0;  // Minimum Y-axis range to prevent zero-range issues
        private const double Y_PADDING_PERCENT = 0.1;  // 10% padding above and below
        private double _yAxisFixedMin = 0.0;
        private double _yAxisFixedMax = 1000.0;
        
        // X-axis mode (Samples vs Time)
        private bool _xAxisTimeBased = false;  // Default to sample-based
        private readonly List<DateTime> _leftTimestamps = new();
        private readonly List<DateTime> _rightTimestamps = new();
        
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
        private const int PAUSED_POLL_INTERVAL_MS = 100;

        // Transmission rate tracking
        private byte _currentTransmissionRate = 0x03; // Default 1kHz
        private const byte RATE_1KHZ = 0x03; // 1kHz rate code
        
        // Simulator adapter reference (for direct pattern weight access)
        private SimulatorCanAdapter? _simulatorAdapter;

        public SuspensionGraphWindow(CANService? canService, WeightProcessor? weightProcessor, byte transmissionRate = 0x03)
        {
            InitializeComponent();
            _canService = canService;
            _weightProcessor = weightProcessor;
            _currentTransmissionRate = transmissionRate;
            
            // Get simulator adapter reference if available (for direct pattern access)
            _simulatorAdapter = _canService?.GetSimulatorAdapter();

            InitializeGraph();
            InitializeTimers();
            UpdateConnectionStatus();

            // Subscribe to CAN service if available
            if (_canService != null)
            {
                _canService.MessageReceived += CanService_MessageReceived;
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

            // Configure two series: Left Side (Green) and Right Side (Blue)
            _suspensionChart.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _leftValues,
                    Name = "Left Side",
                    Fill = null,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(39, 174, 96), 2), // Green
                    LineSmoothness = 0
                },
                new LineSeries<double>
                {
                    Values = _rightValues,
                    Name = "Right Side",
                    Fill = null,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(52, 152, 219), 2), // Blue
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

            // Start background data collection thread (100 Hz - 10ms polling)
            // This runs on a background thread to avoid blocking the UI
            _dataCollectionCts = new CancellationTokenSource();
            _dataCollectionTask = Task.Run(async () => await DataCollectionLoop(_dataCollectionCts.Token));
        }

        /// <summary>
        /// Background thread loop for data collection - does NOT block UI thread
        /// Collects data when:
        /// - Simulator mode: Direct pattern weights (bypasses WeightProcessor) - Always (any rate)
        /// - Real CAN: From WeightProcessor - Only at 1kHz transmission rate
        /// </summary>
        private async Task DataCollectionLoop(CancellationToken cancellationToken)
        {
            const int POLL_INTERVAL_MS = 10; // 100 Hz polling
            
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check conditions: Not paused
                bool isSimulator = _canService?.IsSimulatorAdapter() ?? false;
                bool isRealCan1kHz = !isSimulator && _currentTransmissionRate == RATE_1KHZ;
                
                // Collect data if: Simulator (any rate, direct pattern) OR Real CAN at 1kHz (via WeightProcessor)
                bool shouldCollect = !_isPaused && 
                                     _canService != null &&
                                     ((isSimulator && _simulatorAdapter != null) || (isRealCan1kHz && _weightProcessor != null));

                if (shouldCollect)
                {
                    try
                    {
                        double leftWeight;
                        double rightWeight;
                        
                        if (isSimulator && _simulatorAdapter != null)
                        {
                            // DIRECT PATH: Get pattern weights directly from simulator (bypasses WeightProcessor)
                            // This is faster and doesn't require calibration/filtering setup
                            leftWeight = _simulatorAdapter.GetCurrentPatternWeight(true);
                            rightWeight = _simulatorAdapter.GetCurrentPatternWeight(false);
                        }
                        else
                        {
                            // REAL CAN PATH: Get processed weights from WeightProcessor (with calibration/filtering/tare)
                            leftWeight = _weightProcessor!.LatestLeft.TaredWeight;
                            rightWeight = _weightProcessor.LatestRight.TaredWeight;
                        }

                        // Add to pending batch - NO DISPATCHER HERE! (Non-UI thread safe)
                        // Always collect data (even if 0) to maintain continuous graph
                        var timestamp = DateTime.Now;
                        lock (_lock)
                        {
                            _pendingLeft.Add(leftWeight);
                            _pendingRight.Add(rightWeight);
                            _dataPointsCollected += 2; // Count both left and right
                            
                            // Store timestamps for time-based X-axis (always collect, use if needed)
                            _pendingLeftTimestamps.Add(timestamp);
                            _pendingRightTimestamps.Add(timestamp);
                        }
                        
                        // Use shorter delay when actively collecting
                        await Task.Delay(POLL_INTERVAL_MS, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        ProductionLogger.Instance.LogError($"Data polling error: {ex.Message}", "SuspensionGraph");
                        await Task.Delay(POLL_INTERVAL_MS, cancellationToken);
                    }
                }
                else
                {
                    // Longer sleep when paused or conditions not met to save CPU
                    await Task.Delay(PAUSED_POLL_INTERVAL_MS, cancellationToken);
                }
            }
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

                // Batch process pending data with minimal lock time
                double[]? leftData = null;
                double[]? rightData = null;
                DateTime[]? leftTimestamps = null;
                DateTime[]? rightTimestamps = null;
                
                lock (_lock)
                {
                    if (_pendingLeft.Count > 0 || _pendingRight.Count > 0)
                    {
                        leftData = _pendingLeft.ToArray();
                        rightData = _pendingRight.ToArray();
                        _pendingLeft.Clear();
                        _pendingRight.Clear();
                        
                        // Get corresponding timestamps (always collect, use if time-based mode)
                        if (_pendingLeftTimestamps.Count >= leftData.Length)
                        {
                            leftTimestamps = _pendingLeftTimestamps.Take(leftData.Length).ToArray();
                            _pendingLeftTimestamps.RemoveRange(0, leftTimestamps.Length);
                        }
                        if (_pendingRightTimestamps.Count >= rightData.Length)
                        {
                            rightTimestamps = _pendingRightTimestamps.Take(rightData.Length).ToArray();
                            _pendingRightTimestamps.RemoveRange(0, rightTimestamps.Length);
                        }
                    }
                }

                // Process data outside lock
                if (leftData != null && leftData.Length > 0)
                {
                    ProcessBatchData(_leftValues, leftData);
                    // Store timestamps if time-based mode
                    if (_xAxisTimeBased && leftTimestamps != null && leftTimestamps.Length == leftData.Length)
                    {
                        foreach (var ts in leftTimestamps)
                        {
                            _leftTimestamps.Add(ts);
                        }
                        // Maintain sliding window
                        while (_leftTimestamps.Count > MAX_SAMPLES)
                            _leftTimestamps.RemoveAt(0);
                    }
                }
                
                if (rightData != null && rightData.Length > 0)
                {
                    ProcessBatchData(_rightValues, rightData);
                    // Store timestamps if time-based mode
                    if (_xAxisTimeBased && rightTimestamps != null && rightTimestamps.Length == rightData.Length)
                    {
                        foreach (var ts in rightTimestamps)
                        {
                            _rightTimestamps.Add(ts);
                        }
                        // Maintain sliding window
                        while (_rightTimestamps.Count > MAX_SAMPLES)
                            _rightTimestamps.RemoveAt(0);
                    }
                }

                // Update status displays
                UpdateStatusDisplays();

                // Auto-scroll X-axis only when count changes (removed threshold for immediate scrolling)
                int maxCount = Math.Max(_leftValues.Count, _rightValues.Count);
                if (maxCount != _sampleCount)
                {
                    var xAxis = _suspensionChart.XAxes.First();
                    xAxis.MinLimit = Math.Max(0, maxCount - MAX_SAMPLES);
                    xAxis.MaxLimit = maxCount;
                    _sampleCount = maxCount;
                }
                
                // Update Y-axis based on mode
                if (_yAxisAutoScale)
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

            // Add all new data items
            // Note: ObservableCollection doesn't support AddRange, so we add individually
            // but we've already removed excess items, so this is more efficient
            for (int i = 0; i < addCount; i++)
            {
                collection.Add(newData[i]);
            }
        }

        private void UpdateStatusDisplays()
        {
            try
            {
                // Update current weight displays - read only TaredWeight property to avoid struct copy
                if (_weightProcessor != null)
                {
                    double leftWeight = _weightProcessor.LatestLeft.TaredWeight;
                    double rightWeight = _weightProcessor.LatestRight.TaredWeight;

                    LeftWeightText.Text = $"{leftWeight:F1} kg";
                    RightWeightText.Text = $"{rightWeight:F1} kg";
                }

                // Fix: Show actual data count instead of X-axis position
                SampleCountText.Text = Math.Max(_leftValues.Count, _rightValues.Count).ToString();
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Status update error: {ex.Message}", "SuspensionGraph");
            }
        }

        /// <summary>
        /// Update Y-axis limits based on visible data with auto-scaling
        /// </summary>
        private void UpdateYAxisAutoScale()
        {
            try
            {
                if (_leftValues.Count == 0 && _rightValues.Count == 0)
                {
                    // No data - use default range
                    var yAxis = _suspensionChart.YAxes.First();
                    yAxis.MinLimit = 0.0;
                    yAxis.MaxLimit = 1000.0;
                    return;
                }

                // Get visible data (last MAX_SAMPLES values)
                var visibleLeft = _leftValues.Skip(Math.Max(0, _leftValues.Count - MAX_SAMPLES));
                var visibleRight = _rightValues.Skip(Math.Max(0, _rightValues.Count - MAX_SAMPLES));
                var allValues = visibleLeft.Concat(visibleRight).Where(v => !double.IsNaN(v) && !double.IsInfinity(v));

                if (!allValues.Any())
                {
                    // No valid data - use default range
                    var yAxis = _suspensionChart.YAxes.First();
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
                var yAxis2 = _suspensionChart.YAxes.First();
                yAxis2.MinLimit = minWeight;
                yAxis2.MaxLimit = maxWeight;
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
                    if (_leftTimestamps.Count > 0 || _rightTimestamps.Count > 0)
                    {
                        var allTimestamps = _leftTimestamps.Concat(_rightTimestamps).Where(t => t != default);
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
                    // Sample-based X-axis (existing behavior)
                    int maxCount = Math.Max(_leftValues.Count, _rightValues.Count);
                    xAxis.MinLimit = Math.Max(0, maxCount - MAX_SAMPLES);
                    xAxis.MaxLimit = maxCount;
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
                        ConnectionStatusText.Text = "Connected (Simulator - Direct pattern graphs active)";
                    }
                    else if (!is1kHz)
                    {
                        ConnectionStatusText.Text = $"Connected ({GetRateText(_currentTransmissionRate)} - Graphs disabled, 1kHz required)";
                    }
                    else
                    {
                        ConnectionStatusText.Text = "Connected (1kHz - Graphs active via WeightProcessor)";
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
                lock (_lock)
                {
                    _leftValues.Clear();
                    _rightValues.Clear();
                    _pendingLeft.Clear();
                    _pendingRight.Clear();
                    _leftTimestamps.Clear();
                    _rightTimestamps.Clear();
                    _sampleCount = 0;
                }

                // Reset X-axis
                var xAxis = _suspensionChart.XAxes.First();
                xAxis.MinLimit = 0;
                xAxis.MaxLimit = MAX_SAMPLES;
                
                // Reset Y-axis
                var yAxis = _suspensionChart.YAxes.First();
                yAxis.MinLimit = _originalYMin;
                yAxis.MaxLimit = _originalYMax;

                SampleCountText.Text = "0";
                ProductionLogger.Instance.LogInfo("Graph cleared", "SuspensionGraph");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Clear error: {ex.Message}", "SuspensionGraph");
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
                    
                    // Write header
                    writer.WriteLine("Sample,Left Side (kg),Right Side (kg)");

                    // Copy data to arrays for thread-safe access
                    double[] leftData;
                    double[] rightData;
                    lock (_lock)
                    {
                        leftData = _leftValues.ToArray();
                        rightData = _rightValues.ToArray();
                    }

                    // Write data (use maximum count to align both series)
                    int maxCount = Math.Max(leftData.Length, rightData.Length);
                    for (int i = 0; i < maxCount; i++)
                    {
                        double leftValue = i < leftData.Length ? leftData[i] : 0;
                        double rightValue = i < rightData.Length ? rightData[i] : 0;
                        writer.WriteLine($"{i},{leftValue:F2},{rightValue:F2}");
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

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Stop UI timer
                _uiTimer?.Stop();

                // Stop background data collection thread
                _dataCollectionCts?.Cancel();
                _dataCollectionTask?.Wait(1000); // Wait up to 1 second for graceful shutdown
                _dataCollectionCts?.Dispose();

                // Unsubscribe from events
                if (_canService != null)
                {
                    _canService.MessageReceived -= CanService_MessageReceived;
                }

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

