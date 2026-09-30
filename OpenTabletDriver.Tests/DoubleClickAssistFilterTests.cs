using System;
using System.Numerics;
using System.Threading;
using OpenTabletDriver.Desktop.Output.Filters;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class DoubleClickAssistFilterTests
    {
        private class MockTabletReport : ITabletReport
        {
            public byte[] Raw { get; set; } = Array.Empty<byte>();
            public Vector2 Position { get; set; }
            public uint Pressure { get; set; }
            public bool[] PenButtons { get; set; } = new bool[2];
        }

        [Fact]
        public void Snap_Coordinates_On_Double_Tap()
        {
            var filter = new DoubleClickAssistFilter
            {
                Distance = 8.0f,
                MaxTimeMs = 450.0f
            };

            // Tap 1 Down
            var r1 = new MockTabletReport { Position = new Vector2(100, 100), Pressure = 1000 };
            filter.Consume(r1);
            Assert.Equal(new Vector2(100, 100), r1.Position);

            // Tap 1 Up
            var r2 = new MockTabletReport { Position = new Vector2(100, 100), Pressure = 0 };
            filter.Consume(r2);

            Thread.Sleep(30);

            // Tap 2 Down within distance threshold (distance ~ 5.83 px <= 8.0 px)
            var r3 = new MockTabletReport { Position = new Vector2(105, 103), Pressure = 1000 };
            filter.Consume(r3);

            // Should be snapped to tap 1 position
            Assert.Equal(new Vector2(100, 100), r3.Position);
        }

        [Fact]
        public void No_Snap_When_Second_Tap_Too_Far()
        {
            var filter = new DoubleClickAssistFilter
            {
                Distance = 8.0f,
                MaxTimeMs = 450.0f
            };

            // Tap 1 Down
            var r1 = new MockTabletReport { Position = new Vector2(100, 100), Pressure = 1000 };
            filter.Consume(r1);

            // Tap 1 Up
            var r2 = new MockTabletReport { Position = new Vector2(100, 100), Pressure = 0 };
            filter.Consume(r2);

            Thread.Sleep(30);

            // Tap 2 Down beyond distance threshold (distance ~ 28.28 px > 8.0 px)
            var r3 = new MockTabletReport { Position = new Vector2(120, 120), Pressure = 1000 };
            filter.Consume(r3);

            // Should NOT be snapped
            Assert.Equal(new Vector2(120, 120), r3.Position);
        }

        [Fact]
        public void No_Snap_When_Second_Tap_Too_Slow()
        {
            var filter = new DoubleClickAssistFilter
            {
                Distance = 8.0f,
                MaxTimeMs = 50.0f
            };

            // Tap 1 Down
            var r1 = new MockTabletReport { Position = new Vector2(100, 100), Pressure = 1000 };
            filter.Consume(r1);

            // Tap 1 Up
            var r2 = new MockTabletReport { Position = new Vector2(100, 100), Pressure = 0 };
            filter.Consume(r2);

            // Wait for window to expire
            Thread.Sleep(100);

            // Tap 2 Down within distance, but after timeout
            var r3 = new MockTabletReport { Position = new Vector2(102, 102), Pressure = 1000 };
            filter.Consume(r3);

            // Should NOT be snapped
            Assert.Equal(new Vector2(102, 102), r3.Position);
        }

        [Fact]
        public void Drag_Unlock_When_Moving_Past_Threshold()
        {
            var filter = new DoubleClickAssistFilter
            {
                Distance = 8.0f,
                MaxTimeMs = 450.0f,
                DragUnlockDistance = 14.0f
            };

            // Tap 1
            filter.Consume(new MockTabletReport { Position = new Vector2(100, 100), Pressure = 1000 });
            filter.Consume(new MockTabletReport { Position = new Vector2(100, 100), Pressure = 0 });

            Thread.Sleep(30);

            // Tap 2 Down
            var r3 = new MockTabletReport { Position = new Vector2(103, 103), Pressure = 1000 };
            filter.Consume(r3);
            Assert.Equal(new Vector2(100, 100), r3.Position); // Snapped

            // Hold & small movement within drag unlock distance (dist = 5.65 px <= 14 px)
            var r4 = new MockTabletReport { Position = new Vector2(104, 104), Pressure = 1000 };
            filter.Consume(r4);
            Assert.Equal(new Vector2(100, 100), r4.Position); // Still snapped

            // Hold & large movement exceeding drag unlock distance (dist = 28.28 px > 14 px)
            var r5 = new MockTabletReport { Position = new Vector2(120, 120), Pressure = 1000 };
            filter.Consume(r5);
            Assert.Equal(new Vector2(120, 120), r5.Position); // Unlocked!
        }

        [Fact]
        public void OutOfRange_Resets_State()
        {
            var filter = new DoubleClickAssistFilter
            {
                Distance = 8.0f,
                MaxTimeMs = 450.0f
            };

            // Tap 1 Down
            filter.Consume(new MockTabletReport { Position = new Vector2(100, 100), Pressure = 1000 });
            // Tap 1 Up
            filter.Consume(new MockTabletReport { Position = new Vector2(100, 100), Pressure = 0 });

            // Pen leaves proximity
            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));

            // Pen returns and taps
            var r3 = new MockTabletReport { Position = new Vector2(103, 103), Pressure = 1000 };
            filter.Consume(r3);

            // Should NOT be snapped because OutOfRange cleared state
            Assert.Equal(new Vector2(103, 103), r3.Position);
        }
    }
}
