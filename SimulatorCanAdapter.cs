using System;
using System.Threading;
using System.Threading.Tasks;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Simulator CAN adapter for testing without hardware
    /// Automatically generates realistic weight data
    /// </summary>
    public class SimulatorCanAdapter : ICanAdapter
    {
        public string AdapterType => "Simulator";

        private volatile bool _connected;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _sendLock = new object();
        
        // Simulation state
        private bool _leftStreamActive = false;
        private bool _rightStreamActive = false;
        private byte _leftStreamRate = 0x03; // 1kHz default
        private byte _rightStreamRate = 0x03; // 1kHz default
        private byte _currentADCMode = 0; // 0=Internal, 1=ADS1115
        
        // Simulated weight values (in kg)
        private double _leftWeightKg = 0.0;
        private double _rightWeightKg = 0.0;
        
        // Simulated ADC base values (zero weight)
        private ushort _leftZeroADC = 2048; // Mid-range for 12-bit
        private ushort _rightZeroADC = 2048;
        
        // ADC sensitivity (ADC counts per kg)
        private double _leftSensitivity = 100.0; // 100 ADC counts per kg
        private double _rightSensitivity = 100.0;
        
        // Noise simulation
        private readonly Random _random = new Random();
        private double _noiseLevel = 5.0; // ±5 ADC counts noise
        private readonly object _parameterLock = new object(); // Lock for parameter access

        // Protocol constants
        private const uint CAN_MSG_ID_LEFT_RAW_DATA = 0x200;
        private const uint CAN_MSG_ID_RIGHT_RAW_DATA = 0x201;
        private const uint CAN_MSG_ID_START_LEFT_STREAM = 0x040;
        private const uint CAN_MSG_ID_START_RIGHT_STREAM = 0x041;
        private const uint CAN_MSG_ID_STOP_ALL_STREAMS = 0x044;
        private const uint CAN_MSG_ID_MODE_INTERNAL = 0x030;
        private const uint CAN_MSG_ID_MODE_ADS1115 = 0x031;
        private const uint CAN_MSG_ID_STATUS_REQUEST = 0x032;
        private const uint CAN_MSG_ID_SYSTEM_STATUS = 0x300;

        public bool IsConnected => _connected;

        public event Action<CANMessage>? MessageReceived;
#pragma warning disable CS0067 // Event is never used - simulator doesn't have timeout scenarios
        public event EventHandler<string>? DataTimeout;
#pragma warning restore CS0067
        public event EventHandler<bool>? ConnectionStatusChanged;

        public bool Connect(CanAdapterConfig config, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (config is not SimulatorCanAdapterConfig)
            {
                errorMessage = "Invalid configuration type for Simulator adapter";
                return false;
            }

            try
            {
                _connected = true;
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Start simulation tasks
                Task.Run(() => SimulateDataAsync(_cancellationTokenSource.Token));
                
                ConnectionStatusChanged?.Invoke(this, true);
                ProductionLogger.Instance.LogInfo("Simulator adapter connected", "Simulator");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _connected = false;
                ConnectionStatusChanged?.Invoke(this, false);
                return false;
            }
        }

        public void Disconnect()
        {
            _connected = false;
            _leftStreamActive = false;
            _rightStreamActive = false;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ConnectionStatusChanged?.Invoke(this, false);
            ProductionLogger.Instance.LogInfo("Simulator adapter disconnected", "Simulator");
        }

        public bool SendMessage(uint id, byte[] data)
        {
            if (!_connected) return false;

            try
            {
                lock (_sendLock)
                {
                    // Handle protocol commands
                    switch (id)
                    {
                        case CAN_MSG_ID_START_LEFT_STREAM:
                            if (data != null && data.Length > 0)
                            {
                                _leftStreamRate = data[0];
                                _leftStreamActive = true;
                                ProductionLogger.Instance.LogInfo($"Simulator: Left stream started at rate {_leftStreamRate}", "Simulator");
                            }
                            break;

                        case CAN_MSG_ID_START_RIGHT_STREAM:
                            if (data != null && data.Length > 0)
                            {
                                _rightStreamRate = data[0];
                                _rightStreamActive = true;
                                ProductionLogger.Instance.LogInfo($"Simulator: Right stream started at rate {_rightStreamRate}", "Simulator");
                            }
                            break;

                        case CAN_MSG_ID_STOP_ALL_STREAMS:
                            _leftStreamActive = false;
                            _rightStreamActive = false;
                            ProductionLogger.Instance.LogInfo("Simulator: All streams stopped", "Simulator");
                            break;

                        case CAN_MSG_ID_MODE_INTERNAL:
                            _currentADCMode = 0;
                            ProductionLogger.Instance.LogInfo("Simulator: Switched to Internal ADC mode", "Simulator");
                            break;

                        case CAN_MSG_ID_MODE_ADS1115:
                            _currentADCMode = 1;
                            ProductionLogger.Instance.LogInfo("Simulator: Switched to ADS1115 mode", "Simulator");
                            break;

                        case CAN_MSG_ID_STATUS_REQUEST:
                            // Respond with system status
                            Task.Run(() => SendSystemStatus());
                            break;
                    }

                    // Fire TX message event
                    var txMessage = new CANMessage(id, data ?? new byte[0], DateTime.Now);
                    MessageReceived?.Invoke(txMessage);
                }

                return true;
            }
            catch (Exception ex)
            {
                ProductionLogger.Instance.LogError($"Simulator send error: {ex.Message}", "Simulator");
                return false;
            }
        }

        public string[] GetAvailableOptions()
        {
            return new[] { "Simulator" };
        }

        private async Task SimulateDataAsync(CancellationToken token)
        {
            while (_connected && !token.IsCancellationRequested)
            {
                try
                {
                    // Send left side data if stream is active
                    if (_leftStreamActive)
                    {
                        ushort leftADC = CalculateSimulatedADC(_leftWeightKg, _leftZeroADC, _leftSensitivity);
                        byte[] leftData = new byte[] { (byte)(leftADC & 0xFF), (byte)((leftADC >> 8) & 0xFF) };
                        var leftMessage = new CANMessage(CAN_MSG_ID_LEFT_RAW_DATA, leftData, DateTime.Now);
                        MessageReceived?.Invoke(leftMessage);

                        await Task.Delay(GetRateDelay(_leftStreamRate), token);
                    }

                    // Send right side data if stream is active
                    if (_rightStreamActive)
                    {
                        ushort rightADC = CalculateSimulatedADC(_rightWeightKg, _rightZeroADC, _rightSensitivity);
                        byte[] rightData = new byte[] { (byte)(rightADC & 0xFF), (byte)((rightADC >> 8) & 0xFF) };
                        var rightMessage = new CANMessage(CAN_MSG_ID_RIGHT_RAW_DATA, rightData, DateTime.Now);
                        MessageReceived?.Invoke(rightMessage);

                        await Task.Delay(GetRateDelay(_rightStreamRate), token);
                    }

                    // If no streams active, sleep a bit
                    if (!_leftStreamActive && !_rightStreamActive)
                    {
                        await Task.Delay(100, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ProductionLogger.Instance.LogError($"Simulator data generation error: {ex.Message}", "Simulator");
                    await Task.Delay(100, token);
                }
            }
        }

        private ushort CalculateSimulatedADC(double weightKg, ushort zeroADC, double sensitivity)
        {
            // Calculate ADC value: zeroADC + (weight * sensitivity) + noise
            double adcValue = zeroADC + (weightKg * sensitivity);
            
            // Add realistic noise
            double noiseLevel;
            lock (_parameterLock)
            {
                noiseLevel = _noiseLevel;
            }
            double noise = (_random.NextDouble() - 0.5) * 2.0 * noiseLevel;
            adcValue += noise;
            
            // Clamp to valid ADC range (0-4095 for 12-bit, 0-65535 for 16-bit)
            ushort maxADC = (ushort)(_currentADCMode == 0 ? 4095 : 65535);
            ushort minADC = 0;
            
            if (adcValue < minADC) adcValue = minADC;
            if (adcValue > maxADC) adcValue = maxADC;
            
            return (ushort)adcValue;
        }

        private int GetRateDelay(byte rate)
        {
            return rate switch
            {
                0x01 => 10,   // 100Hz = 10ms
                0x02 => 2,    // 500Hz = 2ms
                0x03 => 1,    // 1kHz = 1ms
                0x05 => 1000, // 1Hz = 1000ms
                _ => 1        // Default 1kHz
            };
        }

        private async Task SendSystemStatus()
        {
            await Task.Delay(50); // Small delay to simulate response time
            
            if (!_connected) return;
            
            byte[] statusData = new byte[]
            {
                0x00,        // System status: 0=OK
                0x00,        // Error flags: 0=no errors
                _currentADCMode  // ADC mode
            };
            
            var statusMessage = new CANMessage(CAN_MSG_ID_SYSTEM_STATUS, statusData, DateTime.Now);
            MessageReceived?.Invoke(statusMessage);
        }

        // Public properties for parameter access (thread-safe)
        public double LeftWeight
        {
            get { lock (_parameterLock) { return _leftWeightKg; } }
            set { lock (_parameterLock) { _leftWeightKg = value; } }
        }

        public double RightWeight
        {
            get { lock (_parameterLock) { return _rightWeightKg; } }
            set { lock (_parameterLock) { _rightWeightKg = value; } }
        }

        public ushort LeftZeroADC
        {
            get { lock (_parameterLock) { return _leftZeroADC; } }
            set { lock (_parameterLock) { _leftZeroADC = value; } }
        }

        public ushort RightZeroADC
        {
            get { lock (_parameterLock) { return _rightZeroADC; } }
            set { lock (_parameterLock) { _rightZeroADC = value; } }
        }

        public double LeftSensitivity
        {
            get { lock (_parameterLock) { return _leftSensitivity; } }
            set { lock (_parameterLock) { _leftSensitivity = value; } }
        }

        public double RightSensitivity
        {
            get { lock (_parameterLock) { return _rightSensitivity; } }
            set { lock (_parameterLock) { _rightSensitivity = value; } }
        }

        public double NoiseLevel
        {
            get { lock (_parameterLock) { return _noiseLevel; } }
            set { lock (_parameterLock) { _noiseLevel = value; } }
        }

        public byte CurrentADCMode => _currentADCMode;

        /// <summary>
        /// Calculate and return current left ADC value (without noise for display)
        /// </summary>
        public ushort GetCurrentLeftADC()
        {
            lock (_parameterLock)
            {
                double adcValue = _leftZeroADC + (_leftWeightKg * _leftSensitivity);
                ushort maxADC = (ushort)(_currentADCMode == 0 ? 4095 : 65535);
                if (adcValue < 0) adcValue = 0;
                if (adcValue > maxADC) adcValue = maxADC;
                return (ushort)adcValue;
            }
        }

        /// <summary>
        /// Calculate and return current right ADC value (without noise for display)
        /// </summary>
        public ushort GetCurrentRightADC()
        {
            lock (_parameterLock)
            {
                double adcValue = _rightZeroADC + (_rightWeightKg * _rightSensitivity);
                ushort maxADC = (ushort)(_currentADCMode == 0 ? 4095 : 65535);
                if (adcValue < 0) adcValue = 0;
                if (adcValue > maxADC) adcValue = maxADC;
                return (ushort)adcValue;
            }
        }

        // Legacy methods for backward compatibility
        public void SetLeftWeight(double weightKg) => LeftWeight = weightKg;
        public void SetRightWeight(double weightKg) => RightWeight = weightKg;
        public void SetLeftZeroADC(ushort zeroADC) => LeftZeroADC = zeroADC;
        public void SetRightZeroADC(ushort zeroADC) => RightZeroADC = zeroADC;
    }
}

