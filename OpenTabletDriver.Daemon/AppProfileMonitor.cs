using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop.AppProfiler;
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
                    var preset = presetManager.FindPreset(presetName);
                    if (preset != null)
                    {
                        Log.Write("AppProfileMonitor", $"Applying preset '{preset.Name}' for application '{windowClass}'.", LogLevel.Info);

                        preset.Settings.EnableAppProfiler = settings.EnableAppProfiler;
                        preset.Settings.AppProfiles = settings.AppProfiles;
                        preset.Settings.DefaultAppProfile = settings.DefaultAppProfile;

                        _ = _daemon.SetSettings(preset.Settings);
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
                var preset = presetManager.FindPreset(settings.DefaultAppProfile);
                if (preset != null)
                {
                    Log.Write("AppProfileMonitor", $"Applying default preset '{preset.Name}'.", LogLevel.Info);

                    preset.Settings.EnableAppProfiler = settings.EnableAppProfiler;
                    preset.Settings.AppProfiles = settings.AppProfiles;
                    preset.Settings.DefaultAppProfile = settings.DefaultAppProfile;

                    _ = _daemon.SetSettings(preset.Settings);
                    _currentPreset = settings.DefaultAppProfile;
                }
            }
        }

        public void Dispose()
        {
            if (_activeProvider != null)
            {
                _activeProvider.Stop();
                _activeProvider.ActiveWindowChanged -= OnActiveWindowChanged;
                _activeProvider = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
