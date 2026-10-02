using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Compositor;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName("Workspace Control")]
    public class WorkspaceControlBinding : IStateBinding
    {
        [Resolved]
        public IDriverDaemon? Daemon { get; set; }

        [Property("Action"), ToolTip("Action to perform (Focus or Move Window)")]
        [PropertyValidated(nameof(ValidateAction))]
        public string Action { get; set; } = "Focus Workspace";

        [Property("Target"), ToolTip("Target Workspace (1, 2, 3, 4, 5, Next, Previous, Special)")]
        public string Target { get; set; } = "1";

        [BooleanProperty("Follow Focus", "Switch active workspace after moving window")]
        public bool FollowFocus { get; set; } = true;

        public static bool ValidateAction(string action)
        {
            return action == "Focus Workspace" || action == "Move Window to Workspace";
        }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            _ = Task.Run(async () =>
            {
                var provider = CompositorManager.ActiveProvider;
                if (!provider.IsAvailable)
                    return;

                var target = Target?.Trim();
                if (string.IsNullOrEmpty(target))
                    return;

                if (target.Equals("Next", System.StringComparison.OrdinalIgnoreCase))
                {
                    var current = await provider.GetActiveWorkspaceAsync();
                    if (current != null && int.TryParse(current.Id, out int curId))
                        target = (curId + 1).ToString();
                    else
                        target = "m+1";
                }
                else if (target.Equals("Previous", System.StringComparison.OrdinalIgnoreCase))
                {
                    var current = await provider.GetActiveWorkspaceAsync();
                    if (current != null && int.TryParse(current.Id, out int curId) && curId > 1)
                        target = (curId - 1).ToString();
                    else
                        target = "m-1";
                }

                if (Action == "Move Window to Workspace")
                {
                    await provider.MoveWindowToWorkspaceAsync(target, FollowFocus);
                }
                else
                {
                    await provider.FocusWorkspaceAsync(target);
                }
            });
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }
    }
}
