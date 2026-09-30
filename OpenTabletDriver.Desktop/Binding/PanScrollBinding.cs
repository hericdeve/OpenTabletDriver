using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class PanScrollBinding : IContinuousBinding, IPointerSuppressor
    {
        private const string PLUGIN_NAME = "Pan / Scroll";
        private const float SCROLL_STEP_PIXELS = 15.0f;

        private PanDirection _direction = PanDirection.Both;
        private bool _isActive;
        private bool _deadzonePassed;
        private Vector2 _startPos;
        private Vector2 _previousPos;
        private float _accumulatedX;
        private float _accumulatedY;

        [Resolved]
        public IMouseScrollHandler? Pointer { set; get; }

        public bool IsActive => _isActive;

        public bool SuppressMotion => _isActive && KeepCursorAnchored;

        public bool SuppressTip => _isActive;

        [Property("Direction"), DefaultPropertyValue("Both"), PropertyValidated(nameof(ValidDirections))]
        public string Direction
        {
            get => _direction.ToString();
            set
            {
                if (Enum.TryParse(value, out PanDirection direction))
                    _direction = direction;
                else
                    Log.Write(PLUGIN_NAME, $"Invalid pan direction '{value}', defaulting to 'Both'.", LogLevel.Warning);
            }
        }

        [Property("Sensitivity"),
         DefaultPropertyValue(100),
         Unit("%"),
         ToolTip("The scroll speed sensitivity multiplier percentage.")]
        public int Sensitivity { get; set; } = 100;

        [Property("Deadzone"),
         DefaultPropertyValue(5),
         Unit("px"),
         ToolTip("Minimum movement in pixels required before pan scrolling activates.")]
        public int Deadzone { get; set; } = 5;

        [BooleanProperty("Invert Vertical", "Scroll Direction"),
         ToolTip("Inverts vertical scroll direction. Default is natural dragging (moving down scrolls content down).")]
        public bool InvertVertical { get; set; }

        [BooleanProperty("Invert Horizontal", "Scroll Direction"),
         ToolTip("Inverts horizontal scroll direction. Default is natural dragging (moving right scrolls content right).")]
        public bool InvertHorizontal { get; set; }

        [BooleanProperty("Keep Cursor Anchored", "Behavior"),
         ToolTip("Keeps the pointer locked at the starting position while panning, preventing cursor drift.")]
        public bool KeepCursorAnchored { get; set; } = true;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            _isActive = true;
            _deadzonePassed = false;
            _accumulatedX = 0;
            _accumulatedY = 0;

            if (report is IAbsolutePositionReport absReport)
            {
                _startPos = absReport.Position;
                _previousPos = absReport.Position;
            }
        }

        public void Update(TabletReference tablet, IDeviceReport report)
        {
            if (!_isActive || Pointer == null)
                return;

            if (report is not IAbsolutePositionReport absReport)
                return;

            var currentPos = absReport.Position;

            if (!_deadzonePassed)
            {
                float dist = Vector2.Distance(currentPos, _startPos);
                if (dist >= Deadzone)
                {
                    _deadzonePassed = true;
                    _previousPos = currentPos;
                }
                else
                {
                    return;
                }
            }

            float deltaX = currentPos.X - _previousPos.X;
            float deltaY = currentPos.Y - _previousPos.Y;
            _previousPos = currentPos;

            float scale = Math.Max(1, Sensitivity) / 100.0f;

            if (_direction is PanDirection.Both or PanDirection.Vertical)
            {
                float effectiveDeltaY = (InvertVertical ? -deltaY : deltaY) * scale;
                _accumulatedY += effectiveDeltaY;
            }

            if (_direction is PanDirection.Both or PanDirection.Horizontal)
            {
                float effectiveDeltaX = (InvertHorizontal ? -deltaX : deltaX) * scale;
                _accumulatedX += effectiveDeltaX;
            }

            bool scrolled = false;

            while (Math.Abs(_accumulatedY) >= SCROLL_STEP_PIXELS)
            {
                int sign = Math.Sign(_accumulatedY);
                Pointer.ScrollVertically(sign * 120);
                _accumulatedY -= sign * SCROLL_STEP_PIXELS;
                scrolled = true;
            }

            while (Math.Abs(_accumulatedX) >= SCROLL_STEP_PIXELS)
            {
                int sign = Math.Sign(_accumulatedX);
                Pointer.ScrollHorizontally(sign * 120);
                _accumulatedX -= sign * SCROLL_STEP_PIXELS;
                scrolled = true;
            }

            if (scrolled && Pointer is ISynchronousPointer sync)
            {
                sync.Flush();
            }
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            _isActive = false;
            _accumulatedX = 0;
            _accumulatedY = 0;
        }

        private static IEnumerable<string>? s_validDirections;
        public static IEnumerable<string> ValidDirections =>
            s_validDirections ??= Enum.GetNames<PanDirection>();

        public override string ToString() => $"{PLUGIN_NAME}: Direction: {Direction}, Sensitivity: {Sensitivity}%";
    }

    public enum PanDirection
    {
        Both,
        Vertical,
        Horizontal
    }
}
