using System.Windows;

namespace SuspensionPCB_CAN_WPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling
            this.DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show($"System Error: {args.Exception.Message}",
                               "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup code here if needed
            base.OnExit(e);
        }
    }
}