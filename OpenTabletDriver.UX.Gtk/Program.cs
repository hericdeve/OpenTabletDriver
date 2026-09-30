using System;
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

            App.Run(Eto.Platforms.Gtk, args);
        }
    }
}
