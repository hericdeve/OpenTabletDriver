using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.AppProfiler;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Interop.AppProfiler;
using OpenTabletDriver.Desktop.Interop.Display;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;

#nullable enable

namespace OpenTabletDriver.Daemon
{
    public sealed class AppProfileMonitor : IDisposable
    {
        private static readonly ConcurrentDictionary<string, Regex?> _regexCache = new(StringComparer.Ordinal);

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
        private AppProfileRule? _activeMatchedRule;
        private MonitorArea[] _previousMonitors = Array.Empty<MonitorArea>();
        private CancellationTokenSource? _monitorChangeDebounceCts;

        public string CurrentWindowClass => _lastActiveWindowClass;
        public string CurrentWindowTitle => _lastActiveWindowTitle;
        public AppProfileRule? ActiveMatchedRule => _activeMatchedRule;

        public void ForceRefreshActiveWindow()
        {
            _activeProvider?.ForceRefreshActiveWindow();
        }

        public ActiveAppProfileContext GetActiveContext()
        {
            return new ActiveAppProfileContext
            {
                WindowClass = _lastActiveWindowClass,
                WindowTitle = _lastActiveWindowTitle,
                IsHoveringLayer = _isHoveringLayer,
                LayerNamespace = _hoveredNamespace,
                MatchedRuleId = _activeMatchedRule?.Id,
                MatchedRuleName = _activeMatchedRule?.Name,
                ActivePreset = _currentPreset,
                ActiveOutputMode = _currentOutputMode
            };
        }

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
            if (_activeProvider == null)
            {
                _activeProvider = _providers.FirstOrDefault(p => p.IsSupported);

                if (_activeProvider != null)
                {
                    _activeProvider.ActiveWindowChanged += OnActiveWindowChanged;
                    _activeProvider.MonitorsChanged += OnMonitorsChanged;
                    _activeProvider.Start();
                    _activeProvider.ForceRefreshActiveWindow();
                    _previousMonitors = HyprlandDisplayInterop.GetMonitors(DesktopInterop.VirtualScreen);
                    Log.Write("AppProfileMonitor", "Active Window Provider started.", LogLevel.Info);
                }
                else
                {
                    Log.Write("AppProfileMonitor", "No supported Application Profiler provider found.", LogLevel.Warning);
                }
            }

            if (_daemon.Settings == null)
                return;

            var enableAppProfiler = _daemon.AppProfilerSettings.EnableAppProfiler;

            if (enableAppProfiler)
            {
                if (_activeProvider is HyprlandWindowProvider && _layerTracker == null)
                {
                    var targets = GetTargetNamespaces();
                    _layerTracker = new HyprlandTrackingThread(targets);
                    _layerTracker.LayerHoverStateChanged += OnLayerHoverStateChanged;
                    _layerTracker.Start();
                    Log.Write("AppProfileMonitor", "Hyprland Layer Tracker started.", LogLevel.Info);
                }
                else if (_layerTracker != null)
                {
                    var targets = GetTargetNamespaces();
                    _layerTracker.UpdateTargetNamespaces(targets);
                }

                // Re-evaluate current active state with updated profiler rules
                _ = ApplyActiveProfileAsync(_lastActiveWindowClass, _lastActiveWindowTitle);
            }
            else
            {
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
                _activeMatchedRule = null;
            }
        }

        public void ResetActiveTrackingState()
        {
            _currentPreset = null;
            _currentOutputMode = null;
            _isHoveringLayer = false;
            _hoveredNamespace = null;
            _activeMatchedRule = null;
        }

        private HashSet<string> GetTargetNamespaces()
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appSettings = _daemon.AppProfilerSettings;
            if (appSettings == null)
                return targets;

            if (appSettings.Rules != null)
            {
                foreach (var rule in appSettings.Rules)
                {
                    if (rule.Enabled && rule.TargetType == RuleTargetType.LayerNamespace && !string.IsNullOrWhiteSpace(rule.Pattern))
                    {
                        targets.Add(rule.Pattern);
                    }
                }
            }

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
            _lastActiveWindowClass = e.WindowClass;
            _lastActiveWindowTitle = e.WindowTitle;

            if (_daemon.AppProfilerSettings == null || !_daemon.AppProfilerSettings.EnableAppProfiler)
                return;

            if (_isHoveringLayer && !string.IsNullOrEmpty(e.WindowClass))
            {
                // Cache active window but retain layer profile while hovering
                return;
            }

            _ = ApplyActiveProfileAsync(e.WindowClass, e.WindowTitle);
        }

        private static bool MatchesRule(AppProfileRule rule, string windowClass, string windowTitle, bool isHoveringLayer, string? hoveredNamespace)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
                return false;

            string candidate;
            if (rule.TargetType == RuleTargetType.LayerNamespace)
            {
                if (!isHoveringLayer || string.IsNullOrEmpty(hoveredNamespace))
                    return false;
                candidate = hoveredNamespace;
            }
            else
            {
                if (isHoveringLayer)
                    return false; // Layer takes precedence; window rules do not evaluate while hovering a layer

                candidate = rule.TargetType switch
                {
                    RuleTargetType.WindowClass => windowClass,
                    RuleTargetType.WindowTitle => windowTitle,
                    _ => string.Empty
                };
            }

            if (string.IsNullOrEmpty(candidate))
                return false;

            return rule.MatchType switch
            {
                RuleMatchType.Exact => string.Equals(candidate, rule.Pattern, StringComparison.OrdinalIgnoreCase),
                RuleMatchType.Contains => candidate.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0,
                RuleMatchType.Regex => MatchRegexSafe(candidate, rule.Pattern),
                _ => false
            };
        }

        private static bool MatchRegexSafe(string input, string pattern)
        {
            try
            {
                var regex = _regexCache.GetOrAdd(pattern, p =>
                {
                    try
                    {
                        return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
                    }
                    catch
                    {
                        return null;
                    }
                });

                return regex != null && regex.IsMatch(input);
            }
            catch
            {
                return false;
            }
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

                AppProfileRule? matchedRule = null;
                if (appSettings.Rules != null && appSettings.Rules.Count > 0)
                {
                    matchedRule = appSettings.Rules.FirstOrDefault(r => MatchesRule(r, windowClass, windowTitle, _isHoveringLayer, _hoveredNamespace));
                }

                _activeMatchedRule = matchedRule;

                string? targetPreset = null;
                string? targetOutputMode = null;
                var targetDisplayMapping = RuleDisplayMapping.Inherit;
                string? targetMonitor = null;

                if (matchedRule != null)
                {
                    targetPreset = !string.IsNullOrEmpty(matchedRule.PresetName) ? matchedRule.PresetName : appSettings.DefaultAppProfile;
                    targetOutputMode = !string.IsNullOrEmpty(matchedRule.OutputMode) ? matchedRule.OutputMode : appSettings.DefaultOutputMode;
                    targetDisplayMapping = matchedRule.DisplayMapping;
                    targetMonitor = matchedRule.TargetMonitor;
                }
                else
                {
                    // Fallback to legacy dictionary lookup if available
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

                    targetDisplayMapping = appSettings.SyncFocus ? RuleDisplayMapping.FollowFocus : RuleDisplayMapping.Inherit;
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
                            var targetName = _isHoveringLayer ? $"layer '{_hoveredNamespace}'" : $"application '{windowClass}'";
                            var ruleText = matchedRule != null ? $" (rule: '{matchedRule.Name}')" : string.Empty;
                            Log.Write("AppProfileMonitor", $"Applying preset '{preset.Name}' for {targetName}{ruleText}.", LogLevel.Info);
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
                        var targetName = _isHoveringLayer ? $"layer '{_hoveredNamespace}'" : $"application '{windowClass}'";
                        Log.Write("AppProfileMonitor", $"Restoring base profile for {targetName}.", LogLevel.Info);
                    }
                    appliedSettings = baseSettings.Clone();
                    _currentPreset = null;
                }

                if (presetChanged && _daemon.Settings != null)
                {
                    PreserveDisplaySettings(appliedSettings, _daemon.Settings);
                }

                bool syncFocusChangedSettings = false;
                if (targetDisplayMapping == RuleDisplayMapping.FollowFocus && _activeProvider is HyprlandWindowProvider)
                {
                    var activeMonitor = OpenTabletDriver.Desktop.Interop.Display.HyprlandDisplayInterop.GetActiveMonitor(null);
                    if (activeMonitor != null)
                    {
                        var targetDisplay = OpenTabletDriver.Desktop.Interop.Display.HyprlandDisplayInterop.ToAreaSettings(activeMonitor);
                        syncFocusChangedSettings = ApplyDisplayToProfiles(appliedSettings, targetDisplay);

                        if (syncFocusChangedSettings)
                        {
                            var targetName = _isHoveringLayer ? $"layer '{_hoveredNamespace}'" : $"application '{windowClass}'";
                            Log.Write("AppProfileMonitor", $"Syncing focus to monitor '{activeMonitor.Name}' at ({targetDisplay.X}, {targetDisplay.Y}) for {targetName}.", LogLevel.Info);
                        }
                    }
                }
                else if (targetDisplayMapping == RuleDisplayMapping.SpecificMonitor && !string.IsNullOrEmpty(targetMonitor))
                {
                    var monitors = OpenTabletDriver.Desktop.Interop.Display.HyprlandDisplayInterop.GetMonitors(null);
                    var matchedMonitor = monitors.FirstOrDefault(m =>
                        string.Equals(m.Name, targetMonitor, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(m.Index.ToString(), targetMonitor, StringComparison.OrdinalIgnoreCase));

                    if (matchedMonitor != null)
                    {
                        var targetDisplay = OpenTabletDriver.Desktop.Interop.Display.HyprlandDisplayInterop.ToAreaSettings(matchedMonitor);
                        syncFocusChangedSettings = ApplyDisplayToProfiles(appliedSettings, targetDisplay);

                        if (syncFocusChangedSettings)
                        {
                            var targetName = _isHoveringLayer ? $"layer '{_hoveredNamespace}'" : $"application '{windowClass}'";
                            Log.Write("AppProfileMonitor", $"Setting display mapping to monitor '{matchedMonitor.Name}' at ({targetDisplay.X}, {targetDisplay.Y}) for {targetName}.", LogLevel.Info);
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
                            var targetName = _isHoveringLayer ? $"layer '{_hoveredNamespace}'" : $"application '{windowClass}'";
                            Log.Write("AppProfileMonitor", $"Applying output mode '{resolvedOutputMode}' for {targetName}.", LogLevel.Info);
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

        private static bool ApplyDisplayToProfiles(Settings appliedSettings, AreaSettings targetDisplay)
        {
            bool changed = false;
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
                        changed = true;
                    }
                }
            }
            return changed;
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
                    Width = currentProfile.AbsoluteModeSettings.Display.Width,
                    Height = currentProfile.AbsoluteModeSettings.Display.Height,
                    X = currentProfile.AbsoluteModeSettings.Display.X,
                    Y = currentProfile.AbsoluteModeSettings.Display.Y,
                    Rotation = currentProfile.AbsoluteModeSettings.Display.Rotation
                };
            }
        }

        private void OnMonitorsChanged(object? sender, EventArgs e)
        {
            _monitorChangeDebounceCts?.Cancel();
            _monitorChangeDebounceCts?.Dispose();
            var cts = new CancellationTokenSource();
            _monitorChangeDebounceCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Debounce rapid monitor events (e.g. negotiation bursts from Hyprland)
                    await Task.Delay(150, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                await _profileLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (cts.IsCancellationRequested || _daemon.Settings == null)
                        return;

                    Log.Write("AppProfileMonitor", "Monitors changed. Re-configuring display mapping.", LogLevel.Info);

                    // Reset DesktopInterop virtual screen and pointers so they will be re-initialized
                    // with the new desktop dimensions upon next access / OutputMode reconstruction.
                    DesktopInterop.ResetVirtualScreenAndPointers();

                    var newMonitors = HyprlandDisplayInterop.GetMonitors(DesktopInterop.VirtualScreen);
                    if (newMonitors.Length == 0)
                        return;

                    var virtualArea = HyprlandDisplayInterop.GetVirtualScreenArea(newMonitors);
                    var oldMonitors = _previousMonitors;

                    var settings = _daemon.Settings;
                    var baseSettings = _daemon.BaseSettings;

                    void UpdateProfilesDisplay(Settings targetSettings)
                    {
                        foreach (var profile in targetSettings.Profiles)
                        {
                            if (profile.AbsoluteModeSettings == null)
                                continue;

                            var oldDisplay = profile.AbsoluteModeSettings.Display;
                            if (oldDisplay == null)
                            {
                                profile.AbsoluteModeSettings.Display = new AreaSettings
                                {
                                    Width = virtualArea.Width,
                                    Height = virtualArea.Height,
                                    X = virtualArea.X,
                                    Y = virtualArea.Y,
                                    Rotation = 0
                                };
                                continue;
                            }

                            // Check if oldDisplay was mapped to the entire virtual desktop
                            bool wasVirtualScreen = false;
                            if (oldMonitors.Length > 1)
                            {
                                float maxOldMonWidth = oldMonitors.Max(m => m.Width);
                                float maxOldMonHeight = oldMonitors.Max(m => m.Height);
                                if (oldDisplay.Width > maxOldMonWidth + 5 || oldDisplay.Height > maxOldMonHeight + 5)
                                {
                                    wasVirtualScreen = true;
                                }
                            }

                            if (wasVirtualScreen)
                            {
                                profile.AbsoluteModeSettings.Display = new AreaSettings
                                {
                                    Width = virtualArea.Width,
                                    Height = virtualArea.Height,
                                    X = virtualArea.X,
                                    Y = virtualArea.Y,
                                    Rotation = 0
                                };
                                continue;
                            }

                            // Determine which monitor the profile was previously on
                            MonitorArea? matchedOldMonitor = null;
                            if (oldMonitors.Length > 0)
                            {
                                var oldCenter = new Vector2(oldDisplay.X, oldDisplay.Y);
                                matchedOldMonitor = oldMonitors.FirstOrDefault(m => m.Contains(oldCenter));
                                if (matchedOldMonitor == null)
                                {
                                    matchedOldMonitor = oldMonitors
                                        .OrderBy(m => Vector2.DistanceSquared(oldCenter, m.Center))
                                        .FirstOrDefault();
                                }
                            }

                            MonitorArea? targetNewMonitor = null;
                            if (matchedOldMonitor != null)
                            {
                                // Match by exact monitor Name first, then by ID
                                targetNewMonitor = newMonitors.FirstOrDefault(m => string.Equals(m.Name, matchedOldMonitor.Name, StringComparison.OrdinalIgnoreCase))
                                                   ?? newMonitors.FirstOrDefault(m => m.Id == matchedOldMonitor.Id);
                            }

                            // If previous monitor is still connected, stay mapped to it!
                            if (targetNewMonitor != null)
                            {
                                profile.AbsoluteModeSettings.Display = HyprlandDisplayInterop.ToAreaSettings(targetNewMonitor);
                            }
                            else
                            {
                                // If the monitor was disconnected, fallback to active/focused or first monitor
                                var fallback = HyprlandDisplayInterop.GetActiveMonitor(DesktopInterop.VirtualScreen) ?? newMonitors[0];
                                profile.AbsoluteModeSettings.Display = HyprlandDisplayInterop.ToAreaSettings(fallback);
                            }
                        }
                    }

                    UpdateProfilesDisplay(settings);
                    if (baseSettings != null)
                    {
                        UpdateProfilesDisplay(baseSettings);
                    }

                    _previousMonitors = newMonitors;

                    // Pass isAppProfileUpdate: false so OutputModes are reconstructed and BaseSettings is refreshed
                    await _daemon.SetSettings(settings, false).ConfigureAwait(false);
                    await _daemon.ForceResynchronize().ConfigureAwait(false);
                    Log.Write("AppProfileMonitor", $"Display mapping updated for {newMonitors.Length} monitor(s).", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, LogLevel.Error);
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

            if (_monitorChangeDebounceCts != null)
            {
                _monitorChangeDebounceCts.Cancel();
                _monitorChangeDebounceCts.Dispose();
                _monitorChangeDebounceCts = null;
            }

            if (_layerTracker != null)
            {
                _layerTracker.LayerHoverStateChanged -= OnLayerHoverStateChanged;
                _layerTracker.Dispose();
                _layerTracker = null;
            }

            _profileLock.Dispose();
        }
    }
}
