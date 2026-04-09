using System;
using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MBluetoothSharedDialReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length < 5 || data[1] != 0xF1)
            {
                return new DeviceReport(data);
            }

            return new Q630MBluetoothSharedDialReport(
                data,
                GetWheel1Delta(data),
                IsWheel1ButtonPressed(data));
        }

        private static int GetWheel1Delta(byte[] data)
        {
            if (data[2] != 0x00)
            {
                return 0;
            }

            return BitConverter.ToInt16(data, 3);
        }

        private static bool IsWheel1ButtonPressed(byte[] data) => data[2] is 0x02 or 0x03;
    }
}
