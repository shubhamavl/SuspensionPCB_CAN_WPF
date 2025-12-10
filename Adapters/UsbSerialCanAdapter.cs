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
            // Variable-length protocol: [0xAA] [Type] [ID_LOW] [ID_HIGH] [DATA...] [0x55]
            // Minimum frame length: 5 bytes (header + type + ID(2) + footer, DLC=0)
            // Maximum frame length: 13 bytes (header + type + ID(2) + data(8) + footer)
            
            while (_frameBuffer.Count >= 5) // Minimum frame size
            {
                // Look for frame header (0xAA)
                if (!_frameBuffer.TryPeek(out byte first) || first != FRAME_HEADER)
                {
                    _frameBuffer.TryDequeue(out _);
                    continue;
                }

                // Need at least 2 bytes to read Type byte and determine DLC
                if (_frameBuffer.Count < 2) break;

                // Peek at first 2 bytes to determine DLC
                // We'll temporarily dequeue them, check, and re-queue if needed
                if (!_frameBuffer.TryDequeue(out byte header) || !_frameBuffer.TryDequeue(out byte typeByte))
                {
                    // Failed to peek, skip this byte and continue
                    continue;
                }

                // Validate header
                if (header != FRAME_HEADER)
                {
                    // Not a valid header, discard typeByte and continue
                    continue;
                }

                // Extract DLC from Type byte (bits 0-3)
                byte dlc = (byte)(typeByte & 0x0F);
                
                // Calculate frame length: header(1) + type(1) + ID(2) + data(DLC) + footer(1) = 5 + DLC
                int frameLength = 5 + dlc;

                // Check if we have enough bytes for complete frame (we already have header + type, need ID(2) + data(DLC) + footer(1))
                int remainingBytes = 2 + dlc + 1; // ID(2) + data(DLC) + footer(1)
                if (_frameBuffer.Count < remainingBytes)
                {
                    // Not enough bytes, re-queue what we took and wait
                    _frameBuffer.Enqueue(header);
                    _frameBuffer.Enqueue(typeByte);
                    break;
                }

                // We have enough bytes, build the complete frame
                var frame = new byte[frameLength];
                frame[0] = header;
                frame[1] = typeByte;
                
                // Extract remaining bytes: ID(2) + data(DLC) + footer(1)
                for (int i = 2; i < frameLength; i++)
                {
                    if (!_frameBuffer.TryDequeue(out frame[i]))
                    {
                        // Frame extraction failed, this shouldn't happen but handle it
                        return;
                    }
                }

                DecodeFrame(frame);
            }
        }

        private void DecodeFrame(byte[] frame)
        {
            // Variable-length protocol format: [0xAA] [Type] [ID_LOW] [ID_HIGH] [DATA...] [0x55]
            // Minimum frame length: 5 bytes (DLC=0)
            if (frame.Length < 5 || frame[0] != FRAME_HEADER)
                return;

            try
            {
                // Validate footer
                if (frame[frame.Length - 1] != FRAME_FOOTER)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid frame footer: expected 0x55, got 0x{frame[frame.Length - 1]:X2}");
                    return;
                }

                // Extract Type byte (byte 1)
                byte typeByte = frame[1];
                byte dlc = (byte)(typeByte & 0x0F); // Extract DLC from bits 0-3
                
                // Validate frame length matches expected: 5 + DLC
                int expectedLength = 5 + dlc;
                if (frame.Length != expectedLength)
                {
                    System.Diagnostics.Debug.WriteLine($"Frame length mismatch: expected {expectedLength}, got {frame.Length}");
                    return;
                }

                // Extract CAN ID from bytes 2-3 (low byte, high byte)
                uint canId = (uint)(frame[2] | (frame[3] << 8));

                // Extract CAN data from bytes 4 to (4+DLC-1)
                byte[] canData = new byte[dlc];
                if (dlc > 0)
                {
                    Array.Copy(frame, 4, canData, 0, dlc);
                }

                // Process suspension system messages
                if (IsSuspensionMessage(canId))
                {
                    var canMessage = new CANMessage(canId, canData);
                    MessageReceived?.Invoke(canMessage);

                    System.Diagnostics.Debug.WriteLine($"Processed: ID=0x{canId:X3}, DLC={dlc}, Data={BitConverter.ToString(canData)}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Decode error: {ex.Message}");
            }
        }

        private bool IsSuspensionMessage(uint canId)
        {
            // Protocol v0.9 - Ultra-Minimal Suspension System - Semantic IDs
            switch (canId)
            {
                // Raw ADC Data Transmission (STM32 → PC3)
                case 0x200:  // Left side raw ADC data (Ch0+Ch1)
                case 0x201:  // Right side raw ADC data (Ch2+Ch3)

                // Stream Control Commands (PC3 → STM32, but we receive them too for monitoring)
                case 0x040:  // Start left side streaming
                case 0x041:  // Start right side streaming
                case 0x044:  // Stop all streams

                // System Control Messages (PC3 → STM32, but we receive them too for monitoring)
                case 0x030:  // Switch to Internal ADC mode
                case 0x031:  // Switch to ADS1115 mode
                case 0x032:  // Request system status

                // System Status (STM32 → PC3)
                case 0x300:  // System status response

                // Bootloader Protocol (separate firmware)
                case 0x510:  // Bootloader app command
                case 0x511:  // Bootloader boot command
                case 0x512:  // Bootloader boot data
                case 0x513:  // Bootloader boot status

                // Legacy calibration responses (for backward compatibility)
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
            // Variable-length protocol: [0xAA] [Type] [ID_LOW] [ID_HIGH] [DATA...] [0x55]
            // Type byte: bit5=0 (standard frame), bit4=0 (data frame), bits 0-3=DLC (0-8)
            byte dlc = (byte)Math.Min(data?.Length ?? 0, 8);
            
            var frame = new List<byte>
            {
                FRAME_HEADER,                                    // Byte 0: Header (0xAA)
                (byte)(0xC0 | dlc),                              // Byte 1: Type (0xC0 = standard, data, DLC)
                (byte)(id & 0xFF),                               // Byte 2: ID low
                (byte)((id >> 8) & 0xFF)                         // Byte 3: ID high
            };

            // Add data bytes (only actual data, no padding)
            if (data != null && dlc > 0)
            {
                frame.AddRange(data.Take(dlc));
            }

            // Add footer
            frame.Add(FRAME_FOOTER);                             // Last byte: Footer (0x55)
            
            return frame.ToArray();
        }

        public void Dispose()
        {
            Disconnect();
            _serialPort?.Dispose();
        }
    }
}


