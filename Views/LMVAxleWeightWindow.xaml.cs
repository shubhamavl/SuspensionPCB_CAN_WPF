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

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class LMVAxleWeightWindow : Window
    {
        // Data collections for graph series
        private readonly ObservableCollection<double> _leftValues = new();
        private readonly ObservableCollection<double> _rightValues = new();

        // Batch processing collections (non-UI thread safe)
        private readonly List<double> _pendingLeft = new();
        private readonly List<double> _pendingRight = new();
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
        private CartesianChart _axleChart = null!;

        // State
        private bool _isPaused = false;
        private const int MAX_SAMPLES = 4000;    // Sliding window size
        private int _sampleCount = 0;

        // Data rate monitoring
        private int _dataPointsCollected = 0;
        private DateTime _lastRateCheck = DateTime.Now;

        // Connection status update throttling
        private DateTime _lastConnectionStatusUpdate = DateTime.MinValue;
        private const int CONNECTION_STATUS_UPDATE_INTERVAL_MS = 500;
        private const int PAUSED_POLL_INTERVAL_MS = 100;

        public LMVAxleWeightWindow(CANService? canService, WeightProcessor? weightProcessor)
        {
            InitializeComponent();
            _canService = canService;
            _weightProcessor = weightProcessor;

            InitializeGraph();
            InitializeTimers();
            UpdateConnectionStatus();

            // Subscribe to CAN service if available
            if (_canService != null)
            {
                _canService.MessageReceived += CanService_MessageReceived;
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

            // Start background data collection thread (100 Hz - 10ms polling)
            // This runs on a background thread to avoid blocking the UI
            _dataCollectionCts = new CancellationTokenSource();
            _dataCollectionTask = Task.Run(async () => await DataCollectionLoop(_dataCollectionCts.Token));
        }

        /// <summary>
        /// Background thread loop for data collection - does NOT block UI thread
        /// </summary>
        private async Task DataCollectionLoop(CancellationToken cancellationToken)
        {
            const int POLL_INTERVAL_MS = 10; // 100 Hz polling

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_isPaused && _weightProcessor != null)
                {
                    try
                    {
                        // Poll latest data from WeightProcessor (lock-free volatile reads - very fast)
                        double leftWeight = _weightProcessor.LatestLeft.TaredWeight;
                        double rightWeight = _weightProcessor.LatestRight.TaredWeight;

                        // Add to pending batch - NO DISPATCHER HERE! (Non-UI thread safe)
                        lock (_lock)
                        {
                            _pendingLeft.Add(leftWeight);
                            _pendingRight.Add(rightWeight);
                            _dataPointsCollected += 2; // Count both left and right
                        }

                        // Use shorter delay when actively collecting
                        await Task.Delay(POLL_INTERVAL_MS, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        ProductionLogger.Instance.LogError($"LMV axle data polling error: {ex.Message}", "LMVAxle");
                        await Task.Delay(POLL_INTERVAL_MS, cancellationToken);
                    }
                }
                else
                {
                    // Longer sleep when paused to save CPU
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

                lock (_lock)
                {
                    if (_pendingLeft.Count > 0 || _pendingRight.Count > 0)
                    {
                        leftData = _pendingLeft.ToArray();
                        rightData = _pendingRight.ToArray();
                        _pendingLeft.Clear();
                        _pendingRight.Clear();
                    }
                }

                // Process data outside lock
                if (leftData != null && leftData.Length > 0)
                {
                    ProcessBatchData(_leftValues, leftData);
                }

                if (rightData != null && rightData.Length > 0)
                {
                    ProcessBatchData(_rightValues, rightData);
                }

                // Update status displays (numeric values)
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

        /// <summary>
        /// Efficiently process batch data for ObservableCollection
        /// Adds all items first, then removes excess in one operation
        /// </summary>
        private void ProcessBatchData(ObservableCollection<double> collection, double[] newData)
        {
            int addCount = newData.Length;
            int currentCount = collection.Count;
            int totalAfterAdd = currentCount + addCount;
            int removeCount = Math.Max(0, totalAfterAdd - MAX_SAMPLES);

            // Add all new data
            for (int i = 0; i < addCount; i++)
            {
                collection.Add(newData[i]);
            }

            // Remove excess from beginning (if needed)
            if (removeCount > 0)
            {
                for (int i = 0; i < removeCount; i++)
                {
                    collection.RemoveAt(0);
                }
            }
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

                LeftAxleText.Text = $"{leftWeight:F1} kg";
                RightAxleText.Text = $"{rightWeight:F1} kg";
                TotalWeightText.Text = $"{total:F1} kg";
                BalanceText.Text = $"{balancePercent:F1} %";

                SampleCountText.Text = _sampleCount.ToString();
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"LMV axle status update error: {ex.Message}", "LMVAxle");
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

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = true;
            PauseBtn.IsEnabled = false;
            ResumeBtn.IsEnabled = true;
            ProductionLogger.Instance.LogInfo("LMV axle graph paused", "LMVAxle");
        }

        private void ResumeBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = false;
            PauseBtn.IsEnabled = true;
            ResumeBtn.IsEnabled = false;
            ProductionLogger.Instance.LogInfo("LMV axle graph resumed", "LMVAxle");
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
                    _sampleCount = 0;
                }

                // Reset X-axis
                var xAxis = _axleChart.XAxes.First();
                xAxis.MinLimit = 0;
                xAxis.MaxLimit = MAX_SAMPLES;

                SampleCountText.Text = "0";
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

                ProductionLogger.Instance.LogInfo("LMVAxleWeightWindow closed", "LMVAxle");
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"LMV axle window close error: {ex.Message}", "LMVAxle");
            }

            base.OnClosed(e);
        }
    }
}


