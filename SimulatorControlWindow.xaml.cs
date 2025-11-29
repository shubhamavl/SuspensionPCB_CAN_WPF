using System;
using System.Windows;
using System.Windows.Threading;
using System.Globalization;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Interaction logic for SimulatorControlWindow.xaml
    /// </summary>
    public partial class SimulatorControlWindow : Window
    {
        private SimulatorCanAdapter? _adapter;
        private DispatcherTimer? _updateTimer;
        private bool _updatingFromSlider = false;
        private bool _updatingFromTextBox = false;

        public SimulatorControlWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize the window with simulator adapter reference
        /// </summary>
        public void Initialize(SimulatorCanAdapter adapter)
        {
            _adapter = adapter;
            
            // Load current values from adapter
            LoadCurrentValues();
            
            // Start update timer for real-time ADC display
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // Update every 100ms
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();
            
            // Update ADC mode display
            UpdateADCModeDisplay();
        }

        private void LoadCurrentValues()
        {
            if (_adapter == null) return;

            _updatingFromSlider = true;
            _updatingFromTextBox = true;

            try
            {
                // Left side
                LeftWeightSlider.Value = _adapter.LeftWeight;
                LeftWeightTextBox.Text = _adapter.LeftWeight.ToString("F1", CultureInfo.InvariantCulture);
                LeftZeroADCTextBox.Text = _adapter.LeftZeroADC.ToString();
                LeftSensitivityTextBox.Text = _adapter.LeftSensitivity.ToString("F1", CultureInfo.InvariantCulture);

                // Right side
                RightWeightSlider.Value = _adapter.RightWeight;
                RightWeightTextBox.Text = _adapter.RightWeight.ToString("F1", CultureInfo.InvariantCulture);
                RightZeroADCTextBox.Text = _adapter.RightZeroADC.ToString();
                RightSensitivityTextBox.Text = _adapter.RightSensitivity.ToString("F1", CultureInfo.InvariantCulture);

                // Global settings
                NoiseLevelSlider.Value = _adapter.NoiseLevel;
                NoiseLevelTextBox.Text = _adapter.NoiseLevel.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally
            {
                _updatingFromSlider = false;
                _updatingFromTextBox = false;
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateADCValues();
        }

        private void UpdateADCValues()
        {
            if (_adapter == null) return;

            try
            {
                ushort leftADC = _adapter.GetCurrentLeftADC();
                ushort rightADC = _adapter.GetCurrentRightADC();

                LeftCurrentADCTextBlock.Text = leftADC.ToString();
                RightCurrentADCTextBlock.Text = rightADC.ToString();
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Error updating ADC values: {ex.Message}", "SimulatorControl");
            }
        }

        private void UpdateADCModeDisplay()
        {
            if (_adapter == null) return;

            string modeText = _adapter.CurrentADCMode == 0 
                ? "Internal (12-bit, 0-4095)" 
                : "ADS1115 (16-bit, 0-65535)";
            
            ADCModeTextBlock.Text = modeText;
        }

        // Left Weight Slider
        private void LeftWeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;

            _updatingFromTextBox = true;
            try
            {
                double value = e.NewValue;
                _adapter.LeftWeight = value;
                LeftWeightTextBox.Text = value.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally
            {
                _updatingFromTextBox = false;
            }
        }

        // Left Weight TextBox
        private void LeftWeightTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;

            if (double.TryParse(LeftWeightTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(1000, value)); // Clamp to 0-1000
                _updatingFromSlider = true;
                try
                {
                    LeftWeightSlider.Value = value;
                    _adapter.LeftWeight = value;
                }
                finally
                {
                    _updatingFromSlider = false;
                }
            }
        }

        // Right Weight Slider
        private void RightWeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;

            _updatingFromTextBox = true;
            try
            {
                double value = e.NewValue;
                _adapter.RightWeight = value;
                RightWeightTextBox.Text = value.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally
            {
                _updatingFromTextBox = false;
            }
        }

        // Right Weight TextBox
        private void RightWeightTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;

            if (double.TryParse(RightWeightTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(1000, value)); // Clamp to 0-1000
                _updatingFromSlider = true;
                try
                {
                    RightWeightSlider.Value = value;
                    _adapter.RightWeight = value;
                }
                finally
                {
                    _updatingFromSlider = false;
                }
            }
        }

        // Left Zero ADC
        private void LeftZeroADCTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_adapter == null) return;

            if (ushort.TryParse(LeftZeroADCTextBox.Text, out ushort value))
            {
                ushort maxADC = (ushort)(_adapter.CurrentADCMode == 0 ? 4095 : 65535);
                value = Math.Min(value, maxADC);
                _adapter.LeftZeroADC = value;
            }
        }

        // Right Zero ADC
        private void RightZeroADCTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_adapter == null) return;

            if (ushort.TryParse(RightZeroADCTextBox.Text, out ushort value))
            {
                ushort maxADC = (ushort)(_adapter.CurrentADCMode == 0 ? 4095 : 65535);
                value = Math.Min(value, maxADC);
                _adapter.RightZeroADC = value;
            }
        }

        // Left Sensitivity
        private void LeftSensitivityTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_adapter == null) return;

            if (double.TryParse(LeftSensitivityTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(1000, value)); // Clamp to 0-1000
                _adapter.LeftSensitivity = value;
            }
        }

        // Right Sensitivity
        private void RightSensitivityTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_adapter == null) return;

            if (double.TryParse(RightSensitivityTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(1000, value)); // Clamp to 0-1000
                _adapter.RightSensitivity = value;
            }
        }

        // Noise Level Slider
        private void NoiseLevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;

            _updatingFromTextBox = true;
            try
            {
                double value = e.NewValue;
                _adapter.NoiseLevel = value;
                NoiseLevelTextBox.Text = value.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally
            {
                _updatingFromTextBox = false;
            }
        }

        // Noise Level TextBox
        private void NoiseLevelTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;

            if (double.TryParse(NoiseLevelTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(50, value)); // Clamp to 0-50
                _updatingFromSlider = true;
                try
                {
                    NoiseLevelSlider.Value = value;
                    _adapter.NoiseLevel = value;
                }
                finally
                {
                    _updatingFromSlider = false;
                }
            }
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            if (_adapter == null) return;

            // Reset to default values
            _adapter.LeftWeight = 0.0;
            _adapter.RightWeight = 0.0;
            _adapter.LeftZeroADC = 2048;
            _adapter.RightZeroADC = 2048;
            _adapter.LeftSensitivity = 100.0;
            _adapter.RightSensitivity = 100.0;
            _adapter.NoiseLevel = 5.0;

            // Reload UI
            LoadCurrentValues();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Stop update timer
            _updateTimer?.Stop();
            _updateTimer = null;
            
            base.OnClosing(e);
        }
    }
}

