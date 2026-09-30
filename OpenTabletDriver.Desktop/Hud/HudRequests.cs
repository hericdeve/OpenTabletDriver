using System.Numerics;

namespace OpenTabletDriver.Desktop.Hud
{
    public class HudShowRequest
    {
        public Vector2 CursorPosition { get; set; }
        public HudConfiguration Configuration { get; set; } = new();
    }

    public class HudUpdateRequest
    {
        public Vector2 CursorPosition { get; set; }
    }

    public class HudReleaseRequest
    {
        public Vector2 CursorPosition { get; set; }
        public bool IsFlick { get; set; }
    }
}
