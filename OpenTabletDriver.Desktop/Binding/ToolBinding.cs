using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Tools;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class ToolBinding : IStateBinding, IContinuousBinding
    {
        private const string PLUGIN_NAME = "Tool Action";
        private const char KEYS_SPLITTER = '+';

        private static readonly string[] _validModes = { "Hold", "Tap / Toggle" };
        private static readonly string[] _validLiftTriggers = { "Button Release", "Pen Tip Lift", "Either" };

        private string[]? _activeKeys;
        private bool _wasTipDown;
        private bool _isHeld;
        private bool _liftActionFired;

        [Resolved]
        public IVirtualKeyboard? Keyboard { get; set; }

        [Resolved]
        public IActiveAppContext? AppContext { get; set; }

        [Resolved]
        public IDriverDaemon? Daemon { get; set; }

        [Property("Tool"), PropertyValidated(nameof(ValidTools))]
        public string? Tool { get; set; } = "Eraser";

        [Property("Mode"), PropertyValidated(nameof(ValidModes))]
        public string Mode { get; set; } = "Hold";

        [Property("On Lift Action"), PropertyValidated(nameof(ValidLiftTools))]
        [ToolTip("Optional tool or action to evoke when lifting (returning to previous tool or switching to pen/brush). Leave 'None' to disable.")]
        public string? OnLiftAction { get; set; } = "None";

        [Property("Lift Trigger"), PropertyValidated(nameof(ValidLiftTriggers))]
        [ToolTip("Specifies what constitutes 'lift': 'Button Release' (releasing held button), 'Pen Tip Lift' (lifting stylus tip off tablet), or 'Either'.")]
        public string LiftTrigger { get; set; } = "Button Release";

        /// <summary>
        /// Optional configuration override for unit testing or embedding.
        /// </summary>
        public ContextualToolsConfiguration? CustomConfiguration { get; set; }

        public static IEnumerable<string> ValidModes => _validModes;
        public static IEnumerable<string> ValidLiftTriggers => _validLiftTriggers;

        public static IEnumerable<string> ValidLiftTools
        {
            get
            {
                var list = new List<string> { "None" };
                list.AddRange(ValidTools);
                return list;
            }
        }

        public static IEnumerable<string> ValidTools
        {
            get
            {
                var tools = ContextualToolsConfiguration.GetDefaultTools();
                var toolNames = new HashSet<string>(tools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

                try
                {
                    if (File.Exists(AppInfo.Current.SettingsFile))
                    {
                        var settingsFile = new FileInfo(AppInfo.Current.SettingsFile);
                        if (Settings.TryDeserialize(settingsFile, out var s) && s?.ContextualTools?.Tools != null)
                        {
                            foreach (var tool in s.ContextualTools.Tools)
                            {
                                if (!string.IsNullOrWhiteSpace(tool.Name))
                                    toolNames.Add(tool.Name);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore fallback errors
                }

                return toolNames.ToList();
            }
        }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            _isHeld = true;
            _liftActionFired = false;
            _wasTipDown = report is ITabletReport { Pressure: > 0 };

            var config = ResolveConfiguration();
            var tool = config.FindTool(Tool);
            if (tool == null)
            {
                Log.Write(PLUGIN_NAME, $"Tool '{Tool}' not found in configuration.", LogLevel.Warning);
                return;
            }

            var windowClass = AppContext?.CurrentWindowClass;
            var keySequence = tool.ResolveKeySequence(windowClass);
            if (string.IsNullOrWhiteSpace(keySequence))
                return;

            var keys = ParseKeys(keySequence);
            if (keys.Length == 0)
                return;

            var keyboard = Keyboard ?? DesktopInterop.VirtualKeyboard;
            if (keyboard == null)
            {
                Log.Write(PLUGIN_NAME, "No virtual keyboard available.", LogLevel.Error);
                return;
            }

            if (string.Equals(Mode, "Tap / Toggle", StringComparison.OrdinalIgnoreCase))
            {
                keyboard.Press(keys);
                keyboard.Release(keys);
                _activeKeys = null;
            }
            else // Default: Hold
            {
                _activeKeys = keys;
                keyboard.Press(_activeKeys);
            }
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            _isHeld = false;

            if (_activeKeys != null && _activeKeys.Length > 0)
            {
                var keyboard = Keyboard ?? DesktopInterop.VirtualKeyboard;
                keyboard?.Release(_activeKeys);
                _activeKeys = null;
            }

            // If lift trigger is Button Release or Either (or Pen Tip Lift when tip is lifted), and lift action hasn't fired yet
            if (ShouldTriggerLiftOnRelease(report) && !_liftActionFired)
            {
                _liftActionFired = true;
                FireLiftAction();
            }
        }

        public void Update(TabletReference tablet, IDeviceReport report)
        {
            if (!_isHeld)
                return;

            if (report is ITabletReport tabletReport)
            {
                bool isTipDown = tabletReport.Pressure > 0;

                // Detect pen tip lift: was touching down, now pressure dropped to 0
                if (_wasTipDown && !isTipDown)
                {
                    if (ShouldTriggerLiftOnPenLift() && !_liftActionFired)
                    {
                        _liftActionFired = true;

                        // Release active hold keys first if still held
                        if (_activeKeys != null && _activeKeys.Length > 0)
                        {
                            var keyboard = Keyboard ?? DesktopInterop.VirtualKeyboard;
                            keyboard?.Release(_activeKeys);
                            _activeKeys = null;
                        }

                        FireLiftAction();
                    }
                }

                _wasTipDown = isTipDown;
            }
        }

        private bool ShouldTriggerLiftOnRelease(IDeviceReport report)
        {
            if (string.IsNullOrEmpty(OnLiftAction) || string.Equals(OnLiftAction, "None", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(LiftTrigger, "Button Release", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(LiftTrigger, "Either", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(LiftTrigger, "Pen Tip Lift", StringComparison.OrdinalIgnoreCase))
            {
                if (report is ITabletReport tr)
                    return tr.Pressure == 0;
                return true;
            }

            return false;
        }

        private bool ShouldTriggerLiftOnPenLift()
        {
            if (string.IsNullOrEmpty(OnLiftAction) || string.Equals(OnLiftAction, "None", StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(LiftTrigger, "Pen Tip Lift", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(LiftTrigger, "Either", StringComparison.OrdinalIgnoreCase);
        }

        private void FireLiftAction()
        {
            if (string.IsNullOrEmpty(OnLiftAction) || string.Equals(OnLiftAction, "None", StringComparison.OrdinalIgnoreCase))
                return;

            var config = ResolveConfiguration();
            var liftTool = config.FindTool(OnLiftAction);
            if (liftTool == null)
                return;

            var windowClass = AppContext?.CurrentWindowClass;
            var keySequence = liftTool.ResolveKeySequence(windowClass);
            if (string.IsNullOrWhiteSpace(keySequence))
                return;

            var keys = ParseKeys(keySequence);
            if (keys.Length == 0)
                return;

            var keyboard = Keyboard ?? DesktopInterop.VirtualKeyboard;
            if (keyboard != null)
            {
                keyboard.Press(keys);
                keyboard.Release(keys);
            }
        }

        private ContextualToolsConfiguration ResolveConfiguration()
        {
            if (CustomConfiguration != null)
                return CustomConfiguration;

            // In daemon process
            try
            {
                var daemonType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.FullName == "OpenTabletDriver.Daemon.DriverDaemon");

                if (daemonType != null)
                {
                    var prop = daemonType.GetProperty("ActiveInstance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var daemonInstance = prop?.GetValue(null);
                    if (daemonInstance != null)
                    {
                        var settingsProp = daemonType.GetProperty("Settings");
                        var settings = settingsProp?.GetValue(daemonInstance) as Settings;
                        if (settings?.ContextualTools != null)
                            return settings.ContextualTools;
                    }
                }
            }
            catch
            {
                // Ignore reflection errors
            }

            // Fallback to reading settings file
            try
            {
                if (File.Exists(AppInfo.Current.SettingsFile))
                {
                    var settingsFile = new FileInfo(AppInfo.Current.SettingsFile);
                    if (Settings.TryDeserialize(settingsFile, out var s) && s?.ContextualTools != null)
                        return s.ContextualTools;
                }
            }
            catch
            {
                // Ignore file read errors
            }

            return ContextualToolsConfiguration.GetDefaults();
        }

        private string[] ParseKeys(string str)
        {
            var keyboard = Keyboard ?? DesktopInterop.VirtualKeyboard;
            var parts = str.Split(KEYS_SPLITTER, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (keyboard != null && keyboard.SupportedKeys.Any())
            {
                return parts.Where(k => keyboard.SupportedKeys.Contains(k)).ToArray();
            }
            return parts;
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(OnLiftAction) && !string.Equals(OnLiftAction, "None", StringComparison.OrdinalIgnoreCase))
                return $"{PLUGIN_NAME}: {Tool} ({Mode}, Lift: {OnLiftAction})";
            return $"{PLUGIN_NAME}: {Tool} ({Mode})";
        }
    }
}
