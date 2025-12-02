using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using SuspensionPCB_CAN_WPF.Adapters;
using SuspensionPCB_CAN_WPF.Services;

namespace SuspensionPCB_CAN_WPF.Views
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

                // Load pattern values
                LoadPatternValues();
            }
            finally
            {
                _updatingFromSlider = false;
                _updatingFromTextBox = false;
            }
        }

        private void LoadPatternValues()
        {
            if (_adapter == null) return;

            try
            {
                // Left side patterns
                SetComboBoxSelection(LeftPatternTypeCombo, _adapter.LeftPattern.ToString());
                LeftBaselineSlider.Value = _adapter.LeftBaseline;
                LeftBaselineTextBox.Text = _adapter.LeftBaseline.ToString("F1", CultureInfo.InvariantCulture);
                LeftAmplitudeSlider.Value = _adapter.LeftAmplitude;
                LeftAmplitudeTextBox.Text = _adapter.LeftAmplitude.ToString("F1", CultureInfo.InvariantCulture);
                LeftFrequencySlider.Value = _adapter.LeftFrequency;
                LeftFrequencyTextBox.Text = _adapter.LeftFrequency.ToString("F1", CultureInfo.InvariantCulture);
                LeftDampingSlider.Value = _adapter.LeftDamping;
                LeftDampingTextBox.Text = _adapter.LeftDamping.ToString("F2", CultureInfo.InvariantCulture);
                LeftRampDurationSlider.Value = _adapter.LeftRampDuration;
                LeftRampDurationTextBox.Text = _adapter.LeftRampDuration.ToString("F1", CultureInfo.InvariantCulture);

                // Right side patterns
                SetComboBoxSelection(RightPatternTypeCombo, _adapter.RightPattern.ToString());
                RightBaselineSlider.Value = _adapter.RightBaseline;
                RightBaselineTextBox.Text = _adapter.RightBaseline.ToString("F1", CultureInfo.InvariantCulture);
                RightAmplitudeSlider.Value = _adapter.RightAmplitude;
                RightAmplitudeTextBox.Text = _adapter.RightAmplitude.ToString("F1", CultureInfo.InvariantCulture);
                RightFrequencySlider.Value = _adapter.RightFrequency;
                RightFrequencyTextBox.Text = _adapter.RightFrequency.ToString("F1", CultureInfo.InvariantCulture);
                RightDampingSlider.Value = _adapter.RightDamping;
                RightDampingTextBox.Text = _adapter.RightDamping.ToString("F2", CultureInfo.InvariantCulture);
                RightRampDurationSlider.Value = _adapter.RightRampDuration;
                RightRampDurationTextBox.Text = _adapter.RightRampDuration.ToString("F1", CultureInfo.InvariantCulture);

                // Update UI visibility based on pattern types
                UpdatePatternUIVisibility(true);
                UpdatePatternUIVisibility(false);
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Error loading pattern values: {ex.Message}", "SimulatorControl");
            }
        }

        private void SetComboBoxSelection(ComboBox comboBox, string value)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Tag?.ToString() == value)
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void UpdatePatternUIVisibility(bool isLeft)
        {
            SimulationPattern pattern = isLeft ? _adapter?.LeftPattern ?? SimulationPattern.Static 
                                                : _adapter?.RightPattern ?? SimulationPattern.Static;

            if (isLeft)
            {
                bool showOscillating = pattern == SimulationPattern.DampedSine;
                bool showStep = pattern == SimulationPattern.Step;
                bool showRamp = pattern == SimulationPattern.Ramp;

                LeftAmplitudePanel.Visibility = (showOscillating || showStep || showRamp) ? Visibility.Visible : Visibility.Collapsed;
                LeftFrequencyPanel.Visibility = showOscillating ? Visibility.Visible : Visibility.Collapsed;
                LeftDampingPanel.Visibility = (showOscillating || showStep) ? Visibility.Visible : Visibility.Collapsed;
                LeftRampDurationPanel.Visibility = showRamp ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                bool showOscillating = pattern == SimulationPattern.DampedSine;
                bool showStep = pattern == SimulationPattern.Step;
                bool showRamp = pattern == SimulationPattern.Ramp;

                RightAmplitudePanel.Visibility = (showOscillating || showStep || showRamp) ? Visibility.Visible : Visibility.Collapsed;
                RightFrequencyPanel.Visibility = showOscillating ? Visibility.Visible : Visibility.Collapsed;
                RightDampingPanel.Visibility = (showOscillating || showStep) ? Visibility.Visible : Visibility.Collapsed;
                RightRampDurationPanel.Visibility = showRamp ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateADCValues();
            UpdatePatternPreview();
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

        private void UpdatePatternPreview()
        {
            if (_adapter == null) return;

            try
            {
                // Calculate current pattern weights (this is a preview, actual calculation happens in adapter)
                // We can show the baseline + current pattern value if needed
                // For now, the ADC display already shows the result
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Error updating pattern preview: {ex.Message}", "SimulatorControl");
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

        // Pattern Type Handlers
        private void LeftPatternTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_adapter == null || LeftPatternTypeCombo.SelectedItem is not ComboBoxItem item) return;

            string? patternStr = item.Tag?.ToString();
            if (Enum.TryParse<SimulationPattern>(patternStr, out var pattern))
            {
                _adapter.LeftPattern = pattern;
                UpdatePatternUIVisibility(true);
            }
        }

        private void RightPatternTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_adapter == null || RightPatternTypeCombo.SelectedItem is not ComboBoxItem item) return;

            string? patternStr = item.Tag?.ToString();
            if (Enum.TryParse<SimulationPattern>(patternStr, out var pattern))
            {
                _adapter.RightPattern = pattern;
                UpdatePatternUIVisibility(false);
            }
        }

        // Left Pattern Parameter Handlers
        private void LeftBaselineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.LeftBaseline = e.NewValue;
                LeftBaselineTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void LeftBaselineTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(LeftBaselineTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(1000, value));
                _updatingFromSlider = true;
                try
                {
                    LeftBaselineSlider.Value = value;
                    _adapter.LeftBaseline = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void LeftAmplitudeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.LeftAmplitude = e.NewValue;
                LeftAmplitudeTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void LeftAmplitudeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(LeftAmplitudeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(500, value));
                _updatingFromSlider = true;
                try
                {
                    LeftAmplitudeSlider.Value = value;
                    _adapter.LeftAmplitude = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void LeftFrequencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.LeftFrequency = e.NewValue;
                LeftFrequencyTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void LeftFrequencyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(LeftFrequencyTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0.1, Math.Min(10, value));
                _updatingFromSlider = true;
                try
                {
                    LeftFrequencySlider.Value = value;
                    _adapter.LeftFrequency = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void LeftDampingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.LeftDamping = e.NewValue;
                LeftDampingTextBox.Text = e.NewValue.ToString("F2", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void LeftDampingTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(LeftDampingTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0.01, Math.Min(2, value));
                _updatingFromSlider = true;
                try
                {
                    LeftDampingSlider.Value = value;
                    _adapter.LeftDamping = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void LeftRampDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.LeftRampDuration = e.NewValue;
                LeftRampDurationTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void LeftRampDurationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(LeftRampDurationTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0.1, Math.Min(30, value));
                _updatingFromSlider = true;
                try
                {
                    LeftRampDurationSlider.Value = value;
                    _adapter.LeftRampDuration = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void LeftRestartPatternBtn_Click(object sender, RoutedEventArgs e)
        {
            _adapter?.ResetLeftPattern();
        }

        // Right Pattern Parameter Handlers
        private void RightBaselineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.RightBaseline = e.NewValue;
                RightBaselineTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void RightBaselineTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(RightBaselineTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(1000, value));
                _updatingFromSlider = true;
                try
                {
                    RightBaselineSlider.Value = value;
                    _adapter.RightBaseline = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void RightAmplitudeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.RightAmplitude = e.NewValue;
                RightAmplitudeTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void RightAmplitudeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(RightAmplitudeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0, Math.Min(500, value));
                _updatingFromSlider = true;
                try
                {
                    RightAmplitudeSlider.Value = value;
                    _adapter.RightAmplitude = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void RightFrequencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.RightFrequency = e.NewValue;
                RightFrequencyTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void RightFrequencyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(RightFrequencyTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0.1, Math.Min(10, value));
                _updatingFromSlider = true;
                try
                {
                    RightFrequencySlider.Value = value;
                    _adapter.RightFrequency = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void RightDampingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.RightDamping = e.NewValue;
                RightDampingTextBox.Text = e.NewValue.ToString("F2", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void RightDampingTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(RightDampingTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0.01, Math.Min(2, value));
                _updatingFromSlider = true;
                try
                {
                    RightDampingSlider.Value = value;
                    _adapter.RightDamping = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void RightRampDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSlider || _adapter == null) return;
            _updatingFromTextBox = true;
            try
            {
                _adapter.RightRampDuration = e.NewValue;
                RightRampDurationTextBox.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
            }
            finally { _updatingFromTextBox = false; }
        }

        private void RightRampDurationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromTextBox || _adapter == null) return;
            if (double.TryParse(RightRampDurationTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                value = Math.Max(0.1, Math.Min(30, value));
                _updatingFromSlider = true;
                try
                {
                    RightRampDurationSlider.Value = value;
                    _adapter.RightRampDuration = value;
                }
                finally { _updatingFromSlider = false; }
            }
        }

        private void RightRestartPatternBtn_Click(object sender, RoutedEventArgs e)
        {
            _adapter?.ResetRightPattern();
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

            // Reset pattern values
            _adapter.LeftPattern = SimulationPattern.Static;
            _adapter.RightPattern = SimulationPattern.Static;
            _adapter.LeftBaseline = 0.0;
            _adapter.RightBaseline = 0.0;
            _adapter.LeftAmplitude = 200.0;
            _adapter.RightAmplitude = 200.0;
            _adapter.LeftFrequency = 2.0;
            _adapter.RightFrequency = 2.0;
            _adapter.LeftDamping = 0.2;
            _adapter.RightDamping = 0.2;
            _adapter.LeftRampDuration = 5.0;
            _adapter.RightRampDuration = 5.0;

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

