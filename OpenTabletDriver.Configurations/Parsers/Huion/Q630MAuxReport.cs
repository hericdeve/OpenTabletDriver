using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    public struct Q630MAuxReport : IAuxReport, IWheelButtonReport
    {
        public Q630MAuxReport(byte[] report)
        {
            Raw = report;

            bool bit(int index, int position) => report.Length > index && report[index].IsBitSet(position);

            AuxButtons =
            [
                bit(4, 0),
                bit(4, 1),
                bit(4, 2),
                bit(4, 3),
                bit(4, 4),
                bit(4, 5),
            ];

            WheelButtons =
            [
                [bit(4, 6)],
                [bit(4, 7)]
            ];
        }

        public bool[] AuxButtons { set; get; }
        public bool[][] WheelButtons { set; get; }
        public byte[] Raw { set; get; }
    }
}
