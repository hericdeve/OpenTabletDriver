# Implementation Plan: Native Precision Mode

## 1. Overview & Problem Statement
Precision Mode enables artists, designers, and tablet users to temporarily drop stylus sensitivity (e.g. to 30% or 50% speed, providing 2x–5x spatial precision) for fine linework, detailing, retouching, and pixel-accurate selections.

Previously, OpenTabletDriver users had to rely on an external plugin (`PrecisionControl` by X9VoiD), which suffered from several critical flaws:
1. **Awkward Two-Part Setup**: Required enabling a filter in the *Filters* tab and separately configuring a binding in the *Bindings* tab. If either was forgotten, the feature silently failed.
2. **Static Global State**: Used static fields (`PrecisionControlBinding.StartingPoint`, `IsActive`, `SetPosition`), causing state cross-contamination across multiple tablets, profiles, and presets.
3. **No Proximity Lift Re-Anchoring**: When lifting the stylus to reposition the hand while Precision Mode was active, touching down at another location caused the cursor to violently jerk across the screen because the delta was still measured against the old contact point from before lifting.
4. **No Floating HUD Integration**: The Floating HUD already includes a "Precision" slice (DriverCommand `PrecisionMode`), but it lacked native backend wiring.

This implementation replaces the external plugin with a first-class, built-in **Native Precision Mode** integrated directly into `OpenTabletDriver.Desktop`, `OpenTabletDriver.Plugin`, and `OpenTabletDriver.Daemon`.

---

## 2. Architecture & Design

```
+-----------------------------------------------------------------------------------+
|                                  Tablet Digitizer                                 |
+-----------------------------------------------------------------------------------+
                                         │
                                         ▼
+-----------------------------------------------------------------------------------+
|                               OutputMode.Transform                                |
|          (Converts raw digitizer coords -> virtual screen pixel coords)           |
+-----------------------------------------------------------------------------------+
                                         │
                                         ▼
+-----------------------------------------------------------------------------------+
|                                  BindingHandler                                   |
|                          (PipelinePosition.PostTransform)                         |
|                                                                                   |
|  1. HandleBinding(report):                                                        |
|     - Evaluates button presses/releases -> activates/deactivates IPrecisionModifier|
|  2. ApplyPrecisionScaling(report):                                                |
|     - Queries active IPrecisionModifier or Daemon.IsPrecisionModeActive           |
|     - If active: transforms position via precision delta scaling & re-anchors     |
|     - Preserves full pen pressure, tilt, eraser, and barrel buttons               |
|  3. ApplyPointerSuppression(report):                                              |
|     - Handles Pan/Scroll and HUD suppression if active                            |
|  4. Emit downstream to OS pointer (EvdevVirtualTablet / EvdevVirtualMouse)        |
+-----------------------------------------------------------------------------------+
```

### Key Components

1. **`IPrecisionModifier` Interface (`OpenTabletDriver.Plugin`)**:
   ```csharp
   namespace OpenTabletDriver.Plugin
   {
       public interface IPrecisionModifier
       {
           bool IsActive { get; }
           float Scale { get; }
           bool ReanchorOnLift { get; }
       }
   }
   ```

2. **`PrecisionModeBinding` (`OpenTabletDriver.Desktop/Binding/PrecisionModeBinding.cs`)**:
   - Implements `IStateBinding`, `IPrecisionModifier`.
   - Attributes:
     - `[PluginName("Precision Mode")]`
     - `Mode`: `"Hold"` (active while held down, returns to normal on release) or `"Toggle"` (press to turn on, press again to turn off). Default: `"Hold"`.
     - `Sensitivity`: Slider/percentage `10%` to `400%` (default `30%`, equivalent to scale `0.3f`), supporting both precision detailing (<100%) and speed navigation (>100%).
     - `Reset on Lift`: Boolean (default `true`). Re-anchors reference point on stylus lift / proximity loss for smooth multi-stroke drawing. When set to `false`, retains the plugin's fixed-center behavior.

3. **Daemon State & HUD Driver Command Integration (`DriverDaemon.cs` & `IDriverDaemon.cs`)**:
   - `IDriverDaemon`: Adds `bool IsPrecisionModeActive { get; set; }` and `void TogglePrecisionMode()`.
   - `DriverDaemon.HandleDriverCommand`:
     - Matches `"PrecisionMode"`: toggles `IsPrecisionModeActive`, logs status, and instructs `BindingHandler`.
   - Allows users to toggle Precision Mode seamlessly from the Floating HUD (radial slice 7:30 or Quick Bar) even without a dedicated physical barrel button.

4. **Pipeline Execution in `BindingHandler.cs`**:
   - In `BindingHandler.Consume`:
     - Executes after `HandleBinding(report)` so button state is updated immediately on the triggering frame.
     - If an active `IPrecisionModifier` exists or `Daemon.IsPrecisionModeActive` is true:
       - On activation or after `OutOfRangeReport`:
         - `_precisionAnchorTablet = absReport.Position;`
         - `_precisionAnchorScreen = absReport.Position;`
       - During motion:
         - `delta = absReport.Position - _precisionAnchorTablet.Value;`
         - `absReport.Position = _precisionAnchorScreen.Value + delta * scale;`
     - Full tip pressure (`ITabletReport.Pressure`), tilt (`ITiltReport`), and eraser (`IEraserReport`) are passed through unaltered.

---

## 3. Mathematical Model & Edge Cases

### Coordinate Scaling
- Let $T$ be the incoming transformed screen position from the digitizer.
- Let $T_{anchor}$ be the reference touch/hover coordinate on the tablet where precision mode was initiated or re-anchored.
- Let $S_{anchor}$ be the output screen coordinate at that moment.
- The output position is:
  $$S_{out} = S_{anchor} + (T - T_{anchor}) \times \text{Scale}$$
- For example, with $\text{Scale} = 0.3$:
  - Moving the pen 100 screen units across the tablet moves the cursor only 30 pixels on screen.
  - The artist enjoys 3.33x finer control over their pen tip.

### Edge Cases
1. **Repositioning Hand (Stylus Lift / Proximity Loss)**:
   - When an artist draws in Precision Mode (especially in `Toggle` mode), they frequently lift the stylus to make multiple strokes.
   - When an `OutOfRangeReport` occurs:
     - Flag `_needsPrecisionReanchor = true`.
   - When the stylus comes back into range:
     - $S_{anchor}$ is preserved at the last output cursor position.
     - $T_{anchor}$ is updated to the new contact point $T_{new}$.
     - **Result**: The cursor stays stationary at the end of the previous stroke without jumping across the canvas when landing at a different spot on the tablet.
2. **Deactivation**:
   - When the button is released (in `Hold` mode) or toggled off:
     - Clear `_precisionAnchorTablet` and `_precisionAnchorScreen`.
     - The next report passes through at true 1:1 absolute position.
3. **Screen Boundary Clamping**:
   - Output position is clamped to the virtual screen / display area so the cursor cannot escape monitor bounds.
4. **Pressure Curve Preservation**:
   - Drawing pressure is completely unaffected by precision coordinate scaling, allowing natural line-weight variation.

---

## 4. Implementation Steps

### Phase 1: Core Plugin Contract
- Create `OpenTabletDriver.Plugin/IPrecisionModifier.cs` declaring `IsActive`, `Scale`, and `ReanchorOnLift`.

### Phase 2: Native Precision Mode Binding
- Create `OpenTabletDriver.Desktop/Binding/PrecisionModeBinding.cs`:
  - Implement `IStateBinding` and `IPrecisionModifier`.
  - Expose `Mode` (`Hold`, `Toggle`), `Sensitivity` (default 30%), and `ReanchorOnLift` (default true) with standard OpenTabletDriver GUI attributes.

### Phase 3: Daemon RPC & HUD Integration
- Update `OpenTabletDriver.Desktop/Contracts/IDriverDaemon.cs` with `bool IsPrecisionModeActive` and `void TogglePrecisionMode()`.
- Update `OpenTabletDriver.Daemon/DriverDaemon.cs`:
  - Implement `IsPrecisionModeActive` and `TogglePrecisionMode()`.
  - Wire `HandleDriverCommand("PrecisionMode")` to call `TogglePrecisionMode()`.

### Phase 4: BindingHandler Pipeline Integration
- Update `OpenTabletDriver.Desktop/Binding/BindingHandler.cs`:
  - Add `ApplyPrecisionScaling(IDeviceReport report)` invoked after `HandleBinding(report)` in `Consume()`.
  - Track `_precisionAnchorTablet`, `_precisionAnchorScreen`, and `_needsPrecisionReanchor`.
  - Handle `OutOfRangeReport` to reset or trigger re-anchoring when `ReanchorOnLift` is enabled.
  - Reset state in `ReleaseAllBindings()`.

### Phase 5: Verification & Unit Tests
- Add comprehensive unit tests in `OpenTabletDriver.Tests/PrecisionModeTests.cs`:
  - `PrecisionMode_Hold_ScalesCoordinatesCorrectly`
  - `PrecisionMode_Toggle_ActivatesAndDeactivates`
  - `PrecisionMode_OutOfRange_ReanchorsWithoutJump`
  - `PrecisionMode_PreservesTipPressureAndTilt`
- Run `./build.sh linux` and run all tests.

---

## 5. User Workflow Comparison

| Action | Old External Plugin | Native Precision Mode |
|---|---|---|
| **Installation** | Required downloading plugin ZIP / DLL from GitHub | Built into core OpenTabletDriver out of the box |
| **Setup** | 1. Enable filter in Filters tab<br>2. Set multiplier<br>3. Open Bindings tab<br>4. Assign binding | Right-click button -> Select **Precision Mode** -> Set % speed (or use default 30%) |
| **Floating HUD** | Not supported | Select "Precision Mode" directly from Radial HUD / Quick Bar |
| **Stylus Lift** | Cursor jumped wildly on new stroke | Smooth re-anchoring; cursor stays in place across strokes |
| **Multi-device / Profiles** | Broken by static state | Fully instance-isolated and reactive |
