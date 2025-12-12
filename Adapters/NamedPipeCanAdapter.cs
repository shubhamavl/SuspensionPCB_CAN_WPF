using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using SuspensionPCB_CAN_WPF.Models;

namespace SuspensionPCB_CAN_WPF.Adapters
{
    /// <summary>
    /// Named Pipe CAN Adapter - Connects to CAN Bridge Service via named pipe
    /// </summary>
    public class NamedPipeCanAdapter : ICanAdapter
    {
        public string AdapterType => "Named Pipe Bridge";

        private NamedPipeClientStream? _pipeClient;
        private volatile bool _connected;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _sendLock = new object();

        public bool IsConnected => _connected;

        public event Action<CANMessage>? MessageReceived;
#pragma warning disable CS0067 // Event is never used - Named pipes don't need timeout detection like physical adapters
        public event EventHandler<string>? DataTimeout;
#pragma warning restore CS0067
        public event EventHandler<bool>? ConnectionStatusChanged;

        /// <summary>
        /// Connect to named pipe server
        /// </summary>
        public bool Connect(CanAdapterConfig config, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (config is not NamedPipeCanAdapterConfig pipeConfig)
            {
                errorMessage = "Invalid configuration type for Named Pipe adapter";
                return false;
            }

            try
            {
                _pipeClient = new NamedPipeClientStream(
                    ".",
                    pipeConfig.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                _pipeClient.Connect(5000); // 5 second timeout
                _connected = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // Start reading messages
                Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token));

                ConnectionStatusChanged?.Invoke(this, true);
                return true;
            }
            catch (TimeoutException)
            {
                errorMessage = $"Timeout connecting to named pipe: {pipeConfig.PipeName}";
                _connected = false;
                ConnectionStatusChanged?.Invoke(this, false);
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _connected = false;
                ConnectionStatusChanged?.Invoke(this, false);
                return false;
            }
        }

        /// <summary>
        /// Disconnect from named pipe
        /// </summary>
        public void Disconnect()
        {
            _connected = false;
            _cancellationTokenSource?.Cancel();

            _pipeClient?.Close();
            _pipeClient?.Dispose();
            _pipeClient = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ConnectionStatusChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Send CAN message via named pipe
        /// </summary>
        public bool SendMessage(uint id, byte[] data)
        {
            if (!_connected || _pipeClient == null || !_pipeClient.IsConnected)
                return false;

            try
            {
                lock (_sendLock)
                {
                    // Format: [CAN_ID:4 bytes] [Data_Length:1 byte] [Data:0-8 bytes]
                    byte[] idBytes = BitConverter.GetBytes(id);
                    byte dataLength = (byte)(data?.Length ?? 0);

                    if (dataLength > 8)
                    {
                        System.Diagnostics.Debug.WriteLine($"Invalid data length: {dataLength} (max 8 bytes)");
                        return false;
                    }

                    _pipeClient.Write(idBytes, 0, 4);
                    _pipeClient.WriteByte(dataLength);
                    if (dataLength > 0 && data != null)
                    {
                        _pipeClient.Write(data, 0, dataLength);
                    }
                    _pipeClient.Flush();

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send message error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read messages from named pipe
        /// </summary>
        private async Task ReadMessagesAsync(CancellationToken token)
        {
            var buffer = new byte[13]; // Max: 4 bytes ID + 1 byte length + 8 bytes data

            while (_connected && _pipeClient?.IsConnected == true && !token.IsCancellationRequested)
            {
                try
                {
                    // Read CAN ID (4 bytes) and data length (1 byte)
                    int bytesRead = await _pipeClient.ReadAsync(buffer, 0, 5, token);
                    if (bytesRead < 5) break;

                    uint canId = BitConverter.ToUInt32(buffer, 0);
                    byte dataLength = buffer[4];

                    if (dataLength > 8) break; // Invalid length

                    // Read data bytes if present
                    byte[] data = new byte[dataLength];
                    if (dataLength > 0)
                    {
                        int dataRead = await _pipeClient.ReadAsync(buffer, 5, dataLength, token);
                        if (dataRead != dataLength) break;
                        Array.Copy(buffer, 5, data, 0, dataLength);
                    }

                    var message = new CANMessage(canId, data, DateTime.Now);
                    MessageReceived?.Invoke(message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Read message error: {ex.Message}");
                    break;
                }
            }

            _connected = false;
            ConnectionStatusChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Get available options (pipe names)
        /// </summary>
        public string[] GetAvailableOptions()
        {
            return new[] { "CANBridge_UI", "CANBridge_Emulator" };
        }
    }
}

