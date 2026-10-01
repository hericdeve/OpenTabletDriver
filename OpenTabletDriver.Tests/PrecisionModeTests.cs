using System.Numerics;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class PrecisionModeTests
    {
        private class DummyTabletReport : IAbsolutePositionReport, ITabletReport
        {
            public Vector2 Position { get; set; }
            public uint Pressure { get; set; }
            public Vector2 Tilt { get; set; }
            public bool[] PenButtons { get; set; } = new bool[2];
            public byte[] Raw { get; set; } = new byte[0];
        }

        private static TabletReference CreateDummyTablet()
        {
            return new TabletReference
            {
                Properties = new TabletConfiguration
                {
                    Name = "Test Tablet",
                    DigitizerIdentifiers = [],
                    Specifications = new TabletSpecifications
                    {
                        Pen = new PenSpecifications
                        {
                            ButtonCount = 2,
                            MaxPressure = 8192
                        },
                        Digitizer = new DigitizerSpecifications
                        {
                            Width = 1000,
                            Height = 1000,
                            MaxX = 1000,
                            MaxY = 1000
                        }
                    }
                }
            };
        }

        [Fact]
        public void PrecisionMode_Hold_ScalesMovementCorrectly()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var precisionBinding = new PrecisionModeBinding
            {
                Mode = "Hold",
                Sensitivity = 30, // 0.3x
                ReanchorOnLift = true
            };

            handler.PenButtons[0] = new BindingState { Binding = precisionBinding };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            // Frame 1: Touch down at (100, 100) with button held
            var report1 = new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                PenButtons = new[] { true, false },
                Pressure = 1000
            };
            handler.Consume(report1);
            Assert.NotNull(emittedReport);
            Assert.Equal(new Vector2(100, 100), ((IAbsolutePositionReport)emittedReport!).Position);

            // Frame 2: Move by +100 units on tablet to (200, 100)
            var report2 = new DummyTabletReport
            {
                Position = new Vector2(200, 100),
                PenButtons = new[] { true, false },
                Pressure = 1000
            };
            handler.Consume(report2);
            // Delta is 100 * 0.3 = 30px => Expected position 130, 100
            Assert.Equal(new Vector2(130, 100), ((IAbsolutePositionReport)emittedReport!).Position);

            // Frame 3: Release button (precision deactivates)
            var report3 = new DummyTabletReport
            {
                Position = new Vector2(200, 100),
                PenButtons = new[] { false, false },
                Pressure = 1000
            };
            handler.Consume(report3);
            // Returns to 1:1 true position
            Assert.Equal(new Vector2(200, 100), ((IAbsolutePositionReport)emittedReport!).Position);
        }

        [Fact]
        public void PrecisionMode_Toggle_MaintainsStateAcrossPresses()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var precisionBinding = new PrecisionModeBinding
            {
                Mode = "Toggle",
                Sensitivity = 50, // 0.5x
                ReanchorOnLift = true
            };

            handler.PenButtons[0] = new BindingState { Binding = precisionBinding };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            // Press 1 (Toggle ON)
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                PenButtons = new[] { true, false }
            });
            Assert.True(precisionBinding.IsActive);

            // Release button (still ON because of Toggle mode)
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                PenButtons = new[] { false, false }
            });
            Assert.True(precisionBinding.IsActive);

            // Move by +100 on X
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(200, 100),
                PenButtons = new[] { false, false }
            });
            // Delta 100 * 0.5 = 50 => Position 150, 100
            Assert.Equal(new Vector2(150, 100), ((IAbsolutePositionReport)emittedReport!).Position);

            // Press 2 (Toggle OFF)
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(200, 100),
                PenButtons = new[] { true, false }
            });
            Assert.False(precisionBinding.IsActive);
        }

        [Fact]
        public void PrecisionMode_ReanchorOnLift_EnablesSeamlessMultiStroke()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var precisionBinding = new PrecisionModeBinding
            {
                Mode = "Hold",
                Sensitivity = 50, // 0.5x
                ReanchorOnLift = true
            };

            handler.PenButtons[0] = new BindingState { Binding = precisionBinding };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            // Stroke 1: Start at (100, 100)
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                PenButtons = new[] { true, false }
            });
            // Move to (200, 100) => Screen pos = 150, 100
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(200, 100),
                PenButtons = new[] { true, false }
            });
            Assert.Equal(new Vector2(150, 100), ((IAbsolutePositionReport)emittedReport!).Position);

            // Pen lifts out of range
            handler.Consume(new OutOfRangeReport(new byte[0]));

            // Stroke 2: Touch down at (400, 400) while still holding button
            // With ReanchorOnLift = true, the cursor should smoothly start from previous screen position (150, 100),
            // NOT violently jump to 400, 400!
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(400, 400),
                PenButtons = new[] { true, false }
            });
            Assert.Equal(new Vector2(150, 100), ((IAbsolutePositionReport)emittedReport!).Position);

            // Move by +100 to (500, 400)
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(500, 400),
                PenButtons = new[] { true, false }
            });
            // Screen pos = 150 + 50 = 200, 100
            Assert.Equal(new Vector2(200, 100), ((IAbsolutePositionReport)emittedReport!).Position);
        }

        [Fact]
        public void PrecisionMode_PreservesTipPressureAndTilt()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var precisionBinding = new PrecisionModeBinding
            {
                Mode = "Hold",
                Sensitivity = 20
            };

            handler.PenButtons[0] = new BindingState { Binding = precisionBinding };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            var report = new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                PenButtons = new[] { true, false },
                Pressure = 4096,
                Tilt = new Vector2(15, -20)
            };
            handler.Consume(report);

            var result = emittedReport as DummyTabletReport;
            Assert.NotNull(result);
            Assert.Equal((uint)4096, result!.Pressure);
            Assert.Equal(new Vector2(15, -20), result.Tilt);
        }

        [Fact]
        public void PrecisionMode_SpeedMode_AllowsMultipliersAbove100Percent()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var precisionBinding = new PrecisionModeBinding
            {
                Mode = "Hold",
                Sensitivity = 200, // 2.0x speed mode
                ReanchorOnLift = true
            };

            handler.PenButtons[0] = new BindingState { Binding = precisionBinding };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                PenButtons = new[] { true, false }
            });

            // Move by +50 on X => delta * 2.0 = +100
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(150, 100),
                PenButtons = new[] { true, false }
            });

            Assert.Equal(new Vector2(200, 100), ((IAbsolutePositionReport)emittedReport!).Position);
        }

        [Fact]
        public void PrecisionMode_FixedCenter_WhenReanchorOnLiftIsFalse()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var precisionBinding = new PrecisionModeBinding
            {
                Mode = "Hold",
                Sensitivity = 50, // 0.5x
                ReanchorOnLift = false // Keep anchor locked to initial press coordinate
            };

            handler.PenButtons[0] = new BindingState { Binding = precisionBinding };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            // Touch down at (100, 100) -> Anchor is (100, 100)
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                PenButtons = new[] { true, false }
            });

            // Move to (200, 100) -> Screen pos = 100 + (100 * 0.5) = 150, 100
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(200, 100),
                PenButtons = new[] { true, false }
            });
            Assert.Equal(new Vector2(150, 100), ((IAbsolutePositionReport)emittedReport!).Position);

            // Pen lifts
            handler.Consume(new OutOfRangeReport(new byte[0]));

            // Touch down at (300, 100). Because ReanchorOnLift = false, anchor remains at (100, 100)
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(300, 100),
                PenButtons = new[] { true, false }
            });
            // Delta from original anchor is (300 - 100) = 200 * 0.5 = 100 => Pos = 100 + 100 = 200, 100
            Assert.Equal(new Vector2(200, 100), ((IAbsolutePositionReport)emittedReport!).Position);
        }

        [Fact]
        public void PrecisionMode_DaemonToggle_ActivatesPrecisionMode()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            bool isDaemonPrecisionOn = false;
            handler.IsDaemonPrecisionActive = () => isDaemonPrecisionOn;

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            // 1:1 Normal movement
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100)
            });
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(200, 100)
            });
            Assert.Equal(new Vector2(200, 100), ((IAbsolutePositionReport)emittedReport!).Position);

            // Turn on via Daemon (e.g. HUD action)
            isDaemonPrecisionOn = true;

            // Touch down at 200, 100
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(200, 100)
            });
            // Move by +100 to 300, 100 => 30% speed (+30px) => 230, 100
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(300, 100)
            });
            Assert.Equal(new Vector2(230, 100), ((IAbsolutePositionReport)emittedReport!).Position);
        }
    }
}
