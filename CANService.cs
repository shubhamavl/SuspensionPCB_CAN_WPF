using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

namespace SuspensionPCB_CAN_WPF
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
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
        private bool _timeoutNotified = false;

        // v0.7 Ultra-Minimal CAN Protocol - Semantic IDs & Maximum Efficiency
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
        private const uint CAN_MSG_ID_MODE_INTERNAL = 0x030;      // Switch to Internal ADC mode (empty message)
        private const uint CAN_MSG_ID_MODE_ADS1115 = 0x031;       // Switch to ADS1115 mode (empty message)

        // Rate Selection Codes
        private const byte CAN_RATE_100HZ = 0x01;  // 100Hz (10ms interval)
        private const byte CAN_RATE_500HZ = 0x02;  // 500Hz (2ms interval)
        private const byte CAN_RATE_1KHZ = 0x03;   // 1kHz (1ms interval)
        private const byte CAN_RATE_1HZ = 0x05;    // 1Hz (1000ms interval)

        public event Action<CANMessage>? MessageReceived;
        public event EventHandler<string>? DataTimeout;
        
        // v0.7 Events
        public event EventHandler<RawDataEventArgs>? RawDataReceived;
        public event EventHandler<SystemStatusEventArgs>? SystemStatusReceived;

        public bool IsConnected => _connected;

        public CANService()
        {
            _connected = false;
            _instance = this;
        }

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

        // Check if message ID belongs to suspension system protocol (v0.7 - Semantic IDs)
        private bool IsSuspensionMessage(uint canId)
        {
            switch (canId)
            {
                // v0.7 Ultra-Minimal Protocol - Semantic IDs
                case CAN_MSG_ID_LEFT_RAW_DATA:      // 0x200 - Left side raw ADC data
                case CAN_MSG_ID_RIGHT_RAW_DATA:     // 0x201 - Right side raw ADC data
                case CAN_MSG_ID_START_LEFT_STREAM:  // 0x040 - Start left side streaming
                case CAN_MSG_ID_START_RIGHT_STREAM: // 0x041 - Start right side streaming
                case CAN_MSG_ID_STOP_ALL_STREAMS:   // 0x044 - Stop all streaming
                case CAN_MSG_ID_SYSTEM_STATUS:      // 0x300 - System status
                case CAN_MSG_ID_MODE_INTERNAL:      // 0x030 - Switch to Internal ADC mode
                case CAN_MSG_ID_MODE_ADS1115:       // 0x031 - Switch to ADS1115 mode
                    return true;

                default:
                    return false;
            }
        }

        public bool SendMessage(uint id, byte[] data)
        {
            if (!_connected || _serialPort == null) return false;

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

                var frame = CreateFrame(id, data);

                lock (_sendLock)
                {
                    _serialPort.Write(frame, 0, frame.Length);
                }

                ProductionLogger.Instance.LogInfo($"USB-CAN-A: Sent CAN frame ID=0x{id:X3}, Data={BitConverter.ToString(data ?? new byte[0])}", "CANService");
                return true;
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

        // v0.7 Stream Control Methods - Semantic IDs
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

        // Static methods for easy access from all windows
        public static bool SendStaticMessage(uint canId, byte[] data)
        {
            try
            {
                ProductionLogger.Instance.LogInfo($"CAN TX: ID=0x{canId:X3}, Data=[{string.Join(",", Array.ConvertAll(data, b => $"0x{b:X2}"))}]", "CANService");

                if (_instance != null)
                {
                    var message = new CANMessage(canId, data);
                    _instance.MessageReceived?.Invoke(message);

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

        #region Event Firing Logic for v0.7 Protocol - Semantic IDs
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
                    if (canData.Length >= 3)
                    {
                        SystemStatusReceived?.Invoke(this, new SystemStatusEventArgs
                        {
                            SystemStatus = canData[0],
                            ErrorFlags = canData[1],
                            ADCMode = canData[2],
                            Timestamp = DateTime.Now
                        });
                    }
                    break;
            }
        }
        #endregion

        public void Dispose()
        {
            Disconnect();
            _serialPort?.Dispose();
        }

    }  // Class closing brace

    // v0.7 Event Args Classes - Ultra-Minimal
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

    public class CANErrorEventArgs : EventArgs
    {
        public string? ErrorMessage { get; set; }
        public int ErrorCode { get; set; }
        public DateTime Timestamp { get; set; }
    }

}  // Namespace closing brace