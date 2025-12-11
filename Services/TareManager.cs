using System;
using System.IO;
using System.Text.Json;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Services
{
    /// <summary>
    /// Tare manager for calibrator weight offset compensation.
    /// When calibrator weight is removed after calibration, the scale shows negative weight.
    /// Tare stores this negative offset and adds it to all future readings to compensate.
    /// Supports mode-specific offsets: Left/Right x Internal/ADS1115 (4 independent offsets)
    /// </summary>
    public class TareManager
    {
        // Left side offsets (negative values from calibrator removal)
        public double LeftOffsetKgInternal { get; private set; }
        public double LeftOffsetKgADS1115 { get; private set; }
        public bool LeftIsTaredInternal { get; private set; }
        public bool LeftIsTaredADS1115 { get; private set; }
        public DateTime LeftTareTimeInternal { get; private set; }
        public DateTime LeftTareTimeADS1115 { get; private set; }
        
        // Right side offsets (negative values from calibrator removal)
        public double RightOffsetKgInternal { get; private set; }
        public double RightOffsetKgADS1115 { get; private set; }
        public bool RightIsTaredInternal { get; private set; }
        public bool RightIsTaredADS1115 { get; private set; }
        public DateTime RightTareTimeInternal { get; private set; }
        public DateTime RightTareTimeADS1115 { get; private set; }
        
        /// <summary>
        /// Tare left side: store positive offset from calibrator removal for compensation
        /// When calibrator is removed, weight goes negative (e.g., -23 kg).
        /// Tare stores the absolute value (e.g., +23 kg) to compensate all future readings.
        /// </summary>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg (must be negative)</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void TareLeft(double currentCalibratedKg, byte adcMode)
        {
            // Validate offset value
            if (double.IsNaN(currentCalibratedKg) || double.IsInfinity(currentCalibratedKg))
            {
                throw new ArgumentException($"Invalid tare offset: {currentCalibratedKg} (NaN or Infinity)", nameof(currentCalibratedKg));
            }
            
            // Only allow negative values (calibrator removed scenario)
            if (currentCalibratedKg >= 0)
            {
                throw new ArgumentException($"Tare only works with negative weight (calibrator removed). Current weight: {currentCalibratedKg:F3} kg", nameof(currentCalibratedKg));
            }
            
            // Store the absolute value (positive) as offset for compensation
            double offset = Math.Abs(currentCalibratedKg);
            
            if (adcMode == 0) // Internal
            {
                LeftOffsetKgInternal = offset; // Store positive offset
                LeftIsTaredInternal = true;
                LeftTareTimeInternal = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Left Internal tare set: offset=+{offset:F3} kg (from {currentCalibratedKg:F3} kg)");
            }
            else // ADS1115
            {
                LeftOffsetKgADS1115 = offset; // Store positive offset
                LeftIsTaredADS1115 = true;
                LeftTareTimeADS1115 = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Left ADS1115 tare set: offset=+{offset:F3} kg (from {currentCalibratedKg:F3} kg)");
            }
        }
        
        /// <summary>
        /// Tare right side: store positive offset from calibrator removal for compensation
        /// When calibrator is removed, weight goes negative (e.g., -23 kg).
        /// Tare stores the absolute value (e.g., +23 kg) to compensate all future readings.
        /// </summary>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg (must be negative)</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void TareRight(double currentCalibratedKg, byte adcMode)
        {
            // Validate offset value
            if (double.IsNaN(currentCalibratedKg) || double.IsInfinity(currentCalibratedKg))
            {
                throw new ArgumentException($"Invalid tare offset: {currentCalibratedKg} (NaN or Infinity)", nameof(currentCalibratedKg));
            }
            
            // Only allow negative values (calibrator removed scenario)
            if (currentCalibratedKg >= 0)
            {
                throw new ArgumentException($"Tare only works with negative weight (calibrator removed). Current weight: {currentCalibratedKg:F3} kg", nameof(currentCalibratedKg));
            }
            
            // Store the absolute value (positive) as offset for compensation
            double offset = Math.Abs(currentCalibratedKg);
            
            if (adcMode == 0) // Internal
            {
                RightOffsetKgInternal = offset; // Store positive offset
                RightIsTaredInternal = true;
                RightTareTimeInternal = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Right Internal tare set: offset=+{offset:F3} kg (from {currentCalibratedKg:F3} kg)");
            }
            else // ADS1115
            {
                RightOffsetKgADS1115 = offset; // Store positive offset
                RightIsTaredADS1115 = true;
                RightTareTimeADS1115 = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Right ADS1115 tare set: offset=+{offset:F3} kg (from {currentCalibratedKg:F3} kg)");
            }
        }
        
        /// <summary>
        /// Tare a side: store negative offset from calibrator removal for compensation
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg (must be negative)</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void Tare(string side, double currentCalibratedKg, byte adcMode)
        {
            if (side == "Left")
                TareLeft(currentCalibratedKg, adcMode);
            else
                TareRight(currentCalibratedKg, adcMode);
        }
        
        /// <summary>
        /// Reset left tare for specific ADC mode
        /// </summary>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void ResetLeft(byte adcMode)
        {
            if (adcMode == 0) // Internal
            {
                LeftIsTaredInternal = false;
                LeftOffsetKgInternal = 0;
            }
            else // ADS1115
            {
                LeftIsTaredADS1115 = false;
                LeftOffsetKgADS1115 = 0;
            }
        }
        
        /// <summary>
        /// Reset right tare for specific ADC mode
        /// </summary>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void ResetRight(byte adcMode)
        {
            if (adcMode == 0) // Internal
            {
                RightIsTaredInternal = false;
                RightOffsetKgInternal = 0;
            }
            else // ADS1115
            {
                RightIsTaredADS1115 = false;
                RightOffsetKgADS1115 = 0;
            }
        }
        
        /// <summary>
        /// Reset tare for a side and ADC mode
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void Reset(string side, byte adcMode)
        {
            if (side == "Left")
                ResetLeft(adcMode);
            else
                ResetRight(adcMode);
        }
        
        /// <summary>
        /// Reset all tares (all modes for both sides)
        /// </summary>
        public void ResetAll()
        {
            ResetLeft(0);
            ResetLeft(1);
            ResetRight(0);
            ResetRight(1);
        }
        
        // Legacy methods for backward compatibility
        [Obsolete("Use ResetLeft(byte adcMode) instead")]
        public void ResetLeft()
        {
            ResetLeft(0); // Default to Internal for backward compatibility
        }
        
        [Obsolete("Use ResetRight(byte adcMode) instead")]
        public void ResetRight()
        {
            ResetRight(0); // Default to Internal for backward compatibility
        }
        
        [Obsolete("Use ResetAll() instead")]
        public void ResetBoth()
        {
            ResetAll();
        }
        
        /// <summary>
        /// Apply tare: add positive offset to current calibrated weight to compensate for calibrator removal
        /// Example: If offset is +23 kg and calibrated weight is -23 kg, result = 0 kg
        /// If calibrated weight is -13 kg (added 10 kg), result = -13 + 23 = 10 kg
        /// </summary>
        /// <param name="calibratedKg">Current calibrated weight in kg</param>
        /// <param name="isLeft">True for left side, false for right side</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Compensated weight (calibrated + offset, where offset is positive)</returns>
        public double ApplyTare(double calibratedKg, bool isLeft, byte adcMode)
        {
            double offset = 0;
            bool isTared = false;
            
            if (isLeft)
            {
                if (adcMode == 0) // Internal
                {
                    offset = LeftOffsetKgInternal;
                    isTared = LeftIsTaredInternal;
                }
                else // ADS1115
                {
                    offset = LeftOffsetKgADS1115;
                    isTared = LeftIsTaredADS1115;
                }
            }
            else
            {
                if (adcMode == 0) // Internal
                {
                    offset = RightOffsetKgInternal;
                    isTared = RightIsTaredInternal;
                }
                else // ADS1115
                {
                    offset = RightOffsetKgADS1115;
                    isTared = RightIsTaredADS1115;
                }
            }
            
            if (isTared)
            {
                // Add positive offset to compensate for calibrator removal
                // Offset is stored as positive value (absolute of negative weight)
                double compensatedWeight = calibratedKg + offset;
                System.Diagnostics.Debug.WriteLine($"[TareManager] ApplyTare: side={(isLeft ? "Left" : "Right")}, mode={(adcMode == 0 ? "Internal" : "ADS1115")}, calibrated={calibratedKg:F3} kg, offset=+{offset:F3} kg, result={compensatedWeight:F3} kg");
                return compensatedWeight;
            }
            else
            {
                return calibratedKg; // Not tared, return as-is
            }
        }
        
        /// <summary>
        /// Check if a side and ADC mode is tared
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>True if tared</returns>
        public bool IsTared(string side, byte adcMode)
        {
            if (side == "Left")
                return adcMode == 0 ? LeftIsTaredInternal : LeftIsTaredADS1115;
            else
                return adcMode == 0 ? RightIsTaredInternal : RightIsTaredADS1115;
        }
        
        /// <summary>
        /// Get offset weight for a side and ADC mode
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Offset weight in kg (positive value, stored as absolute of negative weight)</returns>
        public double GetOffsetKg(string side, byte adcMode)
        {
            if (side == "Left")
                return adcMode == 0 ? LeftOffsetKgInternal : LeftOffsetKgADS1115;
            else
                return adcMode == 0 ? RightOffsetKgInternal : RightOffsetKgADS1115;
        }
        
        /// <summary>
        /// Get baseline weight for a side and ADC mode (legacy method, returns offset)
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Offset weight in kg</returns>
        [Obsolete("Use GetOffsetKg instead")]
        public double GetBaselineKg(string side, byte adcMode)
        {
            return GetOffsetKg(side, adcMode);
        }
        
        /// <summary>
        /// Get tare time for a side and ADC mode
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Tare time or DateTime.MinValue if not tared</returns>
        public DateTime GetTareTime(string side, byte adcMode)
        {
            if (side == "Left")
                return adcMode == 0 ? LeftTareTimeInternal : LeftTareTimeADS1115;
            else
                return adcMode == 0 ? RightTareTimeInternal : RightTareTimeADS1115;
        }
        
        /// <summary>
        /// Get tare status text for display
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Status text</returns>
        public string GetTareStatusText(string side, byte adcMode)
        {
            bool isTared = IsTared(side, adcMode);
            if (isTared)
            {
                double offset = GetOffsetKg(side, adcMode);
                return $"✓ Tared (offset: {offset:F0}kg)";
            }
            else
            {
                return "- Not Tared";
            }
        }
        
        /// <summary>
        /// Get tare time for display
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Tare time or empty string if not tared</returns>
        public string GetTareTimeText(string side, byte adcMode)
        {
            if (IsTared(side, adcMode))
            {
                DateTime tareTime = GetTareTime(side, adcMode);
                return tareTime.ToString("HH:mm:ss");
            }
            else
            {
                return "";
            }
        }
        
        // Legacy methods for backward compatibility
        [Obsolete("Use GetTareStatusText(string side, byte adcMode) instead")]
        public string GetTareStatusText(bool isLeft)
        {
            return GetTareStatusText(isLeft ? "Left" : "Right", 0); // Default to Internal
        }
        
        [Obsolete("Use GetTareTimeText(string side, byte adcMode) instead")]
        public string GetTareTimeText(bool isLeft)
        {
            return GetTareTimeText(isLeft ? "Left" : "Right", 0); // Default to Internal
        }
        
        /// <summary>
        /// Save tare state to JSON file
        /// </summary>
        public void SaveToFile()
        {
            var tareData = new TareData
            {
                // Left side
                LeftOffsetKgInternal = LeftOffsetKgInternal,
                LeftOffsetKgADS1115 = LeftOffsetKgADS1115,
                LeftIsTaredInternal = LeftIsTaredInternal,
                LeftIsTaredADS1115 = LeftIsTaredADS1115,
                LeftTareTimeInternal = LeftTareTimeInternal,
                LeftTareTimeADS1115 = LeftTareTimeADS1115,
                
                // Right side
                RightOffsetKgInternal = RightOffsetKgInternal,
                RightOffsetKgADS1115 = RightOffsetKgADS1115,
                RightIsTaredInternal = RightIsTaredInternal,
                RightIsTaredADS1115 = RightIsTaredADS1115,
                RightTareTimeInternal = RightTareTimeInternal,
                RightTareTimeADS1115 = RightTareTimeADS1115,
                
                SaveTime = DateTime.Now
            };
            
            string path = PathHelper.GetTareConfigPath(); // Portable: in Data directory
            string jsonString = JsonSerializer.Serialize(tareData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, jsonString);
        }
        
        /// <summary>
        /// Load tare state from JSON file
        /// </summary>
        /// <returns>True if loaded successfully</returns>
        public bool LoadFromFile()
        {
            string path = PathHelper.GetTareConfigPath(); // Portable: in Data directory
            if (!File.Exists(path))
                return false;
                
            try
            {
                string jsonString = File.ReadAllText(path);
                var tareData = JsonSerializer.Deserialize<TareData>(jsonString);
                
                if (tareData != null)
                {
                    // Load offsets
                    LeftOffsetKgInternal = tareData.LeftOffsetKgInternal;
                    LeftOffsetKgADS1115 = tareData.LeftOffsetKgADS1115;
                    LeftIsTaredInternal = tareData.LeftIsTaredInternal;
                    LeftIsTaredADS1115 = tareData.LeftIsTaredADS1115;
                    LeftTareTimeInternal = tareData.LeftTareTimeInternal;
                    LeftTareTimeADS1115 = tareData.LeftTareTimeADS1115;
                    
                    RightOffsetKgInternal = tareData.RightOffsetKgInternal;
                    RightOffsetKgADS1115 = tareData.RightOffsetKgADS1115;
                    RightIsTaredInternal = tareData.RightIsTaredInternal;
                    RightIsTaredADS1115 = tareData.RightIsTaredADS1115;
                    RightTareTimeInternal = tareData.RightTareTimeInternal;
                    RightTareTimeADS1115 = tareData.RightTareTimeADS1115;
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tare config: {ex.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Delete tare configuration file
        /// </summary>
        public void DeleteConfig()
        {
            string path = PathHelper.GetTareConfigPath(); // Portable: in Data directory
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting tare config: {ex.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Data structure for tare configuration persistence (offset-based)
    /// </summary>
    public class TareData
    {
        // Mode-specific offsets
        public double LeftOffsetKgInternal { get; set; }
        public double LeftOffsetKgADS1115 { get; set; }
        public bool LeftIsTaredInternal { get; set; }
        public bool LeftIsTaredADS1115 { get; set; }
        public DateTime LeftTareTimeInternal { get; set; }
        public DateTime LeftTareTimeADS1115 { get; set; }
        
        public double RightOffsetKgInternal { get; set; }
        public double RightOffsetKgADS1115 { get; set; }
        public bool RightIsTaredInternal { get; set; }
        public bool RightIsTaredADS1115 { get; set; }
        public DateTime RightTareTimeInternal { get; set; }
        public DateTime RightTareTimeADS1115 { get; set; }
        
        public DateTime SaveTime { get; set; }
    }
}
