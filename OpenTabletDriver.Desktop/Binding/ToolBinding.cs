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
    public class ToolBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Tool Action";
        private const char KEYS_SPLITTER = '+';

        private static readonly string[] _validModes = { "Hold", "Tap / Toggle" };
        private string[]? _activeKeys;

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

        /// <summary>
        /// Optional configuration override for unit testing or embedding.
        /// </summary>
        public ContextualToolsConfiguration? CustomConfiguration { get; set; }

        public static IEnumerable<string> ValidModes => _validModes;

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
            if (_activeKeys != null && _activeKeys.Length > 0)
            {
                var keyboard = Keyboard ?? DesktopInterop.VirtualKeyboard;
                keyboard?.Release(_activeKeys);
                _activeKeys = null;
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

        public override string ToString() => $"{PLUGIN_NAME}: {Tool} ({Mode})";
    }
}
