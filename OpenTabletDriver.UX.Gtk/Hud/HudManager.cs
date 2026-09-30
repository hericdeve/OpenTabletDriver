using System;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Logging;

namespace OpenTabletDriver.UX.Gtk.Hud
{
    public class HudManager
    {
        private static HudManager? _instance;
        public static HudManager Instance => _instance ??= new HudManager();

        private RadialMenuOverlay? _radialMenu;
        private QuickBarOverlay? _quickBar;

        public void Initialize()
        {
            App.Driver.ShowHudRequested += OnShowHudRequested;
            App.Driver.UpdateHudRequested += OnUpdateHudRequested;
            App.Driver.DismissHudRequested += OnDismissHudRequested;
        }

        private void EnsureCreated()
        {
            if (_radialMenu == null)
            {
                _radialMenu = new RadialMenuOverlay();
                _radialMenu.ItemActivated += OnItemActivated;
            }

            if (_quickBar == null)
            {
                _quickBar = new QuickBarOverlay();
                _quickBar.ItemActivated += OnItemActivated;
            }
        }

        private void OnShowHudRequested(object? sender, HudShowRequest request)
        {
            GLib.Idle.Add(() =>
            {
                try
                {
                    EnsureCreated();
                    if (request.Configuration.FormFactor == HudFormFactor.QuickBar)
                    {
                        _radialMenu?.Dismiss();
                        _quickBar?.ShowAt(request.CursorPosition, request.Configuration);
                    }
                    else
                    {
                        _quickBar?.Dismiss();
                        _radialMenu?.ShowAt(request.CursorPosition, request.Configuration);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
                return false;
            });
        }

        private void OnUpdateHudRequested(object? sender, HudUpdateRequest request)
        {
            GLib.Idle.Add(() =>
            {
                try
                {
                    _radialMenu?.UpdatePosition(request.CursorPosition);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
                return false;
            });
        }

        private void OnDismissHudRequested(object? sender, EventArgs e)
        {
            GLib.Idle.Add(() =>
            {
                try
                {
                    _radialMenu?.Dismiss();
                    _quickBar?.Dismiss();
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
                return false;
            });
        }

        private void OnItemActivated(HudItem item)
        {
            _ = App.Driver.Instance?.ExecuteHudAction(item.Action);
        }
    }
}
