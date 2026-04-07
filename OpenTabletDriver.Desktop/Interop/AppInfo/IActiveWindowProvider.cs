using System;

namespace OpenTabletDriver.Desktop.Interop.AppProfiler
{
    public interface IActiveWindowProvider
    {
        bool IsSupported { get; }
        void Start();
        void Stop();
        event EventHandler<ActiveWindowChangedEventArgs> ActiveWindowChanged;
    }

    public class ActiveWindowChangedEventArgs : EventArgs
    {
        public string WindowClass { get; }
        public string WindowTitle { get; }

        public ActiveWindowChangedEventArgs(string windowClass, string windowTitle)
        {
            WindowClass = windowClass;
            WindowTitle = windowTitle;
        }
    }
}
