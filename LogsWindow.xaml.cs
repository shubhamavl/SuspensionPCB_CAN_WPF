using System;
using System.Collections.ObjectModel;
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
        private bool _isInitialized = false;

        // CAN message logging
        public ObservableCollection<LogEntry> AllLogEntries { get; set; }
        public ObservableCollection<LogEntry> FilteredLogEntries { get; set; }

        public LogsWindow(ProductionLogger? logger)
        {
            InitializeComponent();
            
            _logger = logger;

            // Initialize collections
            AllLogEntries = new ObservableCollection<LogEntry>();
            FilteredLogEntries = new ObservableCollection<LogEntry>();

            // Set data context
            DataContext = this;

            // Initialize UI
            InitializeUI();
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
                        Level = ExtractLogLevel(entry.Message),
                        Source = "ProductionLogger"
                    };
                    AllLogEntries.Add(logEntry);
                }
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

        private string ExtractLogLevel(string message)
        {
            if (message.Contains("ERROR")) return "ERROR";
            if (message.Contains("WARNING")) return "WARNING";
            if (message.Contains("CRITICAL")) return "CRITICAL";
            if (message.Contains("INFO")) return "INFO";
            return "INFO";
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
                    
                    // Check log level filters with null safety
                    if (entry.Level == "INFO" && ShowInfoChk?.IsChecked != true)
                        include = false;
                    if (entry.Level == "WARNING" && ShowWarningChk?.IsChecked != true)
                        include = false;
                    if (entry.Level == "ERROR" && ShowErrorChk?.IsChecked != true)
                        include = false;
                    if (entry.Level == "CRITICAL" && ShowCriticalChk?.IsChecked != true)
                        include = false;

                    if (include)
                    {
                        FilteredLogEntries.Add(entry);
                    }
                }
                
                UpdateLogCount();
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
            // Enable logging functionality
        }

        private void EnableLoggingChk_Unchecked(object sender, RoutedEventArgs e)
        {
            // Disable logging functionality
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
                var result = MessageBox.Show("Clear all log entries?", "Confirm Clear", 
                                           MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    AllLogEntries.Clear();
                    FilteredLogEntries.Clear();
                    UpdateLogCount();
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