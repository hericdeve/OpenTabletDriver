using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace OpenTabletDriver.Desktop.Compositor
{
    public static class CompositorManager
    {
        private static readonly List<ICompositorProvider> _providers = new()
        {
            new HyprlandCompositorProvider()
        };

        private static ICompositorProvider? _cachedActive;

        public static IReadOnlyList<ICompositorProvider> Providers => _providers;

        public static ICompositorProvider GetActiveProvider(string? preferredType = "Auto")
        {
            if (!string.IsNullOrWhiteSpace(preferredType) && !preferredType.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                var matched = _providers.FirstOrDefault(p => p.Id.Equals(preferredType, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                    return matched;
            }

            var detected = _providers.FirstOrDefault(p => p.IsAvailable);
            return detected ?? FallbackProvider;
        }

        public static ICompositorProvider ActiveProvider => GetActiveProvider("Auto");

        public static readonly ICompositorProvider FallbackProvider = new NullCompositorProvider();

        private class NullCompositorProvider : ICompositorProvider
        {
            public string Id => "none";
            public string DisplayName => "None / Generic";
            public bool IsAvailable => false;

            public System.Threading.Tasks.Task<IReadOnlyList<WorkspaceInfo>> GetWorkspacesAsync() =>
                System.Threading.Tasks.Task.FromResult<IReadOnlyList<WorkspaceInfo>>(Array.Empty<WorkspaceInfo>());

            public System.Threading.Tasks.Task<WorkspaceInfo?> GetActiveWorkspaceAsync() =>
                System.Threading.Tasks.Task.FromResult<WorkspaceInfo?>(null);

            public System.Threading.Tasks.Task<bool> FocusWorkspaceAsync(string workspaceIdOrName) =>
                System.Threading.Tasks.Task.FromResult(false);

            public System.Threading.Tasks.Task<bool> MoveWindowToWorkspaceAsync(string workspaceIdOrName, bool followFocus = true) =>
                System.Threading.Tasks.Task.FromResult(false);

            public System.Threading.Tasks.Task<bool> FocusWindowAsync(WindowDirection direction) =>
                System.Threading.Tasks.Task.FromResult(false);

            public System.Threading.Tasks.Task<bool> MoveWindowAsync(WindowDirection direction) =>
                System.Threading.Tasks.Task.FromResult(false);

            public System.Threading.Tasks.Task<bool> ToggleWindowFloatAsync() =>
                System.Threading.Tasks.Task.FromResult(false);

            public System.Threading.Tasks.Task<bool> ToggleWindowFullscreenAsync() =>
                System.Threading.Tasks.Task.FromResult(false);

            public System.Threading.Tasks.Task<WindowInfo?> GetActiveWindowAsync() =>
                System.Threading.Tasks.Task.FromResult<WindowInfo?>(null);

            public System.Threading.Tasks.Task<IReadOnlyList<CompositorMonitorInfo>> GetMonitorsAsync() =>
                System.Threading.Tasks.Task.FromResult<IReadOnlyList<CompositorMonitorInfo>>(Array.Empty<CompositorMonitorInfo>());

            public System.Threading.Tasks.Task<bool> CycleMonitorAsync(bool forward = true) =>
                System.Threading.Tasks.Task.FromResult(false);

            public System.Threading.Tasks.Task<bool> MapTabletToActiveWindowAsync(Plugin.Tablet.TabletReference tablet) =>
                System.Threading.Tasks.Task.FromResult(false);
        }
    }
}
