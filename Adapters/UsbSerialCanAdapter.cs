using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using SuspensionPCB_CAN_WPF.Models;

namespace SuspensionPCB_CAN_WPF.Adapters
{
    /// <summary>
    /// USB-CAN-A Serial adapter implementation using SerialPort
    /// </summary>
    public class UsbSerialCanAdapter : ICanAdapter
    {
        public string AdapterType => "USB-CAN-A Serial";

        private SerialPort? _serialPort;
        private readonly ConcurrentQueue<byte> _frameBuffer = new();
        private volatile bool _connected;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _sendLock = new object();
        private DateTime _lastMessageTime = DateTime.MinValue;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
        private bool _timeoutNotified = false;

        // Protocol constants
        private const byte FRAME_HEADER = 0xAA;
        private const byte FRAME_FOOTER = 0x55;
        private const uint MAX_CAN_ID = 0x7FF; // 11-bit CAN ID limit

        public bool IsConnected => _connected;

        public event Action<CANMessage>? MessageReceived;
        public event EventHandler<string>? DataTimeout;
        public event EventHandler<bool>? ConnectionStatusChanged;

        public bool Connect(CanAdapterConfig config, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (config is not UsbSerialCanAdapterConfig usbConfig)
            {
                errorMessage = "Invalid configuration type for USB-CAN-A Serial adapter";
                return false;
            }

            try
            {
                if (string.IsNullOrEmpty(usbConfig.PortName))
                {
                    // Auto-detect COM port
                    string[] availablePorts = SerialPort.GetPortNames();
                    if (availablePorts.Length == 0)
                    {
                        errorMessage = "No COM ports found. Please check:\n• USB-CAN-A is connected\n• CH341 driver is installed\n• Device appears in Device Manager";
                        return false;
                    }
                    usbConfig.PortName = availablePorts[availablePorts.Length - 1];
                }

                _serialPort = new SerialPort(usbConfig.PortName, usbConfig.SerialBaudRate, Parity.None, 8, StopBits.One);
                _serialPort.Open();
                _connected = true;

                _cancellationTokenSource = new CancellationTokenSource();
                Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token));

                ConnectionStatusChanged?.Invoke(this, true);
                System.Diagnostics.Debug.WriteLine($"USB-CAN-A Connected on {usbConfig.PortName}");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                System.Diagnostics.Debug.WriteLine($"USB-CAN-A connection error: {ex.Message}");
                _connected = false;
                ConnectionStatusChanged?.Invoke(this, false);
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

            ConnectionStatusChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine("USB-CAN-A Disconnected");
        }

        public bool SendMessage(uint id, byte[] data)
        {
            if (!_connected || _serialPort == null) return false;

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

                var frame = CreateFrame(id, data ?? new byte[0]);

                lock (_sendLock)
                {
                    _serialPort.Write(frame, 0, frame.Length);
                }

                // Fire event for TX messages
                var txMessage = new CANMessage(id, data ?? new byte[0], DateTime.Now, "TX");
                MessageReceived?.Invoke(txMessage);

                System.Diagnostics.Debug.WriteLine($"USB-CAN-A: Sent CAN frame ID=0x{id:X3}, Data={BitConverter.ToString(data ?? new byte[0])}");
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
            return SerialPort.GetPortNames();
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
                if (!_frameBuffer.TryPeek(out byte first) || first != FRAME_HEADER)
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

        private void DecodeFrame(byte[] frame)
        {
            if (frame.Length < 18 || frame[0] != FRAME_HEADER)
                return;

            try
            {
                // Extract real CAN ID (bytes 5 + 6)
                uint canId = (uint)(frame[5] | (frame[6] << 8));

                // Extract CAN data (bytes 10..17 → exactly 8 bytes)
                byte[] canData = new byte[8];
                Array.Copy(frame, 10, canData, 0, 8);

                // Process suspension system messages
                if (IsSuspensionMessage(canId))
                {
                    var canMessage = new CANMessage(canId, canData);
                    MessageReceived?.Invoke(canMessage);

                    System.Diagnostics.Debug.WriteLine($"Processed: ID=0x{canId:X3}, Data={BitConverter.ToString(canData)}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Decode error: {ex.Message}");
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

        private static byte[] CreateFrame(uint id, byte[] data)
        {
            var frame = new List<byte>
            {
                FRAME_HEADER,
                (byte)(0xC0 | Math.Min(data?.Length ?? 0, 8)),
                (byte)(id & 0xFF),
                (byte)((id >> 8) & 0xFF)
            };

            frame.AddRange((data ?? new byte[0]).Take(8));
            while (frame.Count < 12)
                frame.Add(0x00);

            frame.Add(FRAME_FOOTER);
            return frame.ToArray();
        }

        public void Dispose()
        {
            Disconnect();
            _serialPort?.Dispose();
        }
    }
}


