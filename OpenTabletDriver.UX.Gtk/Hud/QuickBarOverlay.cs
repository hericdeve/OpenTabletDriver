using System;
using System.Numerics;
using Gtk;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.UX.Gtk.Interop;
using GtkWindow = Gtk.Window;

namespace OpenTabletDriver.UX.Gtk.Hud
{
    public class QuickBarOverlay : GtkWindow
    {
        private HudConfiguration _config;
        private Box _box;
        private bool _isLayerShellActive;

        public event Action<HudItem>? ItemActivated;

        public QuickBarOverlay() : base(global::Gtk.WindowType.Toplevel)
        {
            _config = HudConfiguration.GetDefaults();

            Decorated = false;
            SkipTaskbarHint = true;
            SkipPagerHint = true;
            KeepAbove = true;

            var screen = Screen;
            var visual = screen?.RgbaVisual;
            if (visual != null)
                Visual = visual;

            _box = new Box(Orientation.Horizontal, 6);
            _box.MarginStart = 10;
            _box.MarginEnd = 10;
            _box.MarginTop = 8;
            _box.MarginBottom = 8;
            Add(_box);

            if (GtkLayerShell.IsSupported)
            {
                GtkLayerShell.InitForWindow(Handle);
                GtkLayerShell.SetLayer(Handle, GtkLayerShell.Layer.Overlay);
                GtkLayerShell.SetKeyboardMode(Handle, GtkLayerShell.KeyboardMode.None);
                GtkLayerShell.SetNamespace(Handle, "otd-quickbar-hud");
                _isLayerShellActive = true;
            }
        }

        public void ShowAt(Vector2 position, HudConfiguration config)
        {
            _config = config;
            RebuildButtons();

            int x = (int)position.X;
            int y = (int)position.Y - 50;

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
        }

        public void Dismiss()
        {
            if (!_config.Pinned)
                Hide();
        }

        private void RebuildButtons()
        {
            foreach (var child in _box.Children)
            {
                _box.Remove(child);
                child.Dispose();
            }

            // Drag handle / Title
            var handleLabel = new Label("⋮⋮") { TooltipText = "Drag Quick Bar" };
            _box.PackStart(handleLabel, false, false, 2);

            foreach (var item in _config.Items)
            {
                var btnText = !string.IsNullOrEmpty(item.Icon) ? $"{item.Icon} {item.Label}" : item.Label;
                var btn = new Button(btnText);
                btn.Clicked += (_, _) =>
                {
                    ItemActivated?.Invoke(item);
                    Dismiss();
                };
                _box.PackStart(btn, false, false, 0);
            }

            // Close button
            var closeBtn = new Button("✕");
            closeBtn.Clicked += (_, _) => Hide();
            _box.PackEnd(closeBtn, false, false, 2);

            _box.ShowAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _box?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
