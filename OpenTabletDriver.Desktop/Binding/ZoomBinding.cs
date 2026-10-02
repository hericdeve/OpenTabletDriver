using System;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timers;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName("Zoom Binding"), PluginIgnore]
    public class ZoomBinding : IStateBinding, IDisposable
    {
        private ITimer? _timer;
        private ZoomDirection _direction = ZoomDirection.In;
        private bool _isRepeating;

        public enum ZoomDirection
        {
            In = 0,
            Out = 1
        }

        [Resolved]
        public IGestureHandler? GestureHandler { get; set; }

        [Resolved]
        public IMouseScrollHandler? Pointer { get; set; }

        [Resolved]
        public IVirtualKeyboard? Keyboard { get; set; }

        [Resolved]
        public ITimer? Timer
        {
            get => _timer;
            set
            {
                if (_timer != null)
                    _timer.Elapsed -= OnTimerElapsed;

                _timer = value;
                if (_timer != null)
                {
                    _timer.Elapsed += OnTimerElapsed;
                    _timer.Interval = InitialDelay;
                }
            }
        }

        [Property("Direction"), DefaultPropertyValue("In")]
        public virtual string Direction
        {
            get => _direction.ToString();
            set
            {
                if (Enum.TryParse<ZoomDirection>(value, true, out var parsed))
                    _direction = parsed;
            }
        }

        [Property("Amount"), DefaultPropertyValue(0.08f)]
        public float Amount { get; set; } = 0.08f;

        [Property("Initial Delay"), DefaultPropertyValue(200f)]
        public float InitialDelay { get; set; } = 200f;

        [Property("Interval"), DefaultPropertyValue(40f)]
        public float RepeatInterval { get; set; } = 40f;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            PerformZoom();

            if (Timer != null)
            {
                _isRepeating = false;
                Timer.Interval = InitialDelay;
                Timer.Start();
            }
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            Timer?.Stop();
            _isRepeating = false;
            GestureHandler?.EndGesture();
        }

        public void PerformZoom()
        {
            float signedDelta = _direction == ZoomDirection.In ? Amount : -Amount;

            if (GestureHandler != null)
            {
                GestureHandler.Zoom(signedDelta);
                return;
            }

            var scrollHandler = Pointer;
            var keyboard = Keyboard;

            // In non-DI / runtime scenarios, fallback to DesktopInterop if unresolved
            if (scrollHandler == null && keyboard == null)
            {
                scrollHandler = DesktopInterop.RelativePointer as IMouseScrollHandler;
                keyboard = DesktopInterop.VirtualKeyboard;
            }

            if (scrollHandler != null && keyboard != null)
            {
                // Universal smooth zoom standard: Ctrl + Scroll Wheel
                // 120 scroll tick units per step
                int scrollAmount = _direction == ZoomDirection.In ? 120 : -120;
                keyboard.Press("Control");
                scrollHandler.ScrollVertically(scrollAmount);
                if (scrollHandler is ISynchronousPointer syncPointer)
                    syncPointer.Flush();
                keyboard.Release("Control");
            }
            else if (keyboard != null)
            {
                // Fallback to Ctrl + Plus / Ctrl + Minus if scroll handler is unavailable
                keyboard.Press("Control");
                if (_direction == ZoomDirection.In)
                {
                    keyboard.Press("Equal");
                    keyboard.Release("Equal");
                }
                else
                {
                    keyboard.Press("Minus");
                    keyboard.Release("Minus");
                }
                keyboard.Release("Control");
            }
        }

        private void OnTimerElapsed()
        {
            if (Timer == null)
                return;

            if (!_isRepeating)
            {
                _isRepeating = true;
                Timer.Interval = RepeatInterval;
            }

            PerformZoom();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Elapsed -= OnTimerElapsed;
                    _timer.Dispose();
                    _timer = null;
                }
                GestureHandler?.EndGesture();
            }
        }
    }

    [PluginName("Zoom In")]
    public class ZoomInBinding : ZoomBinding
    {
        public ZoomInBinding()
        {
            base.Direction = "In";
        }

        [PluginIgnore]
        public override string Direction
        {
            get => base.Direction;
            set => base.Direction = "In";
        }
    }

    [PluginName("Zoom Out")]
    public class ZoomOutBinding : ZoomBinding
    {
        public ZoomOutBinding()
        {
            base.Direction = "Out";
        }

        [PluginIgnore]
        public override string Direction
        {
            get => base.Direction;
            set => base.Direction = "Out";
        }
    }
}
