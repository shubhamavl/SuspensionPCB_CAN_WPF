using System.Windows;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class SideSelectionDialog : Window
    {
        public string SelectedSide { get; private set; } = "";
        
        public SideSelectionDialog()
        {
            InitializeComponent();
        }
        
        private void LeftBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectedSide = "Left";
            DialogResult = true;
            Close();
        }
        
        private void RightBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectedSide = "Right";
            DialogResult = true;
            Close();
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

