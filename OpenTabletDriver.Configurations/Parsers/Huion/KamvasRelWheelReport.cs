using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Huion;

public struct KamvasRelWheelReport : IRelativeWheelReport
{
    public KamvasRelWheelReport(byte[] data)
    {
        Raw = data;

        AnalogDeltas = [
            data[3] == 1 ? GetWheelDelta(data[5]) : 0,
            data[3] == 2 ? GetWheelDelta(data[5]) : 0,
        ];
    }

    public KamvasRelWheelReport(byte[] data, int[] analogDeltas)
    {
        Raw = data;
        AnalogDeltas = analogDeltas;
    }

    public KamvasRelWheelReport(byte[] data, int wheel1Delta, int wheel2Delta)
    {
        Raw = data;
        AnalogDeltas = [wheel1Delta, wheel2Delta];
    }

    public static int GetWheelDelta(byte wheelData) =>
        wheelData switch
        {
            0x1 => 1,
            0x2 => -1,
            _ => 0,
        };

    public byte[] Raw { get; set; }
    public int[] AnalogDeltas { get; set; }
}
