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
        public string Side { get; set; } = "Left"; // "Left" or "Right" (current side being displayed)
        public double InitialWeight { get; set; } // Initial weight when test started (for Y-axis scaling)
        
        // Current side data
        public double MinWeight { get; set; } // Minimum weight during test (for current side)
        public double MaxWeight { get; set; } // Maximum weight during test (for current side)
        public double Efficiency { get; set; } // Efficiency percentage for current side (Min/AxleWeight × 100%)
        public string TestResult { get; set; } = "Not Tested"; // "Pass", "Fail", or "Not Tested" (for current side)
        
        // AVL-style: Axle weights (reference for efficiency calculation)
        public double AxleWeightLeft { get; set; } = 0.0; // Axle weight for Left side (from axle weight test)
        public double AxleWeightRight { get; set; } = 0.0; // Axle weight for Right side (from axle weight test)
        
        // AVL-style: Per-side efficiency and results
        public double EfficiencyLeft { get; set; } = 0.0; // Efficiency for Left side (MinLeft/AxleWeightLeft × 100%)
        public double EfficiencyRight { get; set; } = 0.0; // Efficiency for Right side (MinRight/AxleWeightRight × 100%)
        public string TestResultLeft { get; set; } = "Not Tested"; // "Pass", "Fail", or "Not Tested" for Left side
        public string TestResultRight { get; set; } = "Not Tested"; // "Pass", "Fail", or "Not Tested" for Right side
        public string TestResultCombined { get; set; } = "Not Tested"; // Combined result: "Pass" if both sides pass, else "Fail"
        
        // Common settings
        public double EfficiencyLimit { get; set; } // Efficiency limit used for Pass/Fail validation
        public string Limits { get; set; } = ""; // Limit string (e.g., "≥85.0%")
        public int SampleCount { get; set; } // Number of data points collected
        public string TransmissionRate { get; set; } = "1kHz"; // CAN transmission rate
        public double[]? DataPoints { get; set; } // Full data array (optional, can be large)
    }
}

