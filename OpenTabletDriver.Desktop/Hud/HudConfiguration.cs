using System.Collections.Generic;
using Newtonsoft.Json;

namespace OpenTabletDriver.Desktop.Hud
{
    public class HudConfiguration
    {
        public HudFormFactor FormFactor { get; set; } = HudFormFactor.RadialMenu;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<HudItem> Items { get; set; } = new();
        public float Radius { get; set; } = 130f;
        public float DeadzoneRadius { get; set; } = 35f;
        public bool RightHanded { get; set; } = true;
        public bool Pinned { get; set; } = false;
        public float Opacity { get; set; } = 0.95f;
        public bool KeepCursorAnchored { get; set; } = true;

        public static HudConfiguration GetDefaults()
        {
            var config = new HudConfiguration
            {
                FormFactor = HudFormFactor.RadialMenu,
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
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "Control+Z" }
                },
                new HudItem
                {
                    Label = "Redo",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "Control+Y" }
                },
                new HudItem
                {
                    Label = "Brush",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "B" }
                },
                new HudItem
                {
                    Label = "Eraser",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "E" }
                },
                new HudItem
                {
                    Label = "Display",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.DriverCommand, Value = "DisplayToggle" }
                },
                new HudItem
                {
                    Label = "Precision",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.DriverCommand, Value = "PrecisionMode" }
                },
                new HudItem
                {
                    Label = "Pan/Scroll",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.DriverCommand, Value = "PanScroll" }
                },
                new HudItem
                {
                    Label = "Save",
                    Icon = "",
                    Action = new HudAction { Type = HudActionType.KeySequence, Value = "Control+S" }
                }
            });
            return config;
        }
    }
}
