using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Adapters;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Services
{
    // USB-CAN-A Binary Protocol Implementation for Suspension System
    public class CANService
    {
        private const uint MAX_CAN_ID = 0x7FF; // 11-bit standard CAN ID maximum
        
        private SerialPort? _serialPort;
        public static CANService? _instance;
        private readonly ConcurrentQueue<byte> _frameBuffer = new();
        public volatile bool _connected;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _sendLock = new object();
        private DateTime _lastMessageTime = DateTime.MinValue;
        private TimeSpan _timeout = TimeSpan.FromSeconds(5); // Configurable timeout
        private bool _timeoutNotified = false;

        // v1.0 Ultra-Minimal CAN Protocol - Semantic IDs & Maximum Efficiency
        // Raw Data: 2 bytes only (75% reduction from 8 bytes)
        // Stream Control: 1 byte only (87.5% reduction from 8 bytes)
        // System Status: 3 bytes only (62.5% reduction from 8 bytes)

        // CAN Message IDs - Semantic IDs (message type encoded in ID)
        private const uint CAN_MSG_ID_LEFT_RAW_DATA = 0x200;      // Left side raw ADC data (Ch0+Ch1)
        private const uint CAN_MSG_ID_RIGHT_RAW_DATA = 0x201;     // Right side raw ADC data (Ch2+Ch3)
        private const uint CAN_MSG_ID_START_LEFT_STREAM = 0x040;  // Start left side streaming
        private const uint CAN_MSG_ID_START_RIGHT_STREAM = 0x041; // Start right side streaming
        private const uint CAN_MSG_ID_STOP_ALL_STREAMS = 0x044;   // Stop all streaming (empty message)
        private const uint CAN_MSG_ID_SYSTEM_STATUS = 0x300;      // System status (on-demand only)
        private const uint CAN_MSG_ID_STATUS_REQUEST = 0x032;     // Request system status (empty message)
        private const uint CAN_MSG_ID_MODE_INTERNAL = 0x030;      // Switch to Internal ADC mode (empty message)
        private const uint CAN_MSG_ID_MODE_ADS1115 = 0x031;       // Switch to ADS1115 mode (empty message)
        private const uint CAN_MSG_ID_VERSION_REQUEST = 0x033;    // Request firmware version (empty message)
        private const uint CAN_MSG_ID_VERSION_RESPONSE = 0x301;   // Firmware version response (4 bytes: major, minor, patch, build)

        // Bootloader protocol IDs
        private const uint CAN_MSG_ID_BOOT_COMMAND = BootloaderProtocol.CanIdBootCommand;
        private const uint CAN_MSG_ID_BOOT_DATA = BootloaderProtocol.CanIdBootData;
        private const uint CAN_MSG_ID_BOOT_STATUS = BootloaderProtocol.CanIdBootStatus;
        private const uint CAN_MSG_ID_BOOT_INFO = BootloaderProtocol.CanIdBootInfo;

        // Rate Selection Codes
        private const byte CAN_RATE_100HZ = 0x01;  // 100Hz (10ms interval)
        private const byte CAN_RATE_500HZ = 0x02;  // 500Hz (2ms interval)
        private const byte CAN_RATE_1KHZ = 0x03;   // 1kHz (1ms interval)
        private const byte CAN_RATE_1HZ = 0x05;    // 1Hz (1000ms interval)

        public event Action<CANMessage>? MessageReceived;
        public event EventHandler<string>? DataTimeout;
        
        // v1.0 Events
        public event EventHandler<RawDataEventArgs>? RawDataReceived;
        public event EventHandler<SystemStatusEventArgs>? SystemStatusReceived;
        public event EventHandler<FirmwareVersionEventArgs>? FirmwareVersionReceived;
        public event EventHandler<BootStatusEventArgs>? BootStatusReceived;

        public bool IsConnected => _connected;

        private ICanAdapter? _adapter;

        public CANService()
        {
            _connected = false;
            _instance = this;
        }

        /// <summary>
        /// Set the CAN adapter to use
        /// </summary>
        public void SetAdapter(ICanAdapter adapter)
        {
            if (_adapter != null)
            {
                _adapter.MessageReceived -= OnAdapterMessageReceived;
                _adapter.DataTimeout -= OnAdapterDataTimeout;
                _adapter.ConnectionStatusChanged -= OnAdapterConnectionStatusChanged;
                _adapter.Disconnect();
            }

            _adapter = adapter;
            _adapter.MessageReceived += OnAdapterMessageReceived;
            _adapter.DataTimeout += OnAdapterDataTimeout;
            _adapter.ConnectionStatusChanged += OnAdapterConnectionStatusChanged;
        }

        /// <summary>
        /// Connect using adapter configuration
        /// </summary>
        public bool Connect(CanAdapterConfig config, out string errorMessage)
        {
            ICanAdapter? adapter = null;

            if (config is UsbSerialCanAdapterConfig)
            {
                adapter = new UsbSerialCanAdapter();
            }
            else if (config is PcanCanAdapterConfig)
            {
                adapter = new PcanCanAdapter();
            }
            else if (config is NamedPipeCanAdapterConfig)
            {
                adapter = new NamedPipeCanAdapter();
            }
            else
            {
                errorMessage = "Unknown adapter configuration type";
                return false;
            }

            SetAdapter(adapter);
            bool result = adapter.Connect(config, out errorMessage);
            _connected = result;
            return result;
        }

        #region Adapter Event Handlers
        private void OnAdapterMessageReceived(CANMessage message)
        {
            MessageReceived?.Invoke(message);
            // Fire specific events for protocol messages
            // Note: Some messages (like status/version requests) may have empty data, but responses should have data
            if (message.Data != null)
            {
                FireSpecificEvents(message.ID, message.Data);
            }
        }

        private void OnAdapterDataTimeout(object? sender, string timeoutMessage)
        {
            DataTimeout?.Invoke(this, timeoutMessage);
        }

        private void OnAdapterConnectionStatusChanged(object? sender, bool connected)
        {
            _connected = connected;
        }
        #endregion

        public bool Connect(string portName, out string message, int baudRate = 2000000)
        {
            message = string.Empty;
            try
            {
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _serialPort.Open();
                _connected = true;

                _cancellationTokenSource = new CancellationTokenSource();
                Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token));

                ProductionLogger.Instance.LogInfo($"USB-CAN-A Connected on {portName}", "CANService");
                return true;
            }
            catch (Exception ex)
            {
                message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                ProductionLogger.Instance.LogError($"USB-CAN-A connection error: {ex.Message}", "CANService");
                return false;
            }
        }

        public bool Connect(ushort channel = 0, ushort baudRate = 250)
        {
            try
            {
                // Find available COM ports
                string[] availablePorts = SerialPort.GetPortNames();

                if (availablePorts.Length == 0)
                {
                    System.Windows.MessageBox.Show("No COM ports found!\n\n" +
                                                  "Please check:\n" +
                                                  "• USB-CAN-A is connected\n" +
                                                  "• CH341 driver is installed\n" +
                                                  "• Device appears in Device Manager",
                                                  "COM Port Not Found");
                    return false;
                }

                // Use the last COM port (usually the USB-CAN-A device)
                string selectedPort = availablePorts[availablePorts.Length - 1];
                string message;
                return Connect(selectedPort, out message);
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"USB-CAN-A connection error: {ex.Message}", "CANService");
                return false;
            }
        }

        public void Disconnect()
        {
            _connected = false;
            _cancellationTokenSource?.Cancel();
            if (_serialPort?.IsOpen == true)
                _serialPort.Close();

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ProductionLogger.Instance.LogInfo("USB-CAN-A Disconnected", "CANService");
        }

        private async Task ReadMessagesAsync(CancellationToken token)
        {
            var buffer = new byte[256];
            _lastMessageTime = DateTime.UtcNow;

            while (_connected && !token.IsCancellationRequested)
            {
                try
                {
                    if (_serialPort is { IsOpen: true } && _serialPort.BytesToRead > 0)
                    {
                        int count = _serialPort.Read(buffer, 0, buffer.Length);

                        for (int i = 0; i < count; i++)
                            _frameBuffer.Enqueue(buffer[i]);

                        ProcessFrames();

                        // Update last received time
                        _lastMessageTime = DateTime.UtcNow;
                        _timeoutNotified = false;
                    }
                }
                catch
                {
                    // ignore read errors for now
                }

                // Check for timeout
                if (!_timeoutNotified && DateTime.UtcNow - _lastMessageTime > _timeout)
                {
                    _timeoutNotified = true;
                    DataTimeout?.Invoke(this, "Timeout");
                }

                await Task.Delay(5, token);
            }
        }

        private void ProcessFrames()
        {
            while (_frameBuffer.Count >= 20)
            {
                if (!_frameBuffer.TryPeek(out byte first) || first != 0xAA)
                {
                    _frameBuffer.TryDequeue(out _);
                    continue;
                }

                if (_frameBuffer.Count < 20) break;

                var frame = new byte[20];
                for (int i = 0; i < 20; i++)
                    _frameBuffer.TryDequeue(out frame[i]);

                DecodeFrame(frame);
            }
        }

        // PERMANENT FIX: Frame decoding with proper CAN ID extraction (matching working steering code)
        private void DecodeFrame(byte[] frame)
        {
            if (frame.Length < 18 || frame[0] != 0xAA)
                return;

            try
            {
                // Extract real CAN ID (bytes 5 + 6) - EXACTLY like working steering code
                uint canId = (uint)(frame[5] | (frame[6] << 8));

                // Extract CAN data (bytes 10..17 → exactly 8 bytes) - EXACTLY like working steering code
                byte[] canData = new byte[8];
                Array.Copy(frame, 10, canData, 0, 8);

                // Description for debugging
                string description = $"CAN ID: 0x{canId:X3}, Data: {BitConverter.ToString(canData)}";

                // Process suspension system messages (expanded from steering code logic)
                if (IsSuspensionMessage(canId))
                {
                    var canMessage = new CANMessage(canId, canData);
                    MessageReceived?.Invoke(canMessage);

                    // Fire specific events for WeightCalibrationPoint
                    FireSpecificEvents(canId, canData);

                    ProductionLogger.Instance.LogInfo($"Processed: ID=0x{canId:X3}, Data={BitConverter.ToString(canData)}", "CANService");
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Decode error: {ex.Message}", "CANService");
            }
        }

        // Check if message ID belongs to suspension system protocol (v1.0 - Semantic IDs)
        private bool IsSuspensionMessage(uint canId)
        {
            switch (canId)
            {
                // v1.0 Ultra-Minimal Protocol - Semantic IDs
                case CAN_MSG_ID_LEFT_RAW_DATA:      // 0x200 - Left side raw ADC data
                case CAN_MSG_ID_RIGHT_RAW_DATA:     // 0x201 - Right side raw ADC data
                case CAN_MSG_ID_START_LEFT_STREAM:  // 0x040 - Start left side streaming
                case CAN_MSG_ID_START_RIGHT_STREAM: // 0x041 - Start right side streaming
                case CAN_MSG_ID_STOP_ALL_STREAMS:   // 0x044 - Stop all streaming
                case CAN_MSG_ID_SYSTEM_STATUS:      // 0x300 - System status
                case CAN_MSG_ID_STATUS_REQUEST:     // 0x032 - Request system status
                case CAN_MSG_ID_MODE_INTERNAL:      // 0x030 - Switch to Internal ADC mode
                case CAN_MSG_ID_MODE_ADS1115:       // 0x031 - Switch to ADS1115 mode
                case CAN_MSG_ID_VERSION_REQUEST:    // 0x033 - Request firmware version
                case CAN_MSG_ID_VERSION_RESPONSE:   // 0x301 - Firmware version response
                case CAN_MSG_ID_BOOT_STATUS:
                case CAN_MSG_ID_BOOT_INFO:
                    return true;

                default:
                    return false;
            }
        }

        public bool SendMessage(uint id, byte[] data)
        {
            if (!_connected || _adapter == null) return false;

            try
            {
                // Validate CAN ID (11-bit max for standard frame)
                if (id > MAX_CAN_ID)
                {
                    ProductionLogger.Instance.LogWarning($"Invalid CAN ID: 0x{id:X3} (max 0x{MAX_CAN_ID:X3} for standard frame)", "CANService");
                    return false;
                }

                // Validate data length
                if (data != null && data.Length > 8)
                {
                    ProductionLogger.Instance.LogWarning($"Invalid data length: {data.Length} (max 8 bytes)", "CANService");
                    return false;
                }

                if (data == null)
                {
                    ProductionLogger.Instance.LogWarning("Cannot send message: data is null", "CANService");
                    return false;
                }

                bool result = _adapter.SendMessage(id, data);

                // Fire event for TX messages so they appear in monitor (adapter may already fire this, but ensure it's fired)
                if (result)
                {
                    var txMessage = new CANMessage(id, data, "TX");
                    MessageReceived?.Invoke(txMessage);
                }

                ProductionLogger.Instance.LogInfo($"CAN: Sent CAN frame ID=0x{id:X3}, Data={BitConverter.ToString(data ?? new byte[0])}", "CANService");
                return result;
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Send message error: {ex.Message}", "CANService");
                return false;
            }
        }

        // Create frame using the working steering code method
        private static byte[] CreateFrame(uint id, byte[] data)
        {
            var frame = new List<byte>
            {
                0xAA,
                (byte)(0xC0 | Math.Min(data?.Length ?? 0, 8)),
                (byte)(id & 0xFF),
                (byte)((id >> 8) & 0xFF)
            };

            frame.AddRange((data ?? new byte[0]).Take(8));
            while (frame.Count < 12)
                frame.Add(0x00);

            frame.Add(0x55);
            return frame.ToArray();
        }

        // v1.0 Stream Control Methods - Semantic IDs
        public bool StartLeftStream(byte rate)
        {
            byte[] data = new byte[1];
            data[0] = rate;  // Rate selection
            
            return SendMessage(CAN_MSG_ID_START_LEFT_STREAM, data);
        }

        public bool StartRightStream(byte rate)
        {
            byte[] data = new byte[1];
            data[0] = rate;  // Rate selection
            
            return SendMessage(CAN_MSG_ID_START_RIGHT_STREAM, data);
        }

        public bool StopAllStreams()
        {
            // Empty message (0 bytes) for stop all streams
            return SendMessage(CAN_MSG_ID_STOP_ALL_STREAMS, new byte[0]);
        }

        public bool SwitchToInternalADC()
        {
            // Empty message (0 bytes) for mode switch
            return SendMessage(CAN_MSG_ID_MODE_INTERNAL, new byte[0]);
        }

        public bool SwitchToADS1115()
        {
            // Empty message (0 bytes) for mode switch
            return SendMessage(CAN_MSG_ID_MODE_ADS1115, new byte[0]);
        }

        /// <summary>
        /// Request system status from STM32 (on-demand)
        /// </summary>
        /// <returns>True if request sent successfully</returns>
        public bool RequestSystemStatus()
        {
            return SendMessage(CAN_MSG_ID_STATUS_REQUEST, new byte[0]);
        }

        /// <summary>
        /// Request firmware version from STM32 (on-demand)
        /// </summary>
        /// <returns>True if request sent successfully</returns>
        public bool RequestFirmwareVersion()
        {
            return SendMessage(CAN_MSG_ID_VERSION_REQUEST, new byte[0]);
        }

        // Static methods for easy access from all windows
        public static bool SendStaticMessage(uint canId, byte[] data)
        {
            try
            {
                ProductionLogger.Instance.LogInfo($"CAN TX: ID=0x{canId:X3}, Data=[{string.Join(",", Array.ConvertAll(data, b => $"0x{b:X2}"))}]", "CANService");

                if (_instance != null)
                {
                    if (_instance._connected)
                    {
                        bool success = _instance.SendMessage(canId, data);
                        return success;
                    }
                    else
                    {
                        ProductionLogger.Instance.LogWarning("CAN Send Error: Not connected to hardware", "CANService");
                        return false;
                    }
                }
                else
                {
                    ProductionLogger.Instance.LogWarning("CAN Send Error: CANService instance not available", "CANService");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"CAN Send Error: {ex.Message}", "CANService");
                return false;
            }
        }

        public bool RequestBootloaderInfo()
        {
            byte[] data = new byte[] { BootloaderProtocol.AppCmdQueryBootInfo };
            return SendMessage(CAN_MSG_ID_BOOT_COMMAND, data);
        }

        public bool RequestEnterBootloader()
        {
            byte[] data = new byte[] { BootloaderProtocol.AppCmdEnterBootloader };
            return SendMessage(CAN_MSG_ID_BOOT_COMMAND, data);
        }

        #region Event Firing Logic for v1.0 Protocol - Semantic IDs
        private void FireSpecificEvents(uint canId, byte[] canData)
        {
            switch (canId)
            {
                case CAN_MSG_ID_LEFT_RAW_DATA: // 0x200 - Left side raw ADC data
                    if (canData.Length >= 2)
                    {
                        ushort rawADC = (ushort)(canData[0] | (canData[1] << 8));
                        RawDataReceived?.Invoke(this, new RawDataEventArgs
                        {
                            Side = 0,  // Left side
                            RawADCSum = rawADC,
                            TimestampFull = DateTime.Now
                        });
                    }
                    break;

                case CAN_MSG_ID_RIGHT_RAW_DATA: // 0x201 - Right side raw ADC data
                    if (canData.Length >= 2)
                    {
                        ushort rawADC = (ushort)(canData[0] | (canData[1] << 8));
                        RawDataReceived?.Invoke(this, new RawDataEventArgs
                        {
                            Side = 1,  // Right side
                            RawADCSum = rawADC,
                            TimestampFull = DateTime.Now
                        });
                    }
                    break;

                case CAN_MSG_ID_SYSTEM_STATUS: // 0x300 - System status
                    if (canData != null && canData.Length >= 3)
                    {
                        SystemStatusReceived?.Invoke(this, new SystemStatusEventArgs
                        {
                            SystemStatus = canData[0],
                            ErrorFlags = canData[1],
                            ADCMode = canData[2],
                            Timestamp = DateTime.Now
                        });
                        ProductionLogger.Instance.LogInfo($"SystemStatus event fired: Status={canData[0]}, Errors=0x{canData[1]:X2}, ADC={canData[2]}", "CANService");
                    }
                    else
                    {
                        ProductionLogger.Instance.LogWarning($"SystemStatus message invalid: Data length={canData?.Length ?? 0}", "CANService");
                    }
                    break;

                case CAN_MSG_ID_VERSION_RESPONSE: // 0x301 - Firmware version response
                    if (canData != null && canData.Length >= 4)
                    {
                        FirmwareVersionReceived?.Invoke(this, new FirmwareVersionEventArgs
                        {
                            Major = canData[0],
                            Minor = canData[1],
                            Patch = canData[2],
                            Build = canData[3],
                            Timestamp = DateTime.Now
                        });
                        ProductionLogger.Instance.LogInfo($"FirmwareVersion event fired: {canData[0]}.{canData[1]}.{canData[2]}.{canData[3]}", "CANService");
                    }
                    else
                    {
                        ProductionLogger.Instance.LogWarning($"FirmwareVersion message invalid: Data length={canData?.Length ?? 0}", "CANService");
                    }
                    break;
                case CAN_MSG_ID_BOOT_STATUS:
                case CAN_MSG_ID_BOOT_INFO:
                    BootStatusReceived?.Invoke(this, new BootStatusEventArgs
                    {
                        RawData = canData.ToArray(),
                        Timestamp = DateTime.Now
                    });
                    break;
            }
        }
        #endregion

        /// <summary>
        /// Set data timeout for CAN communication
        /// </summary>
        /// <param name="timeout">Timeout duration</param>
        public void SetTimeout(TimeSpan timeout)
        {
            if (timeout.TotalSeconds < 1 || timeout.TotalSeconds > 300)
            {
                ProductionLogger.Instance.LogWarning($"Invalid timeout value: {timeout.TotalSeconds}s (must be 1-300 seconds)", "CANService");
                return;
            }
            
            _timeout = timeout;
            ProductionLogger.Instance.LogInfo($"CAN data timeout set to {timeout.TotalSeconds} seconds", "CANService");
        }

        public void Dispose()
        {
            Disconnect();
            _serialPort?.Dispose();
        }

    }  // Class closing brace

    // v1.0 Event Args Classes - Ultra-Minimal
    public class RawDataEventArgs : EventArgs
    {
        public byte Side { get; set; }              // 0=Left, 1=Right
        public ushort RawADCSum { get; set; }       // Raw ADC sum (Ch0+Ch1 or Ch2+Ch3)
        public DateTime TimestampFull { get; set; } // PC3 reception timestamp
    }

    public class SystemStatusEventArgs : EventArgs
    {
        public byte SystemStatus { get; set; }      // 0=OK, 1=Warning, 2=Error
        public byte ErrorFlags { get; set; }        // Error flags
        public byte ADCMode { get; set; }           // Current ADC mode (0=Internal, 1=ADS1115)
        public DateTime Timestamp { get; set; }     // PC3 reception timestamp
    }

    public class FirmwareVersionEventArgs : EventArgs
    {
        public byte Major { get; set; }              // Major version number
        public byte Minor { get; set; }              // Minor version number
        public byte Patch { get; set; }              // Patch version number
        public byte Build { get; set; }              // Build number
        public DateTime Timestamp { get; set; }      // PC3 reception timestamp
        
        public string VersionString => $"{Major}.{Minor}.{Patch}";
        public string VersionStringFull => $"{Major}.{Minor}.{Patch}.{Build}";
    }

    public class BootStatusEventArgs : EventArgs
    {
        public byte[] RawData { get; set; } = Array.Empty<byte>();
        public DateTime Timestamp { get; set; }
    }

    public class CANErrorEventArgs : EventArgs
    {
        public string? ErrorMessage { get; set; }
        public int ErrorCode { get; set; }
        public DateTime Timestamp { get; set; }
    }

}  // Namespace closing brace