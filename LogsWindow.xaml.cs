using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using Microsoft.Win32;

namespace SuspensionPCB_CAN_WPF
{
    public partial class LogsWindow : Window
    {
        private readonly ProductionLogger? _logger;
        private readonly DataLogger? _dataLogger;
        private bool _isInitialized = false;
        private bool _autoScroll = true;

        // CAN message logging
        public ObservableCollection<LogEntry> AllLogEntries { get; set; }
        public ObservableCollection<LogEntry> FilteredLogEntries { get; set; }

        public LogsWindow(ProductionLogger? logger, DataLogger? dataLogger = null)
        {
            InitializeComponent();
            
            _logger = logger;
            _dataLogger = dataLogger;

            // Initialize collections
            AllLogEntries = new ObservableCollection<LogEntry>();
            FilteredLogEntries = new ObservableCollection<LogEntry>();

            // Set data context
            DataContext = this;

            // Initialize UI
            InitializeUI();
            
            // Subscribe to ProductionLogger collection changes for real-time updates
            if (_logger != null)
            {
                _logger.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
            }
        }
        
        private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    // New entries added
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (e.NewItems != null)
                        {
                            foreach (ProductionLogger.LogEntry entry in e.NewItems)
                            {
                                var logEntry = new LogEntry
                                {
                                    Timestamp = entry.Timestamp,
                                    Message = entry.Message,
                                    Level = entry.Level.ToString(), // Use actual Level property
                                    Source = string.IsNullOrEmpty(entry.Source) ? "ProductionLogger" : entry.Source
                                };
                                AllLogEntries.Add(logEntry);
                            }
                            
                            // Apply filters to new entries
                            if (_isInitialized)
                            {
                                ApplyFiltersToNewEntries();
                                UpdateLogCount();
                                AutoScrollToBottom();
                            }
                        }
                    }));
                }
                else if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    // Collection cleared
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        AllLogEntries.Clear();
                        FilteredLogEntries.Clear();
                        UpdateLogCount();
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogEntries_CollectionChanged error: {ex.Message}");
            }
        }
        
        private void ApplyFiltersToNewEntries()
        {
            try
            {
                // Only add new entries that match current filters
                var lastCount = FilteredLogEntries.Count;
                foreach (var entry in AllLogEntries.Skip(lastCount))
                {
                    bool include = true;
                    
                    string levelUpper = entry.Level.ToUpper();
                    if ((levelUpper == "INFO" || entry.Level == "Info") && ShowInfoChk?.IsChecked != true)
                        include = false;
                    if ((levelUpper == "WARNING" || entry.Level == "Warning") && ShowWarningChk?.IsChecked != true)
                        include = false;
                    if ((levelUpper == "ERROR" || entry.Level == "Error") && ShowErrorChk?.IsChecked != true)
                        include = false;
                    if ((levelUpper == "CRITICAL" || entry.Level == "Critical") && ShowCriticalChk?.IsChecked != true)
                        include = false;

                    if (include)
                    {
                        FilteredLogEntries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyFiltersToNewEntries error: {ex.Message}");
            }
        }
        
        private void AutoScrollToBottom()
        {
            try
            {
                if (_autoScroll && LogMessagesListBox != null && FilteredLogEntries.Count > 0)
                {
                    LogMessagesListBox.ScrollIntoView(FilteredLogEntries[FilteredLogEntries.Count - 1]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoScrollToBottom error: {ex.Message}");
            }
        }

        private void InitializeUI()
        {
            // Load existing log entries
            if (_logger != null)
            {
                foreach (var entry in _logger.LogEntries)
                {
                    // Convert ProductionLogger.LogEntry to LogEntry
                    var logEntry = new LogEntry
                    {
                        Timestamp = entry.Timestamp,
                        Message = entry.Message,
                        Level = entry.Level.ToString(), // Use actual Level property instead of parsing
                        Source = string.IsNullOrEmpty(entry.Source) ? "ProductionLogger" : entry.Source
                    };
                    AllLogEntries.Add(logEntry);
                }
            }

            // Initialize EnableLoggingChk checkbox state based on DataLogger
            if (EnableLoggingChk != null && _dataLogger != null)
            {
                EnableLoggingChk.IsChecked = _dataLogger.IsLogging;
                EnableLoggingChk.IsEnabled = false; // Disable checkbox - state is read-only, controlled by MainWindow buttons
            }
            else if (EnableLoggingChk != null)
            {
                EnableLoggingChk.IsChecked = false;
                EnableLoggingChk.IsEnabled = false;
            }

            // Set up filtering only after UI is fully loaded
            Dispatcher.BeginInvoke(new Action(() => {
                try
                {
                    _isInitialized = true;
                    LogFilterChanged(null!, null!);
                    UpdateLogCount();
                }
                catch (Exception ex)
                {
                    // Silently handle initialization errors
                    System.Diagnostics.Debug.WriteLine($"LogsWindow initialization error: {ex.Message}");
                }
            }));
        }


        private void LogFilterChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Don't process filters until UI is fully initialized
                if (!_isInitialized) return;
                
                FilteredLogEntries.Clear();
                
                // Apply filters based on checkboxes (with null checks)
                foreach (var entry in AllLogEntries)
                {
                    bool include = true;
                    
                    // Check log level filters with null safety (handle both "Info" and "INFO" formats)
                    string levelUpper = entry.Level.ToUpper();
                    if ((levelUpper == "INFO" || entry.Level == "Info") && ShowInfoChk?.IsChecked != true)
                        include = false;
                    if ((levelUpper == "WARNING" || entry.Level == "Warning") && ShowWarningChk?.IsChecked != true)
                        include = false;
                    if ((levelUpper == "ERROR" || entry.Level == "Error") && ShowErrorChk?.IsChecked != true)
                        include = false;
                    if ((levelUpper == "CRITICAL" || entry.Level == "Critical") && ShowCriticalChk?.IsChecked != true)
                        include = false;

                    if (include)
                    {
                        FilteredLogEntries.Add(entry);
                    }
                }
                
                UpdateLogCount();
                AutoScrollToBottom();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Filter error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateLogCount()
        {
            try
            {
                if (LogCountTxt != null)
                {
                    LogCountTxt.Text = $"{FilteredLogEntries.Count} entries";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateLogCount error: {ex.Message}");
            }
        }

        private void EnableLoggingChk_Checked(object sender, RoutedEventArgs e)
        {
            // Checkbox is read-only - state reflects DataLogger.IsLogging
            // Logging is controlled only from MainWindow Start/Stop buttons
        }

        private void EnableLoggingChk_Unchecked(object sender, RoutedEventArgs e)
        {
            // Checkbox is read-only - state reflects DataLogger.IsLogging
            // Logging is controlled only from MainWindow Start/Stop buttons
        }
        
        /// <summary>
        /// Update the logging checkbox state (called from MainWindow when logging state changes)
        /// </summary>
        public void UpdateLoggingState(bool isLogging)
        {
            try
            {
                if (EnableLoggingChk != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        EnableLoggingChk.IsChecked = isLogging;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateLoggingState error: {ex.Message}");
            }
        }

        private void MinLevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle minimum log level change
            LogFilterChanged(sender, e);
        }

        private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("Clear all log entries from display?\n\nNote: This only clears the display. Log files are not affected.", 
                                           "Confirm Clear", 
                                           MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    AllLogEntries.Clear();
                    FilteredLogEntries.Clear();
                    UpdateLogCount();
                    
                    // Also clear ProductionLogger if available
                    _logger?.ClearLogs();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Clear error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Title = "Export Logs",
                    Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    using (var writer = new StreamWriter(saveDialog.FileName))
                    {
                        writer.WriteLine("Timestamp,Level,Message,Source");
                        
                        foreach (var entry in FilteredLogEntries)
                        {
                            writer.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{entry.Level},\"{entry.Message}\",{entry.Source}");
                        }
                    }
                    
                    MessageBox.Show($"Logs exported to: {saveDialog.FileName}", "Export Complete", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from collection changes
            if (_logger != null)
            {
                _logger.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
            }
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Log entry for display
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string Source { get; set; } = "";
        
        public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] {Level}: {Message}";
    }
}