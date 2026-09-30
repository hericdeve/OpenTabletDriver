# Implementation Plan: Double-Click Assist (Feature #2)

## 1. Overview & Problem Definition

In graphics tablets and pen displays, rapid double-tapping on the tablet surface is prone to coordinate jitter:
- When a user taps the stylus tip twice in rapid succession (e.g., to open a folder, launch an application, or select a word), slight hand tremor, tablet friction, or pen tilt variations shift the contact point by several pixels between tap 1 and tap 2.
- Desktop environments (Windows, macOS, KDE, GNOME, Wayland/Hyprland) enforce a strict double-click spatial deadzone (often 4–5 pixels). If the distance between the two clicks exceeds this threshold, the OS registers two distinct single clicks or accidentally initiates a drag-selection.
- **Wacom's Solution ("Tip Double Click Distance"):**
  Wacom drivers monitor rapid tip taps. When a second tap lands within a configurable temporal window and spatial radius of the first tap, the driver **snaps the coordinates of the second tap to the exact coordinates of the first tap**, ensuring the OS reliably registers the double click every time.

---

## 2. Algorithmic Specification

### 2.1 State Machine
The assist filter maintains a lightweight state machine across device reports:

```
               ┌─────────────┐
               │    Idle     │◄─────────────────────────────┐
               └──────┬──────┘                              │
                      │ Tip Down (Pressure > 0)             │
                      ▼                                     │
               ┌─────────────┐                              │
               │  FirstTap   │                              │
               └──────┬──────┘                              │
                      │ Tip Up (Pressure == 0)              │
                      │ & Tap Duration < MaxTapLength       │
                      ▼                                     │
          ┌───────────────────────┐                         │
          │  WaitingForSecondTap  │                         │
          └───────────┬───────────┘                         │
                      │                                     │
     ┌────────────────┴────────────────┐                    │
     │ Tip Down                        │ Timeout (> MaxTime)│
     │ & Distance <= DistanceThreshold │ OR OutOfRange      │
     ▼                                 ▼                    │
┌─────────────────────────┐      ┌─────────────┐            │
│    AssistingDoubleTap   │      │ Reset/Idle  ├────────────┘
│ (Snap Pos = FirstTapPos)│      └─────────────┘
└────────────┬────────────┘
             │
             ├─► Movement > UnlockDistance  ──► Break snap (track real Pos)
             └─► Tip Up (Pressure == 0)     ──► Return to Idle
```

### 2.2 Parameters & Thresholds
- **`Distance` ($D_{\text{threshold}}$):** Maximum distance (in screen pixels) between tap 1 and tap 2 to qualify for snapping. Default: `8.0 px` (range: `2.0` – `30.0 px`).
- **`MaxTimeMs` ($T_{\text{threshold}}$):** Maximum time window between the release of tap 1 and the contact of tap 2. Default: `450 ms` (range: `150` – `1000 ms`).
- **`MaxTapLengthMs`:** Maximum duration tap 1 may remain in contact with the tablet surface to be considered a "tap" rather than a drawing stroke or drag. Default: `300 ms`.
- **`DragUnlockDistance` ($D_{\text{unlock}}$):** If the user holds down the stylus on the second tap and moves further than this distance from the original anchor point, assist snapping automatically unlocks so drawing, painting, or dragging is never hindered. Default: `14.0 px`.

---

## 3. Pipeline Architecture & Placement

```
[Hardware HID Report]
         │
         ▼
[PreTransform Elements] (PressureRewriteFilter, etc.)
         │
         ▼
[OutputMode.Transform] (Converts tablet coordinates -> Virtual Screen Pixels)
         │
         ▼
[PostTransform Elements]
         │
         ├─► [DoubleClickAssistFilter]  <── (Operates in Screen Pixels)
         │        Snaps (report.Position = _firstTapPos) when assist engages
         │
         └─► [BindingHandler]
                  Tip Threshold Binding fires Mouse Left Click down/up
                  sees the exact snapped pixel coordinates
         │
         ▼
[OutputMode.OnOutput] -> Pointer.SetPosition(report.Position)
```

### 3.1 Pipeline Position: `PostTransform` (Pixels)
- Operating at `PipelinePosition.PostTransform` ensures the coordinates evaluated (`report.Position`) are already projected into **virtual screen pixels**.
- Spatial thresholds ($D_{\text{threshold}}$ and $D_{\text{unlock}}$) remain visually consistent regardless of whether the user is on a small or huge tablet, custom active area, or multi-monitor arrangement.

### 3.2 Interaction with `BindingHandler` and Pointer Output
- When `DoubleClickAssistFilter` snaps `report.Position` on tap 2:
  1. `BindingHandler` processes `Tip` (e.g. Left Click) while `report.Position` is anchored.
  2. `AbsoluteOutputMode.OnOutput` passes the anchored `report.Position` to `Pointer.SetPosition`.
  3. The operating system receives MouseDown at $(X_1, Y_1)$ matching tap 1 exactly.

---

## 4. Implementation Steps

### 4.1 Filter Class Implementation
- Create `OpenTabletDriver.Desktop/Output/Filters/DoubleClickAssistFilter.cs`:
  - Implement `IPositionedPipelineElement<IDeviceReport>`.
  - Decorated with `[PluginName("Double-Click Assist")]`.
  - Properties with OpenTabletDriver metadata:
    ```csharp
    [Property("Double-Click Distance (px)"), Range(2, 30)]
    public float Distance { get; set; } = 8.0f;

    [Property("Double-Click Time Window (ms)"), Range(150, 1000)]
    public float MaxTimeMs { get; set; } = 450.0f;

    [Property("Drag Unlock Distance (px)"), Range(5, 50)]
    public float DragUnlockDistance { get; set; } = 14.0f;

    [Property("Assist Eraser"), Description("Apply double-click assist to eraser tip as well")]
    public bool EnableEraser { get; set; } = false;
    ```
  - State tracking using high-resolution timestamp (`Stopwatch.GetTimestamp()`):
    - Track pressure state transitions (`wasTipDown`, `isTipDown`).
    - Detect `OutOfRangeReport` to reset state.

### 4.2 Unit Tests
- Create `OpenTabletDriver.Tests/Filters/DoubleClickAssistFilterTests.cs`:
  - Test 1: **Normal Double-Tap**: Verifies that two quick taps within $D_{\text{threshold}}$ and $T_{\text{threshold}}$ snap tap 2 coordinates to tap 1.
  - Test 2: **Distant Tap**: Two taps with distance $> D_{\text{threshold}}$ do not snap.
  - Test 3: **Slow Tap**: Two taps with $\Delta t > T_{\text{threshold}}$ do not snap.
  - Test 4: **Double-Tap and Drag**: Verifies snap locks initially on tap 2, but releases once movement exceeds $D_{\text{unlock}}$.
  - Test 5: **OutOfRange Reset**: Lifting the pen completely off the tablet resets any pending double-click window.

---

## 5. Verification & Testing Matrix

| Scenario | Input Action | Expected Behavior |
| :--- | :--- | :--- |
| **Rapid Double-Tap in File Manager** | Double-tap a folder with normal pen jitter ($\approx 3\text{--}6\text{ px}$). | Coordinates of second tap snap to first tap; folder opens reliably every time. |
| **Wide Second Tap** | Tap at $(100, 100)$, then tap at $(200, 200)$. | No snap; treated as two independent single taps. |
| **Double-Tap and Hold Drag** | Tap once, tap again and hold, then drag across the screen. | Starts snapped to tap 1, unlocks once movement $> 14\text{ px}$, allowing normal dragging. |
| **Long First Click (Drawing Stroke)** | Draw a line for 1 second, lift pen, then tap quickly. | Tap 1 was longer than $300\text{ ms}$; not treated as first tap of a double-click. |
| **Out-of-Range Re-entry** | Tap once, move pen far out of proximity, bring pen back down. | `OutOfRangeReport` resets the state; no accidental snap. |
