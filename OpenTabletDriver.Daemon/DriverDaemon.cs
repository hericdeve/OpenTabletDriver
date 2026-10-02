using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Diagnostics;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Desktop.Updater;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.SystemDrivers;

namespace OpenTabletDriver.Daemon
{
    public class DriverDaemon : IDriverDaemon, IActiveAppContext
    {
        private const string AVALONIA_REVISION = "0.7.0.0";
        private readonly AppProfileMonitor _appProfileMonitor;
        private readonly object _hudLock = new();

        public static DriverDaemon? ActiveInstance { get; private set; }
        public bool IsHudActive { get; private set; }
        public Vector2? HudAnchorPosition { get; private set; }
        public Vector2? HudCurrentPosition { get; private set; }
        public HudConfiguration? ActiveHudConfig { get; private set; }
        public int CurrentHoveredSlice { get; private set; } = -1;

        private System.Threading.Timer? _hudSubLayerTimer;
        private HudConfiguration? _rootHudConfig;

        public DriverDaemon(Driver driver)
        {
            ActiveInstance = this;
            Driver = driver;
            _appProfileMonitor = new AppProfileMonitor(this);
            _logFile = new LogFile(AppInfo.Current.LogDirectory);

            Log.Output += (sender, message) =>
            {
                _logFile.Write(message);
                Message?.Invoke(sender, message);
            };

            InitializePlatform();
            Driver.TabletsChanged += (sender, e) => TabletsChanged?.Invoke(sender, e);
            Driver.CompositeDeviceHub.DevicesChanged += async (sender, args) =>
            {
                if (!args.Additions.Any()) return;

                // only re-initialize pipeline if a relevant device is plugged in
                if (args.Additions.Any(x => Driver.KnownVendorIDs.Contains(x.VendorID)))
                {
                    await DetectTablets();
                    await SetSettings(Settings);
                }
                else
                {
                    Log.Write(nameof(DriverDaemon), "No known tablets added, skipping detect", LogLevel.Debug);
                }
            };

            foreach (var driverInfo in DriverInfo.GetDriverInfos())
            {
                var os = SystemInterop.CurrentPlatform switch
                {
                    PluginPlatform.Windows => "Windows",
                    PluginPlatform.Linux => "Linux",
                    PluginPlatform.MacOS => "MacOS",
                    _ => null
                };
                var wikiUrl = $"https://opentabletdriver.net/Wiki/FAQ/{os}";

                var message = new StringBuilder();
                message.Append($"'{driverInfo.Name}' driver is detected.");

                if (driverInfo.Status.HasFlag(DriverStatus.Blocking))
                    message.Append(" It will block detection of tablets.");
                if (driverInfo.Status.HasFlag(DriverStatus.Flaky))
                    message.Append(" It will cause flaky support to tablets.");
                if (driverInfo.Status.HasFlag(DriverStatus.Uncertain))
                    message.Append(" It may be a false positive.");

                var processStrings = safeGetProcessDetails(driverInfo.Processes);
                message.Append($" Processes found: [" + string.Join(", ", processStrings) + "].");

                if (os != null)
                    message.Append($" If any problems arise, visit '{wikiUrl}'.");

                Log.WriteNotify("Detect", message.ToString(), LogLevel.Warning);
            }

            LoadUserSettings().Wait();

            SleepDetector.Slept += async () =>
            {
                if (System.Diagnostics.Debugger.IsAttached)
                    return;

                Log.Write(nameof(DriverDaemon), "Sleep detected...");
                await DetectTablets();
                await SetSettings(Settings);
            };
        }

        private static IEnumerable<string> safeGetProcessDetails(Process[] processes)
        {
            foreach (var driverProcess in processes)
            {
                var details = "";
                try
                {
                    details += driverProcess.ProcessName;
                }
                catch
                {
                    details += "Failed to get ProcessName";
                }

                try
                {
                    details += ": " + driverProcess.MainModule?.FileName;
                }
                catch
                {
                    details += ": Failed to get FileName";
                }
                yield return details;
            }
        }

        public event EventHandler<LogMessage>? Message;
        public event EventHandler<DebugReportData>? DeviceReport;
        public event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;
        public event EventHandler? Resynchronize;
        public event EventHandler<HudShowRequest>? ShowHudRequested;
        public event EventHandler<HudUpdateRequest>? UpdateHudRequested;
        public event EventHandler? DismissHudRequested;

        public Driver Driver { get; }
        public Settings? Settings { set; get; }
        public Settings? BaseSettings { get; private set; }
        public AppProfilerSettings AppProfilerSettings { get; private set; } = new AppProfilerSettings();
        private Collection<ITool> Tools { set; get; } = new Collection<ITool>();
        private readonly IUpdater? Updater = DesktopInterop.Updater;
        private readonly ISleepDetector? SleepDetector = new SleepDetector();
        private Settings? lastValidSettings;

        private UpdateInfo? _updateInfo;
        private LogFile _logFile;

        private bool debugging;

        public Task WriteMessage(LogMessage message)
        {
            Log.Write(message);
            return Task.CompletedTask;
        }

        public Task LoadPlugins()
        {
            var pluginDir = new DirectoryInfo(AppInfo.Current.PluginDirectory);

            if (!pluginDir.Exists)
            {
                pluginDir.Create();
                Log.Write("Plugin", $"The plugin directory '{pluginDir.FullName}' has been created");
            }

            AppInfo.PluginManager.Load();

            // Add services to inject on plugin construction
            AppInfo.PluginManager.AddService<IDriver>(() => this.Driver);
            AppInfo.PluginManager.AddService<IDriverDaemon>(() => this);
            AppInfo.PluginManager.AddService<IActiveAppContext>(() => this);

            return Task.CompletedTask;
        }

        public Task<bool> InstallPlugin(string filePath)
        {
            return Task.FromResult(AppInfo.PluginManager.InstallPlugin(filePath));
        }

        // FIXME: needs API bump: IDriverDaemon expects friendlyName but this implementation takes a full path
        public Task<bool> UninstallPlugin(string directoryPath)
        {
            var plugins = AppInfo.PluginManager.GetLoadedPlugins();
            var context = plugins.First(ctx => ctx.Directory.FullName == directoryPath);
            return Task.FromResult(AppInfo.PluginManager.UninstallPlugin(context));
        }

        public Task<bool> DownloadPlugin(PluginMetadata metadata)
        {
            return AppInfo.PluginManager.DownloadPlugin(metadata);
        }

        public Task<IEnumerable<TabletReference>> GetTablets()
        {
            return Task.FromResult(Driver.Tablets);
        }

        public async Task<IEnumerable<TabletReference>> DetectTablets()
        {
            Driver.Detect();
            await Task.Run(CheckForProblematicProcesses).ConfigureAwait(false);

            foreach (var tablet in Driver.InputDevices)
            {
                foreach (var dev in tablet.InputDevices)
                {
                    dev.RawReport += (_, report) => PostDebugReport(tablet, report);
                    dev.RawClone = debugging;
                }
            }

            return await GetTablets();
        }

        public Task SetSettings(Settings? settings) => SetSettings(settings, false);

        public Task SetSettings(Settings? settings, bool isAppProfileUpdate)
        {
            try
            {
                if (!isAppProfileUpdate && settings != null)
                {
                    BaseSettings = settings.Clone();
                }

                foreach (var dev in Driver.InputDevices)
                {
                    if (dev.OutputMode?.Elements != null)
                    {
                        foreach (var bindingHandler in dev.OutputMode.Elements.OfType<BindingHandler>())
                            bindingHandler.ReleaseAllBindings();
                    }

                    dev.OutputMode?.Dispose();
                }

                Settings = settings ??= Settings.GetDefaults();

                if (!isAppProfileUpdate && BaseSettings == null)
                {
                    BaseSettings = Settings.Clone();
                }

                foreach (var dev in Driver.InputDevices)
                {
                    var tabletReference = dev.CreateReference();
                    string group = dev.Properties.Name;
                    var profile = Settings.Profiles[dev];

                    profile.BindingSettings.MatchSpecifications(dev.Properties.Specifications);

                    dev.OutputMode = profile.OutputMode.Construct<IOutputMode>(tabletReference);

                    if (dev.OutputMode != null)
                        Log.Write(group, $"Output mode: {profile.OutputMode.Name}");

                    if (dev.OutputMode is AbsoluteOutputMode absoluteMode)
                    {
                        if (profile.AbsoluteModeSettings == null)
                            throw new InvalidOperationException($"{nameof(AbsoluteModeSettings)} not found");

                        SetAbsoluteModeSettings(dev, absoluteMode, profile.AbsoluteModeSettings);
                        if (absoluteMode.Pointer is IPressureHandler)
                            LogPressureState(group, profile);
                        if (absoluteMode.Pointer is ITiltHandler)
                            LogTiltState(group, profile);
                    }

                    if (dev.OutputMode is RelativeOutputMode relativeMode)
                    {
                        if (profile.RelativeModeSettings == null)
                            throw new InvalidOperationException($"{nameof(RelativeModeSettings)} not found");

                        SetRelativeModeSettings(dev, relativeMode, profile.RelativeModeSettings);
                        if (relativeMode.Pointer is IPressureHandler)
                            LogPressureState(group, profile);
                        if (relativeMode.Pointer is ITiltHandler)
                            LogTiltState(group, profile);
                    }

                    if (dev.OutputMode is { } outputMode)
                    {
                        outputMode.Tablet = tabletReference;
                        var bindingHandler = CreateBindingHandler(dev, outputMode, profile.BindingSettings);
                        SetOutputModeElements(dev, outputMode, profile, bindingHandler);

                        outputMode.DisablePressure = profile.BindingSettings.DisablePressure;
                        outputMode.UniformStrokePressure = profile.BindingSettings.UniformStrokePressure;
                        outputMode.DisableTilt = profile.BindingSettings.DisableTilt;
                        outputMode.DisableRotation = profile.BindingSettings.DisableRotation;
                    }
                }

                if (Driver.InputDevices.Length > 0)
                    Log.Write("Settings", "Driver is enabled.");

                SetToolSettings();

                if (!isAppProfileUpdate)
                {
                    _appProfileMonitor.ResetActiveTrackingState();
                    _appProfileMonitor.Initialize();
                }

                lastValidSettings = settings;
                Resynchronize?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Write("Settings", $"Exception in SetSettings: {ex}", LogLevel.Error);
                try
                {
                    SetSettings(lastValidSettings, isAppProfileUpdate);
                    Log.Write("Settings", "Failed to apply settings. Reverted to last valid settings.", LogLevel.Error, true);
                }
                catch
                {
                    RecoverSettings(settings);
                    Log.Write("Settings", "Failed to apply settings. Attempted recovery. Some settings may have been lost.", LogLevel.Error, true);
                }

                Resynchronize?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            }
        }

        private static void LogRotationState(string group, Profile profile)
        {
            Log.Write(group,
                $"Rotation: {(profile.BindingSettings.DisableRotation ? "Disabled" : "Enabled")}");
        }

        private static void LogTiltState(string group, Profile profile)
        {
            Log.Write(group, $"Tilt: {(profile.BindingSettings.DisableTilt ? "Disabled" : "Enabled")}");
        }

        private static void LogPressureState(string group, Profile profile)
        {
            string mode = profile.BindingSettings.DisablePressure ? "Disabled" :
                (profile.BindingSettings.UniformStrokePressure ? "Uniform (Constant 100%)" : "Enabled");
            Log.Write(group, $"Pressure: {mode}");
        }

        private void RecoverSettings(Settings? settings)
        {
            var recoveredSettings = Settings.GetDefaults();

            if (settings != null)
            {
                // Copy by value, not by reference
                foreach (var profile in settings.Profiles)
                {
                    var recoveredProfile = recoveredSettings.Profiles.GetProfile(profile.Tablet);
                    if (recoveredProfile != null)
                    {
                        if (profile.AbsoluteModeSettings != null)
                        {
                            recoveredProfile.AbsoluteModeSettings = new AbsoluteModeSettings
                            {
                                Display = new AreaSettings
                                {
                                    Area = profile.AbsoluteModeSettings.Display.Area
                                },
                                Tablet = new AreaSettings
                                {
                                    Area = profile.AbsoluteModeSettings.Tablet.Area
                                },
                                EnableClipping = profile.AbsoluteModeSettings.EnableClipping,
                                EnableAreaLimiting = profile.AbsoluteModeSettings.EnableAreaLimiting,
                                LockAspectRatio = profile.AbsoluteModeSettings.LockAspectRatio
                            };
                        }

                        if (profile.RelativeModeSettings != null)
                        {
                            recoveredProfile.RelativeModeSettings = new RelativeModeSettings
                            {
                                Sensitivity = profile.RelativeModeSettings.Sensitivity,
                                RelativeRotation = profile.RelativeModeSettings.RelativeRotation,
                                ResetTime = profile.RelativeModeSettings.ResetTime
                            };
                        }
                    }
                }
            }

            SetSettings(recoveredSettings);
        }

        public async Task ResetSettings()
        {
            await SetSettings(Settings.GetDefaults());
        }

        private async Task LoadUserSettings()
        {
            AppInfo.PluginManager.Clean();
            await LoadPlugins();
            await DetectTablets();

            var appdataDir = new DirectoryInfo(AppInfo.Current.AppDataDirectory);
            if (!appdataDir.Exists)
            {
                appdataDir.Create();
                Log.Write("Settings", $"Created OpenTabletDriver application data directory: {appdataDir.FullName}");
            }
            else
            {
                Log.Write("Settings", $"Using OpenTabletDriver application data directory: {appdataDir.FullName}", LogLevel.Debug);
            }

            var settingsFile = new FileInfo(AppInfo.Current.SettingsFile);
            var appProfilesFile = new FileInfo(AppInfo.Current.AppProfilesFile);

            if (AppProfilerSettings.TryDeserialize(appProfilesFile, out var appProfilerSettings) && appProfilerSettings != null)
            {
                AppProfilerSettings = appProfilerSettings;
            }

            if (settingsFile.Exists)
            {
                if (Settings.TryDeserialize(settingsFile, out var settings) &&
                    settings.Revision != AVALONIA_REVISION)
                {
                    settings.Revision = Settings.GetVersion(); // ensure Revision matches Daemon version
                    await SetSettings(settings);
                }
                else
                {
                    Log.Write("Settings", "Invalid settings found. Moving invalid config.");
                    MoveSettingsFile();
                    await ResetSettings();
                }
            }
            else
            {
                await ResetSettings();

                // only save fresh settings if a tablet was configured
                if (Settings!.Profiles.Any())
                    Settings.Serialize(settingsFile);
            }
        }

        private static void MoveSettingsFile()
        {
            var src = AppInfo.Current.SettingsFile;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var dstFileName = $"settings_bak-{now}.json";
            var dst = Path.Join(AppInfo.Current.AppDataDirectory, dstFileName);

            Log.Write("MoveSettingsFile", $"Moving settings file at '{src}' to '{dst}'", LogLevel.Debug);
            File.Move(src, dst);
        }

        private static void SetOutputModeElements(InputDeviceTree dev, IOutputMode outputMode, Profile profile, BindingHandler bindingHandler)
        {
            string group = dev.Properties.Name;

            var pressureRewriteFilter = new PressureRewriteFilter
            {
                TipPressureThreshold = profile.BindingSettings.TipActivationThreshold,
                EraserPressureThreshold = profile.BindingSettings.EraserActivationThreshold,
                MaxPenPressure = dev.Properties.Specifications.Pen.MaxPressure,
            };

            var elements = (from store in profile.Filters
                            where store is { Enable: true }
                            let filter = store!.Construct<IPositionedPipelineElement<IDeviceReport>>(outputMode.Tablet)
                            where filter != null
                            select filter!).ToArray();

            outputMode.Elements = elements.Prepend(pressureRewriteFilter).Append(bindingHandler).ToList();

            foreach (var filter in elements)
            {
                var pluginSettings = profile.Filters.First(x => x?.Path == filter.GetType().FullName);
                if (pluginSettings == null) continue;

                Log.Write(group, $"Filter Settings {pluginSettings.GetHumanReadableString()}");
            }
        }

        private static void SetAbsoluteModeSettings(InputDeviceTree dev, AbsoluteOutputMode absoluteMode, AbsoluteModeSettings settings)
        {
            string group = dev.Properties.Name;
            absoluteMode.Output = settings.Display.Area;

            Log.Write(group, $"Display area: {absoluteMode.Output}");

            absoluteMode.Input = settings.Tablet.Area;
            Log.Write(group, $"Tablet area: {absoluteMode.Input}");

            absoluteMode.AreaClipping = settings.EnableClipping;
            Log.Write(group, $"Clipping: {(absoluteMode.AreaClipping ? "Enabled" : "Disabled")}");

            absoluteMode.AreaLimiting = settings.EnableAreaLimiting;
            Log.Write(group, $"Ignoring reports outside area: {(absoluteMode.AreaLimiting ? "Enabled" : "Disabled")}");
        }

        private static void SetRelativeModeSettings(InputDeviceTree dev, RelativeOutputMode relativeMode, RelativeModeSettings settings)
        {
            string group = dev.Properties.Name;
            relativeMode.Sensitivity = settings.Sensitivity;

            Log.Write(group, $"Relative Mode Sensitivity (X, Y): {relativeMode.Sensitivity}");

            relativeMode.Rotation = settings.RelativeRotation;
            Log.Write(group, $"Relative Mode Rotation: {relativeMode.Rotation}");

            relativeMode.ResetTime = settings.ResetTime;
            Log.Write(group, $"Reset time: {relativeMode.ResetTime}");
        }

        /// <summary>
        /// Checks for any problematic processes running on the user's computer that may
        /// impair function or detection of tablets, such as video game anti-cheat software.
        /// </summary>
        private void CheckForProblematicProcesses()
        {
            if (SystemInterop.CurrentPlatform == PluginPlatform.Windows)
            {
                if (Process.GetProcessesByName("vgc").Any())
                    Log.Write("Detect", "Valorant's anti-cheat program Vanguard is detected. Tablet function may be impaired.", LogLevel.Warning);
                if (Process.GetProcessesByName("VALORANT-Win64-Shipping").Any())
                    Log.Write("Detect", "Valorant is detected. Tablet function may be impaired.", LogLevel.Warning);
            }
        }

        private BindingHandler CreateBindingHandler(InputDeviceTree dev, IOutputMode outputMode, BindingSettings settings)
        {
            string group = dev.Properties.Name;
            var tabletReference = outputMode.Tablet;

            Debug.Assert(tabletReference != null,
                "tabletReference was null. This was expected to be checked by the sender");

            var bindingHandler = new BindingHandler(tabletReference);
            bindingHandler.IsDaemonPrecisionActive = () => _isPrecisionModeActive;

            var bindingServiceProvider = new ServiceManager();
            bindingServiceProvider.AddService<IDriverDaemon>(() => this);
            object? pointer = outputMode switch
            {
                AbsoluteOutputMode absoluteOutputMode => absoluteOutputMode.Pointer,
                RelativeOutputMode relativeOutputMode => relativeOutputMode.Pointer,
                _ => null
            };

            if (pointer is IMouseButtonHandler mouseButtonHandler)
                bindingServiceProvider.AddService(() => mouseButtonHandler);

            if (pointer is IMouseScrollHandler mouseScrollHandler)
                bindingServiceProvider.AddService(() => mouseScrollHandler);
            else if (DesktopInterop.RelativePointer is IMouseScrollHandler fallbackScrollHandler)
                bindingServiceProvider.AddService(() => fallbackScrollHandler);

            bindingServiceProvider.AddService<OpenTabletDriver.Plugin.Timers.ITimer>(() => DesktopInterop.Timer);

            if (DesktopInterop.GestureHandler is IGestureHandler gestureHandler)
                bindingServiceProvider.AddService(() => gestureHandler);

            if (pointer is IPenActionHandler penActionHandler)
                bindingServiceProvider.AddService(() => penActionHandler);

            var tip = bindingHandler.Tip = new ThresholdBindingState
            {
                Binding = settings.TipButton?.Construct<IBinding>(bindingServiceProvider, tabletReference),
            };

            if (tip.Binding != null)
            {
                Log.Write(group, $"Tip Binding: [{tip.Binding}]@{settings.TipActivationThreshold}%");
            }

            // Only initialize separate TipDeepPress if TipButton is not already handling it via MultiActionBinding
            if (settings.TipDeepPressButton != null && tip.Binding is not MultiActionBinding)
            {
                var tipDeepPress = bindingHandler.TipDeepPress = new DeepPressBindingState
                {
                    Binding = settings.TipDeepPressButton.Construct<IBinding>(bindingServiceProvider, tabletReference),
                    LiftBinding = settings.TipDeepPressLiftButton?.Construct<IBinding>(bindingServiceProvider, tabletReference),
                    ActivationThreshold = settings.TipDeepPressThreshold,
                    HoldDelayMs = settings.TipDeepPressHoldDelayMs,
                    SuppressStroke = settings.TipDeepPressSuppressStroke
                };

                if (tipDeepPress.Binding != null)
                {
                    var liftInfo = tipDeepPress.LiftBinding != null ? $", Lift: [{tipDeepPress.LiftBinding}]" : "";
                    Log.Write(group, $"Tip Deep Press: [{tipDeepPress.Binding}]@{settings.TipDeepPressThreshold}% (Hold: {settings.TipDeepPressHoldDelayMs}ms, SuppressStroke: {settings.TipDeepPressSuppressStroke}{liftInfo})");
                }
            }

            var eraser = bindingHandler.Eraser = new ThresholdBindingState
            {
                Binding = settings.EraserButton?.Construct<IBinding>(bindingServiceProvider, tabletReference),
            };

            if (eraser.Binding != null)
            {
                Log.Write(group, $"Eraser Binding: [{eraser.Binding}]@{settings.EraserActivationThreshold}%");
            }

            if (settings.PenButtons.Any(b => b?.Path != null))
            {
                SetBindingHandlerCollectionSettings(bindingServiceProvider, settings.PenButtons, bindingHandler.PenButtons, tabletReference, settings.EnableDragBindings);
                Log.Write(group, $"Pen Bindings: " + string.Join(", ", bindingHandler.PenButtons.Select(b => b.Value?.Binding)));

                if (settings.EnableDragBindings)
                    Log.Write(group, "Pen Bindings are configured as drag-only (requires pen pressure to activate)");
            }

            if (settings.AuxButtons.Any(b => b?.Path != null))
            {
                SetBindingHandlerCollectionSettings(bindingServiceProvider, settings.AuxButtons, bindingHandler.AuxButtons, tabletReference);
                Log.Write(group, $"Express Key Bindings: " + string.Join(", ", bindingHandler.AuxButtons.Select(b => b.Value?.Binding)));
            }

            for (int wheelIndex = 0; wheelIndex < settings.WheelBindings.Count; wheelIndex++)
            {
                var wheelBindingSetting = settings.WheelBindings[wheelIndex];
                var wheelBindingHandler = bindingHandler.Wheels[wheelIndex];

                if (wheelBindingSetting.WheelButtons.Any(b => b?.Path != null))
                {
                    SetBindingHandlerCollectionSettings(bindingServiceProvider, wheelBindingSetting.WheelButtons,
                        wheelBindingHandler.WheelButtons, tabletReference);

                    Log.Write(group,
                        $"Wheel {wheelIndex + 1} Button Bindings: [" + string.Join("], [",
                            wheelBindingHandler.WheelButtons.Select(b => b.Value?.Binding)) + "]");
                }

                var clockwiseRotation = wheelBindingHandler.ClockwiseRotation = new DeltaThresholdBindingState
                {
                    Binding = wheelBindingSetting.ClockwiseRotation?.Construct<IBinding>(bindingServiceProvider,
                        tabletReference),
                    ActivationThreshold = wheelBindingSetting.ClockwiseActivationThreshold,
                    IsNegativeThreshold = false
                };

                var counterClockwiseRotation = wheelBindingHandler.CounterClockwiseRotation =
                    new DeltaThresholdBindingState
                    {
                        Binding = wheelBindingSetting.CounterClockwiseRotation?.Construct<IBinding>(
                            bindingServiceProvider, tabletReference),
                        ActivationThreshold = wheelBindingSetting.CounterClockwiseActivationThreshold,
                        IsNegativeThreshold = true
                    };

                if (clockwiseRotation.Binding != null)
                    Log.Write(group, $"Wheel {wheelIndex + 1} Clockwise Rotation: [{clockwiseRotation.Binding}]@{clockwiseRotation.ActivationThreshold}°");

                if (counterClockwiseRotation.Binding != null)
                    Log.Write(group, $"Wheel {wheelIndex + 1} Counter-Clockwise Rotation: [{counterClockwiseRotation.Binding}]@{counterClockwiseRotation.ActivationThreshold}°");
            }

            if (settings.MouseButtons.Any(b => b?.Path != null))
            {
                SetBindingHandlerCollectionSettings(bindingServiceProvider, settings.MouseButtons, bindingHandler.MouseButtons, tabletReference);
                Log.Write(group, $"Mouse Button Bindings: [" + string.Join("], [", bindingHandler.MouseButtons.Select(b => b.Value?.Binding)) + "]");
            }

            var scrollUp = bindingHandler.MouseScrollUp = new BindingState
            {
                Binding = settings.MouseScrollUp?.Construct<IBinding>(bindingServiceProvider, tabletReference)
            };

            var scrollDown = bindingHandler.MouseScrollDown = new BindingState
            {
                Binding = settings.MouseScrollDown?.Construct<IBinding>(bindingServiceProvider, tabletReference)
            };

            if (scrollUp.Binding != null || scrollDown.Binding != null)
            {
                Log.Write(group, $"Mouse Scroll: Up: [{scrollUp.Binding}] Down: [{scrollDown.Binding}]");
            }

            return bindingHandler;
        }

        private static void SetBindingHandlerCollectionSettings(IServiceManager serviceManager, PluginSettingStoreCollection collection, Dictionary<int, BindingState?> targetDict, TabletReference tabletReference, bool bindingRequiresPressure = false)
        {
            for (int index = 0; index < collection.Count; index++)
            {
                var binding = collection[index]?.Construct<IBinding>(serviceManager, tabletReference);
                var state = binding == null ? null : new BindingState
                {
                    Binding = binding,
                    RequiresPenPressure = bindingRequiresPressure,
                };

                if (!targetDict.TryAdd(index, state))
                    targetDict[index] = state;
            }
        }

        private void SetToolSettings()
        {
            foreach (var runningTool in Tools)
                runningTool.Dispose();
            Tools.Clear();

            if (Settings != null)
            {
                foreach (var store in Settings.Tools)
                {
                    if (store is not { Enable: true })
                        continue;

                    var tool = store.Construct<ITool>();

                    if (tool?.Initialize() ?? false)
                        Tools.Add(tool);
                    else
                        Log.Write("Tool", $"Failed to initialize {store.Name} tool.", LogLevel.Error);
                }
            }
        }

        public Task<Settings> GetSettings()
        {
            return Task.FromResult((BaseSettings ?? Settings ?? Settings.GetDefaults()).Clone());
        }

        public async Task SetAppProfilerSettings(AppProfilerSettings settings)
        {
            var wasEnabled = AppProfilerSettings?.EnableAppProfiler ?? false;
            AppProfilerSettings = settings ?? new AppProfilerSettings();
            var file = new FileInfo(AppInfo.Current.AppProfilesFile);
            AppProfilerSettings.Serialize(file);
            _appProfileMonitor.Initialize();
            if (wasEnabled && !AppProfilerSettings.EnableAppProfiler && BaseSettings != null)
            {
                await SetSettings(BaseSettings.Clone(), true);
            }
            Resynchronize?.Invoke(this, EventArgs.Empty);
        }

        public Task<AppProfilerSettings> GetAppProfilerSettings()
        {
            return Task.FromResult(AppProfilerSettings);
        }

        public Task<IEnumerable<SerializedDeviceEndpoint>> GetDevices()
        {
            return Task.FromResult(Driver.CompositeDeviceHub.GetDevices().Select(d => new SerializedDeviceEndpoint(d)));
        }

        public Task<AppInfo> GetApplicationInfo()
        {
            return Task.FromResult(AppInfo.Current);
        }

        public Task SetTabletDebug(bool enabled)
        {
            debugging = enabled;
            foreach (var dev in Driver.InputDevices.SelectMany(d => d.InputDevices))
                dev.RawClone = debugging;

            Log.Debug("Tablet", $"Tablet debugging is {(debugging ? "enabled" : "disabled")}");

            return Task.CompletedTask;
        }

        public Task<string> RequestDeviceString(int vid, int pid, int index)
        {
            var tablet = Driver.CompositeDeviceHub.GetDevices().Where(d => d.VendorID == vid && d.ProductID == pid).FirstOrDefault();
            if (tablet == null)
                throw new IOException("Device not found");

            return Task.FromResult(tablet.GetDeviceString((byte)index) ?? throw new InvalidOperationException($"Unable to look up device string on index {index}"));
        }

        public Task<IEnumerable<LogMessage>> GetCurrentLog()
        {
            return Task.FromResult(_logFile.Read());
        }

        public async Task<DiagnosticInfo> GetDiagnosticInfo()
        {
            var log = await GetCurrentLog();
            return new DiagnosticInfo(log, await GetDevices());
        }

        private void PostDebugReport(TabletReference tablet, IDeviceReport report)
        {
            DeviceReport?.Invoke(this, new DebugReportData(tablet, report));
        }

        public async Task<SerializedUpdateInfo?> CheckForUpdates()
        {
            if (Updater == null)
                return null;

            _updateInfo = await Updater.CheckForUpdates();
            return _updateInfo?.ToSerializedUpdateInfo();
        }

        public async Task InstallUpdate()
        {
            if (_updateInfo == null)
                throw new InvalidOperationException("No update available"); // Misbehaving client

            try
            {
                var update = await _updateInfo.GetUpdate();
                Updater?.Install(update);
            }
            finally
            {
                _updateInfo = null;
            }
        }

        public Task ForceResynchronize()
        {
            Resynchronize?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task TriggerHudShow(HudShowRequest request)
        {
            lock (_hudLock)
            {
                _hudSubLayerTimer?.Dispose();
                _hudSubLayerTimer = null;
                _rootHudConfig = null;

                IsHudActive = true;
                ActiveHudConfig = Settings?.Hud ?? request.Configuration ?? HudConfiguration.GetDefaults();
                HudAnchorPosition = request.CursorPosition;
                HudCurrentPosition = request.CursorPosition;
                CurrentHoveredSlice = -1;
                request.Configuration = ActiveHudConfig;
            }

            ShowHudRequested?.Invoke(this, request);
            return Task.CompletedTask;
        }

        public Task TriggerHudUpdate(HudUpdateRequest request)
        {
            int hovered = -1;
            bool changed = false;
            lock (_hudLock)
            {
                if (!IsHudActive)
                    return Task.CompletedTask;

                HudCurrentPosition = request.CursorPosition;

                if (ActiveHudConfig != null && HudAnchorPosition.HasValue)
                {
                    hovered = CalculateHoveredSlice(request.CursorPosition, HudAnchorPosition.Value, ActiveHudConfig, CurrentHoveredSlice);
                }

                if (CurrentHoveredSlice != hovered)
                {
                    CurrentHoveredSlice = hovered;
                    changed = true;
                    OnHoveredSliceChanged(CurrentHoveredSlice);
                }

                request.HoveredSlice = CurrentHoveredSlice;
            }

            if (changed)
            {
                UpdateHudRequested?.Invoke(this, request);
            }

            return Task.CompletedTask;
        }

        private void OnHoveredSliceChanged(int hoveredSlice)
        {
            _hudSubLayerTimer?.Dispose();
            _hudSubLayerTimer = null;

            if (!IsHudActive || ActiveHudConfig == null)
                return;

            int delay = ActiveHudConfig.SubMenuHoverDelayMs > 0 ? ActiveHudConfig.SubMenuHoverDelayMs : 250;

            if (hoveredSlice >= 0 && hoveredSlice < ActiveHudConfig.Items.Count)
            {
                var item = ActiveHudConfig.Items[hoveredSlice];
                if (item.IsSubLayer)
                {
                    int targetSlice = hoveredSlice;
                    _hudSubLayerTimer = new System.Threading.Timer(async _ =>
                    {
                        await HandleSubLayerHoverTimeout(targetSlice);
                    }, null, delay, System.Threading.Timeout.Infinite);
                }
            }
            else if (_rootHudConfig != null && hoveredSlice == -1)
            {
                // Hovered back to center deadzone while in sub-layer:
                // After delay, return to root menu
                _hudSubLayerTimer = new System.Threading.Timer(async _ =>
                {
                    await RestoreRootHudLayer();
                }, null, delay + 50, System.Threading.Timeout.Infinite);
            }
        }

        private async Task HandleSubLayerHoverTimeout(int targetSlice)
        {
            HudItem? item = null;
            lock (_hudLock)
            {
                if (!IsHudActive || ActiveHudConfig == null || CurrentHoveredSlice != targetSlice)
                    return;

                if (targetSlice >= 0 && targetSlice < ActiveHudConfig.Items.Count)
                    item = ActiveHudConfig.Items[targetSlice];
            }

            if (item == null || !item.IsSubLayer)
                return;

            if (item.Action?.Type == HudActionType.WorkspaceLayer || item.Binding?.Path?.Contains("CompositorWorkspaceHudBinding") == true)
            {
                await SwitchToWorkspaceSubLayer(isMoveWindow: false);
            }
            else if (item.Action?.Type == HudActionType.MoveWindowWorkspaceLayer || item.Binding?.Path?.Contains("CompositorMoveWindowHudBinding") == true)
            {
                await SwitchToWorkspaceSubLayer(isMoveWindow: true);
            }
        }

        public async Task SwitchToWorkspaceSubLayer(bool isMoveWindow = false)
        {
            try
            {
                var workspaces = await GetCompositorWorkspaces();

                lock (_hudLock)
                {
                    if (!IsHudActive || ActiveHudConfig == null)
                        return;

                    _rootHudConfig ??= ActiveHudConfig;
                    var wsConfig = new HudConfiguration
                    {
                        FormFactor = _rootHudConfig.FormFactor,
                        ThemeStyle = _rootHudConfig.ThemeStyle,
                        FontFamily = _rootHudConfig.FontFamily,
                        Radius = _rootHudConfig.Radius,
                        DeadzoneRadius = _rootHudConfig.DeadzoneRadius,
                        Opacity = _rootHudConfig.Opacity,
                        KeepCursorAnchored = _rootHudConfig.KeepCursorAnchored,
                        SubMenuHoverDelayMs = _rootHudConfig.SubMenuHoverDelayMs,
                        IsSubMenu = true,
                        Items = new List<HudItem>()
                    };

                    int maxSlots = Settings?.CompositorSettings?.MaxHudWorkspaceSlots ?? 8;
                    var displayWorkspaces = workspaces.Take(maxSlots).ToList();
                    var actionType = isMoveWindow ? HudActionType.MoveWindowWorkspaceLayer : HudActionType.WorkspaceLayer;

                    foreach (var ws in displayWorkspaces)
                    {
                        var baseTitle = string.IsNullOrWhiteSpace(ws.LastWindowTitle)
                            ? $"WS {ws.Name}"
                            : $"[{ws.Name}] {ws.LastWindowTitle}";

                        string label;
                        if (isMoveWindow)
                        {
                            label = string.IsNullOrWhiteSpace(ws.LastWindowTitle)
                                ? $"-> WS {ws.Name}"
                                : $"-> [{ws.Name}] {ws.LastWindowTitle}";
                        }
                        else
                        {
                            label = baseTitle;
                        }

                        if (label.Length > 16)
                            label = label.Substring(0, 14) + "..";

                        if (ws.IsActive)
                            label = "✓ " + label;

                        wsConfig.Items.Add(new HudItem
                        {
                            Label = label,
                            Action = new HudAction
                            {
                                Type = actionType,
                                Value = ws.Id
                            }
                        });
                    }

                    if (wsConfig.Items.Count == 0)
                    {
                        for (int i = 1; i <= 5; i++)
                        {
                            wsConfig.Items.Add(new HudItem
                            {
                                Label = isMoveWindow ? $"-> WS {i}" : $"WS {i}",
                                Action = new HudAction
                                {
                                    Type = actionType,
                                    Value = i.ToString()
                                }
                            });
                        }
                    }

                    ActiveHudConfig = wsConfig;
                    CurrentHoveredSlice = -1;
                }

                if (HudAnchorPosition.HasValue)
                {
                    ShowHudRequested?.Invoke(this, new HudShowRequest
                    {
                        CursorPosition = HudAnchorPosition.Value,
                        Configuration = ActiveHudConfig
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        public Task RestoreRootHudLayer()
        {
            lock (_hudLock)
            {
                if (!IsHudActive || _rootHudConfig == null)
                    return Task.CompletedTask;

                ActiveHudConfig = _rootHudConfig;
                _rootHudConfig = null;
                CurrentHoveredSlice = -1;
            }

            if (HudAnchorPosition.HasValue)
            {
                ShowHudRequested?.Invoke(this, new HudShowRequest
                {
                    CursorPosition = HudAnchorPosition.Value,
                    Configuration = ActiveHudConfig
                });
            }

            return Task.CompletedTask;
        }

        private static int CalculateHoveredSlice(Vector2 currentPos, Vector2 anchorPos, HudConfiguration config, int currentSlice)
        {
            Vector2 delta = currentPos - anchorPos;
            float distance = delta.Length();
            if (distance < config.DeadzoneRadius || config.Items.Count == 0)
                return -1;

            int count = config.Items.Count;
            float sliceAngle = 360f / count;
            double rad = Math.Atan2(delta.Y, delta.X);
            double deg = (rad * 180.0 / Math.PI) + 90.0;
            if (deg < 0) deg += 360.0;

            // If we currently have a slice selected, apply angular hysteresis (buffer of 2.5 degrees)
            // to eliminate boundary jitter and split-second jumping.
            const double hysteresis = 2.5;
            if (currentSlice >= 0 && currentSlice < count)
            {
                double currentCenterAngle = currentSlice * sliceAngle;
                double diff = deg - currentCenterAngle;
                while (diff > 180.0) diff -= 360.0;
                while (diff < -180.0) diff += 360.0;

                // Within current slice boundary + hysteresis margin
                if (Math.Abs(diff) <= (sliceAngle / 2.0) + hysteresis)
                {
                    return currentSlice;
                }
            }

            return (int)Math.Floor((deg + (sliceAngle / 2.0)) / sliceAngle) % count;
        }

        public Task ConfirmHudSelection(Vector2? finalPosition = null)
        {
            _hudSubLayerTimer?.Dispose();
            _hudSubLayerTimer = null;

            HudItem? itemToExecute = null;
            bool keepOpenForSubLayer = false;

            lock (_hudLock)
            {
                if (!IsHudActive)
                    return Task.CompletedTask;

                // If a final position was provided, calculate the definitive slice at release time
                if (finalPosition.HasValue && ActiveHudConfig != null && HudAnchorPosition.HasValue)
                {
                    CurrentHoveredSlice = CalculateHoveredSlice(finalPosition.Value, HudAnchorPosition.Value, ActiveHudConfig, CurrentHoveredSlice);
                }

                if (ActiveHudConfig != null && CurrentHoveredSlice >= 0 && CurrentHoveredSlice < ActiveHudConfig.Items.Count)
                {
                    itemToExecute = ActiveHudConfig.Items[CurrentHoveredSlice];
                }

                if (itemToExecute != null && itemToExecute.IsSubLayer)
                {
                    keepOpenForSubLayer = true;
                }
                else
                {
                    IsHudActive = false;
                    _rootHudConfig = null;
                    CurrentHoveredSlice = -1;
                    HudAnchorPosition = null;
                    HudCurrentPosition = null;
                }
            }

            if (keepOpenForSubLayer)
            {
                bool isMove = itemToExecute?.Action?.Type == HudActionType.MoveWindowWorkspaceLayer ||
                              itemToExecute?.Binding?.Path?.Contains("CompositorMoveWindowHudBinding") == true;
                _ = Task.Run(async () =>
                {
                    await SwitchToWorkspaceSubLayer(isMoveWindow: isMove);
                });
                return Task.CompletedTask;
            }

            DismissHudRequested?.Invoke(this, EventArgs.Empty);

            if (itemToExecute != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteHudItem(itemToExecute);
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex);
                    }
                });
            }

            return Task.CompletedTask;
        }

        public Task TriggerHudDismiss()
        {
            lock (_hudLock)
            {
                if (!IsHudActive)
                    return Task.CompletedTask;

                _hudSubLayerTimer?.Dispose();
                _hudSubLayerTimer = null;
                _rootHudConfig = null;

                IsHudActive = false;
                HudAnchorPosition = null;
                HudCurrentPosition = null;
                CurrentHoveredSlice = -1;
            }

            DismissHudRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public async Task ExecuteHudItem(HudItem item)
        {
            if (item == null) return;

            if (item.IsSubLayer)
            {
                bool isMove = item.Action?.Type == HudActionType.MoveWindowWorkspaceLayer ||
                              item.Binding?.Path?.Contains("CompositorMoveWindowHudBinding") == true;
                await SwitchToWorkspaceSubLayer(isMoveWindow: isMove);
                return;
            }

            var effectiveBinding = item.GetEffectiveBinding();
            if (effectiveBinding != null)
            {
                await ExecuteBinding(effectiveBinding);
                return;
            }

            if (item.Action != null)
            {
                await ExecuteHudAction(item.Action);
            }
        }

        public Task<IReadOnlyList<OpenTabletDriver.Desktop.Compositor.WorkspaceInfo>> GetCompositorWorkspaces()
        {
            var provider = OpenTabletDriver.Desktop.Compositor.CompositorManager.GetActiveProvider(Settings?.CompositorSettings?.CompositorType);
            return provider.GetWorkspacesAsync();
        }

        public Task<bool> FocusCompositorWorkspace(string workspaceId)
        {
            var provider = OpenTabletDriver.Desktop.Compositor.CompositorManager.GetActiveProvider(Settings?.CompositorSettings?.CompositorType);
            return provider.FocusWorkspaceAsync(workspaceId);
        }

        public Task<bool> MoveWindowToCompositorWorkspace(string workspaceId, bool followFocus = true)
        {
            var provider = OpenTabletDriver.Desktop.Compositor.CompositorManager.GetActiveProvider(Settings?.CompositorSettings?.CompositorType);
            return provider.MoveWindowToWorkspaceAsync(workspaceId, followFocus);
        }

        public Task<bool> FocusCompositorWindow(OpenTabletDriver.Desktop.Compositor.WindowDirection direction)
        {
            var provider = OpenTabletDriver.Desktop.Compositor.CompositorManager.GetActiveProvider(Settings?.CompositorSettings?.CompositorType);
            return provider.FocusWindowAsync(direction);
        }

        public Task<bool> MoveCompositorWindow(OpenTabletDriver.Desktop.Compositor.WindowDirection direction)
        {
            var provider = OpenTabletDriver.Desktop.Compositor.CompositorManager.GetActiveProvider(Settings?.CompositorSettings?.CompositorType);
            return provider.MoveWindowAsync(direction);
        }

        public async Task ExecuteBinding(PluginSettingStore store)
        {
            if (store == null || string.IsNullOrWhiteSpace(store.Path))
                return;

            try
            {
                var serviceProvider = new ServiceManager();
                serviceProvider.AddService<IDriverDaemon>(() => this);

                if (DesktopInterop.RelativePointer is IMouseButtonHandler mouseButtonHandler)
                    serviceProvider.AddService(() => mouseButtonHandler);
                if (DesktopInterop.RelativePointer is IMouseScrollHandler mouseScrollHandler)
                    serviceProvider.AddService(() => mouseScrollHandler);
                if (DesktopInterop.VirtualKeyboard != null)
                    serviceProvider.AddService(() => DesktopInterop.VirtualKeyboard);

                var tabletRef = Driver.InputDevices.FirstOrDefault()?.CreateReference();
                var binding = store.Construct<IBinding>(serviceProvider, tabletRef);

                if (binding == null)
                    return;

                var emptyReport = new DeviceReport(Array.Empty<byte>());

                if (binding is PrecisionModeBinding precisionBinding)
                {
                    // For precision mode, toggle daemon precision state with configured sensitivity
                    _isPrecisionModeActive = !_isPrecisionModeActive;
                    Log.Write("PrecisionMode", $"HUD Precision mode toggled {(_isPrecisionModeActive ? "ON" : "OFF")}.");
                    return;
                }

                if (binding is IStateBinding stateBinding)
                {
                    stateBinding.Press(tabletRef!, emptyReport);
                    await Task.Delay(35);
                    stateBinding.Release(tabletRef!, emptyReport);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        public async Task ExecuteHudAction(HudAction action)
        {
            if (action == null) return;

            try
            {
                switch (action.Type)
                {
                    case HudActionType.KeySequence when !string.IsNullOrWhiteSpace(action.Value):
                        var keys = action.Value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (keys.Length > 0 && DesktopInterop.VirtualKeyboard != null)
                        {
                            DesktopInterop.VirtualKeyboard.Press(keys);
                            await Task.Delay(25);
                            DesktopInterop.VirtualKeyboard.Release(keys);
                        }
                        break;
                    case HudActionType.DriverCommand:
                        await HandleDriverCommand(action.Value, action.SecondaryValue);
                        break;
                    case HudActionType.MouseClick:
                        await HandleMouseClick(action.Value);
                        break;
                    case HudActionType.ShellCommand when !string.IsNullOrWhiteSpace(action.Value):
                        Process.Start(new ProcessStartInfo("/bin/bash", $"-c \"{action.Value.Replace("\"", "\\\"")}\"") { UseShellExecute = false });
                        break;
                    case HudActionType.Tool when !string.IsNullOrWhiteSpace(action.Value):
                        var tool = Settings?.ContextualTools?.FindTool(action.Value);
                        if (tool != null)
                        {
                            var winClass = CurrentWindowClass;
                            var seq = tool.ResolveKeySequence(winClass);
                            if (!string.IsNullOrWhiteSpace(seq))
                            {
                                var toolKeys = seq.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                                if (toolKeys.Length > 0 && DesktopInterop.VirtualKeyboard != null)
                                {
                                    DesktopInterop.VirtualKeyboard.Press(toolKeys);
                                    await Task.Delay(25);
                                    DesktopInterop.VirtualKeyboard.Release(toolKeys);
                                }
                            }
                        }
                        break;
                    case HudActionType.WorkspaceLayer when !string.IsNullOrWhiteSpace(action.Value):
                        await FocusCompositorWorkspace(action.Value);
                        break;
                    case HudActionType.WorkspaceLayer when string.IsNullOrWhiteSpace(action.Value):
                        await SwitchToWorkspaceSubLayer(isMoveWindow: false);
                        break;
                    case HudActionType.MoveWindowWorkspaceLayer when !string.IsNullOrWhiteSpace(action.Value):
                        await MoveWindowToCompositorWorkspace(action.Value, followFocus: true);
                        break;
                    case HudActionType.MoveWindowWorkspaceLayer when string.IsNullOrWhiteSpace(action.Value):
                        await SwitchToWorkspaceSubLayer(isMoveWindow: true);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        private async Task HandleDriverCommand(string? command, string? secondaryValue)
        {
            switch (command)
            {
                case "Preset" when !string.IsNullOrWhiteSpace(secondaryValue):
                    AppInfo.PresetManager.Refresh();
                    var preset = AppInfo.PresetManager.FindPreset(secondaryValue);
                    if (preset != null)
                    {
                        await SetSettings(preset.Settings.Clone());
                        await ForceResynchronize();
                    }
                    break;
                case "DisplayToggle":
                    CycleDisplay();
                    break;
                case "Workspace" when !string.IsNullOrWhiteSpace(secondaryValue):
                    await FocusCompositorWorkspace(secondaryValue);
                    break;
                case "PrecisionMode":
                    await TogglePrecisionMode();
                    break;
            }
        }

        private bool _isPrecisionModeActive;

        public Task<bool> IsPrecisionModeActive()
        {
            return Task.FromResult(_isPrecisionModeActive);
        }

        public Task TogglePrecisionMode()
        {
            _isPrecisionModeActive = !_isPrecisionModeActive;
            Log.Write("PrecisionMode", $"Precision mode toggled {(_isPrecisionModeActive ? "ON" : "OFF")}.");
            return Task.CompletedTask;
        }

        public string? CurrentWindowClass => _appProfileMonitor?.CurrentWindowClass;
        public string? CurrentWindowTitle => _appProfileMonitor?.CurrentWindowTitle;

        public Task<string?> GetActiveWindowClass()
        {
            _appProfileMonitor?.ForceRefreshActiveWindow();
            return Task.FromResult<string?>(_appProfileMonitor?.CurrentWindowClass);
        }

        public Task<string?> GetActiveWindowTitle()
        {
            _appProfileMonitor?.ForceRefreshActiveWindow();
            return Task.FromResult<string?>(_appProfileMonitor?.CurrentWindowTitle);
        }

        public Task<OpenTabletDriver.Desktop.AppProfiler.ActiveAppProfileContext?> GetActiveAppProfileContext()
        {
            _appProfileMonitor?.ForceRefreshActiveWindow();
            return Task.FromResult<OpenTabletDriver.Desktop.AppProfiler.ActiveAppProfileContext?>(_appProfileMonitor?.GetActiveContext());
        }

        private static async Task HandleMouseClick(string? button)
        {
            var btn = button?.ToLowerInvariant() switch
            {
                "middle" => MouseButton.Middle,
                "right" => MouseButton.Right,
                _ => MouseButton.Left
            };

            var mouseHandler = DesktopInterop.RelativePointer as IMouseButtonHandler;
            if (mouseHandler != null)
            {
                mouseHandler.MouseDown(btn);
                await Task.Delay(20);
                mouseHandler.MouseUp(btn);
            }
        }

        private void CycleDisplay()
        {
            var virtualScreen = DesktopInterop.VirtualScreen;
            if (virtualScreen == null || Settings == null) return;

            var displays = virtualScreen.Displays.ToList();
            if (displays.Count <= 1) return;

            foreach (var profile in Settings.Profiles)
            {
                if (profile.AbsoluteModeSettings != null)
                {
                    var currentDisplay = profile.AbsoluteModeSettings.Display;
                    int currentIndex = -1;
                    for (int i = 0; i < displays.Count; i++)
                    {
                        if (Math.Abs(displays[i].Position.X - currentDisplay.X) < 1 &&
                            Math.Abs(displays[i].Position.Y - currentDisplay.Y) < 1 &&
                            Math.Abs(displays[i].Width - currentDisplay.Width) < 1 &&
                            Math.Abs(displays[i].Height - currentDisplay.Height) < 1)
                        {
                            currentIndex = i;
                            break;
                        }
                    }

                    if (currentIndex >= 0 && currentIndex < displays.Count - 1)
                    {
                        var nextDisplay = displays[currentIndex + 1];
                        currentDisplay.Width = nextDisplay.Width;
                        currentDisplay.Height = nextDisplay.Height;
                        currentDisplay.X = nextDisplay.Position.X;
                        currentDisplay.Y = nextDisplay.Position.Y;
                        currentDisplay.Rotation = 0;
                        Log.Write("HUD", $"Cycled to display: {nextDisplay.Width}x{nextDisplay.Height} @ ({nextDisplay.Position.X},{nextDisplay.Position.Y})");
                    }
                    else if (currentIndex == displays.Count - 1)
                    {
                        currentDisplay.Width = virtualScreen.Width;
                        currentDisplay.Height = virtualScreen.Height;
                        currentDisplay.X = 0;
                        currentDisplay.Y = 0;
                        currentDisplay.Rotation = 0;
                        Log.Write("HUD", $"Cycled to all displays: {virtualScreen.Width}x{virtualScreen.Height}");
                    }
                    else
                    {
                        var first = displays[0];
                        currentDisplay.Width = first.Width;
                        currentDisplay.Height = first.Height;
                        currentDisplay.X = first.Position.X;
                        currentDisplay.Y = first.Position.Y;
                        currentDisplay.Rotation = 0;
                        Log.Write("HUD", $"Cycled to first display");
                    }
                }
            }

            _ = SetSettings(Settings);
        }

        private static void InitializePlatform()
        {
            switch (SystemInterop.CurrentPlatform)
            {
                case PluginPlatform.Windows:
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

                    if (Environment.OSVersion.Version.Build >= 22000) // Windows 11
                    {
                        unsafe
                        {
                            var state = Native.Windows.Windows.PowerThrottlingState.Create();
                            state.ControlMask = (int)Native.Windows.Windows.PowerThrottlingStateMask.IgnoreTimerResolution;

                            if (!Native.Windows.Windows.SetProcessInformation(
                                Process.GetCurrentProcess().Handle,
                                Native.Windows.Windows.ProcessInformationClass.ProcessPowerThrottling,
                                (IntPtr)Unsafe.AsPointer(ref state),
                                Unsafe.SizeOf<Native.Windows.Windows.PowerThrottlingState>()))
                            {
                                Log.Write("Platform", "Failed to allow management of timer resolution, asynchronous filters may have lower timing resolution when OTD is minimized.", LogLevel.Error);
                            }
                        }
                    }
                    break;
            }
        }
    }
}
