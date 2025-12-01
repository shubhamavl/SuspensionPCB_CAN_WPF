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

        // Data rate monitoring
        private int _dataPointsCollected = 0;
        private DateTime _lastRateCheck = DateTime.Now;
        
        // Connection status update throttling
        private DateTime _lastConnectionStatusUpdate = DateTime.MinValue;
        private const int CONNECTION_STATUS_UPDATE_INTERVAL_MS = 500;
        private const int PAUSED_POLL_INTERVAL_MS = 100;

        public SuspensionGraphWindow(CANService? canService, WeightProcessor? weightProcessor)
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
                        // Always collect data (even if 0) to maintain continuous graph
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
                        ProductionLogger.Instance.LogError($"Data polling error: {ex.Message}", "SuspensionGraph");
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

                // Update status displays
                UpdateStatusDisplays();

                // Auto-scroll X-axis only when count changes
                int maxCount = Math.Max(_leftValues.Count, _rightValues.Count);
                if (maxCount != _sampleCount && maxCount > 100)
                {
                    var xAxis = _suspensionChart.XAxes.First();
                    xAxis.MinLimit = Math.Max(0, maxCount - MAX_SAMPLES);
                    xAxis.MaxLimit = maxCount;
                    _sampleCount = maxCount;
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"UI update error: {ex.Message}", "SuspensionGraph");
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
                // Update current weight displays - read only TaredWeight property to avoid struct copy
                if (_weightProcessor != null)
                {
                    double leftWeight = _weightProcessor.LatestLeft.TaredWeight;
                    double rightWeight = _weightProcessor.LatestRight.TaredWeight;

                    LeftWeightText.Text = $"{leftWeight:F1} kg";
                    RightWeightText.Text = $"{rightWeight:F1} kg";
                }

                SampleCountText.Text = _sampleCount.ToString();
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Status update error: {ex.Message}", "SuspensionGraph");
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
                ProductionLogger.Instance.LogError($"Connection status update error: {ex.Message}", "SuspensionGraph");
            }
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
                    _sampleCount = 0;
                }

                // Reset X-axis
                var xAxis = _suspensionChart.XAxes.First();
                xAxis.MinLimit = 0;
                xAxis.MaxLimit = MAX_SAMPLES;

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

