using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuspensionPCB_CAN_WPF.Core
{
    /// <summary>
    /// Result of calibration capture with statistics
    /// </summary>
    public class CalibrationCaptureResult
    {
        public int AveragedValue { get; set; } // Changed to int to support signed values (ADS1115)
        public double Mean { get; set; }
        public double Median { get; set; }
        public double StandardDeviation { get; set; }
        public int SampleCount { get; set; }
        public int OutliersRemoved { get; set; }
        public bool IsStable { get; set; } // Based on std dev threshold
    }

    /// <summary>
    /// Helper class for calibration statistics and multi-sample averaging
    /// </summary>
    public static class CalibrationStatistics
    {
        /// <summary>
        /// Capture averaged ADC value by collecting multiple samples
        /// </summary>
        /// <param name="sampleCount">Target number of samples to collect</param>
        /// <param name="durationMs">Maximum duration to collect samples over (milliseconds)</param>
        /// <param name="getCurrentADC">Function to get current raw ADC value</param>
        /// <param name="updateProgress">Optional callback to update progress (sample number, total)</param>
        /// <param name="useMedian">Use median instead of mean</param>
        /// <param name="removeOutliers">Remove outliers before averaging</param>
        /// <param name="outlierThreshold">Standard deviations for outlier removal</param>
        /// <param name="maxStdDev">Maximum acceptable standard deviation (warning threshold)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>CalibrationCaptureResult with averaged value and statistics</returns>
        public static async Task<CalibrationCaptureResult> CaptureAveragedADC(
            int sampleCount,
            int durationMs,
            Func<int> getCurrentADC,
            Action<int, int>? updateProgress = null,
            bool useMedian = true,
            bool removeOutliers = true,
            double outlierThreshold = 2.0,
            double maxStdDev = 10.0,
            CancellationToken cancellationToken = default)
        {
            var samples = new List<int>(); // Changed to int to support signed values (ADS1115)
            var startTime = DateTime.Now;
            const int sampleIntervalMs = 10; // Collect samples at ~100Hz

            // Collect samples until we reach target count or duration limit
            while (samples.Count < sampleCount && 
                   (DateTime.Now - startTime).TotalMilliseconds < durationMs &&
                   !cancellationToken.IsCancellationRequested)
            {
                int currentADC = getCurrentADC();
                
                // Accept all valid signed values (including zero and negative for ADS1115)
                // Range: -65536 to +65534 for combined channels
                if (currentADC >= -65536 && currentADC <= 65534)
                {
                    samples.Add(currentADC);
                    updateProgress?.Invoke(samples.Count, sampleCount);
                }

                // Small delay between samples
                if (samples.Count < sampleCount)
                {
                    await Task.Delay(sampleIntervalMs, cancellationToken);
                }
            }

            if (samples.Count == 0)
            {
                throw new InvalidOperationException("No valid samples collected during calibration capture");
            }

            // Calculate statistics
            double mean = samples.Average(x => (double)x);
            double stdDev = CalculateStandardDeviation(samples, mean);
            
            // Calculate median
            var sortedSamples = new List<int>(samples);
            sortedSamples.Sort();
            double median = CalculateMedian(sortedSamples);

            // Remove outliers if requested
            int outliersRemoved = 0;
            List<int> filteredSamples = samples;
            if (removeOutliers && samples.Count > 2)
            {
                filteredSamples = RemoveOutliers(samples, mean, stdDev, outlierThreshold);
                outliersRemoved = samples.Count - filteredSamples.Count;
                
                // Recalculate statistics after outlier removal
                if (filteredSamples.Count > 0)
                {
                    mean = filteredSamples.Average(x => (double)x);
                    stdDev = CalculateStandardDeviation(filteredSamples, mean);
                    
                    sortedSamples = new List<int>(filteredSamples);
                    sortedSamples.Sort();
                    median = CalculateMedian(sortedSamples);
                }
            }

            // Determine final averaged value (keep as int to support signed values)
            int averagedValue = useMedian 
                ? (int)Math.Round(median) 
                : (int)Math.Round(mean);

            // Check stability
            bool isStable = stdDev <= maxStdDev;

            return new CalibrationCaptureResult
            {
                AveragedValue = averagedValue,
                Mean = mean,
                Median = median,
                StandardDeviation = stdDev,
                SampleCount = samples.Count,
                OutliersRemoved = outliersRemoved,
                IsStable = isStable
            };
        }

        /// <summary>
        /// Calculate standard deviation from sample list
        /// </summary>
        public static double CalculateStandardDeviation(List<int> samples, double mean)
        {
            if (samples.Count == 0)
                return 0.0;

            double sumSquaredDiff = samples.Sum(s => Math.Pow(s - mean, 2));
            return Math.Sqrt(sumSquaredDiff / samples.Count);
        }

        /// <summary>
        /// Remove outliers from sample list (values outside N standard deviations from mean)
        /// </summary>
        public static List<int> RemoveOutliers(List<int> samples, double mean, double stdDev, double threshold)
        {
            if (stdDev < 0.1) // Very low std dev, no outliers to remove
                return new List<int>(samples);

            return samples.Where(s => Math.Abs(s - mean) <= threshold * stdDev).ToList();
        }

        /// <summary>
        /// Calculate median from sorted sample list
        /// </summary>
        public static double CalculateMedian(List<int> sortedSamples)
        {
            if (sortedSamples.Count == 0)
                return 0.0;

            int mid = sortedSamples.Count / 2;
            if (sortedSamples.Count % 2 == 0)
            {
                // Even number of samples: average of two middle values
                return (sortedSamples[mid - 1] + sortedSamples[mid]) / 2.0;
            }
            else
            {
                // Odd number of samples: middle value
                return sortedSamples[mid];
            }
        }
    }
}

