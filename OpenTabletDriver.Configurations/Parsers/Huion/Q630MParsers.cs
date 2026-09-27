using OpenTabletDriver.Configurations.Parsers.UCLogic;
using OpenTabletDriver.Plugin.Tablet.Wheel;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OpenTabletDriver.Configurations.Parsers.Huion
{
    public class Q630MWheelDebouncer
    {
        private readonly Func<double> _timeProvider;
        public double MinStepIntervalMs { get; }
        public double ReverseLockoutMs { get; }
        public double IdleResetMs { get; }

        private readonly double[] _lastEmitTime = [-1e9, -1e9];
        private readonly int[] _lastDirection = [0, 0];

        public Q630MWheelDebouncer(
            Func<double>? timeProvider = null,
            double minStepIntervalMs = 20.0,
            double reverseLockoutMs = 120.0,
            double idleResetMs = 200.0)
        {
            _timeProvider = timeProvider ?? GetDefaultTimestampMs;
            MinStepIntervalMs = minStepIntervalMs;
            ReverseLockoutMs = reverseLockoutMs;
            IdleResetMs = idleResetMs;
        }

        private static double GetDefaultTimestampMs() =>
            Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

        public int FilterDelta(int wheelIndex, int rawDelta)
        {
            if (rawDelta == 0 || wheelIndex < 0 || wheelIndex >= _lastEmitTime.Length)
                return 0;

            double currentTime = _timeProvider();
            int direction = Math.Sign(rawDelta);
            double lastTime = _lastEmitTime[wheelIndex];
            int lastDir = _lastDirection[wheelIndex];
            double elapsed = currentTime - lastTime;

            // If wheel was idle or has not been moved yet, accept the pulse immediately (0ms latency).
            if (elapsed >= IdleResetMs || lastDir == 0)
            {
                _lastEmitTime[wheelIndex] = currentTime;
                _lastDirection[wheelIndex] = direction;
                return direction;
            }

            // If pulse is in the same direction, enforce minimum detent interval
            // to filter contact chatter pulses from a single physical click.
            if (direction == lastDir)
            {
                if (elapsed >= MinStepIntervalMs)
                {
                    _lastEmitTime[wheelIndex] = currentTime;
                    return direction;
                }

                return 0;
            }

            // If pulse is in opposite direction, enforce reverse lockout
            // to filter opposing bounce pulses while actively rotating.
            if (elapsed >= ReverseLockoutMs)
            {
                _lastEmitTime[wheelIndex] = currentTime;
                _lastDirection[wheelIndex] = direction;
                return direction;
            }

            return 0;
        }

        public void Reset()
        {
            for (int i = 0; i < _lastEmitTime.Length; i++)
            {
                _lastEmitTime[i] = -1e9;
                _lastDirection[i] = 0;
            }
        }
    }

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
        private readonly Q630MWheelDebouncer _debouncer;
        private byte[]? _lastData;
        private IDeviceReport? _lastReport;

        public Q630MAuxReportParser() : this(new Q630MWheelDebouncer())
        {
        }

        public Q630MAuxReportParser(Q630MWheelDebouncer debouncer)
        {
            _debouncer = debouncer;
        }

        public Q630MWheelDebouncer Debouncer => _debouncer;

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
            if (ReferenceEquals(data, _lastData) && _lastReport != null)
            {
                return _lastReport;
            }

            var report = ParseInternal(data);
            _lastData = data;
            _lastReport = report;
            return report;
        }

        private IDeviceReport ParseInternal(byte[] data)
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

                int w1 = _debouncer.FilterDelta(0, data[3] == 1 ? KamvasRelWheelReport.GetWheelDelta(data[5]) : 0);
                int w2 = _debouncer.FilterDelta(1, data[3] == 2 ? KamvasRelWheelReport.GetWheelDelta(data[5]) : 0);
                return new KamvasRelWheelReport(data, w1, w2);
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
public struct Q630MBluetoothTabletReport : ITabletReport
    {
        public Q630MBluetoothTabletReport(byte[] report)
            : this(report, GetDefaultPenButtons(report))
        {
        }

        public Q630MBluetoothTabletReport(byte[] report, bool[] penButtons)
        {
            Raw = report;

            Position = new Vector2
            {
                X = Unsafe.ReadUnaligned<ushort>(ref report[2]),
                Y = Unsafe.ReadUnaligned<ushort>(ref report[4])
            };
            Pressure = Unsafe.ReadUnaligned<ushort>(ref report[6]);
            PenButtons = penButtons;
        }

        public byte[] Raw { get; set; }
        public Vector2 Position { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; }

        private static bool[] GetDefaultPenButtons(byte[] report)
        {
            // Q630M Bluetooth appears to expose two logical pen buttons, while the
            // generic tablet report shape assumes three bits. Treat the second
            // logical button as the union of bits 2 and 3 so alternating firmware
            // status packets don't drop the hold state on button 2.
            var penByte = report[1];
            return
            [
                penByte.IsBitSet(1),
                penByte.IsBitSet(2) || penByte.IsBitSet(3),
            ];
        }
    }
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MBluetoothPenReportParser : IReportParser<IDeviceReport>
    {
        private readonly Q630MWheelDebouncer _debouncer;
        private byte[]? _lastData;
        private IDeviceReport? _lastReport;

        public Q630MBluetoothPenReportParser() : this(new Q630MWheelDebouncer())
        {
        }

        public Q630MBluetoothPenReportParser(Q630MWheelDebouncer debouncer)
        {
            _debouncer = debouncer;
        }

        public Q630MWheelDebouncer Debouncer => _debouncer;

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
        private bool[] _previousPenButtons = new bool[2];
        private uint _previousPressure;
        private int _consecutiveClearButtonPackets;
        private int _consecutiveZeroPressurePackets;

        public IDeviceReport Parse(byte[] data)
        {
            if (ReferenceEquals(data, _lastData) && _lastReport != null)
            {
                return _lastReport;
            }

            var report = ParseInternal(data);
            _lastData = data;
            _lastReport = report;
            return report;
        }

        private IDeviceReport ParseInternal(byte[] data)
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

                int w1 = _debouncer.FilterDelta(0, data[3] == 1 ? KamvasRelWheelReport.GetWheelDelta(data[5]) : 0);
                int w2 = _debouncer.FilterDelta(1, data[3] == 2 ? KamvasRelWheelReport.GetWheelDelta(data[5]) : 0);
                return new KamvasRelWheelReport(data, w1, w2);
            }

            // Some firmware variants reuse the pen endpoint for shortcut packets, but
            // normal in-range pen packets also carry the same high status bits. Only
            // treat these as aux reports when they actually decode into known shortcut
            // state, otherwise keep them on the pen path.
            if (LooksLikeBluetoothShortcutPacket(data))
            {
                return new Q630MBluetoothAuxReport(data, DecodeShortcutButtons(data));
            }

            // On Q630M Bluetooth, 0xC0 packets still carry live hover coordinates.
            // Treat 0x00 as the true out-of-range state and keep 0xC0 on the pen path.
            if (data[1] == 0x00)
            {
                ResetPenState();
                return new OutOfRangeReport(data);
            }

            // BT endpoint reports are 10-byte pen packets (no tilt fields).
            if (data.Length < 8)
            {
                return new DeviceReport(data);
            }

            return StabilizePenReport(new Q630MBluetoothTabletReport(data));
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

        private bool LooksLikeBluetoothShortcutPacket(byte[] data)
        {
            if (data.Length < 8)
            {
                return false;
            }

            if (data[1] != 0xE0 && !(data[1].IsBitSet(5) && data[1].IsBitSet(6)))
            {
                return false;
            }

            if (LooksLikeLegacyBitfieldPacket(data))
            {
                return true;
            }

            var buttons = DecodeShortcutButtons(data);
            return Array.Exists(buttons, static pressed => pressed);
        }

        private Q630MBluetoothTabletReport StabilizePenReport(Q630MBluetoothTabletReport report)
        {
            bool anyButtonsPressed = Array.Exists(report.PenButtons, static pressed => pressed);
            if (!anyButtonsPressed && Array.Exists(_previousPenButtons, static pressed => pressed))
            {
                _consecutiveClearButtonPackets++;
                if (_consecutiveClearButtonPackets == 1)
                {
                    report.PenButtons = (bool[])_previousPenButtons.Clone();
                }
                else
                {
                    _previousPenButtons = [false, false];
                }
            }
            else
            {
                _consecutiveClearButtonPackets = 0;
                _previousPenButtons = (bool[])report.PenButtons.Clone();
            }

            if (report.Pressure == 0 && _previousPressure > 0)
            {
                _consecutiveZeroPressurePackets++;
                if (_consecutiveZeroPressurePackets == 1)
                {
                    report.Pressure = _previousPressure;
                }
                else
                {
                    _previousPressure = 0;
                }
            }
            else
            {
                _consecutiveZeroPressurePackets = 0;
                _previousPressure = report.Pressure;
            }

            return report;
        }

        private void ResetPenState()
        {
            _previousPenButtons = [false, false];
            _previousPressure = 0;
            _consecutiveClearButtonPackets = 0;
            _consecutiveZeroPressurePackets = 0;
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
        private readonly Q630MWheelDebouncer _debouncer;
        private byte[]? _lastData;
        private IDeviceReport? _lastReport;

        public Q630MBluetoothSharedDialReportParser() : this(new Q630MWheelDebouncer())
        {
        }

        public Q630MBluetoothSharedDialReportParser(Q630MWheelDebouncer debouncer)
        {
            _debouncer = debouncer;
        }

        public Q630MWheelDebouncer Debouncer => _debouncer;

        public IDeviceReport Parse(byte[] data)
        {
            if (ReferenceEquals(data, _lastData) && _lastReport != null)
            {
                return _lastReport;
            }

            var report = ParseInternal(data);
            _lastData = data;
            _lastReport = report;
            return report;
        }

        private IDeviceReport ParseInternal(byte[] data)
        {
            if (data.Length < 5 || data[1] != 0xF1)
            {
                return new DeviceReport(data);
            }

            int rawDelta = GetWheel1Delta(data);
            int debouncedDelta = _debouncer.FilterDelta(0, rawDelta);

            return new Q630MBluetoothSharedDialReport(
                data,
                debouncedDelta,
                IsWheel1ButtonPressed(data));
        }

        private static int GetWheel1Delta(byte[] data)
        {
            if (data[2] != 0x00)
            {
                return 0;
            }

            // The BT firmware sends an accumulated int16 delta that can be very large
            // (or even anomalous, e.g. -256) when the pen is also active and BT bandwidth
            // is constrained. Clamp to ±1 to match the wired KamvasRelWheelReport
            // behaviour and prevent a single burst packet from firing the binding
            // hundreds of times.
            return Math.Sign(BitConverter.ToInt16(data, 3));
        }

        private static bool IsWheel1ButtonPressed(byte[] data) => data[2] is 0x02 or 0x03;
    }
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Q630MReportParser : IReportParser<IDeviceReport>
    {
        private readonly Q630MWheelDebouncer _debouncer;
        private byte[]? _lastData;
        private IDeviceReport? _lastReport;

        public Q630MReportParser() : this(new Q630MWheelDebouncer())
        {
        }

        public Q630MReportParser(Q630MWheelDebouncer debouncer)
        {
            _debouncer = debouncer;
        }

        public Q630MWheelDebouncer Debouncer => _debouncer;

        public IDeviceReport Parse(byte[] data)
        {
            if (ReferenceEquals(data, _lastData) && _lastReport != null)
            {
                return _lastReport;
            }

            var report = ParseInternal(data);
            _lastData = data;
            _lastReport = report;
            return report;
        }

        private IDeviceReport ParseInternal(byte[] data)
        {
            if (data.Length < 2)
                return new DeviceReport(data);

            if (data[1] == 0xF1)
            {
                if (data.Length >= 6)
                {
                    int w1 = _debouncer.FilterDelta(0, data[3] == 1 ? KamvasRelWheelReport.GetWheelDelta(data[5]) : 0);
                    int w2 = _debouncer.FilterDelta(1, data[3] == 2 ? KamvasRelWheelReport.GetWheelDelta(data[5]) : 0);
                    return new KamvasRelWheelReport(data, w1, w2);
                }

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
