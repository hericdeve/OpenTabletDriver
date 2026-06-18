using System;

namespace OpenTabletDriver.Desktop.Interop.AppProfiler
{
    public struct TrackedLayer
    {
        public string Namespace;
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public bool Contains(int x, int y)
        {
            return x >= X && x <= X + Width && y >= Y && y <= Y + Height;
        }
    }
}
