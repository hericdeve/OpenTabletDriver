using System;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.UX.Gtk.Hud;

namespace OpenTabletDriver.UX.Gtk
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            App.Initialized += () =>
            {
                HudManager.Instance.Initialize();
            };

            App.Terminating += () =>
            {
                GLib.Idle.Add(() =>
                {
                    try
                    {
                        HudManager.Instance.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex);
                    }
                    return false;
                });
            };

            App.Run(Eto.Platforms.Gtk, args);
        }
    }
}
