using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SuspensionPCB_CAN_WPF
{
    public partial class LogsWindow : Window
    {
        private readonly ProductionLogger? _logger;

        // Log filtering
        public ObservableCollection<ProductionLogger.LogEntry> AllLogEntries { get; set; }
        public ObservableCollection<ProductionLogger.LogEntry> FilteredLogEntries { get; set; }

        public LogsWindow(ProductionLogger? logger)
        {
            InitializeComponent();
            
            _logger = logger;

            // Initialize collections
            AllLogEntries = new ObservableCollection<ProductionLogger.LogEntry>();
            FilteredLogEntries = new ObservableCollection<ProductionLogger.LogEntry>();

            // Set data context
            DataContext = this;

            // Subscribe to logger events - ProductionLogger doesn't have LogEntryAdded event
            // We'll use a timer to update the UI periodically

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
                    AllLogEntries.Add(entry);
                }
            }

            // Set up filtering
            LogFilterChanged(null!, null!);
        }


        private void EnableLoggingChk_Checked(object sender, RoutedEventArgs e)
        {
            if (_logger != null)
            {
                _logger.IsEnabled = true;
            }
        }

        private void EnableLoggingChk_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_logger != null)
            {
                _logger.IsEnabled = false;
            }
        }

        private void MinLevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_logger != null && MinLevelCombo.SelectedIndex >= 0)
            {
                _logger.MinimumLevel = (ProductionLogger.LogLevel)(MinLevelCombo.SelectedIndex + 1);
            }
            LogFilterChanged(sender, e);
        }

        private void LogFilterChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                FilteredLogEntries.Clear();

                bool showInfo = ShowInfoChk?.IsChecked == true;
                bool showWarning = ShowWarningChk?.IsChecked == true;
                bool showError = ShowErrorChk?.IsChecked == true;
                bool showCritical = ShowCriticalChk?.IsChecked == true;

                var filtered = AllLogEntries.Where(entry =>
                {
                    return entry.Level switch
                    {
                        ProductionLogger.LogLevel.Info => showInfo,
                        ProductionLogger.LogLevel.Warning => showWarning,
                        ProductionLogger.LogLevel.Error => showError,
                        ProductionLogger.LogLevel.Critical => showCritical,
                        _ => false
                    };
                });

                foreach (var entry in filtered)
                {
                    FilteredLogEntries.Add(entry);
                }

                LogCountTxt.Text = $"{FilteredLogEntries.Count} entries";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log filtering error: {ex.Message}");
            }
        }

        private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AllLogEntries.Clear();
                FilteredLogEntries.Clear();
                LogCountTxt.Text = "0 entries";
                
                if (_logger != null)
                {
                    _logger.ClearLogs();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clear logs error: {ex.Message}");
            }
        }

        private void ExportLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_logger != null)
                {
                    var saveDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                        DefaultExt = "txt",
                        FileName = $"suspension_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                    };

                    if (saveDialog.ShowDialog() == true)
                    {
                        if (_logger.ExportLogs(saveDialog.FileName))
                        {
                            MessageBox.Show($"Logs exported successfully to:\n{saveDialog.FileName}", 
                                          "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("Failed to export logs.", "Export Error", 
                                          MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export error: {ex.Message}", "Export Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}
