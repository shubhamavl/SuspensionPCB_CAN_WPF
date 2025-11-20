using System;
using System.IO;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Helper class for portable file paths - all paths relative to executable directory
    /// </summary>
    public static class PathHelper
    {
        private static string? _applicationDirectory;
        
        /// <summary>
        /// Gets the directory where the executable is located
        /// </summary>
        public static string ApplicationDirectory
        {
            get
            {
                if (_applicationDirectory == null)
                {
                    // For single-file deployments, AppContext.BaseDirectory is the recommended approach
                    // It points to the directory containing the extracted files or the executable
                    string? baseDir = AppContext.BaseDirectory;
                    
                    if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
                    {
                        _applicationDirectory = baseDir;
                    }
                    else
                    {
                        // Fallback: Get the directory of the executable
                        string? exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        if (string.IsNullOrEmpty(exePath))
                        {
                            // Fallback for single-file deployments
                            exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        }
                        
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            _applicationDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                        }
                        else
                        {
                            _applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        }
                    }
                }
                return _applicationDirectory;
            }
        }
        
        /// <summary>
        /// Gets the path to the application data directory (portable, next to executable)
        /// </summary>
        public static string GetDataDirectory()
        {
            string dataDir = Path.Combine(ApplicationDirectory, "Data");
            if (!Directory.Exists(dataDir))
            {
                try
                {
                    Directory.CreateDirectory(dataDir);
                }
                catch { }
            }
            return dataDir;
        }
        
        /// <summary>
        /// Gets the path to the logs directory (portable, next to executable)
        /// </summary>
        public static string GetLogsDirectory()
        {
            string logsDir = Path.Combine(ApplicationDirectory, "Logs");
            if (!Directory.Exists(logsDir))
            {
                try
                {
                    Directory.CreateDirectory(logsDir);
                }
                catch { }
            }
            return logsDir;
        }
        
        /// <summary>
        /// Gets the path to the settings file (portable, next to executable)
        /// </summary>
        public static string GetSettingsPath()
        {
            return Path.Combine(ApplicationDirectory, "settings.json");
        }
        
        /// <summary>
        /// Gets the path to a calibration file (portable, in Data directory)
        /// </summary>
        public static string GetCalibrationPath(string side)
        {
            return Path.Combine(GetDataDirectory(), $"calibration_{side.ToLower()}.json");
        }
        
        /// <summary>
        /// Gets the path to the tare configuration file (portable, in Data directory)
        /// </summary>
        public static string GetTareConfigPath()
        {
            return Path.Combine(GetDataDirectory(), "tare_config.json");
        }
        
        /// <summary>
        /// Gets a path relative to the application directory
        /// </summary>
        public static string GetApplicationPath(string relativePath)
        {
            return Path.Combine(ApplicationDirectory, relativePath);
        }
    }
}

