using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin.Platform.Display;

#nullable enable

namespace OpenTabletDriver.Desktop.Interop.Display
{
    public static class HyprlandDisplayInterop
    {
        private const string PLUGIN_NAME = "Hyprland Display Interop";

        public static MonitorArea[] GetMonitors(IVirtualScreen? virtualScreen)
        {
            var hyprlandMonitors = GetHyprlandMonitors();
            if (hyprlandMonitors.Length > 0)
                return hyprlandMonitors;

            return GetVirtualScreenMonitors(virtualScreen);
        }

        public static MonitorArea? GetActiveMonitor(IVirtualScreen? virtualScreen)
        {
            var monitors = GetMonitors(virtualScreen);
            if (monitors.Length == 0)
                return null;

            var activeWindowOutput = RunHyprctl("activewindow");
            if (!string.IsNullOrWhiteSpace(activeWindowOutput))
            {
                try
                {
                    var activeWindow = JObject.Parse(activeWindowOutput);
                    var monitorId = activeWindow.Value<int?>("monitor");
                    if (monitorId != null)
                    {
                        var found = monitors.FirstOrDefault(m => m.Id == monitorId.Value);
                        if (found != null)
                            return found;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Write(PLUGIN_NAME, $"Failed to parse activewindow: {ex.Message}", Plugin.LogLevel.Debug);
                }
            }

            var focusedMonitor = monitors.FirstOrDefault(m => m.Focused);
            return focusedMonitor ?? monitors.FirstOrDefault();
        }

        private static string? RunHyprctl(params string[] args)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo("hyprctl")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                foreach (var arg in args)
                {
                    process.StartInfo.ArgumentList.Add(arg);
                }
                process.StartInfo.ArgumentList.Add("-j");

                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(1000))
                {
                    process.Kill();
                    Plugin.Log.Write(PLUGIN_NAME, $"Timed out while asking hyprctl {string.Join(" ", args)}", Plugin.LogLevel.Warning);
                    return null;
                }

                var output = outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                {
                    Plugin.Log.Write(PLUGIN_NAME, $"hyprctl {string.Join(" ", args)} failed: {error.Trim()}", Plugin.LogLevel.Debug);
                    return null;
                }

                return output;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Plugin.Log.Write(PLUGIN_NAME, $"hyprctl unavailable: {ex.Message}", Plugin.LogLevel.Debug);
                return null;
            }
        }

        private static MonitorArea[] GetHyprlandMonitors()
        {
            var output = RunHyprctl("monitors");
            if (string.IsNullOrWhiteSpace(output))
                return [];

            return ParseHyprlandMonitors(output);
        }

        private static MonitorArea[] ParseHyprlandMonitors(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return [];

            var monitors = JArray.Parse(output);
            var parsed = monitors
                .Where(m => !(m.Value<bool?>("disabled") ?? false))
                .Select((m, i) =>
                {
                    var width = m.Value<float?>("width") ?? 0;
                    var height = m.Value<float?>("height") ?? 0;
                    var scale = m.Value<float?>("scale") ?? 1.0f;
                    if (scale <= 0) scale = 1.0f;
                    var transform = m.Value<int?>("transform") ?? 0;

                    var isRotated = transform % 2 != 0;
                    var actualWidth = isRotated ? height : width;
                    var actualHeight = isRotated ? width : height;
                    var focused = m.Value<bool?>("focused") ?? false;

                    return new MonitorArea(
                        m.Value<int?>("id") ?? i,
                        m.Value<string>("name") ?? $"Monitor {i + 1}",
                        m.Value<float?>("x") ?? 0,
                        m.Value<float?>("y") ?? 0,
                        actualWidth / scale,
                        actualHeight / scale,
                        i + 1,
                        focused);
                })
                .Where(m => m.Width > 0 && m.Height > 0)
                .OrderBy(m => m.Index)
                .ToArray();

            // Hyprland reports positions in its own coordinate space which can
            // include negative values (e.g. laptop at x=-1536 when external
            // monitor sits at 0,0).  The evdev virtual pointer and VirtualScreen
            // use a coordinate space that starts at (0,0), so we need to shift
            // all monitors so the top-left corner of the bounding box is at the
            // origin.
            if (parsed.Length > 0)
            {
                var minX = parsed.Min(m => m.X);
                var minY = parsed.Min(m => m.Y);
                if (minX != 0 || minY != 0)
                {
                    parsed = parsed
                        .Select(m => new MonitorArea(m.Id, m.Name, m.X - minX, m.Y - minY, m.Width, m.Height, m.Index, m.Focused))
                        .ToArray();
                }
            }

            return parsed;
        }

        private static MonitorArea[] GetVirtualScreenMonitors(IVirtualScreen? virtualScreen)
        {
            if (virtualScreen == null)
                return [];

            return virtualScreen.Displays
                .Where(d => d.Index != 0 && d.Width > 0 && d.Height > 0)
                .Select(d => new MonitorArea(d.Index, $"Display {d.Index}", d.Position.X, d.Position.Y, d.Width, d.Height, d.Index, false))
                .OrderBy(m => m.Index)
                .ToArray();
        }

        public static int FindCurrentMonitorIndex(IReadOnlyList<MonitorArea> monitors, AreaSettings display)
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

        public static AreaSettings ToAreaSettings(MonitorArea monitor)
        {
            return new AreaSettings
            {
                Width = monitor.Width,
                Height = monitor.Height,
                X = monitor.Center.X,
                Y = monitor.Center.Y,
                Rotation = 0
            };
        }

        public static AreaSettings GetVirtualScreenArea(IReadOnlyList<MonitorArea> monitors)
        {
            if (monitors == null || monitors.Count == 0)
            {
                return new AreaSettings
                {
                    Width = 1920,
                    Height = 1080,
                    X = 960,
                    Y = 540,
                    Rotation = 0
                };
            }

            var minX = monitors.Min(m => m.X);
            var minY = monitors.Min(m => m.Y);
            var maxX = monitors.Max(m => m.X + m.Width);
            var maxY = monitors.Max(m => m.Y + m.Height);
            var width = Math.Max(0, maxX - minX);
            var height = Math.Max(0, maxY - minY);

            return new AreaSettings
            {
                Width = width,
                Height = height,
                X = minX + width / 2f,
                Y = minY + height / 2f,
                Rotation = 0
            };
        }
    }

    public sealed class MonitorArea
    {
        public MonitorArea(int id, string name, float x, float y, float width, float height, int index, bool focused = false)
        {
            Id = id;
            Name = name;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Index = index;
            Focused = focused;
        }

        public int Id { get; }
        public string Name { get; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public int Index { get; }
        public bool Focused { get; }
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
