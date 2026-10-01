using System.Numerics;

namespace OpenTabletDriver.Desktop.Hud
{
    public class HudShowRequest
    {
        public Vector2 CursorPosition { get; set; }
        public HudConfiguration? Configuration { get; set; }
    }

    public class HudUpdateRequest
    {
        public Vector2 CursorPosition { get; set; }
        public int HoveredSlice { get; set; } = -1;
    }

    public class HudReleaseRequest
    {
        public Vector2 CursorPosition { get; set; }
        public bool IsFlick { get; set; }
    }
}
