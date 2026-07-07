using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop.AppProfiler;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
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
        private string? _currentOutputMode;
        private HyprlandTrackingThread? _layerTracker;
        private bool _isHoveringLayer = false;

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

            var enableAppProfiler = _daemon.AppProfilerSettings.EnableAppProfiler;

            if (enableAppProfiler && _activeProvider == null)
            {
                _activeProvider = _providers.FirstOrDefault(p => p.IsSupported);

                if (_activeProvider != null)
                {
                    _activeProvider.ActiveWindowChanged += OnActiveWindowChanged;
                    _activeProvider.MonitorsChanged += OnMonitorsChanged;
                    _activeProvider.Start();
                    Log.Write("AppProfileMonitor", "Application Profiler started.", LogLevel.Info);

                    if (_activeProvider is HyprlandWindowProvider)
                    {
                        var targets = new List<string>();
                        if (_daemon.AppProfilerSettings.TrackedNamespaces != null)
                            targets.AddRange(_daemon.AppProfilerSettings.TrackedNamespaces);

                        _layerTracker = new HyprlandTrackingThread(targets.Distinct());
                        _layerTracker.LayerHoverStateChanged += OnLayerHoverStateChanged;
                        _layerTracker.Start();
                        Log.Write("AppProfileMonitor", "Hyprland Layer Tracker started.", LogLevel.Info);
                    }
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

                if (_layerTracker != null)
                {
                    _layerTracker.LayerHoverStateChanged -= OnLayerHoverStateChanged;
                    _layerTracker.Dispose();
                    _layerTracker = null;
                }
            }
        }

        private string? _hoveredNamespace;

        private void OnLayerHoverStateChanged(object? sender, HyprlandTrackingThread.LayerHoverStateChangedEventArgs e)
        {
            _isHoveringLayer = e.IsHovering;
            _hoveredNamespace = e.Namespace;

            if (e.IsHovering)
            {
                Log.Write("AppProfileMonitor", $"Cursor entered a tracked layer ({e.Namespace}).", LogLevel.Info);
                OnActiveWindowChanged(this, new ActiveWindowChangedEventArgs("", ""));
            }
            else
            {
                Log.Write("AppProfileMonitor", "Cursor exited tracked layer. Restoring active window profile.", LogLevel.Info);
                if (_activeProvider is HyprlandWindowProvider hyprlandProvider)
                {
                    hyprlandProvider.ForceRefreshActiveWindow();
                }
            }
        }

        private void OnActiveWindowChanged(object? sender, ActiveWindowChangedEventArgs e)
        {
            if (_daemon.AppProfilerSettings == null || !_daemon.AppProfilerSettings.EnableAppProfiler)
                return;

            if (_isHoveringLayer && !string.IsNullOrEmpty(e.WindowClass))
            {
                // Ignore real window focus changes while hovering a layer
                return;
            }

            var windowClass = e.WindowClass;
            var settings = _daemon.Settings;
            var appSettings = _daemon.AppProfilerSettings;
            var presetManager = OpenTabletDriver.Desktop.AppInfo.PresetManager;

            string? targetPreset = null;
            if (_isHoveringLayer && !string.IsNullOrEmpty(_hoveredNamespace) && appSettings.NamespaceProfiles != null && appSettings.NamespaceProfiles.TryGetValue(_hoveredNamespace, out var nsPreset))
            {
                targetPreset = nsPreset;
            }
            else if (!_isHoveringLayer && appSettings.AppProfiles != null && appSettings.AppProfiles.TryGetValue(windowClass, out var presetName))
            {
                targetPreset = presetName;
            }
            else if (!string.IsNullOrEmpty(appSettings.DefaultAppProfile))
            {
                targetPreset = appSettings.DefaultAppProfile;
            }

            string? targetOutputMode = null;
            if (_isHoveringLayer && !string.IsNullOrEmpty(_hoveredNamespace) && appSettings.NamespaceOutputModes != null && appSettings.NamespaceOutputModes.TryGetValue(_hoveredNamespace, out var nsOutputMode))
            {
                targetOutputMode = nsOutputMode;
            }
            else if (!_isHoveringLayer && appSettings.AppOutputModes != null && appSettings.AppOutputModes.TryGetValue(windowClass, out var outputModeName))
            {
                targetOutputMode = outputModeName;
            }
            else if (!string.IsNullOrEmpty(appSettings.DefaultOutputMode))
            {
                targetOutputMode = appSettings.DefaultOutputMode;
            }

            bool presetChanged = targetPreset != _currentPreset;
            bool outputModeChanged = targetOutputMode != _currentOutputMode;
            bool syncFocusChangedSettings = false;

            Settings appliedSettings;

            if (presetChanged && targetPreset != null)
            {
                presetManager.Refresh();
                var preset = presetManager.FindPreset(targetPreset);
                if (preset != null)
                {
                    Log.Write("AppProfileMonitor", $"Applying preset '{preset.Name}' for application '{windowClass}'.", LogLevel.Info);
                    Console.WriteLine($"[AppProfiler] Switching to preset '{preset.Name}' for application '{windowClass}'");

                    appliedSettings = preset.Settings.Clone();
                    _currentPreset = targetPreset;
                }
                else
                {
                    Log.Write("AppProfileMonitor", $"Preset '{targetPreset}' not found.", LogLevel.Error);
                    appliedSettings = settings.Clone();
                }
            }
            else
            {
                appliedSettings = settings.Clone();
            }

            if (presetChanged)
            {
                PreserveDisplaySettings(appliedSettings, settings);
            }

            if (appSettings.SyncFocus && _activeProvider is HyprlandWindowProvider)
            {
                var activeMonitor = OpenTabletDriver.Desktop.Interop.Display.HyprlandDisplayInterop.GetActiveMonitor(null);
                if (activeMonitor != null)
                {
                    var targetDisplay = OpenTabletDriver.Desktop.Interop.Display.HyprlandDisplayInterop.ToAreaSettings(activeMonitor);
                    foreach (var profile in appliedSettings.Profiles)
                    {
                        if (profile.AbsoluteModeSettings != null)
                        {
                            var currentDisplay = profile.AbsoluteModeSettings.Display;
                            if (currentDisplay.Width != targetDisplay.Width ||
                                currentDisplay.Height != targetDisplay.Height ||
                                currentDisplay.X != targetDisplay.X ||
                                currentDisplay.Y != targetDisplay.Y)
                            {
                                profile.AbsoluteModeSettings.Display = new AreaSettings
                                {
                                    Width = targetDisplay.Width,
                                    Height = targetDisplay.Height,
                                    X = targetDisplay.X,
                                    Y = targetDisplay.Y,
                                    Rotation = targetDisplay.Rotation
                                };
                                syncFocusChangedSettings = true;
                            }
                        }
                    }

                    if (syncFocusChangedSettings)
                    {
                        Log.Write("AppProfileMonitor", $"Syncing focus to monitor at ({targetDisplay.X}, {targetDisplay.Y}) for application '{windowClass}'.", LogLevel.Info);
                    }
                }
            }

            if (targetOutputMode != null && (outputModeChanged || presetChanged))
            {
                var newOutputModeStore = PluginSettingStore.FromPath(targetOutputMode);
                if (newOutputModeStore != null)
                {
                    Log.Write("AppProfileMonitor", $"Applying output mode '{targetOutputMode}' for application '{windowClass}'.", LogLevel.Info);
                    Console.WriteLine($"[AppProfiler] Switching to output mode '{targetOutputMode}' for application '{windowClass}'");

                    foreach (var profile in appliedSettings.Profiles)
                    {
                        profile.OutputMode = newOutputModeStore;
                    }
                    _currentOutputMode = targetOutputMode;
                }
                else
                {
                    Log.Write("AppProfileMonitor", $"Output mode '{targetOutputMode}' not found.", LogLevel.Error);
                }
            }

            if (presetChanged || outputModeChanged || syncFocusChangedSettings)
            {
                _ = _daemon.SetSettings(appliedSettings);
            }
        }

        private static void PreserveDisplaySettings(Settings appliedSettings, Settings currentSettings)
        {

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

            if (_layerTracker != null)
            {
                _layerTracker.LayerHoverStateChanged -= OnLayerHoverStateChanged;
                _layerTracker.Dispose();
                _layerTracker = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}
