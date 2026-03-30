using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    public struct Q630MAuxReport : IAuxReport, IWheelButtonReport
    {
        public Q630MAuxReport(byte[] report)
        {
            Raw = report;

            AuxButtons =
            [
                report[4].IsBitSet(0),
                report[4].IsBitSet(1),
                report[4].IsBitSet(2),
                report[4].IsBitSet(3),
                report[4].IsBitSet(4),
                report[4].IsBitSet(5),
            ];

            WheelButtons = 
            [
                [report[4].IsBitSet(6)],
                [report[4].IsBitSet(7)]
            ];
        }

        public bool[] AuxButtons { set; get; }
        public bool[][] WheelButtons { set; get; }
        public byte[] Raw { set; get; }
    }
}
