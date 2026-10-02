using System;
using System.Linq;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Compositor;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX.Controls
{
    public class OsSettingsEditor : Panel
    {
        private readonly Label statusLabel;
        private readonly DropDown compositorDropDown;
        private readonly CheckBox enableCompositorCheck;
        private readonly CheckBox followFocusCheck;
        private readonly NumericStepper maxWorkspaceStepper;
        private readonly TextBox specialWsTextBox;
        private readonly GridView<WorkspaceInfo> workspacesGrid;
        private readonly Button refreshWsButton;
        private readonly Button focusWsButton;
        private readonly Button saveButton;

        public OsSettingsEditor()
        {
            var layout = new DynamicLayout
            {
                Padding = new Padding(12),
                Spacing = new Size(8, 8)
            };

            // Compositor / Environment Status
            var provider = CompositorManager.ActiveProvider;
            statusLabel = new Label
            {
                Text = provider.IsAvailable
                    ? $"● Active Compositor: {provider.DisplayName} (Connected)"
                    : "○ No supported Wayland/X11 Compositor IPC detected",
                TextColor = provider.IsAvailable ? Colors.Green : Colors.Gray
            };

            var envGroup = new GroupBox { Text = "Compositor & Shell Environment" };
            var envLayout = new DynamicLayout { Padding = new Padding(8), Spacing = new Size(6, 6) };

            compositorDropDown = new DropDown();
            compositorDropDown.Items.Add("Auto (Auto-Detect)");
            foreach (var p in CompositorManager.Providers)
                compositorDropDown.Items.Add(new ListItem { Text = p.DisplayName, Key = p.Id });

            enableCompositorCheck = new CheckBox { Text = "Enable Compositor / OS integration (workspaces, windows, overlays)" };
            followFocusCheck = new CheckBox { Text = "Follow window focus when moving window to another workspace" };

            maxWorkspaceStepper = new NumericStepper { MinValue = 3, MaxValue = 12, Value = 8 };
            specialWsTextBox = new TextBox { Text = "special:scratchpad" };

            envLayout.Add(statusLabel);
            envLayout.AddRow(new Label { Text = "Backend:" }, compositorDropDown);
            envLayout.Add(enableCompositorCheck);
            envLayout.Add(followFocusCheck);
            envLayout.AddRow(new Label { Text = "Max HUD Workspace Slots:" }, maxWorkspaceStepper);
            envLayout.AddRow(new Label { Text = "Special Workspace Name:" }, specialWsTextBox);
            envGroup.Content = envLayout;
            layout.Add(envGroup);

            // Live Workspaces Grid
            var wsGroup = new GroupBox { Text = "Active Compositor Workspaces" };
            var wsLayout = new DynamicLayout { Padding = new Padding(8), Spacing = new Size(6, 6) };

            workspacesGrid = new GridView<WorkspaceInfo>
            {
                Height = 180
            };

            workspacesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "ID",
                DataCell = new TextBoxCell { Binding = Binding.Property<WorkspaceInfo, string>(w => w.Id) }
            });

            workspacesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Name",
                DataCell = new TextBoxCell { Binding = Binding.Property<WorkspaceInfo, string>(w => w.Name) }
            });

            workspacesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Monitor",
                DataCell = new TextBoxCell { Binding = Binding.Property<WorkspaceInfo, string>(w => w.Monitor) }
            });

            workspacesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Windows",
                DataCell = new TextBoxCell { Binding = Binding.Property<WorkspaceInfo, string>(w => w.WindowsCount.ToString()) }
            });

            workspacesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Active",
                DataCell = new TextBoxCell { Binding = Binding.Property<WorkspaceInfo, string>(w => w.IsActive ? "✓ Active" : "") }
            });

            workspacesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Last Window / Title",
                DataCell = new TextBoxCell { Binding = Binding.Property<WorkspaceInfo, string>(w => w.LastWindowTitle ?? "") }
            });

            refreshWsButton = new Button { Text = "🔄 Refresh Workspaces" };
            refreshWsButton.Click += async (s, e) => await RefreshWorkspacesAsync();

            focusWsButton = new Button { Text = "🎯 Switch to Selected Workspace" };
            focusWsButton.Click += async (s, e) =>
            {
                if (workspacesGrid.SelectedItem is WorkspaceInfo ws)
                {
                    await CompositorManager.ActiveProvider.FocusWorkspaceAsync(ws.Id);
                    await RefreshWorkspacesAsync();
                }
            };

            var wsBtnLayout = new DynamicLayout { Spacing = new Size(6, 6) };
            wsBtnLayout.BeginHorizontal();
            wsBtnLayout.Add(refreshWsButton);
            wsBtnLayout.Add(focusWsButton);
            wsBtnLayout.EndHorizontal();

            wsLayout.Add(workspacesGrid);
            wsLayout.Add(wsBtnLayout);
            wsGroup.Content = wsLayout;
            layout.Add(wsGroup);

            // Save / Apply Button
            saveButton = new Button { Text = "Save OS & Compositor Settings" };
            saveButton.Click += async (s, e) => await SaveSettingsAsync();
            layout.Add(saveButton);

            Content = layout;

            LoadCurrentSettings();
            _ = RefreshWorkspacesAsync();
        }

        private void LoadCurrentSettings()
        {
            var settings = App.Current?.Settings?.CompositorSettings ?? new CompositorSettings();
            enableCompositorCheck.Checked = settings.EnableCompositorIntegration;
            followFocusCheck.Checked = settings.FollowFocusOnWindowMove;
            maxWorkspaceStepper.Value = settings.MaxHudWorkspaceSlots;
            specialWsTextBox.Text = settings.SpecialWorkspaceName ?? "special:scratchpad";

            if (settings.CompositorType == "Auto")
                compositorDropDown.SelectedIndex = 0;
            else
                compositorDropDown.SelectedKey = settings.CompositorType;
        }

        private async Task SaveSettingsAsync()
        {
            if (App.Current?.Settings == null)
                return;

            var settings = App.Current.Settings.CompositorSettings ??= new CompositorSettings();
            settings.EnableCompositorIntegration = enableCompositorCheck.Checked ?? true;
            settings.FollowFocusOnWindowMove = followFocusCheck.Checked ?? true;
            settings.MaxHudWorkspaceSlots = (int)maxWorkspaceStepper.Value;
            settings.SpecialWorkspaceName = specialWsTextBox.Text;
            settings.CompositorType = compositorDropDown.SelectedIndex == 0 ? "Auto" : (compositorDropDown.SelectedKey ?? "Auto");

            if (App.Driver?.Instance != null)
            {
                await App.Driver.Instance.SetSettings(App.Current.Settings);
            }

            MessageBox.Show("Compositor & OS settings saved successfully.", "OpenTabletDriver", MessageBoxType.Information);
        }

        private async Task RefreshWorkspacesAsync()
        {
            try
            {
                var prov = CompositorManager.ActiveProvider;
                statusLabel.Text = prov.IsAvailable
                    ? $"● Active Compositor: {prov.DisplayName} (Connected)"
                    : "○ No supported Wayland/X11 Compositor IPC detected";
                statusLabel.TextColor = prov.IsAvailable ? Colors.Green : Colors.Gray;

                var workspaces = await prov.GetWorkspacesAsync();
                Application.Instance.AsyncInvoke(() =>
                {
                    workspacesGrid.DataStore = workspaces.ToList();
                });
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }
    }
}
