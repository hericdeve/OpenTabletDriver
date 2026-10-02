using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Diagnostics;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Desktop.Updater;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Contracts
{
    public interface IDriverDaemon
    {
        event EventHandler<LogMessage>? Message;
        event EventHandler<DebugReportData>? DeviceReport;
        event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;
        event EventHandler? Resynchronize;
        event EventHandler<HudShowRequest>? ShowHudRequested;
        event EventHandler<HudUpdateRequest>? UpdateHudRequested;
        event EventHandler? DismissHudRequested;

        Task WriteMessage(LogMessage message);

        Task LoadPlugins();
        Task<bool> InstallPlugin(string filePath);
        Task<bool> UninstallPlugin(string friendlyName);
        Task<bool> DownloadPlugin(PluginMetadata metadata);

        Task<IEnumerable<SerializedDeviceEndpoint>> GetDevices();

        Task<IEnumerable<TabletReference>> GetTablets();
        Task<IEnumerable<TabletReference>> DetectTablets();

        Task SetSettings(Settings settings);
        Task<Settings> GetSettings();
        Task ResetSettings();

        Task SetAppProfilerSettings(AppProfilerSettings settings);
        Task<AppProfilerSettings> GetAppProfilerSettings();

        Task<AppInfo> GetApplicationInfo();

        Task SetTabletDebug(bool isEnabled);
        Task<string> RequestDeviceString(int vendorID, int productID, int index);

        Task<IEnumerable<LogMessage>> GetCurrentLog();
        Task<DiagnosticInfo> GetDiagnosticInfo();

        Task<SerializedUpdateInfo?> CheckForUpdates();
        Task InstallUpdate();

        Task ForceResynchronize();

        Task TriggerHudShow(HudShowRequest request);
        Task TriggerHudUpdate(HudUpdateRequest request);
        Task TriggerHudDismiss();
        Task ConfirmHudSelection(Vector2? finalPosition = null);
        Task<IReadOnlyList<Compositor.WorkspaceInfo>> GetCompositorWorkspaces();
        Task SwitchToWorkspaceSubLayer(bool isMoveWindow = false);
        Task RestoreRootHudLayer();
        Task<bool> FocusCompositorWorkspace(string workspaceId);
        Task<bool> MoveWindowToCompositorWorkspace(string workspaceId, bool followFocus = true);
        Task<bool> FocusCompositorWindow(Compositor.WindowDirection direction);
        Task<bool> MoveCompositorWindow(Compositor.WindowDirection direction);
        Task ExecuteHudAction(HudAction action);
        Task ExecuteHudItem(HudItem item);
        Task ExecuteBinding(PluginSettingStore store);

        Task<bool> IsPrecisionModeActive();
        Task TogglePrecisionMode();

        Task<string?> GetActiveWindowClass();
        Task<string?> GetActiveWindowTitle();
        Task<AppProfiler.ActiveAppProfileContext?> GetActiveAppProfileContext();
    }
}
