using System;
using System.IO;
using System.Text.Json;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Linear calibration class for 2-point calibration using known weights.
    /// Implements the industry-standard "Calibrate First, Then Tare" approach.
    /// </summary>
    public class LinearCalibration
    {
        public double Slope { get; set; }           // m in equation: kg = m * raw + b
        public double Intercept { get; set; }      // b in equation: kg = m * raw + b
        public DateTime CalibrationDate { get; set; }
        public bool IsValid { get; set; }
        public string Side { get; set; } = "";     // "Left" or "Right"
        
        // Calibration points for verification
        public CalibrationPoint Point1 { get; set; } = new CalibrationPoint();
        public CalibrationPoint Point2 { get; set; } = new CalibrationPoint();
        
        /// <summary>
        /// Fit linear calibration from 2 known weight points
        /// </summary>
        /// <param name="raw1">Raw ADC value for point 1</param>
        /// <param name="kg1">Known weight for point 1 (kg)</param>
        /// <param name="raw2">Raw ADC value for point 2</param>
        /// <param name="kg2">Known weight for point 2 (kg)</param>
        /// <returns>LinearCalibration object with calculated slope and intercept</returns>
        public static LinearCalibration FitTwoPoints(int raw1, double kg1, int raw2, double kg2)
        {
            if (raw2 == raw1)
                throw new ArgumentException("Raw values must be different for linear calibration");
            
            double slope = (kg2 - kg1) / (raw2 - raw1);
            double intercept = kg1 - slope * raw1;
            
            return new LinearCalibration 
            { 
                Slope = slope, 
                Intercept = intercept, 
                IsValid = true,
                CalibrationDate = DateTime.Now,
                Point1 = new CalibrationPoint { RawADC = raw1, KnownWeight = kg1 },
                Point2 = new CalibrationPoint { RawADC = raw2, KnownWeight = kg2 }
            };
        }
        
        /// <summary>
        /// Convert raw ADC value to calibrated weight (before tare)
        /// </summary>
        /// <param name="raw">Raw ADC value</param>
        /// <returns>Calibrated weight in kg</returns>
        public double RawToKg(int raw)
        {
            if (!IsValid)
                throw new InvalidOperationException("Calibration is not valid. Please calibrate first.");
                
            return Slope * raw + Intercept;
        }
        
        /// <summary>
        /// Verify calibration accuracy at a specific point
        /// </summary>
        /// <param name="raw">Raw ADC value</param>
        /// <param name="expectedKg">Expected weight in kg</param>
        /// <returns>Error percentage</returns>
        public double VerifyPoint(int raw, double expectedKg)
        {
            double calculatedKg = RawToKg(raw);
            double errorKg = Math.Abs(calculatedKg - expectedKg);
            double errorPercent = expectedKg > 0 ? (errorKg / expectedKg) * 100.0 : 0.0;
            return errorPercent;
        }
        
        /// <summary>
        /// Get calibration equation as string
        /// </summary>
        /// <returns>Equation string like "kg = 0.244 × raw - 36.6"</returns>
        public string GetEquationString()
        {
            if (!IsValid)
                return "Not calibrated";
                
            string slopeStr = Slope.ToString("F3");
            string interceptStr = Intercept.ToString("F0");
            
            if (Intercept >= 0)
                return $"kg = {slopeStr} × raw + {interceptStr}";
            else
                return $"kg = {slopeStr} × raw - {Math.Abs(Intercept):F0}";
        }
        
        /// <summary>
        /// Save calibration to JSON file
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        public void SaveToFile(string side)
        {
            Side = side;
            string filename = PathHelper.GetCalibrationPath(side); // Portable: in Data directory
            string jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filename, jsonString);
        }
        
        /// <summary>
        /// Load calibration from JSON file
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <returns>Loaded calibration or null if file doesn't exist</returns>
        public static LinearCalibration? LoadFromFile(string side)
        {
            string filename = PathHelper.GetCalibrationPath(side); // Portable: in Data directory
            if (!File.Exists(filename))
                return null;
                
            try
            {
                string jsonString = File.ReadAllText(filename);
                var calibration = JsonSerializer.Deserialize<LinearCalibration>(jsonString);
                return calibration;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading calibration file {filename}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Check if calibration file exists for a side
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <returns>True if calibration file exists</returns>
        public static bool CalibrationExists(string side)
        {
            string filename = PathHelper.GetCalibrationPath(side); // Portable: in Data directory
            return File.Exists(filename);
        }
        
        /// <summary>
        /// Delete calibration file for a side
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        public static void DeleteCalibration(string side)
        {
            string filename = PathHelper.GetCalibrationPath(side); // Portable: in Data directory
            if (File.Exists(filename))
            {
                try
                {
                    File.Delete(filename);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting calibration file {filename}: {ex.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Represents a single calibration point
    /// </summary>
    public class CalibrationPoint
    {
        public int RawADC { get; set; }
        public double KnownWeight { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
