using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuspensionPCB_CAN_WPF.Core
{
    /// <summary>
    /// Calibration mode enumeration
    /// </summary>
    public enum CalibrationMode
    {
        Regression,  // Global linear regression (default, backward compatible)
        Piecewise    // Piecewise linear interpolation between adjacent points
    }
    
    /// <summary>
    /// Represents a single segment in piecewise linear calibration
    /// </summary>
    public class CalibrationSegment
    {
        public int XMin { get; set; }      // Minimum ADC value for this segment (inclusive)
        public int XMax { get; set; }      // Maximum ADC value for this segment (inclusive)
        public double Slope { get; set; }  // Slope for this segment: kg = Slope * raw + Intercept
        public double Intercept { get; set; } // Intercept for this segment
    }
    
    /// <summary>
    /// Linear calibration class for multi-point calibration using least-squares linear regression.
    /// Implements the industry-standard "Calibrate First, Then Tare" approach.
    /// Supports both global regression and piecewise linear interpolation.
    /// </summary>
    public class LinearCalibration
    {
        public double Slope { get; set; }           // m in equation: kg = m * raw + b (for regression mode)
        public double Intercept { get; set; }      // b in equation: kg = m * raw + b (for regression mode)
        public DateTime CalibrationDate { get; set; }
        public bool IsValid { get; set; }
        public string Side { get; set; } = "";     // "Left" or "Right"
        public byte ADCMode { get; set; } = 0;    // 0=Internal, 1=ADS1115
        
        // Calibration mode: Regression (default) or Piecewise
        public CalibrationMode Mode { get; set; } = CalibrationMode.Regression;
        
        // Piecewise segments (for Piecewise mode)
        public List<CalibrationSegment> Segments { get; set; } = new List<CalibrationSegment>();
        
        // All calibration points used for fitting
        public List<CalibrationPoint> Points { get; set; } = new List<CalibrationPoint>();
        
        // Quality metrics
        public double R2 { get; set; }              // Coefficient of determination (0.0 to 1.0)
        public double MaxErrorPercent { get; set; } // Maximum error percentage across all points
        
        /// <summary>
        /// Fit linear calibration from multiple points using least-squares linear regression
        /// </summary>
        /// <param name="points">List of calibration points (minimum 1 point required)</param>
        /// <returns>LinearCalibration object with calculated slope and intercept</returns>
        public static LinearCalibration FitMultiplePoints(List<CalibrationPoint> points)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("At least one calibration point is required");
            
            if (points.Count == 1)
            {
                // Single point: assume zero intercept (passes through origin)
                var point = points[0];
                
                // Allow zero ADC only if weight is also zero (empty platform)
                // But we can't calculate slope from (0,0) alone
                if (point.RawADC == 0 && point.KnownWeight == 0)
                {
                    throw new ArgumentException("Cannot calibrate with a single point at (0,0). Please add at least one more point with non-zero weight.");
                }
                
                // Can't divide by zero if ADC is zero but weight is not
                if (point.RawADC == 0 && point.KnownWeight != 0)
                {
                    throw new ArgumentException("Cannot calibrate: Raw ADC is zero but weight is non-zero. Please check your measurements.");
                }
                
                double singlePointSlope = point.KnownWeight / point.RawADC;
                
                return new LinearCalibration
                {
                    Slope = singlePointSlope,
                    Intercept = 0,
                    IsValid = true,
                    CalibrationDate = DateTime.Now,
                    Points = new List<CalibrationPoint>(points),
                    R2 = 1.0, // Perfect fit for single point
                    MaxErrorPercent = 0.0,
                    ADCMode = 0 // Default to Internal, should be set by caller
                };
            }
            
            // Multiple points: least-squares linear regression
            // Zero ADC is fine for multiple points as long as we have variation
            int n = points.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            
            // Check if all points have zero ADC (would cause division issues)
            bool allZeroADC = points.All(p => p.RawADC == 0);
            if (allZeroADC)
            {
                throw new ArgumentException("Cannot calibrate: All points have zero Raw ADC value. Please ensure at least one point has a non-zero ADC reading.");
            }
            
            foreach (var point in points)
            {
                double x = point.RawADC;
                double y = point.KnownWeight;
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }
            
            // Calculate slope: (n*Σxy - Σx*Σy) / (n*Σx² - (Σx)²)
            double denominator = n * sumX2 - sumX * sumX;
            if (Math.Abs(denominator) < 1e-10)
                throw new ArgumentException("Calibration points are collinear or have insufficient spread");
            
            double slope = (n * sumXY - sumX * sumY) / denominator;
            
            // Calculate intercept: (Σy - slope*Σx) / n
            double intercept = (sumY - slope * sumX) / n;
            
            // Calculate R² (coefficient of determination)
            double yMean = sumY / n;
            double ssRes = 0; // Sum of squares of residuals
            double ssTot = 0; // Total sum of squares
            
            foreach (var point in points)
            {
                double yActual = point.KnownWeight;
                double yPredicted = slope * point.RawADC + intercept;
                double residual = yActual - yPredicted;
                ssRes += residual * residual;
                ssTot += (yActual - yMean) * (yActual - yMean);
            }
            
            double r2 = ssTot > 1e-10 ? (1.0 - ssRes / ssTot) : 1.0;
            
            // Calculate max error percentage
            double maxErrorPercent = 0.0;
            foreach (var point in points)
            {
                double yPredicted = slope * point.RawADC + intercept;
                double error = Math.Abs(yPredicted - point.KnownWeight);
                double errorPercent = point.KnownWeight > 0 ? (error / point.KnownWeight) * 100.0 : 0.0;
                if (errorPercent > maxErrorPercent)
                    maxErrorPercent = errorPercent;
            }
            
            return new LinearCalibration
            {
                Slope = slope,
                Intercept = intercept,
                IsValid = true,
                CalibrationDate = DateTime.Now,
                Points = new List<CalibrationPoint>(points),
                R2 = r2,
                MaxErrorPercent = maxErrorPercent,
                ADCMode = 0 // Default to Internal, should be set by caller
            };
        }
        
        /// <summary>
        /// Convert raw ADC value to calibrated weight (before tare)
        /// Routes to regression or piecewise based on Mode
        /// </summary>
        /// <param name="raw">Raw ADC value</param>
        /// <returns>Calibrated weight in kg</returns>
        public double RawToKg(int raw)
        {
            if (!IsValid)
                throw new InvalidOperationException("Calibration is not valid. Please calibrate first.");
            
            // Route to appropriate conversion method based on mode
            if (Mode == CalibrationMode.Piecewise && Segments != null && Segments.Count > 0)
            {
                return RawToKgPiecewise(raw);
            }
            else
            {
                return RawToKgRegression(raw);
            }
        }
        
        /// <summary>
        /// Convert raw ADC value using global linear regression
        /// </summary>
        /// <param name="raw">Raw ADC value</param>
        /// <returns>Calibrated weight in kg</returns>
        public double RawToKgRegression(int raw)
        {
            return Slope * raw + Intercept;
        }
        
        /// <summary>
        /// Convert raw ADC value using piecewise linear interpolation
        /// </summary>
        /// <param name="raw">Raw ADC value</param>
        /// <returns>Calibrated weight in kg</returns>
        public double RawToKgPiecewise(int raw)
        {
            if (Segments == null || Segments.Count == 0)
            {
                // Fallback to regression if segments not available
                return RawToKgRegression(raw);
            }
            
            // Handle edge cases: below first segment
            if (raw < Segments[0].XMin)
            {
                var firstSegment = Segments[0];
                return firstSegment.Slope * raw + firstSegment.Intercept;
            }
            
            // Handle edge cases: above last segment
            if (raw > Segments[Segments.Count - 1].XMax)
            {
                var lastSegment = Segments[Segments.Count - 1];
                return lastSegment.Slope * raw + lastSegment.Intercept;
            }
            
            // Find the segment containing this raw value
            // Segments should be sorted by XMin, so we can do a linear search
            // (Binary search could be used for many segments, but linear is simpler and fast enough for typical use)
            foreach (var segment in Segments)
            {
                if (raw >= segment.XMin && raw <= segment.XMax)
                {
                    return segment.Slope * raw + segment.Intercept;
                }
            }
            
            // Should not reach here if segments are properly constructed, but fallback to regression
            return RawToKgRegression(raw);
        }
        
        /// <summary>
        /// Build piecewise segments from calibration points
        /// Points must be sorted by RawADC (ascending)
        /// </summary>
        public void BuildSegmentsFromPoints()
        {
            if (Points == null || Points.Count < 2)
            {
                // Need at least 2 points for piecewise interpolation
                Segments = new List<CalibrationSegment>();
                return;
            }
            
            // Sort points by RawADC (ascending)
            var sortedPoints = Points.OrderBy(p => p.RawADC).ToList();
            
            Segments = new List<CalibrationSegment>();
            
            // Create segments between adjacent points
            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                var p1 = sortedPoints[i];
                var p2 = sortedPoints[i + 1];
                
                // Skip if points have same ADC value (would cause division by zero)
                if (p1.RawADC == p2.RawADC)
                    continue;
                
                // Calculate slope and intercept for this segment
                // Using two-point form: y = m*x + b
                // m = (y2 - y1) / (x2 - x1)
                // b = y1 - m*x1
                double slope = (p2.KnownWeight - p1.KnownWeight) / (double)(p2.RawADC - p1.RawADC);
                double intercept = p1.KnownWeight - slope * p1.RawADC;
                
                var segment = new CalibrationSegment
                {
                    XMin = p1.RawADC,
                    XMax = p2.RawADC,
                    Slope = slope,
                    Intercept = intercept
                };
                
                Segments.Add(segment);
            }
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
        /// Get quality assessment based on R² value
        /// </summary>
        /// <returns>Quality string (Excellent/Good/Acceptable/Poor)</returns>
        public string GetQualityAssessment()
        {
            if (R2 >= 0.999)
                return "Excellent";
            else if (R2 >= 0.99)
                return "Good";
            else if (R2 >= 0.95)
                return "Acceptable";
            else
                return "Poor";
        }
        
        /// <summary>
        /// Save calibration to JSON file
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115). If not provided, uses current ADCMode property.</param>
        public void SaveToFile(string side, byte? adcMode = null)
        {
            Side = side;
            if (adcMode.HasValue)
            {
                ADCMode = adcMode.Value;
            }
            string filename = PathHelper.GetCalibrationPath(side, ADCMode); // Portable: in Data directory
            string jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filename, jsonString);
        }
        
        /// <summary>
        /// Load calibration from JSON file
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>Loaded calibration or null if file doesn't exist</returns>
        public static LinearCalibration? LoadFromFile(string side, byte adcMode)
        {
            string filename = PathHelper.GetCalibrationPath(side, adcMode); // Portable: in Data directory
            if (!File.Exists(filename))
                return null;
                
            try
            {
                string jsonString = File.ReadAllText(filename);
                var calibration = JsonSerializer.Deserialize<LinearCalibration>(jsonString);
                
                // Backward compatibility: if segments are missing but points exist, build segments lazily
                if (calibration != null && calibration.Points != null && calibration.Points.Count >= 2)
                {
                    if (calibration.Segments == null || calibration.Segments.Count == 0)
                    {
                        // Old calibration file - build segments from points if mode is Piecewise
                        // Otherwise, keep Regression mode (default)
                        if (calibration.Mode == CalibrationMode.Piecewise)
                        {
                            calibration.BuildSegmentsFromPoints();
                        }
                    }
                }
                
                return calibration;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading calibration file {filename}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Check if calibration file exists for a side and ADC mode
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        /// <returns>True if calibration file exists</returns>
        public static bool CalibrationExists(string side, byte adcMode)
        {
            string filename = PathHelper.GetCalibrationPath(side, adcMode); // Portable: in Data directory
            return File.Exists(filename);
        }
        
        /// <summary>
        /// Delete calibration file for a side and ADC mode
        /// </summary>
        /// <param name="side">"Left" or "Right"</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public static void DeleteCalibration(string side, byte adcMode)
        {
            string filename = PathHelper.GetCalibrationPath(side, adcMode); // Portable: in Data directory
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
