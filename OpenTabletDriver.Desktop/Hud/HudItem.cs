using System;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletDriver.Desktop.Hud
{
    public class HudItem
    {
        public string Label { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Color { get; set; }

        [JsonProperty("Action")]
        public HudAction Action { get; set; } = new();

        [JsonProperty("Binding")]
        public PluginSettingStore? Binding { get; set; }

        [JsonIgnore]
        public bool IsSubLayer =>
            (Action?.Type == HudActionType.WorkspaceLayer && string.IsNullOrWhiteSpace(Action?.Value))
            || (Action?.Type == HudActionType.MoveWindowWorkspaceLayer && string.IsNullOrWhiteSpace(Action?.Value))
            || (Binding?.Path?.Contains("CompositorWorkspaceHudBinding") == true && string.IsNullOrWhiteSpace(Action?.Value))
            || (Binding?.Path?.Contains("CompositorMoveWindowHudBinding") == true && string.IsNullOrWhiteSpace(Action?.Value));

        public PluginSettingStore? GetEffectiveBinding()
        {
            if (Binding != null)
                return Binding;

            if (Action == null)
                return null;

            return Action.Type switch
            {
                HudActionType.WorkspaceLayer when string.IsNullOrWhiteSpace(Action.Value) =>
                    new PluginSettingStore(new CompositorWorkspaceHudBinding()),
                HudActionType.WorkspaceLayer when !string.IsNullOrWhiteSpace(Action.Value) =>
                    new PluginSettingStore(new WorkspaceControlBinding { Target = Action.Value, Action = "Focus Workspace" }),
                HudActionType.MoveWindowWorkspaceLayer when string.IsNullOrWhiteSpace(Action.Value) =>
                    new PluginSettingStore(new CompositorMoveWindowHudBinding()),
                HudActionType.MoveWindowWorkspaceLayer when !string.IsNullOrWhiteSpace(Action.Value) =>
                    new PluginSettingStore(new WorkspaceControlBinding { Target = Action.Value, Action = "Move Window to Workspace" }),
                HudActionType.KeySequence when !string.IsNullOrWhiteSpace(Action.Value) =>
                    new PluginSettingStore(new MultiKeyBinding { Keys = Action.Value }),
                HudActionType.DriverCommand when Action.Value == "DisplayToggle" =>
                    new PluginSettingStore(new HyprlandMonitorCycleBinding()),
                HudActionType.DriverCommand when Action.Value == "PrecisionMode" =>
                    new PluginSettingStore(new PrecisionModeBinding { Mode = "Toggle" }),
                HudActionType.DriverCommand when Action.Value == "PanScroll" =>
                    new PluginSettingStore(new PanScrollBinding()),
                HudActionType.DriverCommand when Action.Value == "Preset" =>
                    new PluginSettingStore(new PresetBinding { Preset = Action.SecondaryValue ?? "" }),
                HudActionType.MouseClick when !string.IsNullOrWhiteSpace(Action.Value) =>
                    new PluginSettingStore(new MouseBinding { Button = Action.Value }),
                HudActionType.Tool when !string.IsNullOrWhiteSpace(Action.Value) =>
                    new PluginSettingStore(new ToolBinding { Tool = Action.Value }),
                _ => null
            };
        }

        public void SyncActionFromBinding()
        {
            if (Binding == null)
            {
                Action = new HudAction();
                return;
            }

            if (Binding.Path == typeof(MultiKeyBinding).FullName || Binding.Path == typeof(KeyBinding).FullName)
            {
                Action = new HudAction
                {
                    Type = HudActionType.KeySequence,
                    Value = Binding["Keys"].GetValue<string>() ?? Binding["Key"].GetValue<string>()
                };
            }
            else if (Binding.Path == typeof(PrecisionModeBinding).FullName)
            {
                Action = new HudAction
                {
                    Type = HudActionType.DriverCommand,
                    Value = "PrecisionMode"
                };
            }
            else if (Binding.Path == typeof(PanScrollBinding).FullName)
            {
                Action = new HudAction
                {
                    Type = HudActionType.DriverCommand,
                    Value = "PanScroll"
                };
            }
            else if (Binding.Path == typeof(HyprlandMonitorCycleBinding).FullName)
            {
                Action = new HudAction
                {
                    Type = HudActionType.DriverCommand,
                    Value = "DisplayToggle"
                };
            }
            else if (Binding.Path == typeof(CompositorWorkspaceHudBinding).FullName || Binding.Path?.Contains("CompositorWorkspaceHudBinding") == true)
            {
                Action = new HudAction
                {
                    Type = HudActionType.WorkspaceLayer,
                    Value = null
                };
            }
            else if (Binding.Path == typeof(CompositorMoveWindowHudBinding).FullName || Binding.Path?.Contains("CompositorMoveWindowHudBinding") == true)
            {
                Action = new HudAction
                {
                    Type = HudActionType.MoveWindowWorkspaceLayer,
                    Value = null
                };
            }
            else if (Binding.Path == typeof(PresetBinding).FullName)
            {
                Action = new HudAction
                {
                    Type = HudActionType.DriverCommand,
                    Value = "Preset",
                    SecondaryValue = Binding["Preset"].GetValue<string>()
                };
            }
            else if (Binding.Path == typeof(MouseBinding).FullName)
            {
                Action = new HudAction
                {
                    Type = HudActionType.MouseClick,
                    Value = Binding["Button"].GetValue<string>()
                };
            }
            else if (Binding.Path == typeof(ToolBinding).FullName)
            {
                Action = new HudAction
                {
                    Type = HudActionType.Tool,
                    Value = Binding["Tool"].GetValue<string>()
                };
            }
        }

        public override string ToString() => $"{Label} [{Icon}] -> {Binding?.GetHumanReadableString() ?? Action.ToString()}";
    }
}
