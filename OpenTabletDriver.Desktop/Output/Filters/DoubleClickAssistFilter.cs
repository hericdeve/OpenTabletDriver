using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Output.Filters
{
    [PluginName("Double-Click Assist")]
    public class DoubleClickAssistFilter : IPositionedPipelineElement<IDeviceReport>
    {
        public DoubleClickAssistFilter()
        {
        }

        public DoubleClickAssistFilter(TabletReference tablet)
        {
        }

        private enum AssistState
        {
            Idle,
            FirstTapDown,
            WaitingForSecondTap,
            AssistingDoubleTap,
            DragUnlocked
        }

        private AssistState _state = AssistState.Idle;
        private Vector2 _tap1ContactPos;
        private long _tap1DownTimestamp;
        private long _tap1ReleaseTimestamp;
        private bool _wasTipDown;

        [SliderProperty("Double-Click Distance", 2f, 30f, 8f)]
        [Unit("px")]
        [ToolTip("Maximum screen distance in pixels between tap 1 and tap 2 to snap coordinates for double click.")]
        public float Distance { get; set; } = 8f;

        [SliderProperty("Double-Click Time Window", 150f, 1000f, 450f)]
        [Unit("ms")]
        [ToolTip("Maximum time in milliseconds between release of tap 1 and contact of tap 2.")]
        public float MaxTimeMs { get; set; } = 450f;

        [SliderProperty("Max Tap Duration", 50f, 600f, 300f)]
        [Unit("ms")]
        [ToolTip("Maximum contact duration in milliseconds of tap 1 to qualify as a tap rather than a stroke.")]
        public float MaxTapLengthMs { get; set; } = 300f;

        [SliderProperty("Drag Unlock Distance", 5f, 50f, 14f)]
        [Unit("px")]
        [ToolTip("Distance moved in pixels during second tap hold to unlock snapping and allow dragging.")]
        public float DragUnlockDistance { get; set; } = 14f;

        [BooleanProperty("Assist Eraser", "Apply double-click assist to the eraser as well")]
        public bool EnableEraser { get; set; } = false;

        public PipelinePosition Position => PipelinePosition.PostTransform;

        public event Action<IDeviceReport?>? Emit;

        public void Consume(IDeviceReport? report)
        {
            if (report != null)
            {
                ProcessReport(report);
            }

            Emit?.Invoke(report);
        }

        private void ProcessReport(IDeviceReport report)
        {
            if (report is OutOfRangeReport)
            {
                Reset();
                return;
            }

            if (report is not (ITabletReport tabletReport and IAbsolutePositionReport absReport))
                return;

            if (report is IEraserReport eraserReport && eraserReport.Eraser && !EnableEraser)
            {
                Reset();
                return;
            }

            bool nowTipDown = tabletReport.Pressure > 0;
            long now = Stopwatch.GetTimestamp();

            if (nowTipDown && !_wasTipDown)
            {
                // Tip pressed down
                if (_state == AssistState.WaitingForSecondTap)
                {
                    double waitTime = ElapsedMs(_tap1ReleaseTimestamp, now);
                    if (waitTime <= MaxTimeMs)
                    {
                        float dist = Vector2.Distance(absReport.Position, _tap1ContactPos);
                        if (dist <= Distance)
                        {
                            // Qualifies as a double-click tap: snap coordinates
                            _state = AssistState.AssistingDoubleTap;
                            absReport.Position = _tap1ContactPos;
                        }
                        else
                        {
                            // Too far away: treat as fresh tap 1
                            _state = AssistState.FirstTapDown;
                            _tap1ContactPos = absReport.Position;
                            _tap1DownTimestamp = now;
                        }
                    }
                    else
                    {
                        // Window expired: treat as fresh tap 1
                        _state = AssistState.FirstTapDown;
                        _tap1ContactPos = absReport.Position;
                        _tap1DownTimestamp = now;
                    }
                }
                else
                {
                    // Fresh tap 1
                    _state = AssistState.FirstTapDown;
                    _tap1ContactPos = absReport.Position;
                    _tap1DownTimestamp = now;
                }
            }
            else if (nowTipDown && _wasTipDown)
            {
                // Tip held down
                if (_state == AssistState.AssistingDoubleTap)
                {
                    float dist = Vector2.Distance(absReport.Position, _tap1ContactPos);
                    if (dist > DragUnlockDistance)
                    {
                        // Intentional drag gesture: unlock snap
                        _state = AssistState.DragUnlocked;
                    }
                    else
                    {
                        // Keep snapped to anchor position
                        absReport.Position = _tap1ContactPos;
                    }
                }
                else if (_state == AssistState.FirstTapDown)
                {
                    // If tap 1 is held down too long, it's a drag or drawing stroke, not a tap
                    if (ElapsedMs(_tap1DownTimestamp, now) > MaxTapLengthMs)
                        _state = AssistState.DragUnlocked;
                }
            }
            else if (!nowTipDown && _wasTipDown)
            {
                // Tip released
                if (_state == AssistState.FirstTapDown)
                {
                    double tapDuration = ElapsedMs(_tap1DownTimestamp, now);
                    if (tapDuration <= MaxTapLengthMs)
                    {
                        _state = AssistState.WaitingForSecondTap;
                        _tap1ReleaseTimestamp = now;
                    }
                    else
                    {
                        _state = AssistState.Idle;
                    }
                }
                else if (_state is AssistState.AssistingDoubleTap or AssistState.DragUnlocked)
                {
                    _state = AssistState.Idle;
                }
            }
            else
            {
                // Hovering (tip up)
                if (_state == AssistState.WaitingForSecondTap)
                {
                    if (ElapsedMs(_tap1ReleaseTimestamp, now) > MaxTimeMs)
                        _state = AssistState.Idle;
                }
            }

            _wasTipDown = nowTipDown;
        }

        public void Reset()
        {
            _state = AssistState.Idle;
            _wasTipDown = false;
        }

        private static double ElapsedMs(long start, long end) =>
            (end - start) * 1000.0 / Stopwatch.Frequency;
    }
}
