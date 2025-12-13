using System;
using System.ComponentModel;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Models
{
    /// <summary>
    /// ViewModel for a single calibration point in the UI
    /// </summary>
    public class CalibrationPointViewModel : INotifyPropertyChanged
    {
        private int _pointNumber;
        private int _rawADC = 0;
        private ushort _internalADC = 0;
        private int _ads1115ADC = 0;  // Changed to int for signed support (-32768 to +32767)
        private double _knownWeight = 0;
        private bool _isCaptured = false;
        private bool _bothModesCaptured = false;
        private bool _isEditing = false;
        private string _statusText = "Ready to capture";
        
        // Statistics properties for multi-sample averaging
        private double _captureMean = 0;
        private double _captureStdDev = 0;
        private int _captureSampleCount = 0;
        private string _captureStabilityWarning = "";
        
        public int PointNumber
        {
            get => _pointNumber;
            set { _pointNumber = value; OnPropertyChanged(nameof(PointNumber)); }
        }
        
        public int RawADC
        {
            get => _rawADC;
            set { _rawADC = value; OnPropertyChanged(nameof(RawADC)); }
        }
        
        public ushort InternalADC
        {
            get => _internalADC;
            set 
            { 
                if (value > 4095)
                    throw new ArgumentOutOfRangeException(nameof(InternalADC), $"Internal ADC value must be between 0-4095. Value: {value}");
                _internalADC = value; 
                OnPropertyChanged(nameof(InternalADC));
                UpdateStatusText();
            }
        }
        
        public int ADS1115ADC
        {
            get => _ads1115ADC;
            set 
            { 
                if (value < -32768 || value > 32767)
                    throw new ArgumentOutOfRangeException(nameof(ADS1115ADC), $"ADS1115 ADC value must be between -32768 to +32767. Value: {value}");
                _ads1115ADC = value; 
                OnPropertyChanged(nameof(ADS1115ADC));
                UpdateStatusText();
            }
        }
        
        public double KnownWeight
        {
            get => _knownWeight;
            set 
            { 
                _knownWeight = value; 
                OnPropertyChanged(nameof(KnownWeight));
                UpdateStatusText(); // Update status to show zero indicator if weight is 0
            }
        }
        
        public bool IsCaptured
        {
            get => _isCaptured;
            set 
            { 
                _isCaptured = value; 
                OnPropertyChanged(nameof(IsCaptured));
                UpdateStatusText();
            }
        }
        
        public bool BothModesCaptured
        {
            get => _bothModesCaptured;
            set 
            { 
                _bothModesCaptured = value; 
                OnPropertyChanged(nameof(BothModesCaptured));
                UpdateStatusText();
            }
        }
        
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(IsNotEditing));
            }
        }

        public bool IsNotEditing => !_isEditing;
        
        public double CaptureMean
        {
            get => _captureMean;
            set { _captureMean = value; OnPropertyChanged(nameof(CaptureMean)); }
        }
        
        public double CaptureStdDev
        {
            get => _captureStdDev;
            set { _captureStdDev = value; OnPropertyChanged(nameof(CaptureStdDev)); }
        }
        
        public int CaptureSampleCount
        {
            get => _captureSampleCount;
            set { _captureSampleCount = value; OnPropertyChanged(nameof(CaptureSampleCount)); }
        }
        
        public string CaptureStabilityWarning
        {
            get => _captureStabilityWarning;
            set { _captureStabilityWarning = value; OnPropertyChanged(nameof(CaptureStabilityWarning)); }
        }
        
        private void UpdateStatusText()
        {
            if (_bothModesCaptured && _isCaptured)
            {
                string zeroIndicator = Math.Abs(KnownWeight) < 0.01 ? " [ZERO POINT]" : "";
                string statsInfo = "";
                
                // Show statistics if available
                if (_captureSampleCount > 0)
                {
                    string stabilityIndicator = string.IsNullOrEmpty(_captureStabilityWarning) ? "✓" : "⚠";
                    statsInfo = $" (n={_captureSampleCount}, σ={_captureStdDev:F1}){stabilityIndicator}";
                }
                
                // Format ADS1115 as signed (can be negative)
                string ads1115Display = ADS1115ADC >= 0 ? $"+{ADS1115ADC}" : ADS1115ADC.ToString();
                StatusText = $"✓ Captured: {KnownWeight:F0} kg @ Internal:{InternalADC} ADS1115:{ads1115Display}{zeroIndicator}{statsInfo}";
            }
            else if (_isCaptured)
            {
                StatusText = $"⚠ Partial: {KnownWeight:F0} kg @ ADC {RawADC} (capturing both modes...)";
            }
            else
            {
                StatusText = "Ready to capture";
            }
        }
        
        /// <summary>
        /// Convert to CalibrationPoint for Internal mode calculation
        /// </summary>
        public CalibrationPoint ToCalibrationPointInternal()
        {
            return new CalibrationPoint
            {
                RawADC = InternalADC,
                KnownWeight = KnownWeight,
                Timestamp = DateTime.Now
            };
        }
        
        /// <summary>
        /// Convert to CalibrationPoint for ADS1115 mode calculation
        /// </summary>
        public CalibrationPoint ToCalibrationPointADS1115()
        {
            return new CalibrationPoint
            {
                RawADC = ADS1115ADC,
                KnownWeight = KnownWeight,
                Timestamp = DateTime.Now
            };
        }
        
        /// <summary>
        /// Convert to CalibrationPoint for calculation (legacy support - uses RawADC)
        /// </summary>
        public CalibrationPoint ToCalibrationPoint()
        {
            return new CalibrationPoint
            {
                RawADC = RawADC,
                KnownWeight = KnownWeight,
                Timestamp = DateTime.Now
            };
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

