using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SuspensionPCB_CAN_WPF.Models
{
    // CAN Message data structure
    public class CANMessage
    {
        public uint ID { get; set; }
        public byte[] Data { get; set; }
        public DateTime Timestamp { get; set; }
        public string Direction { get; set; } = "RX";
        public int Length => Data?.Length ?? 0;

        public CANMessage()
        {
            ID = 0;
            Data = new byte[0];
            Timestamp = DateTime.Now;
            Direction = "RX";
        }

        public CANMessage(uint id, byte[] data, string direction = "RX")
        {
            ID = id;
            Data = data ?? new byte[0];
            Timestamp = DateTime.Now;
            Direction = direction;
        }

        public CANMessage(uint id, byte[] data, DateTime timestamp, string direction = "RX")
        {
            ID = id;
            Data = data ?? new byte[0];
            Timestamp = timestamp;
            Direction = direction;
        }

        // Helper method to get hex string representation of data
        public string GetDataHexString()
        {
            if (Data == null || Data.Length == 0)
                return "No Data";

            return BitConverter.ToString(Data).Replace("-", " ");
        }

        // Helper method to get ID as hex string
        public string GetIDHexString()
        {
            return $"0x{ID:X3}";
        }

        // Get protocol description for semantic IDs
        public string GetProtocolDescription()
        {
            return ID switch
            {
                0x200 => "LEFT_RAW_DATA",
                0x201 => "RIGHT_RAW_DATA",
                0x040 => "START_LEFT_STREAM",
                0x041 => "START_RIGHT_STREAM",
                0x044 => "STOP_ALL_STREAMS",
                0x300 => "SYSTEM_STATUS",
                0x032 => "STATUS_REQUEST",
                0x030 => "MODE_INTERNAL",
                0x031 => "MODE_ADS1115",
                _ => $"UNKNOWN_0x{ID:X3}"
            };
        }

        // Create a copy of the message
        public CANMessage Clone()
        {
            byte[] dataCopy = new byte[Data?.Length ?? 0];
            Data?.CopyTo(dataCopy, 0);
            return new CANMessage(ID, dataCopy, Timestamp);
        }
    }

    // ViewModel for displaying CAN messages in UI
    public class CANMessageViewModel : INotifyPropertyChanged
    {
        private readonly CANMessage _message;
        private readonly HashSet<uint> _rxMessageIds;
        private readonly HashSet<uint> _txMessageIds;

        public CANMessage Message => _message;

        public CANMessageViewModel(CANMessage message, HashSet<uint> rxMessageIds, HashSet<uint> txMessageIds)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _rxMessageIds = rxMessageIds ?? new HashSet<uint>();
            _txMessageIds = txMessageIds ?? new HashSet<uint>();
        }

        // Properties for UI binding
        public string Time => _message.Timestamp.ToString("HH:mm:ss.fff");

        public string ID => $"0x{_message.ID:X3}";

        public string Direction => _message.Direction;

        public string Data
        {
            get
            {
                if (_message.Data == null || _message.Data.Length == 0)
                    return "No Data";

                // Format data as space-separated hex bytes
                return string.Join(" ", _message.Data.Select(b => $"{b:X2}"));
            }
        }

        public string Length => _message.Length.ToString();

        public string MessageType
        {
            get
            {
                return _message.ID switch
                {
                    0x200 or 0x201 => "Raw Data",
                    0x040 or 0x041 or 0x044 => "Stream Control",
                    0x300 or 0x032 or 0x030 or 0x031 => "System",
                    _ => "Unknown"
                };
            }
        }

        public string Decoded
        {
            get
            {
                try
                {
                    return DecodeMessage();
                }
                catch (Exception ex)
                {
                    return $"Decode Error: {ex.Message}";
                }
            }
        }

        // Decode message based on CAN Protocol Specification v0.9
        private string DecodeMessage()
        {
            // Handle empty messages properly (v0.9 protocol has empty messages)
            if (_message.Data == null || _message.Data.Length == 0)
            {
                // For commands that expect empty data, show decoded name
                switch (_message.ID)
                {
                    case 0x044: return "Stop All Streams (empty)";
                    case 0x030: return "Switch to Internal ADC (empty)";
                    case 0x031: return "Switch to ADS1115 (empty)";
                    default: return "Empty Message";
                }
            }

            switch (_message.ID)
            {
                case 0x200:
                    return DecodeRawADCData("Left Side", _message.Data);

                case 0x201:
                    return DecodeRawADCData("Right Side", _message.Data);

                case 0x040:
                    return DecodeStreamControl("Start Left Stream", _message.Data);

                case 0x041:
                    return DecodeStreamControl("Start Right Stream", _message.Data);

                case 0x044:
                    return "Stop All Streams";

                case 0x300:
                    return DecodeSystemStatus(_message.Data);

                case 0x032:
                    return "Request System Status";

                case 0x030:
                    return "Switch to Internal ADC Mode";

                case 0x031:
                    return "Switch to ADS1115 Mode";

                default:
                    return $"Unknown Message ID: 0x{_message.ID:X3}";
            }
        }

        private string DecodeRawADCData(string side, byte[] data)
        {
            if (data.Length < 2) return $"{side} Raw ADC Data (Invalid)";

            ushort rawADC = (ushort)(data[0] | (data[1] << 8));
            return $"{side} Raw ADC: {rawADC}";
        }

        private string DecodeStreamControl(string action, byte[] data)
        {
            if (data.Length < 1) return $"{action} (Invalid Data)";

            string rate = data[0] switch
            {
                0x01 => "100Hz",
                0x02 => "500Hz", 
                0x03 => "1kHz",
                0x05 => "1Hz",
                _ => $"Unknown Rate (0x{data[0]:X2})"
            };

            return $"{action}: {rate}";
        }

        private string DecodeSystemStatus(byte[] data)
        {
            if (data.Length < 3) return "System Status (Invalid)";

            byte status = data[0];
            byte errorFlags = data[1];
            byte adcMode = data[2];

            string statusText = status switch
            {
                0 => "OK",
                1 => "Warning", 
                2 => "Error",
                3 => "Critical",
                _ => "Unknown"
            };

            string modeText = adcMode switch
            {
                0 => "Internal",
                1 => "ADS1115",
                _ => "Unknown"
            };

            return $"System: {statusText}, Errors=0x{errorFlags:X2}, ADC={modeText}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}