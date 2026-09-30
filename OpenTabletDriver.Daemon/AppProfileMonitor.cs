using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly SemaphoreSlim _profileLock = new(1, 1);
        private IActiveWindowProvider? _activeProvider;
        private string? _currentPreset;
        private string? _currentOutputMode;
        private HyprlandTrackingThread? _layerTracker;
        private bool _isHoveringLayer = false;
        private string? _hoveredNamespace;
        private string _lastActiveWindowClass = string.Empty;
        private string _lastActiveWindowTitle = string.Empty;

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
                        var targets = GetTargetNamespaces();
                        _layerTracker = new HyprlandTrackingThread(targets);
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
            else if (enableAppProfiler && _activeProvider != null)
            {
                if (_layerTracker != null)
                {
                    var targets = GetTargetNamespaces();
                    _layerTracker.UpdateTargetNamespaces(targets);
                }

                // Re-evaluate current active state with updated profiler rules
                _ = ApplyActiveProfileAsync(_lastActiveWindowClass, _lastActiveWindowTitle);
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

                _currentPreset = null;
                _currentOutputMode = null;
                _isHoveringLayer = false;
                _hoveredNamespace = null;
                _lastActiveWindowClass = string.Empty;
                _lastActiveWindowTitle = string.Empty;

                // Restore base settings synchronously/cleanly if available
                if (_daemon.BaseSettings != null)
                {
                    _ = Task.Run(async () =>
                    {
                        await _profileLock.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            if (_daemon.AppProfilerSettings.EnableAppProfiler)
                                return;

                            if (_daemon.BaseSettings != null)
                            {
                                await _daemon.SetSettings(_daemon.BaseSettings.Clone(), false).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            _profileLock.Release();
                        }
                    });
                }
            }
        }

        public void ResetActiveTrackingState()
        {
            _currentPreset = null;
            _currentOutputMode = null;
            _isHoveringLayer = false;
            _hoveredNamespace = null;
            _lastActiveWindowClass = string.Empty;
            _lastActiveWindowTitle = string.Empty;
        }

        private HashSet<string> GetTargetNamespaces()
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appSettings = _daemon.AppProfilerSettings;
            if (appSettings.TrackedNamespaces != null)
            {
                foreach (var ns in appSettings.TrackedNamespaces)
                {
                    if (!string.IsNullOrWhiteSpace(ns))
                        targets.Add(ns);
                }
            }

            if (appSettings.NamespaceProfiles != null)
            {
                foreach (var ns in appSettings.NamespaceProfiles.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(ns))
                        targets.Add(ns);
                }
            }

            if (appSettings.NamespaceOutputModes != null)
            {
                foreach (var ns in appSettings.NamespaceOutputModes.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(ns))
                        targets.Add(ns);
                }
            }

            return targets;
        }

        private void OnLayerHoverStateChanged(object? sender, HyprlandTrackingThread.LayerHoverStateChangedEventArgs e)
        {
            _isHoveringLayer = e.IsHovering;
            _hoveredNamespace = e.Namespace;

            if (e.IsHovering)
            {
                Log.Write("AppProfileMonitor", $"Cursor entered a tracked layer ({e.Namespace}).", LogLevel.Info);
                _ = ApplyActiveProfileAsync(string.Empty, string.Empty);
            }
            else
            {
                Log.Write("AppProfileMonitor", "Cursor exited tracked layer. Restoring active window profile.", LogLevel.Info);
                if (_activeProvider is HyprlandWindowProvider hyprlandProvider)
                {
                    hyprlandProvider.ForceRefreshActiveWindow();
                }
                else
                {
                    _ = ApplyActiveProfileAsync(_lastActiveWindowClass, _lastActiveWindowTitle);
                }
            }
        }

        private void OnActiveWindowChanged(object? sender, ActiveWindowChangedEventArgs e)
        {
            if (_daemon.AppProfilerSettings == null || !_daemon.AppProfilerSettings.EnableAppProfiler)
                return;

            _lastActiveWindowClass = e.WindowClass;
            _lastActiveWindowTitle = e.WindowTitle;

            if (_isHoveringLayer && !string.IsNullOrEmpty(e.WindowClass))
            {
                // Cache active window but retain layer profile while hovering
                return;
            }

            _ = ApplyActiveProfileAsync(e.WindowClass, e.WindowTitle);
        }

        private static bool TryLookupCaseInsensitive(Dictionary<string, string>? dict, string key, out string value)
        {
            value = string.Empty;
            if (dict == null || string.IsNullOrEmpty(key))
                return false;

            if (dict.TryGetValue(key, out var directMatch))
            {
                value = directMatch;
                return true;
            }

            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        private async Task ApplyActiveProfileAsync(string windowClass, string windowTitle)
        {
            await _profileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_daemon.AppProfilerSettings == null || !_daemon.AppProfilerSettings.EnableAppProfiler)
                    return;

                var baseSettings = _daemon.BaseSettings ?? _daemon.Settings;
                if (baseSettings == null)
                    return;

                var appSettings = _daemon.AppProfilerSettings;
                var presetManager = OpenTabletDriver.Desktop.AppInfo.PresetManager;

                string? targetPreset = null;
                if (_isHoveringLayer && !string.IsNullOrEmpty(_hoveredNamespace) && TryLookupCaseInsensitive(appSettings.NamespaceProfiles, _hoveredNamespace, out var nsPreset))
                {
                    targetPreset = nsPreset;
                }
                else if (!_isHoveringLayer && TryLookupCaseInsensitive(appSettings.AppProfiles, windowClass, out var presetName))
                {
                    targetPreset = presetName;
                }
                else if (!string.IsNullOrEmpty(appSettings.DefaultAppProfile))
                {
                    targetPreset = appSettings.DefaultAppProfile;
                }

                string? targetOutputMode = null;
                if (_isHoveringLayer && !string.IsNullOrEmpty(_hoveredNamespace) && TryLookupCaseInsensitive(appSettings.NamespaceOutputModes, _hoveredNamespace, out var nsOutputMode))
                {
                    targetOutputMode = nsOutputMode;
                }
                else if (!_isHoveringLayer && TryLookupCaseInsensitive(appSettings.AppOutputModes, windowClass, out var outputModeName))
                {
                    targetOutputMode = outputModeName;
                }
                else if (!string.IsNullOrEmpty(appSettings.DefaultOutputMode))
                {
                    targetOutputMode = appSettings.DefaultOutputMode;
                }

                bool presetChanged = targetPreset != _currentPreset;

                Settings appliedSettings;
                if (targetPreset != null)
                {
                    presetManager.Refresh();
                    var preset = presetManager.FindPreset(targetPreset);
                    if (preset != null)
                    {
                        if (presetChanged)
                        {
                            Log.Write("AppProfileMonitor", $"Applying preset '{preset.Name}' for application '{windowClass}'.", LogLevel.Info);
                        }
                        appliedSettings = preset.Settings.Clone();
                        _currentPreset = targetPreset;
                    }
                    else
                    {
                        Log.Write("AppProfileMonitor", $"Preset '{targetPreset}' not found. Falling back to base settings.", LogLevel.Error);
                        appliedSettings = baseSettings.Clone();
                        _currentPreset = null;
                    }
                }
                else
                {
                    if (presetChanged)
                    {
                        Log.Write("AppProfileMonitor", $"Restoring base profile for application '{windowClass}'.", LogLevel.Info);
                    }
                    appliedSettings = baseSettings.Clone();
                    _currentPreset = null;
                }

                if (presetChanged && _daemon.Settings != null)
                {
                    PreserveDisplaySettings(appliedSettings, _daemon.Settings);
                }

                bool syncFocusChangedSettings = false;
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

                // Resolve target output mode:
                // 1. Explicit targetOutputMode (layer override, app override, or DefaultOutputMode)
                // 2. Preset's own configured OutputMode (if a preset was loaded)
                // 3. Fallback to baseSettings OutputMode
                string? fallbackOutputMode = appliedSettings.Profiles.FirstOrDefault()?.OutputMode.Path
                    ?? baseSettings.Profiles.FirstOrDefault()?.OutputMode.Path;

                string resolvedOutputMode = targetOutputMode ?? fallbackOutputMode ?? string.Empty;
                bool outputModeChanged = !string.IsNullOrEmpty(resolvedOutputMode) && resolvedOutputMode != _currentOutputMode;

                if (!string.IsNullOrEmpty(resolvedOutputMode) && (outputModeChanged || presetChanged))
                {
                    var newOutputModeStore = PluginSettingStore.FromPath(resolvedOutputMode);
                    if (newOutputModeStore != null)
                    {
                        if (outputModeChanged)
                        {
                            Log.Write("AppProfileMonitor", $"Applying output mode '{resolvedOutputMode}' for application '{windowClass}'.", LogLevel.Info);
                        }

                        foreach (var profile in appliedSettings.Profiles)
                        {
                            profile.OutputMode = newOutputModeStore;
                            EnsureOutputModeSettings(profile);
                        }
                        _currentOutputMode = resolvedOutputMode;
                    }
                    else
                    {
                        Log.Write("AppProfileMonitor", $"Output mode '{resolvedOutputMode}' not found.", LogLevel.Error);
                    }
                }

                if (presetChanged || outputModeChanged || syncFocusChangedSettings)
                {
                    await _daemon.SetSettings(appliedSettings, true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Write("AppProfileMonitor", $"Error applying profile: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _profileLock.Release();
            }
        }

        private void EnsureOutputModeSettings(Profile profile)
        {
            if (profile.RelativeModeSettings == null)
            {
                profile.RelativeModeSettings = RelativeModeSettings.GetDefaults();
            }

            if (profile.AbsoluteModeSettings == null)
            {
                var tablet = _daemon.Driver.InputDevices.FirstOrDefault(t => t.Properties.Name == profile.Tablet);
                var digitizer = tablet?.Properties.Specifications.Digitizer;
                if (digitizer != null)
                {
                    profile.AbsoluteModeSettings = AbsoluteModeSettings.GetDefaults(digitizer);
                }
            }
        }

        private static void PreserveDisplaySettings(Settings appliedSettings, Settings currentSettings)
        {
            foreach (var currentProfile in currentSettings.Profiles)
            {
                var appliedProfile = appliedSettings.Profiles.GetProfile(currentProfile.Tablet);
                if (appliedProfile?.AbsoluteModeSettings == null || currentProfile.AbsoluteModeSettings?.Display == null)
                    continue;

                appliedProfile.AbsoluteModeSettings.Display = new AreaSettings
                {
                    Area = currentProfile.AbsoluteModeSettings.Display.Area
                };
            }
        }

        private void OnMonitorsChanged(object? sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await _profileLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_daemon.Settings == null)
                        return;

                    Log.Write("AppProfileMonitor", "Monitors changed. Re-configuring display mapping.", LogLevel.Info);

                    // Give Hyprland a tiny moment to settle the displays before we query
                    await Task.Delay(100).ConfigureAwait(false);

                    var settings = _daemon.Settings;
                    AreaSettings? virtualArea = null;
                    List<AreaSettings>? monitors = null;

                    if (_activeProvider is HyprlandWindowProvider)
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

                                if (oldDisplay.Width > maxMonitorWidth + 1 || oldDisplay.Height > maxMonitorHeight + 1)
                                {
                                    wasVirtualScreen = true;
                                }
                            }

                            if (profile.AbsoluteModeSettings == null)
                                continue;

                            if (oldDisplay == null || wasVirtualScreen || monitors == null || monitors.Count == 0)
                            {
                                profile.AbsoluteModeSettings.Display = new AreaSettings
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
                                AreaSettings? nearest = null;
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
                                    profile.AbsoluteModeSettings.Display = new AreaSettings
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

                        await _daemon.SetSettings(settings, true).ConfigureAwait(false);
                        await _daemon.ForceResynchronize().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _profileLock.Release();
                }
            });
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

            _profileLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
