// SPDX-License-Identifier: Apache-2.0
// Copyright Pionix GmbH and Contributors to EVerest

using System;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// Model for suspension test data (database-like structure)
    /// Similar to AVL LMSV1.0 SuspensionTestData
    /// </summary>
    public class SuspensionTestDataModel
    {
        public string TestId { get; set; } = string.Empty;
        public DateTime TestStartTime { get; set; }
        public DateTime TestEndTime { get; set; }
        public string Side { get; set; } = "Left"; // "Left" or "Right"
        public double InitialWeight { get; set; } // Initial weight when test started
        public double MinWeight { get; set; } // Minimum weight during test
        public double MaxWeight { get; set; } // Maximum weight during test
        public double Efficiency { get; set; } // Efficiency percentage (Min/Initial × 100%)
        public double EfficiencyLimit { get; set; } // Efficiency limit used for Pass/Fail validation
        public string TestResult { get; set; } = "Not Tested"; // "Pass", "Fail", or "Not Tested"
        public string Limits { get; set; } = ""; // Limit string (e.g., "≥85.0%")
        public int SampleCount { get; set; } // Number of data points collected
        public string TransmissionRate { get; set; } = "1kHz"; // CAN transmission rate
        public double[]? DataPoints { get; set; } // Full data array (optional, can be large)
    }
}

