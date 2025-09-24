using System.Windows;

namespace SuspensionPCB_CAN_WPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling for AVL LMS
            this.DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show($"AVL LMS Error: {args.Exception.Message}",
                               "AVL India - System Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // Show splash screen (optional)
            ShowSplashScreen();
        }

        private void ShowSplashScreen()
        {
            // Optional: You can create a splash screen here
            // For now, just show a simple message
            var splashResult = MessageBox.Show(
                "AVL India LMS - LMV Station-3\n\n" +
                "CAN Bus Suspension PCB Emulator\n" +
                "Version 1.0\n\n" +
                "Loading application...",
                "AVL LMS Startup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup code here if needed
            base.OnExit(e);
        }
    }
}