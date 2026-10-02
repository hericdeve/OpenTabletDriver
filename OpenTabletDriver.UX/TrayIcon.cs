using System;
using System.Collections.Generic;
using Eto.Forms;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX
{
    public sealed class TrayIcon : IDisposable
    {
        public TrayIcon(MainForm window)
        {
            this.window = window;

            Indicator = new TrayIndicator
            {
                Title = "OpenTabletDriver",
                Image = App.Logo
            };

            RefreshMenuItems();
            Indicator.Show();

            Indicator.Activated += (_, _) =>
            {
                try
                {
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    if (!window.Visible)
                        window.Show();
                    window.BringToFront();
                    window.Focus();
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
            };
        }

        public TrayIndicator Indicator { get; }
        private MainForm window;

        public void Dispose()
        {
            Indicator.Hide();
            Indicator.Dispose();
        }

        // macOS doesn't render a menu bar for windows of background apps
        // This is used to clone the items of the menu bar to the context menu
        private static MenuItem CloneMenuItem(MenuItem original)
        {
            if (original is SeparatorMenuItem)
            {
                return new SeparatorMenuItem();
            }

            if (original is ButtonMenuItem buttonItem)
            {
                var cloned = new ButtonMenuItem
                {
                    Text = buttonItem.Text,
                    Enabled = buttonItem.Enabled,
                    Shortcut = buttonItem.Shortcut
                };

                // Clone sub-items recursively
                foreach (var subItem in buttonItem.Items)
                {
                    cloned.Items.Add(CloneMenuItem(subItem));
                }

                // Re-create the click handler by triggering the original item's click
                cloned.Click += (sender, e) => buttonItem.PerformClick();

                return cloned;
            }

            return original;
        }

        public void RefreshMenuItems()
        {
            try
            {
                var showWindow = new ButtonMenuItem
                {
                    Text = "Show Window"
                };
                showWindow.Click += (sender, e) =>
                {
                    try
                    {
                        if (window.WindowState == WindowState.Minimized)
                            window.WindowState = WindowState.Normal;
                        if (!window.Visible)
                            window.Show();
                        window.BringToFront();
                        window.Focus();
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex);
                    }
                };

                var close = new ButtonMenuItem
                {
                    Text = "Close"
                };
                close.Click += (sender, e) => window.Close();

                if (DesktopInterop.CurrentPlatform == PluginPlatform.MacOS)
                {
                    // It's more idiomatic for macOS to include the name here
                    showWindow.Text = "Show OpenTabletDriver";

                    // Applications on macOS will keep running even after closing all their windows
                    // Offering a way to quit the app here is more idiomatic
                    close.Text = "Quit";
                    close.Click += (sender, e) => App.Exit();
                }
                else if (DesktopInterop.CurrentPlatform == PluginPlatform.Linux)
                {
                    showWindow.Text = "Open OpenTabletDriver";

                    // On Linux, closing the window hides it to background to keep services (Floating HUD) active.
                    // Offering a Quit action allows exiting the background app cleanly.
                    close.Text = "Quit";
                    close.Click += (sender, e) => App.Exit();
                }

                var items = new List<MenuItem>();
                var presets = AppInfo.PresetManager.GetPresets();

                if (presets.Count != 0)
                {
                    foreach (var preset in presets)
                    {
                        var presetItem = new ButtonMenuItem
                        {
                            Text = preset.Name
                        };
                        presetItem.Click += MainForm.PresetButtonHandler;

                        items.Add(presetItem);
                    }

                    items.Add(new SeparatorMenuItem());
                }

                items.Add(showWindow);

                // macOS doesn't present a menu bar for agent apps
                if (DesktopInterop.CurrentPlatform == PluginPlatform.MacOS && window.Menu != null)
                {
                    items.Add(new SeparatorMenuItem());

                    var fileMenu = window.Menu.Items.GetSubmenu("&File") as ButtonMenuItem;
                    if (fileMenu != null)
                    {
                        foreach (var item in fileMenu.Items)
                        {
                            if (item.Text == "Close" || item.Text == "Presets")
                                continue;

                            items.Add(CloneMenuItem(item));
                        }
                        items.Add(new SeparatorMenuItem());
                    }

                    var presetsMenu = window.Menu.Items.GetSubmenu("&Presets");
                    if (presetsMenu != null)
                        items.Add(CloneMenuItem(presetsMenu));

                    var profilesMenu = window.Menu.Items.GetSubmenu("P&rofiles");
                    if (profilesMenu != null)
                        items.Add(CloneMenuItem(profilesMenu));

                    var tabletsMenu = window.Menu.Items.GetSubmenu("Tablets");
                    if (tabletsMenu != null)
                        items.Add(CloneMenuItem(tabletsMenu));

                    var pluginsMenu = window.Menu.Items.GetSubmenu("Plugins");
                    if (pluginsMenu != null)
                        items.Add(CloneMenuItem(pluginsMenu));

                    var helpMenu = window.Menu.Items.GetSubmenu("&Help");
                    if (helpMenu != null)
                        items.Add(CloneMenuItem(helpMenu));

                    items.Add(new SeparatorMenuItem());
                }

                items.Add(close);

                // Safely update or assign the menu
                Application.Instance.AsyncInvoke(() =>
                {
                    try
                    {
                        Indicator.Menu = new ContextMenu(items);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("TrayIcon", $"Failed to update context menu: {ex.Message}", LogLevel.Debug);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Write("TrayIcon", $"Failed to refresh tray menu items: {ex.Message}", LogLevel.Debug);
            }
        }
    }
}
