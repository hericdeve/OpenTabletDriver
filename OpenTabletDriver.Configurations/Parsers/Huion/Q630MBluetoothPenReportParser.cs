using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MBluetoothPenReportParser : IReportParser<IDeviceReport>
    {
        private readonly Dictionary<ulong, int> _shortcutButtonSlots = new()
        {
            { 0x050000000000, 0 }, // Button 1
            { 0x080000000000, 1 }, // Button 2
            { 0x0C0000000000, 2 }, // Button 3
            { 0x1160000000000, 3 }, // Button 4
            { 0x02C0000000000, 4 }, // Button 5
            { 0x51D0000000000, 5 }  // Button 6
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

                if (LooksLikeLegacyBitfieldPacket(data))
                {
                    return new UCLogicAuxReport(data);
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
                Log.Write("Q630MPen", $"Unmapped button pressed! Signature: {signature:X}, Assigned Slot: {slot}");
                
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
