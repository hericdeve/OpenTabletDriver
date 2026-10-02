using System;
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
        private Vector2 _anchorPos;
        private Vector2 _currentPos;
        private Vector2 _monitorOrigin = Vector2.Zero;
        private int _hoveredSlice = -1;
        private bool _isLayerShellActive;

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

            // Deduplicate items if corrupted settings file had repeated items
            var seen = new System.Collections.Generic.HashSet<string>();
            var unique = new System.Collections.Generic.List<HudItem>();
            foreach (var item in _config.Items)
            {
                if (seen.Add(item.Label))
                    unique.Add(item);
            }
            _config.Items = unique;

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

                int diameter = (int)(_config.Radius * 2 + 40);
                SetDefaultSize(diameter, diameter);
                Resize(diameter, diameter);
                int x = (int)(position.X - diameter / 2f);
                int y = (int)(position.Y - diameter / 2f);
                Move(x, y);
            }

            _hoveredSlice = -1;

            Log.Write("HUD_GTK", $"Showing RadialMenu at {position.X},{position.Y} (monitor origin: {_monitorOrigin.X},{_monitorOrigin.Y}, local: {_anchorPos.X},{_anchorPos.Y})");
            ShowAll();
            QueueDraw();
        }

        public void UpdatePosition(Vector2 position, int hoveredSlice = -1)
        {
            _currentPos = position - _monitorOrigin;
            if (_hoveredSlice != hoveredSlice)
            {
                _hoveredSlice = hoveredSlice;
                QueueDraw();
            }
        }

        public void Dismiss()
        {
            Hide();
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
            double radiusOuter = _config.Radius;
            double radiusInner = _config.DeadzoneRadius;

            // Render slices
            for (int i = 0; i < count; i++)
            {
                double startDeg = (i * sliceAngle) - (sliceAngle / 2.0) - 90.0;
                double endDeg = startDeg + sliceAngle;

                double startRad = startDeg * Math.PI / 180.0;
                double endRad = endDeg * Math.PI / 180.0;

                bool isHovered = (i == _hoveredSlice);

                cr.NewPath();
                cr.Arc(centerX, centerY, isHovered ? radiusOuter + 8 : radiusOuter, startRad, endRad);
                cr.ArcNegative(centerX, centerY, radiusInner, endRad, startRad);
                cr.ClosePath();

                // Wedge fill
                if (isHovered)
                    cr.SetSourceRGBA(0.18, 0.55, 0.95, Math.Min(1.0, _config.Opacity + 0.1));
                else
                    cr.SetSourceRGBA(0.14, 0.14, 0.18, _config.Opacity * 0.9);
                cr.FillPreserve();

                // Wedge border
                if (isHovered)
                    cr.SetSourceRGBA(0.5, 0.85, 1.0, 1.0);
                else
                    cr.SetSourceRGBA(0.28, 0.28, 0.33, 0.6);
                cr.LineWidth = isHovered ? 3.0 : 1.2;
                cr.Stroke();

                // Draw Slice Content (Icon & Text)
                double midDeg = startDeg + (sliceAngle / 2.0);
                double midRad = midDeg * Math.PI / 180.0;
                double textRadius = (radiusInner + radiusOuter) / 2.0;
                double itemX = centerX + Math.Cos(midRad) * textRadius;
                double itemY = centerY + Math.Sin(midRad) * textRadius;

                var item = _config.Items[i];
                cr.SelectFontFace("Sans", FontSlant.Normal, isHovered ? FontWeight.Bold : FontWeight.Normal);

                // Label
                cr.SetFontSize(12);
                var ext = cr.TextExtents(item.Label);
                cr.MoveTo(itemX - (ext.Width / 2.0) - ext.XBearing, itemY - (ext.Height / 2.0) - ext.YBearing);
                cr.SetSourceRGBA(1.0, 1.0, 1.0, isHovered ? 1.0 : 0.85);
                cr.ShowText(item.Label);
            }

            // Draw Center Deadzone Ring
            cr.NewPath();
            cr.Arc(centerX, centerY, radiusInner, 0, 2 * Math.PI);
            cr.SetSourceRGBA(0.08, 0.08, 0.10, _config.Opacity * 0.95);
            cr.FillPreserve();

            cr.SetSourceRGBA(0.3, 0.3, 0.35, 0.8);
            cr.LineWidth = 1.5;
            cr.Stroke();

            // Center Cancel / Pin Icon
            cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            cr.SetFontSize(14);
            var centerExt = cr.TextExtents("X");
            cr.MoveTo(centerX - (centerExt.Width / 2.0) - centerExt.XBearing, centerY - (centerExt.Height / 2.0) - centerExt.YBearing);
            cr.SetSourceRGBA(0.6, 0.6, 0.65, 0.8);
            cr.ShowText("X");
        }

        [GLib.ConnectBefore]
        private void OnButtonPress(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button == 1) // Left Click
            {
                // In full-screen layer-shell, clicking outside the menu dismisses the HUD
                if (_isLayerShellActive)
                {
                    double dx = args.Event.X - _anchorPos.X;
                    double dy = args.Event.Y - _anchorPos.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > _config.Radius + 20)
                    {
                        Dismiss();
                        return;
                    }
                }

                if (_hoveredSlice >= 0 && _hoveredSlice < _config.Items.Count)
                {
                    var item = _config.Items[_hoveredSlice];
                    Dismiss();
                    ItemActivated?.Invoke(item);
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
