using System.Collections.Generic;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletDriver.Desktop.Hud
{
    public class HudConfiguration
    {
        public HudFormFactor FormFactor { get; set; } = HudFormFactor.RadialMenu;
        public HudThemeStyle ThemeStyle { get; set; } = HudThemeStyle.Translucent;
        public string FontFamily { get; set; } = "Sans";
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<HudItem> Items { get; set; } = new();
        public float Radius { get; set; } = 130f;
        public float DeadzoneRadius { get; set; } = 35f;
        public bool RightHanded { get; set; } = true;
        public bool Pinned { get; set; } = false;
        public float Opacity { get; set; } = 0.95f;
        public bool KeepCursorAnchored { get; set; } = true;
        public bool IsSubMenu { get; set; } = false;
        public int SubMenuHoverDelayMs { get; set; } = 250;

        public static HudConfiguration GetDefaults()
        {
            var config = new HudConfiguration
            {
                FormFactor = HudFormFactor.RadialMenu,
                ThemeStyle = HudThemeStyle.Translucent,
                FontFamily = "Sans",
                Radius = 130f,
                DeadzoneRadius = 35f,
                RightHanded = true,
                Pinned = false,
                Opacity = 0.95f,
                KeepCursorAnchored = true
            };
            config.Items.AddRange(new[]
            {
                new HudItem
                {
                    Label = "Undo",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "Control+Z" },
                    Binding = new PluginSettingStore(new MultiKeyBinding { Keys = "Control+Z" })
                },
                new HudItem
                {
                    Label = "Redo",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "Control+Y" },
                    Binding = new PluginSettingStore(new MultiKeyBinding { Keys = "Control+Y" })
                },
                new HudItem
                {
                    Label = "Brush",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "B" },
                    Binding = new PluginSettingStore(new MultiKeyBinding { Keys = "B" })
                },
                new HudItem
                {
                    Label = "Eraser",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "E" },
                    Binding = new PluginSettingStore(new MultiKeyBinding { Keys = "E" })
                },
                new HudItem
                {
                    Label = "Display",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.DriverCommand, Value = "DisplayToggle" },
                    Binding = new PluginSettingStore(new HyprlandMonitorCycleBinding())
                },
                new HudItem
                {
                    Label = "Precision",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.DriverCommand, Value = "PrecisionMode" },
                    Binding = new PluginSettingStore(new PrecisionModeBinding { Mode = "Toggle", Sensitivity = 30 })
                },
                new HudItem
                {
                    Label = "Pan/Scroll",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.DriverCommand, Value = "PanScroll" },
                    Binding = new PluginSettingStore(new PanScrollBinding())
                },
                new HudItem
                {
                    Label = "Save",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "Control+S" },
                    Binding = new PluginSettingStore(new MultiKeyBinding { Keys = "Control+S" })
                }
            });
            return config;
        }
    }
}
