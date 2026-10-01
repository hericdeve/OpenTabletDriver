# Implementation Plan: Contextual Tool Actions & App Tools Manager

## Overview

Currently, users must create separate full tablet presets for every application just to change basic pen tools (Brush, Eraser, Selection, Hand/Pan, etc.) because each software assigns different keyboard shortcuts to these tools (e.g. `xournalpp` uses `Shift+Control+E` for eraser and `Shift+Control+R` for selection, while Obsidian Excalidraw uses `Control+Shift+E` and `V`, and Krita uses `E` and `T`).

This feature introduces a **Contextual Tools System**:
1. A dedicated **App Tools** management tab in OpenTabletDriver UX to manage semantic tool definitions with default keybinds and per-application overrides mapped to window classes.
2. Classic built-in tools (`Brush / Pen`, `Eraser`, `Selection`, `Hand / Pan`, `Color Picker`, `Undo`, `Redo`, `Zoom`) with pre-populated shortcuts across popular Linux/desktop applications, plus the ability to create arbitrary custom tools.
3. A `Tool Action` binding (`ToolBinding`) that can be bound directly to pen/auxiliary buttons, assigned to button modes in `MultiActionBinding` (Tap, Double-Click, Hold), or triggered from the Floating HUD.
4. An active window tracking service (`IActiveAppContext`) to resolve the active application's window class at input time.
5. A **🎯 Detect Active Window** helper in the UI to automatically grab the active window class from the compositor.

---

## Architectural Design

```
+───────────────────────────────────────────────────────────────────────────────────────────+
|                                    User Interaction                                       |
|  - Physical Pen Button (Direct ToolBinding)                                               |
|  - Multi-Action Binding (Tap = Selection, Double = Brush, Hold = Eraser)                  |
|  - Floating HUD (Radial Slice = Eraser)                                                   |
+───────────────────────────────────────────────────────────────────────────────────────────+
                                              │
                                              ▼
+───────────────────────────────────────────────────────────────────────────────────────────+
|                                      ToolBinding                                          |
|                                                                                           |
|  1. Queries IActiveAppContext -> CurrentWindowClass (e.g. "xournalpp" or "obsidian")     |
|  2. Fetches ToolDefinition from Settings.ContextualTools (e.g. "Eraser")                  |
|  3. Resolves Key Sequence:                                                                |
|     - "xournalpp" -> "Shift+Control+E"                                                    |
|     - "obsidian"  -> "Control+Shift+E"                                                    |
|     - Unmapped    -> "E" (Default fallback)                                               |
|  4. Dispatches to IKeyboardHandler:                                                       |
|     - Mode "Hold": Keyboard.Press on press -> Keyboard.Release on release                 |
|     - Mode "Tap / Toggle": Keyboard.Press + Keyboard.Release immediately on press         |
+───────────────────────────────────────────────────────────────────────────────────────────+
```

---

## Proposed Changes

### 1. Data Models (`OpenTabletDriver.Desktop/Tools/`)

Create `OpenTabletDriver.Desktop/Tools/ContextualToolsConfiguration.cs`:
- **`ContextualToolsConfiguration`**:
  - `List<ToolDefinition> Tools`: collection of tool definitions.
  - `GetDefaultTools()`: pre-populates classic tools with known mappings:
    - `Brush / Pen`: Default `B`; overrides for `xournalpp` (`P`), `obsidian` (`P`), `krita` (`B`), `gimp` (`P`), `inkscape` (`P`).
    - `Eraser`: Default `E`; overrides for `xournalpp` (`Shift+Control+E`), `obsidian` (`Control+Shift+E`), `krita` (`E`), `gimp` (`Shift+E`), `inkscape` (`Shift+E`).
    - `Selection`: Default `S`; overrides for `xournalpp` (`Shift+Control+R`), `obsidian` (`V`), `krita` (`T`), `gimp` (`R`), `inkscape` (`S`).
    - `Hand / Pan`: Default `Space`; overrides for `xournalpp` (`Shift+Control+H`), `obsidian` (`H`), `krita` (`Space`), `gimp` (`Space`).
    - `Color Picker`: Default `Alt`; overrides for `xournalpp` (`Shift+Control+C`), `krita` (`Control`), `gimp` (`O`), `inkscape` (`D`).
    - `Undo`: Default `Control+Z`.
    - `Redo`: Default `Control+Y`; overrides for `xournalpp` (`Control+Shift+Z`), `obsidian` (`Control+Shift+Z`), `krita` (`Control+Shift+Z`).
    - `Zoom`: Default `Z`.
- **`ToolDefinition`**:
  - `string Id`: unique slug.
  - `string Name`: display label.
  - `bool IsBuiltIn`: flag to distinguish built-ins from user-created tools.
  - `string DefaultBinding`: fallback shortcut string.
  - `List<ToolAppOverride> AppOverrides`: application-specific mappings.
  - `string ResolveKeySequence(string? windowClass)`: matches exact, then substring (case-insensitive), or returns `DefaultBinding`.
- **`ToolAppOverride`**:
  - `string WindowClass`: target application class identifier.
  - `string KeySequence`: key combination.

Integrate into `OpenTabletDriver.Desktop/Settings.cs`:
- Add `[JsonProperty(nameof(ContextualTools))] public ContextualToolsConfiguration ContextualTools { get; set; } = new();`

---

### 2. Active Window Context & Daemon Service (`OpenTabletDriver.Desktop/Contracts/`)

Create `OpenTabletDriver.Desktop/Contracts/IActiveAppContext.cs`:
- `string? CurrentWindowClass { get; }`
- `string? CurrentWindowTitle { get; }`

Update `OpenTabletDriver.Daemon/AppProfileMonitor.cs`:
- Expose `public string CurrentWindowClass => _lastActiveWindowClass;` and `CurrentWindowTitle => _lastActiveWindowTitle;`.
- Start `_activeProvider` unconditionally when supported (`_providers.Any(p => p.IsSupported)`), not only when `EnableAppProfiler` is true, ensuring window class is always tracked.
- Call `ForceRefreshActiveWindow()` on startup so the active window is populated immediately.

Update `OpenTabletDriver.Daemon/DriverDaemon.cs`:
- Implement `IActiveAppContext`:
  - `public string? CurrentWindowClass => _appProfileMonitor?.CurrentWindowClass;`
  - `public string? CurrentWindowTitle => _appProfileMonitor?.CurrentWindowTitle;`
- Register `IActiveAppContext` in plugin DI:
  - `AppInfo.PluginManager.AddService<IActiveAppContext>(() => this);`
- Update `IDriverDaemon.cs` and `DriverDaemon.cs`:
  - Add `Task<string?> GetActiveWindowClass();`
  - Add `Task<string?> GetActiveWindowTitle();`

---

### 3. Tool Binding Plugin (`OpenTabletDriver.Desktop/Binding/ToolBinding.cs`)

Create `OpenTabletDriver.Desktop/Binding/ToolBinding.cs`:
- `[PluginName("Tool Action")]`
- Properties:
  - `[Property("Tool"), PropertyValidated(nameof(ValidTools))] public string? Tool { get; set; }`
  - `[Property("Mode"), PropertyValidated(nameof(ValidModes))] public string Mode { get; set; } = "Hold";`
- Injections:
  - `[Resolved] public IActiveAppContext? AppContext { get; set; }`
  - `[Resolved] public IDriverDaemon? Daemon { get; set; }`
  - `[Resolved] public IKeyboardHandler? Keyboard { get; set; }`
- Execution:
  - `Press`: Resolves `AppContext.CurrentWindowClass`, looks up tool definition from daemon settings, resolves key sequence, parses keys, and presses them via `Keyboard.Press`. If `Mode == "Tap / Toggle"`, immediately releases the keys.
  - `Release`: If `Mode == "Hold"`, releases active keys via `Keyboard.Release`.
- Summary representation in `PluginSettingStore.cs`:
  - Format as `Tool: {Tool} ({Mode})`.

Update `OpenTabletDriver.Desktop/Binding/MultiActionBinding.cs`:
- Add `[Resolved] public IKeyboardHandler? KeyboardHandler { get; set; }`
- Add `[Resolved] public IActiveAppContext? AppContext { get; set; }`
- Register `IKeyboardHandler` and `IActiveAppContext` in `ServiceManager` in `OnDependencyLoad()` so `ToolBinding` operates inside `MultiActionBinding`.

---

### 4. Floating HUD Integration

Update `OpenTabletDriver.Desktop/Hud/`:
- Add `HudActionType.Tool` in `HudActionType.cs`.
- In `DriverDaemon.ExecuteHudAction`:
  - When `action.Type == HudActionType.Tool`, look up the tool in `Settings.ContextualTools`, resolve the key sequence against `CurrentWindowClass`, and send keys through `VirtualKeyboard`.

---

### 5. UI: App Tools Tab (`OpenTabletDriver.UX/Controls/ToolEditor.cs`)

Create `OpenTabletDriver.UX/Controls/ToolEditor.cs`:
- Split layout:
  - **Left Panel (Tools List)**:
    - List of available tools with built-in vs custom indicators.
    - `[+ Add Tool]` button: prompts for tool label, creates custom tool.
    - `[- Remove Tool]` button: deletes custom tool (disabled for built-in tools).
  - **Right Panel (Selected Tool Details)**:
    - **Label**: Editable name text box.
    - **Default Keybind**: Text box for fallback shortcut.
    - **Application Overrides Table**:
      - GridView displaying `Application (Class)` and `Keybind` with `✕ Remove` action per row.
    - **Add App Override Section**:
      - Window Class input.
      - **`[🎯 Detect Active Window]`** button: asynchronously calls `App.Driver.Instance.GetActiveWindowClass()` and populates the field with the currently focused application!
      - Keybind input (shortcut string).
      - `[+ Add Override]` button.

Update `OpenTabletDriver.UX/Controls/ControlPanel.cs`:
- Rename legacy empty plugin tools tab to `"Plugin Tools"`.
- Add new `"App Tools"` tab hosting `ToolEditor`.
- Bind `ToolEditor` to `App.Current.Settings.ContextualTools`.

---

### 6. Testing & Verification

1. **Unit Tests (`OpenTabletDriver.Tests/ToolBindingTests.cs`)**:
   - `ToolBinding_ResolvesDefaultKey_WhenWindowClassUnmapped`: verify fallback to `DefaultBinding`.
   - `ToolBinding_ResolvesAppOverride_OnExactMatch`: verify `xournalpp` gets `Shift+Control+E`.
   - `ToolBinding_ResolvesAppOverride_OnSubstringMatch`: verify `com.github.xournalpp.xournalpp` matches `xournalpp`.
   - `ToolBinding_HoldMode_PressesAndReleasesKeys`: verify press on down, release on up.
   - `ToolBinding_TapMode_PressesAndReleasesImmediatelyOnPress`: verify momentary impulse.
   - `ToolBinding_CustomTool_WorksAcrossApps`: verify user-created custom tool resolution.
   - `ToolBinding_InsideMultiActionBinding`: verify `ToolBinding` execution inside `MultiActionBinding`.
2. **Build & Suite Verification**:
   - Verify `./build.sh linux` compiles with 0 errors.
   - Verify all existing and new unit tests pass with `dotnet test OpenTabletDriver.Tests`.
