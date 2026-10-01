using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using OpenTabletDriver.Native.Linux;
using OpenTabletDriver.Native.Linux.Evdev;
using OpenTabletDriver.Native.Linux.Evdev.Structs;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input.Gesture
{
    public class EvdevVirtualTouchpad : IGestureHandler, IDisposable
    {
        private const int MAX_X = 2000;
        private const int MAX_Y = 1500;
        private const int CENTER_X = 1000;
        private const int CENTER_Y = 750;
        private const int BASE_HALF_DISTANCE = 100; // 200px initial finger distance

        private readonly EvdevDevice _device;
        private readonly Lock _lock = new();
        private System.Threading.Timer? _inactivityTimer;
        private bool _isActive;
        private float _currentScale = 1.0f;
        private int _trackingId = 1;
        private bool _isDisposed;

        [SetsRequiredMembers]
        public unsafe EvdevVirtualTouchpad()
        {
            _device = new EvdevDevice("OpenTabletDriver Virtual Touchpad");

            _device.EnableProperty(InputProperty.INPUT_PROP_POINTER);
            _device.EnableProperty(InputProperty.INPUT_PROP_BUTTONPAD);

            _device.EnableType(EventType.EV_KEY);
            _device.EnableCodes(EventType.EV_KEY,
                EventCode.BTN_LEFT,
                EventCode.BTN_TOUCH,
                EventCode.BTN_TOOL_FINGER,
                EventCode.BTN_TOOL_DOUBLETAP
            );

            _device.EnableType(EventType.EV_ABS);

            SetupAbs(EventCode.ABS_X, 0, MAX_X, 20);
            SetupAbs(EventCode.ABS_Y, 0, MAX_Y, 20);
            SetupAbs(EventCode.ABS_MT_SLOT, 0, 4, 0);
            SetupAbs(EventCode.ABS_MT_POSITION_X, 0, MAX_X, 20);
            SetupAbs(EventCode.ABS_MT_POSITION_Y, 0, MAX_Y, 20);
            SetupAbs(EventCode.ABS_MT_TRACKING_ID, 0, 65535, 0);

            var result = _device.Initialize();
            switch (result)
            {
                case ERRNO.NONE:
                    Log.Debug("Evdev", $"Successfully initialized virtual touchpad gesture device. (code {result})");
                    break;
                default:
                    Log.WriteNotify("Evdev", $"Failed to initialize virtual touchpad gesture device. (error code {result})", LogLevel.Error);
                    break;
            }
        }

        private unsafe void SetupAbs(EventCode code, int min, int max, int res)
        {
            var absinfo = new input_absinfo
            {
                value = 0,
                minimum = min,
                maximum = max,
                fuzz = 0,
                flat = 0,
                resolution = res
            };
            input_absinfo* ptr = &absinfo;
            _device.EnableCustomCode(EventType.EV_ABS, code, (IntPtr)ptr);
        }

        public void Zoom(float delta)
        {
            if (!_device.CanWrite)
                return;

            lock (_lock)
            {
                if (_isDisposed)
                    return;

                if (!_isActive)
                {
                    _isActive = true;
                    _currentScale = 1.0f;
                    _trackingId = (_trackingId + 2) % 60000;
                    if (_trackingId == 0) _trackingId = 1;

                    // Slot 0 (Finger 1)
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_SLOT, 0);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_TRACKING_ID, _trackingId);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_X, CENTER_X - BASE_HALF_DISTANCE);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_Y, CENTER_Y);

                    // Slot 1 (Finger 2)
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_SLOT, 1);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_TRACKING_ID, _trackingId + 1);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_X, CENTER_X + BASE_HALF_DISTANCE);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_Y, CENTER_Y);

                    _device.Write(EventType.EV_KEY, EventCode.BTN_TOUCH, 1);
                    _device.Write(EventType.EV_KEY, EventCode.BTN_TOOL_DOUBLETAP, 1);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_X, CENTER_X);
                    _device.Write(EventType.EV_ABS, EventCode.ABS_Y, CENTER_Y);
                    _device.Sync();
                }

                _currentScale = Math.Clamp(_currentScale + delta, 0.25f, 4.0f);
                int currentHalfDistance = (int)(BASE_HALF_DISTANCE * _currentScale);

                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_SLOT, 0);
                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_X, Math.Clamp(CENTER_X - currentHalfDistance, 10, MAX_X - 10));
                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_Y, CENTER_Y);

                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_SLOT, 1);
                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_X, Math.Clamp(CENTER_X + currentHalfDistance, 10, MAX_X - 10));
                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_POSITION_Y, CENTER_Y);
                _device.Sync();

                _inactivityTimer?.Dispose();
                _inactivityTimer = new System.Threading.Timer(_ => EndGestureInternal(), null, 140, Timeout.Infinite);
            }
        }

        public void EndGesture()
        {
            lock (_lock)
            {
                _inactivityTimer?.Dispose();
                _inactivityTimer = null;
                EndGestureInternal();
            }
        }

        private void EndGestureInternal()
        {
            lock (_lock)
            {
                if (!_isActive || _isDisposed || !_device.CanWrite)
                    return;

                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_SLOT, 0);
                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_TRACKING_ID, -1);
                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_SLOT, 1);
                _device.Write(EventType.EV_ABS, EventCode.ABS_MT_TRACKING_ID, -1);
                _device.Write(EventType.EV_KEY, EventCode.BTN_TOUCH, 0);
                _device.Write(EventType.EV_KEY, EventCode.BTN_TOOL_DOUBLETAP, 0);
                _device.Sync();

                _isActive = false;
                _currentScale = 1.0f;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                EndGestureInternal();
                _inactivityTimer?.Dispose();
                _inactivityTimer = null;
                _device.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
