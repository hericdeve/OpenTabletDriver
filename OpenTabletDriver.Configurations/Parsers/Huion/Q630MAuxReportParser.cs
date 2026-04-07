using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MAuxReportParser : IReportParser<IDeviceReport>
    {
        private readonly Dictionary<ulong, int> _shortcutButtonSlots = new()
        {
            { 0x050000000000, 0 }, // B
            { 0x080000000000, 1 }, // E
            { 0x0C0000000000, 2 }, // I
            { 0x116000000000, 3 }, // Ctrl+S
            { 0x02C000000000, 4 }, // Space
            { 0x51D000000000, 5 }  // Ctrl+Alt+Z
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
                for (int i = 0; i < data.Length; i++)
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

            if (LooksLikeLegacyBitfieldPacket(data))
            {
                return new UCLogicAuxReport(data);
            }

            return new Q630MBluetoothAuxReport(data, DecodeShortcutButtons(data));
        }

        private bool[] DecodeShortcutButtons(byte[] data)
        {
            var buttons = new bool[ButtonSlotCount];

            ulong signature = 0;
            for (int i = 2; i < data.Length; i++)
            {
                signature = (signature << 8) | data[i];
            }

            if (signature == 0)
            {
                return buttons;
            }

            if (!_shortcutButtonSlots.TryGetValue(signature, out int slot))
            {
                slot = _shortcutButtonSlots.Count;
                Log.Write("Q630MAux", $"Unmapped button pressed! Signature: {signature:X}, Assigned Slot: {slot}");
                
                if (slot >= ButtonSlotCount)
                {
                    return buttons;
                }

                _shortcutButtonSlots[signature] = slot;
            }

            buttons[slot] = true;
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
