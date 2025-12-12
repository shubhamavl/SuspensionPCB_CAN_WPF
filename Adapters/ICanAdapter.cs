using System;
using SuspensionPCB_CAN_WPF.Models;

namespace SuspensionPCB_CAN_WPF.Adapters
{
    /// <summary>
    /// Interface for CAN adapter implementations
    /// </summary>
    public interface ICanAdapter
    {
        /// <summary>
        /// Gets the adapter type name (e.g., "USB-CAN-A Serial", "PCAN")
        /// </summary>
        string AdapterType { get; }

        /// <summary>
        /// Gets whether the adapter is currently connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Event fired when a CAN message is received
        /// </summary>
        event Action<CANMessage>? MessageReceived;

        /// <summary>
        /// Event fired when data timeout occurs
        /// </summary>
        event EventHandler<string>? DataTimeout;

        /// <summary>
        /// Event fired when connection status changes
        /// </summary>
        event EventHandler<bool>? ConnectionStatusChanged;

        /// <summary>
        /// Connect to the CAN adapter using the provided configuration
        /// </summary>
        /// <param name="config">Adapter-specific configuration</param>
        /// <param name="errorMessage">Output parameter for error message if connection fails</param>
        /// <returns>True if connection successful, false otherwise</returns>
        bool Connect(CanAdapterConfig config, out string errorMessage);

        /// <summary>
        /// Disconnect from the CAN adapter
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Send a CAN message
        /// </summary>
        /// <param name="id">CAN message ID (11-bit standard frame)</param>
        /// <param name="data">Message data (max 8 bytes)</param>
        /// <returns>True if message sent successfully, false otherwise</returns>
        bool SendMessage(uint id, byte[] data);

        /// <summary>
        /// Get available adapter-specific options (e.g., COM ports, PCAN channels)
        /// </summary>
        /// <returns>Array of available options</returns>
        string[] GetAvailableOptions();
    }

    /// <summary>
    /// Base configuration class for CAN adapters
    /// </summary>
    public abstract class CanAdapterConfig
    {
        /// <summary>
        /// CAN bitrate in kbps (e.g., 250, 500, 1000)
        /// </summary>
        public ushort BitrateKbps { get; set; } = 500;
    }

    /// <summary>
    /// Configuration for USB-CAN-A Serial adapter
    /// </summary>
    public class UsbSerialCanAdapterConfig : CanAdapterConfig
    {
        /// <summary>
        /// COM port name (e.g., "COM3")
        /// </summary>
        public string PortName { get; set; } = string.Empty;

        /// <summary>
        /// Serial port baud rate (default 2000000 for USB-CAN-A)
        /// </summary>
        public int SerialBaudRate { get; set; } = 2000000;
    }

    /// <summary>
    /// Configuration for PCAN adapter
    /// </summary>
    public class PcanCanAdapterConfig : CanAdapterConfig
    {
        /// <summary>
        /// PCAN channel (e.g., PCAN_USBBUS1, PCAN_USBBUS2)
        /// </summary>
        public ushort Channel { get; set; } = 0x51; // PCAN_USBBUS1

        /// <summary>
        /// PCAN bitrate code (e.g., PCAN_BAUD_500K)
        /// </summary>
        public ushort PcanBitrate { get; set; } = 0x001C; // PCAN_BAUD_500K
    }

    /// <summary>
    /// Configuration for Named Pipe CAN adapter
    /// </summary>
    public class NamedPipeCanAdapterConfig : CanAdapterConfig
    {
        /// <summary>
        /// Named pipe name (e.g., "CANBridge_UI" or "CANBridge_Emulator")
        /// </summary>
        public string PipeName { get; set; } = "CANBridge_UI";
    }
}


