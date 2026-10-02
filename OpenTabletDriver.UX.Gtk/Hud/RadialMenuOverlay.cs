using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Cairo;
using Gdk;
using Gtk;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.UX.Gtk.Interop;
using GtkWindow = Gtk.Window;

namespace OpenTabletDriver.UX.Gtk.Hud
{
    public class RadialMenuOverlay : GtkWindow
    {
        private HudConfiguration _config;
        private HudConfiguration? _rootConfig;
        public bool IsInWorkspaceSubMenu => _rootConfig != null;
        private Vector2 _anchorPos;
        private Vector2 _currentPos;
        private Vector2 _monitorOrigin = Vector2.Zero;
        private int _hoveredSlice = -1;
        private bool _isLayerShellActive;

        // High refresh rate (165Hz/180Hz) physics & animation tracking
        private long _lastTickTimestamp;
        private float[] _hoverBlooms = Array.Empty<float>();
        private float _layerMorphProgress = 1.0f;
        private float _deadzoneProgress = 0.0f;

        public event Action<HudItem>? ItemActivated;

        public RadialMenuOverlay() : base(global::Gtk.WindowType.Toplevel)
        {
            _config = HudConfiguration.GetDefaults();

            AppPaintable = true;
            Decorated = false;
            SkipTaskbarHint = true;
            SkipPagerHint = true;
            KeepAbove = true;

            // Enable RGBA transparency
            var screen = Screen;
            var visual = screen?.RgbaVisual;
            if (visual != null)
                Visual = visual;

            Drawn += OnDrawn;
            ButtonPressEvent += OnButtonPress;

            Events |= EventMask.ButtonPressMask;

            // VSync-synchronized tick callback for 165Hz/180Hz displays
            AddTickCallback((widget, frameClock) => OnTick(frameClock));

            if (GtkLayerShell.IsSupported)
            {
                GtkLayerShell.InitForWindow(Handle);
                GtkLayerShell.SetLayer(Handle, GtkLayerShell.Layer.Overlay);
                GtkLayerShell.SetKeyboardMode(Handle, GtkLayerShell.KeyboardMode.None);
                GtkLayerShell.SetNamespace(Handle, "otd-radial-hud");
                _isLayerShellActive = true;
            }
        }

        public void ShowAt(Vector2 position, HudConfiguration config)
        {
            _config = config;
            if (_config.Items == null || _config.Items.Count == 0)
                _config = HudConfiguration.GetDefaults();

            // Track root configuration if entering a sub-menu
            if (_config.IsSubMenu)
            {
                _rootConfig ??= _config;
            }
            else
            {
                _rootConfig = null;
            }

            // Deduplicate items if corrupted settings file had repeated items
            var seen = new System.Collections.Generic.HashSet<string>();
            var unique = new System.Collections.Generic.List<HudItem>();
            foreach (var item in _config.Items)
            {
                if (seen.Add(item.Label))
                    unique.Add(item);
            }
            _config.Items = unique;

            if (_hoverBlooms.Length != _config.Items.Count)
                _hoverBlooms = new float[_config.Items.Count];
            else
                Array.Clear(_hoverBlooms, 0, _hoverBlooms.Length);

            _lastTickTimestamp = Stopwatch.GetTimestamp();
            _layerMorphProgress = 0.0f; // start smooth morph
            _deadzoneProgress = 0.0f;

            if (_isLayerShellActive)
            {
                var gdkDisplay = Display ?? Gdk.Display.Default;
                var monitor = gdkDisplay?.GetMonitorAtPoint((int)position.X, (int)position.Y);
                if (monitor != null)
                {
                    GtkLayerShell.SetMonitor(Handle, monitor.Handle);
                    var geom = monitor.Geometry;
                    _monitorOrigin = new Vector2(geom.X, geom.Y);
                }
                else
                {
                    _monitorOrigin = Vector2.Zero;
                }

                _anchorPos = position - _monitorOrigin;
                _currentPos = _anchorPos;

                // Anchor to all 4 edges to span the full screen, with exclusive zone -1 to overlay panels/bars without displacement
                GtkLayerShell.SetAnchor(Handle, GtkLayerShell.Edge.Left, true);
                GtkLayerShell.SetAnchor(Handle, GtkLayerShell.Edge.Right, true);
                GtkLayerShell.SetAnchor(Handle, GtkLayerShell.Edge.Top, true);
                GtkLayerShell.SetAnchor(Handle, GtkLayerShell.Edge.Bottom, true);
                GtkLayerShell.SetMargin(Handle, GtkLayerShell.Edge.Left, 0);
                GtkLayerShell.SetMargin(Handle, GtkLayerShell.Edge.Right, 0);
                GtkLayerShell.SetMargin(Handle, GtkLayerShell.Edge.Top, 0);
                GtkLayerShell.SetMargin(Handle, GtkLayerShell.Edge.Bottom, 0);
                GtkLayerShell.SetExclusiveZone(Handle, -1);
            }
            else
            {
                _monitorOrigin = Vector2.Zero;
                _anchorPos = position;
                _currentPos = position;

                int diameter = (int)(_config.Radius * 2 + 120);
                SetDefaultSize(diameter, diameter);
                Resize(diameter, diameter);
                int x = (int)(position.X - diameter / 2f);
                int y = (int)(position.Y - diameter / 2f);
                Move(x, y);
            }

            _hoveredSlice = -1;

            Log.Write("HUD_GTK", $"Showing RadialMenu at {position.X},{position.Y} (Style: {_config.ThemeStyle}, Font: {_config.FontFamily})");
            ShowAll();
            QueueDraw();
        }

        public void UpdatePosition(Vector2 position, int hoveredSlice = -1)
        {
            _currentPos = position - _monitorOrigin;
            _hoveredSlice = hoveredSlice;
        }

        public void SwitchToWorkspaceLayer(bool isMoveWindow = false)
        {
            _ = App.Driver.Instance?.SwitchToWorkspaceSubLayer(isMoveWindow);
        }

        public void RestoreRootMenu()
        {
            _ = App.Driver.Instance?.RestoreRootHudLayer();
        }

        public void Dismiss()
        {
            _rootConfig = null;
            _deadzoneProgress = 0.0f;
            _hoveredSlice = -1;
            Hide();
        }

        private bool OnTick(FrameClock frameClock)
        {
            if (!Visible)
                return true;

            long currentTimestamp = Stopwatch.GetTimestamp();
            double dt = _lastTickTimestamp > 0
                ? (currentTimestamp - _lastTickTimestamp) / (double)Stopwatch.Frequency
                : 0.006;
            _lastTickTimestamp = currentTimestamp;

            // Clamp dt to prevent jumping after a hitch or window spawn
            if (dt > 0.05) dt = 0.05;

            bool needsRedraw = false;
            int count = _config.Items.Count;
            if (_hoverBlooms.Length != count)
                _hoverBlooms = new float[count];

            // 1. Frame-rate independent hover bloom smoothing (165Hz/180Hz)
            for (int i = 0; i < count; i++)
            {
                float target = (i == _hoveredSlice) ? 1.0f : 0.0f;
                float diff = target - _hoverBlooms[i];
                if (Math.Abs(diff) > 0.002f)
                {
                    _hoverBlooms[i] = target - diff * (float)Math.Exp(-28.0 * dt);
                    needsRedraw = true;
                }
                else if (_hoverBlooms[i] != target)
                {
                    _hoverBlooms[i] = target;
                    needsRedraw = true;
                }
            }

            // 2. Sub-layer morph animation (120ms ease-in)
            if (_layerMorphProgress < 1.0f)
            {
                _layerMorphProgress += (float)(dt / 0.120);
                if (_layerMorphProgress >= 1.0f)
                    _layerMorphProgress = 1.0f;
                needsRedraw = true;
            }

            // 3. Center deadzone return progress tracking
            bool inDeadzone = false;
            if (_isLayerShellActive)
            {
                double dx = _currentPos.X - _anchorPos.X;
                double dy = _currentPos.Y - _anchorPos.Y;
                inDeadzone = Math.Sqrt(dx * dx + dy * dy) <= _config.DeadzoneRadius;
            }
            else
            {
                double dx = _currentPos.X - AllocatedWidth / 2.0;
                double dy = _currentPos.Y - AllocatedHeight / 2.0;
                inDeadzone = Math.Sqrt(dx * dx + dy * dy) <= _config.DeadzoneRadius;
            }

            if (IsInWorkspaceSubMenu && inDeadzone && _hoveredSlice == -1)
            {
                _deadzoneProgress += (float)(dt / 0.300);
                if (_deadzoneProgress >= 1.0f)
                {
                    _deadzoneProgress = 0.0f;
                    RestoreRootMenu();
                }
                needsRedraw = true;
            }
            else if (_deadzoneProgress > 0.0f)
            {
                _deadzoneProgress -= (float)(dt / 0.150);
                if (_deadzoneProgress < 0.0f)
                    _deadzoneProgress = 0.0f;
                needsRedraw = true;
            }

            if (needsRedraw)
                QueueDraw();

            return true;
        }

        private static double EaseOutCubic(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            double inv = 1.0 - t;
            return 1.0 - inv * inv * inv;
        }

        private static void DrawRoundedRectangle(Context cr, double x, double y, double w, double h, double r)
        {
            r = Math.Min(r, Math.Min(w / 2.0, h / 2.0));
            cr.NewSubPath();
            cr.Arc(x + r, y + r, r, Math.PI, 1.5 * Math.PI);
            cr.Arc(x + w - r, y + r, r, 1.5 * Math.PI, 2 * Math.PI);
            cr.Arc(x + w - r, y + h - r, r, 0, 0.5 * Math.PI);
            cr.Arc(x + r, y + h - r, r, 0.5 * Math.PI, Math.PI);
            cr.ClosePath();
        }

        private static void DrawWorkspaceAppIcons(Context cr, System.Collections.Generic.List<string> apps, double centerX, double centerY, double midRad, double radiusInner, double radiusOuter, double currentAlpha)
        {
            if (apps == null || apps.Count == 0)
                return;

            int totalApps = apps.Count;
            int maxDisplay = Math.Min(totalApps, 7);

            // Determine concentric rows based on app count
            // Outer rows have more arc space, inner rows have less
            // 1 app:  [1]
            // 2 apps: [1 inner, 1 outer]
            // 3 apps: [1 inner, 2 outer]
            // 4 apps: [1 inner, 1 mid, 2 outer]
            // 5 apps: [1 inner, 2 mid, 2 outer]
            // 6 apps: [1 inner, 2 mid, 3 outer]
            // 7 apps: [1 inner, 3 mid, 3 outer]
            int[] rowCounts = maxDisplay switch
            {
                1 => new[] { 1 },
                2 => new[] { 1, 1 },
                3 => new[] { 1, 2 },
                4 => new[] { 1, 1, 2 },
                5 => new[] { 1, 2, 2 },
                6 => new[] { 1, 2, 3 },
                _ => new[] { 1, 3, 3 }
            };

            int numRows = rowCounts.Length;
            double radialSpan = radiusOuter - radiusInner;
            double midRadius = (radiusInner + radiusOuter) / 2.0;

            // Icon size dynamically scales if multiple rows
            int iconSize = numRows switch
            {
                1 => 26,
                2 => 22,
                _ => 18
            };

            // Dynamic radial spacing centered around midRadius
            double rowSpacing = numRows switch
            {
                1 => 0.0,
                2 => Math.Min(26.0, radialSpan * 0.35),
                _ => Math.Min(22.0, radialSpan * 0.28)
            };

            double firstRowRadius = midRadius - ((numRows - 1) * rowSpacing / 2.0);

            int appIdx = 0;
            for (int r = 0; r < numRows; r++)
            {
                double rowRadius = firstRowRadius + (r * rowSpacing);
                int countInRow = rowCounts[r];

                // Angular step along the arc at rowRadius:
                // Arc length S = r * dTheta => dTheta = (iconSize + spacing) / r
                double itemArcWidth = iconSize + 6.0;
                double angularStep = itemArcWidth / rowRadius;

                // Center the row's icons symmetrically around midRad
                double startAngle = midRad - ((countInRow - 1) * angularStep / 2.0);

                for (int c = 0; c < countInRow && appIdx < maxDisplay; c++)
                {
                    double iconAngle = startAngle + (c * angularStep);
                    double iconCenterX = centerX + Math.Cos(iconAngle) * rowRadius;
                    double iconCenterY = centerY + Math.Sin(iconAngle) * rowRadius;

                    var appClass = apps[appIdx++];
                    var pixbuf = AppIconCache.GetIcon(appClass, iconSize);

                    if (pixbuf != null)
                    {
                        cr.Save();
                        // Position upright without rotating context
                        global::Gdk.CairoHelper.SetSourcePixbuf(cr, pixbuf, iconCenterX - (pixbuf.Width / 2.0), iconCenterY - (pixbuf.Height / 2.0));
                        cr.PaintWithAlpha(currentAlpha);
                        cr.Restore();
                    }
                    else
                    {
                        // Sleek rounded letter badge fallback
                        double badgeR = iconSize / 2.0;
                        cr.Arc(iconCenterX, iconCenterY, badgeR, 0, 2 * Math.PI);
                        cr.SetSourceRGBA(0.25, 0.28, 0.35, 0.90 * currentAlpha);
                        cr.FillPreserve();
                        cr.SetSourceRGBA(1.0, 1.0, 1.0, 0.30 * currentAlpha);
                        cr.LineWidth = 1.0;
                        cr.Stroke();

                        string letter = !string.IsNullOrWhiteSpace(appClass) ? appClass.Substring(0, 1).ToUpperInvariant() : "?";
                        cr.SetFontSize(iconSize * 0.55);
                        var ext = cr.TextExtents(letter);
                        cr.MoveTo(iconCenterX - (ext.Width / 2.0) - ext.XBearing, iconCenterY - (ext.Height / 2.0) - ext.YBearing);
                        cr.SetSourceRGBA(1.0, 1.0, 1.0, 0.95 * currentAlpha);
                        cr.ShowText(letter);
                    }
                }
            }
        }

        private void OnDrawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            int width = AllocatedWidth;
            int height = AllocatedHeight;
            double centerX = _isLayerShellActive ? _anchorPos.X : (width / 2.0);
            double centerY = _isLayerShellActive ? _anchorPos.Y : (height / 2.0);

            // Clear background with complete transparency
            cr.Save();
            cr.Operator = Operator.Clear;
            cr.Paint();
            cr.Restore();

            int count = _config.Items.Count;
            if (count == 0) return;

            float sliceAngle = 360f / count;
            double morphEased = EaseOutCubic(_layerMorphProgress);
            double currentScale = 0.94 + 0.06 * morphEased;
            double currentAlpha = morphEased;

            double radiusOuter = _config.Radius * currentScale;
            double radiusInner = _config.DeadzoneRadius * currentScale;

            string fontFamily = string.IsNullOrWhiteSpace(_config.FontFamily) ? "Sans" : _config.FontFamily;
            bool isSolid = _config.ThemeStyle == HudThemeStyle.Solid;

            // Render slices
            for (int i = 0; i < count; i++)
            {
                var item = _config.Items[i];
                bool isSubLayer = item.IsSubLayer;

                double startDeg = (i * sliceAngle) - (sliceAngle / 2.0) - 90.0;
                double endDeg = startDeg + sliceAngle;

                double startRad = startDeg * Math.PI / 180.0;
                double endRad = endDeg * Math.PI / 180.0;

                float bloom = (i < _hoverBlooms.Length) ? _hoverBlooms[i] : 0.0f;
                double effectiveOuterRadius = radiusOuter + 6.0 * bloom;

                cr.NewPath();
                cr.Arc(centerX, centerY, effectiveOuterRadius, startRad, endRad);
                cr.ArcNegative(centerX, centerY, radiusInner, endRad, startRad);
                cr.ClosePath();

                if (isSolid)
                {
                    // SOLID THEME: Crisp high-contrast matte dark finish
                    if (bloom > 0.01f)
                    {
                        // Lerp between matte charcoal and Apple Electric Blue
                        double r = 0.09 + (0.04 - 0.09) * bloom;
                        double g = 0.09 + (0.52 - 0.09) * bloom;
                        double b = 0.11 + (1.00 - 0.11) * bloom;
                        cr.SetSourceRGBA(r, g, b, _config.Opacity * currentAlpha);
                    }
                    else
                    {
                        cr.SetSourceRGBA(0.09, 0.09, 0.11, _config.Opacity * currentAlpha);
                    }
                    cr.FillPreserve();

                    // Solid border stroke
                    double sr = 0.22 + (0.40 - 0.22) * bloom;
                    double sg = 0.22 + (0.75 - 0.22) * bloom;
                    double sb = 0.27 + (1.00 - 0.27) * bloom;
                    cr.SetSourceRGBA(sr, sg, sb, 0.95 * currentAlpha);
                    cr.LineWidth = 1.4 + 1.2 * bloom;
                    cr.Stroke();
                }
                else
                {
                    // TRANSLUCENT THEME: Apple optical glass with specular reflection
                    if (bloom > 0.01f)
                    {
                        using (var grad = new RadialGradient(centerX, centerY, radiusInner, centerX, centerY, effectiveOuterRadius))
                        {
                            grad.AddColorStop(0.0, new Cairo.Color(0.05, 0.50, 1.00, (0.85 + 0.10 * bloom) * currentAlpha));
                            grad.AddColorStop(1.0, new Cairo.Color(0.00, 0.38, 0.85, (0.90 + 0.08 * bloom) * currentAlpha));
                            cr.SetSource(grad);
                            cr.FillPreserve();
                        }

                        // Top gloss highlight wash
                        cr.SetSourceRGBA(1.0, 1.0, 1.0, 0.25 * bloom * currentAlpha);
                        cr.LineWidth = 1.8;
                        cr.StrokePreserve();

                        cr.SetSourceRGBA(0.45, 0.80, 1.0, (0.70 + 0.25 * bloom) * currentAlpha);
                        cr.LineWidth = 1.2 + 1.2 * bloom;
                        cr.Stroke();
                    }
                    else
                    {
                        using (var grad = new RadialGradient(centerX, centerY, radiusInner, centerX, centerY, radiusOuter))
                        {
                            grad.AddColorStop(0.0, new Cairo.Color(0.12, 0.12, 0.16, 0.78 * _config.Opacity * currentAlpha));
                            grad.AddColorStop(1.0, new Cairo.Color(0.07, 0.07, 0.10, 0.86 * _config.Opacity * currentAlpha));
                            cr.SetSource(grad);
                            cr.FillPreserve();
                        }

                        // Refined subtle glass border
                        cr.SetSourceRGBA(1.0, 1.0, 1.0, 0.12 * currentAlpha);
                        cr.LineWidth = 1.0;
                        cr.Stroke();
                    }
                }

                // Subtle radial edge for HUD sub-layer slots (single accented outer rim)
                if (isSubLayer)
                {
                    cr.NewPath();
                    cr.Arc(centerX, centerY, effectiveOuterRadius, startRad, endRad);
                    if (isSolid)
                    {
                        cr.SetSourceRGBA(0.35, 0.65, 0.95, (0.75 + 0.25 * bloom) * currentAlpha);
                        cr.LineWidth = 2.4;
                    }
                    else
                    {
                        cr.SetSourceRGBA(0.55, 0.82, 1.00, (0.50 + 0.45 * bloom) * currentAlpha);
                        cr.LineWidth = 2.0;
                    }
                    cr.Stroke();
                }

                // Render Content (Icon, Pill Badges, Labels, Active Indicator)
                double midDeg = startDeg + (sliceAngle / 2.0);
                double midRad = midDeg * Math.PI / 180.0;
                double textRadius = (radiusInner + effectiveOuterRadius) / 2.0;
                double itemX = centerX + Math.Cos(midRad) * textRadius;
                double itemY = centerY + Math.Sin(midRad) * textRadius;

                string rawText = item.Label ?? string.Empty;

                bool isActiveWorkspace = false;
                if (rawText.StartsWith("✓ "))
                {
                    isActiveWorkspace = true;
                    rawText = rawText.Substring(2).Trim();
                }

                string badgeText = string.Empty;
                string labelText = rawText;

                if (rawText.StartsWith("-> "))
                {
                    rawText = rawText.Substring(3).Trim();
                }

                if (rawText.StartsWith("[") && rawText.Contains("]"))
                {
                    int endIdx = rawText.IndexOf(']');
                    badgeText = rawText.Substring(1, endIdx - 1);
                    labelText = rawText.Substring(endIdx + 1).Trim();
                }
                else if (rawText.StartsWith("WS "))
                {
                    badgeText = rawText.Substring(3).Trim();
                    labelText = string.Empty;
                }

                cr.SelectFontFace(fontFamily, FontSlant.Normal, (bloom > 0.4f) ? FontWeight.Bold : FontWeight.Normal);

                if (!string.IsNullOrEmpty(badgeText))
                {
                    // Render workspace number badge OUTSIDE the slot perimeter
                    double badgeRadius = effectiveOuterRadius + 22.0 + (2.0 * bloom);
                    double badgeCenterX = centerX + Math.Cos(midRad) * badgeRadius;
                    double badgeCenterY = centerY + Math.Sin(midRad) * badgeRadius;

                    cr.SetFontSize(11.5);
                    var bExt = cr.TextExtents(badgeText);
                    double pillW = Math.Max(26, bExt.Width + 14);
                    double pillH = 19;
                    double pillX = badgeCenterX - (pillW / 2.0);
                    double pillY = badgeCenterY - (pillH / 2.0);

                    DrawRoundedRectangle(cr, pillX, pillY, pillW, pillH, pillH / 2.0);

                    if (isSolid)
                    {
                        if (bloom > 0.01f)
                            cr.SetSourceRGBA(0.04, 0.52, 1.00, currentAlpha);
                        else
                            cr.SetSourceRGBA(0.16, 0.16, 0.20, 0.95 * currentAlpha);
                    }
                    else
                    {
                        if (bloom > 0.01f)
                            cr.SetSourceRGBA(0.05, 0.50, 1.00, 0.90 * currentAlpha);
                        else
                            cr.SetSourceRGBA(0.12, 0.12, 0.16, 0.85 * currentAlpha);
                    }
                    cr.FillPreserve();

                    cr.SetSourceRGBA(1.0, 1.0, 1.0, (bloom > 0.01f ? 0.45 : 0.20) * currentAlpha);
                    cr.LineWidth = 1.2;
                    cr.Stroke();

                    // Text inside outer capsule
                    cr.SetSourceRGBA(1.0, 1.0, 1.0, (bloom > 0.4f ? 1.0 : 0.95) * currentAlpha);
                    cr.MoveTo(pillX + (pillW - bExt.Width) / 2.0 - bExt.XBearing, pillY + (pillH - bExt.Height) / 2.0 - bExt.YBearing);
                    cr.ShowText(badgeText);

                    // Active workspace glowing dot indicator
                    if (isActiveWorkspace)
                    {
                        double dotX = pillX - 7;
                        double dotY = badgeCenterY;

                        // Soft halo
                        cr.Arc(dotX, dotY, 4.5, 0, 2 * Math.PI);
                        cr.SetSourceRGBA(0.19, 0.82, 0.35, 0.40 * currentAlpha);
                        cr.Fill();

                        // Core glowing dot
                        cr.Arc(dotX, dotY, 2.5, 0, 2 * Math.PI);
                        cr.SetSourceRGBA(0.19, 0.82, 0.35, 1.00 * currentAlpha);
                        cr.Fill();
                    }

                    // Render unrotated upright app icons inside the trapezoidal slice slot
                    var apps = item.AppClasses ?? new System.Collections.Generic.List<string>();
                    if (apps.Count > 0)
                    {
                        DrawWorkspaceAppIcons(cr, apps, centerX, centerY, midRad, radiusInner, effectiveOuterRadius, currentAlpha);
                    }
                    else if (!string.IsNullOrEmpty(labelText))
                    {
                        // Fallback title if apps not resolved
                        cr.SetFontSize(10.5);
                        var lExt = cr.TextExtents(labelText);
                        cr.MoveTo(itemX - (lExt.Width / 2.0) - lExt.XBearing, itemY - (lExt.Height / 2.0) - lExt.YBearing);
                        cr.SetSourceRGBA(1.0, 1.0, 1.0, (bloom > 0.4f ? 1.0 : 0.85) * currentAlpha);
                        cr.ShowText(labelText);
                    }
                }
                else
                {
                    // Standard centered action label
                    cr.SetFontSize(12);
                    var ext = cr.TextExtents(item.Label);
                    cr.MoveTo(itemX - (ext.Width / 2.0) - ext.XBearing, itemY - (ext.Height / 2.0) - ext.YBearing);
                    cr.SetSourceRGBA(1.0, 1.0, 1.0, (bloom > 0.4f ? 1.0 : 0.88) * currentAlpha);
                    cr.ShowText(item.Label);
                }
            }

            // Draw Center Deadzone Orb
            cr.NewPath();
            cr.Arc(centerX, centerY, radiusInner, 0, 2 * Math.PI);

            if (isSolid)
            {
                cr.SetSourceRGBA(0.07, 0.07, 0.09, currentAlpha);
                cr.FillPreserve();

                cr.SetSourceRGBA(0.24, 0.24, 0.30, 0.95 * currentAlpha);
                cr.LineWidth = 1.5;
                cr.Stroke();
            }
            else
            {
                using (var grad = new RadialGradient(centerX, centerY, 0, centerX, centerY, radiusInner))
                {
                    grad.AddColorStop(0.0, new Cairo.Color(0.09, 0.09, 0.12, 0.88 * currentAlpha));
                    grad.AddColorStop(1.0, new Cairo.Color(0.05, 0.05, 0.08, 0.94 * currentAlpha));
                    cr.SetSource(grad);
                    cr.FillPreserve();
                }

                cr.SetSourceRGBA(1.0, 1.0, 1.0, 0.14 * currentAlpha);
                cr.LineWidth = 1.2;
                cr.Stroke();
            }

            // Center Navigation Chevron / Cancel Icon (pure Cairo vectors: crisp & immune to missing font glyphs)
            if (IsInWorkspaceSubMenu)
            {
                // Crisp back chevron ‹
                double cw = 3.2;
                double ch = 5.2;
                cr.MoveTo(centerX + cw * 0.4, centerY - ch);
                cr.LineTo(centerX - cw * 0.6, centerY);
                cr.LineTo(centerX + cw * 0.4, centerY + ch);
                cr.SetSourceRGBA(0.85, 0.85, 0.90, 0.85 * currentAlpha);
                cr.LineWidth = 1.8;
                cr.LineCap = LineCap.Round;
                cr.LineJoin = LineJoin.Round;
                cr.Stroke();
            }
            else
            {
                // Crisp modern cancel cross ✕
                double size = 4.2;
                cr.MoveTo(centerX - size, centerY - size);
                cr.LineTo(centerX + size, centerY + size);
                cr.MoveTo(centerX + size, centerY - size);
                cr.LineTo(centerX - size, centerY + size);
                cr.SetSourceRGBA(0.80, 0.80, 0.85, 0.85 * currentAlpha);
                cr.LineWidth = 1.8;
                cr.LineCap = LineCap.Round;
                cr.Stroke();
            }

            // Center Deadzone Return Animated Progress Arc
            if (IsInWorkspaceSubMenu && _deadzoneProgress > 0.005f)
            {
                double pStart = -Math.PI / 2.0;
                double pEnd = pStart + (2 * Math.PI * _deadzoneProgress);
                cr.Arc(centerX, centerY, radiusInner - 2.0, pStart, pEnd);
                cr.SetSourceRGBA(0.04, 0.52, 1.00, 0.95 * currentAlpha);
                cr.LineWidth = 2.5;
                cr.Stroke();
            }
        }

        [GLib.ConnectBefore]
        private void OnButtonPress(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button == 1) // Left Click
            {
                double clickDist = 0;
                if (_isLayerShellActive)
                {
                    double dx = args.Event.X - _anchorPos.X;
                    double dy = args.Event.Y - _anchorPos.Y;
                    clickDist = Math.Sqrt(dx * dx + dy * dy);
                    if (clickDist > _config.Radius + 50)
                    {
                        Dismiss();
                        return;
                    }
                }
                else
                {
                    double dx = args.Event.X - Allocation.Width / 2.0;
                    double dy = args.Event.Y - Allocation.Height / 2.0;
                    clickDist = Math.Sqrt(dx * dx + dy * dy);
                    if (clickDist > _config.Radius + 50)
                    {
                        Dismiss();
                        return;
                    }
                }

                if (_hoveredSlice >= 0 && _hoveredSlice < _config.Items.Count)
                {
                    var item = _config.Items[_hoveredSlice];
                    if (item.IsSubLayer)
                    {
                        // Expand into workspace layer
                        bool isMove = item.Action?.Type == HudActionType.MoveWindowWorkspaceLayer ||
                                      item.Binding?.Path?.Contains("CompositorMoveWindowHudBinding") == true;
                        SwitchToWorkspaceLayer(isMove);
                        return;
                    }

                    Dismiss();
                    ItemActivated?.Invoke(item);
                }
                else if (IsInWorkspaceSubMenu && clickDist <= _config.DeadzoneRadius)
                {
                    // Center clicked in sub-layer: back to root menu
                    RestoreRootMenu();
                }
                else
                {
                    // Clicked deadzone or outside
                    Dismiss();
                }
            }
        }
    }
}
