using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SuspensionPCB_CAN_WPF
{
    // CAN Message data structure
    public class CANMessage
    {
        public uint ID { get; set; }
        public byte[] Data { get; set; }
        public DateTime Timestamp { get; set; }
        public int Length => Data?.Length ?? 0;

        public CANMessage()
        {
            ID = 0;
            Data = new byte[0];
            Timestamp = DateTime.Now;
        }

        public CANMessage(uint id, byte[] data)
        {
            ID = id;
            Data = data ?? new byte[0];
            Timestamp = DateTime.Now;
        }

        public CANMessage(uint id, byte[] data, DateTime timestamp)
        {
            ID = id;
            Data = data ?? new byte[0];
            Timestamp = timestamp;
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

        public string Direction
        {
            get
            {
                if (_rxMessageIds.Contains(_message.ID))
                    return "RX";
                else if (_txMessageIds.Contains(_message.ID))
                    return "TX";
                else
                    return "??";
            }
        }

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

        // Decode message based on CAN Protocol Specification v0.5
        private string DecodeMessage()
        {
            if (_message.Data == null || _message.Data.Length == 0)
                return "No Data";

            switch (_message.ID)
            {
                case 0x000:
                    return _message.Data[0] == 0x01 ? "Emergency Stop Command" : "Emergency Command";

                case 0x001:
                    return _message.Data[0] == 0x02 ? "System Shutdown Command" : "System Command";

                case 0x030:
                    return DecodeDataRequest("Suspension Weight Data", _message.Data);

                case 0x031:
                    return DecodeDataRequest("Axle Weight Data", _message.Data);

                case 0x020:
                    return DecodeVariableCalibrationStartV06(_message.Data);

                case 0x022:
                    return DecodeWeightCalibrationPoint(_message.Data);

                case 0x023:
                    return "[Removed in v0.6] Percentage calibration is not supported";

                case 0x024:
                    return DecodeCompleteCalibration(_message.Data);

                case 0x025:
                    return "Save Calibration to Flash";

                case 0x026:
                    return "Load Calibration from Flash";

                case 0x027:
                    return DecodeLoadCellRating(_message.Data);

                case 0x200:
                    return DecodeSuspensionWeightData(_message.Data);

                case 0x201:
                    return DecodeAxleWeightData(_message.Data);

                case 0x400:
                    return DecodeVariableCalibrationData(_message.Data);

                case 0x401:
                    return DecodeCalibrationQuality(_message.Data);

                case 0x402:
                    return DecodeErrorResponse(_message.Data);

                case 0x500:
                    return DecodeSystemStatus(_message.Data);

                default:
                    return $"Unknown Message ID: 0x{_message.ID:X3}";
            }
        }

        private string DecodeDataRequest(string dataType, byte[] data)
        {
            if (data.Length < 2) return $"Request {dataType} (Invalid Data)";

            string action = data[0] == 0x01 ? "Start" : "Stop";
            string rate = data[1] switch
            {
                0x01 => "100Hz",
                0x02 => "500Hz",
                0x03 => "1024Hz",
                0x04 => "2048Hz",
                _ => $"Unknown Rate (0x{data[1]:X2})"
            };

            return $"Request {dataType}: {action} at {rate}";
        }

        private string DecodeVariableCalibrationStartV06(byte[] data)
        {
            if (data.Length < 8) return "Start Variable Calibration (v0.6) [Invalid Data]";

            // v0.6 format:
            // Byte0: 0x01, Bytes1-4: Reserved (0), Bytes5-6: MaxWeight (kg*10), Byte7: Channel Mask
            ushort maxWeight = (ushort)(data[5] | (data[6] << 8));
            byte channelMask = data[7];
            return $"Start Variable Calibration (v0.6): Max {maxWeight / 10.0:F1} kg, Channels=0x{channelMask:X2}";
        }

        private string DecodeWeightCalibrationPoint(byte[] data)
        {
            // v0.6 format:
            // Byte0: 0x03, Byte1: Reserved, Bytes2-3: Weight (kg*10), Byte4: Channel Mask
            if (data.Length < 5) return "Set Weight Point (v0.6) [Invalid Data]";

            ushort weight = (ushort)(data[2] | (data[3] << 8));
            byte channelMask = data[4];
            return $"Set Weight Point (v0.6): {weight / 10.0:F1} kg, Channels=0x{channelMask:X2}";
        }

        // v0.6: Percentage calibration removed

        private string DecodeCompleteCalibration(byte[] data)
        {
            // v0.6 format:
            // Byte0: 0x05, Byte1: Channel Mask, Byte2: Auto Analyze, Byte3: Auto Save
            if (data.Length < 4) return "Complete Calibration (v0.6) [Invalid Data]";

            string autoAnalyze = data[2] == 0x01 ? "Auto Analyze" : "Manual";
            string autoSave = data[3] == 0x01 ? "Auto Save" : "Manual";
            byte channelMask = data[1];

            return $"Complete Calibration (v0.6): {autoAnalyze}, {autoSave}, Channels=0x{channelMask:X2}";
        }

        private string DecodeLoadCellRating(byte[] data)
        {
            if (data.Length < 6) return "Set Load Cell Rating (Invalid Data)";

            ushort rating = (ushort)(data[2] | (data[3] << 8));
            ushort voltage = (ushort)(data[4] | (data[5] << 8));

            return $"Set Load Cell Rating: {rating / 10.0:F1} kg at {voltage / 10.0:F1} mV";
        }

        private string DecodeSuspensionWeightData(byte[] data)
        {
            if (data.Length < 8) return "Suspension Weight Data (Invalid)";

            double fl = BitConverter.ToUInt16(data, 0) / 10.0;
            double fr = BitConverter.ToUInt16(data, 2) / 10.0;
            double rl = BitConverter.ToUInt16(data, 4) / 10.0;
            double rr = BitConverter.ToUInt16(data, 6) / 10.0;

            return $"Suspension Weights: FL={fl:F1} FR={fr:F1} RL={rl:F1} RR={rr:F1} kg";
        }

        private string DecodeAxleWeightData(byte[] data)
        {
            if (data.Length < 8) return "Axle Weight Data (Invalid)";

            double fl = BitConverter.ToUInt16(data, 0) / 10.0;
            double fr = BitConverter.ToUInt16(data, 2) / 10.0;
            double rl = BitConverter.ToUInt16(data, 4) / 10.0;
            double rr = BitConverter.ToUInt16(data, 6) / 10.0;

            return $"Axle Weights: FL={fl:F1} FR={fr:F1} RL={rl:F1} RR={rr:F1} kg";
        }

        private string DecodeVariableCalibrationData(byte[] data)
        {
            // v0.6 response (0x400):
            // Byte0: Point Index, Byte1: Status, Bytes2-3: Ref Weight (kg*10), Bytes4-5: ADC
            if (data.Length < 6) return "Variable Calibration Data (v0.6) [Invalid]";

            byte pointIndex = data[0];
            byte status = data[1];
            ushort weight = BitConverter.ToUInt16(data, 2);
            ushort adcValue = BitConverter.ToUInt16(data, 4);

            string statusText = status switch
            {
                0x00 => "Valid",
                0x80 => "Calibration Started",
                0x81 => "Session Reset",
                0x82 => "Calibration Completed",
                0x83 => "Calibration Saved",
                0x84 => "Calibration Loaded",
                0x85 => "Point Deleted",
                0x86 => "Session Reset",
                0x87 => "Point Count",
                0x88 => "Specific Point Deleted",
                0x90 => "Calibrated Weight Verification",
                0x91 => "Raw ADC Conversion",
                0x92 => "Current Weight at Start",
                0x07 => "Calibration Not Active",
                _ => $"Status 0x{status:X2}"
            };

            return $"Calibration Point {pointIndex}: {statusText}, {weight / 10.0:F1}kg, ADC={adcValue}";
        }

        private string DecodeCalibrationQuality(byte[] data)
        {
            // v0.6 (0x401): Bytes0-1 Accuracy*10, Bytes2-3 MaxErr*10, Byte4 Grade, Byte5 Recommendation
            if (data.Length < 6) return "Calibration Quality (v0.6) [Invalid]";

            ushort accuracy = BitConverter.ToUInt16(data, 0);
            ushort maxError = BitConverter.ToUInt16(data, 2);
            byte grade = data[4];
            byte recommendation = data[5];

            string gradeText = grade switch
            {
                0x01 => "Excellent",
                0x02 => "Good",
                0x03 => "Acceptable",
                0x04 => "Poor",
                0x05 => "Failed",
                _ => "Unknown"
            };

            string recText = recommendation switch
            {
                0x01 => "Accept",
                0x02 => "Retry",
                0x03 => "Add Points",
                _ => "None"
            };

            return $"Quality (v0.6): {accuracy / 10.0:F1}% accuracy, {gradeText} grade, Rec: {recText}";
        }

        private string DecodeErrorResponse(byte[] data)
        {
            if (data.Length < 3) return "Error Response (Invalid)";

            ushort errorCode = BitConverter.ToUInt16(data, 0);
            byte severity = data[2];

            string severityText = severity switch
            {
                0x01 => "INFO",
                0x02 => "WARNING",
                0x03 => "ERROR",
                0x04 => "CRITICAL",
                0x05 => "FATAL",
                _ => "UNKNOWN"
            };

            return $"Error: Code=0x{errorCode:X4}, Severity={severityText}";
        }

        private string DecodeSystemStatus(byte[] data)
        {
            if (data.Length < 3) return "System Status (Invalid)";

            byte status = data[0];
            byte errorFlags = data[1];
            byte nodeCount = data[2];

            string statusText = status switch
            {
                0 => "OK",
                1 => "Warning",
                2 => "Error",
                3 => "Critical",
                _ => "Unknown"
            };

            return $"System: {statusText}, Errors=0x{errorFlags:X2}, Nodes={nodeCount}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}