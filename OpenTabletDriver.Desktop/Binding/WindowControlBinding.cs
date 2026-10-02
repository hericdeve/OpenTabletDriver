using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Compositor;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName("Window Control")]
    public class WindowControlBinding : IStateBinding
    {
        [Resolved]
        public IDriverDaemon? Daemon { get; set; }

        [Property("Action"), ToolTip("Window Action (Focus Window, Move Window, Toggle Float, Toggle Fullscreen)")]
        public string Action { get; set; } = "Focus Window";

        [Property("Direction"), ToolTip("Direction for focus or move (Left, Right, Up, Down, Next, Previous)")]
        public string Direction { get; set; } = "Next";

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            _ = Task.Run(async () =>
            {
                var provider = CompositorManager.ActiveProvider;
                if (!provider.IsAvailable)
                    return;

                if (Action == "Toggle Float")
                {
                    await provider.ToggleWindowFloatAsync();
                    return;
                }

                if (Action == "Toggle Fullscreen")
                {
                    await provider.ToggleWindowFullscreenAsync();
                    return;
                }

                if (!System.Enum.TryParse<WindowDirection>(Direction, true, out var dir))
                    dir = WindowDirection.Next;

                if (Action == "Move Window")
                {
                    await provider.MoveWindowAsync(dir);
                }
                else
                {
                    await provider.FocusWindowAsync(dir);
                }
            });
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }
    }
}
