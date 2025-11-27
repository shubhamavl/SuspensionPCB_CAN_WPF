namespace SuspensionPCB_CAN_WPF
{
    internal static class BootloaderProtocol
    {
        public const uint CanIdBootCommand = 0x510;
        public const uint CanIdBootData = 0x511;
        public const uint CanIdBootStatus = 0x512;
        public const uint CanIdBootInfo = 0x513;

        public const byte AppCmdEnterBootloader = 0xA0;
        public const byte AppCmdQueryBootInfo = 0xA1;

        public const byte BootCmdPing = 0x01;
        public const byte BootCmdBegin = 0x02;
        public const byte BootCmdEnd = 0x03;
        public const byte BootCmdReset = 0x04;

        public enum BootloaderStatus : byte
        {
            Idle = 0,
            Ready = 1,
            InProgress = 2,
            Success = 3,
            FailedChecksum = 4,
            FailedTimeout = 5,
            FailedFlash = 6,
        }

        public static string DescribeStatus(BootloaderStatus status)
        {
            return status switch
            {
                BootloaderStatus.Idle => "Idle",
                BootloaderStatus.Ready => "Ready",
                BootloaderStatus.InProgress => "Updating...",
                BootloaderStatus.Success => "Last update succeeded",
                BootloaderStatus.FailedChecksum => "Checksum failed",
                BootloaderStatus.FailedTimeout => "Timeout while updating",
                BootloaderStatus.FailedFlash => "Flash error",
                _ => "Unknown",
            };
        }
    }
}

