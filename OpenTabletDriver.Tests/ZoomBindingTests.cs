using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timers;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class ZoomBindingTests
    {
        [Fact]
        public void ZoomIn_DispatchesPositiveGestureDelta()
        {
            var gesture = Substitute.For<IGestureHandler>();
            using var binding = new ZoomInBinding
            {
                GestureHandler = gesture,
                Amount = 0.1f
            };

            binding.PerformZoom();

            gesture.Received(1).Zoom(Arg.Is<float>(f => Math.Abs(f - 0.1f) < 0.001f));
        }

        [Fact]
        public void ZoomOut_DispatchesNegativeGestureDelta()
        {
            var gesture = Substitute.For<IGestureHandler>();
            using var binding = new ZoomOutBinding
            {
                GestureHandler = gesture,
                Amount = 0.12f
            };

            binding.PerformZoom();

            gesture.Received(1).Zoom(Arg.Is<float>(f => Math.Abs(f - (-0.12f)) < 0.001f));
        }

        [Fact]
        public void Zoom_ReleasesEndGestureOnRelease()
        {
            var gesture = Substitute.For<IGestureHandler>();
            using var binding = new ZoomInBinding
            {
                GestureHandler = gesture
            };

            binding.Press(null!, null!);
            gesture.Received(1).Zoom(Arg.Any<float>());

            binding.Release(null!, null!);
            gesture.Received(1).EndGesture();
        }

        [Fact]
        public void Zoom_UsesCtrlScrollWhenNoGestureHandler()
        {
            var keyboard = Substitute.For<IVirtualKeyboard>();
            var scroll = Substitute.For<IMouseScrollHandler>();
            using var binding = new ZoomInBinding
            {
                GestureHandler = null,
                Pointer = scroll,
                Keyboard = keyboard
            };

            binding.PerformZoom();

            keyboard.Received(1).Press("Control");
            scroll.Received(1).ScrollVertically(120);
            keyboard.Received(1).Release("Control");
        }

        [Fact]
        public void Zoom_FallbackToKeyboardWhenNoGestureHandler()
        {
            var keyboard = Substitute.For<IVirtualKeyboard>();
            using var binding = new ZoomInBinding
            {
                GestureHandler = null,
                Pointer = null,
                Keyboard = keyboard
            };

            binding.PerformZoom();

            keyboard.Received(1).Press("Control");
            keyboard.Received(1).Press("Equal");
            keyboard.Received(1).Release("Equal");
            keyboard.Received(1).Release("Control");
        }

        [Fact]
        public void ZoomInAndOutBinding_DirectionProperty_IsHidden()
        {
            var zoomInDir = typeof(ZoomInBinding).GetProperty(nameof(ZoomBinding.Direction));
            var zoomOutDir = typeof(ZoomOutBinding).GetProperty(nameof(ZoomBinding.Direction));

            Assert.NotNull(zoomInDir);
            Assert.NotNull(zoomOutDir);

            Assert.True(zoomInDir.GetCustomAttributes(typeof(PluginIgnoreAttribute), true).Any());
            Assert.True(zoomOutDir.GetCustomAttributes(typeof(PluginIgnoreAttribute), true).Any());
        }

        [Fact]
        public void Zoom_HoldRepeatTimerDispatchesContinuously()
        {
            var gesture = Substitute.For<IGestureHandler>();
            var timer = Substitute.For<ITimer>();
            using var binding = new ZoomInBinding
            {
                GestureHandler = gesture,
                Timer = timer,
                InitialDelay = 150f,
                RepeatInterval = 30f
            };

            binding.Press(null!, null!);
            gesture.Received(1).Zoom(Arg.Any<float>());
            timer.Received(1).Start();
            Assert.Equal(150f, timer.Interval);

            // Trigger timer elapsed
            timer.Elapsed += Raise.Event<Action>();
            gesture.Received(2).Zoom(Arg.Any<float>());
            Assert.Equal(30f, timer.Interval);

            // Second tick
            timer.Elapsed += Raise.Event<Action>();
            gesture.Received(3).Zoom(Arg.Any<float>());

            binding.Release(null!, null!);
            timer.Received(1).Stop();
        }
    }
}
