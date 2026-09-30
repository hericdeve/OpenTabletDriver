using System;
using System.Numerics;
using Cairo;
using Gdk;
using Gtk;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.UX.Gtk.Interop;
using GtkWindow = Gtk.Window;

namespace OpenTabletDriver.UX.Gtk.Hud
{
    public class RadialMenuOverlay : GtkWindow
    {
        private HudConfiguration _config;
        private Vector2 _anchorPos;
        private Vector2 _currentPos;
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
            MotionNotifyEvent += OnMotionNotify;

            Events |= EventMask.ButtonPressMask | EventMask.PointerMotionMask;

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
            if (_config.Items.Count > 8)
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                var unique = new System.Collections.Generic.List<HudItem>();
                foreach (var item in _config.Items)
                {
                    if (seen.Add(item.Label))
                        unique.Add(item);
                }
                _config.Items = unique;
            }

            _anchorPos = position;
            _currentPos = position;
            _hoveredSlice = -1;

            int diameter = (int)(_config.Radius * 2 + 40);
            SetDefaultSize(diameter, diameter);
            Resize(diameter, diameter);

            int x = (int)(position.X - diameter / 2f);
            int y = (int)(position.Y - diameter / 2f);

            if (_isLayerShellActive)
            {
                GtkLayerShell.SetAnchor(Handle, GtkLayerShell.Edge.Left, true);
                GtkLayerShell.SetAnchor(Handle, GtkLayerShell.Edge.Top, true);
                GtkLayerShell.SetMargin(Handle, GtkLayerShell.Edge.Left, Math.Max(0, x));
                GtkLayerShell.SetMargin(Handle, GtkLayerShell.Edge.Top, Math.Max(0, y));
            }
            else
            {
                Move(x, y);
            }

            ShowAll();
            QueueDraw();
        }

        public void UpdatePosition(Vector2 position)
        {
            _currentPos = position;
            UpdateHoverState();
            QueueDraw();
        }

        public void Dismiss()
        {
            Hide();
        }

        private void UpdateHoverState()
        {
            Vector2 delta = _currentPos - _anchorPos;
            float dist = delta.Length();

            if (dist < _config.DeadzoneRadius || _config.Items.Count == 0)
            {
                _hoveredSlice = -1;
                return;
            }

            int count = _config.Items.Count;
            float sliceAngle = 360f / count;

            double rad = Math.Atan2(delta.Y, delta.X);
            double deg = (rad * 180.0 / Math.PI) + 90.0;
            if (deg < 0) deg += 360.0;

            _hoveredSlice = (int)Math.Floor((deg + (sliceAngle / 2.0)) / sliceAngle) % count;
        }

        private void OnDrawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            int width = AllocatedWidth;
            int height = AllocatedHeight;
            double centerX = width / 2.0;
            double centerY = height / 2.0;

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
                cr.Arc(centerX, centerY, isHovered ? radiusOuter + 6 : radiusOuter, startRad, endRad);
                cr.ArcNegative(centerX, centerY, radiusInner, endRad, startRad);
                cr.ClosePath();

                // Wedge fill
                if (isHovered)
                    cr.SetSourceRGBA(0.18, 0.55, 0.90, _config.Opacity);
                else
                    cr.SetSourceRGBA(0.12, 0.12, 0.15, _config.Opacity * 0.9);
                cr.FillPreserve();

                // Wedge border
                if (isHovered)
                    cr.SetSourceRGBA(0.4, 0.8, 1.0, 0.9);
                else
                    cr.SetSourceRGBA(0.25, 0.25, 0.30, 0.6);
                cr.LineWidth = isHovered ? 2.5 : 1.2;
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
        private void OnMotionNotify(object o, MotionNotifyEventArgs args)
        {
            int diameter = (int)(_config.Radius * 2 + 40);
            float localX = (float)args.Event.X - (diameter / 2f);
            float localY = (float)args.Event.Y - (diameter / 2f);
            _currentPos = _anchorPos + new Vector2(localX, localY);
            UpdateHoverState();
            QueueDraw();
        }

        [GLib.ConnectBefore]
        private void OnButtonPress(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button == 1) // Left Click
            {
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
