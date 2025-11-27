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
        private double _knownWeight = 0;
        private bool _isCaptured = false;
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
        
        public double KnownWeight
        {
            get => _knownWeight;
            set { _knownWeight = value; OnPropertyChanged(nameof(KnownWeight)); }
        }
        
        public bool IsCaptured
        {
            get => _isCaptured;
            set 
            { 
                _isCaptured = value; 
                OnPropertyChanged(nameof(IsCaptured));
                StatusText = _isCaptured ? $"✓ Captured: {KnownWeight:F0} kg @ ADC {RawADC}" : "Ready to capture";
            }
        }
        
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }
        
        /// <summary>
        /// Convert to CalibrationPoint for calculation
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

