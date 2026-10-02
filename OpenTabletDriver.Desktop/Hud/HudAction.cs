namespace OpenTabletDriver.Desktop.Hud
{
    public class HudAction
    {
        public HudActionType Type { get; set; } = HudActionType.KeySequence;
        public string? Value { get; set; }
        public string? SecondaryValue { get; set; }

        public override string ToString()
        {
            return Type switch
            {
                HudActionType.KeySequence => $"Key: {Value}",
                HudActionType.DriverCommand => $"Command: {Value} ({SecondaryValue})",
                HudActionType.MouseClick => $"Mouse: {Value}",
                HudActionType.ShellCommand => $"Shell: {Value}",
                HudActionType.WorkspaceLayer => string.IsNullOrWhiteSpace(Value) ? "Switch Workspace" : $"Workspace: {Value}",
                HudActionType.MoveWindowWorkspaceLayer => string.IsNullOrWhiteSpace(Value) ? "Move Window to Workspace" : $"Move -> WS {Value}",
                _ => "None"
            };
        }
    }
}
