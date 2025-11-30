using System.Windows;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class SettingsInfoDialog : Window
    {
        public new string Title { get; set; }
        public string InfoContent { get; set; }

        public SettingsInfoDialog(string title, string content)
        {
            InitializeComponent();
            Title = title;
            InfoContent = content;
            DataContext = this;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

