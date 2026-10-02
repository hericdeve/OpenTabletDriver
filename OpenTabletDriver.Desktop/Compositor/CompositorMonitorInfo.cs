namespace OpenTabletDriver.Desktop.Compositor
{
    public class CompositorMonitorInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public float Scale { get; set; } = 1.0f;
        public bool IsFocused { get; set; }

        public override string ToString() => $"{Name} ({Width}x{Height} @ {X},{Y})";
    }
}
