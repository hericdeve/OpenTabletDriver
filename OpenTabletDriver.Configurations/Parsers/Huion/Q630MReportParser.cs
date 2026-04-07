using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length < 2)
                return new DeviceReport(data);

            if (data[1] == 0xF1)
            {
                if (data.Length >= 6)
                    return new KamvasRelWheelReport(data);

                return new DeviceReport(data);
            }

            if (data[1] == 0xE0 || (data[1].IsBitSet(5) && data[1].IsBitSet(6)))
            {
                if (data.Length >= 5)
                    return new Q630MAuxReport(data);

                return new DeviceReport(data);
            }

            if (data[1] == 0xC0)
                return new OutOfRangeReport(data);

            if (data.Length >= 12)
                return new GianoReport(data);

            if (data.Length >= 8)
                return new TabletReport(data);

            return new DeviceReport(data);
        }
    }
}
