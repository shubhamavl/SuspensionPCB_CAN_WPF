using System;
using System.IO;
using System.Text;
using System.Globalization;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// CSV data logger for suspension system data
    /// </summary>
    public class DataLogger
    {
        private string _logFilePath;
        private bool _isLogging = false;
        private readonly object _logLock = new object();
        
        // System status tracking
        private byte _lastSystemStatus = 0;
        private byte _lastErrorFlags = 0;
        private DateTime _lastStatusTimestamp = DateTime.MinValue;
        
        public bool IsLogging 
        { 
            get 
            { 
                lock (_logLock) 
                { 
                    return _isLogging; 
                } 
            } 
        }
        
        public DataLogger()
        {
            // File path will be created when logging starts
            _logFilePath = "";
        }
        
        /// <summary>
        /// Start logging to CSV file (creates new timestamped file each time)
        /// </summary>
        public bool StartLogging()
        {
            try
            {
                lock (_logLock)
                {
                    if (_isLogging)
                        return true; // Already logging
                    
                    // Create new timestamped log file each time logging starts
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string baseDir = SettingsManager.Instance.Settings.SaveDirectory;
                    try
                    {
                        if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
                    }
                    catch { }
                    _logFilePath = Path.Combine(baseDir, $"suspension_log_{timestamp}.csv");
                    
                    // Create CSV header with system status fields
                    string header = "Timestamp,Side,RawADC,CalibratedKg,TaredKg,TareBaseline,CalSlope,CalIntercept,ADCMode,SystemStatus,ErrorFlags,StatusTimestamp";
                    File.WriteAllText(_logFilePath, header + Environment.NewLine);
                    
                    _isLogging = true;
                    System.Diagnostics.Debug.WriteLine($"Data logging started: {_logFilePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting data logging: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Stop logging
        /// </summary>
        public void StopLogging()
        {
            lock (_logLock)
            {
                if (!_isLogging)
                {
                    System.Diagnostics.Debug.WriteLine("Data logging already stopped");
                    return; // Already stopped
                }
                
                _isLogging = false;
                System.Diagnostics.Debug.WriteLine($"Data logging stopped. File: {_logFilePath}");
            }
        }
        
        /// <summary>
        /// Update system status for logging
        /// </summary>
        /// <param name="systemStatus">System status (0=OK, 1=Warning, 2=Error)</param>
        /// <param name="errorFlags">Error flags</param>
        public void UpdateSystemStatus(byte systemStatus, byte errorFlags)
        {
            lock (_logLock)
            {
                _lastSystemStatus = systemStatus;
                _lastErrorFlags = errorFlags;
                _lastStatusTimestamp = DateTime.Now;
            }
        }
        
        /// <summary>
        /// Log a data point
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="rawADC">Raw ADC value</param>
        /// <param name="calibratedKg">Calibrated weight in kg</param>
        /// <param name="taredKg">Tared weight in kg</param>
        /// <param name="tareBaseline">Tare baseline in kg</param>
        /// <param name="calSlope">Calibration slope</param>
        /// <param name="calIntercept">Calibration intercept</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void LogDataPoint(string side, int rawADC, double calibratedKg, double taredKg, 
                               double tareBaseline, double calSlope, double calIntercept, byte adcMode)
        {
            // Early exit check (outside lock for performance)
            if (!_isLogging)
                return;
                
            try
            {
                lock (_logLock)
                {
                    // Double-check inside lock to prevent race condition
                    if (!_isLogging)
                        return;
                    
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    string statusTimestamp = _lastStatusTimestamp != DateTime.MinValue 
                        ? _lastStatusTimestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        : "";
                    
                    string line = $"{timestamp},{side},{rawADC},{calibratedKg:F3},{taredKg:F3}," +
                                 $"{tareBaseline:F3},{calSlope:F6},{calIntercept:F3},{adcMode}," +
                                 $"{_lastSystemStatus},{_lastErrorFlags},{statusTimestamp}";
                    
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error logging data point: {ex.Message}");
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
        /// Export current session to a new CSV file
        /// </summary>
        /// <param name="exportPath">Path for exported file</param>
        /// <returns>True if successful</returns>
        public bool ExportToCSV(string exportPath)
        {
            try
            {
                if (!File.Exists(_logFilePath))
                    return false;
                    
                File.Copy(_logFilePath, exportPath, true);
                System.Diagnostics.Debug.WriteLine($"Data exported to: {exportPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting data: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get log file size in bytes
        /// </summary>
        public long GetLogFileSize()
        {
            try
            {
                if (File.Exists(_logFilePath))
                    return new FileInfo(_logFilePath).Length;
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Get number of lines in log file
        /// </summary>
        public int GetLogLineCount()
        {
            try
            {
                if (File.Exists(_logFilePath))
                    return File.ReadAllLines(_logFilePath).Length;
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
