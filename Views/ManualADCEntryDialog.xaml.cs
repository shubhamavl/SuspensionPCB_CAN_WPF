using System;
using System.Windows;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class ManualADCEntryDialog : Window
    {
        public ushort InternalADC { get; private set; }
        public ushort ADS1115ADC { get; private set; }
        
        public ManualADCEntryDialog(double weight)
        {
            InitializeComponent();
            WeightTxt.Text = $"{weight:F0} kg";
        }
        
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ushort.TryParse(InternalADCTxt.Text, out ushort internalADC))
                {
                    InternalADC = internalADC;
                }
                else
                {
                    MessageBox.Show("Invalid Internal ADC value. Please enter a number between 0 and 65535.", 
                                  "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (ushort.TryParse(ADS1115ADCTxt.Text, out ushort ads1115ADC))
                {
                    ADS1115ADC = ads1115ADC;
                }
                else
                {
                    MessageBox.Show("Invalid ADS1115 ADC value. Please enter a number between 0 and 65535.", 
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

