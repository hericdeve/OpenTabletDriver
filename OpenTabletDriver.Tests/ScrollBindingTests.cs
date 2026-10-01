using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timers;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class ScrollBindingTests
    {
        private class DummyTimer : ITimer
        {
            public bool Enabled { get; private set; }
            public float Interval { get; set; } = 1;
            public event System.Action? Elapsed;

            public void Start() => Enabled = true;
            public void Stop() => Enabled = false;
            public void Trigger() => Elapsed?.Invoke();
            public void Dispose() => Enabled = false;
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
        public void ScrollUpBinding_ScrollsVerticallyPositive()
        {
            var pointer = Substitute.For<IMouseScrollHandler, ISynchronousPointer>();
            var binding = new ScrollUpBinding
            {
                Pointer = pointer,
                Amount = 120
            };

            binding.Press(CreateDummyTablet(), Substitute.For<IDeviceReport>());

            pointer.Received(1).ScrollVertically(120);
            ((ISynchronousPointer)pointer).Received(1).Flush();
        }

        [Fact]
        public void ScrollDownBinding_ScrollsVerticallyNegative()
        {
            var pointer = Substitute.For<IMouseScrollHandler, ISynchronousPointer>();
            var binding = new ScrollDownBinding
            {
                Pointer = pointer,
                Amount = 120
            };

            binding.Press(CreateDummyTablet(), Substitute.For<IDeviceReport>());

            pointer.Received(1).ScrollVertically(-120);
            ((ISynchronousPointer)pointer).Received(1).Flush();
        }

        [Fact]
        public void ScrollLeftBinding_ScrollsHorizontallyNegative()
        {
            var pointer = Substitute.For<IMouseScrollHandler, ISynchronousPointer>();
            var binding = new ScrollLeftBinding
            {
                Pointer = pointer,
                Amount = 120
            };

            binding.Press(CreateDummyTablet(), Substitute.For<IDeviceReport>());

            pointer.Received(1).ScrollHorizontally(-120);
            ((ISynchronousPointer)pointer).Received(1).Flush();
        }

        [Fact]
        public void ScrollRightBinding_ScrollsHorizontallyPositive()
        {
            var pointer = Substitute.For<IMouseScrollHandler, ISynchronousPointer>();
            var binding = new ScrollRightBinding
            {
                Pointer = pointer,
                Amount = 120
            };

            binding.Press(CreateDummyTablet(), Substitute.For<IDeviceReport>());

            pointer.Received(1).ScrollHorizontally(120);
            ((ISynchronousPointer)pointer).Received(1).Flush();
        }

        [Fact]
        public void MouseScrollBinding_LegacyVerticalInverted_PreservesCompatibility()
        {
            var pointer = Substitute.For<IMouseScrollHandler, ISynchronousPointer>();
            var binding = new MouseScrollBinding
            {
                Pointer = pointer,
                Direction = "Vertical",
                Invert = true,
                Amount = 120
            };

            binding.Press(CreateDummyTablet(), Substitute.For<IDeviceReport>());

            // Legacy: Invert ? Amount : Amount * -1 => 120
            pointer.Received(1).ScrollVertically(120);
        }

        [Fact]
        public void MouseScrollBinding_RepeatTimer_DispatchesContinuousTicks()
        {
            var pointer = Substitute.For<IMouseScrollHandler, ISynchronousPointer>();
            var timer = new DummyTimer();
            var binding = new ScrollDownBinding
            {
                Pointer = pointer,
                Timer = timer,
                Amount = 120,
                InitialDelay = 150,
                Interval = 30
            };

            var tablet = CreateDummyTablet();
            var report = Substitute.For<IDeviceReport>();

            // Press fires immediately once
            binding.Press(tablet, report);
            pointer.Received(1).ScrollVertically(-120);
            Assert.True(timer.Enabled);
            Assert.Equal(150, timer.Interval);

            // First timer tick switches to repeat interval and scrolls
            timer.Trigger();
            pointer.Received(2).ScrollVertically(-120);
            Assert.Equal(30, timer.Interval);

            // Second timer tick
            timer.Trigger();
            pointer.Received(3).ScrollVertically(-120);

            // Release stops timer
            binding.Release(tablet, report);
            Assert.False(timer.Enabled);
        }
    }
}
