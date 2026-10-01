using System;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.UX.Gtk.Hud;

namespace OpenTabletDriver.UX.Gtk
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(Eto.GtkSharp.Platform).Assembly, (libraryName, assembly, searchPath) =>
                {
                    if (libraryName == "libappindicator3.so.1")
                    {
                        string[] candidates = ["libappindicator3.so.1", "libayatana-appindicator3.so.1", "libappindicator3.so", "libayatana-appindicator3.so"];
                        foreach (var candidate in candidates)
                        {
                            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                                return handle;
                        }
                    }
                    return IntPtr.Zero;
                });
            }
            catch (Exception ex)
            {
                Log.Debug("TrayResolver", $"Failed to set DllImportResolver: {ex.Message}");
            }

            App.RequestQuit += () =>
            {
                GLib.Timeout.Add(50, () =>
                {
                    try
                    {
                        global::Gtk.Application.Quit();
                    }
                    catch
                    {
                    }
                    return false;
                });
            };

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
