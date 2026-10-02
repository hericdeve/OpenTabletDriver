using System;

namespace OpenTabletDriver.Desktop.Compositor
{
    public class WorkspaceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Monitor { get; set; } = string.Empty;
        public int WindowsCount { get; set; }
        public bool IsActive { get; set; }
        public string? LastWindowTitle { get; set; }
        public System.Collections.Generic.List<string> AppClasses { get; set; } = new();

        public override string ToString() => string.IsNullOrWhiteSpace(LastWindowTitle) ? Name : $"{Name} ({LastWindowTitle})";
    }
}
