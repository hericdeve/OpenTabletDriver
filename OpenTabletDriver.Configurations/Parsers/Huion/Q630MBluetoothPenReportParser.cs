using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MBluetoothPenReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length < 2)
            {
                return new DeviceReport(data);
            }

            // Wheel reports from some Q630M BT firmware variants.
            if (data[1] == 0xF1)
            {
                if (data.Length < 6)
                {
                    return new DeviceReport(data);
                }

                return new KamvasRelWheelReport(data);
            }

            // Aux/button reports can be emitted in UCLogic-style packets.
            if (data[1] == 0xE0 || (data[1].IsBitSet(5) && data[1].IsBitSet(6)))
            {
                if (data.Length < 7)
                {
                    return new DeviceReport(data);
                }

                return new UCLogicAuxReport(data);
            }

            if (data[1] == 0xC0)
            {
                return new OutOfRangeReport(data);
            }

            // BT endpoint reports are 10-byte pen packets (no tilt fields).
            if (data.Length < 8)
            {
                return new DeviceReport(data);
            }

            return new TabletReport(data);
        }
    }
}