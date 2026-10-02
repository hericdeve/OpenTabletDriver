using System;
using System.Runtime.InteropServices;

namespace OpenTabletDriver.UX.Gtk.Interop
{
    public static class GtkLayerShell
    {
        private const string LIBRARY = "libgtk-layer-shell.so.0";

        public enum Layer
        {
            Background = 0,
            Bottom = 1,
            Top = 2,
            Overlay = 3
        }

        public enum Edge
        {
            Left = 0,
            Right = 1,
            Top = 2,
            Bottom = 3
        }

        public enum KeyboardMode
        {
            None = 0,
            Exclusive = 1,
            OnDemand = 2
        }

        private static bool? _isSupported;

        public static bool IsSupported
        {
            get
            {
                if (!_isSupported.HasValue)
                {
                    try
                    {
                        _isSupported = gtk_layer_is_supported();
                    }
                    catch
                    {
                        _isSupported = false;
                    }
                }
                return _isSupported.Value;
            }
        }

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_is_supported")]
        private static extern bool gtk_layer_is_supported();

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_init_for_window")]
        public static extern void InitForWindow(IntPtr window);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_set_layer")]
        public static extern void SetLayer(IntPtr window, Layer layer);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_set_anchor")]
        public static extern void SetAnchor(IntPtr window, Edge edge, bool anchor);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_set_margin")]
        public static extern void SetMargin(IntPtr window, Edge edge, int margin);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_set_exclusive_zone")]
        public static extern void SetExclusiveZone(IntPtr window, int exclusiveZone);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_set_keyboard_mode")]
        public static extern void SetKeyboardMode(IntPtr window, KeyboardMode mode);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_set_namespace")]
        public static extern void SetNamespace(IntPtr window, [MarshalAs(UnmanagedType.LPStr)] string ns);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_set_monitor")]
        public static extern void SetMonitor(IntPtr window, IntPtr monitor);

        [DllImport(LIBRARY, EntryPoint = "gtk_layer_get_monitor")]
        public static extern IntPtr GetMonitor(IntPtr window);
    }
}
