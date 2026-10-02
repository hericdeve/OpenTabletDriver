using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Reflection
{
    public class PluginSettingStore
    {
        private static readonly Type _tabletRefType = typeof(TabletReference);

        public PluginSettingStore(Type? type, bool enable = true)
        {
            Path = type?.FullName;
            Settings = type != null ? GetSettingsForType(type) : new ObservableCollection<PluginSetting>();
            Enable = enable;
        }

        public PluginSettingStore(object source, bool enable = true)
        {
            var sourceType = source.GetType();

            Path = sourceType.FullName;

            Settings = GetSettingsForType(sourceType, source);
            Enable = enable;
        }

        [JsonConstructor]
        public PluginSettingStore(string path, ObservableCollection<PluginSetting> settings)
        {
            Path = path;
            Settings = settings;
        }

        // TODO: make non-nullable or similar fix, since it never makes sense to have a null/empty path? -gonX
        public string? Path { set; get; }

        [JsonIgnore]
        public string? Name => Path != null ? AppInfo.PluginManager.GetFriendlyName(Path) : null;

        public ObservableCollection<PluginSetting> Settings { set; get; }

        public bool Enable { set; get; }

        public T? Construct<T>(TabletReference? tabletReference = null, bool trigger = true) where T : class
        {
            if (Path == null)
            {
                Log.Write($"Construct<T>", $"{nameof(Path)} is null, returning null", LogLevel.Debug);
                return null;
            }

            var obj = AppInfo.PluginManager.ConstructObject<T>(Path);
            ApplySettings(obj);
            if (trigger)
                TriggerEventMethods(obj, tabletReference);
            return obj;
        }

        public T? Construct<T>(IServiceManager provider, TabletReference? tabletReference = null) where T : class
        {
            var obj = Construct<T>(tabletReference, false);
            PluginManager.Inject(provider, obj);
            TriggerEventMethods(obj, tabletReference);
            return obj;
        }

        public static PluginSettingStore? FromPath(string? path)
        {
            var pathType = AppInfo.PluginManager.PluginTypes.FirstOrDefault(t => t.FullName == path);
            return pathType != null ? new PluginSettingStore(pathType) : null;
        }

        /// <summary>
        /// Apply <see cref="Settings"/> values for <see cref="PropertyAttribute"/> properties
        /// </summary>
        /// <param name="target">The target to apply settings for</param>
        public void ApplySettings(object? target)
        {
            if (target == null)
                return;

            var properties = from property in target.GetType().GetProperties()
                             let attrs = property.GetCustomAttributes(true)
                             where attrs.Any(attr => attr is PropertyAttribute)
                             select property;

            foreach (var setting in Settings)
            {
                if (properties.FirstOrDefault(d => d.Name == setting.Property) is PropertyInfo property)
                {
                    if (setting.HasValue)
                        property.SetValue(target, setting.GetValue(property.PropertyType));
                    else if (property.GetCustomAttribute<DefaultPropertyValueAttribute>() is DefaultPropertyValueAttribute defaults)
                        property.SetValue(target, defaults.Value);
                }
            }
        }

        private static ObservableCollection<PluginSetting> GetSettingsForType(Type targetType, object? source = null)
        {
            var settings = from property in targetType.GetProperties()
                           where property.GetCustomAttribute<PropertyAttribute>() is PropertyAttribute
                           select new PluginSetting(property, source == null ? null : property.GetValue(source));
            return new ObservableCollection<PluginSetting>(settings);
        }

        public PluginSetting this[string propertyName]
        {
            set
            {
                if (Settings.FirstOrDefault(t => t.Property == propertyName) is PluginSetting setting)
                {
                    Settings.Remove(setting);
                    Settings.Add(value);
                }
                else
                {
                    Settings.Add(value);
                }
            }
            get
            {
                var result = Settings.FirstOrDefault(s => s.Property == propertyName);
                if (result == null)
                {
                    var newSetting = new PluginSetting(propertyName, null);
                    Settings.Add(newSetting);
                    return newSetting;
                }
                return result;
            }
        }

        public PluginSetting this[PropertyInfo property]
        {
            set => this[property.Name] = value;
            get => this[property.Name];
        }

        public override string ToString() => GetHumanReadableString();

        public string GetHumanReadableString()
        {
            var name = Name ?? Path?.Split('.').Last() ?? "Unknown";

            // 1. MultiActionBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.MultiActionBinding")
            {
                var tap = GetNestedStore("TapAction");
                var dbl = GetNestedStore("DoubleClickAction");
                var hold = GetNestedStore("HoldAction");
                var lift = GetNestedStore("HoldLiftAction");
                var deep = GetNestedStore("DeepClickAction");

                var parts = new List<string>();
                if (tap != null)
                    parts.Add($"Tap: {FormatCompactAction(tap)}");
                if (dbl != null)
                    parts.Add($"Double: {FormatCompactAction(dbl)}");
                if (hold != null)
                    parts.Add($"Hold: {FormatCompactAction(hold)}");
                if (lift != null)
                    parts.Add($"Lift: {FormatCompactAction(lift)}");
                if (deep != null)
                    parts.Add($"Deep: {FormatCompactAction(deep)}");

                if (parts.Count > 0)
                    return string.Join(" • ", parts);
                return "Multi-Action (Unconfigured)";
            }

            // 2. PrecisionModeBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.PrecisionModeBinding")
            {
                var sens = Settings.FirstOrDefault(s => s.Property == "Sensitivity")?.Value?.ToString() ?? "30";
                var mode = Settings.FirstOrDefault(s => s.Property == "Mode")?.Value?.ToString() ?? "Hold";
                var resetOnLift = Settings.FirstOrDefault(s => s.Property == "Reset on Lift")?.Value?.ToString();
                var liftAction = GetNestedStore("OnLiftAction");
                string liftStr = liftAction != null ? $", Lift: {FormatCompactAction(liftAction)}" : "";

                if (string.Equals(resetOnLift, "false", StringComparison.OrdinalIgnoreCase))
                    return $"Precision Mode: {sens}% ({mode}, Fixed Center{liftStr})";
                return $"Precision Mode: {sens}% ({mode}{liftStr})";
            }

            // 3. PanScrollBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.PanScrollBinding")
            {
                var sens = Settings.FirstOrDefault(s => s.Property == "Sensitivity")?.Value?.ToString() ?? "100";
                var dir = Settings.FirstOrDefault(s => s.Property == "Direction")?.Value?.ToString();
                return dir != null && dir != "Both" ? $"Pan / Scroll ({dir}, {sens}%)" : $"Pan / Scroll ({sens}%)";
            }

            // 4. ToolBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.ToolBinding")
            {
                var tool = Settings.FirstOrDefault(s => s.Property == "Tool")?.Value?.ToString() ?? "Tool";
                return $"Tool Action: {tool}";
            }

            // 4. MultiKeyBinding / KeyBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.MultiKeyBinding" || Path == "OpenTabletDriver.Desktop.Binding.KeyBinding")
            {
                var keys = Settings.FirstOrDefault(s => s.Property == "Keys" || s.Property == "Key")?.Value?.ToString();
                if (!string.IsNullOrEmpty(keys))
                    return $"Keystroke: {keys}";
            }

            // 5. MouseBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.MouseBinding")
            {
                var btn = Settings.FirstOrDefault(s => s.Property == "Button")?.Value?.ToString();
                if (!string.IsNullOrEmpty(btn))
                    return $"Mouse: {btn}";
            }

            // 6. MouseScrollBinding / Dedicated Scroll Bindings
            if (Path == "OpenTabletDriver.Desktop.Binding.ScrollUpBinding")
                return "Scroll Up";
            if (Path == "OpenTabletDriver.Desktop.Binding.ScrollDownBinding")
                return "Scroll Down";
            if (Path == "OpenTabletDriver.Desktop.Binding.ScrollLeftBinding")
                return "Scroll Left";
            if (Path == "OpenTabletDriver.Desktop.Binding.ScrollRightBinding")
                return "Scroll Right";
            if (Path == "OpenTabletDriver.Desktop.Binding.ZoomInBinding")
                return "Zoom In";
            if (Path == "OpenTabletDriver.Desktop.Binding.ZoomOutBinding")
                return "Zoom Out";
            if (Path == "OpenTabletDriver.Desktop.Binding.ZoomBinding")
            {
                var dir = Settings.FirstOrDefault(s => s.Property == "Direction")?.Value?.ToString() ?? "In";
                return $"Zoom {dir}";
            }
            if (Path == "OpenTabletDriver.Desktop.Binding.MouseScrollBinding")
            {
                var dir = Settings.FirstOrDefault(s => s.Property == "Direction")?.Value?.ToString() ?? "Vertical";
                var invert = Settings.FirstOrDefault(s => s.Property == "Invert")?.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                return $"Scroll {dir}{(invert ? " (Inverted)" : "")}";
            }

            // 7. WindowControlBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.WindowControlBinding")
            {
                var action = Settings.FirstOrDefault(s => s.Property == "Action")?.Value?.ToString() ?? "Focus Window";
                if (action == "Toggle Float" || action == "Toggle Fullscreen")
                    return action;

                var dir = Settings.FirstOrDefault(s => s.Property == "Direction")?.Value?.ToString() ?? "Next";
                return $"{action} ({dir})";
            }

            // 8. WorkspaceControlBinding
            if (Path == "OpenTabletDriver.Desktop.Binding.WorkspaceControlBinding")
            {
                var action = Settings.FirstOrDefault(s => s.Property == "Action")?.Value?.ToString() ?? "Focus Workspace";
                var target = Settings.FirstOrDefault(s => s.Property == "Target")?.Value?.ToString() ?? "1";
                if (action == "Move Window to Workspace")
                    return $"Move to Workspace {target}";
                return $"Workspace {target}";
            }

            // 9. Generic formatted fallback
            var validSettings = Settings.Where(s => s.HasValue && s.Value != null).ToList();
            if (validSettings.Count == 0)
                return name;

            var formattedSettings = string.Join(", ", validSettings.Select(s => $"{s.Property}: {FormatSettingValue(s)}"));
            return $"{name} ({formattedSettings})";
        }

        public PluginSettingStore? GetNestedStore(string propertyName)
        {
            var setting = Settings.FirstOrDefault(s => s.Property == propertyName);
            if (setting == null || !setting.HasValue || setting.Value == null)
                return null;

            if (setting.Value is JObject jObj && jObj["Path"] != null)
            {
                try
                {
                    return setting.GetValue<PluginSettingStore>();
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public static string FormatCompactAction(PluginSettingStore? store)
        {
            if (store == null)
                return "None";

            // If it's a key binding, return the key string
            var keys = store.Settings.FirstOrDefault(s => s.Property == "Keys" || s.Property == "Key")?.Value?.ToString();
            if (!string.IsNullOrEmpty(keys))
                return keys;

            // If mouse button
            var mouseBtn = store.Settings.FirstOrDefault(s => s.Property == "Button")?.Value?.ToString();
            if (!string.IsNullOrEmpty(mouseBtn))
                return $"{mouseBtn} Click";

            // If scroll bindings
            if (store.Path?.EndsWith("ScrollUpBinding") == true)
                return "Scroll Up";
            if (store.Path?.EndsWith("ScrollDownBinding") == true)
                return "Scroll Down";
            if (store.Path?.EndsWith("ScrollLeftBinding") == true)
                return "Scroll Left";
            if (store.Path?.EndsWith("ScrollRightBinding") == true)
                return "Scroll Right";
            if (store.Path?.EndsWith("MouseScrollBinding") == true)
            {
                var dir = store.Settings.FirstOrDefault(s => s.Property == "Direction")?.Value?.ToString() ?? "Vertical";
                return $"Scroll {dir}";
            }

            // If precision mode
            if (store.Path?.EndsWith("PrecisionModeBinding") == true)
            {
                var sens = store.Settings.FirstOrDefault(s => s.Property == "Sensitivity")?.Value?.ToString() ?? "30";
                var mode = store.Settings.FirstOrDefault(s => s.Property == "Mode")?.Value?.ToString();
                return mode != null ? $"Precision {sens}% ({mode})" : $"Precision {sens}%";
            }

            // If ToolBinding
            if (store.Path?.EndsWith("ToolBinding") == true)
            {
                var tool = store.Settings.FirstOrDefault(s => s.Property == "Tool")?.Value?.ToString() ?? "Tool";
                return $"Tool: {tool}";
            }

            // If PanScroll
            if (store.Path?.EndsWith("PanScrollBinding") == true)
                return "Pan/Scroll";

            // If FloatingHud
            if (store.Path?.EndsWith("FloatingHudBinding") == true)
                return "HUD";

            // If WindowControl
            if (store.Path?.EndsWith("WindowControlBinding") == true)
            {
                var action = store.Settings.FirstOrDefault(s => s.Property == "Action")?.Value?.ToString() ?? "Focus Window";
                if (action == "Toggle Float") return "Toggle Float";
                if (action == "Toggle Fullscreen") return "Fullscreen";
                var dir = store.Settings.FirstOrDefault(s => s.Property == "Direction")?.Value?.ToString() ?? "Next";
                return action == "Move Window" ? $"Move {dir}" : $"Focus {dir}";
            }

            // If WorkspaceControl
            if (store.Path?.EndsWith("WorkspaceControlBinding") == true)
            {
                var action = store.Settings.FirstOrDefault(s => s.Property == "Action")?.Value?.ToString() ?? "Focus Workspace";
                var target = store.Settings.FirstOrDefault(s => s.Property == "Target")?.Value?.ToString() ?? "1";
                return action == "Move Window to Workspace" ? $"Move -> WS {target}" : $"WS {target}";
            }

            // Otherwise, friendly name or class name
            return store.Name ?? store.Path?.Split('.').Last() ?? "Action";
        }

        private static string FormatSettingValue(PluginSetting s)
        {
            if (!s.HasValue || s.Value == null)
                return "null";

            if (s.Value is JObject jObj)
            {
                if (jObj["Path"] != null)
                {
                    try
                    {
                        var nested = s.GetValue<PluginSettingStore>();
                        if (nested != null)
                            return FormatCompactAction(nested);
                    }
                    catch
                    {
                    }
                }
                return jObj.ToString(Formatting.None);
            }

            if (s.Value is JArray jArr)
                return jArr.ToString(Formatting.None);

            return s.Value.ToString();
        }

        public TypeInfo? GetTypeInfo()
        {
            return AppInfo.PluginManager.PluginTypes.FirstOrDefault(t => t.FullName == Path);
        }

        public TypeInfo? GetTypeInfo<T>()
        {
            return AppInfo.PluginManager.GetChildTypes<T>().FirstOrDefault(t => t.FullName == Path);
        }

        private static void TriggerEventMethods(object? obj, TabletReference? tabletReference)
        {
            if (obj == null)
                return;

            var properties = from property in obj.GetType().GetProperties()
                             let attr = property.GetCustomAttribute<TabletReferenceAttribute>()
                             where attr != null && property.PropertyType == _tabletRefType
                             select property;

            foreach (var property in properties)
                property.SetValue(obj, tabletReference);

            var methods = from method in obj.GetType().GetMethods()
                          let attr = method.GetCustomAttribute<OnDependencyLoadAttribute>()
                          where attr != null
                          select method;

            foreach (var method in methods)
                method.Invoke(obj, Array.Empty<object>());
        }
    }
}
