using OpenTabletDriver.Configurations.Parsers.Huion;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class Q630MWiredParserTest
    {
        [Fact]
        public void WiredAuxPacket_ParsesSixAuxButtonsAndTwoWheelButtons()
        {
            var parser = new Q630MReportParser();

            var aux1 = Assert.IsType<Q630MAuxReport>(parser.Parse([0x08, 0xE0, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
            Assert.True(aux1.AuxButtons[0]);
            Assert.False(aux1.WheelButtons[0][0]);
            Assert.False(aux1.WheelButtons[1][0]);

            var wheel1Button = Assert.IsType<Q630MAuxReport>(parser.Parse([0x08, 0xE0, 0x01, 0x01, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
            Assert.True(wheel1Button.WheelButtons[0][0]);
            Assert.False(wheel1Button.WheelButtons[1][0]);

            var wheel2Button = Assert.IsType<Q630MAuxReport>(parser.Parse([0x08, 0xE0, 0x01, 0x01, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
            Assert.False(wheel2Button.WheelButtons[0][0]);
            Assert.True(wheel2Button.WheelButtons[1][0]);
        }

        [Theory]
        [InlineData(0x01, 0x01, 1, 0)]
        [InlineData(0x01, 0x02, -1, 0)]
        [InlineData(0x02, 0x01, 0, 1)]
        [InlineData(0x02, 0x02, 0, -1)]
        public void WiredWheelPackets_ParseBothDials(byte wheelIndex, byte direction, int expectedWheel1, int expectedWheel2)
        {
            var parser = new Q630MReportParser();

            var report = Assert.IsType<KamvasRelWheelReport>(
                parser.Parse([0x08, 0xF1, 0x01, wheelIndex, 0x00, direction, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));

            Assert.Equal(expectedWheel1, report.AnalogDeltas[0]);
            Assert.Equal(expectedWheel2, report.AnalogDeltas[1]);
        }
    }
}
