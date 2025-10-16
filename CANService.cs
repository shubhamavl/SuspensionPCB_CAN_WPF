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

        private bool _suspensionRequestActive = false;
        private bool _axleRequestActive = false;

        // v0.7 Protocol constants
        private const uint CAN_MSG_ID_STREAM_CONTROL = 0x040;  // Stream control command
        private const uint CAN_MSG_ID_RAW_DATA = 0x200;        // Raw ADC data response
        private const uint CAN_MSG_ID_STATUS = 0x201;          // System status response

        // Manage Calibration Points Operation Constants
        public const byte MANAGE_CAL_OP_DELETE_LAST = 0x01;      // Delete last point
        public const byte MANAGE_CAL_OP_RESET_SESSION = 0x02;    // Reset session (clear all points)
        public const byte MANAGE_CAL_OP_GET_POINT_COUNT = 0x03;  // Get point count
        public const byte MANAGE_CAL_OP_DELETE_SPECIFIC = 0x04;  // Delete specific point by index

        public event Action<CANMessage>? MessageReceived;
        public event EventHandler<string> DataTimeout;
        
        // v0.7 Events
        public event EventHandler<RawDataEventArgs>? RawDataReceived;

        // ADDED: Static events for WeightCalibrationPoint
        public static event EventHandler<WeightDataEventArgs> WeightDataReceived;
        public static event EventHandler<ADCDataEventArgs> ADCDataReceived;
        public static event EventHandler<CANErrorEventArgs> CommunicationError;
        public static event EventHandler<CalibrationDataEventArgs> CalibrationDataReceived;
        public static event EventHandler<CalibrationQualityEventArgs> CalibrationQualityReceived;

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

                System.Diagnostics.Debug.WriteLine($"USB-CAN-A Connected on {portName}");
                return true;
            }
            catch (Exception ex)
            {
                message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                System.Diagnostics.Debug.WriteLine($"USB-CAN-A connection error: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"USB-CAN-A connection error: {ex.Message}");
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

            System.Diagnostics.Debug.WriteLine("USB-CAN-A Disconnected");
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

                    System.Diagnostics.Debug.WriteLine($"Processed: ID=0x{canId:X3}, Data={BitConverter.ToString(canData)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Decode error: {ex.Message}");
            }
        }

        // Check if message ID belongs to suspension system protocol (v0.7)
        private bool IsSuspensionMessage(uint canId)
        {
            switch (canId)
            {
                // v0.7 Protocol messages
                case CAN_MSG_ID_RAW_DATA:    // 0x200 - Raw ADC data
                case CAN_MSG_ID_STATUS:      // 0x201 - System status
                case CAN_MSG_ID_STREAM_CONTROL: // 0x040 - Stream control (TX only)
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
                    System.Diagnostics.Debug.WriteLine($"Invalid CAN ID: 0x{id:X3} (max 0x{MAX_CAN_ID:X3} for standard frame)");
                    return false;
                }

                // Validate data length
                if (data != null && data.Length > 8)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid data length: {data.Length} (max 8 bytes)");
                    return false;
                }

                var frame = CreateFrame(id, data);

                lock (_sendLock)
                {
                    _serialPort.Write(frame, 0, frame.Length);
                }

                System.Diagnostics.Debug.WriteLine($"USB-CAN-A: Sent CAN frame ID=0x{id:X3}, Data={BitConverter.ToString(data ?? new byte[0])}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send message error: {ex.Message}");
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

        // Helper method to send suspension weight data request
        public bool RequestSuspensionData(bool start, byte transmissionRate = 0x02)
        {
            byte[] data = new byte[8];
            data[0] = start ? (byte)0x01 : (byte)0x00;  // Start/Stop
            data[1] = transmissionRate;  // 0x01=100Hz, 0x02=500Hz, 0x03=1024Hz, 0x04=2048Hz

            if (start)
            {
                _suspensionRequestActive = true;
                _axleRequestActive = false;
            }
            else
            {
                _suspensionRequestActive = false;
            }

            return SendMessage(0x030, data);
        }

        // Helper method to send axle weight data request
        public bool RequestAxleData(bool start, byte transmissionRate = 0x02)
        {
            byte[] data = new byte[8];
            data[0] = start ? (byte)0x01 : (byte)0x00;  // Start/Stop
            data[1] = transmissionRate;  // 0x01=100Hz, 0x02=500Hz, 0x03=1024Hz, 0x04=2048Hz

            if (start)
            {
                _axleRequestActive = true;
                _suspensionRequestActive = false;
            }
            else
            {
                _axleRequestActive = false;
            }

            return SendMessage(0x031, data);
        }

        // Stop Suspension Data Transmission
        public bool StopSuspensionData()
        {
            byte[] data = new byte[8]; // All zeros: [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]
            return SendMessage(0x030, data);
        }

        // Stop Axle Data Transmission  
        public bool StopAxleData()
        {
            byte[] data = new byte[8]; // All zeros: [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]
            return SendMessage(0x031, data);
        }

        // v0.7 Stream Control Methods
        public bool StartStream(byte side, byte rate)
        {
            byte[] data = new byte[8];
            data[0] = 0x01;  // Start command
            data[1] = side;  // 0=Left, 1=Right
            data[2] = rate;  // 0x01=100Hz, 0x02=500Hz, 0x03=1kHz, 0x04=1Hz
            data[3] = 0x00;  // Reserved
            data[4] = 0x00;  // Reserved
            data[5] = 0x00;  // Reserved
            data[6] = 0x00;  // Reserved
            data[7] = 0x00;  // Reserved
            
            return SendMessage(CAN_MSG_ID_STREAM_CONTROL, data);
        }
        
        public bool StopStream(byte side)
        {
            byte[] data = new byte[8];
            data[0] = 0x00;  // Stop command
            data[1] = side;  // 0=Left, 1=Right
            data[2] = 0x00;  // Reserved
            data[3] = 0x00;  // Reserved
            data[4] = 0x00;  // Reserved
            data[5] = 0x00;  // Reserved
            data[6] = 0x00;  // Reserved
            data[7] = 0x00;  // Reserved
            
            return SendMessage(CAN_MSG_ID_STREAM_CONTROL, data);
        }

        // Helper method to start weight-based variable calibration only (v0.6)
        public bool StartVariableCalibration(byte pointCount, byte polyOrder,
                                           bool autoSpacing, ushort maxWeight, byte channelMask = 0x0F)
        {
            if (pointCount < 2 || pointCount > 20)
            {
                System.Diagnostics.Debug.WriteLine("Invalid point count: must be 2-20");
                return false;
            }

            byte[] data = new byte[8];
            data[0] = 0x01;  // Start Calibration
            data[1] = 0x00;  // v0.6: Reserved (was pointCount)
            data[2] = 0x00;  // v0.6: Reserved (was calibration_mode)
            data[3] = 0x00;  // v0.6: Reserved (was polynomial_order)
            data[4] = 0x00;  // v0.6: Reserved (was auto_spacing)
            data[5] = (byte)(maxWeight & 0xFF);        // Max weight low
            data[6] = (byte)((maxWeight >> 8) & 0xFF); // Max weight high
            data[7] = channelMask;

            // Use static sender to also echo TX into UI via MessageReceived
            return SendStaticMessage(0x020, data);
        }

        // Get active calibration coefficients for verification
        public bool GetActiveCalibrationCoefficients(byte channelMask = 0x0F)
        {
            byte[] data = new byte[8];
            data[0] = 0x09;  // Get Active Coefficients command
            data[1] = channelMask;  // Channel mask (0x0F = all channels)
            data[2] = 0x00;  // Reserved
            data[3] = 0x00;  // Reserved
            data[4] = 0x00;  // Reserved
            data[5] = 0x00;  // Reserved
            data[6] = 0x00;  // Reserved
            data[7] = 0x00;  // Reserved

            return SendStaticMessage(0x028, data);
        }

        // Get calibration points for verification (removed duplicate - using static method below)


        // Static methods for easy access from all windows
        public static bool SendStaticMessage(uint canId, byte[] data)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CAN TX: ID=0x{canId:X3}, Data=[{string.Join(",", Array.ConvertAll(data, b => $"0x{b:X2}"))}]");

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
                        System.Diagnostics.Debug.WriteLine("CAN Send Error: Not connected to hardware");
                        return false;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("CAN Send Error: CANService instance not available");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CAN Send Error: {ex.Message}");
                return false;
            }
        }

        public static bool SendWeightCalibrationPoint(byte pointIndex, double weight, byte channelMask)
        {
            byte[] data = new byte[8];
            data[0] = 0x03; // Command Type: Set Weight Point ✓
            data[1] = 0x00; // v0.6: Reserved (point index auto-assigned by firmware)

            ushort weightValue = (ushort)(weight * 10); // kg × 10 ✓
            data[2] = (byte)(weightValue & 0xFF);        // Low byte ✓
            data[3] = (byte)((weightValue >> 8) & 0xFF); // High byte ✓

            data[4] = channelMask; // Channel Mask ✓
            data[5] = 0x00; // Reserved ✓
            data[6] = 0x00; // Reserved ✓
            data[7] = 0x00; // Reserved ✓

            return SendStaticMessage(0x022, data);
        }

        // v0.6: Get Active Coefficients (0x028)
        public static bool GetActiveCoefficients(byte channelMask)
        {
            byte[] data = new byte[8];
            data[0] = 0x08; // Command Type: Get Active Coefficients
            data[1] = channelMask; // Channel mask
            data[2] = 0x00; // Reserved
            data[3] = 0x00; // Reserved
            data[4] = 0x00; // Reserved
            data[5] = 0x00; // Reserved
            data[6] = 0x00; // Reserved
            data[7] = 0x00; // Reserved

            return SendStaticMessage(0x028, data);
        }

        // v0.6: Get Calibration Points (0x029)
        public static bool GetCalibrationPoints(byte channelMask)
        {
            byte[] data = new byte[8];
            data[0] = 0x09; // Command Type: Get Calibration Points
            data[1] = channelMask; // Channel mask
            data[2] = 0x00; // Reserved
            data[3] = 0x00; // Reserved
            data[4] = 0x00; // Reserved
            data[5] = 0x00; // Reserved
            data[6] = 0x00; // Reserved
            data[7] = 0x00; // Reserved

            return SendStaticMessage(0x029, data);
        }

        // v0.6: Manage Calibration Points (0x02A)
        public static bool ManageCalibrationPoints(byte channelMask, byte operation)
        {
            byte[] data = new byte[8];
            data[0] = 0x0B; // Command Type: Manage Calibration Points (v0.6)
            data[1] = channelMask; // Channel mask
            data[2] = operation; // Operation: 0x01=Delete Last, 0x02=Reset Session, 0x03=Get Count
            data[3] = 0x00; // Reserved
            data[4] = 0x00; // Reserved
            data[5] = 0x00; // Reserved
            data[6] = 0x00; // Reserved
            data[7] = 0x00; // Reserved

            return SendStaticMessage(0x02A, data);
        }

        /// <summary>
        /// Manage calibration points with specific point index (for Delete Specific Point operation)
        /// </summary>
        /// <param name="channelMask">Channel mask</param>
        /// <param name="operation">Operation type</param>
        /// <param name="pointIndex">1-based point index (for Delete Specific Point operation)</param>
        /// <returns>True if message sent successfully</returns>
        public static bool ManageCalibrationPoints(byte channelMask, byte operation, byte pointIndex)
        {
            byte[] data = new byte[8];
            data[0] = 0x0B; // Command Type: Manage Calibration Points (v0.6)
            data[1] = channelMask; // Channel mask
            data[2] = operation; // Operation: 0x01=Delete Last, 0x02=Reset Session, 0x03=Get Count, 0x04=Delete Specific
            data[3] = pointIndex; // Point index for Delete Specific Point operation
            data[4] = 0x00; // Reserved
            data[5] = 0x00; // Reserved
            data[6] = 0x00; // Reserved
            data[7] = 0x00; // Reserved

            return SendStaticMessage(0x02A, data);
        }

        #region Event Firing Logic for v0.7 Protocol
        private void FireSpecificEvents(uint canId, byte[] canData)
        {
            switch (canId)
            {
                case CAN_MSG_ID_RAW_DATA: // 0x200 - Raw ADC data
                    if (canData.Length >= 8)
                    {
                        var rawDataArgs = new RawDataEventArgs
                        {
                            Side = canData[0],  // 0=Left, 1=Right
                            RawADCSum = BitConverter.ToUInt16(canData, 1), // Raw ADC sum
                            ADCMode = canData[3], // 0=Internal, 1=ADS1115
                            Timestamp = BitConverter.ToUInt16(canData, 4), // Milliseconds
                            TimestampFull = DateTime.Now
                        };
                        RawDataReceived?.Invoke(this, rawDataArgs);
                    }
                    break;

                case CAN_MSG_ID_STATUS: // 0x201 - System status
                    if (canData.Length >= 8)
                    {
                        var statusArgs = new SystemStatusEventArgs
                        {
                            SystemStatus = canData[0], // 0=OK, 1=Warning, 2=Error
                            ErrorFlags = canData[1],    // Error flags
                            ADCMode = canData[2],       // Current ADC mode
                            UptimeSeconds = BitConverter.ToUInt32(canData, 4), // System uptime
                            Timestamp = DateTime.Now
                        };
                        // TODO: Add SystemStatusReceived event if needed
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

    #region Event Args Classes for WeightCalibrationPoint
    public class WeightDataEventArgs : EventArgs
    {
        public byte ChannelMask { get; set; }
        // 4 Load Cells - Standard Configuration
        public double FrontLeftWeight { get; set; }   // STM32 ADC Channel 0
        public double FrontRightWeight { get; set; }  // STM32 ADC Channel 1
        public double RearLeftWeight { get; set; }    // STM32 ADC Channel 2
        public double RearRightWeight { get; set; }   // STM32 ADC Channel 3
        public double TotalVehicleWeight { get; set; } // Sum of all 4
        public DateTime Timestamp { get; set; }
    }

    public class ADCDataEventArgs : EventArgs
    {
        // ADC values for all 4 channels
        public int FrontLeftADC { get; set; }     // STM32 ADC Channel 0 (12-bit: 0-4095)
        public int FrontRightADC { get; set; }    // STM32 ADC Channel 1 (12-bit: 0-4095)
        public int RearLeftADC { get; set; }      // STM32 ADC Channel 2 (12-bit: 0-4095)
        public int RearRightADC { get; set; }     // STM32 ADC Channel 3 (12-bit: 0-4095)
        public double FrontLeftVoltage { get; set; }
        public double FrontRightVoltage { get; set; }
        public double RearLeftVoltage { get; set; }
        public double RearRightVoltage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CANErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public int ErrorCode { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CalibrationDataEventArgs : EventArgs
    {
        public byte PointIndex { get; set; }        // Protocol: Byte 0 (Point Index 0-19)
        public byte PointStatus { get; set; }       // Protocol: Byte 1 (Status Code 0x00-0xFF)
        public double ReferenceWeight { get; set; } // Protocol: Bytes 2-3 converted to kg
        public int ADCValue { get; set; }           // Protocol: Bytes 4-5 (Raw ADC value)
        public DateTime Timestamp { get; set; }
    }

    public class CalibrationQualityEventArgs : EventArgs
    {
        public double AccuracyPercentage { get; set; }
        public double MaxErrorKg { get; set; }
        public byte QualityGrade { get; set; }
        public byte Recommendation { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Calibration verification data structure
    public class CalibrationVerificationData
    {
        public double CalibratedWeight { get; set; }  // Weight from calibration
        public double RawADCWeight { get; set; }      // Weight from raw ADC conversion
        public int RawADCValue { get; set; }         // Raw ADC reading
        public double ErrorPercentage { get; set; }    // Calibration error percentage
        public bool IsAccurate { get; set; }         // Within acceptable error range
        public DateTime Timestamp { get; set; }
    }

    // Calibration coefficients data structure
    public class CalibrationCoefficientsData
    {
        public byte Channel { get; set; }            // Channel number (0-7)
        public byte Order { get; set; }              // Polynomial order (1-4)
        public byte Segment { get; set; }            // Segment index (0-2)
        public double CoefficientA { get; set; }    // Coefficient A (scaled by 1000)
        public double CoefficientB { get; set; }    // Coefficient B (scaled by 1000)
        public DateTime Timestamp { get; set; }
    }

    // v0.7 Protocol Event Args
    public class RawDataEventArgs : EventArgs
    {
        public byte Side { get; set; }              // 0=Left, 1=Right
        public ushort RawADCSum { get; set; }       // Raw ADC sum (0-8192 or 0-65535)
        public byte ADCMode { get; set; }           // 0=Internal, 1=ADS1115
        public ushort Timestamp { get; set; }       // Milliseconds
        public DateTime TimestampFull { get; set; } // Full timestamp
    }

    public class SystemStatusEventArgs : EventArgs
    {
        public byte SystemStatus { get; set; }      // 0=OK, 1=Warning, 2=Error
        public byte ErrorFlags { get; set; }        // Error flags
        public byte ADCMode { get; set; }           // Current ADC mode
        public uint UptimeSeconds { get; set; }     // System uptime in seconds
        public DateTime Timestamp { get; set; }
    }

    #endregion


}  // Namespace closing brace