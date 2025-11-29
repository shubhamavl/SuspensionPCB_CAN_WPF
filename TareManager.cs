using System;
using System.IO;
using System.Text.Json;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Tare manager for zero-out functionality.
    /// Works with calibrated weights to provide convenient daily zero-out.
    /// Supports mode-specific tares: Left/Right x Internal/ADS1115 (4 independent tares)
    /// </summary>
    public class TareManager
    {
        // Left side tares
        public double LeftBaselineKgInternal { get; private set; }
        public double LeftBaselineKgADS1115 { get; private set; }
        public bool LeftIsTaredInternal { get; private set; }
        public bool LeftIsTaredADS1115 { get; private set; }
        public DateTime LeftTareTimeInternal { get; private set; }
        public DateTime LeftTareTimeADS1115 { get; private set; }
        
        // Right side tares
        public double RightBaselineKgInternal { get; private set; }
        public double RightBaselineKgADS1115 { get; private set; }
        public bool RightIsTaredInternal { get; private set; }
        public bool RightIsTaredADS1115 { get; private set; }
        public DateTime RightTareTimeInternal { get; private set; }
        public DateTime RightTareTimeADS1115 { get; private set; }
        
        // Legacy properties for backward compatibility (deprecated, use mode-specific methods)
        [Obsolete("Use GetBaselineKg(string side, byte adcMode) instead")]
        public double LeftBaselineKg => LeftBaselineKgInternal;
        [Obsolete("Use GetBaselineKg(string side, byte adcMode) instead")]
        public double RightBaselineKg => RightBaselineKgInternal;
        [Obsolete("Use IsTared(string side, byte adcMode) instead")]
        public bool LeftIsTared => LeftIsTaredInternal;
        [Obsolete("Use IsTared(string side, byte adcMode) instead")]
        public bool RightIsTared => RightIsTaredInternal;
        [Obsolete("Use GetTareTime(string side, byte adcMode) instead")]
        public DateTime LeftTareTime => LeftTareTimeInternal;
        [Obsolete("Use GetTareTime(string side, byte adcMode) instead")]
        public DateTime RightTareTime => RightTareTimeInternal;
        
        /// <summary>
        /// Tare left side: remember current calibrated weight as zero baseline
        /// </summary>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void TareLeft(double currentCalibratedKg, byte adcMode)
        {
            // Validate baseline value
            if (double.IsNaN(currentCalibratedKg) || double.IsInfinity(currentCalibratedKg))
            {
                throw new ArgumentException($"Invalid tare baseline: {currentCalibratedKg} (NaN or Infinity)", nameof(currentCalibratedKg));
            }
            
            if (currentCalibratedKg < 0)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Negative tare baseline ({currentCalibratedKg} kg) - setting to 0");
                currentCalibratedKg = 0;
            }
            
            if (currentCalibratedKg > 1000)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Very large tare baseline ({currentCalibratedKg} kg) - may indicate calibration issue");
            }
            
            if (adcMode == 0) // Internal
            {
                LeftBaselineKgInternal = currentCalibratedKg;
                LeftIsTaredInternal = true;
                LeftTareTimeInternal = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Left Internal tare set: baseline={currentCalibratedKg:F3} kg");
            }
            else // ADS1115
            {
                LeftBaselineKgADS1115 = currentCalibratedKg;
                LeftIsTaredADS1115 = true;
                LeftTareTimeADS1115 = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Left ADS1115 tare set: baseline={currentCalibratedKg:F3} kg");
            }
        }
        
        /// <summary>
        /// Tare right side: remember current calibrated weight as zero baseline
        /// </summary>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void TareRight(double currentCalibratedKg, byte adcMode)
        {
            // Validate baseline value
            if (double.IsNaN(currentCalibratedKg) || double.IsInfinity(currentCalibratedKg))
            {
                throw new ArgumentException($"Invalid tare baseline: {currentCalibratedKg} (NaN or Infinity)", nameof(currentCalibratedKg));
            }
            
            if (currentCalibratedKg < 0)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Negative tare baseline ({currentCalibratedKg} kg) - setting to 0");
                currentCalibratedKg = 0;
            }
            
            if (currentCalibratedKg > 1000)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Very large tare baseline ({currentCalibratedKg} kg) - may indicate calibration issue");
            }
            
            if (adcMode == 0) // Internal
            {
                RightBaselineKgInternal = currentCalibratedKg;
                RightIsTaredInternal = true;
                RightTareTimeInternal = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Right Internal tare set: baseline={currentCalibratedKg:F3} kg");
            }
            else // ADS1115
            {
                RightBaselineKgADS1115 = currentCalibratedKg;
                RightIsTaredADS1115 = true;
                RightTareTimeADS1115 = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[TareManager] Right ADS1115 tare set: baseline={currentCalibratedKg:F3} kg");
            }
        }
        
        /// <summary>
        /// Tare a side: remember current calibrated weight as zero baseline
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg</param>
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
                LeftBaselineKgInternal = 0;
            }
            else // ADS1115
            {
                LeftIsTaredADS1115 = false;
                LeftBaselineKgADS1115 = 0;
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
                RightBaselineKgInternal = 0;
            }
            else // ADS1115
            {
                RightIsTaredADS1115 = false;
                RightBaselineKgADS1115 = 0;
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
        /// Apply tare: subtract baseline from current calibrated weight
        /// </summary>
        /// <param name="calibratedKg">Current calibrated weight in kg</param>
        /// <param name="isLeft">True for left side, false for right side</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Tared weight (never negative)</returns>
        public double ApplyTare(double calibratedKg, bool isLeft, byte adcMode)
        {
            double baseline = 0;
            bool isTared = false;
            
            if (isLeft)
            {
                if (adcMode == 0) // Internal
                {
                    baseline = LeftBaselineKgInternal;
                    isTared = LeftIsTaredInternal;
                }
                else // ADS1115
                {
                    baseline = LeftBaselineKgADS1115;
                    isTared = LeftIsTaredADS1115;
                }
            }
            else
            {
                if (adcMode == 0) // Internal
                {
                    baseline = RightBaselineKgInternal;
                    isTared = RightIsTaredInternal;
                }
                else // ADS1115
                {
                    baseline = RightBaselineKgADS1115;
                    isTared = RightIsTaredADS1115;
                }
            }
            
            if (isTared)
            {
                double taredWeight = Math.Max(0, calibratedKg - baseline);
                System.Diagnostics.Debug.WriteLine($"[TareManager] ApplyTare: side={(isLeft ? "Left" : "Right")}, mode={(adcMode == 0 ? "Internal" : "ADS1115")}, calibrated={calibratedKg:F3} kg, baseline={baseline:F3} kg, result={taredWeight:F3} kg");
                return taredWeight;
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
        /// Get baseline weight for a side and ADC mode
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Baseline weight in kg</returns>
        public double GetBaselineKg(string side, byte adcMode)
        {
            if (side == "Left")
                return adcMode == 0 ? LeftBaselineKgInternal : LeftBaselineKgADS1115;
            else
                return adcMode == 0 ? RightBaselineKgInternal : RightBaselineKgADS1115;
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
                double baseline = GetBaselineKg(side, adcMode);
                return $"✓ Tared (baseline: {baseline:F0}kg)";
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
                LeftBaselineKgInternal = LeftBaselineKgInternal,
                LeftBaselineKgADS1115 = LeftBaselineKgADS1115,
                LeftIsTaredInternal = LeftIsTaredInternal,
                LeftIsTaredADS1115 = LeftIsTaredADS1115,
                LeftTareTimeInternal = LeftTareTimeInternal,
                LeftTareTimeADS1115 = LeftTareTimeADS1115,
                
                // Right side
                RightBaselineKgInternal = RightBaselineKgInternal,
                RightBaselineKgADS1115 = RightBaselineKgADS1115,
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
                    // Load new format (mode-specific)
                    if (tareData.LeftBaselineKgInternal != 0 || tareData.LeftBaselineKgADS1115 != 0 ||
                        tareData.RightBaselineKgInternal != 0 || tareData.RightBaselineKgADS1115 != 0 ||
                        tareData.LeftIsTaredInternal || tareData.LeftIsTaredADS1115 ||
                        tareData.RightIsTaredInternal || tareData.RightIsTaredADS1115)
                    {
                        // New format with mode-specific tares
                        LeftBaselineKgInternal = tareData.LeftBaselineKgInternal;
                        LeftBaselineKgADS1115 = tareData.LeftBaselineKgADS1115;
                        LeftIsTaredInternal = tareData.LeftIsTaredInternal;
                        LeftIsTaredADS1115 = tareData.LeftIsTaredADS1115;
                        LeftTareTimeInternal = tareData.LeftTareTimeInternal;
                        LeftTareTimeADS1115 = tareData.LeftTareTimeADS1115;
                        
                        RightBaselineKgInternal = tareData.RightBaselineKgInternal;
                        RightBaselineKgADS1115 = tareData.RightBaselineKgADS1115;
                        RightIsTaredInternal = tareData.RightIsTaredInternal;
                        RightIsTaredADS1115 = tareData.RightIsTaredADS1115;
                        RightTareTimeInternal = tareData.RightTareTimeInternal;
                        RightTareTimeADS1115 = tareData.RightTareTimeADS1115;
                    }
                    else
                    {
                        // Legacy format - migrate to Internal mode
                        LeftBaselineKgInternal = tareData.LeftBaselineKg;
                        LeftIsTaredInternal = tareData.LeftIsTared;
                        LeftTareTimeInternal = tareData.LeftTareTime;
                        
                        RightBaselineKgInternal = tareData.RightBaselineKg;
                        RightIsTaredInternal = tareData.RightIsTared;
                        RightTareTimeInternal = tareData.RightTareTime;
                    }
                    
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
    /// Data structure for tare configuration persistence
    /// Supports both new mode-specific format and legacy format for backward compatibility
    /// </summary>
    public class TareData
    {
        // New format: Mode-specific tares
        public double LeftBaselineKgInternal { get; set; }
        public double LeftBaselineKgADS1115 { get; set; }
        public bool LeftIsTaredInternal { get; set; }
        public bool LeftIsTaredADS1115 { get; set; }
        public DateTime LeftTareTimeInternal { get; set; }
        public DateTime LeftTareTimeADS1115 { get; set; }
        
        public double RightBaselineKgInternal { get; set; }
        public double RightBaselineKgADS1115 { get; set; }
        public bool RightIsTaredInternal { get; set; }
        public bool RightIsTaredADS1115 { get; set; }
        public DateTime RightTareTimeInternal { get; set; }
        public DateTime RightTareTimeADS1115 { get; set; }
        
        // Legacy format: For backward compatibility when loading old config files
        public double LeftBaselineKg { get; set; }
        public double RightBaselineKg { get; set; }
        public bool LeftIsTared { get; set; }
        public bool RightIsTared { get; set; }
        public DateTime LeftTareTime { get; set; }
        public DateTime RightTareTime { get; set; }
        
        public DateTime SaveTime { get; set; }
    }
}
