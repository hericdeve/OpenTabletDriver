# Implementation Plan: Floating HUD with Dual Form Factors (Radial Menu & Quick Bar)

## 1. Overview & Objectives

The goal is to implement a Wacom-inspired **Floating HUD (On-Screen Controls)** that appears seamlessly over any active application window (Krita, Blender, browser, IDE, fullscreen games) on Wayland compositors (Hyprland on CachyOS, wlroots/Sway, KDE Plasma 6, and GNOME with layer-shell).

### Key Features:
1. **Dual Form Factors:**
   - **Radial / Pie Menu:** Fast, gesture-driven circular overlay centered at the pen tip with 4, 6, or 8 wedges. Optimized for muscle memory and Fitts's law (direction-based selection).
   - **Floating Quick Bar / Dock:** Draggable, pinnable horizontal or vertical toolbar that can float near the pen tip or dock to screen edges.
2. **Dual Interaction Modes:**
   - **Hold & Flick:** Hold the barrel button, flick in the direction of the desired wedge, and release to trigger and auto-dismiss within milliseconds.
   - **Tap & Browse:** Quick tap on the barrel button keeps the HUD open; the artist taps an option with the stylus tip.
3. **Application Awareness:**
   - HUD configurations (actions, labels, icons) automatically adapt to the currently focused application via OpenTabletDriver's `AppProfileMonitor`.
4. **Wayland First-Class Integration:**
   - Uses `libgtk-layer-shell` + GTK3 + Cairo to achieve pixel-perfect placement on the active monitor in the `Overlay` layer without stealing application focus.

---

## 2. Technology Stack & Wayland Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             WAYLAND COMPOSITOR                              │
│                      (Hyprland / Plasma 6 / Sway / GNOME)                   │
├─────────────────────────────────────────────────────────────────────────────┤
│  OVERLAY LAYER (wlr-layer-shell)                                            │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                     OpenTabletDriver Floating HUD                     │  │
│  │              (GTK3 Window + Cairo Drawing Context)                    │  │
│  │  - Centered at Pen Cursor (margin_left = X - R, margin_top = Y - R)   │  │
│  │  - Transparent background, anti-aliased slices, glow hover highlights │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  TOP / NORMAL LAYER                                                         │
│  ┌───────────────────────────────┐       ┌───────────────────────────────┐  │
│  │   Active Application Window   │       │   Another Application         │  │
│  │      (Krita / Blender / etc.) │       │      (Browser / IDE)          │  │
│  └───────────────────────────────┘       └───────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                               ▲
                               │ StreamJsonRpc (TriggerHud / Update / Dismiss)
                               │
┌──────────────────────────────┴──────────────────────────────────────────────┐
│                    OpenTabletDriver Daemon (Background)                     │
│  - Stylus Input Pipeline (AbsoluteMode / RelativeMode / ArtistMode)         │
│  - FloatingHudBinding (Captures barrel press, tracks position & angle)       │
│  - AppProfileMonitor (Feeds active app profile & preset to HUD)             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Why GTK3 + `libgtk-layer-shell` + Cairo?
- `OpenTabletDriver.UX.Gtk` already loads `GtkSharp`, `CairoSharp`, and `GdkSharp` on Linux.
- `libgtk-layer-shell.so.0` is already installed on CachyOS (`/usr/lib/libgtk-layer-shell.so.0`).
- GTK transparent windows with Cairo contexts provide zero-latency hardware-accelerated 2D rendering of arcs, vectors, text, and SVG icons.
- Avoids heavy dependencies (no WebKit, no Qt runtime, no Electron).

---

## 3. Form Factor Specifications

### Form Factor A: Radial / Pie Menu

```
                    [ 0: Undo ]
                       ▲
        [ 7: Zoom - ]  │  [ 1: Redo ]
              ↖        │        ↗
                \      │      /
    [ 6: Brush ] ─── ( + ) ─── [ 2: Zoom + ]
                /      │      \
              ↙        │        ↘
       [ 5: Eraser ]   │   [ 3: Precision ]
                       ▼
                 [ 4: Disp Toggle ]
```

1. **Geometry:**
   - Outer radius: $R_{\text{outer}} = 130\text{ px}$.
   - Inner deadzone radius: $R_{\text{inner}} = 35\text{ px}$. Releasing the pen within the center circle cancels the menu without triggering any action.
   - Slices: Configurable 4, 6, or 8 wedges.
2. **Selection Angle Math:**
   Given the origin $(X_0, Y_0)$ and current pen position $(X, Y)$:
   $$\Delta X = X - X_0, \quad \Delta Y = Y - Y_0$$
   $$\text{Distance} = \sqrt{\Delta X^2 + \Delta Y^2}$$
   $$\theta = \left(\operatorname{atan2}(\Delta Y, \Delta X) \times \frac{180}{\pi} + 90^\circ + 360^\circ\right) \pmod{360^\circ}$$
   $$\text{Active Slice} = \begin{cases} -1 \text{ (None/Deadzone)}, & \text{if } \text{Distance} < R_{\text{inner}} \\ \left\lfloor \frac{\theta + \frac{\text{SliceAngle}}{2}}{\text{SliceAngle}} \right\rfloor \pmod N, & \text{otherwise} \end{cases}$$
3. **Screen Edge Clamping:**
   If $(X_0 - R_{\text{outer}} < \text{Monitor.X})$, clamp $X_{\text{origin}} = \text{Monitor.X} + R_{\text{outer}}$.
   Identical clamping for right, top, and bottom edges ensures the menu is never clipped off-screen.

---

### Form Factor B: Floating Quick Bar / Dock

```
┌─────────────────────────────────────────────────────────────┐
│ [::] OpenTabletDriver Quick Bar                   [-] [x]   │
├─────────────────────────────────────────────────────────────┤
│ [ Undo ] [ Redo ] [ Brush ] [ Eraser ] [ Disp ] [ Precision]│
└─────────────────────────────────────────────────────────────┘
```

1. **Geometry & Layout:**
   - Rounded border box with translucent dark backdrop (`rgba(25, 25, 30, 0.88)`), blur, and subtle border glow.
   - Orientation: Horizontal or Vertical.
   - Drag Handle: `[::]` grip on the leading edge allows dragging anywhere across monitors.
2. **Docking & Pinning:**
   - **Pin Button `[-]` / `[x]`:** When pinned, stays visible on top across all apps until explicitly closed.
   - **Hand-Aware Spawn Offset:**
     - Right-handed mode: spawns offset slightly to the top-left of the pen tip $(X - 180, Y - 60)$.
     - Left-handed mode: spawns offset slightly to the top-right of the pen tip $(X + 40, Y - 60)$.
     - Prevents the user's hand/stylus from obscuring buttons.

---

## 4. Supported Action Types

Each slot (Radial wedge or Quick Bar button) can be bound to:
1. **Keystroke / Key Combination:** `Ctrl+Z`, `Ctrl+Shift+S`, `Space`, `B`, `E`, etc. Dispatched via OpenTabletDriver's `IVirtualKeyboard`.
2. **Driver Commands:**
   - **Universal Display Toggle** (cycle monitors / desktop).
   - **Precision Mode Toggle** (toggle high-precision scaled area).
   - **Pan / Scroll Mode** (activate continuous pan scrolling).
   - **Switch Preset / Profile** (switch directly to Preset "Inking" or "Gaming").
3. **Mouse Clicks:** Middle Click, Right Click, Double Click.
4. **Shell Execution:** Custom bash script or application launch.
5. **Sub-Menu (Radial only):** Expands into a secondary ring of choices (e.g. "Brushes $\to$ Pencil, Airbrush, Ink, Watercolor").

---

## 5. Architecture & Implementation Phases

### Phase 1: Data Contracts & Daemon RPC Extensions
1. **`HudConfiguration.cs` & `HudItem.cs` (`OpenTabletDriver.Desktop/Hud/`)**:
   - Data models for HUD profiles, menu items, icons, actions, form factor (`Radial` vs `QuickBar`), radius, colors, and pin state.
2. **`IDriverDaemon` RPC Interface Update**:
   - Add RPC events / methods:
     ```csharp
     event EventHandler<HudShowRequest>? ShowHudRequested;
     event EventHandler<HudUpdateRequest>? UpdateHudRequested;
     event EventHandler? DismissHudRequested;
     Task ExecuteHudAction(HudAction action);
     ```
3. **`FloatingHudBinding.cs` (`OpenTabletDriver.Desktop/Binding/`)**:
   - Implements `IContinuousBinding` and `IPointerSuppressor`.
   - On `Press`: Captures anchor coordinates $(X_0, Y_0)$ and fires `ShowHudRequested`.
   - On `Update`: Fires `UpdateHudRequested` with $(X, Y)$ and suppresses pointer motion during hold gestures.
   - On `Release`: If hold duration $> 150\text{ ms}$, executes active slice and fires `DismissHudRequested`. If $< 150\text{ ms}$, leaves HUD open in tap mode.

### Phase 2: Wayland Layer-Shell C# Interop (`OpenTabletDriver.Desktop/Interop/`)
1. **`GtkLayerShellInterop.cs`**:
   - P/Invoke bindings to `libgtk-layer-shell.so.0`:
     - `gtk_layer_init_for_window(IntPtr window)`
     - `gtk_layer_set_layer(IntPtr window, LayerShellLayer.Overlay)`
     - `gtk_layer_set_anchor(IntPtr window, Edge edge, bool anchor)`
     - `gtk_layer_set_margin(IntPtr window, Edge edge, int margin)`
     - `gtk_layer_set_keyboard_interactivity(IntPtr window, bool interactivity)`
     - `gtk_layer_set_monitor(IntPtr window, IntPtr gdkMonitor)`
     - `gtk_layer_is_supported()` for dynamic Wayland compositor capability detection.

### Phase 3: Visual Renderers (`OpenTabletDriver.UX.Gtk/Hud/`)
1. **`RadialMenuOverlay.cs`**:
   - GTK3 window utilizing Cairo vector graphics.
   - Transparent visual surface (`gdk_screen_get_rgba_visual`).
   - Draw routines for anti-aliased wedges, inner deadzone, icons, text labels, and active slice accent glow.
   - Edge-clamping logic relative to active monitor boundaries.
2. **`QuickBarOverlay.cs`**:
   - GTK3 floating dock with draggable header, action buttons, orientation switch, and pin toggle.
3. **`HudManager.cs`**:
   - Subscribes to `App.Driver.Instance.ShowHudRequested`.
   - Marshals events to the GTK main loop (`GLib.Idle.Add` / `Application.Invoke`).
   - Displays and positions the chosen form factor instantaneously.

### Phase 4: UX Configuration Panel (`OpenTabletDriver.UX/Windows/Hud/`)
1. **HUD Settings Tab in `MainForm.cs`**:
   - Form factor selector: Radial Menu vs Quick Bar.
   - Interactive visual wedge editor: Click a slice to assign hotkey, icon, color, or driver action.
   - Sliders for Size/Radius, Deadzone, Opacity, and Handedness.
   - Preset linking: allows per-application custom HUD layouts.

---

## 6. Verification & Test Plan

| Test Case | Scenario | Expected Outcome |
| :--- | :--- | :--- |
| **Wayland Overlay Layer** | Summon HUD over fullscreen Krita or mpv. | Appears strictly above the application in the Wayland `Overlay` layer. |
| **Hold & Flick** | Hold pen button, flick diagonally toward "Undo", release. | "Ctrl+Z" dispatches immediately; HUD closes; takes $< 200\text{ ms}$. |
| **Tap & Browse** | Click pen button once and hover over slices. | HUD remains visible; hovering highlights slices; tapping a slice executes action and closes. |
| **Deadzone Cancel** | Open radial menu, move pen slightly within center ring, release. | Menu dismisses without firing any command. |
| **Screen Edge Clamping** | Press barrel button when pen is 15 px from the top-left monitor corner. | Menu shifts outward into visible screen space; no wedges cut off. |
| **Multi-Monitor Positioning** | Summon HUD on secondary monitor. | Spawns on the correct monitor directly at pen cursor. |
| **Quick Bar Drag & Pin** | Drag quick bar to corner of canvas and click Pin. | Stays docked across window switches; buttons remain clickable. |
| **AppProfile Switch** | Switch from Krita to Blender. | HUD automatically loads Blender shortcuts (e.g. G, R, S, Extrude). |
