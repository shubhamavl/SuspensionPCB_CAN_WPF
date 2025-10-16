using System;
using System.IO;
using System.Text.Json;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Tare manager for zero-out functionality.
    /// Works with calibrated weights to provide convenient daily zero-out.
    /// </summary>
    public class TareManager
    {
        public double LeftBaselineKg { get; private set; }
        public double RightBaselineKg { get; private set; }
        public bool LeftIsTared { get; private set; }
        public bool RightIsTared { get; private set; }
        public DateTime LeftTareTime { get; private set; }
        public DateTime RightTareTime { get; private set; }
        
        /// <summary>
        /// Tare left side: remember current calibrated weight as zero baseline
        /// </summary>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg</param>
        public void TareLeft(double currentCalibratedKg)
        {
            LeftBaselineKg = currentCalibratedKg;
            LeftIsTared = true;
            LeftTareTime = DateTime.Now;
        }
        
        /// <summary>
        /// Tare right side: remember current calibrated weight as zero baseline
        /// </summary>
        /// <param name="currentCalibratedKg">Current calibrated weight in kg</param>
        public void TareRight(double currentCalibratedKg)
        {
            RightBaselineKg = currentCalibratedKg;
            RightIsTared = true;
            RightTareTime = DateTime.Now;
        }
        
        /// <summary>
        /// Reset left tare
        /// </summary>
        public void ResetLeft()
        {
            LeftIsTared = false;
            LeftBaselineKg = 0;
        }
        
        /// <summary>
        /// Reset right tare
        /// </summary>
        public void ResetRight()
        {
            RightIsTared = false;
            RightBaselineKg = 0;
        }
        
        /// <summary>
        /// Reset both tares
        /// </summary>
        public void ResetBoth()
        {
            ResetLeft();
            ResetRight();
        }
        
        /// <summary>
        /// Apply tare: subtract baseline from current calibrated weight
        /// </summary>
        /// <param name="calibratedKg">Current calibrated weight in kg</param>
        /// <param name="isLeft">True for left side, false for right side</param>
        /// <returns>Tared weight (never negative)</returns>
        public double ApplyTare(double calibratedKg, bool isLeft)
        {
            if (isLeft && LeftIsTared)
                return Math.Max(0, calibratedKg - LeftBaselineKg);
            else if (!isLeft && RightIsTared)
                return Math.Max(0, calibratedKg - RightBaselineKg);
            else
                return calibratedKg; // Not tared, return as-is
        }
        
        /// <summary>
        /// Get tare status text for display
        /// </summary>
        /// <param name="isLeft">True for left side, false for right side</param>
        /// <returns>Status text</returns>
        public string GetTareStatusText(bool isLeft)
        {
            if (isLeft)
            {
                if (LeftIsTared)
                    return $"✓ Tared (baseline: {LeftBaselineKg:F1}kg)";
                else
                    return "- Not Tared";
            }
            else
            {
                if (RightIsTared)
                    return $"✓ Tared (baseline: {RightBaselineKg:F1}kg)";
                else
                    return "- Not Tared";
            }
        }
        
        /// <summary>
        /// Get tare time for display
        /// </summary>
        /// <param name="isLeft">True for left side, false for right side</param>
        /// <returns>Tare time or empty string if not tared</returns>
        public string GetTareTimeText(bool isLeft)
        {
            if (isLeft && LeftIsTared)
                return LeftTareTime.ToString("HH:mm:ss");
            else if (!isLeft && RightIsTared)
                return RightTareTime.ToString("HH:mm:ss");
            else
                return "";
        }
        
        /// <summary>
        /// Save tare state to JSON file
        /// </summary>
        public void SaveToFile()
        {
            var tareData = new TareData
            {
                LeftBaselineKg = LeftBaselineKg,
                RightBaselineKg = RightBaselineKg,
                LeftIsTared = LeftIsTared,
                RightIsTared = RightIsTared,
                LeftTareTime = LeftTareTime,
                RightTareTime = RightTareTime,
                SaveTime = DateTime.Now
            };
            
            string jsonString = JsonSerializer.Serialize(tareData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("tare_config.json", jsonString);
        }
        
        /// <summary>
        /// Load tare state from JSON file
        /// </summary>
        /// <returns>True if loaded successfully</returns>
        public bool LoadFromFile()
        {
            if (!File.Exists("tare_config.json"))
                return false;
                
            try
            {
                string jsonString = File.ReadAllText("tare_config.json");
                var tareData = JsonSerializer.Deserialize<TareData>(jsonString);
                
                if (tareData != null)
                {
                    LeftBaselineKg = tareData.LeftBaselineKg;
                    RightBaselineKg = tareData.RightBaselineKg;
                    LeftIsTared = tareData.LeftIsTared;
                    RightIsTared = tareData.RightIsTared;
                    LeftTareTime = tareData.LeftTareTime;
                    RightTareTime = tareData.RightTareTime;
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
            if (File.Exists("tare_config.json"))
            {
                try
                {
                    File.Delete("tare_config.json");
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
    /// </summary>
    public class TareData
    {
        public double LeftBaselineKg { get; set; }
        public double RightBaselineKg { get; set; }
        public bool LeftIsTared { get; set; }
        public bool RightIsTared { get; set; }
        public DateTime LeftTareTime { get; set; }
        public DateTime RightTareTime { get; set; }
        public DateTime SaveTime { get; set; }
    }
}
