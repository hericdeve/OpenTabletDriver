using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Huion;

public struct Q630MBluetoothSharedDialReport : IRelativeWheelReport, IWheelButtonReport
{
    public Q630MBluetoothSharedDialReport(byte[] report, int wheel1Delta, bool wheel1Button)
    {
        Raw = report;
        AnalogDeltas = [wheel1Delta, 0];
        WheelButtons = [[wheel1Button], [false]];
    }

    public byte[] Raw { get; set; }
    public int[] AnalogDeltas { get; set; }
    public bool[][] WheelButtons { get; set; }
}
