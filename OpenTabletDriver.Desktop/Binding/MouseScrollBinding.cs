using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timers;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class MouseScrollBinding : IStateBinding, IDisposable
    {
        private const string PLUGIN_NAME = "Mouse Scroll Binding";

        private ITimer? _timer;
        private ScrollDirection _direction = ScrollDirection.Vertical;
        private int _interval = 40;
        private int _initialDelay = 200;
        private bool _isRepeating;
        private readonly object _timerLock = new object();

        [Resolved]
        public IMouseScrollHandler? Pointer { set; get; }

        [Resolved]
        public ITimer? Timer
        {
            get => _timer;
            set
            {
                lock (_timerLock)
                {
                    if (_timer != null)
                        _timer.Elapsed -= OnTimerElapsed;

                    _timer = value;

                    if (_timer != null)
                    {
                        _timer.Interval = _interval;
                        _timer.Elapsed += OnTimerElapsed;
                    }
                }
            }
        }

        [OnDependencyLoad]
        public void VerifyInitialization()
        {
            if (Pointer == null)
            {
                Log.Write(PLUGIN_NAME,
                    $"{nameof(IMouseScrollHandler)} unavailable. Your selected output mode is incompatible",
                    LogLevel.Error);
            }
        }

        [Property("Direction"), DefaultPropertyValue("Vertical"), PropertyValidated(nameof(ValidDirections))]
        public string Direction
        {
            get => _direction.ToString();
            set
            {
                if (Enum.TryParse(value, true, out ScrollDirection direction))
                {
                    _direction = direction;
                }
                else
                {
                    Log.Write(PLUGIN_NAME, $"Invalid scroll direction '{value}', defaulting to 'Vertical'", LogLevel.Warning);
                }
            }
        }

        [BooleanProperty("Invert", "Scroll Direction")]
        public bool Invert
        {
            get;
            set;
        }

        private int _amount = 120;

        [Property("Amount"),
         DefaultPropertyValue(120),
         ToolTip("The amount to scroll per step/tick. Standard wheel tick is 120 (use 60 for fine/half-step, 240 for double speed).")]
        public int Amount
        {
            get => _amount;
            set => _amount = value != 0 ? value : 1;
        }

        [Property("Initial Delay"),
         DefaultPropertyValue(200),
         Unit("ms"),
         ToolTip("The delay in milliseconds before continuous scrolling starts when the button is held.")]
        public int InitialDelay
        {
            get => _initialDelay;
            set => _initialDelay = Math.Max(0, value);
        }

        [Property("Interval"),
         DefaultPropertyValue(40),
         Unit("ms"),
         ToolTip("The repeat interval in milliseconds while holding the button.")]
        public int Interval
        {
            get => _interval;
            set
            {
                _interval = Math.Max(1, value);
                lock (_timerLock)
                {
                    if (_timer != null && _isRepeating)
                        _timer.Interval = _interval;
                }
            }
        }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            // First tick fires immediately (Wacom parity: instant response on dials and click)
            Scroll();

            // Set up auto-repeat if held
            lock (_timerLock)
            {
                if (_timer != null)
                {
                    _isRepeating = false;
                    _timer.Stop();
                    _timer.Interval = _initialDelay > 0 ? _initialDelay : _interval;
                    _timer.Start();
                }
            }
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            lock (_timerLock)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _isRepeating = false;
                }
            }
        }

        private void OnTimerElapsed()
        {
            lock (_timerLock)
            {
                if (_timer == null || !_timer.Enabled)
                    return;

                if (!_isRepeating)
                {
                    _isRepeating = true;
                    _timer.Interval = _interval;
                }
            }

            Scroll();
        }

        public void Scroll()
        {
            int baseAmount = Math.Abs(Amount);
            if (baseAmount == 0)
                baseAmount = 120;

            switch (_direction)
            {
                case ScrollDirection.Up:
                    int upAmount = Invert ? -baseAmount : baseAmount;
                    Pointer?.ScrollVertically(upAmount);
                    break;

                case ScrollDirection.Down:
                    int downAmount = Invert ? baseAmount : -baseAmount;
                    Pointer?.ScrollVertically(downAmount);
                    break;

                case ScrollDirection.Right:
                    int rightAmount = Invert ? -baseAmount : baseAmount;
                    Pointer?.ScrollHorizontally(rightAmount);
                    break;

                case ScrollDirection.Left:
                    int leftAmount = Invert ? baseAmount : -baseAmount;
                    Pointer?.ScrollHorizontally(leftAmount);
                    break;

                case ScrollDirection.Vertical:
                default:
                    // Legacy OpenTabletDriver behavior: Invert ? Amount : Amount * -1
                    int legacyVAmount = Invert ? Amount : Amount * -1;
                    Pointer?.ScrollVertically(legacyVAmount);
                    break;

                case ScrollDirection.Horizontal:
                    // Legacy OpenTabletDriver behavior: Invert ? Amount : Amount * -1
                    int legacyHAmount = Invert ? Amount : Amount * -1;
                    Pointer?.ScrollHorizontally(legacyHAmount);
                    break;
            }

            if (Pointer is ISynchronousPointer synchronousPointer)
                synchronousPointer.Flush();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool _isDisposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                lock (_timerLock)
                {
                    if (_timer != null)
                    {
                        _timer.Stop();
                        _timer.Elapsed -= OnTimerElapsed;
                        _timer.Dispose();
                        _timer = null;
                    }
                }
            }

            _isDisposed = true;
        }

        private static IEnumerable<string>? validDirections;
        public static IEnumerable<string> ValidDirections =>
            validDirections ??= Enum.GetValues<ScrollDirection>().Select(Enum.GetName)!;

        public override string ToString() => $"{PLUGIN_NAME}: Direction: {Direction}, Amount: {Amount}, Interval: {Interval}ms";
    }

    [PluginName("Scroll Up")]
    public class ScrollUpBinding : MouseScrollBinding
    {
        public ScrollUpBinding()
        {
            Direction = nameof(ScrollDirection.Up);
        }

        public override string ToString() => $"Scroll Up ({Amount})";
    }

    [PluginName("Scroll Down")]
    public class ScrollDownBinding : MouseScrollBinding
    {
        public ScrollDownBinding()
        {
            Direction = nameof(ScrollDirection.Down);
        }

        public override string ToString() => $"Scroll Down ({Amount})";
    }

    [PluginName("Scroll Left")]
    public class ScrollLeftBinding : MouseScrollBinding
    {
        public ScrollLeftBinding()
        {
            Direction = nameof(ScrollDirection.Left);
        }

        public override string ToString() => $"Scroll Left ({Amount})";
    }

    [PluginName("Scroll Right")]
    public class ScrollRightBinding : MouseScrollBinding
    {
        public ScrollRightBinding()
        {
            Direction = nameof(ScrollDirection.Right);
        }

        public override string ToString() => $"Scroll Right ({Amount})";
    }

    public enum ScrollDirection
    {
        Vertical = 0,
        Horizontal = 1,
        Up = 2,
        Down = 3,
        Left = 4,
        Right = 5
    }
}
