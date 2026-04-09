using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin.Tablet.Wheel;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System;

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
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MAuxReportParser : IReportParser<IDeviceReport>
    {
        private readonly Dictionary<ulong, int> _shortcutButtonSlots = new()
        {
            { 0x0005, 0 },
            { 0x0500, 0 },
            { 0x0008, 1 },
            { 0x0800, 1 },
            { 0x000C, 2 },
            { 0x0C00, 2 },
            { 0x0116, 3 },
            { 0x1601, 3 },
            { 0x002C, 4 },
            { 0x2C00, 4 },
            { 0x051D, 5 },
            { 0x1D05, 5 }
        };
        private readonly Dictionary<byte, int> _shortcutButtonKeySlots = new()
        {
            { 0x05, 0 },
            { 0x08, 1 },
            { 0x0C, 2 },
            { 0x16, 3 },
            { 0x2C, 4 },
            { 0x1D, 5 }
        };
        private const int ButtonSlotCount = 8;

        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length < 2)
            {
                return new DeviceReport(data);
            }

            if (data.Length >= 8)
            {
                bool isZero = true;
                for (int i = 1; i < data.Length; i++)
                {
                    if (data[i] != 0)
                    {
                        isZero = false;
                        break;
                    }
                }
                
                if (isZero)
                {
                    return new Q630MBluetoothAuxReport(data, new bool[ButtonSlotCount]);
                }
            }

            if (data.Length >= 2 && !data[1].IsBitSet(7) && data[1] != 0xE0 && !(data[1].IsBitSet(5) && data[1].IsBitSet(6)) && data[1] != 0xC0 && data[1] != 0xF1) 
            {
                Log.Write("Q630MMystery", BitConverter.ToString(data));
            }

            if (data[1] == 0xF1)
            {
                if (data.Length < 6)
                {
                    return new DeviceReport(data);
                }

                return new KamvasRelWheelReport(data);
            }

            if (data[1] != 0xE0 && !(data[1].IsBitSet(5) && data[1].IsBitSet(6)))
            {
                return new DeviceReport(data);
            }

            if (data.Length < 4)
            {
                return new DeviceReport(data);
            }

            return new Q630MBluetoothAuxReport(data, DecodeShortcutButtons(data));
        }

        private bool[] DecodeShortcutButtons(byte[] data)
        {
            var buttons = new bool[ButtonSlotCount];
            if (data.Length < 4) return buttons;

            if (LooksLikeLegacyBitfieldPacket(data))
            {
                buttons[0] = data[4].IsBitSet(0);
                buttons[1] = data[4].IsBitSet(1);
                buttons[2] = data[4].IsBitSet(2);
                buttons[3] = data[4].IsBitSet(3);
                buttons[4] = data[4].IsBitSet(4);
                buttons[5] = data[4].IsBitSet(5);
                buttons[6] = data[4].IsBitSet(6);
                buttons[7] = data[4].IsBitSet(7);
                return buttons;
            }

            ulong signature = (ulong)((data[2] << 8) | data[3]);

            if (signature == 0)
            {
                return buttons;
            }

            if (!_shortcutButtonSlots.TryGetValue(signature, out int slot) &&
                !_shortcutButtonKeySlots.TryGetValue((byte)(signature & 0xFF), out slot))
            {
                Log.Write("Q630MAux", $"Unmapped button signature: {signature:X4}");
                return buttons;
            }

            if (slot < ButtonSlotCount)
            {
                buttons[slot] = true;
            }
            return buttons;
        }

        private static bool LooksLikeLegacyBitfieldPacket(byte[] data)
        {
            return data.Length >= 7 &&
                   (data[4] != 0 || data[5] != 0 || data[6] != 0) &&
                   data[2] == 0 && data[3] == 0;
        }
    }
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
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MBluetoothPenReportParser : IReportParser<IDeviceReport>
    {
        private readonly Dictionary<ulong, int> _shortcutButtonSlots = new()
        {
            { 0x0005, 0 },
            { 0x0500, 0 },
            { 0x0008, 1 },
            { 0x0800, 1 },
            { 0x000C, 2 },
            { 0x0C00, 2 },
            { 0x0116, 3 },
            { 0x1601, 3 },
            { 0x002C, 4 },
            { 0x2C00, 4 },
            { 0x051D, 5 },
            { 0x1D05, 5 }
        };
        private readonly Dictionary<byte, int> _shortcutButtonKeySlots = new()
        {
            { 0x05, 0 },
            { 0x08, 1 },
            { 0x0C, 2 },
            { 0x16, 3 },
            { 0x2C, 4 },
            { 0x1D, 5 }
        };
        private const int ButtonSlotCount = 8;

        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length < 2)
            {
                return new DeviceReport(data);
            }

            if (data.Length >= 8)
            {
                bool isZero = true;
                for (int i = 1; i < data.Length; i++)
                {
                    if (data[i] != 0)
                    {
                        isZero = false;
                        break;
                    }
                }
                
                if (isZero)
                {
                    return new Q630MBluetoothAuxReport(data, new bool[ButtonSlotCount]);
                }
            }

            if (data.Length >= 2 && !data[1].IsBitSet(7) && data[1] != 0xE0 && !(data[1].IsBitSet(5) && data[1].IsBitSet(6)) && data[1] != 0xC0 && data[1] != 0xF1) 
            {
                Log.Write("Q630MMystery", BitConverter.ToString(data));
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
                if (data.Length < 4)
                {
                    return new DeviceReport(data);
                }

                return new Q630MBluetoothAuxReport(data, DecodeShortcutButtons(data));
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

        private bool[] DecodeShortcutButtons(byte[] data)
        {
            var buttons = new bool[ButtonSlotCount];
            if (data.Length < 4) return buttons;

            if (LooksLikeLegacyBitfieldPacket(data))
            {
                buttons[0] = data[4].IsBitSet(0);
                buttons[1] = data[4].IsBitSet(1);
                buttons[2] = data[4].IsBitSet(2);
                buttons[3] = data[4].IsBitSet(3);
                buttons[4] = data[4].IsBitSet(4);
                buttons[5] = data[4].IsBitSet(5);
                buttons[6] = data[4].IsBitSet(6);
                buttons[7] = data[4].IsBitSet(7);
                return buttons;
            }

            ulong signature = (ulong)((data[2] << 8) | data[3]);

            if (signature == 0)
            {
                return buttons;
            }

            if (!_shortcutButtonSlots.TryGetValue(signature, out int slot) &&
                !_shortcutButtonKeySlots.TryGetValue((byte)(signature & 0xFF), out slot))
            {
                Log.Write("Q630MPen", $"Unmapped button signature: {signature:X4}");
                return buttons;
            }

            if (slot < ButtonSlotCount)
            {
                buttons[slot] = true;
            }
            return buttons;
        }

        private static bool LooksLikeLegacyBitfieldPacket(byte[] data)
        {
            return data.Length >= 7 &&
                   (data[4] != 0 || data[5] != 0 || data[6] != 0) &&
                   data[2] == 0 && data[3] == 0;
        }
    }
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
