using System;
using System.Diagnostics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    public class DeepPressBindingState : BindingState
    {
        public float ActivationThreshold { get; set; } = 80.0f;
        public float ReleaseHysteresis { get; set; } = 8.0f;
        public float HoldDelayMs { get; set; } = 60.0f;
        public bool SuppressStroke { get; set; } = true;

        public bool IsDeepPressed { get; private set; }
        public bool IsPending { get; private set; }
        public bool LockTipUntilLift { get; private set; }
        public long DeepPressActivatedTimestamp => _deepPressActivatedTimestamp;

        private long _pendingStartTimestamp;
        private long _deepPressActivatedTimestamp;
        private bool _wasDrawingBeforeThreshold;

        /// <summary>
        /// Duration in milliseconds to momentarily pulse tip touch to 0 upon entering deep press,
        /// ending any in-progress drawing stroke and allowing the target application to switch tools.
        /// </summary>
        public const double ToolSwitchPulseDurationMs = 25.0;

        /// <summary>
        /// Determines whether this binding represents a mouse click (such as Right Click context menu)
        /// or a pointer suppressor (e.g. Floating HUD) where the tip should stay suppressed for the whole press.
        /// If false (e.g. key sequence, tool shortcut, eraser), the tip touch re-engages after tool switch pulse.
        /// </summary>
        public bool ShouldHoldTipSuppressed()
        {
            if (Binding is MouseBinding mouseBinding)
            {
                // Right click or middle click menus should not have tip clicking underneath them
                return true;
            }

            if (Binding is IPointerSuppressor)
            {
                return true;
            }

            return false;
        }

        public bool ProcessReport(TabletReference tablet, IDeviceReport report, float pressurePercent, out bool suppressTip)
        {
            suppressTip = false;

            if (Binding == null)
                return false;

            bool isTipDown = pressurePercent > 0;

            if (!isTipDown)
            {
                Reset(tablet, report);
                return false;
            }

            // Once deep press is triggered during a touch
            if (LockTipUntilLift)
            {
                if (SuppressStroke)
                {
                    if (ShouldHoldTipSuppressed())
                    {
                        suppressTip = true;
                    }
                    else
                    {
                        // For tool shortcuts / keybinds (e.g. Eraser):
                        // Momentarily suppress tip for ToolSwitchPulseDurationMs to cancel the drawing stroke,
                        // then re-engage tip touch so the user can erase/draw continuously without lifting the pen!
                        long elapsedSinceActivation = Stopwatch.GetTimestamp() - _deepPressActivatedTimestamp;
                        double elapsedMs = (double)elapsedSinceActivation / Stopwatch.Frequency * 1000.0;
                        if (elapsedMs < ToolSwitchPulseDurationMs)
                        {
                            suppressTip = true;
                        }
                    }
                }

                if (IsDeepPressed)
                {
                    float releaseThreshold = Math.Max(0.0f, ActivationThreshold - ReleaseHysteresis);
                    if (pressurePercent < releaseThreshold)
                    {
                        IsDeepPressed = false;
                        base.Invoke(tablet, report, false);
                    }
                    else
                    {
                        base.Invoke(tablet, report, true);
                    }
                }

                return IsDeepPressed;
            }

            if (pressurePercent >= ActivationThreshold)
            {
                if (!IsPending && !IsDeepPressed)
                {
                    IsPending = true;
                    _pendingStartTimestamp = Stopwatch.GetTimestamp();
                }

                if (IsPending)
                {
                    long elapsed = Stopwatch.GetTimestamp() - _pendingStartTimestamp;
                    double elapsedMs = (double)elapsed / Stopwatch.Frequency * 1000.0;

                    if (elapsedMs >= HoldDelayMs)
                    {
                        IsPending = false;
                        IsDeepPressed = true;
                        LockTipUntilLift = true;
                        _deepPressActivatedTimestamp = Stopwatch.GetTimestamp();

                        if (SuppressStroke)
                            suppressTip = true;

                        base.Invoke(tablet, report, true);
                    }
                    else
                    {
                        // Pending hold delay: if this touch started directly at high pressure,
                        // suppress tip during debounce delay so no ink mark is made
                        if (!_wasDrawingBeforeThreshold && SuppressStroke)
                            suppressTip = true;
                    }
                }
                else if (IsDeepPressed)
                {
                    if (SuppressStroke)
                        suppressTip = true;

                    base.Invoke(tablet, report, true);
                }
            }
            else
            {
                if (IsPending)
                {
                    IsPending = false;
                }

                _wasDrawingBeforeThreshold = true;
            }

            return IsDeepPressed;
        }

        public void Reset(TabletReference tablet, IDeviceReport report)
        {
            if (IsDeepPressed)
            {
                IsDeepPressed = false;
                base.Invoke(tablet, report, false);
            }

            IsPending = false;
            LockTipUntilLift = false;
            _deepPressActivatedTimestamp = 0;
            _wasDrawingBeforeThreshold = false;
        }
    }
}
