using OpenTabletDriver.Configurations.Parsers.Huion;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class Q630MBluetoothParserTest
    {
        [Fact]
        public void AuxParser_LegacyBitfieldPacket_PreservesTwoDialButtons()
        {
            var parser = new Q630MAuxReportParser();

            var report = parser.Parse([0x00, 0xE0, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00]);

            var auxReport = Assert.IsType<Q630MBluetoothAuxReport>(report);
            Assert.False(auxReport.AuxButtons[0]);
            Assert.False(auxReport.AuxButtons[5]);
            Assert.True(auxReport.WheelButtons[0][0]);
            Assert.True(auxReport.WheelButtons[1][0]);
        }

        [Fact]
        public void PenParser_LegacyBitfieldPacket_PreservesTwoDialButtons()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = parser.Parse([0x00, 0xE0, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00]);

            var auxReport = Assert.IsType<Q630MBluetoothAuxReport>(report);
            Assert.True(auxReport.WheelButtons[0][0]);
            Assert.False(auxReport.WheelButtons[1][0]);
        }

        [Fact]
        public void AuxParser_SignaturePacket_MapsShortcutButtonsWithoutSettingDialButtons()
        {
            var parser = new Q630MAuxReportParser();

            var report = parser.Parse([0x00, 0xE0, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00]);

            var auxReport = Assert.IsType<Q630MBluetoothAuxReport>(report);
            Assert.True(auxReport.AuxButtons[0]);
            Assert.False(auxReport.WheelButtons[0][0]);
            Assert.False(auxReport.WheelButtons[1][0]);
        }

        [Theory]
        [InlineData(0x00, 0x16, 3)]
        [InlineData(0x01, 0x16, 3)]
        [InlineData(0x05, 0x1D, 5)]
        [InlineData(0x01, 0x1D, 5)]
        public void AuxParser_KeycodeFallback_MapsButtonsWithVaryingModifierBytes(byte modifier, byte keycode, int expectedSlot)
        {
            var parser = new Q630MAuxReportParser();

            var report = Assert.IsType<Q630MBluetoothAuxReport>(
                parser.Parse([0x00, 0xE0, modifier, keycode, 0x00, 0x00, 0x00, 0x00]));

            for (int i = 0; i < report.AuxButtons.Length; i++)
            {
                Assert.Equal(i == expectedSlot, report.AuxButtons[i]);
            }
        }

        [Theory]
        [InlineData(0x00, 0x16, 3)]
        [InlineData(0x01, 0x16, 3)]
        [InlineData(0x05, 0x1D, 5)]
        [InlineData(0x01, 0x1D, 5)]
        public void PenParser_KeycodeFallback_MapsButtonsWithVaryingModifierBytes(byte modifier, byte keycode, int expectedSlot)
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = Assert.IsType<Q630MBluetoothAuxReport>(
                parser.Parse([0x00, 0xE0, modifier, keycode, 0x00, 0x00, 0x00, 0x00]));

            for (int i = 0; i < report.AuxButtons.Length; i++)
            {
                Assert.Equal(i == expectedSlot, report.AuxButtons[i]);
            }
        }

        [Fact]
        public void PenParser_DoesNotMisclassifyRegularPenPacketWithInRangeBits()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = parser.Parse([0x02, 0xE0, 0x34, 0x12, 0x78, 0x56, 0x20, 0x03]);

            var tabletReport = Assert.IsType<Q630MBluetoothTabletReport>(report);
            Assert.Equal(0x1234u, tabletReport.Position.X);
            Assert.Equal(0x5678u, tabletReport.Position.Y);
            Assert.Equal(0x0320u, tabletReport.Pressure);
        }

        [Fact]
        public void PenParser_PreservesSecondBarrelButtonHoldOnPenPackets()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE4, 0x34, 0x12, 0x78, 0x56, 0x00, 0x00]));

            Assert.False(report.PenButtons[0]);
            Assert.True(report.PenButtons[1]);
        }

        [Fact]
        public void PenParser_PreservesTipStateOnPenPackets()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE2, 0x34, 0x12, 0x78, 0x56, 0x10, 0x00]));

            Assert.True(report.PenButtons[0]);
        }

        [Fact]
        public void PenParser_TreatsC0StatusAsHoverInRange()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xC0, 0x34, 0x12, 0x78, 0x56, 0x00, 0x00]));

            Assert.Equal(0x1234u, report.Position.X);
            Assert.Equal(0x5678u, report.Position.Y);
            Assert.Equal(0u, report.Pressure);
            Assert.False(report.PenButtons[0]);
            Assert.False(report.PenButtons[1]);
        }

        [Fact]
        public void PenParser_TreatsZeroStatusAsOutOfRange()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = parser.Parse([0x02, 0x00, 0x34, 0x12, 0x78, 0x56, 0x00, 0x00]);

            Assert.IsType<OutOfRangeReport>(report);
        }

        [Theory]
        [InlineData(0xC3, true, false)]
        [InlineData(0xC4, false, true)]
        [InlineData(0xC5, false, true)]
        public void PenParser_MapsRealBluetoothStatusBits(byte status, bool expectedButton1, bool expectedButton2)
        {
            var parser = new Q630MBluetoothPenReportParser();

            var report = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, status, 0x34, 0x12, 0x78, 0x56, 0x20, 0x03]));

            Assert.Equal(expectedButton1, report.PenButtons[0]);
            Assert.Equal(expectedButton2, report.PenButtons[1]);
        }

        [Fact]
        public void PenParser_SuppressesSinglePacketBarrelButton2Drop()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var pressed = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE4, 0x34, 0x12, 0x78, 0x56, 0x20, 0x03]));
            var preserved = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE0, 0x34, 0x12, 0x78, 0x56, 0x20, 0x03]));
            var released = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE0, 0x34, 0x12, 0x78, 0x56, 0x20, 0x03]));

            Assert.True(pressed.PenButtons[1]);
            Assert.True(preserved.PenButtons[1]);
            Assert.False(released.PenButtons[1]);
        }

        [Fact]
        public void PenParser_SuppressesSinglePacketPressureDrop()
        {
            var parser = new Q630MBluetoothPenReportParser();

            var touching = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE2, 0x34, 0x12, 0x78, 0x56, 0x10, 0x00]));
            var preserved = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE2, 0x34, 0x12, 0x78, 0x56, 0x00, 0x00]));
            var dropped = Assert.IsType<Q630MBluetoothTabletReport>(
                parser.Parse([0x02, 0xE2, 0x34, 0x12, 0x78, 0x56, 0x00, 0x00]));

            Assert.Equal(16u, touching.Pressure);
            Assert.Equal(16u, preserved.Pressure);
            Assert.Equal(0u, dropped.Pressure);
        }

        [Fact]
        public void SharedDialParser_RoutesBluetoothRotationToWheel1()
        {
            var parser = new Q630MBluetoothSharedDialReportParser();

            var report = Assert.IsType<Q630MBluetoothSharedDialReport>(
                parser.Parse([0x03, 0xF1, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));

            Assert.Equal(1, report.AnalogDeltas[0]);
            Assert.Equal(0, report.AnalogDeltas[1]);
            Assert.False(report.WheelButtons[0][0]);
            Assert.False(report.WheelButtons[1][0]);
        }

        [Fact]
        public void SharedDialParser_RoutesBluetoothNegativeRotationToWheel1()
        {
            var parser = new Q630MBluetoothSharedDialReportParser();

            var report = Assert.IsType<Q630MBluetoothSharedDialReport>(
                parser.Parse([0x03, 0xF1, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00]));

            Assert.Equal(-1, report.AnalogDeltas[0]);
            Assert.Equal(0, report.AnalogDeltas[1]);
        }

        [Theory]
        [InlineData(0x02, true)]
        [InlineData(0x03, true)]
        [InlineData(0x00, false)]
        public void SharedDialParser_RoutesBluetoothDialButtonToWheel1(byte packetType, bool expectedPressed)
        {
            var parser = new Q630MBluetoothSharedDialReportParser();

            var report = Assert.IsType<Q630MBluetoothSharedDialReport>(
                parser.Parse([0x03, 0xF1, packetType, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));

            Assert.Equal(0, report.AnalogDeltas[0]);
            Assert.True(report.WheelButtons.Length >= 2);
            Assert.Equal(expectedPressed, report.WheelButtons[0][0]);
            Assert.False(report.WheelButtons[1][0]);
        }
    }
}
