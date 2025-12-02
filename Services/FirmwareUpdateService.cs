using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Services
{
    public sealed class FirmwareUpdateService
    {
        private const int MaxChunkSize = 8;
        private const uint CmdId = BootloaderProtocol.CanIdBootCommand;
        private const uint DataId = BootloaderProtocol.CanIdBootData;

        private readonly CANService _canService;
        private readonly ProductionLogger _logger = ProductionLogger.Instance;

        public FirmwareUpdateService(CANService canService)
        {
            _canService = canService;
        }

        public async Task<bool> UpdateFirmwareAsync(string binPath, IProgress<FirmwareProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(binPath))
                throw new FileNotFoundException("Firmware binary not found", binPath);

            byte[] firmware = await File.ReadAllBytesAsync(binPath, cancellationToken).ConfigureAwait(false);
            int totalChunks = (firmware.Length + (MaxChunkSize - 1)) / MaxChunkSize;

            _logger.LogInfo($"Firmware update start. Size={firmware.Length} bytes, Chunks={totalChunks}", "FWUpdater");

            if (!_canService.RequestEnterBootloader())
            {
                _logger.LogError("Failed to request bootloader entry", "FWUpdater");
                return false;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            if (!SendCommand(BootloaderProtocol.BootCmdPing))
            {
                _logger.LogError("Bootloader ping failed", "FWUpdater");
                return false;
            }

            if (!SendBeginCommand(firmware.Length))
            {
                _logger.LogError("Bootloader begin command failed", "FWUpdater");
                return false;
            }

            uint runningCrc = 0xFFFFFFFFu;
            for (int chunk = 0; chunk < totalChunks; chunk++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int offset = chunk * MaxChunkSize;
                int remaining = Math.Min(MaxChunkSize, firmware.Length - offset);
                byte[] data = new byte[remaining];
                Array.Copy(firmware, offset, data, 0, remaining);

                if (!_canService.SendMessage(DataId, PadData(data)))
                {
                    _logger.LogError($"Failed to send chunk {chunk}", "FWUpdater");
                    return false;
                }

                runningCrc = UpdateCrc(runningCrc, data);
                progress?.Report(new FirmwareProgress(chunk + 1, totalChunks));

                await Task.Delay(2, cancellationToken).ConfigureAwait(false);
            }

            uint finalCrc = runningCrc ^ 0xFFFFFFFFu;
            if (!SendEndCommand(finalCrc))
            {
                _logger.LogError("Bootloader end command failed", "FWUpdater");
                return false;
            }

            _logger.LogInfo("Firmware update completed.", "FWUpdater");
            return true;
        }

        private bool SendCommand(byte command)
        {
            return _canService.SendMessage(CmdId, new byte[] { command });
        }

        private bool SendBeginCommand(int size)
        {
            byte[] payload = new byte[8];
            payload[0] = BootloaderProtocol.BootCmdBegin;
            BitConverter.GetBytes(size).CopyTo(payload, 2);
            return _canService.SendMessage(CmdId, payload);
        }

        private bool SendEndCommand(uint crc)
        {
            byte[] payload = new byte[8];
            payload[0] = BootloaderProtocol.BootCmdEnd;
            BitConverter.GetBytes(crc).CopyTo(payload, 2);
            return _canService.SendMessage(CmdId, payload);
        }

        private static byte[] PadData(byte[] data)
        {
            if (data.Length == MaxChunkSize)
                return data;
            byte[] padded = new byte[MaxChunkSize];
            Array.Copy(data, padded, data.Length);
            for (int i = data.Length; i < MaxChunkSize; i++)
                padded[i] = 0xFF;
            return padded;
        }

        private static uint UpdateCrc(uint running, byte[] data)
        {
            const uint polynomial = 0x04C11DB7u;
            uint crc = running;
            foreach (byte b in data)
            {
                uint byteReflected = Reflect(b, 8);
                crc ^= byteReflected << 24;
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x80000000u) != 0)
                        crc = (crc << 1) ^ polynomial;
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        private static uint Reflect(uint data, int nBits)
        {
            uint reflection = 0;
            for (int bit = 0; bit < nBits; bit++)
            {
                if ((data & 0x01) != 0)
                    reflection |= (uint)(1 << ((nBits - 1) - bit));
                data >>= 1;
            }
            return reflection;
        }
    }

    public readonly struct FirmwareProgress
    {
        public int ChunksSent { get; }
        public int TotalChunks { get; }

        public FirmwareProgress(int chunksSent, int totalChunks)
        {
            ChunksSent = chunksSent;
            TotalChunks = totalChunks;
        }

        public double Percentage => (double)ChunksSent / TotalChunks * 100.0;
    }
}

