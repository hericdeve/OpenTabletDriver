using System.Drawing;

namespace OpenTabletDriver.Desktop.Compositor
{
    public class WindowInfo
    {
        public string Address { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public string Workspace { get; set; } = string.Empty;
        public Rectangle Bounds { get; set; }
        public bool IsFloating { get; set; }
        public bool IsFullscreen { get; set; }
    }
}
