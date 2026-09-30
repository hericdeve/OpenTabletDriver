namespace OpenTabletDriver.Desktop.Hud
{
    public class HudItem
    {
        public string Label { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Color { get; set; }
        public HudAction Action { get; set; } = new();

        public override string ToString() => $"{Label} [{Icon}] -> {Action}";
    }
}
