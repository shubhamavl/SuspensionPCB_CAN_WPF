using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Peak.Can.Basic;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// PCAN adapter implementation using Peak.PCANBasic.NET NuGet package
    /// </summary>
    public class PcanCanAdapter : ICanAdapter
    {
        public string AdapterType => "PCAN";

        private PcanChannel _channel = PcanChannel.Usb01;
        private volatile bool _connected;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _sendLock = new object();
        private DateTime _lastMessageTime = DateTime.MinValue;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
        private bool _timeoutNotified = false;

        private const uint MAX_CAN_ID = 0x7FF; // 11-bit CAN ID limit

        public bool IsConnected => _connected;

        public event Action<CANMessage>? MessageReceived;
        public event EventHandler<string>? DataTimeout;
        public event EventHandler<bool>? ConnectionStatusChanged;

        // PCAN Channel constants - mapped to PcanChannel enum
        public const ushort PCAN_USBBUS1 = 0x51;
        public const ushort PCAN_USBBUS2 = 0x52;
        public const ushort PCAN_USBBUS3 = 0x53;
        public const ushort PCAN_USBBUS4 = 0x54;
        public const ushort PCAN_USBBUS5 = 0x55;
        public const ushort PCAN_USBBUS6 = 0x56;
        public const ushort PCAN_USBBUS7 = 0x57;
        public const ushort PCAN_USBBUS8 = 0x58;

        // PCAN Bitrate constants - mapped to Bitrate enum
        public const ushort PCAN_BAUD_1M = 0x0014;
        public const ushort PCAN_BAUD_500K = 0x001C;
        public const ushort PCAN_BAUD_250K = 0x011C;
        public const ushort PCAN_BAUD_125K = 0x031C;
        public const ushort PCAN_BAUD_100K = 0x432F;
        public const ushort PCAN_BAUD_50K = 0x472F;
        public const ushort PCAN_BAUD_20K = 0x532F;
        public const ushort PCAN_BAUD_10K = 0x672F;
        public const ushort PCAN_BAUD_5K = 0x7F7F;

        public bool Connect(CanAdapterConfig config, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (config is not PcanCanAdapterConfig pcanConfig)
            {
                errorMessage = "Invalid configuration type for PCAN adapter";
                return false;
            }

            try
            {
                // Convert channel from ushort to PcanChannel enum
                _channel = ConvertToPcanChannel(pcanConfig.Channel);
                
                // Convert bitrate from ushort to Bitrate enum
                Bitrate bitrate = ConvertToBitrate(pcanConfig.PcanBitrate);

                // Initialize PCAN channel
                PcanStatus status = Api.Initialize(_channel, bitrate);

                if (status == PcanStatus.OK)
                {
                    _connected = true;
                    _cancellationTokenSource = new CancellationTokenSource();
                    Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token));

                    ConnectionStatusChanged?.Invoke(this, true);
                    System.Diagnostics.Debug.WriteLine($"PCAN Connected on channel {_channel}");
                    return true;
                }
                else
                {
                    errorMessage = GetPcanErrorString(status);
                    _connected = false;
                    ConnectionStatusChanged?.Invoke(this, false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"PCAN connection error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"PCAN connection error: {ex.Message}");
                _connected = false;
                ConnectionStatusChanged?.Invoke(this, false);
                return false;
            }
        }

        public void Disconnect()
        {
            _connected = false;
            _cancellationTokenSource?.Cancel();

            if (_channel != PcanChannel.None)
            {
                try
                {
                    Api.Uninitialize(_channel);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PCAN Uninitialize error: {ex.Message}");
                }
                _channel = PcanChannel.Usb01; // Reset to default
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ConnectionStatusChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine("PCAN Disconnected");
        }

        public bool SendMessage(uint id, byte[] data)
        {
            if (!_connected || _channel == PcanChannel.None) return false;

            try
            {
                // Validate CAN ID (11-bit max for standard frame)
                if (id > MAX_CAN_ID)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid CAN ID: 0x{id:X3} (max 0x{MAX_CAN_ID:X3} for standard frame)");
                    return false;
                }

                // Validate data length
                if (data != null && data.Length > 8)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid data length: {data.Length} (max 8 bytes)");
                    return false;
                }

                // Create PCAN message
                PcanMessage msg = new PcanMessage(
                    id,
                    MessageType.Standard,
                    (byte)(data?.Length ?? 0),
                    data ?? new byte[0],
                    false
                );

                lock (_sendLock)
                {
                    PcanStatus status = Api.Write(_channel, msg);
                    if (status != PcanStatus.OK)
                    {
                        System.Diagnostics.Debug.WriteLine($"PCAN Write error: {GetPcanErrorString(status)}");
                        return false;
                    }
                }

                // Fire event for TX messages
                var txMessage = new CANMessage(id, data ?? new byte[0], DateTime.Now);
                MessageReceived?.Invoke(txMessage);

                System.Diagnostics.Debug.WriteLine($"PCAN: Sent CAN frame ID=0x{id:X3}, Data={BitConverter.ToString(data ?? new byte[0])}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send message error: {ex.Message}");
                return false;
            }
        }

        public string[] GetAvailableOptions()
        {
            var channels = new List<string>();
            var testChannels = new PcanChannel[] 
            { 
                PcanChannel.Usb01, PcanChannel.Usb02, PcanChannel.Usb03, PcanChannel.Usb04,
                PcanChannel.Usb05, PcanChannel.Usb06, PcanChannel.Usb07, PcanChannel.Usb08
            };

            foreach (var channel in testChannels)
            {
                try
                {
                    PcanStatus status = Api.GetValue(channel, PcanParameter.ChannelCondition, out uint value);
                    if (status == PcanStatus.OK && value == 1) // Channel available
                    {
                        string channelName = channel.ToString().Replace("Usb", "USB");
                        channels.Add(channelName);
                    }
                }
                catch
                {
                    // Channel not available
                }
            }

            return channels.Count > 0 ? channels.ToArray() : new[] { "USB1" };
        }

        private async Task ReadMessagesAsync(CancellationToken token)
        {
            _lastMessageTime = DateTime.UtcNow;

            while (_connected && !token.IsCancellationRequested)
            {
                try
                {
                    PcanMessage msg = new PcanMessage();
                    PcanStatus status = Api.Read(_channel, out msg);

                    if (status == PcanStatus.OK)
                    {
                        // Process received message
                        byte[] data = new byte[msg.Length];
                        Array.Copy(msg.Data, data, msg.Length);

                        if (IsSuspensionMessage(msg.ID))
                        {
                            var canMessage = new CANMessage(msg.ID, data, DateTime.Now);
                            MessageReceived?.Invoke(canMessage);
                            System.Diagnostics.Debug.WriteLine($"PCAN: Received ID=0x{msg.ID:X3}, Data={BitConverter.ToString(data)}");
                        }

                        _lastMessageTime = DateTime.UtcNow;
                        _timeoutNotified = false;
                    }
                    else if (status != PcanStatus.ReceiveQueueEmpty)
                    {
                        System.Diagnostics.Debug.WriteLine($"PCAN Read error: {GetPcanErrorString(status)}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PCAN Read exception: {ex.Message}");
                }

                // Check for timeout
                if (!_timeoutNotified && DateTime.UtcNow - _lastMessageTime > _timeout)
                {
                    _timeoutNotified = true;
                    DataTimeout?.Invoke(this, "Timeout");
                }

                await Task.Delay(1, token); // Small delay to prevent CPU spinning
            }
        }

        private bool IsSuspensionMessage(uint canId)
        {
            // Explicitly block 0x500 messages
            if (canId == 0x500)
                return false;

            switch (canId)
            {
                // Weight data responses
                case 0x200:  // Suspension weight data
                case 0x201:  // Axle weight data

                // Calibration responses
                case 0x400:  // Variable calibration data
                case 0x401:  // Calibration quality analysis
                case 0x402:  // Error response
                    return true;

                default:
                    return false;
            }
        }

        private bool IsPcanAvailable()
        {
            try
            {
                // Try to get channel condition for USB1 to check if PCAN is available
                PcanStatus status = Api.GetValue(PcanChannel.Usb01, PcanParameter.ChannelCondition, out uint value);
                return status == PcanStatus.OK;
            }
            catch
            {
                return false;
            }
        }

        private string GetPcanErrorString(PcanStatus status)
        {
            try
            {
                PcanStatus result = Api.GetErrorText(status, out string errorText);
                if (result == PcanStatus.OK && !string.IsNullOrEmpty(errorText))
                {
                    return errorText;
                }
                return $"PCAN Error Code: {status}";
            }
            catch
            {
                return $"PCAN Error Code: {status}";
            }
        }

        private PcanChannel ConvertToPcanChannel(ushort channelValue)
        {
            return channelValue switch
            {
                0x51 => PcanChannel.Usb01,
                0x52 => PcanChannel.Usb02,
                0x53 => PcanChannel.Usb03,
                0x54 => PcanChannel.Usb04,
                0x55 => PcanChannel.Usb05,
                0x56 => PcanChannel.Usb06,
                0x57 => PcanChannel.Usb07,
                0x58 => PcanChannel.Usb08,
                _ => PcanChannel.Usb01
            };
        }

        private Bitrate ConvertToBitrate(ushort bitrateValue)
        {
            return bitrateValue switch
            {
                0x0014 => Bitrate.Pcan1000,    // 1 Mbps
                0x001C => Bitrate.Pcan500,     // 500 kbps
                0x011C => Bitrate.Pcan250,     // 250 kbps
                0x031C => Bitrate.Pcan125,     // 125 kbps
                0x432F => Bitrate.Pcan100,     // 100 kbps
                0x472F => Bitrate.Pcan50,      // 50 kbps
                0x532F => Bitrate.Pcan20,      // 20 kbps
                0x672F => Bitrate.Pcan10,      // 10 kbps
                0x7F7F => Bitrate.Pcan5,       // 5 kbps
                _ => Bitrate.Pcan500           // Default 500 kbps
            };
        }
    }
}
