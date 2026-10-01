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
        public bool KeepCursorAnchored { get; set; } = false;

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

            _ = Daemon?.TriggerHudShow(new HudShowRequest
            {
                CursorPosition = _anchorPosition,
                Configuration = null
            });
        }

        public void Update(TabletReference tablet, IDeviceReport report)
        {
            if (!_isActive)
                return;

            if (report is IAbsolutePositionReport absReport)
            {
                _currentPosition = absReport.Position;
            }

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

            if (report is IAbsolutePositionReport absReport)
            {
                _currentPosition = absReport.Position;
            }

            _ = Daemon?.ConfirmHudSelection(_currentPosition);
        }
    }
}
