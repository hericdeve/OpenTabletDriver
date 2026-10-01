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
                    var config = request.Configuration ?? HudConfiguration.GetDefaults();
                    Log.Write("HUD_MGR", $"Show requested with FormFactor: {config.FormFactor}");
                    if (config.FormFactor == HudFormFactor.QuickBar)
                    {
                        _radialMenu?.Dismiss();
                        _quickBar?.ShowAt(request.CursorPosition, config);
                    }
                    else
                    {
                        _quickBar?.Dismiss();
                        _radialMenu?.ShowAt(request.CursorPosition, config);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
                return false;
            });
        }

        private bool _isUpdatePending;
        private HudUpdateRequest? _pendingUpdateRequest;

        private void OnUpdateHudRequested(object? sender, HudUpdateRequest request)
        {
            _pendingUpdateRequest = request;
            if (_isUpdatePending)
                return;

            _isUpdatePending = true;
            GLib.Idle.Add(() =>
            {
                _isUpdatePending = false;
                var req = _pendingUpdateRequest;
                if (req != null)
                {
                    try
                    {
                        _radialMenu?.UpdatePosition(req.CursorPosition, req.HoveredSlice);
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex);
                    }
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
            _ = App.Driver.Instance?.ExecuteHudItem(item);
        }
    }
}
