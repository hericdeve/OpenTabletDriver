using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Diagnostics;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.UX.Controls;
using OpenTabletDriver.UX.Controls.Generic.Reflection;
using OpenTabletDriver.Plugin.Output;

namespace OpenTabletDriver.UX
{
    public class MainForm : DesktopForm
    {
        public MainForm()
        {
            this.DataContext = App.Current;

            // Call InitializeForm on ctor since DesktopForm.Show() won't be called on binary launch
            InitializeForm();
            InitializePlatform();

            SetTitle();
            base.Menu = ConstructLimitedMenu();
            fullMenu = ConstructMenu();

            base.Content = placeholder;

            trayIcon?.Indicator?.Show();

            saveButton = new Button(async (s, e) => await SaveSettings())
            {
                Text = "Save"
            };

            applyButton = new Button(async (s, e) => await ApplySettings())
            {
                Text = "Apply"
            };

            App.Driver.Connected += HandleDaemonConnected;
            App.Driver.Disconnected += HandleDaemonDisconnected;

            Application.Instance.InvokeAsync(ConnectToDaemon);
        }

        private async Task ConnectToDaemon()
        {
            if (Interlocked.Exchange(ref _isConnectingToDaemon, 1) == 1)
                return;

            try
            {
                while (true)
                {
                    var timeout = Task.Delay(TimeSpan.FromSeconds(15));
                    var result = await Task.WhenAny(App.Driver.Connect(), timeout);

                    if (result != timeout || App.Driver.IsConnected)
                        break; // daemon connected

                    var message = SystemInterop.CurrentPlatform switch
                    {
                        PluginPlatform.Windows =>
                            "Connecting to daemon has timed out.\nVerify that OpenTabletDriver.Daemon is running or is in the same folder as OpenTabletDriver.UX\nPress OK to retry",
                        PluginPlatform.Linux =>
                            """
                            Connecting to daemon has timed out.
                            Verify that OpenTabletDriver.Daemon is running, e.g. by starting the systemd user service (usually 'systemctl --user start opentabletdriver'), or by starting 'otd-daemon'.
                            Press OK to retry
                            """,
                        _ =>
                            "Connecting to daemon has timed out. Verify that OpenTabletDriver.Daemon is running.\nPress OK to retry"
                    };

                    var dialogResult = MessageBox.Show(this, message, "Daemon Connection Error",
                        MessageBoxButtons.OKCancel, MessageBoxType.Error);

                    if (App.Driver.IsConnected) break;

                    if (dialogResult == DialogResult.Cancel)
                        Environment.Exit(1);

                    if (App.EnableDaemonWatchdog)
                        StartDaemonWatchdog();
                }

                if (!this.SkipUpdate)
                    CheckForUpdates();
            }
            catch (Exception ex) when (IsExpectedRpcDisconnect(ex))
            {
                // The daemon disconnected mid-handshake; disconnect handler will retry.
            }
            catch (Exception ex)
            {
                ex.ShowMessageBox();
                Environment.Exit(2);
            }
            finally
            {
                Interlocked.Exchange(ref _isConnectingToDaemon, 0);
            }
        }

        private const int DEFAULT_CLIENT_WIDTH = 960;
        private const int DEFAULT_CLIENT_HEIGHT = 760;

        private readonly MenuBar fullMenu;
        private readonly Placeholder placeholder = new()
        {
            Text = "Connecting to OpenTabletDriver Daemon...",
        };

                private TrayIcon trayIcon;
        private int _isConnectingToDaemon;

        public bool SilenceDaemonShutdown { get; set; }
        public bool SkipUpdate { get; set; }

        protected override void InitializeForm()
        {
            var bounds = Screen.FromPoint(Mouse.Position).Bounds;

            if (this.WindowState != WindowState.Maximized)
            {
                var minWidth = Math.Min(DEFAULT_CLIENT_WIDTH, bounds.Width * 0.95);
                var minHeight = Math.Min(DEFAULT_CLIENT_HEIGHT, bounds.Height * 0.95);

                this.Size = new Size((int)minWidth, (int)minHeight);

                if (SystemInterop.CurrentPlatform == PluginPlatform.Windows)
                {
                    var x = Screen.WorkingArea.Center.X - (minWidth / 2);
                    var y = Screen.WorkingArea.Center.Y - (minHeight / 2);
                    this.Location = new Point((int)x, (int)y);
                }
            }
        }

        protected void InitializePlatform()
        {
            switch (SystemInterop.CurrentPlatform)
            {
                case PluginPlatform.Windows:
                    var programPath = AppInfo.ProgramDirectory;
                    var tempPath = Regex.Escape(Path.GetTempPath());
                    var regex = new Regex(@$"^{tempPath}Temp\d+.*?\.zip");

                    if (regex.IsMatch(programPath))
                    {
                        MessageBox.Show(this, $"You are running OpenTabletDriver.UX from a zip file.\n\nPlease extract the zip file to a folder then run OpenTabletDriver.UX from there.", "Error", MessageBoxType.Error);
                        Environment.Exit(1);
                    }
                    break;
                case PluginPlatform.MacOS:
                    this.Padding = 10;
                    break;
            }

            if (App.EnableTrayIcon)
            {
                trayIcon = new TrayIcon(this);
                if (WindowState == WindowState.Minimized)
                {
                    this.Visible = false;
                    this.ShowInTaskbar = false;
                }
                this.WindowStateChanged += (sender, e) =>
                {
                    switch (this.WindowState)
                    {
                        case WindowState.Normal:
                        case WindowState.Maximized:
                            this.Visible = true;
                            this.ShowInTaskbar = true;
                            break;
                        case WindowState.Minimized:
                            this.Visible = false;
                            this.ShowInTaskbar = false;
                            break;
                    }
                };
                Application.Instance.Terminating += (sender, e) => trayIcon.Dispose();
            }

            if (App.EnableDaemonWatchdog)
            {
                // Check if daemon is already active, if not then start it as a subprocess if it exists in the local path.
                StartDaemonWatchdog();

                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    App.DaemonWatchdog?.Dispose();
                    App.DaemonWatchdog = null;
                };

                this.Closing += (sender, e) =>
                {
                    App.DaemonWatchdog?.Dispose();
                    App.DaemonWatchdog = null;
                };
            }
        }

        private static void StartDaemonWatchdog()
        {
            if (Instance.Exists("OpenTabletDriver.Daemon") || !DaemonWatchdog.CanExecute) return;

            var watchdog = new DaemonWatchdog();
            watchdog.Start();
            App.DaemonWatchdog = watchdog;
        }

        private static MenuBar ConstructLimitedMenu()
        {
            var quitCommand = new Command { MenuText = "Quit", Shortcut = Application.Instance.CommonModifier | Keys.Q };
            quitCommand.Executed += (sender, e) => Application.Instance.Quit();

            var aboutCommand = new Command { MenuText = "About...", Shortcut = Keys.F1 };
            aboutCommand.Executed += (sender, e) => App.Current.AboutWindow.Show();

            var wikiUrl = new Command { MenuText = "Open Wiki..." };
            wikiUrl.Executed += (sender, e) => DesktopInterop.Open(App.WikiUrl);

            var menuBar = new MenuBar
            {
                Items =
                {
                    new ButtonMenuItem
                    {
                        Text = "&Help",
                        Items =
                        {
                            wikiUrl,
                        }
                    }
                },
                QuitItem = quitCommand,
                AboutItem = aboutCommand
            };

            return menuBar;
        }

        private MenuBar ConstructMenu()
        {
            var quitCommand = new Command { MenuText = "Quit", Shortcut = Application.Instance.CommonModifier | Keys.Q };
            quitCommand.Executed += (sender, e) => Application.Instance.Quit();

            var aboutCommand = new Command { MenuText = "About...", Shortcut = Keys.F1 };
            aboutCommand.Executed += (sender, e) => App.Current.AboutWindow.Show();

            var resetSettings = new Command { MenuText = "Reset to defaults" };
            resetSettings.Executed += async (sender, e) => await ResetSettingsDialog();

            var loadSettings = new Command { MenuText = "Load settings...", Shortcut = Application.Instance.CommonModifier | Keys.O };
            loadSettings.Executed += async (sender, e) => await LoadSettingsDialog();

            var saveSettingsAs = new Command { MenuText = "Save settings as...", Shortcut = Application.Instance.CommonModifier | Keys.Shift | Keys.S };
            saveSettingsAs.Executed += async (sender, e) => await SaveSettingsDialog();

            var saveSettings = new Command { MenuText = "Save settings", Shortcut = Application.Instance.CommonModifier | Keys.S };
            saveSettings.Executed += async (sender, e) => await SaveSettings();

            var applySettings = new Command { MenuText = "Apply settings", Shortcut = Application.Instance.CommonModifier | Keys.Enter };
            applySettings.Executed += async (sender, e) => await ApplySettings();

            var refreshPresets = new Command { MenuText = "Refresh presets" };
            refreshPresets.Executed += async (sender, e) => await RefreshPresets();

            var savePreset = new Command { MenuText = "Save as preset..." };
            savePreset.Executed += async (sender, e) => await SavePresetDialog();

            var saveAppPreset = new Command { MenuText = "Save as app preset..." };
            saveAppPreset.Executed += async (sender, e) => await SaveAppPresetDialog();

            var saveAppMode = new Command { MenuText = "Map to app mode..." };
            saveAppMode.Executed += async (sender, e) => await SaveAppModeDialog();

            var setDefaultAppPreset = new Command { MenuText = "Set default app preset..." };
            setDefaultAppPreset.Executed += async (sender, e) => await SetDefaultAppPresetDialog();

            var setDefaultAppMode = new Command { MenuText = "Set default app mode..." };
            setDefaultAppMode.Executed += async (sender, e) => await SetDefaultAppModeDialog();

            var toggleAppPresets = new CheckCommand { MenuText = "Enable app profiling" };
            var toggleSyncFocus = new CheckCommand { MenuText = "Sync focus on window change" };
            App.Current.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(App.AppProfilerSettings) && App.Current.AppProfilerSettings != null)
                {
                    toggleAppPresets.Checked = App.Current.AppProfilerSettings.EnableAppProfiler;
                    toggleSyncFocus.Checked = App.Current.AppProfilerSettings.SyncFocus;
                }
            };
            toggleAppPresets.Executed += async (sender, e) =>
            {
                if (App.Current.AppProfilerSettings is AppProfilerSettings appSettings)
                {
                    appSettings.EnableAppProfiler = toggleAppPresets.Checked;
                    await App.Driver.Instance.SetAppProfilerSettings(appSettings);
                }
            };
            toggleSyncFocus.Executed += async (sender, e) =>
            {
                if (App.Current.AppProfilerSettings is AppProfilerSettings appSettings)
                {
                    appSettings.SyncFocus = toggleSyncFocus.Checked;
                    await App.Driver.Instance.SetAppProfilerSettings(appSettings);
                }
            };
            if (App.Current.AppProfilerSettings != null)
            {
                toggleAppPresets.Checked = App.Current.AppProfilerSettings.EnableAppProfiler;
                toggleSyncFocus.Checked = App.Current.AppProfilerSettings.SyncFocus;
            }

            var detectTablet = new Command { MenuText = "Detect tablet", Shortcut = Application.Instance.CommonModifier | Keys.D };
            detectTablet.Executed += async (sender, e) => await DetectTablet();

            var showTabletDebugger = new Command { MenuText = "Tablet debugger..." };
            showTabletDebugger.Executed += (sender, e) => App.Current.DebuggerWindow.Show();

            var deviceStringReader = new Command { MenuText = "Device string reader..." };
            deviceStringReader.Executed += (sender, e) => App.Current.StringReaderWindow.Show();

            var pluginManager = new Command { MenuText = "Open Plugin Manager..." };
            pluginManager.Executed += (sender, e) => App.Current.PluginManagerWindow.Show();

            var wikiUrl = new Command { MenuText = "Open Wiki..." };
            wikiUrl.Executed += (sender, e) => DesktopInterop.Open(App.WikiUrl);

            var showGuide = new Command { MenuText = "Show guide..." };
            showGuide.Executed += (sender, e) => App.Current.StartupGreeterWindow.Show();

            var exportDiagnostics = new Command { MenuText = "Export diagnostics..." };
            exportDiagnostics.Executed += async (sender, e) => await ExportDiagnostics();

            var exportDiagnosticsToClipboard = new Command { MenuText = "Export diagnostics to Clipboard..." };
            exportDiagnosticsToClipboard.Executed += async (sender, e) => await ExportDiagnosticsToClipboard();

            var updater = new Command { MenuText = "Check for updates..." };
            updater.Executed += (sender, e) => App.Current.UpdaterWindow.Show();

            var menuBar = new MenuBar
            {
                Items =
                {
                    // File submenu
                    new ButtonMenuItem
                    {
                        Text = "&File",
                        Items =
                        {
                            loadSettings,
                            saveSettings,
                            saveSettingsAs,
                            resetSettings,
                            applySettings,
                            new SeparatorMenuItem(),
                            refreshPresets,
                            savePreset,
                            saveAppPreset,
                            saveAppMode,
                            setDefaultAppPreset,
                            setDefaultAppMode,
                            toggleAppPresets,
                            toggleSyncFocus,
                            new ButtonMenuItem
                            {
                                Text = "Presets",
                                Items =
                                {
                                    new ButtonMenuItem
                                    {
                                        Text = "No presets loaded",
                                        Enabled = false
                                    }
                                }
                            }
                        }
                    },
                    // Tablets submenu
                    new ButtonMenuItem
                    {
                        Text = "Tablets",
                        Items =
                        {
                            detectTablet,
                            showTabletDebugger,
                            deviceStringReader,
                        }
                    },
                    // Plugins submenu
                    new ButtonMenuItem
                    {
                        Text = "Plugins",
                        Items =
                        {
                            pluginManager
                        }
                    },
                    new ButtonMenuItem
                    {
                        Text = "&Help",
                        Items =
                        {
                            wikiUrl,
                            exportDiagnostics,
                            exportDiagnosticsToClipboard,
                            showGuide
                        }
                    }
                },
                ApplicationItems =
                {
                    // application (OS X) or file menu (others)
                },
                QuitItem = quitCommand,
                AboutItem = aboutCommand
            };

            switch (SystemInterop.CurrentPlatform)
            {
                case PluginPlatform.Windows:
                case PluginPlatform.MacOS:
                {
                    menuBar.Items.GetSubmenu("&Help").Items.Add(updater);
                    break;
                }
            }

            return menuBar;
        }

        private void SetTitle(IEnumerable<TabletReference> tablets = null)
        {
            string prefix = $"OpenTabletDriver v{App.Version}";
            string affix = string.Empty;

            if (tablets?.Any() ?? false)
            {
                // Limit to 3 tablets in the title
                int numTablets = Math.Min(tablets.Count(), 3);
                affix = string.Join(", ", tablets.Take(numTablets).Select(t => t.Properties.Name));
            }

            this.Title = !string.IsNullOrEmpty(affix)
                ? $"{prefix} - {affix}"
                : prefix;
        }

        // ReSharper disable once AsyncVoidMethod
        private void HandleDaemonConnected(object sender, EventArgs e) => Application.Instance.AsyncInvoke(async void () =>
        {
            var daemon = App.Driver.Instance;
            if (daemon is null)
                return;

            try
            {
                // Hook events after the instance is (re)instantiated
                Log.Output -= LogToDriver;
                Log.Output += LogToDriver;
                App.Driver.TabletsChanged -= HandleTabletsChanged;
                App.Driver.TabletsChanged += HandleTabletsChanged;
                App.Driver.Resynchronize -= HandleResynchronize;
                App.Driver.Resynchronize += HandleResynchronize;

                // Load full menu
                this.Menu = ConstructMenu();

                // Load the application information from the daemon
                AppInfo.Current = await daemon.GetApplicationInfo();

                AppInfo.PluginManager = new DesktopPluginManager();
                AppInfo.PresetManager = new PresetManager();

                // Load any new plugins
                AppInfo.PluginManager.Load();

                // Show the startup greeter
                if (!File.Exists(AppInfo.Current.SettingsFile) && this.WindowState != WindowState.Minimized)
                    App.Current.StartupGreeterWindow.Show();

                // Synchronize settings
                await SyncSettings();

                // Set window content
                this.Menu = fullMenu;
                base.Content = new TabletSwitcherPanel
                {
                    CommandsControl = new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalContentAlignment = HorizontalAlignment.Right,
                        Spacing = 5,
                        Items =
                        {
                            saveButton,
                            applyButton,
                        }
                    }
                };

                // Update preset options in File menu and tray icon
                await RefreshPresets();

                // Update title to new instance
                if (await daemon.GetTablets() is IEnumerable<TabletReference> tablets)
                    SetTitle(tablets);
            }
            catch (Exception ex) when (IsExpectedRpcDisconnect(ex))
            {
                // The daemon disconnected during startup synchronization.
            }
        });

        private async void HandleResynchronize(object sender, EventArgs e)
        {
            try
            {
                await SyncSettings();
            }
            catch (Exception ex) when (IsExpectedRpcDisconnect(ex))
            {
                // The daemon disconnected while syncing settings.
            }
        }

        private void HandleTabletsChanged(object sender, IEnumerable<TabletReference> tablets)
        {
            SetTitle(tablets);
        }

        private Button saveButton;
        private Button applyButton;

        private async void LogToDriver(object sender, LogMessage message)
        {
            if (!App.Driver.IsConnected)
                return;

            var daemon = App.Driver.Instance;
            if (daemon is null)
                return;

            try
            {
                await daemon.WriteMessage(message);
            }
            catch (Exception ex) when (IsExpectedRpcDisconnect(ex))
            {
                // The daemon disconnected while forwarding UI logs.
            }
        }

        private static bool IsExpectedRpcDisconnect(Exception ex)
        {
            if (ex is StreamJsonRpc.ConnectionLostException)
                return true;

            if (ex is ObjectDisposedException or IOException or OperationCanceledException)
                return true;

            if (ex is AggregateException aggregateException)
                return aggregateException.InnerExceptions.All(IsExpectedRpcDisconnect);

            if (ex is SocketException socketEx)
                return socketEx.SocketErrorCode is SocketError.ConnectionReset or SocketError.Shutdown or SocketError.NotConnected;

            if (ex.InnerException is not null)
                return IsExpectedRpcDisconnect(ex.InnerException);

            return false;
        }

        private void HandleDaemonDisconnected(object sender, EventArgs e)
        {
            Log.Output -= LogToDriver;
            App.Driver.TabletsChanged -= HandleTabletsChanged;
            App.Driver.Resynchronize -= HandleResynchronize;
            if (SilenceDaemonShutdown)
                return;

            // Hide all controls until reconnected
            Application.Instance.Invoke(() =>
            {
                base.Content = placeholder;
                base.Menu = null;

                Application.Instance.InvokeAsync(ConnectToDaemon).ConfigureAwait(false);
            });
        }

        private static async Task ResetSettings()
        {
            await App.Driver.Instance.ResetSettings();
            App.Current.Settings = await App.Driver.Instance.GetSettings();
        }

        private static async Task ResetSettingsDialog()
        {
            if (MessageBox.Show("Reset settings to default?", "Reset to defaults", MessageBoxButtons.OKCancel, MessageBoxType.Question) == DialogResult.Ok)
                await ResetSettings();
        }

        private static async Task SyncSettings()
        {
            App.Current.Settings = await App.Driver.Instance.GetSettings();
            App.Current.AppProfilerSettings = await App.Driver.Instance.GetAppProfilerSettings();
        }

        private async Task LoadSettingsDialog()
        {
            var fileDialog = Extensions.OpenFileDialog(
                "Load OpenTabletDriver settings...",
                Eto.EtoEnvironment.GetFolderPath(Eto.EtoSpecialFolder.Documents),
                [new FileFilter("OpenTabletDriver Settings (*.json)", ".json")]
            );

            switch (fileDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var file = new FileInfo(fileDialog.FileName);
                    if (file.Exists)
                    {
                        if (Settings.TryDeserialize(file, out var settings))
                        {
                            App.Current.Settings = settings;
                            await App.Driver.Instance.SetSettings(settings);
                        }
                        else
                        {
                            MessageBox.Show(
                                "Invalid settings file.",
                                MessageBoxType.Error
                            );
                        }
                    }
                    break;
            }
        }

        private async Task SaveSettingsDialog()
        {
            var fileDialog = Extensions.SaveFileDialog(
                "Save OpenTabletDriver settings...",
                Eto.EtoEnvironment.GetFolderPath(Eto.EtoSpecialFolder.Documents),
                [new FileFilter("OpenTabletDriver Settings (*.json)", ".json")],
                "opentabletdriver-settings.json"
            );

            switch (fileDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var file = new FileInfo(fileDialog.FileName);
                    if (App.Current.Settings is Settings settings)
                    {
                        settings.Serialize(file);
                        await ApplySettings();
                    }
                    break;
            }
        }

        private async Task SaveSettings()
        {
            DisableApplySaveButtons();

            if (App.Current.Settings is Settings settings)
            {
                if (settings.Profiles.Any(p => p.AbsoluteModeSettings.Tablet.Width + p.AbsoluteModeSettings.Tablet.Height == 0))
                {
                    var result = MessageBox.Show(
                        "Warning: Your tablet area is invalid. Saving this configuration may cause problems." + Environment.NewLine +
                        "Are you sure you want to save your configuration?",
                        MessageBoxButtons.YesNo,
                        MessageBoxType.Warning
                    );
                    if (result != DialogResult.Yes)
                        return;
                }

                var appInfo = await App.Driver.Instance.GetApplicationInfo();
                settings.Serialize(new FileInfo(appInfo.SettingsFile));
                await ApplySettings();
            }
        }

        private CancellationTokenSource _disableApplySaveButtons = new();

        private void DisableApplySaveButtons(bool disableSave = true)
        {
            Application.Instance.InvokeAsync(async () =>
            {
                await _disableApplySaveButtons.CancelAsync();
                _disableApplySaveButtons = new CancellationTokenSource();
                if (disableSave)
                    saveButton.Enabled = false;

                applyButton.Enabled = false;

                // debounce re-enabling task
                await Task.Delay(1000, _disableApplySaveButtons.Token).ContinueWith(task =>
                {
                    if (task.IsCanceled) return;

                    saveButton.Enabled = true;
                    applyButton.Enabled = true;
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }).ConfigureAwait(false);
        }

        private async Task ApplySettings()
        {
            DisableApplySaveButtons(false);

            try
            {
                if (App.Current.Settings is Settings settings)
                    await App.Driver.Instance.SetSettings(settings);
            }
            catch (StreamJsonRpc.RemoteInvocationException riex) when (riex.ErrorData is JObject err)
            {
                var type = (string)err["type"];
                var message = (string)err["message"];
                var stack = (string)err["stack"];
                var logMessage = new LogMessage
                {
                    Group = type,
                    Message = message,
                    StackTrace = stack
                };
                Log.Write(logMessage);
            }
        }

        private static void LoadPresets() => AppInfo.PresetManager.Refresh();

        private Task RefreshPresets()
        {
            LoadPresets();

            if (trayIcon != null) // Check non-Linux
                trayIcon.RefreshMenuItems();

            // Update File submenu
            var presets = AppInfo.PresetManager.GetPresets();
            var presetsMenu = fullMenu.Items.GetSubmenu("&File").Items.GetSubmenu("Presets") as ButtonMenuItem;
            presetsMenu.Items.Clear();

            if (presets.Count != 0)
            {
                foreach (var preset in presets)
                {
                    var presetItem = new ButtonMenuItem
                    {
                        Text = preset.Name
                    };
                    presetItem.Click += PresetButtonHandler;

                    presetsMenu.Items.Add(presetItem);
                }
            }
            else
            {
                var emptyPresetsItem = new ButtonMenuItem
                {
                    Text = "No presets loaded",
                    Enabled = false
                };

                presetsMenu.Items.Add(emptyPresetsItem);
            }

            return Task.CompletedTask;
        }

        private async Task SavePresetDialog()
        {
            // TODO: this should probably use a modal dialog instead, as presets are only readable from the Preset directory
            var fileDialog = Extensions.SaveFileDialog(
                "Save OpenTabletDriver settings as preset...",
                AppInfo.Current.PresetDirectory,
                [new FileFilter("OpenTabletDriver Settings (*.json)", ".json")],
                "mypreset.json"
            );

            switch (fileDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var file = new FileInfo(fileDialog.FileName + (fileDialog.FileName.EndsWith(".json") ? "" : ".json"));
                    if (App.Current.Settings is Settings settings)
                        settings.Serialize(file);
                    await RefreshPresets();
                    break;
            }
        }

        private async Task SaveAppPresetDialog()
        {
            var txtName = new TextBox();
            var txtClass = new TextBox();

            var dialog = new Dialog<DialogResult>
            {
                Title = "Save App Preset",
                WindowStyle = WindowStyle.Default,
                Content = new TableLayout
                {
                    Padding = new Padding(10),
                    Spacing = new Size(5, 5),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Preset Name:" }, txtName),
                        new TableRow(new Label { Text = "Window Class:" }, txtClass),
                        null
                    }
                }
            };

            var ok = new Button { Text = "Save" };
            ok.Click += (s, e) => dialog.Close(DialogResult.Ok);
            dialog.DefaultButton = ok;

            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => dialog.Close(DialogResult.Cancel);
            dialog.AbortButton = cancel;

            dialog.PositiveButtons.Add(ok);
            dialog.NegativeButtons.Add(cancel);

            if (dialog.ShowModal(this) == DialogResult.Ok && !string.IsNullOrWhiteSpace(txtName.Text) && !string.IsNullOrWhiteSpace(txtClass.Text))
            {
                var file = new FileInfo(Path.Combine(AppInfo.Current.PresetDirectory, txtName.Text + ".json"));
                if (App.Current.Settings is Settings settings)
                {
                    settings.Serialize(file);
                    await RefreshPresets();

                    if (App.Current.AppProfilerSettings is AppProfilerSettings appSettings)
                    {
                        appSettings.AppProfiles ??= new Dictionary<string, string>();
                        appSettings.AppProfiles[txtClass.Text] = txtName.Text;
                        appSettings.EnableAppProfiler = true;
                        await App.Driver.Instance.SetAppProfilerSettings(appSettings);
                    }
                }
            }
        }

        private async Task SaveAppModeDialog()
        {
            var outputModeSelector = new TypeDropDown<IOutputMode>();
            var txtClass = new TextBox();

            var dialog = new Dialog<DialogResult>
            {
                Title = "Map App Mode",
                WindowStyle = WindowStyle.Default,
                Content = new TableLayout
                {
                    Padding = new Padding(10),
                    Spacing = new Size(5, 5),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Output Mode:" }, outputModeSelector),
                        new TableRow(new Label { Text = "Window Class:" }, txtClass),
                        null
                    }
                }
            };

            var ok = new Button { Text = "Save" };
            ok.Click += (s, e) => dialog.Close(DialogResult.Ok);
            dialog.DefaultButton = ok;

            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => dialog.Close(DialogResult.Cancel);
            dialog.AbortButton = cancel;

            dialog.PositiveButtons.Add(ok);
            dialog.NegativeButtons.Add(cancel);

            if (dialog.ShowModal(this) == DialogResult.Ok && outputModeSelector.SelectedItem != null && !string.IsNullOrWhiteSpace(txtClass.Text))
            {
                if (App.Current.AppProfilerSettings is AppProfilerSettings appSettings)
                {
                    appSettings.AppOutputModes ??= new Dictionary<string, string>();
                    appSettings.AppOutputModes[txtClass.Text] = outputModeSelector.SelectedItem!.FullName;
                    appSettings.EnableAppProfiler = true;
                    await App.Driver.Instance.SetAppProfilerSettings(appSettings);
                }
            }
        }

        private async Task SetDefaultAppPresetDialog()
        {
            var presetBox = new ComboBox();
            presetBox.DataStore = AppInfo.PresetManager.GetPresets().Select(p => p.Name).ToList();

            var dialog = new Dialog<DialogResult>
            {
                Title = "Set Default App Preset",
                WindowStyle = WindowStyle.Default,
                Content = new TableLayout
                {
                    Padding = new Padding(10),
                    Spacing = new Size(5, 5),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Preset:" }, presetBox),
                        null
                    }
                }
            };

            var ok = new Button { Text = "Save" };
            ok.Click += (s, e) => dialog.Close(DialogResult.Ok);
            dialog.DefaultButton = ok;

            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => dialog.Close(DialogResult.Cancel);
            dialog.AbortButton = cancel;

            dialog.PositiveButtons.Add(ok);
            dialog.NegativeButtons.Add(cancel);

            if (dialog.ShowModal(this) == DialogResult.Ok && !string.IsNullOrWhiteSpace(presetBox.Text))
            {
                if (App.Current.AppProfilerSettings is AppProfilerSettings appSettings)
                {
                    appSettings.DefaultAppProfile = presetBox.Text;
                    appSettings.EnableAppProfiler = true;
                    await App.Driver.Instance.SetAppProfilerSettings(appSettings);
                }
            }
        }

        private async Task SetDefaultAppModeDialog()
        {
            var outputModeSelector = new TypeDropDown<IOutputMode>();

            var dialog = new Dialog<DialogResult>
            {
                Title = "Set Default App Mode",
                WindowStyle = WindowStyle.Default,
                Content = new TableLayout
                {
                    Padding = new Padding(10),
                    Spacing = new Size(5, 5),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Output Mode:" }, outputModeSelector),
                        null
                    }
                }
            };

            var ok = new Button { Text = "Save" };
            ok.Click += (s, e) => dialog.Close(DialogResult.Ok);
            dialog.DefaultButton = ok;

            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => dialog.Close(DialogResult.Cancel);
            dialog.AbortButton = cancel;

            dialog.PositiveButtons.Add(ok);
            dialog.NegativeButtons.Add(cancel);

            if (dialog.ShowModal(this) == DialogResult.Ok && outputModeSelector.SelectedItem != null)
            {
                if (App.Current.AppProfilerSettings is AppProfilerSettings appSettings)
                {
                    appSettings.DefaultOutputMode = outputModeSelector.SelectedItem!.FullName;
                    appSettings.EnableAppProfiler = true;
                    await App.Driver.Instance.SetAppProfilerSettings(appSettings);
                }
            }
        }

        public static async void PresetButtonHandler(object sender, EventArgs e)
        {
            var presetName = (sender as ButtonMenuItem).Text;
            var preset = AppInfo.PresetManager.FindPreset(presetName);

            if (preset != null && App.Current.Settings is Settings currentSettings)
            {
                var settingsToApply = preset.Settings.Clone();

                await App.Driver.Instance.SetSettings(settingsToApply);
                settingsToApply.Serialize(new FileInfo(AppInfo.Current.SettingsFile));

                Log.Write("Settings", $"Applied preset '{preset.Name}'");
            }
        }

        private static async Task DetectTablet()
        {
            await App.Driver.Instance.DetectTablets();
            await App.Driver.Instance.SetSettings(await App.Driver.Instance.GetSettings());
        }

        private readonly string _diagnosticsPrefix = $"Diagnostics-{App.Version.Replace(".", "")}";

        private async Task ExportDiagnostics()
        {
            try
            {
                var diagnosticDump = await App.Driver.Instance.GetDiagnosticInfo();

                var tablets = await App.Driver.Instance.GetTablets();
                var tabletReferences = tablets as TabletReference[] ?? tablets.ToArray();
                string tabletNames = tabletReferences.Length != 0
                    ? " " + string.Join(", ", tabletReferences.Select(x => x.Properties.Name))
                    : string.Empty;

                var fileDialog = Extensions.SaveFileDialog(
                    "Save diagnostic information to...",
                    Eto.EtoEnvironment.GetFolderPath(Eto.EtoSpecialFolder.Documents),
                    [new FileFilter("Diagnostic information", ".json")],
                    $"{_diagnosticsPrefix}{tabletNames}.json"
                );

                switch (fileDialog.ShowDialog(this))
                {
                    case DialogResult.Ok:
                    case DialogResult.Yes:
                        string[] options = [".json", ".txt", ".log"];
                        var file = new FileInfo(fileDialog.FileName + (options.Any(fileDialog.FileName.EndsWith) ? "" : ".json"));
                        if (file.Exists)
                            file.Delete();

                        await using (var fs = file.OpenWrite())
                        await using (var sw = new StreamWriter(fs))
                            await sw.WriteLineAsync(diagnosticDump.ToString());
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                ex.ShowMessageBox();
            }
        }
        private static async Task ExportDiagnosticsToClipboard()
        {
            try
            {
                var diagnosticDump = await App.Driver.Instance.GetDiagnosticInfo();

                Clipboard.Instance.Clear();
                Clipboard.Instance.Text = diagnosticDump.ToString();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                ex.ShowMessageBox();
            }
        }
        private static void CheckForUpdates()
        {
            // ReSharper disable once AsyncVoidMethod
            Application.Instance.AsyncInvoke(async void () =>
            {
                if (await App.Current.UpdaterWindow.GetWindow().HasUpdates())
                {
                    App.Current.UpdaterWindow.Show();
                }
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            App.Driver.Disconnected -= HandleDaemonDisconnected;
            base.OnClosing(e);
        }
    }
}
