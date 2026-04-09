using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
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

            if (!_shortcutButtonSlots.TryGetValue(signature, out int slot))
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
}
