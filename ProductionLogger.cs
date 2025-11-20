using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Production logging system with configurable severity levels and UI visibility control
    /// </summary>
    public class ProductionLogger : INotifyPropertyChanged
    {
        public enum LogLevel
        {
            Info = 1,
            Warning = 2,
            Error = 3,
            Critical = 4
        }

        public class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; } = "";
            public string Source { get; set; } = "";

            public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] {Level}: {Message}";
            public string LevelText => Level.ToString();
        }

        private static ProductionLogger? _instance;
        private static readonly object _lock = new object();
        private readonly ObservableCollection<LogEntry> _logEntries = new();
        private readonly object _logLock = new object();
        private bool _isEnabled = true;
        private LogLevel _minimumLevel = LogLevel.Info;
        private string _logFilePath = "";

        public static ProductionLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ProductionLogger();
                    }
                }
                return _instance;
            }
        }

        public ObservableCollection<LogEntry> LogEntries => _logEntries;
        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set 
            { 
                _isEnabled = value; 
                OnPropertyChanged(nameof(IsEnabled));
            } 
        }

        public LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set
            {
                _minimumLevel = value;
                OnPropertyChanged(nameof(MinimumLevel));
            }
        }

        private ProductionLogger()
        {
            // Create timestamped log file in portable logs directory (next to executable)
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logsDir = PathHelper.GetLogsDirectory(); // Portable: relative to executable
            
            _logFilePath = Path.Combine(logsDir, $"suspension_log_{timestamp}.txt");
        }

        /// <summary>
        /// Log a message with specified level
        /// </summary>
        public void Log(LogLevel level, string message, string source = "")
        {
            if (!_isEnabled || level < _minimumLevel)
                return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Source = source
            };

            lock (_logLock)
            {
                _logEntries.Add(entry);

                // Keep only last 1000 entries to prevent memory issues
                while (_logEntries.Count > 1000)
                {
                    _logEntries.RemoveAt(0);
                }

                // Write to file
                WriteToFile(entry);
            }

            // Update UI on main thread
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(LogEntries));
            }));
        }

        /// <summary>
        /// Log info message
        /// </summary>
        public void LogInfo(string message, string source = "")
        {
            Log(LogLevel.Info, message, source);
        }

        /// <summary>
        /// Log warning message
        /// </summary>
        public void LogWarning(string message, string source = "")
        {
            Log(LogLevel.Warning, message, source);
        }

        /// <summary>
        /// Log error message
        /// </summary>
        public void LogError(string message, string source = "")
        {
            Log(LogLevel.Error, message, source);
        }

        /// <summary>
        /// Log critical message
        /// </summary>
        public void LogCritical(string message, string source = "")
        {
            Log(LogLevel.Critical, message, source);
        }

        /// <summary>
        /// Clear all log entries
        /// </summary>
        public void ClearLogs()
        {
            lock (_logLock)
            {
                _logEntries.Clear();
            }

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(LogEntries));
            }));
        }

        /// <summary>
        /// Export logs to file
        /// </summary>
        public bool ExportLogs(string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Suspension System Log Export");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("=" + new string('=', 50));

                lock (_logLock)
                {
                    foreach (var entry in _logEntries)
                    {
                        sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {entry.Level}: {entry.Message}");
                        if (!string.IsNullOrEmpty(entry.Source))
                        {
                            sb.AppendLine($"  Source: {entry.Source}");
                        }
                    }
                }

                File.WriteAllText(filePath, sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to export logs: {ex.Message}", "ProductionLogger");
                return false;
            }
        }

        /// <summary>
        /// Get filtered log entries based on level checkboxes
        /// </summary>
        public IEnumerable<LogEntry> GetFilteredLogs(bool showInfo, bool showWarning, bool showError, bool showCritical)
        {
            lock (_logLock)
            {
                foreach (var entry in _logEntries)
                {
                    bool shouldShow = entry.Level switch
                    {
                        LogLevel.Info => showInfo,
                        LogLevel.Warning => showWarning,
                        LogLevel.Error => showError,
                        LogLevel.Critical => showCritical,
                        _ => false
                    };

                    if (shouldShow)
                        yield return entry;
                }
            }
        }

        /// <summary>
        /// Get current log file path
        /// </summary>
        public string GetLogFilePath()
        {
            return _logFilePath;
        }

        /// <summary>
        /// Get number of log entries
        /// </summary>
        public int GetLogCount()
        {
            lock (_logLock)
            {
                return _logEntries.Count;
            }
        }

        private void WriteToFile(LogEntry entry)
        {
            try
            {
                string logLine = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {entry.Level}: {entry.Message}";
                if (!string.IsNullOrEmpty(entry.Source))
                {
                    logLine += $" (Source: {entry.Source})";
                }
                logLine += Environment.NewLine;

                File.AppendAllText(_logFilePath, logLine);
            }
            catch (Exception)
            {
                // Silently fail file writing to prevent infinite loops
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
