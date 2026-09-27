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

        [Fact]
        public void WheelDebouncer_ReboundWithinThreshold_IsSuppressed()
        {
            long mockTimeMs = 1000;
            var debouncer = new Q630MWheelDebouncer(() => mockTimeMs);
            var parser = new Q630MReportParser(debouncer);

            // Step 1: Forward rotation (+1) at t=1000ms
            byte[] packetForward = [0x08, 0xF1, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            var r1 = Assert.IsType<KamvasRelWheelReport>(parser.Parse(packetForward));
            Assert.Equal(1, r1.AnalogDeltas[0]);

            // Step 2: Immediate mechanical rebound backward (-1) at t=1025ms (within 100ms rebound window)
            mockTimeMs = 1025;
            byte[] packetBackward1 = [0x08, 0xF1, 0x01, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            var r2 = Assert.IsType<KamvasRelWheelReport>(parser.Parse(packetBackward1));
            Assert.Equal(0, r2.AnalogDeltas[0]); // Suppressed!

            // Step 3: Intentional direction change backward (-1) after rebound window (t=1150ms > 1000+100ms)
            mockTimeMs = 1150;
            byte[] packetBackward2 = [0x08, 0xF1, 0x01, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            var r3 = Assert.IsType<KamvasRelWheelReport>(parser.Parse(packetBackward2));
            Assert.Equal(-1, r3.AnalogDeltas[0]); // Allowed!
        }

        [Fact]
        public void WheelDebouncer_FastSpuriousBurst_IsSuppressed()
        {
            long mockTimeMs = 2000;
            var debouncer = new Q630MWheelDebouncer(() => mockTimeMs);
            var parser = new Q630MReportParser(debouncer);

            // Step 1: Forward rotation (+1) at t=2000ms
            byte[] packet1 = [0x08, 0xF1, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            var r1 = Assert.IsType<KamvasRelWheelReport>(parser.Parse(packet1));
            Assert.Equal(1, r1.AnalogDeltas[0]);

            // Step 2: Spurious bounce packet in same direction after only 10ms (< 20ms debounce window)
            mockTimeMs = 2010;
            byte[] packet2 = [0x08, 0xF1, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            var r2 = Assert.IsType<KamvasRelWheelReport>(parser.Parse(packet2));
            Assert.Equal(0, r2.AnalogDeltas[0]); // Suppressed!

            // Step 3: Legitimate next detent at t=2030ms (30ms since last accepted tick >= 20ms)
            mockTimeMs = 2030;
            byte[] packet3 = [0x08, 0xF1, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            var r3 = Assert.IsType<KamvasRelWheelReport>(parser.Parse(packet3));
            Assert.Equal(1, r3.AnalogDeltas[0]); // Allowed!
        }

        [Fact]
        public void Parser_DuplicateDataReference_ReturnsCachedReport()
        {
            long mockTimeMs = 3000;
            var debouncer = new Q630MWheelDebouncer(() => mockTimeMs);
            var parser = new Q630MReportParser(debouncer);

            byte[] packet = [0x08, 0xF1, 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            var report1 = parser.Parse(packet);
            var report2 = parser.Parse(packet);

            Assert.Same(report1, report2);
        }
    }
}
