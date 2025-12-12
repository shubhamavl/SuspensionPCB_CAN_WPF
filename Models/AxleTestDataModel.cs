using System;
using System.Collections.Generic;
using System.Linq;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// Model for storing axle weight test data for a single axle.
    /// </summary>
    public class AxleTestDataModel
    {
        /// <summary>
        /// Unique test identifier (GUID or timestamp-based)
        /// </summary>
        public string TestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Axle number (1, 2, 3, etc.)
        /// </summary>
        public int AxleNumber { get; set; } = 1;

        /// <summary>
        /// Test start time
        /// </summary>
        public DateTime TestStartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Test end time (when saved)
        /// </summary>
        public DateTime? TestEndTime { get; set; }

        /// <summary>
        /// Left side weight (kg)
        /// </summary>
        public double LeftWeight { get; set; } = 0.0;

        /// <summary>
        /// Right side weight (kg)
        /// </summary>
        public double RightWeight { get; set; } = 0.0;

        /// <summary>
        /// Total weight (Left + Right)
        /// </summary>
        public double TotalWeight => LeftWeight + RightWeight;

        /// <summary>
        /// Minimum weight observed during test (kg)
        /// </summary>
        public double MinWeight { get; set; } = double.MaxValue;

        /// <summary>
        /// Maximum weight observed during test (kg)
        /// </summary>
        public double MaxWeight { get; set; } = double.MinValue;

        /// <summary>
        /// Number of samples collected during test
        /// </summary>
        public int SampleCount { get; set; } = 0;

        /// <summary>
        /// Left side validation status: "Pass" if >= 10kg, "Fail" if < 10kg
        /// </summary>
        public string LeftValidationStatus { get; set; } = "Not Tested";

        /// <summary>
        /// Right side validation status: "Pass" if >= 10kg, "Fail" if < 10kg
        /// </summary>
        public string RightValidationStatus { get; set; } = "Not Tested";

        /// <summary>
        /// Balance validation status: "Pass" if balanced, "Warning" if imbalanced (one side >= 2x the other)
        /// </summary>
        public string BalanceStatus { get; set; } = "Not Tested";

        /// <summary>
        /// Test duration in seconds
        /// </summary>
        public double TestDurationSeconds => TestEndTime.HasValue
            ? (TestEndTime.Value - TestStartTime).TotalSeconds
            : (DateTime.Now - TestStartTime).TotalSeconds;

        /// <summary>
        /// Left side percentage of total weight
        /// </summary>
        public double LeftPercentage => TotalWeight > 0 ? (LeftWeight / TotalWeight) * 100.0 : 0.0;

        /// <summary>
        /// Right side percentage of total weight
        /// </summary>
        public double RightPercentage => TotalWeight > 0 ? (RightWeight / TotalWeight) * 100.0 : 0.0;
    }

    /// <summary>
    /// Model for storing complete axle test session data (all axles).
    /// </summary>
    public class AxleTestSessionModel
    {
        /// <summary>
        /// Session identifier
        /// </summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Session start time
        /// </summary>
        public DateTime SessionStartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Session end time
        /// </summary>
        public DateTime? SessionEndTime { get; set; }

        /// <summary>
        /// List of axle test data (one per axle)
        /// </summary>
        public List<AxleTestDataModel> AxleTests { get; set; } = new List<AxleTestDataModel>();

        /// <summary>
        /// Total number of axles tested
        /// </summary>
        public int TotalAxles => AxleTests.Count;

        /// <summary>
        /// Total weight across all axles
        /// </summary>
        public double TotalWeight => AxleTests.Sum(a => a.TotalWeight);
    }
}
