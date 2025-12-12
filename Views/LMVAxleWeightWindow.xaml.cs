using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Services;
using Microsoft.Win32;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class LMVAxleWeightWindow : Window
    {
        // Data collections for graph series
        private readonly ObservableCollection<double> _leftValues = new();
        private readonly ObservableCollection<double> _rightValues = new();

        // Event-driven data collection (thread-safe)
        private readonly ConcurrentQueue<double> _pendingWeights = new();
        private readonly ConcurrentQueue<DateTime> _pendingTimestamps = new();

        // Timers
        private DispatcherTimer? _uiTimer;      // UI update timer (20 Hz)

        // Services
        private readonly CANService? _canService;
        private readonly WeightProcessor? _weightProcessor;

        // Chart control
        private CartesianChart _axleChart = null!;

        // State
        private bool _isPaused = false;
        private const int MAX_SAMPLES = 21000;    // Sliding window size (matching SuspensionGraphWindow)
        private int _sampleCount = 0;

        // Test state management
        private enum TestState { Idle, Reading, Stopped, Completed }
        private TestState _testState = TestState.Idle;

        // Test data tracking
        private AxleTestDataModel? _currentTestData;
        private double _minWeight = double.MaxValue;
        private double _maxWeight = double.MinValue;
        private bool _hasMinMaxData = false;
        private int _testSampleCount = 0;
        private DateTime? _testStartTime = null;

        // Data rate monitoring
        private int _dataPointsCollected = 0;
        private DateTime _lastRateCheck = DateTime.Now;

        // Connection status update throttling
        private DateTime _lastConnectionStatusUpdate = DateTime.MinValue;
        private const int CONNECTION_STATUS_UPDATE_INTERVAL_MS = 500;

        // Validation thresholds
        private const double MIN_VALID_WEIGHT = 10.0; // kg - minimum valid weight
        private const double BALANCE_THRESHOLD = 2.0;  // One side can be max 2x the other

        public LMVAxleWeightWindow(CANService? canService, WeightProcessor? weightProcessor)
        {
            InitializeComponent();
            _canService = canService;
            _weightProcessor = weightProcessor;

            InitializeGraph();
            InitializeTimers();
            UpdateConnectionStatus();
            InitializeAxleSelector();

            // Subscribe to CAN service events
            if (_canService != null)
            {
                _canService.MessageReceived += CanService_MessageReceived;
                _canService.RawDataReceived += OnRawDataReceived;
            }

            // Set initial state
            UpdateTestState(TestState.Idle);
        }

        private void InitializeAxleSelector()
        {
            // Remove axle selector - single axle testing only (LMV 4-wheelers)
            if (AxleSelector != null)
            {
                AxleSelector.Visibility = Visibility.Collapsed;
            }
        }

        private void OnRawDataReceived(object? sender, RawDataEventArgs e)
        {
            // Event-driven data collection - enqueue weights for processing
            if (_testState == TestState.Reading && _weightProcessor != null)
            {
                double leftWeight = _weightProcessor.LatestLeft.TaredWeight;
                double rightWeight = _weightProcessor.LatestRight.TaredWeight;
                double totalWeight = leftWeight + rightWeight;

                _pendingWeights.Enqueue(totalWeight);
                _pendingTimestamps.Enqueue(DateTime.Now);
            }
        }

        private void InitializeGraph()
        {
            // Create chart control programmatically
            _axleChart = new CartesianChart();
            chartContainer.Children.Add(_axleChart);

            // Configure two series: Left Axle (Green) and Right Axle (Blue)
            _axleChart.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _leftValues,
                    Name = "Left Axle",
                    Fill = null,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(39, 174, 96), 2), // Green
                    LineSmoothness = 0
                },
                new LineSeries<double>
                {
                    Values = _rightValues,
                    Name = "Right Axle",
                    Fill = null,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(52, 152, 219), 2), // Blue
                    LineSmoothness = 0
                }
            };

            // X-Axis Configuration (Samples)
            _axleChart.XAxes = new Axis[]
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
            _axleChart.YAxes = new Axis[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MaxLimit = 2000, // Axle weight range (adjust as needed)
                    Name = "Weight (kg)",
                    TextSize = 12
                }
            };
        }

        private void InitializeTimers()
        {
            // UI Update Timer (20 Hz - 50ms) - Batch processes pending data on UI thread
            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(50);
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPaused && _testState != TestState.Reading)
                return;

            try
            {
                // Calculate data rate every second
                var now = DateTime.Now;
                if ((now - _lastRateCheck).TotalSeconds >= 1.0)
                {
                    if (DataRateText != null)
                    {
                        DataRateText.Text = $"Rate: {_dataPointsCollected} pts/sec";
                    }
                    _dataPointsCollected = 0;
                    _lastRateCheck = now;
                }

                // Batch dequeue from ConcurrentQueue (thread-safe, non-blocking)
                var batchWeights = new List<double>();
                var batchTimestamps = new List<DateTime>();

                while (_pendingWeights.TryDequeue(out double weight) && _pendingTimestamps.TryDequeue(out DateTime timestamp))
                {
                    batchWeights.Add(weight);
                    batchTimestamps.Add(timestamp);
                }

                // Process batch data
                if (batchWeights.Count > 0 && _testState == TestState.Reading)
                {
                    ProcessBatchData(batchWeights);
                }

                // Update status displays (numeric values, validation, etc.)
                UpdateStatusDisplays();

                // Auto-scroll X-axis only when count changes
                int maxCount = Math.Max(_leftValues.Count, _rightValues.Count);
                if (maxCount != _sampleCount && maxCount > 100)
                {
                    var xAxis = _axleChart.XAxes.First();
                    xAxis.MinLimit = Math.Max(0, maxCount - MAX_SAMPLES);
                    xAxis.MaxLimit = maxCount;
                    _sampleCount = maxCount;
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"LMV axle UI update error: {ex.Message}", "LMVAxle");
            }
        }

        private void ProcessBatchData(List<double> weights)
        {
            if (_weightProcessor == null) return;

            double leftWeight = _weightProcessor.LatestLeft.TaredWeight;
            double rightWeight = _weightProcessor.LatestRight.TaredWeight;
            double totalWeight = leftWeight + rightWeight;

            // Add to graph collections
            _leftValues.Add(leftWeight);
            _rightValues.Add(rightWeight);

            // Track Min/Max during test
            if (_testState == TestState.Reading)
            {
                if (totalWeight > 0)
                {
                    if (!_hasMinMaxData || totalWeight < _minWeight)
                        _minWeight = totalWeight;
                    if (!_hasMinMaxData || totalWeight > _maxWeight)
                        _maxWeight = totalWeight;
                    _hasMinMaxData = true;
                }

                _testSampleCount++;
                _dataPointsCollected += 2; // Count both left and right
            }

            // Maintain sliding window
            if (_leftValues.Count > MAX_SAMPLES)
                _leftValues.RemoveAt(0);
            if (_rightValues.Count > MAX_SAMPLES)
                _rightValues.RemoveAt(0);
        }


        private void UpdateStatusDisplays()
        {
            try
            {
                double leftWeight = 0;
                double rightWeight = 0;

                if (_weightProcessor != null)
                {
                    leftWeight = _weightProcessor.LatestLeft.TaredWeight;
                    rightWeight = _weightProcessor.LatestRight.TaredWeight;
                }

                double total = leftWeight + rightWeight;
                double balancePercent = (total > 0) ? (leftWeight / total) * 100.0 : 0.0;

                // Update weight displays
                if (LeftWeightText != null)
                    LeftWeightText.Text = $"{leftWeight:F1} kg";
                if (RightWeightText != null)
                    RightWeightText.Text = $"{rightWeight:F1} kg";
                if (TotalWeightText != null)
                    TotalWeightText.Text = $"{total:F1} kg";
                if (BalanceText != null)
                    BalanceText.Text = $"{balancePercent:F1} %";

                // Update sample count (use test sample count if test is active)
                if (SampleCountText != null)
                {
                    SampleCountText.Text = _testState == TestState.Reading ? _testSampleCount.ToString() : _sampleCount.ToString();
                }

                // Update Min/Max displays
                if (MinWeightText != null && MaxWeightText != null)
                {
                    if (_hasMinMaxData)
                    {
                        MinWeightText.Text = $"{_minWeight:F1} kg";
                        MaxWeightText.Text = $"{_maxWeight:F1} kg";
                    }
                    else
                    {
                        MinWeightText.Text = "-- kg";
                        MaxWeightText.Text = "-- kg";
                    }
                }

                // Update validation indicators
                UpdateValidationIndicators(leftWeight, rightWeight);

                // Check balance and show warnings
                CheckBalance(leftWeight, rightWeight);
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"LMV axle status update error: {ex.Message}", "LMVAxle");
            }
        }

        private void UpdateValidationIndicators(double leftWeight, double rightWeight)
        {
            // Left side validation (Green >= 10kg, Red < 10kg)
            if (LeftValidationRect != null)
            {
                var leftBrush = leftWeight >= MIN_VALID_WEIGHT
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96))  // Green
                    : new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                LeftValidationRect.Background = leftBrush;
            }

            // Right side validation (Green >= 10kg, Red < 10kg)
            if (RightValidationRect != null)
            {
                var rightBrush = rightWeight >= MIN_VALID_WEIGHT
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96))  // Green
                    : new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                RightValidationRect.Background = rightBrush;
            }
        }

        private void CheckBalance(double leftWeight, double rightWeight)
        {
            // Check if one side is >= 2x the other (imbalance warning)
            if (leftWeight > 0 && rightWeight > 0)
            {
                double ratio = Math.Max(leftWeight, rightWeight) / Math.Min(leftWeight, rightWeight);
                if (ratio >= BALANCE_THRESHOLD)
                {
                    // Show warning in status message
                    if (StatusMessageText != null && _testState == TestState.Reading)
                    {
                        StatusMessageText.Text = $"⚠️ Warning: Imbalance detected! Ratio: {ratio:F2}x";
                        StatusMessageText.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    }
                }
                else if (StatusMessageText != null && _testState == TestState.Reading)
                {
                    StatusMessageText.Text = "Reading axle weight...";
                    StatusMessageText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Green
                }
            }
        }

        private void UpdateConnectionStatus()
        {
            try
            {
                bool isConnected = _canService?.IsConnected ?? false;

                if (ConnectionIndicator != null)
                {
                    ConnectionIndicator.Fill = isConnected
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96)) // Green
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                }

                if (ConnectionStatusText != null)
                {
                    ConnectionStatusText.Text = isConnected ? "Connected" : "Disconnected";
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"LMV axle connection status error: {ex.Message}", "LMVAxle");
            }
        }

        private void CanService_MessageReceived(Models.CANMessage message)
        {
            // Throttle connection status updates to prevent excessive UI updates
            var now = DateTime.Now;
            if ((now - _lastConnectionStatusUpdate).TotalMilliseconds >= CONNECTION_STATUS_UPDATE_INTERVAL_MS)
            {
                _lastConnectionStatusUpdate = now;
                Dispatcher.BeginInvoke(() => UpdateConnectionStatus());
            }
        }

        // Manual Test Controls
        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            StartTest();
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            StopTest();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveTestData();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Keyboard shortcuts: F1=Start, F2=Stop, F3=Save
            switch (e.Key)
            {
                case Key.F1:
                    if (StartBtn != null && StartBtn.IsEnabled)
                        StartTest();
                    e.Handled = true;
                    break;
                case Key.F2:
                    if (StopBtn != null && StopBtn.IsEnabled)
                        StopTest();
                    e.Handled = true;
                    break;
                case Key.F3:
                    if (SaveBtn != null && SaveBtn.IsEnabled)
                        SaveTestData();
                    e.Handled = true;
                    break;
            }
        }

        private void StartTest()
        {
            try
            {
                _testStartTime = DateTime.Now;
                _testSampleCount = 0;
                _minWeight = double.MaxValue;
                _maxWeight = double.MinValue;
                _hasMinMaxData = false;

                _currentTestData = new AxleTestDataModel
                {
                    TestId = Guid.NewGuid().ToString(),
                    AxleNumber = 1, // Single axle for LMV 4-wheelers
                    TestStartTime = _testStartTime.Value
                };

                UpdateTestState(TestState.Reading);
                ProductionLogger.Instance.LogInfo("Axle weight test started", "LMVAxle");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Error starting test: {ex.Message}", "LMVAxle");
                MessageBox.Show($"Error starting test: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopTest()
        {
            try
            {
                if (_testState == TestState.Reading)
                {
                    UpdateTestState(TestState.Stopped);
                    ProductionLogger.Instance.LogInfo("Axle weight test stopped", "LMVAxle");
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Error stopping test: {ex.Message}", "LMVAxle");
            }
        }

        private void SaveTestData()
        {
            try
            {
                if (_currentTestData == null || _weightProcessor == null)
                {
                    MessageBox.Show("No test data to save. Please start a test first.", "No Data",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get current weights
                double leftWeight = _weightProcessor.LatestLeft.TaredWeight;
                double rightWeight = _weightProcessor.LatestRight.TaredWeight;

                // Update test data
                _currentTestData.LeftWeight = leftWeight;
                _currentTestData.RightWeight = rightWeight;
                _currentTestData.TestEndTime = DateTime.Now;
                _currentTestData.SampleCount = _testSampleCount;
                _currentTestData.MinWeight = _hasMinMaxData ? _minWeight : 0;
                _currentTestData.MaxWeight = _hasMinMaxData ? _maxWeight : 0;

                // Validation status
                _currentTestData.LeftValidationStatus = leftWeight >= MIN_VALID_WEIGHT ? "Pass" : "Fail";
                _currentTestData.RightValidationStatus = rightWeight >= MIN_VALID_WEIGHT ? "Pass" : "Fail";

                // Balance status
                if (leftWeight > 0 && rightWeight > 0)
                {
                    double ratio = Math.Max(leftWeight, rightWeight) / Math.Min(leftWeight, rightWeight);
                    _currentTestData.BalanceStatus = ratio >= BALANCE_THRESHOLD ? "Warning" : "Pass";
                }
                else
                {
                    _currentTestData.BalanceStatus = "Not Tested";
                }

                // Save to JSON file
                SaveTestDataToJson(_currentTestData);

                UpdateTestState(TestState.Completed);
                ProductionLogger.Instance.LogInfo($"Axle test data saved: Left={leftWeight:F1}kg, Right={rightWeight:F1}kg", "LMVAxle");

                MessageBox.Show(
                    $"Test data saved successfully!\n\n" +
                    $"Left: {leftWeight:F1} kg ({_currentTestData.LeftValidationStatus})\n" +
                    $"Right: {rightWeight:F1} kg ({_currentTestData.RightValidationStatus})\n" +
                    $"Total: {_currentTestData.TotalWeight:F1} kg\n" +
                    $"Balance: {_currentTestData.BalanceStatus}\n" +
                    $"Samples: {_testSampleCount}",
                    "Test Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Error saving test data: {ex.Message}", "LMVAxle");
                MessageBox.Show($"Error saving test data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveTestDataToJson(AxleTestDataModel testData)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"AxleTest_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    string json = JsonSerializer.Serialize(testData, options);
                    File.WriteAllText(saveDialog.FileName, json);

                    ProductionLogger.Instance.LogInfo($"Test data saved to: {saveDialog.FileName}", "LMVAxle");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save JSON file: {ex.Message}", ex);
            }
        }

        private void UpdateTestState(TestState newState)
        {
            _testState = newState;

            Dispatcher.Invoke(() =>
            {
                // Update button states
                if (StartBtn != null)
                    StartBtn.IsEnabled = (newState == TestState.Idle || newState == TestState.Completed);
                if (StopBtn != null)
                    StopBtn.IsEnabled = (newState == TestState.Reading);
                if (SaveBtn != null)
                    SaveBtn.IsEnabled = (newState == TestState.Stopped || newState == TestState.Completed);

                // Update status message
                if (StatusMessageText != null)
                {
                    switch (newState)
                    {
                        case TestState.Idle:
                            StatusMessageText.Text = "Ready to start axle weight test";
                            StatusMessageText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Green
                            break;
                        case TestState.Reading:
                            StatusMessageText.Text = "Reading axle weight...";
                            StatusMessageText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Green
                            break;
                        case TestState.Stopped:
                            StatusMessageText.Text = "Test stopped. Click Save to save data.";
                            StatusMessageText.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                            break;
                        case TestState.Completed:
                            StatusMessageText.Text = "Test completed and saved. Ready for next test.";
                            StatusMessageText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Green
                            break;
                    }
                }
            });
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = true;
            if (PauseBtn != null) PauseBtn.IsEnabled = false;
            if (ResumeBtn != null) ResumeBtn.IsEnabled = true;
            ProductionLogger.Instance.LogInfo("LMV axle graph paused", "LMVAxle");
        }

        private void ResumeBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = false;
            if (PauseBtn != null) PauseBtn.IsEnabled = true;
            if (ResumeBtn != null) ResumeBtn.IsEnabled = false;
            ProductionLogger.Instance.LogInfo("LMV axle graph resumed", "LMVAxle");
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear graph data
                _leftValues.Clear();
                _rightValues.Clear();
                
                // Clear queues
                while (_pendingWeights.TryDequeue(out _)) { }
                while (_pendingTimestamps.TryDequeue(out _)) { }

                // Reset tracking
                _sampleCount = 0;
                _testSampleCount = 0;
                _minWeight = double.MaxValue;
                _maxWeight = double.MinValue;
                _hasMinMaxData = false;
                _currentTestData = null;
                _testStartTime = null;

                // Reset X-axis
                var xAxis = _axleChart.XAxes.First();
                xAxis.MinLimit = 0;
                xAxis.MaxLimit = MAX_SAMPLES;

                // Reset UI
                if (SampleCountText != null) SampleCountText.Text = "0";
                if (MinWeightText != null) MinWeightText.Text = "-- kg";
                if (MaxWeightText != null) MaxWeightText.Text = "-- kg";
                if (LeftWeightText != null) LeftWeightText.Text = "-- kg";
                if (RightWeightText != null) RightWeightText.Text = "-- kg";
                if (TotalWeightText != null) TotalWeightText.Text = "-- kg";
                if (BalanceText != null) BalanceText.Text = "-- %";

                // Reset validation indicators
                if (LeftValidationRect != null)
                    LeftValidationRect.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                if (RightValidationRect != null)
                    RightValidationRect.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red

                UpdateTestState(TestState.Idle);
                ProductionLogger.Instance.LogInfo("LMV axle graph cleared", "LMVAxle");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"LMV axle clear error: {ex.Message}", "LMVAxle");
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
                    FileName = $"LMVAxleGraph_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    // Disable button during export
                    exportBtn.IsEnabled = false;
                    exportBtn.Content = "💾 Exporting...";

                    try
                    {
                        await ExportToCSVAsync(saveDialog.FileName);
                        MessageBox.Show("LMV axle graph data exported successfully!", "Export Complete",
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
                ProductionLogger.Instance.LogError($"LMV axle export error: {ex.Message}", "LMVAxle");

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
                    writer.WriteLine("Sample,Left Axle (kg),Right Axle (kg)");

                    // Copy data to arrays for thread-safe access
                    double[] leftData = _leftValues.ToArray();
                    double[] rightData = _rightValues.ToArray();

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

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Stop UI timer
                _uiTimer?.Stop();

                // Unsubscribe from events
                if (_canService != null)
                {
                    _canService.MessageReceived -= CanService_MessageReceived;
                    _canService.RawDataReceived -= OnRawDataReceived;
                }

                // Clear queues
                while (_pendingWeights.TryDequeue(out _)) { }
                while (_pendingTimestamps.TryDequeue(out _)) { }

                ProductionLogger.Instance.LogInfo("LMVAxleWeightWindow closed", "LMVAxle");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"LMV axle window close error: {ex.Message}", "LMVAxle");
            }

            base.OnClosed(e);
        }

        private void AxleSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Multi-axle support removed - single axle testing only (LMV 4-wheelers)
            // This handler is kept to satisfy XAML binding, but does nothing
        }
    }
}


