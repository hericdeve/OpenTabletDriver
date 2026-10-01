using System;

namespace OpenTabletDriver.Plugin.Platform.Pointer
{
    public interface IGestureHandler
    {
        void Zoom(float delta);
        void EndGesture();
    }
}
