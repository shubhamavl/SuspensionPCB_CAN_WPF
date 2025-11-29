using System;
using System.ComponentModel;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// ViewModel for a single calibration point in the UI
    /// </summary>
    public class CalibrationPointViewModel : INotifyPropertyChanged
    {
        private int _pointNumber;
        private int _rawADC = 0;
        private ushort _internalADC = 0;
        private ushort _ads1115ADC = 0;
        private double _knownWeight = 0;
        private bool _isCaptured = false;
        private bool _bothModesCaptured = false;
        private string _statusText = "Ready to capture";
        
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
                _internalADC = value; 
                OnPropertyChanged(nameof(InternalADC));
                UpdateStatusText();
            }
        }
        
        public ushort ADS1115ADC
        {
            get => _ads1115ADC;
            set 
            { 
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
        
        private void UpdateStatusText()
        {
            if (_bothModesCaptured && _isCaptured)
            {
                string zeroIndicator = Math.Abs(KnownWeight) < 0.01 ? " [ZERO POINT]" : "";
                StatusText = $"✓ Captured: {KnownWeight:F0} kg @ Internal:{InternalADC} ADS1115:{ADS1115ADC}{zeroIndicator}";
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

