using OpenTabletDriver.Configurations.Parsers.Huion;
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
