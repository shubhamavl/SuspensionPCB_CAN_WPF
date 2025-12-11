using System;
using System.IO;

namespace SuspensionPCB_CAN_WPF.Core
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
                    // Prefer the real executable directory (works for single-file publish)
                    string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        string? exeDir = Path.GetDirectoryName(exePath);
                        if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
                        {
                            _applicationDirectory = exeDir;
                        }
                    }

                    // Fallback: AppContext.BaseDirectory (may be temp extraction in single-file)
                    if (_applicationDirectory == null)
                    {
                        string? baseDir = AppContext.BaseDirectory;
                        if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
                        {
                            _applicationDirectory = baseDir;
                        }
                    }

                    // Last resort
                    _applicationDirectory ??= AppDomain.CurrentDomain.BaseDirectory;
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
        /// Gets the path to the update working directory (portable, next to executable)
        /// </summary>
        public static string GetUpdateDirectory()
        {
            string updateDir = Path.Combine(ApplicationDirectory, "Update");
            if (!Directory.Exists(updateDir))
            {
                try
                {
                    Directory.CreateDirectory(updateDir);
                }
                catch
                {
                    // Ignore directory creation failures here; caller will handle IO errors.
                }
            }
            return updateDir;
        }

        /// <summary>
        /// Gets the expected path to the external updater executable that performs file replacement.
        /// </summary>
        public static string GetUpdaterExecutablePath()
        {
            return Path.Combine(ApplicationDirectory, "SuspensionPCB_Updater.exe");
        }
        
        /// <summary>
        /// Gets the path to a calibration file (portable, in Data directory)
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public static string GetCalibrationPath(string side, byte adcMode)
        {
            string modeSuffix = adcMode == 0 ? "internal" : "ads1115";
            return Path.Combine(GetDataDirectory(), $"calibration_{side.ToLower()}_{modeSuffix}.json");
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

