using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eto.Forms;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.UX.RPC
{
    public class DaemonRpcClient : RpcClient<IDriverDaemon>
    {
        public DaemonRpcClient(string pipeName) : base(pipeName)
        {
        }

        public event EventHandler<LogMessage>? Message;
        public event EventHandler<DebugReportData>? DeviceReport;
        public event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;
        public event EventHandler? Resynchronize;
        public event EventHandler<HudShowRequest>? ShowHudRequested;
        public event EventHandler<HudUpdateRequest>? UpdateHudRequested;
        public event EventHandler? DismissHudRequested;

        protected override void OnConnected()
        {
            Debug.Assert(IsConnected, $"{nameof(OnConnected)} called without being connected");
            base.OnConnected();

            Instance.Message += (sender, e) =>
                Application.Instance.AsyncInvoke(() => Message?.Invoke(sender, e));
            Instance.DeviceReport += (sender, e) =>
                Application.Instance.AsyncInvoke(() => DeviceReport?.Invoke(sender, e));
            Instance.TabletsChanged += (sender, e) =>
                Application.Instance.AsyncInvoke(() => TabletsChanged?.Invoke(sender, e));
            Instance.Resynchronize += (sender, e) =>
                Application.Instance.AsyncInvoke(() => Resynchronize?.Invoke(sender, e));
            Instance.ShowHudRequested += (sender, e) =>
                Application.Instance.AsyncInvoke(() => ShowHudRequested?.Invoke(sender, e));
            Instance.UpdateHudRequested += (sender, e) =>
                Application.Instance.AsyncInvoke(() => UpdateHudRequested?.Invoke(sender, e));
            Instance.DismissHudRequested += (sender, e) =>
                Application.Instance.AsyncInvoke(() => DismissHudRequested?.Invoke(sender, e));
        }
    }
}
