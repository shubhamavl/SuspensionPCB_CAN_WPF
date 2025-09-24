using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SuspensionPCB_CAN_WPF
{
    public partial class CalibrationWindow : Window
    {
        // Calibration parameters - REMOVED _selectedMode (always weight-based)
        private byte _selectedPolynomialOrder = 2; // Default: Order 2
        private bool _autoSpacing = true; // Default: Auto Spacing
        private byte _channelMask = 0x0F; // All 4 channels (0x01+0x02+0x04+0x08)

        // Parent reference for sending calibration command
        private MainWindow? _parentWindow;

        public CalibrationWindow(MainWindow parentWindow)
        {
            InitializeComponent();
            _parentWindow = parentWindow;

            // WINDOW FOCUS MANAGEMENT
           //  this.Topmost = true; // Always on top
            this.ShowInTaskbar = true;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.Owner = parentWindow;


            // Handle window activation events
            this.Activated += CalibrationWindow_Activated;
            this.Deactivated += CalibrationWindow_Deactivated;


            // Initialize default selections
            InitializeDefaults();
        }


        // ADDED: Window activation event handler
        private void CalibrationWindow_Activated(object sender, EventArgs e)
        {
            // Window focus में आने पर
            System.Diagnostics.Debug.WriteLine("CalibrationWindow activated - bringing to front");
            this.BringIntoView();
        }

        // ADDED: Window deactivation event handler  
        private void CalibrationWindow_Deactivated(object sender, EventArgs e)
        {
            // Optional: Handle window losing focus
            System.Diagnostics.Debug.WriteLine("CalibrationWindow deactivated");
        }


        // ADDED: Manual focus method for external calls
        public void BringToFront()
        {
            try
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.WindowState = WindowState.Normal;
                }

                this.Activate();
                this.Focus();
               // this.Topmost = true;
                this.Topmost = false; // Trick to bring window to front without keeping it always on top
                this.BringIntoView();

                System.Diagnostics.Debug.WriteLine("CalibrationWindow brought to front");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BringToFront error: {ex.Message}");
            }
        }




        private void InitializeDefaults()
        {
            // Set default values
            PointCountSlider.Value = 2;

            // Set default polynomial order
            Poly2.IsChecked = true;

            // Set default spacing
            AutoSpacingToggle.IsChecked = false;
            ManualSpacingToggle.IsChecked = true;

            // Set default channels (All 4 channels for 4-load cell system)
            Channel1.IsChecked = true;
            Channel2.IsChecked = true;
            Channel3.IsChecked = true;
            Channel4.IsChecked = true;

            // Update channel mask after selection
            UpdateChannelMask();

            // Update summary
            UpdateSummary();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSummary();
        }

        // Point Count Slider Event
        private void PointCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSummary();
        }

        // REMOVED: CalibrationMode_Click method (no longer needed)

        private void PolynomialOrder_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as ToggleButton;
            if (button == null) return;

            // Mutual exclusion for polynomial order
            Poly1.IsChecked = false; Poly2.IsChecked = false;
            Poly3.IsChecked = false; Poly4.IsChecked = false;

            button.IsChecked = true; // Re-check clicked button

            // Update selected order
            if (button == Poly1) _selectedPolynomialOrder = 1;
            else if (button == Poly2) _selectedPolynomialOrder = 2;
            else if (button == Poly3) _selectedPolynomialOrder = 3;
            else if (button == Poly4) _selectedPolynomialOrder = 4;

            UpdateSummary();
        }

        private void SpacingMode_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as ToggleButton;
            if (button == null) return;

            // Implement mutual exclusion for spacing mode
            if (button == AutoSpacingToggle && AutoSpacingToggle.IsChecked == true)
            {
                ManualSpacingToggle.IsChecked = false;
                _autoSpacing = true;
            }
            else if (button == ManualSpacingToggle && ManualSpacingToggle.IsChecked == true)
            {
                AutoSpacingToggle.IsChecked = false;
                _autoSpacing = false;
            }

            // Ensure at least one is always selected
            if (AutoSpacingToggle.IsChecked != true && ManualSpacingToggle.IsChecked != true)
            {
                button.IsChecked = true;
            }

            UpdateSummary();
        }

        // Channel Selection
        private void Channel_Click(object sender, RoutedEventArgs e)
        {
            UpdateChannelMask();
            UpdateSummary();
        }

        private void UpdateChannelMask()
        {
            _channelMask = 0;

            if (Channel1.IsChecked == true) _channelMask |= 0x01;
            if (Channel2.IsChecked == true) _channelMask |= 0x02;
            if (Channel3.IsChecked == true) _channelMask |= 0x04;
            if (Channel4.IsChecked == true) _channelMask |= 0x08;
        }

        // Max Weight Text Changed
        private void MaxWeightTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateSummary();
        }

        // Update Summary Display
        private void UpdateSummary()
        {
            try
            {
                // Update points
                if (SummaryPoints != null)
                    SummaryPoints.Text = ((int)PointCountSlider.Value).ToString();

                // Update mode - ALWAYS Weight-based
                if (SummaryMode != null)
                {
                    SummaryMode.Text = "Weight-based";
                }

                // Update order
                if (SummaryOrder != null)
                {
                    SummaryOrder.Text = _selectedPolynomialOrder.ToString();
                }

                // Update spacing
                if (SummarySpacing != null)
                {
                    if (AutoSpacingToggle.IsChecked == true)
                        SummarySpacing.Text = "Auto";
                    else if (ManualSpacingToggle.IsChecked == true)
                        SummarySpacing.Text = "Manual";
                    else
                        SummarySpacing.Text = "Auto"; // Default fallback
                }

                // Update weight
                if (SummaryWeight != null && MaxWeightTextBox != null)
                    SummaryWeight.Text = MaxWeightTextBox.Text + "kg";

                // Update channels
                if (SummaryChannels != null)
                {
                    var channels = new System.Collections.Generic.List<string>();
                    if (Channel1.IsChecked == true) channels.Add("Ch1");
                    if (Channel2.IsChecked == true) channels.Add("Ch2");
                    if (Channel3.IsChecked == true) channels.Add("Ch3");
                    if (Channel4.IsChecked == true) channels.Add("Ch4");

                    SummaryChannels.Text = channels.Count > 0 ? string.Join(", ", channels) : "None";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update summary error: {ex.Message}");
            }
        }

        // Reset Button
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Reset to defaults
                PointCountSlider.Value = 5;

                _selectedPolynomialOrder = 2;
                Poly1.IsChecked = false;
                Poly2.IsChecked = true;
                Poly3.IsChecked = false;
                Poly4.IsChecked = false;

                _autoSpacing = true;
                AutoSpacingToggle.IsChecked = true;
                ManualSpacingToggle.IsChecked = false;

                MaxWeightTextBox.Text = "1000.0";

                Channel1.IsChecked = false;
                Channel2.IsChecked = false;
                Channel3.IsChecked = true;
                Channel4.IsChecked = true;

                UpdateChannelMask();
                UpdateSummary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reset error: {ex.Message}");
            }
        }

        // Cancel Button
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // Start Calibration Button - UPDATED to remove mode parameter
        //private void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        // Validate inputs
        //        int pointCount = (int)PointCountSlider.Value;
        //        if (pointCount < 2 || pointCount > 20)
        //        {
        //            MessageBox.Show("Point count must be between 2 and 20.", "Invalid Input",
        //                          MessageBoxButton.OK, MessageBoxImage.Warning);
        //            return;
        //        }

        //        if (!double.TryParse(MaxWeightTextBox.Text, out double maxWeight) || maxWeight <= 0)
        //        {
        //            MessageBox.Show("Maximum weight must be a positive number.", "Invalid Input",
        //                          MessageBoxButton.OK, MessageBoxImage.Warning);
        //            return;
        //        }

        //        if (_channelMask == 0)
        //        {
        //            MessageBox.Show("At least one channel must be selected.", "Invalid Selection",
        //                          MessageBoxButton.OK, MessageBoxImage.Warning);
        //            return;
        //        }

        //        // Convert max weight to protocol format (kg * 10)
        //        ushort maxWeightProtocol = (ushort)(maxWeight * 10);

        //        // Send calibration command via parent window
        //        bool success = _parentWindow?.StartVariableCalibration(
        //            (byte)pointCount,
        //            _selectedPolynomialOrder,
        //            _autoSpacing,
        //            maxWeightProtocol,
        //            _channelMask) ?? false;

        //        if (success)
        //        {
        //            string spacingText = _autoSpacing ? "Auto" : "Manual";
        //            string channels = GetSelectedChannelsText();


        //            //var result =    MessageBox.Show($"Variable Calibration Started Successfully!\n\n" +
        //            //              $"Configuration Summary:\n" +
        //            //              $"• Points: {pointCount}\n" +
        //            //              $"• Mode: Weight-based\n" +
        //            //              $"• Polynomial Order: {_selectedPolynomialOrder}\n" +
        //            //              $"• Spacing: {spacingText}\n" +
        //            //              $"• Max Weight: {maxWeight:F1} kg\n" +
        //            //              $"• Channels: {channels}\n\n" +
        //            //              $"Opening weight calibration window...",
        //            //              "Calibration Started", MessageBoxButton.OK, MessageBoxImage.Information);

        //            //if (result == MessageBoxResult.Yes)
        //            //{

        //            //    // IMPORTANT: Close this window first, then open WeightCalibrationPoint
        //            //    this.DialogResult = true;
        //            //    this.Hide();

        //            //    // Open WeightCalibrationPoint window
        //            //    var weightCalPage = new WeightCalibrationPoint(
        //            //        pointCount,                 // totalPoints
        //            //        maxWeight,                  // maxKg
        //            //        _channelMask,              // channels
        //            //        _selectedPolynomialOrder   // polyOrder
        //            //    );


        //            //    weightCalPage.Owner = _parentWindow;
        //            //    weightCalPage.Show();

        //            //    this.Close();
        //            //}
        //        }
        //        else
        //        {
        //            MessageBox.Show("Failed to start calibration.\n\nPlease check:\n• CAN connection is active\n• Hardware is responding\n• No other calibration in progress",
        //                          "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show($"Error starting calibration:\n\n{ex.Message}",
        //                      "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //}

        // MODIFIED: Start Calibration Button - with WeightCalibrationPoint opening
        private void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                int pointCount = (int)PointCountSlider.Value;
                if (pointCount < 2 || pointCount > 20)
                {
                    MessageBox.Show("Point count must be between 2 and 20.", "Invalid Input",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!double.TryParse(MaxWeightTextBox.Text, out double maxWeight) || maxWeight <= 0)
                {
                    MessageBox.Show("Maximum weight must be a positive number.", "Invalid Input",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_channelMask == 0)
                {
                    MessageBox.Show("At least one channel must be selected.", "Invalid Selection",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Convert max weight to protocol format (kg * 10)
                ushort maxWeightProtocol = (ushort)(maxWeight * 10);

                // Send calibration command via parent window
                bool success = _parentWindow?.StartVariableCalibration(
                    (byte)pointCount,
                    _selectedPolynomialOrder,
                    _autoSpacing,
                    maxWeightProtocol,
                    _channelMask) ?? false;

                if (success)
                {
                    string spacingText = _autoSpacing ? "Auto" : "Manual";
                    string channels = GetSelectedChannelsText();

                    // UNCOMMENTED and MODIFIED: Show confirmation and open WeightCalibrationPoint
                    var result = MessageBox.Show($"Variable Calibration Started Successfully!\n\n" +
                                  $"Configuration Summary:\n" +
                                  $"• Points: {pointCount}\n" +
                                  $"• Mode: Weight-based\n" +
                                  $"• Polynomial Order: {_selectedPolynomialOrder}\n" +
                                  $"• Spacing: {spacingText}\n" +
                                  $"• Max Weight: {maxWeight:F1} kg\n" +
                                  $"• Channels: {channels}\n\n" +
                                  $"Opening weight calibration window...",
                                  "Calibration Started", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (result == MessageBoxResult.OK)
                    {
                        // Open WeightCalibrationPoint window with FOCUS MANAGEMENT
                        var weightCalPage = new WeightCalibrationPoint(
                            pointCount,                 // totalPoints
                            maxWeight,                  // maxKg
                            _channelMask,              // channels
                            _selectedPolynomialOrder   // polyOrder
                        );

                        // ADDED: FOCUS MANAGEMENT for WeightCalibrationPoint
                        weightCalPage.Owner = _parentWindow; // Set parent relationship
                        weightCalPage.Topmost = true; // Ensure it comes to front
                        weightCalPage.Show(); // Show window
                        weightCalPage.Activate(); // Activate window
                        weightCalPage.Focus(); // Give focus
                        weightCalPage.BringIntoView(); // Bring into view

                        System.Diagnostics.Debug.WriteLine("WeightCalibrationPoint opened with focus");

                        // Close current calibration setup window
                        this.DialogResult = true;
                        this.Close();
                    }
                }
                else
                {
                    MessageBox.Show("Failed to start calibration.\n\nPlease check:\n• CAN connection is active\n• Hardware is responding\n• No other calibration in progress",
                                  "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting calibration:\n\n{ex.Message}",
                              "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private string GetSelectedChannelsText()
        {
            var channels = new System.Collections.Generic.List<string>();
            if (Channel1.IsChecked == true) channels.Add("Ch1");
            if (Channel2.IsChecked == true) channels.Add("Ch2");
            if (Channel3.IsChecked == true) channels.Add("Ch3");
            if (Channel4.IsChecked == true) channels.Add("Ch4");

            return channels.Count > 0 ? string.Join(", ", channels) : "None Selected";
        }

        protected override void OnClosed(EventArgs e)
        {
            // ADDED: Unsubscribe events to prevent memory leaks
            this.Activated -= CalibrationWindow_Activated;
            this.Deactivated -= CalibrationWindow_Deactivated;

            System.Diagnostics.Debug.WriteLine("CalibrationWindow closed and cleaned up");

            base.OnClosed(e);
        }


    }
}