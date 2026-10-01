using System;
using System.Numerics;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class MultiActionBindingTests
    {
        public class MockStateBinding : IStateBinding
        {
            public int PressCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public void Press(TabletReference tablet, IDeviceReport report) => PressCount++;
            public void Release(TabletReference tablet, IDeviceReport report) => ReleaseCount++;
        }

        private class DummyTabletReport : IAbsolutePositionReport, ITabletReport
        {
            public Vector2 Position { get; set; }
            public uint Pressure { get; set; }
            public Vector2 Tilt { get; set; }
            public bool[] PenButtons { get; set; } = new bool[2];
            public byte[] Raw { get; set; } = Array.Empty<byte>();
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
                        Pen = new PenSpecifications { ButtonCount = 2, MaxPressure = 8192 },
                        Digitizer = new DigitizerSpecifications { Width = 1000, Height = 1000, MaxX = 1000, MaxY = 1000 }
                    }
                }
            };
        }

        [Fact]
        public async Task MultiActionBinding_HoldLiftAction_TriggersOnButtonRelease()
        {
            typeof(MultiActionBinding).GetField("_lastHoldActionFiredGlobal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, DateTime.MinValue);

            var tablet = CreateDummyTablet();
            var binding = new MultiActionBinding
            {
                HoldThresholdMs = 50f,
                LiftTrigger = "Button Release"
            };

            var mockHold = new MockStateBinding();
            var mockLift = new MockStateBinding();

            binding.HoldAction = new PluginSettingStore(typeof(MockStateBinding));
            binding.HoldLiftAction = new PluginSettingStore(typeof(MockStateBinding));

            typeof(MultiActionBinding).GetField("_holdBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockHold);
            typeof(MultiActionBinding).GetField("_holdLiftBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockLift);

            var report = new DummyTabletReport { Pressure = 2000 };

            // Press and hold past threshold
            binding.Press(tablet, report);
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (mockHold.PressCount == 0 && DateTime.UtcNow < timeout)
                await Task.Delay(10);

            // Hold should have activated
            Assert.Equal(1, mockHold.PressCount);
            Assert.Equal(0, mockLift.PressCount);

            // Release button
            binding.Release(tablet, report);

            // Hold released, Lift action evoked
            Assert.Equal(1, mockHold.ReleaseCount);
            Assert.Equal(1, mockLift.PressCount);
            Assert.Equal(1, mockLift.ReleaseCount);
        }

        [Fact]
        public async Task MultiActionBinding_HoldLiftAction_TriggersOnPenTipLift()
        {
            typeof(MultiActionBinding).GetField("_lastHoldActionFiredGlobal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, DateTime.MinValue);

            var tablet = CreateDummyTablet();
            var binding = new MultiActionBinding
            {
                HoldThresholdMs = 50f,
                LiftTrigger = "Pen Tip Lift"
            };

            var mockHold = new MockStateBinding();
            var mockLift = new MockStateBinding();

            binding.HoldAction = new PluginSettingStore(typeof(MockStateBinding));
            binding.HoldLiftAction = new PluginSettingStore(typeof(MockStateBinding));

            typeof(MultiActionBinding).GetField("_holdBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockHold);
            typeof(MultiActionBinding).GetField("_holdLiftBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockLift);

            var downReport = new DummyTabletReport { Pressure = 2000 };

            // Press and hold with pen touching
            binding.Press(tablet, downReport);
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (mockHold.PressCount == 0 && DateTime.UtcNow < timeout)
                await Task.Delay(10);

            Assert.Equal(1, mockHold.PressCount);
            Assert.Equal(0, mockLift.PressCount);

            // Pen tip lifts while button held
            var upReport = new DummyTabletReport { Pressure = 0 };
            binding.Update(tablet, upReport);

            // Lift action fired immediately
            Assert.Equal(1, mockLift.PressCount);
            Assert.Equal(1, mockLift.ReleaseCount);

            // Subsequent button release does not double-fire lift action
            binding.Release(tablet, upReport);
            Assert.Equal(1, mockHold.ReleaseCount);
            Assert.Equal(1, mockLift.PressCount);
            Assert.Equal(1, mockLift.ReleaseCount);
        }

        [Fact]
        public void MultiActionBinding_WhenHoldActionNull_AndLiftConfigured_UsesTapAsHoldImmediately()
        {
            typeof(MultiActionBinding).GetField("_lastHoldActionFiredGlobal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, DateTime.MinValue);

            var tablet = CreateDummyTablet();
            var binding = new MultiActionBinding
            {
                LiftTrigger = "Button Release"
            };

            var mockTapAsHold = new MockStateBinding();
            var mockLift = new MockStateBinding();

            binding.TapAction = new PluginSettingStore(typeof(MockStateBinding));
            binding.HoldLiftAction = new PluginSettingStore(typeof(MockStateBinding));

            typeof(MultiActionBinding).GetField("_tapBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockTapAsHold);
            typeof(MultiActionBinding).GetField("_holdBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockTapAsHold);
            typeof(MultiActionBinding).GetField("_holdLiftBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockLift);

            var report = new DummyTabletReport { Pressure = 2000 };

            // Press should activate immediately (0 delay)
            binding.Press(tablet, report);
            Assert.Equal(1, mockTapAsHold.PressCount);
            Assert.Equal(0, mockLift.PressCount);

            // Release button
            binding.Release(tablet, report);
            Assert.Equal(1, mockTapAsHold.ReleaseCount);
            Assert.Equal(1, mockLift.PressCount);
            Assert.Equal(1, mockLift.ReleaseCount);
        }

        [Fact]
        public void MultiActionBinding_DeepClick_ActivatesAndSuppressesStroke()
        {
            var tablet = CreateDummyTablet();
            var binding = new MultiActionBinding
            {
                DeepClickThreshold = 80f,
                DeepClickHoldDelayMs = 0f,
                DeepClickSuppressStroke = true
            };

            var mockTap = new MockStateBinding();
            var mockDeep = new MockStateBinding();
            var mockDeepLift = new MockStateBinding();

            binding.TapAction = new PluginSettingStore(typeof(MockStateBinding));
            binding.DeepClickAction = new PluginSettingStore(typeof(MockStateBinding));
            binding.DeepClickLiftAction = new PluginSettingStore(typeof(MockStateBinding));

            var deepPressState = new DeepPressBindingState
            {
                Binding = mockDeep,
                LiftBinding = mockDeepLift,
                ActivationThreshold = 80f,
                HoldDelayMs = 0f,
                SuppressStroke = true
            };

            typeof(MultiActionBinding).GetField("_tapBinding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, mockTap);
            typeof(MultiActionBinding).GetField("_deepPressState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(binding, deepPressState);

            // Report below threshold: 4000 / 8192 ~= 48.8%
            var lowReport = new DummyTabletReport { Pressure = 4000 };
            binding.Update(tablet, lowReport);
            Assert.Equal(0, mockDeep.PressCount);
            Assert.False(binding.SuppressTip);

            // Report above threshold: 7000 / 8192 ~= 85.4%
            var highReport = new DummyTabletReport { Pressure = 7000 };
            binding.Update(tablet, highReport);
            Assert.Equal(1, mockDeep.PressCount);
            Assert.True(binding.SuppressTip);

            // Pen lifts: pressure drops to 0
            var liftReport = new DummyTabletReport { Pressure = 0 };
            binding.Update(tablet, liftReport);
            Assert.Equal(1, mockDeep.ReleaseCount);
            Assert.Equal(1, mockDeepLift.ReleaseCount);
            Assert.False(binding.SuppressTip);
        }

        [Fact]
        public void PluginSettingStore_FormatsMultiAction_WithDeepClickFriendlyString()
        {
            var store = new PluginSettingStore(typeof(MultiActionBinding));
            var tapStore = new PluginSettingStore(typeof(MockStateBinding));
            var deepStore = new PluginSettingStore(typeof(MockStateBinding));

            store["TapAction"].SetValue(tapStore);
            store["DeepClickAction"].SetValue(deepStore);

            var friendly = store.ToString();
            Assert.Contains("Deep:", friendly);
            Assert.Contains("MockStateBinding", friendly);
        }
    }
}
