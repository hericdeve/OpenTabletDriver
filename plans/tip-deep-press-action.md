# Implementation Plan: Tip Deep Press / 3D Touch Action (Feature #6)

## 1. Overview & Problem Definition

### 1.1 Context & Use Case
For technical note-taking, handwriting, and drawing circuit diagrams or engineering schematics (e.g. in Xournal++, Obsidian, Rnote, or Krita), stylus pressure sensitivity is frequently undesirable:
- Natural handwriting and circuit line work (resistors, logic gates, wires) require **crisp, uniform line thickness and opacity**. Variable pressure introduces tapering, blobbing at stroke origins, and faint lines during quick handwriting strokes.
- However, modern stylus hardware continuously reports high-resolution physical tip force (typically 0 to 8,192 or 16,384 levels).
- **The iPhone 3D Touch / Force Touch Concept:**
  On devices with pressure sensing, physical force can be split into two operational zones:
  1. **Zone 1: Normal Contact / Writing (Light to Medium Force):** Normal pen tip touch (Left Click / Draw). Strokes remain completely uniform without line-width variance.
  2. **Zone 2: Deep Press / Pressure Apply (Firm Force > Threshold):** Firmly pressing down past a high threshold activates a **Secondary Binding** (e.g. Right Click for Context Menus, Floating HUD, Eraser tool switch, or shortcuts).

### 1.2 Key Design Requirements
1. **Configurable High Threshold for Zone 2:** The pressure required to trigger the secondary action must be configurable with a **high default** (default: **80%**, range: 50%–98%).
2. **Stroke Uniformity (No Pressure Changes on Lines):** Drawing strokes in Zone 1 must maintain fixed, constant pressure (100% uniform line width) so handwriting and circuit lines never fluctuate.
3. **Stroke Cancellation (No Ink Blots on Deep Press):** When pressing firmly to summon a context menu or HUD, the driver must cancel/suppress the drawing stroke so no stray ink dot or blot is drawn on the diagram.
4. **Anti-Jitter Hold Delay (Debounce):** Fast handwriting downstrokes can produce brief 10–20 ms pressure spikes. An intentional deep press requires holding the firm force for a configurable duration (default: **60 ms**, range: 0–300 ms).
5. **Hysteresis Deadband:** To prevent rapid on/off flickering when the user's hand pressure fluctuates near the threshold, a release hysteresis band (e.g. 8–10%) keeps the action steadily active until pressure drops below the release threshold.

---

## 2. Dual-Zone Algorithmic Specification

### 2.1 Zone Architecture

```
0%                                Threshold (e.g. 80%)            100%
┌──────────────────────────────────────────┬────────────────────────┐
│        Zone 1: Normal Writing            │   Zone 2: Deep Press   │
│  - Tip Binding Active (Left Click / Pen) │  - Tip Suppressed      │
│  - Constant 100% Stroke Width            │  - Secondary Binding   │
│    (No line thickness variation)         │    Active (Right Click │
│                                          │    or Floating HUD)    │
└──────────────────────────────────────────┴────────────────────────┘
 ▲                                          ▲
 │                                          │
TipActivationThreshold (e.g. 1%)      TipDeepPressThreshold (e.g. 80%)
```

### 2.2 State Machine

```
                   ┌──────────────┐
                   │     Idle     │◄───────────────────────────────────┐
                   └──────┬───────┘                                    │
                          │ Pressure > TipActivationThreshold          │
                          ▼                                            │
               ┌─────────────────────┐                                 │
               │ Zone 1: Pen Writing │                                 │
               │ (Tip Active, Draw)  │                                 │
               └──────────┬──────────┘                                 │
                          │                                            │
                          │ Pressure >= DeepPressThreshold (e.g. 80%)  │
                          ▼                                            │
               ┌─────────────────────┐                                 │
               │  PendingDeepPress   │                                 │
               │ (Timer Starting...) │                                 │
               └─────┬─────────┬─────┘                                 │
                     │         │                                       │
     Timer < HoldDelayMs       │ Timer >= HoldDelayMs (e.g. 60 ms)     │
     & Pressure Drops          ▼                                       │
     (Handwriting downstroke)  ┌────────────────────────────────────┐  │
             │                 │ Zone 2: Deep Press Active          │  │
             └────────►(Back)  │ - Tip Released (Stroke Cancelled)  │  │
                               │ - DeepPressBinding Activated       │  │
                               │ - Outgoing Report Pressure = 0     │  │
                               └─────────────────┬──────────────────┘  │
                                                 │                     │
                                                 │ Pressure Drops Below│
                                                 │ (Threshold - 8%)    │
                                                 ▼                     │
                                       ┌───────────────────┐           │
                                       │ ReleaseDeepPress  │───────────┘
                                       │ (Pen Lift / Idle) │
                                       └───────────────────┘
```

### 2.3 Debounce & Anti-Jitter Hold Timing
- Uses high-resolution `Stopwatch.GetTimestamp()`:
  $$\Delta t = \frac{\text{Timestamp} - \text{AnchorTimestamp}}{\text{Stopwatch.Frequency}} \times 1000 \text{ ms}$$
- If $\Delta t < \text{HoldDelayMs}$, the state machine remains in `PendingDeepPress`. Normal tip drawing is held without dispatching the deep press action.
- If the user immediately eases off, it returns to `Zone 1` without ever triggering the deep press action.
- If held $\ge \text{HoldDelayMs}$, `Zone 2` engages.

### 2.4 Stroke Suppression & Cancellation
When Zone 2 activates:
1. `Tip.Invoke(tablet, report, false)` is called immediately to release any active touch/left click.
2. In `BindingHandler.ApplyPointerSuppression`, `suppressTip` is set to `true`, forcing `tabletReport.Pressure = 0` on the report forwarded to `OnOutput`.
3. In `LinuxArtistMode` (`EvdevVirtualTablet`) / `AbsoluteOutputMode`, setting pressure to 0 releases `BTN_TOUCH`, ensuring the active application terminates the stroke cleanly without drawing an ink blot at the menu anchor point.
4. `DeepPressBinding.Invoke(tablet, report, true)` is dispatched.

---

## 3. Stroke Uniformity & Constant Pressure Pipeline

To guarantee that strokes do not change thickness with pressure:
1. **`UniformStrokePressure` Property:** Added to `BindingSettings`. When enabled (or when `DisablePressure = true`):
   - In `AbsoluteOutputMode.cs`:
     ```csharp
     if (report is ITabletReport tabletReport && Pointer is IPressureHandler pressureHandler &&
         Tablet?.Properties.Specifications.Pen != null)
     {
         if (DisablePressure || profile.BindingSettings.UniformStrokePressure)
         {
             // Clamps pressure to constant 100% while touching surface
             pressureHandler.SetPressure(tabletReport.Pressure > 0 ? 1.0f : 0.0f);
         }
         else
         {
             pressureHandler.SetPressure(tabletReport.Pressure / (float)Tablet.Properties.Specifications.Pen.MaxPressure);
         }
     }
     ```
2. **Benefit:**
   - Resolves the existing driver bug in `LinuxArtistMode` where checking `Disable Pressure` previously broke `BTN_TOUCH` completely.
   - All stroke engines (Xournal++, Rnote, Krita, Inkscape) receive constant 100% pressure, yielding uniform engineering lines.
   - Meanwhile, `BindingHandler` and `DeepPressBindingState` read the raw hardware pressure report to evaluate the Zone 2 transition.

---

## 4. Data Models & Configuration Schema

### 4.1 `BindingSettings.cs` (`OpenTabletDriver.Desktop/Profiles/`)
Add the following properties:
```csharp
[JsonProperty(nameof(TipDeepPressButton))]
public PluginSettingStore? TipDeepPressButton
{
    set => this.RaiseAndSetIfChanged(ref this.tipDeepPressButton, value);
    get => this.tipDeepPressButton;
}

[JsonProperty(nameof(TipDeepPressThreshold))]
public float TipDeepPressThreshold
{
    set => this.RaiseAndSetIfChanged(ref this.tipDeepPressThreshold, value);
    get => this.tipDeepPressThreshold;
} = 80.0f; // High default as requested

[JsonProperty(nameof(TipDeepPressHoldDelayMs))]
public int TipDeepPressHoldDelayMs
{
    set => this.RaiseAndSetIfChanged(ref this.tipDeepPressHoldDelayMs, value);
    get => this.tipDeepPressHoldDelayMs;
} = 60; // 60 ms anti-jitter hold delay

[JsonProperty(nameof(TipDeepPressSuppressStroke))]
public bool TipDeepPressSuppressStroke
{
    set => this.RaiseAndSetIfChanged(ref this.tipDeepPressSuppressStroke, value);
    get => this.tipDeepPressSuppressStroke;
} = true;

[JsonProperty(nameof(UniformStrokePressure))]
public bool UniformStrokePressure
{
    set => this.RaiseAndSetIfChanged(ref this.uniformStrokePressure, value);
    get => this.uniformStrokePressure;
} = false;
```

---

## 5. Driver Daemon & Pipeline Integration

### 5.1 `DeepPressBindingState.cs` (`OpenTabletDriver.Desktop/Binding/`)
A dedicated state wrapper extending `BindingState`:
```csharp
public class DeepPressBindingState : BindingState
{
    public float ActivationThreshold { get; set; } = 80.0f;
    public float ReleaseHysteresis { get; set; } = 8.0f; // Releases at (Threshold - 8%)
    public int HoldDelayMs { get; set; } = 60;
    public bool SuppressStroke { get; set; } = true;

    public bool IsDeepPressed { get; private set; }
    public bool IsPending { get; private set; }

    public bool ProcessReport(TabletReference tablet, IDeviceReport report, float pressurePercent, out bool suppressTip);
    public void Reset(TabletReference tablet, IDeviceReport report);
}
```

### 5.2 `BindingHandler.cs` Integration
- Add `TipDeepPress` property (`DeepPressBindingState?`).
- In `HandleTabletReport`:
  - Call `TipDeepPress.ProcessReport(tablet, report, pressurePercent, out bool suppressTip)`.
  - If `suppressTip == true`, normal `Tip` binding is released and outgoing report pressure is zeroed.
  - If `IsDeepPressed == false`, normal `Tip` binding processes `pressurePercent`.
- In `ApplyPointerSuppression`:
  - Incorporate `TipDeepPress.IsDeepPressed && SuppressStroke` into tip suppression.
- In `HandleOutOfRange`:
  - Reset `TipDeepPress`.

### 5.3 `DriverDaemon.cs` Construction
In `CreateBindingHandler`:
```csharp
if (settings.TipDeepPressButton != null)
{
    bindingHandler.TipDeepPress = new DeepPressBindingState
    {
        Binding = settings.TipDeepPressButton.Construct<IBinding>(bindingServiceProvider, tabletReference),
        ActivationThreshold = settings.TipDeepPressThreshold,
        HoldDelayMs = settings.TipDeepPressHoldDelayMs,
        SuppressStroke = settings.TipDeepPressSuppressStroke
    };
    Log.Write(group, $"Tip Deep Press: [{bindingHandler.TipDeepPress.Binding}]@{settings.TipDeepPressThreshold}%");
}
```

---

## 6. User Interface (GTK UX in `OpenTabletDriver.UX`)

### 6.1 `PenBindingEditor.cs`
Under **Tip Settings**:
1. **Tip Binding**: Primary binding (`BindingDisplay`, default: Left Click / Draw).
2. **Tip Threshold**: Primary threshold slider (1%–5%).
3. **Deep Press (Zone 2) Group**:
   - **Deep Press Binding**: Secondary binding (`BindingDisplay`, e.g. Right Click, Floating HUD, Tool switch).
   - **Deep Press Threshold**: `FloatSlider` with `%` unit, range `50%` to `98%`, default **`80%`**.
   - **Hold Delay**: `FloatSlider` or numeric input with `ms` unit, range `0 ms` to `250 ms`, default **`60 ms`**.
   - **Suppress Stroke on Deep Press**: `CheckBox` (default: checked).
4. Under **Miscellaneous**:
   - **Uniform Stroke Pressure**: `CheckBox` (tooltip: "Locks drawing stroke pressure to constant 100% for uniform engineering lines").

---

## 7. Implementation Phases

| Phase | Tasks | Key Files |
|---|---|---|
| **Phase 1: Domain Models & State Machine** | Define `TipDeepPressButton`, threshold, delay, and uniform pressure properties in `BindingSettings.cs`. Implement `DeepPressBindingState.cs` with timer debounce, hysteresis, and stroke suppression. | `BindingSettings.cs`, `DeepPressBindingState.cs` |
| **Phase 2: Pipeline & BindingHandler** | Wire `TipDeepPress` into `BindingHandler.cs` (`HandleTabletReport`, pointer suppression, out-of-range reset). Update `AbsoluteOutputMode.cs` to ensure `BTN_TOUCH` and constant pressure work reliably under `DisablePressure`/`UniformStrokePressure`. | `BindingHandler.cs`, `AbsoluteOutputMode.cs` |
| **Phase 3: Daemon Service Construction** | Update `DriverDaemon.cs` `CreateBindingHandler` to instantiate and configure `DeepPressBindingState`. Wire logging and synchronization. | `DriverDaemon.cs` |
| **Phase 4: GTK UX Settings Editor** | Update `PenBindingEditor.cs` to expose Deep Press binding selector, threshold slider (default 80%), hold delay, and uniform stroke toggle. | `PenBindingEditor.cs` |
| **Phase 5: Automated Testing & Verification** | Create unit tests covering threshold activation, anti-jitter debounce, stroke suppression, hysteresis release, and uniform pressure. Compile via `./build.sh linux`. | `TipDeepPressTests.cs` |

---

## 8. Verification & Test Plan

1. **Automated Unit Tests (`OpenTabletDriver.Tests/TipDeepPressTests.cs`):**
   - `Normal_Stroke_Under_Threshold_Does_Not_Trigger_Deep_Press`
   - `Deep_Press_Triggers_When_Exceeding_Threshold_For_Hold_Duration`
   - `Fast_Downstroke_Under_Hold_Duration_Does_Not_Trigger_Deep_Press`
   - `Suppress_Stroke_Zeroes_Outgoing_Report_Pressure`
   - `Hysteresis_Prevents_Oscillation_When_Pressure_Fluctuates`
   - `Pen_Lift_OutOfRange_Resets_Deep_Press_State`
   - `Uniform_Stroke_Pressure_Outputs_Constant_Max_Pressure`
2. **Build Verification:**
   - Execute `./build.sh linux` and ensure 0 compilation errors.
   - Run `dotnet test OpenTabletDriver.Tests` and ensure 100% test passage.
3. **End-to-End Functional Test:**
   - Set Tip Binding = Left Click (or default).
   - Set Deep Press Binding = Right Click (or Floating HUD).
   - Verify light writing draws uniform lines without menu popups.
   - Verify firm press (> 80%) instantly opens context menu / HUD with zero ink marks left behind.
