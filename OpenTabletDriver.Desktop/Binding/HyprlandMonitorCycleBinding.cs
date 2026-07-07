using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using OpenTabletDriver.Desktop.Interop.Display;

#nullable enable

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME), SupportedPlatform(PluginPlatform.Linux)]
    public class HyprlandMonitorCycleBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Hyprland Monitor Cycle Binding";
        private const int TIMEOUT = 50;
        private readonly static HPETDeltaStopwatch _stopwatch = new();

        private CycleDirection _direction = CycleDirection.Next;

        [Resolved]
        public IDriverDaemon? Daemon { set; get; }

        [Resolved]
        public IVirtualScreen? VirtualScreen { set; get; }

        [Property("Direction"), DefaultPropertyValue("Next"), PropertyValidated(nameof(ValidDirections))]
        public string Direction
        {
            get => _direction.ToString();
            set
            {
                if (Enum.TryParse(value, out CycleDirection direction))
                    _direction = direction;
                else
                    Log.Write(PLUGIN_NAME, $"Invalid direction '{value}', defaulting to 'Next'.", LogLevel.Warning);
            }
        }

        public static IEnumerable<string> ValidDirections => Enum.GetNames<CycleDirection>();

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (_stopwatch.Elapsed.TotalMilliseconds <= TIMEOUT)
                return;

            try
            {
                if (Daemon == null)
                {
                    Log.Write(PLUGIN_NAME, "Unable to cycle monitors because the daemon is unavailable.", LogLevel.Error);
                    return;
                }

                var monitors = HyprlandDisplayInterop.GetMonitors(VirtualScreen);
                if (monitors.Length == 0)
                {
                    Log.Write(PLUGIN_NAME, "No monitors were found to cycle through.", LogLevel.Warning);
                    return;
                }

                var settings = Daemon.GetSettings().GetAwaiter().GetResult();
                var profile = settings.Profiles.GetProfile(tablet);
                var display = profile.AbsoluteModeSettings.Display;
                var currentIndex = HyprlandDisplayInterop.FindCurrentMonitorIndex(monitors, display);
                var nextIndex = GetNextIndex(currentIndex, monitors.Length);
                var nextMonitor = monitors[nextIndex];

                profile.AbsoluteModeSettings.Display = HyprlandDisplayInterop.ToAreaSettings(nextMonitor);
                Daemon.SetSettings(settings).GetAwaiter().GetResult();
                Daemon.ForceResynchronize().GetAwaiter().GetResult();

                Log.Write(PLUGIN_NAME, $"Cycled '{tablet.Properties.Name}' to monitor {nextMonitor}.");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, LogLevel.Error);
            }
            finally
            {
                _stopwatch.Restart();
            }
        }

        public void Release(TabletReference tablet, IDeviceReport report) { }

        private int GetNextIndex(int currentIndex, int monitorCount)
        {
            var offset = _direction == CycleDirection.Previous ? -1 : 1;
            return (currentIndex + offset + monitorCount) % monitorCount;
        }

        public override string ToString() => $"{PLUGIN_NAME}: {Direction}";

        private enum CycleDirection
        {
            Next,
            Previous
        }
    }
}
