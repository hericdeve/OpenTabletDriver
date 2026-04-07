using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MAuxReportParser : IReportParser<IDeviceReport>
    {
        private readonly Dictionary<ulong, int> _shortcutButtonSlots = new();
        private const int ButtonSlotCount = 8;

        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length < 2)
            {
                return new DeviceReport(data);
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
