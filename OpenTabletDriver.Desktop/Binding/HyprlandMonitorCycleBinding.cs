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

                var monitors = GetMonitors().ToArray();
                if (monitors.Length == 0)
                {
                    Log.Write(PLUGIN_NAME, "No monitors were found to cycle through.", LogLevel.Warning);
                    return;
                }

                var settings = Daemon.GetSettings().GetAwaiter().GetResult();
                var profile = settings.Profiles.GetProfile(tablet);
                var display = profile.AbsoluteModeSettings.Display;
                var currentIndex = FindCurrentMonitorIndex(monitors, display);
                var nextIndex = GetNextIndex(currentIndex, monitors.Length);
                var nextMonitor = monitors[nextIndex];

                profile.AbsoluteModeSettings.Display = ToAreaSettings(nextMonitor);
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

        private MonitorArea[] GetMonitors()
        {
            var hyprlandMonitors = GetHyprlandMonitors();
            if (hyprlandMonitors.Length > 0)
                return hyprlandMonitors;

            return GetVirtualScreenMonitors();
        }

        private static MonitorArea[] GetHyprlandMonitors()
        {
            string output;

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo("hyprctl")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                process.StartInfo.ArgumentList.Add("monitors");
                process.StartInfo.ArgumentList.Add("-j");

                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(1000))
                {
                    process.Kill();
                    Log.Write(PLUGIN_NAME, "Timed out while asking hyprctl for monitors; falling back to desktop monitor discovery.", LogLevel.Warning);
                    return [];
                }

                output = outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                {
                    Log.Write(PLUGIN_NAME, $"hyprctl monitor discovery failed: {error.Trim()}", LogLevel.Debug);
                    return [];
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Write(PLUGIN_NAME, $"hyprctl monitor discovery unavailable: {ex.Message}", LogLevel.Debug);
                return [];
            }

            return ParseHyprlandMonitors(output);
        }

        private static MonitorArea[] ParseHyprlandMonitors(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return [];

            var monitors = JArray.Parse(output);
            return monitors
                .Where(m => !(m.Value<bool?>("disabled") ?? false))
                .Select((m, i) =>
                {
                    var width = m.Value<float?>("width") ?? 0;
                    var height = m.Value<float?>("height") ?? 0;
                    var scale = m.Value<float?>("scale") ?? 1.0f;
                    if (scale <= 0) scale = 1.0f;
                    var transform = m.Value<int?>("transform") ?? 0;

                    // If rotated, swap physical width/height
                    var isRotated = transform % 2 != 0;
                    var actualWidth = isRotated ? height : width;
                    var actualHeight = isRotated ? width : height;

                    return new MonitorArea(
                        m.Value<string>("name") ?? $"Monitor {i + 1}",
                        m.Value<float?>("x") ?? 0,
                        m.Value<float?>("y") ?? 0,
                        actualWidth / scale,
                        actualHeight / scale,
                        i + 1);
                })
                .Where(m => m.Width > 0 && m.Height > 0)
                .OrderBy(m => m.Index)
                .ToArray();
        }

        private MonitorArea[] GetVirtualScreenMonitors()
        {
            if (VirtualScreen == null)
                return [];

            return VirtualScreen.Displays
                .Where(d => d.Index != 0 && d.Width > 0 && d.Height > 0)
                .Select(d => new MonitorArea($"Display {d.Index}", d.Position.X, d.Position.Y, d.Width, d.Height, d.Index))
                .OrderBy(m => m.Index)
                .ToArray();
        }

        private static int FindCurrentMonitorIndex(IReadOnlyList<MonitorArea> monitors, AreaSettings display)
        {
            var center = new Vector2(display.X, display.Y);
            var containingIndex = monitors
                .Select((m, i) => (Monitor: m, Index: i))
                .FirstOrDefault(x => x.Monitor.Contains(center));

            if (containingIndex.Monitor != null)
                return containingIndex.Index;

            return monitors
                .Select((m, i) => (Index: i, Distance: Vector2.DistanceSquared(center, m.Center)))
                .OrderBy(x => x.Distance)
                .First()
                .Index;
        }

        private static AreaSettings ToAreaSettings(MonitorArea monitor)
        {
            return new AreaSettings
            {
                Width = monitor.Width,
                Height = monitor.Height,
                X = monitor.X + monitor.Width / 2,
                Y = monitor.Y + monitor.Height / 2,
                Rotation = 0
            };
        }

        public override string ToString() => $"{PLUGIN_NAME}: {Direction}";

        private enum CycleDirection
        {
            Next,
            Previous
        }

        private sealed class MonitorArea
        {
            public MonitorArea(string name, float x, float y, float width, float height, int index)
            {
                Name = name;
                X = x;
                Y = y;
                Width = width;
                Height = height;
                Index = index;
            }

            public string Name { get; }
            public float X { get; }
            public float Y { get; }
            public float Width { get; }
            public float Height { get; }
            public int Index { get; }
            public Vector2 Center => new(X + Width / 2, Y + Height / 2);

            public bool Contains(Vector2 point)
            {
                return point.X >= X &&
                       point.X < X + Width &&
                       point.Y >= Y &&
                       point.Y < Y + Height;
            }

            public override string ToString() => $"{Name} ({Width}x{Height}@<{Center.X}, {Center.Y}>)";
        }
    }
}
