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
        
        public bool IsLogging => _isLogging;
        
        public DataLogger()
        {
            // Create timestamped log file
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logFilePath = $"suspension_log_{timestamp}.csv";
        }
        
        /// <summary>
        /// Start logging to CSV file
        /// </summary>
        public bool StartLogging()
        {
            try
            {
                lock (_logLock)
                {
                    if (_isLogging)
                        return true; // Already logging
                    
                    // Create CSV header
                    string header = "Timestamp,Side,RawADC,CalibratedKg,TaredKg,TareBaseline,CalSlope,CalIntercept,ADCMode";
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
                _isLogging = false;
                System.Diagnostics.Debug.WriteLine("Data logging stopped");
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
            if (!_isLogging)
                return;
                
            try
            {
                lock (_logLock)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    string line = $"{timestamp},{side},{rawADC},{calibratedKg:F3},{taredKg:F3}," +
                                 $"{tareBaseline:F3},{calSlope:F6},{calIntercept:F3},{adcMode}";
                    
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
