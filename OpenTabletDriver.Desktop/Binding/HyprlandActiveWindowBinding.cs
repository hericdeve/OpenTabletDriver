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
                    if (windowArea != null && GetMonitorAreaSettings(windowArea.MonitorId) is AreaSettings displayArea)
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

        private static AreaSettings? GetMonitorAreaSettings(int monitorId)
        {
            var monitors = GetHyprlandMonitors();
            var target = monitors.FirstOrDefault(m => m.MonitorId == monitorId);
            if (target != null)
            {
                // Hyprland 'width' in json is physical width. Logical width = width / scale.
                // But wait! Hyprland 'monitor.transform' or 'monitor.scale' might be needed.
                // To be safe, we calculate width/height logically if we can, or just use what we used in the Cycle Binding.
                // In HyprlandMonitorCycleBinding, the scaling isn't applied either, it uses 'width' from json.
                // If the user's issue was just about wrong monitors entirely, this will fix it.
                return new AreaSettings
                {
                    Width = target.Width,
                    Height = target.Height,
                    X = target.X + target.Width / 2,
                    Y = target.Y + target.Height / 2,
                    Rotation = 0
                };
            }
            return null;
        }

        private static WindowArea[] GetHyprlandMonitors()
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
                process.StartInfo.ArgumentList.Add("monitors");
                process.StartInfo.ArgumentList.Add("-j");

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var monitors = JArray.Parse(output);
                    return monitors
                        .Where(m => !(m.Value<bool?>("disabled") ?? false))
                        .Select(m => {
                            var width = m.Value<float?>("width") ?? 0;
                            var height = m.Value<float?>("height") ?? 0;
                            var scale = m.Value<float?>("scale") ?? 1.0f;
                            if (scale <= 0) scale = 1.0f;
                            var transform = m.Value<int?>("transform") ?? 0;
                            
                            // If rotated, swap physical width/height
                            var isRotated = transform % 2 != 0;
                            var actualWidth = isRotated ? height : width;
                            var actualHeight = isRotated ? width : height;

                            // Scale to logical coordinates
                            var logicalWidth = actualWidth / scale;
                            var logicalHeight = actualHeight / scale;

                            return new WindowArea(
                                m.Value<float?>("x") ?? 0,
                                m.Value<float?>("y") ?? 0,
                                logicalWidth,
                                logicalHeight,
                                m.Value<int?>("id") ?? -1
                            );
                        })
                        .Where(m => m.Width > 0 && m.Height > 0)
                        .ToArray();
                }
            }
            catch {}
            return [];
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
