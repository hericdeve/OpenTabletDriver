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
    }
}
