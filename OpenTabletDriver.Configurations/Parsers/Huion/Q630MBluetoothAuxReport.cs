using System;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    public struct Q630MBluetoothAuxReport : IAuxReport, IWheelButtonReport
    {
        public Q630MBluetoothAuxReport(byte[] report, bool[] buttons)
        {
            Raw = report;

            AuxButtons = new bool[6];
            if (buttons is not null)
            {
                Array.Copy(buttons, AuxButtons, Math.Min(AuxButtons.Length, buttons.Length));
            }

            WheelButtons =
            [
                [buttons is not null && buttons.Length > 6 && buttons[6]],
                [buttons is not null && buttons.Length > 7 && buttons[7]],
            ];
        }

        public bool[] AuxButtons { set; get; }
        public bool[][] WheelButtons { set; get; }
        public byte[] Raw { set; get; }
    }
}
