using System;
using System.Numerics;
using System.Threading;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class TipDeepPressTests
    {
        private class DummyTabletReport : IAbsolutePositionReport, ITabletReport
        {
            public Vector2 Position { get; set; }
            public uint Pressure { get; set; }
            public Vector2 Tilt { get; set; }
            public bool[] PenButtons { get; set; } = new bool[2];
            public byte[] Raw { get; set; } = new byte[0];
        }

        private class MockStateBinding : IStateBinding
        {
            public int PressCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public bool IsPressed { get; private set; }

            public void Press(TabletReference tablet, IDeviceReport report)
            {
                PressCount++;
                IsPressed = true;
            }

            public void Release(TabletReference tablet, IDeviceReport report)
            {
                ReleaseCount++;
                IsPressed = false;
            }
        }

        private class MockPressurePointer : IAbsolutePointer, IPressureHandler
        {
            public float LastPressure { get; private set; }

            public void SetPosition(Vector2 pos) { }
            public void SetPressure(float percentage)
            {
                LastPressure = percentage;
            }
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
        public void NormalWriting_UnderThreshold_ActivatesTip_NotDeepPress()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var tipBinding = new MockStateBinding();
            var deepPressBinding = new MockStateBinding();

            handler.Tip = new ThresholdBindingState { Binding = tipBinding };
            handler.TipDeepPress = new DeepPressBindingState
            {
                Binding = deepPressBinding,
                ActivationThreshold = 80.0f,
                HoldDelayMs = 20.0f,
                SuppressStroke = true
            };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            // Pressure = 4096 (50%), well below 80% threshold
            var report = new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                Pressure = 4096
            };

            handler.Consume(report);

            Assert.True(tipBinding.IsPressed);
            Assert.False(deepPressBinding.IsPressed);
            Assert.NotNull(emittedReport);
            Assert.Equal(4096u, ((ITabletReport)emittedReport!).Pressure);
        }

        [Fact]
        public void DeepPress_HeldPastDelay_ActivatesDeepPress_AndSuppressesTip()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var tipBinding = new MockStateBinding();
            var deepPressBinding = new MockStateBinding();

            handler.Tip = new ThresholdBindingState { Binding = tipBinding };
            handler.TipDeepPress = new DeepPressBindingState
            {
                Binding = deepPressBinding,
                ActivationThreshold = 80.0f,
                HoldDelayMs = 25.0f,
                SuppressStroke = true
            };

            IDeviceReport? emittedReport = null;
            handler.Emit += r => emittedReport = r;

            // Step 1: Normal drawing at 50%
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                Pressure = 4096
            });
            Assert.True(tipBinding.IsPressed);
            Assert.False(deepPressBinding.IsPressed);

            // Step 2: Push firmly to 7000 (> 85%) - pending debounce delay
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                Pressure = 7000
            });
            Assert.False(deepPressBinding.IsPressed);

            // Wait past HoldDelayMs
            Thread.Sleep(40);

            // Step 3: Still holding firmly at 7000 - deep press engages!
            handler.Consume(new DummyTabletReport
            {
                Position = new Vector2(100, 100),
                Pressure = 7000
            });

            Assert.True(deepPressBinding.IsPressed);
            Assert.False(tipBinding.IsPressed); // Tip released / stroke cancelled
            Assert.NotNull(emittedReport);
            Assert.Equal(0u, ((ITabletReport)emittedReport!).Pressure); // Suppressed to 0
        }

        [Fact]
        public void FastDownstroke_UnderHoldDuration_DoesNotTriggerDeepPress()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var tipBinding = new MockStateBinding();
            var deepPressBinding = new MockStateBinding();

            handler.Tip = new ThresholdBindingState { Binding = tipBinding };
            handler.TipDeepPress = new DeepPressBindingState
            {
                Binding = deepPressBinding,
                ActivationThreshold = 80.0f,
                HoldDelayMs = 100.0f, // 100 ms hold required
                SuppressStroke = true
            };

            // Touch down at 40%
            handler.Consume(new DummyTabletReport { Pressure = 3200 });
            Assert.True(tipBinding.IsPressed);

            // Brief spike to 90%
            handler.Consume(new DummyTabletReport { Pressure = 7500 });
            Assert.False(deepPressBinding.IsPressed);

            // Pressure drops back to 40% before 100ms elapsed
            handler.Consume(new DummyTabletReport { Pressure = 3200 });

            Assert.False(deepPressBinding.IsPressed);
            Assert.True(tipBinding.IsPressed);
        }

        [Fact]
        public void Hysteresis_MaintainsDeepPressUntilBelowReleaseThreshold()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var tipBinding = new MockStateBinding();
            var deepPressBinding = new MockStateBinding();

            handler.Tip = new ThresholdBindingState { Binding = tipBinding };
            handler.TipDeepPress = new DeepPressBindingState
            {
                Binding = deepPressBinding,
                ActivationThreshold = 80.0f,
                ReleaseHysteresis = 10.0f, // Releases below 70%
                HoldDelayMs = 0.0f, // Instant for this test
                SuppressStroke = true,
                LiftTrigger = "Pressure Release"
            };

            // Engage deep press at 85% (8192 * 0.85 ~= 6963)
            handler.Consume(new DummyTabletReport { Pressure = 7000 });
            Assert.True(deepPressBinding.IsPressed);

            // Pressure wavers down to 75% (6144) - should STAY active because of 10% hysteresis (threshold 70%)
            handler.Consume(new DummyTabletReport { Pressure = 6144 });
            Assert.True(deepPressBinding.IsPressed);

            // Pressure drops to 65% (5324) - below 70%, deep press releases
            handler.Consume(new DummyTabletReport { Pressure = 5324 });
            Assert.False(deepPressBinding.IsPressed);
        }

        [Fact]
        public void PenLift_OutOfRange_ResetsDeepPress()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var tipBinding = new MockStateBinding();
            var deepPressBinding = new MockStateBinding();

            handler.Tip = new ThresholdBindingState { Binding = tipBinding };
            handler.TipDeepPress = new DeepPressBindingState
            {
                Binding = deepPressBinding,
                ActivationThreshold = 80.0f,
                HoldDelayMs = 0.0f,
                SuppressStroke = true
            };

            // Engage deep press
            handler.Consume(new DummyTabletReport { Pressure = 7000 });
            Assert.True(deepPressBinding.IsPressed);

            // Pen lifted out of range
            handler.Consume(new OutOfRangeReport(Array.Empty<byte>()));
            Assert.False(deepPressBinding.IsPressed);
            Assert.False(handler.TipDeepPress.LockTipUntilLift);

            // Next touch at light pressure (40%) operates as normal drawing stroke
            handler.Consume(new DummyTabletReport { Pressure = 3200 });
            Assert.True(tipBinding.IsPressed);
            Assert.False(deepPressBinding.IsPressed);
        }

        [Fact]
        public void UniformStrokePressure_ClampsPressureTo100Percent()
        {
            var tablet = CreateDummyTablet();
            var pointer = new MockPressurePointer();
            var outputMode = new Desktop.Output.AbsoluteMode
            {
                Tablet = tablet,
                Pointer = pointer,
                UniformStrokePressure = true
            };

            // Frame 1: Pressure > 0 (e.g. 2000 out of 8192)
            var report1 = new DummyTabletReport
            {
                Position = new Vector2(50, 50),
                Pressure = 2000
            };
            outputMode.Read(report1);
            Assert.Equal(1.0f, pointer.LastPressure);

            // Frame 2: Pressure = 0
            var report2 = new DummyTabletReport
            {
                Position = new Vector2(50, 50),
                Pressure = 0
            };
            outputMode.Read(report2);
            Assert.Equal(0.0f, pointer.LastPressure);
        }

        [Fact]
        public void DeepPress_WithLiftBinding_EvokesLiftBinding_OnPenLift()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var tipBinding = new MockStateBinding();
            var eraserBinding = new MockStateBinding();
            var penModeBinding = new MockStateBinding();

            handler.Tip = new ThresholdBindingState { Binding = tipBinding };
            handler.TipDeepPress = new DeepPressBindingState
            {
                Binding = eraserBinding,
                LiftBinding = penModeBinding,
                ActivationThreshold = 80.0f,
                HoldDelayMs = 0.0f,
                SuppressStroke = true
            };

            // Touch down and push to 85% -> Deep press eraser activates!
            handler.Consume(new DummyTabletReport { Pressure = 7000 });
            Assert.True(eraserBinding.IsPressed);
            Assert.Equal(1, eraserBinding.PressCount);
            Assert.Equal(0, penModeBinding.PressCount);

            // Pen lifts off digitizer (Pressure = 0)
            handler.Consume(new DummyTabletReport { Pressure = 0 });

            // Eraser releases, Pen mode evokes on lift!
            Assert.False(eraserBinding.IsPressed);
            Assert.Equal(1, eraserBinding.ReleaseCount);
            Assert.Equal(1, penModeBinding.PressCount);
            Assert.Equal(1, penModeBinding.ReleaseCount);
        }

        [Fact]
        public void DeepPress_WithLiftBinding_EvokesLiftBinding_OnPressureDropBelowThreshold()
        {
            var tablet = CreateDummyTablet();
            var handler = new BindingHandler(tablet);

            var tipBinding = new MockStateBinding();
            var eraserBinding = new MockStateBinding();
            var penModeBinding = new MockStateBinding();

            handler.Tip = new ThresholdBindingState { Binding = tipBinding };
            handler.TipDeepPress = new DeepPressBindingState
            {
                Binding = eraserBinding,
                LiftBinding = penModeBinding,
                LiftTrigger = "Pressure Release",
                ActivationThreshold = 80.0f,
                ReleaseHysteresis = 10.0f, // Releases below 70%
                HoldDelayMs = 0.0f,
                SuppressStroke = true
            };

            // Touch down and push to 85% -> Deep press eraser activates!
            handler.Consume(new DummyTabletReport { Pressure = 7000 });
            Assert.True(eraserBinding.IsPressed);
            Assert.Equal(1, eraserBinding.PressCount);
            Assert.Equal(0, penModeBinding.PressCount);

            // Ease up to 60% (below 70% threshold) while still touching tablet
            handler.Consume(new DummyTabletReport { Pressure = 4900 });

            // Eraser releases, Pen mode evokes!
            Assert.False(eraserBinding.IsPressed);
            Assert.Equal(1, eraserBinding.ReleaseCount);
            Assert.Equal(1, penModeBinding.PressCount);
            Assert.Equal(1, penModeBinding.ReleaseCount);

            // Subsequent pen lift does not double-fire lift binding
            handler.Consume(new DummyTabletReport { Pressure = 0 });
            Assert.Equal(1, penModeBinding.PressCount);
            Assert.Equal(1, penModeBinding.ReleaseCount);
        }
    }
}
