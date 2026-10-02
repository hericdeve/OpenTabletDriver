using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Compositor
{
    public interface ICompositorProvider
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }

        Task<IReadOnlyList<WorkspaceInfo>> GetWorkspacesAsync();
        Task<WorkspaceInfo?> GetActiveWorkspaceAsync();
        Task<bool> FocusWorkspaceAsync(string workspaceIdOrName);
        Task<bool> MoveWindowToWorkspaceAsync(string workspaceIdOrName, bool followFocus = true);

        Task<bool> FocusWindowAsync(WindowDirection direction);
        Task<bool> MoveWindowAsync(WindowDirection direction);
        Task<bool> ToggleWindowFloatAsync();
        Task<bool> ToggleWindowFullscreenAsync();
        Task<WindowInfo?> GetActiveWindowAsync();

        Task<IReadOnlyList<CompositorMonitorInfo>> GetMonitorsAsync();
        Task<bool> CycleMonitorAsync(bool forward = true);
        Task<bool> MapTabletToActiveWindowAsync(TabletReference tablet);
    }
}
