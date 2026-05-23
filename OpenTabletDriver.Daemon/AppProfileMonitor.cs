using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop.AppProfiler;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin;

#nullable enable

namespace OpenTabletDriver.Daemon
{
    public sealed class AppProfileMonitor : IDisposable
    {
        private readonly DriverDaemon _daemon;
        private readonly List<IActiveWindowProvider> _providers;
        private IActiveWindowProvider? _activeProvider;
        private string? _currentPreset;

        public AppProfileMonitor(DriverDaemon daemon)
        {
            _daemon = daemon;
            _providers = new List<IActiveWindowProvider>
            {
                new HyprlandWindowProvider()
                // Add more providers here in the future
            };

            Initialize();
        }

        public void Initialize()
        {
            if (_daemon.Settings == null)
                return;

            var enableAppProfiler = _daemon.Settings.EnableAppProfiler;

            if (enableAppProfiler && _activeProvider == null)
            {
                _activeProvider = _providers.FirstOrDefault(p => p.IsSupported);

                if (_activeProvider != null)
                {
                    _activeProvider.ActiveWindowChanged += OnActiveWindowChanged;
                    _activeProvider.MonitorsChanged += OnMonitorsChanged;
                    _activeProvider.Start();
                    Log.Write("AppProfileMonitor", "Application Profiler started.", LogLevel.Info);
                }
                else
                {
                    Log.Write("AppProfileMonitor", "No supported Application Profiler provider found.", LogLevel.Warning);
                }
            }
            else if (!enableAppProfiler && _activeProvider != null)
            {
                _activeProvider.Stop();
                _activeProvider.ActiveWindowChanged -= OnActiveWindowChanged;
                _activeProvider.MonitorsChanged -= OnMonitorsChanged;
                _activeProvider = null;
                Log.Write("AppProfileMonitor", "Application Profiler stopped.", LogLevel.Info);
            }
        }

        private void OnActiveWindowChanged(object? sender, ActiveWindowChangedEventArgs e)
        {
            if (_daemon.Settings == null || !_daemon.Settings.EnableAppProfiler)
                return;

            var windowClass = e.WindowClass;
            var settings = _daemon.Settings;
            var presetManager = OpenTabletDriver.Desktop.AppInfo.PresetManager;

            if (settings.AppProfiles != null && settings.AppProfiles.TryGetValue(windowClass, out var presetName))
            {
                if (presetName != _currentPreset)
                {
                    presetManager.Refresh();
                    var preset = presetManager.FindPreset(presetName);
                    if (preset != null)
                    {
                        Log.Write("AppProfileMonitor", $"Applying preset '{preset.Name}' for application '{windowClass}'.", LogLevel.Info);
                        Console.WriteLine($"[AppProfiler] Switching to preset '{preset.Name}' for application '{windowClass}'");

                        var appliedSettings = preset.Settings.Clone();
                        PreserveRuntimeSettings(appliedSettings, settings);

                        _ = _daemon.SetSettings(appliedSettings);
                        _currentPreset = presetName;
                    }
                    else
                    {
                        Log.Write("AppProfileMonitor", $"Preset '{presetName}' mapped to '{windowClass}' not found.", LogLevel.Error);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(settings.DefaultAppProfile) && settings.DefaultAppProfile != _currentPreset)
            {
                presetManager.Refresh();
                var preset = presetManager.FindPreset(settings.DefaultAppProfile);
                if (preset != null)
                {
                    Log.Write("AppProfileMonitor", $"Applying default preset '{preset.Name}'.", LogLevel.Info);
                    Console.WriteLine($"[AppProfiler] Reverting to default preset '{preset.Name}' for application '{windowClass}'");

                    var appliedSettings = preset.Settings.Clone();
                    PreserveRuntimeSettings(appliedSettings, settings);

                    _ = _daemon.SetSettings(appliedSettings);
                    _currentPreset = settings.DefaultAppProfile;
                }
            }
        }

        private static void PreserveRuntimeSettings(Settings appliedSettings, Settings currentSettings)
        {
            appliedSettings.EnableAppProfiler = currentSettings.EnableAppProfiler;
            appliedSettings.AppProfiles = currentSettings.AppProfiles != null
                ? new Dictionary<string, string>(currentSettings.AppProfiles)
                : new Dictionary<string, string>();
            appliedSettings.DefaultAppProfile = currentSettings.DefaultAppProfile;

            foreach (var currentProfile in currentSettings.Profiles)
            {
                var appliedProfile = appliedSettings.Profiles.GetProfile(currentProfile.Tablet);
                if (appliedProfile == null)
                    continue;

                appliedProfile.AbsoluteModeSettings.Display = new AreaSettings
                {
                    Area = currentProfile.AbsoluteModeSettings.Display.Area
                };
            }
        }

        private void OnMonitorsChanged(object? sender, EventArgs e)
        {
            if (_daemon.Settings == null)
                return;

            Log.Write("AppProfileMonitor", "Monitors changed. Re-configuring display mapping.", LogLevel.Info);

            // Give Hyprland a tiny moment to settle the displays before we query
            System.Threading.Tasks.Task.Delay(100).GetAwaiter().GetResult();

            var settings = _daemon.Settings;
            OpenTabletDriver.Desktop.Profiles.AreaSettings? virtualArea = null;
            System.Collections.Generic.List<OpenTabletDriver.Desktop.Profiles.AreaSettings>? monitors = null;

            if (_activeProvider is HyprlandWindowProvider hyprProvider)
            {
                virtualArea = HyprlandWindowProvider.GetVirtualScreenArea();
                monitors = HyprlandWindowProvider.GetMonitors();
            }

            if (virtualArea != null)
            {
                foreach (var profile in settings.Profiles)
                {
                    var oldDisplay = profile.AbsoluteModeSettings?.Display;
                    bool wasVirtualScreen = false;

                    if (oldDisplay != null && monitors != null && monitors.Count > 0)
                    {
                        float maxMonitorWidth = 0;
                        float maxMonitorHeight = 0;
                        foreach (var m in monitors)
                        {
                            if (m.Width > maxMonitorWidth) maxMonitorWidth = m.Width;
                            if (m.Height > maxMonitorHeight) maxMonitorHeight = m.Height;
                        }

                        // If it's significantly larger than the largest single monitor, it was likely mapped to the full virtual screen
                        if (oldDisplay.Width > maxMonitorWidth + 1 || oldDisplay.Height > maxMonitorHeight + 1)
                        {
                            wasVirtualScreen = true;
                        }
                    }

                    if (profile.AbsoluteModeSettings == null)
                        continue;

                    if (oldDisplay == null || wasVirtualScreen || monitors == null || monitors.Count == 0)
                    {
                        profile.AbsoluteModeSettings.Display = new OpenTabletDriver.Desktop.Profiles.AreaSettings
                        {
                            Width = virtualArea.Width,
                            Height = virtualArea.Height,
                            X = virtualArea.X,
                            Y = virtualArea.Y,
                            Rotation = 0
                        };
                    }
                    else
                    {
                        OpenTabletDriver.Desktop.Profiles.AreaSettings? nearest = null;
                        float minDistance = float.MaxValue;

                        foreach (var m in monitors)
                        {
                            float dx = oldDisplay.X - m.X;
                            float dy = oldDisplay.Y - m.Y;
                            float dist = dx * dx + dy * dy;
                            if (dist < minDistance)
                            {
                                minDistance = dist;
                                nearest = m;
                            }
                        }

                        if (nearest != null)
                        {
                            profile.AbsoluteModeSettings.Display = new OpenTabletDriver.Desktop.Profiles.AreaSettings
                            {
                                Width = nearest.Width,
                                Height = nearest.Height,
                                X = nearest.X,
                                Y = nearest.Y,
                                Rotation = 0
                            };
                        }
                    }
                }

                _ = _daemon.SetSettings(settings);
                _ = _daemon.ForceResynchronize();
            }
        }

        public void Dispose()
        {
            if (_activeProvider != null)
            {
                _activeProvider.Stop();
                _activeProvider.ActiveWindowChanged -= OnActiveWindowChanged;
                _activeProvider.MonitorsChanged -= OnMonitorsChanged;
                _activeProvider = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
