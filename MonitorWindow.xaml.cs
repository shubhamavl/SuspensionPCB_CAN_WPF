using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SuspensionPCB_CAN_WPF
{
    public partial class MonitorWindow : Window
    {
        private readonly ObservableCollection<CANMessageEntry> _messages;
        private DispatcherTimer? _updateTimer;
        private bool _isMonitoring = false;

        public MonitorWindow()
        {
            InitializeComponent();
            
            _messages = new ObservableCollection<CANMessageEntry>();
            MessageListBox.ItemsSource = _messages;

            InitializeUI();
        }

        private void InitializeUI()
        {
            UpdateMessageCount();
        }

        private void StartMonitorBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isMonitoring = true;
                StartMonitorBtn.IsEnabled = false;
                StopMonitorBtn.IsEnabled = true;
                MonitorStatusTxt.Text = "Monitoring...";
                MonitorStatusTxt.Foreground = System.Windows.Media.Brushes.Green;

                // Start update timer
                _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _updateTimer.Tick += UpdateTimer_Tick;
                _updateTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Start monitor error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopMonitorBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isMonitoring = false;
                StartMonitorBtn.IsEnabled = true;
                StopMonitorBtn.IsEnabled = false;
                MonitorStatusTxt.Text = "Stopped";
                MonitorStatusTxt.Foreground = System.Windows.Media.Brushes.Red;

                // Stop update timer
                _updateTimer?.Stop();
                _updateTimer = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Stop monitor error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearMonitorBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _messages.Clear();
                UpdateMessageCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Clear monitor error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!_isMonitoring) return;

                // Simulate CAN message monitoring
                // In a real implementation, this would connect to the CAN service
                // and display actual CAN messages
                
                // For now, just show a placeholder message
                if (_messages.Count == 0)
                {
                    AddMessage("TX", "0x100", "01 02 03 04", "Stream Start Request");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update timer error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddMessage(string direction, string canId, string data, string description)
        {
            try
            {
                var message = new CANMessageEntry
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                    Direction = direction,
                    CanId = canId,
                    Data = data,
                    Description = description
                };

                _messages.Add(message);

                // Keep only last 1000 messages
                while (_messages.Count > 1000)
                {
                    _messages.RemoveAt(0);
                }

                // Auto-scroll to bottom
                if (MessageListBox.Items.Count > 0)
                {
                    MessageListBox.ScrollIntoView(MessageListBox.Items[MessageListBox.Items.Count - 1]);
                }

                UpdateMessageCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Add message error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateMessageCount()
        {
            MessageCountTxt.Text = $"{_messages.Count} messages";
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isMonitoring = false;
                _updateTimer?.Stop();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Close error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _isMonitoring = false;
                _updateTimer?.Stop();
                base.OnClosed(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Window close error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// CAN message entry for monitoring display
    /// </summary>
    public class CANMessageEntry
    {
        public string Timestamp { get; set; } = "";
        public string Direction { get; set; } = "";
        public string CanId { get; set; } = "";
        public string Data { get; set; } = "";
        public string Description { get; set; } = "";
    }
}