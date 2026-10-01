# Implementation Plan: Fix Radial HUD Hover Flickering and Selection Alignment

## Problem Analysis

The user reported three distinct symptoms when using the Radial HUD:
1. **Boundary Flickering & Jitter**: Flickering when hovering around a section of the circle.
2. **Split-Second Cross-Section Selection**: For a split second, the adjacent wedge is selected while hovering inside a section.
3. **Selection Highlight vs Action Mismatch**: The area that triggers the blue selection background on hover is noticeably larger than (and misaligned with) the area that actually triggers the action upon releasing/lifting the pen.

### Root Causes

1. **Dual Competing Hover Calculators (Tug-of-War)**:
   - In `RadialMenuOverlay.cs`, `OnMotionNotify` listens to GTK `MotionNotifyEvent` and calculates `_hoveredSlice` based on GTK window coordinates `(args.Event.X, args.Event.Y)`.
   - In `DriverDaemon.cs`, `TriggerHudUpdate` independently calculates `CurrentHoveredSlice` based on digitizer coordinates `(request.CursorPosition - HudAnchorPosition)`.
   - `UpdateHudRequested` RPC from `DriverDaemon` pushes its slice into `RadialMenuOverlay.UpdatePosition()`, overwriting `_hoveredSlice`.
   - On the next mouse move, GTK's `OnMotionNotify` overwrites `_hoveredSlice` back.
   - When GTK and DriverDaemon disagree on which wedge is active, they alternate every few milliseconds, causing rapid **visual flickering** and **split-second jumping**.
   - Crucially, `ConfirmHudSelection()` executes `DriverDaemon.CurrentHoveredSlice`, **NOT** GTK's `_hoveredSlice`. If GTK draws a blue highlight on Slice A because of coordinate offsets, but DriverDaemon thinks the pen is in Slice B or in the deadzone, lifting the pen executes Slice B or nothing.

2. **Window Origin & Geometry Offsets (Coordinate Discrepancy)**:
   - `RadialMenuOverlay` is a floating `300x300` window positioned using `GtkLayerShell.SetMargin(Left, x)` and `SetMargin(Top, y)`.
   - On Wayland with compositors like Hyprland, `SetMargin(Top, y)` anchors against usable work areas (below panels like Waybar, which has an exclusive zone of 40px in the user's setup).
   - Furthermore, `x` and `y` are truncated to integers and clamped with `Math.Max(0, x)` when near the screen edges.
   - As a result, the center of the GTK window `(centerX, centerY)` is vertically and horizontally shifted from the true digitizer anchor position `HudAnchorPosition`. A 40px vertical shift drastically changes the angle $\text{atan2}(dy, dx)$, creating a massive mismatch between GTK's angle and DriverDaemon's angle.

3. **Double `TriggerHudUpdate` Dispatch with Suppressed Coordinates**:
   - In `BindingHandler.cs`, `bindingHandler.OnPositionChanged = OnPositionReport` was subscribed in `DriverDaemon.cs`.
   - `BindingHandler.Consume` calls `HandleBinding(report)` (which fires `FloatingHudBinding.Update` with live coordinates), then runs `ApplyPointerSuppression(report)` (which clamps coordinates to `_anchorPosition` if anchored), and then invokes `OnPositionChanged?.Invoke(absReport.Position)`.
   - This caused `DriverDaemon.TriggerHudUpdate` to be called twice per report—once with live pen coordinates and once with anchor coordinates `(delta = 0)`—causing state jitter.

4. **Missing Final Coordinate Update on Release**:
   - In `FloatingHudBinding.Release`, `ConfirmHudSelection()` is invoked immediately without updating `DriverDaemon` with the final position report of the release gesture. Fast flick gestures could confirm a stale position from a previous frame.

5. **Lack of Angular Hysteresis**:
   - At boundary lines (e.g. 22.5°), microscopic digitizer noise or hand tremor alternates between adjacent wedges without any boundary debounce/hysteresis margin.

---

## Proposed Changes

### Phase 1: Establish Single Source of Truth for Hover State
- **Remove GTK `OnMotionNotify` calculation**:
  - Remove the competing hover calculation from `RadialMenuOverlay.OnMotionNotify`.
  - `RadialMenuOverlay` will purely be a view: its `_hoveredSlice` will be driven strictly by authoritative updates from `DriverDaemon` via `UpdatePosition()`.
  - When the pen is lifted or released, `DriverDaemon.ConfirmHudSelection()` executes the exact slice that was visually highlighted in blue—eliminating 100% of the discrepancy between the visual highlight and the confirmed action.

### Phase 2: Eliminate Double Dispatch in Daemon & Binding Handler
- In `DriverDaemon.cs`:
  - Remove `bindingHandler.OnPositionChanged = OnPositionReport;`.
  - `FloatingHudBinding.Update` is an `IContinuousBinding` that receives authoritative, unsuppressed live device reports directly from the pipeline. It is the sole component responsible for streaming `TriggerHudUpdate`.
- In `FloatingHudBinding.Release`:
  - Pass the final report's position to `ConfirmHudSelection(Vector2? finalPosition = null)` so the daemon calculates the confirmed wedge using the exact release point before dismissing the HUD.

### Phase 3: Full-Monitor Layer Surface to Eliminate Margin Offsets
- In `RadialMenuOverlay.cs`:
  - Configure `GtkLayerShell` to anchor to all four screen edges (`Left`, `Right`, `Top`, `Bottom`) with `SetExclusiveZone(-1)`.
  - Make the overlay surface span the full monitor with transparent Cairo background.
  - Slices are drawn centered at `_anchorPos.X, _anchorPos.Y` directly in screen coordinates.
  - This eliminates:
    - Waybar / panel exclusive zone offsets (no vertical displacement).
    - Screen-edge clamping (`Math.Max(0, x)` edge squishing).
    - Window margin rounding errors.
  - The digitizer screen pixels and GTK screen pixels become 1:1 identical.

### Phase 4: Angular Hysteresis for Rock-Solid Section Hover
- In `DriverDaemon.cs` (and HUD wedge angle calculation):
  - Add an angular hysteresis buffer (e.g. ±2.5°):
    - When no slice is active (`hovered == -1`), use the standard 45° boundaries.
    - When a slice $i$ is currently active, require the cursor to cross beyond $i$'s boundary by $+2.5^\circ$ before transitioning to the next slice $i+1$, or $-2.5^\circ$ before transitioning to $i-1$.
  - This prevents boundary flickering and eliminates split-second flickering to the next section when hovering near the edges of a section.

---

## Verification Plan

1. **Build Verification**:
   - Execute `./build.sh linux` to confirm 0 compilation errors across all projects.
2. **Unit Test Verification**:
   - Run `dotnet test OpenTabletDriver.Tests` to verify no regressions in the test suite.
3. **Runtime Testing on Wayland/Hyprland**:
   - Launch `bin/OpenTabletDriver.Daemon` and `bin/OpenTabletDriver.UX.Gtk`.
   - Trigger the Radial HUD by holding the pen button.
   - Test hover across all 8 slices: verify smooth, immediate blue highlighting with no flickering.
   - Hover near the boundaries between sections: verify the hysteresis keeps the active wedge steady without jumping back and forth.
   - Hover near the outer edges and center deadzone: verify that the blue highlighted wedge corresponds 100% to the action executed when the button is released or pen is pulled away.
