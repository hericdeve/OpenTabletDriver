using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Compositor;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName("Workspace Switcher (HUD Layer)"), HudOnlyBinding]
    public class CompositorWorkspaceHudBinding : IStateBinding
    {
        public void Press(TabletReference tablet, IDeviceReport report)
        {
            // When pressed as a normal binding, focus the next workspace
            _ = Task.Run(async () =>
            {
                var provider = CompositorManager.ActiveProvider;
                if (provider.IsAvailable)
                {
                    var workspaces = await provider.GetWorkspacesAsync();
                    if (workspaces.Count > 0)
                    {
                        int currentIndex = workspaces.ToList().FindIndex(w => w.IsActive);
                        int nextIndex = (currentIndex + 1) % workspaces.Count;
                        await provider.FocusWorkspaceAsync(workspaces[nextIndex].Id);
                    }
                }
            });
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }
    }

    [PluginName("Move Window to Workspace (HUD Layer)"), HudOnlyBinding]
    public class CompositorMoveWindowHudBinding : IStateBinding
    {
        public void Press(TabletReference tablet, IDeviceReport report)
        {
            // When pressed as a normal binding, move window to next workspace
            _ = Task.Run(async () =>
            {
                var provider = CompositorManager.ActiveProvider;
                if (provider.IsAvailable)
                {
                    var workspaces = await provider.GetWorkspacesAsync();
                    if (workspaces.Count > 0)
                    {
                        int currentIndex = workspaces.ToList().FindIndex(w => w.IsActive);
                        int nextIndex = (currentIndex + 1) % workspaces.Count;
                        await provider.MoveWindowToWorkspaceAsync(workspaces[nextIndex].Id, followFocus: true);
                    }
                }
            });
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }
    }
}
