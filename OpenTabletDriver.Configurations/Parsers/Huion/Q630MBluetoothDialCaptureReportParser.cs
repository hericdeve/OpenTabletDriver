using System;
using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MBluetoothDialCaptureReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data)
        {
            Log.Write("Q630MBTDial", BitConverter.ToString(data));
            return new DeviceReport(data);
        }
    }
}
