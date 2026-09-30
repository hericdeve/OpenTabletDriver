# Implementation Plan: Instant Preset Activation When Untoggling App-Profiling

## Problem Summary
When a user untoggles "Enable app profiling" (setting `EnableAppProfiler = false`) and manually applies a preset (e.g., from the File -> Presets menu), the preset does not take effect immediately or is overridden/incompletely applied unless the daemon is restarted.

### Root Causes Identified
1. **Daemon State & Reset Loop in `AppProfileMonitor.Initialize()`**:
   - In `AppProfileMonitor.cs`: When `EnableAppProfiler` becomes false, `Initialize()` stops the active window provider and layer tracker, and then executes:
     ```csharp
     if (_daemon.BaseSettings != null)
     {
         _ = _daemon.SetSettings(_daemon.BaseSettings.Clone(), false);
     }
     _currentPreset = null;
     _currentOutputMode = null;
     ```
   - If the user untoggles app-profiling in UX, `SetAppProfilerSettings` is sent to the daemon, which fires `_appProfileMonitor.Initialize()`. Because `_daemon.BaseSettings` was preserved or points to the baseline before app profiling, it asynchronously resets daemon settings to `_daemon.BaseSettings`.
   - Furthermore, in `DriverDaemon.cs`:
     ```csharp
     public Task SetSettings(Settings? settings, bool isAppProfileUpdate)
     {
         ...
         if (!isAppProfileUpdate)
         {
             _appProfileMonitor.Initialize();
         }
     }
     ```
     When a preset is manually loaded (via `PresetButtonHandler` -> `App.Driver.Instance.SetSettings(settingsToApply)`), `isAppProfileUpdate` is `false`. This immediately calls `_appProfileMonitor.Initialize()`.
   - If `EnableAppProfiler` is `false`, calling `_appProfileMonitor.Initialize()` again will hit:
     ```csharp
     else if (!enableAppProfiler && _activeProvider != null)
     ```
     If `_activeProvider` was already stopped, it does nothing. But notice: `_daemon.BaseSettings` in `DriverDaemon`:
     ```csharp
     if (!isAppProfileUpdate && settings != null)
     {
         BaseSettings = settings.Clone();
     }
     ```
     When `SetSettings(presetSettings)` runs, `BaseSettings` gets overwritten by the preset settings.
   - However, look closely at `PresetButtonHandler` in `MainForm.cs`:
     ```csharp
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
     ```
     Notice that `PresetButtonHandler`:
     a. Does **NOT** update `App.Current.Settings` locally in UX! So the UI editor displays stale settings, and if the user clicks Save or Apply later, or another sync occurs, stale settings are sent.
     b. Does **NOT** call `Resynchronize` on the daemon or notify UI.
     c. Most importantly: In `AppProfileMonitor.cs`:
        When app-profiling was active, `_currentPreset` or `_currentOutputMode` or an in-flight window change or layer hover event might be running. When `EnableAppProfiler` is untoggled, `_activeProvider.Stop()` is called, but `HyprlandTrackingThread` (`_layerTracker`) and `HyprlandWindowProvider` might have pending tasks or locks.
     d. In `AppProfileMonitor.cs`, lines 101-104:
        ```csharp
        // Restore base settings when profiler is disabled
        if (_daemon.BaseSettings != null)
        {
            _ = _daemon.SetSettings(_daemon.BaseSettings.Clone(), false);
        }
        ```
        This call is an unawaited fire-and-forget task (`_ = _daemon.SetSettings(...)`)! If the user untoggles app-profiling and clicks a preset quickly, this fire-and-forget `SetSettings(_daemon.BaseSettings.Clone())` race condition can execute AFTER or concurrently with the manual preset load, immediately reverting the manual preset back to the old `BaseSettings`!
     e. In `DriverDaemon.cs`, when output modes change in `SetSettings`:
        ```csharp
        foreach (var dev in Driver.InputDevices)
        {
            if (dev.OutputMode?.Elements != null)
            {
                foreach (var bindingHandler in dev.OutputMode.Elements.OfType<BindingHandler>())
                    bindingHandler.ReleaseAllBindings();
            }

            dev.OutputMode?.Dispose();
        }
        ```
        If the preset specifies an output mode (like `LinuxArtistMode` or `AbsoluteMode`), but `AppProfileMonitor._currentOutputMode` or `_currentPreset` was not cleared or is stale, or if `DriverDaemon.SetSettings` doesn't re-instantiate output mode pointers or screen bounds properly without a daemon restart, the input pipeline may continue using the previous output mode state.
     f. Furthermore, when `EnableAppProfiler` is toggled off in `MainForm.cs`:
        ```csharp
        toggleAppPresets.Executed += async (sender, e) =>
        {
            if (App.Current.AppProfilerSettings is AppProfilerSettings appSettings)
            {
                appSettings.EnableAppProfiler = toggleAppPresets.Checked;
                toggleSyncFocus.Enabled = toggleAppPresets.Checked;
                await App.Driver.Instance.SetAppProfilerSettings(appSettings);
            }
        };
        ```
        `SetAppProfilerSettings` in `DriverDaemon` calls:
        ```csharp
        public Task SetAppProfilerSettings(AppProfilerSettings settings)
        {
            AppProfilerSettings = settings ?? new AppProfilerSettings();
            var file = new FileInfo(AppInfo.Current.AppProfilesFile);
            AppProfilerSettings.Serialize(file);
            _appProfileMonitor.Initialize();
            Resynchronize?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
        ```
        `_appProfileMonitor.Initialize()` fires the asynchronous unawaited:
        `_ = _daemon.SetSettings(_daemon.BaseSettings.Clone(), false);`
        And then `Resynchronize` tells UX to `SyncSettings()`.
        If the user clicks Preset right after unchecking the box, the race condition clobbers the preset.
     g. What if the preset has different output modes or monitor geometries? In `ApplyActiveProfileAsync`, monitor geometry is adapted, but when manually loading a preset, `SetSettings` applies `preset.Settings` directly. If display area / tablet area in the preset was saved for a different resolution or needs monitor matching, or if `dev.OutputMode` needs re-initialization.

---

## Proposed Solution & Architecture

### 1. Fix the Race Condition in `AppProfileMonitor.Initialize()`
- In `AppProfileMonitor.cs`:
  When profiler is disabled (`!enableAppProfiler && _activeProvider != null`):
  - Do NOT trigger an unawaited background task `_ = _daemon.SetSettings(...)`.
  - Instead, await the restore or make `Initialize()` / `DisableAsync()` properly synchronized with `_profileLock`.
  - Reset `_currentPreset = null;` and `_currentOutputMode = null;` immediately before releasing or restoring settings.
  - Ensure that when a manual `SetSettings(..., isAppProfileUpdate: false)` is called, any pending app profile transitions are canceled or aborted.

### 2. Synchronize UX State and Notify Clients in `PresetButtonHandler`
- In `MainForm.cs` `PresetButtonHandler`:
  - Update `App.Current.Settings = settingsToApply;`
  - Await `App.Driver.Instance.SetSettings(settingsToApply);`
  - Ensure the daemon's `BaseSettings` is updated to the newly loaded preset so subsequent checks or restorations default to this preset.
  - Call `await SyncSettings();` (or ensure `DriverDaemon.SetSettings` fires `Resynchronize` / refreshes UI bindings).

### 3. Ensure Immediate Pipeline & Output Mode Reconfiguration in Daemon
- When `DriverDaemon.SetSettings(settings, false)` is called:
  - Ensure `BaseSettings` is cleanly updated.
  - Reset `_currentPreset` and `_currentOutputMode` in `AppProfileMonitor` so that if app profiling is re-enabled later, it doesn't think the old preset is already active.
  - Ensure all input devices re-bind their output modes, pipeline elements, and virtual screen mapping immediately.
  - Ensure `Resynchronize?.Invoke(this, EventArgs.Empty);` is raised so any open UX or Tray components immediately reflect the applied preset.

---

## Verification Plan
1. Untoggle "Enable app profiling" in the UX menu.
2. Select a preset (e.g. `Xournal`, `Desktop`, `Obsidian`) from the Presets menu.
3. Verify that the tablet mapping, bindings, and output mode take effect immediately without restarting `OpenTabletDriver.Daemon`.
4. Check daemon logs to confirm no unhandled exceptions or reverted settings occur.
