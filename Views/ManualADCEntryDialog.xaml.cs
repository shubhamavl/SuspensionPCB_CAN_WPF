using System;
using System.Windows;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class ManualADCEntryDialog : Window
    {
        public int InternalADC { get; private set; }
        public int ADS1115ADC { get; private set; }
        
        public ManualADCEntryDialog(double weight)
        {
            InitializeComponent();
            WeightTxt.Text = $"{weight:F0} kg";
        }
        
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Internal ADC combined values (Ch0+Ch1 or Ch2+Ch3) are unsigned: 0-8190
                if (int.TryParse(InternalADCTxt.Text, out int internalADC))
                {
                    if (internalADC < 0 || internalADC > 8190)
                    {
                        MessageBox.Show("Invalid Internal ADC value. Please enter a number between 0 and 8190.", 
                                      "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    InternalADC = internalADC;
                }
                else
                {
                    MessageBox.Show("Invalid Internal ADC value. Please enter a valid number between 0 and 8190.", 
                                  "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // ADS1115 combined values are signed 32-bit: -65536 to +65534
                if (int.TryParse(ADS1115ADCTxt.Text, out int ads1115ADC))
                {
                    if (ads1115ADC < -65536 || ads1115ADC > 65534)
                    {
                        MessageBox.Show("Invalid ADS1115 ADC value. Please enter a number between -65536 and +65534.", 
                                      "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ADS1115ADC = ads1115ADC;
                }
                else
                {
                    MessageBox.Show("Invalid ADS1115 ADC value. Please enter a valid number between -65536 and +65534.", 
                                  "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

