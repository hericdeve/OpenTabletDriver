using System;
using System.Collections.Generic;
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

        private static readonly string[] _validDirections = { "In", "Out" };
        public static IEnumerable<string> ValidDirections => _validDirections;

        [Property("Direction"), DefaultPropertyValue("In"), PropertyValidated(nameof(ValidDirections))]
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
            if (GestureHandler != null)
            {
                float signedDelta = _direction == ZoomDirection.In ? Amount : -Amount;
                GestureHandler.Zoom(signedDelta);
                return;
            }

            var keyboard = Keyboard ?? DesktopInterop.VirtualKeyboard;
            if (keyboard != null)
            {
                // Universal zoom shortcut: Ctrl + Equal (Zoom In) / Ctrl + Minus (Zoom Out)
                // This reliably triggers zoom in all applications (browsers, Xournal++, office suites, document viewers)
                // without relying on scroll wheel events that can get misidentified as plain scrolls due to cross-device race conditions.
                keyboard.Press("Control");
                string key = _direction == ZoomDirection.In ? "Equal" : "Minus";
                keyboard.Press(key);
                keyboard.Release(key);
                keyboard.Release("Control");
                return;
            }

            // Fallback: If keyboard is not available, try mouse scroll wheel with Ctrl
            var scrollHandler = Pointer ?? DesktopInterop.RelativePointer as IMouseScrollHandler;
            if (scrollHandler != null)
            {
                int scrollAmount = _direction == ZoomDirection.In ? 120 : -120;
                scrollHandler.ScrollVertically(scrollAmount);
                if (scrollHandler is ISynchronousPointer syncPointer)
                    syncPointer.Flush();
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
