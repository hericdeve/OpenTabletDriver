using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName("Floating HUD")]
    public class FloatingHudBinding : IContinuousBinding, IPointerSuppressor
    {
        private bool _isActive;
        private long _pressTimestamp;
        private Vector2 _anchorPosition;
        private Vector2 _currentPosition;
        private HudConfiguration _cachedConfig = HudConfiguration.GetDefaults();

        [Resolved]
        public IDriverDaemon? Daemon { get; set; }

        [BooleanProperty("Keep Cursor Anchored", "Anchor pointer at gesture start during flick gestures")]
        public bool KeepCursorAnchored { get; set; } = true;

        [SliderProperty("Flick Threshold", 80f, 400f, 160f)]
        [Unit("ms")]
        [ToolTip("Maximum hold time to distinguish a quick tap from a hold-and-flick gesture.")]
        public float FlickThresholdMs { get; set; } = 160f;

        public bool IsActive => _isActive;
        public bool SuppressMotion => KeepCursorAnchored && _isActive;
        public bool SuppressTip => _isActive;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (report is not IAbsolutePositionReport absReport)
                return;

            _anchorPosition = absReport.Position;
            _currentPosition = absReport.Position;
            _pressTimestamp = Stopwatch.GetTimestamp();
            _isActive = true;

            // Retrieve current HUD config asynchronously or fall back to cached defaults
            if (Daemon != null)
            {
                _ = Daemon.GetSettings().ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && t.Result?.Hud != null)
                        _cachedConfig = t.Result.Hud;

                    _ = Daemon.TriggerHudShow(new HudShowRequest
                    {
                        CursorPosition = _anchorPosition,
                        Configuration = _cachedConfig
                    });
                });
            }
        }

        public void Update(TabletReference tablet, IDeviceReport report)
        {
            if (!_isActive || report is not IAbsolutePositionReport absReport)
                return;

            _currentPosition = absReport.Position;
            _ = Daemon?.TriggerHudUpdate(new HudUpdateRequest
            {
                CursorPosition = _currentPosition
            });
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            if (!_isActive)
                return;

            _isActive = false;
            double elapsedMs = (Stopwatch.GetTimestamp() - _pressTimestamp) * 1000.0 / Stopwatch.Frequency;

            if (elapsedMs > FlickThresholdMs)
            {
                // Hold & Flick gesture: compute active slice and fire action
                ProcessFlickSelection();
                _ = Daemon?.TriggerHudDismiss();
            }
            else
            {
                // Quick Tap: HUD remains open in Tap & Browse mode
                // No action triggered on release; waiting for user click/tap on wedge
            }
        }

        private void ProcessFlickSelection()
        {
            if (_cachedConfig.Items.Count == 0 || Daemon == null)
                return;

            Vector2 delta = _currentPosition - _anchorPosition;
            float distance = delta.Length();

            if (distance < _cachedConfig.DeadzoneRadius)
            {
                // Released inside deadzone: cancel gesture
                return;
            }

            int itemCount = _cachedConfig.Items.Count;
            float sliceAngle = 360f / itemCount;

            // Calculate angle in degrees: 0 deg = Up (12 o'clock), clockwise
            double rad = Math.Atan2(delta.Y, delta.X);
            double deg = (rad * 180.0 / Math.PI) + 90.0;
            if (deg < 0)
                deg += 360.0;

            int selectedIndex = (int)Math.Floor((deg + (sliceAngle / 2.0)) / sliceAngle) % itemCount;

            if (selectedIndex >= 0 && selectedIndex < itemCount)
            {
                var item = _cachedConfig.Items[selectedIndex];
                _ = Daemon.ExecuteHudAction(item.Action);
            }
        }
    }
}
