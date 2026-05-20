using System;
using System.Diagnostics;
using System.Linq;
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
    public class HyprlandActiveWindowBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Hyprland Active Window Binding";
        private const int TIMEOUT = 50;
        private readonly static HPETDeltaStopwatch _stopwatch = new();

        private bool _mappedToWindow = false;

        [Resolved]
        public IDriverDaemon? Daemon { set; get; }

        [Resolved]
        public IVirtualScreen? VirtualScreen { set; get; }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (_stopwatch.Elapsed.TotalMilliseconds <= TIMEOUT)
                return;

            try
            {
                if (Daemon == null)
                {
                    Log.Write(PLUGIN_NAME, "Unable to perform mapping because the daemon is unavailable.", LogLevel.Error);
                    return;
                }

                var settings = Daemon.GetSettings().GetAwaiter().GetResult();
                var profile = settings.Profiles.GetProfile(tablet);

                if (!_mappedToWindow)
                {
                    var windowArea = GetActiveWindowArea();
                    if (windowArea == null)
                    {
                        Log.Write(PLUGIN_NAME, "No active window found or hyprctl failed.", LogLevel.Warning);
                        return;
                    }

                    // Map to window
                    profile.AbsoluteModeSettings.Display = new AreaSettings
                    {
                        Width = windowArea.Width,
                        Height = windowArea.Height,
                        X = windowArea.X + windowArea.Width / 2,
                        Y = windowArea.Y + windowArea.Height / 2,
                        Rotation = 0 // Standard rotation for windows
                    };

                    _mappedToWindow = true;
                    Log.Write(PLUGIN_NAME, $"Mapped '{tablet.Properties.Name}' to active window.");
                }
                else
                {
                    if (VirtualScreen == null)
                    {
                        Log.Write(PLUGIN_NAME, "Unable to restore full screen mapping because the virtual screen is unavailable.", LogLevel.Warning);
                        return;
                    }

                    var windowArea = GetActiveWindowArea();
                    if (windowArea != null && TryGetDisplayAreaForWindow(windowArea, VirtualScreen, out var displayArea))
                    {
                        profile.AbsoluteModeSettings.Display = displayArea;
                    }
                    else
                    {
                        profile.AbsoluteModeSettings.Display = AreaSettings.GetDefaults(VirtualScreen);
                    }

                    _mappedToWindow = false;
                    Log.Write(PLUGIN_NAME, $"Mapped '{tablet.Properties.Name}' back to full screen.");
                }

                Daemon.SetSettings(settings).GetAwaiter().GetResult();
                Daemon.ForceResynchronize().GetAwaiter().GetResult();
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

        private static WindowArea? GetActiveWindowArea()
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
                process.StartInfo.ArgumentList.Add("activewindow");
                process.StartInfo.ArgumentList.Add("-j");

                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                
                if (!process.WaitForExit(1000))
                {
                    process.Kill();
                    return null;
                }

                if (process.ExitCode != 0)
                    return null;

                output = outputTask.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return null;
            }

            return ParseActiveWindow(output);
        }

        private static WindowArea? ParseActiveWindow(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            try
            {
                var jObj = JObject.Parse(output);
                var at = jObj["at"] as JArray;
                var size = jObj["size"] as JArray;
                var monitor = jObj.Value<int?>("monitor") ?? -1;

                if (at != null && size != null && at.Count >= 2 && size.Count >= 2)
                {
                    return new WindowArea(
                        at[0].Value<float>(),
                        at[1].Value<float>(),
                        size[0].Value<float>(),
                        size[1].Value<float>(),
                        monitor
                    );
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool TryGetDisplayAreaForWindow(WindowArea windowArea, IVirtualScreen virtualScreen, out AreaSettings area)
        {
            var displays = virtualScreen.Displays.Where(d => d is not IVirtualScreen).ToArray();
            if (displays.Length == 0)
            {
                area = AreaSettings.GetDefaults(virtualScreen);
                return true;
            }

            var centerX = windowArea.X + windowArea.Width / 2;
            var centerY = windowArea.Y + windowArea.Height / 2;

            var display = displays.FirstOrDefault(d =>
                centerX >= d.Position.X && centerX < d.Position.X + d.Width &&
                centerY >= d.Position.Y && centerY < d.Position.Y + d.Height);

            if (display == null)
                display = displays[0];

            var xOffset = displays.Min(d => d.Position.X);
            var yOffset = displays.Min(d => d.Position.Y);

            area = new AreaSettings
            {
                Width = display.Width,
                Height = display.Height,
                X = display.Position.X - xOffset + virtualScreen.Position.X + (display.Width / 2),
                Y = display.Position.Y - yOffset + virtualScreen.Position.Y + (display.Height / 2),
                Rotation = 0
            };

            return true;
        }

        public override string ToString() => PLUGIN_NAME;

        private sealed class WindowArea
        {
            public WindowArea(float x, float y, float width, float height, int monitorId)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                MonitorId = monitorId;
            }

            public float X { get; }
            public float Y { get; }
            public float Width { get; }
            public float Height { get; }
            public int MonitorId { get; }
        }
    }
}
