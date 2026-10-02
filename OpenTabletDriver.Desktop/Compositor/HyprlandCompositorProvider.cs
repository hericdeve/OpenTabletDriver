using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Interop.AppProfiler;
using OpenTabletDriver.Desktop.Interop.Display;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

#nullable enable

namespace OpenTabletDriver.Desktop.Compositor
{
    public class HyprlandCompositorProvider : ICompositorProvider
    {
        private readonly HyprlandIpcService _ipcService;

        public HyprlandCompositorProvider()
        {
            _ipcService = new HyprlandIpcService();
        }

        public string Id => "hyprland";
        public string DisplayName => "Hyprland / Linux (Noctalia Shell)";

        public bool IsAvailable =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")) &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));

        private string SendDispatch(string modernLuaCmd, string legacyCmd)
        {
            var res = _ipcService.SendCommand($"dispatch {modernLuaCmd}").Trim();
            if (res.Equals("ok", StringComparison.OrdinalIgnoreCase))
                return res;

            return _ipcService.SendCommand($"dispatch {legacyCmd}").Trim();
        }

        public Task<IReadOnlyList<WorkspaceInfo>> GetWorkspacesAsync()
        {
            var list = new List<WorkspaceInfo>();
            if (!IsAvailable)
                return Task.FromResult<IReadOnlyList<WorkspaceInfo>>(list);

            try
            {
                var activeWs = GetActiveWorkspaceInternal();
                var json = _ipcService.SendCommand("j/workspaces");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt32().ToString() : "";
                            var name = el.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? id : id;
                            var mon = el.TryGetProperty("monitor", out var monProp) ? monProp.GetString() ?? "" : "";
                            var wins = el.TryGetProperty("windows", out var winProp) ? winProp.GetInt32() : 0;
                            var lastTitle = el.TryGetProperty("lastwindowtitle", out var titleProp) ? titleProp.GetString() : null;

                            bool isActive = activeWs != null && (activeWs.Id == id || activeWs.Name == name);

                            list.Add(new WorkspaceInfo
                            {
                                Id = id,
                                Name = name,
                                Monitor = mon,
                                WindowsCount = wins,
                                IsActive = isActive,
                                LastWindowTitle = lastTitle
                            });
                        }
                    }
                }

                // If active workspace exists and wasn't in list (e.g. newly created/empty)
                if (activeWs != null && !list.Any(w => w.Id == activeWs.Id))
                {
                    activeWs.IsActive = true;
                    list.Add(activeWs);
                }

                // Sort numerically if possible, else alphabetically
                list = list.OrderBy(w => int.TryParse(w.Id, out var n) ? n : 9999).ThenBy(w => w.Name).ToList();
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandCompositorProvider", $"Failed to fetch workspaces: {ex.Message}", LogLevel.Error);
            }

            return Task.FromResult<IReadOnlyList<WorkspaceInfo>>(list);
        }

        private WorkspaceInfo? GetActiveWorkspaceInternal()
        {
            try
            {
                var json = _ipcService.SendCommand("j/activeworkspace");
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32().ToString() : "";
                var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? id : id;
                var mon = root.TryGetProperty("monitor", out var monProp) ? monProp.GetString() ?? "" : "";
                var wins = root.TryGetProperty("windows", out var winProp) ? winProp.GetInt32() : 0;
                var lastTitle = root.TryGetProperty("lastwindowtitle", out var titleProp) ? titleProp.GetString() : null;

                return new WorkspaceInfo
                {
                    Id = id,
                    Name = name,
                    Monitor = mon,
                    WindowsCount = wins,
                    IsActive = true,
                    LastWindowTitle = lastTitle
                };
            }
            catch
            {
                return null;
            }
        }

        public Task<WorkspaceInfo?> GetActiveWorkspaceAsync()
        {
            return Task.FromResult(GetActiveWorkspaceInternal());
        }

        public Task<bool> FocusWorkspaceAsync(string workspaceIdOrName)
        {
            if (string.IsNullOrWhiteSpace(workspaceIdOrName) || !IsAvailable)
                return Task.FromResult(false);

            try
            {
                var res = SendDispatch($"hl.dsp.focus({{ workspace = \"{workspaceIdOrName}\" }})", $"workspace {workspaceIdOrName}");
                Log.Write("HyprlandCompositorProvider", $"Focus workspace '{workspaceIdOrName}': {res}", LogLevel.Debug);
                return Task.FromResult(res.Equals("ok", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandCompositorProvider", $"Focus workspace error: {ex.Message}", LogLevel.Error);
                return Task.FromResult(false);
            }
        }

        public Task<bool> MoveWindowToWorkspaceAsync(string workspaceIdOrName, bool followFocus = true)
        {
            if (string.IsNullOrWhiteSpace(workspaceIdOrName) || !IsAvailable)
                return Task.FromResult(false);

            try
            {
                string modern = $"hl.dsp.window.move({{ workspace = \"{workspaceIdOrName}\", follow = {(followFocus ? "true" : "false")} }})";
                string legacy = followFocus ? $"movetoworkspace {workspaceIdOrName}" : $"movetoworkspacesilent {workspaceIdOrName}";
                var res = SendDispatch(modern, legacy);
                Log.Write("HyprlandCompositorProvider", $"Move window to workspace '{workspaceIdOrName}': {res}", LogLevel.Debug);
                return Task.FromResult(res.Equals("ok", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandCompositorProvider", $"Move window error: {ex.Message}", LogLevel.Error);
                return Task.FromResult(false);
            }
        }

        public Task<bool> FocusWindowAsync(WindowDirection direction)
        {
            if (!IsAvailable) return Task.FromResult(false);

            try
            {
                string res;
                switch (direction)
                {
                    case WindowDirection.Next:
                        res = SendDispatch("hl.dsp.window.cycle_next()", "cyclenext");
                        break;
                    case WindowDirection.Previous:
                        res = SendDispatch("hl.dsp.window.cycle_prev()", "cyclenext prev");
                        break;
                    case WindowDirection.Left:
                        res = SendDispatch("hl.dsp.focus({ direction = \"l\" })", "movefocus l");
                        break;
                    case WindowDirection.Right:
                        res = SendDispatch("hl.dsp.focus({ direction = \"r\" })", "movefocus r");
                        break;
                    case WindowDirection.Up:
                        res = SendDispatch("hl.dsp.focus({ direction = \"u\" })", "movefocus u");
                        break;
                    case WindowDirection.Down:
                        res = SendDispatch("hl.dsp.focus({ direction = \"d\" })", "movefocus d");
                        break;
                    default:
                        return Task.FromResult(false);
                }

                return Task.FromResult(res.Equals("ok", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandCompositorProvider", $"Focus window error: {ex.Message}", LogLevel.Error);
                return Task.FromResult(false);
            }
        }

        public Task<bool> MoveWindowAsync(WindowDirection direction)
        {
            if (!IsAvailable) return Task.FromResult(false);

            try
            {
                string res;
                switch (direction)
                {
                    case WindowDirection.Next:
                        res = SendDispatch("hl.dsp.window.swap({ next = true })", "swapnext");
                        break;
                    case WindowDirection.Previous:
                        res = SendDispatch("hl.dsp.window.swap({ prev = true })", "swapnext prev");
                        break;
                    case WindowDirection.Left:
                        res = SendDispatch("hl.dsp.window.move({ direction = \"l\" })", "movewindow l");
                        break;
                    case WindowDirection.Right:
                        res = SendDispatch("hl.dsp.window.move({ direction = \"r\" })", "movewindow r");
                        break;
                    case WindowDirection.Up:
                        res = SendDispatch("hl.dsp.window.move({ direction = \"u\" })", "movewindow u");
                        break;
                    case WindowDirection.Down:
                        res = SendDispatch("hl.dsp.window.move({ direction = \"d\" })", "movewindow d");
                        break;
                    default:
                        return Task.FromResult(false);
                }

                return Task.FromResult(res.Equals("ok", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandCompositorProvider", $"Move window error: {ex.Message}", LogLevel.Error);
                return Task.FromResult(false);
            }
        }

        public Task<bool> ToggleWindowFloatAsync()
        {
            if (!IsAvailable) return Task.FromResult(false);
            try
            {
                var res = SendDispatch("hl.dsp.window.float()", "togglefloating");
                return Task.FromResult(res.Equals("ok", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public Task<bool> ToggleWindowFullscreenAsync()
        {
            if (!IsAvailable) return Task.FromResult(false);
            try
            {
                var res = SendDispatch("hl.dsp.window.fullscreen()", "fullscreen 1");
                return Task.FromResult(res.Equals("ok", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public Task<WindowInfo?> GetActiveWindowAsync()
        {
            if (!IsAvailable) return Task.FromResult<WindowInfo?>(null);

            try
            {
                var json = _ipcService.SendCommand("j/activewindow");
                if (string.IsNullOrWhiteSpace(json)) return Task.FromResult<WindowInfo?>(null);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return Task.FromResult<WindowInfo?>(null);

                var addr = root.TryGetProperty("address", out var aProp) ? aProp.GetString() ?? "" : "";
                var title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                var cls = root.TryGetProperty("class", out var cProp) ? cProp.GetString() ?? "" : "";
                var flt = root.TryGetProperty("floating", out var fProp) && fProp.GetBoolean();
                var fs = root.TryGetProperty("fullscreen", out var fsProp) && fsProp.GetInt32() > 0;

                string wsName = "";
                if (root.TryGetProperty("workspace", out var wsProp))
                {
                    if (wsProp.TryGetProperty("name", out var wsn))
                        wsName = wsn.GetString() ?? "";
                    else if (wsProp.TryGetProperty("id", out var wsi))
                        wsName = wsi.GetInt32().ToString();
                }

                int x = 0, y = 0, w = 0, h = 0;
                if (root.TryGetProperty("at", out var atProp) && atProp.GetArrayLength() >= 2)
                {
                    x = atProp[0].GetInt32();
                    y = atProp[1].GetInt32();
                }
                if (root.TryGetProperty("size", out var sProp) && sProp.GetArrayLength() >= 2)
                {
                    w = sProp[0].GetInt32();
                    h = sProp[1].GetInt32();
                }

                return Task.FromResult<WindowInfo?>(new WindowInfo
                {
                    Address = addr,
                    Title = title,
                    Class = cls,
                    Workspace = wsName,
                    Bounds = new Rectangle(x, y, w, h),
                    IsFloating = flt,
                    IsFullscreen = fs
                });
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandCompositorProvider", $"Get active window error: {ex.Message}", LogLevel.Error);
                return Task.FromResult<WindowInfo?>(null);
            }
        }

        public Task<IReadOnlyList<CompositorMonitorInfo>> GetMonitorsAsync()
        {
            var list = new List<CompositorMonitorInfo>();
            if (!IsAvailable) return Task.FromResult<IReadOnlyList<CompositorMonitorInfo>>(list);

            try
            {
                var json = _ipcService.SendCommand("j/monitors");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                            var name = el.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                            var w = el.TryGetProperty("width", out var wProp) ? wProp.GetInt32() : 0;
                            var h = el.TryGetProperty("height", out var hProp) ? hProp.GetInt32() : 0;
                            var x = el.TryGetProperty("x", out var xProp) ? xProp.GetInt32() : 0;
                            var y = el.TryGetProperty("y", out var yProp) ? yProp.GetInt32() : 0;
                            var scale = el.TryGetProperty("scale", out var sProp) ? (float)sProp.GetDouble() : 1.0f;
                            var focused = el.TryGetProperty("focused", out var fProp) && fProp.GetBoolean();

                            list.Add(new CompositorMonitorInfo
                            {
                                Id = id,
                                Name = name,
                                Width = w,
                                Height = h,
                                X = x,
                                Y = y,
                                Scale = scale,
                                IsFocused = focused
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandCompositorProvider", $"Get monitors error: {ex.Message}", LogLevel.Error);
            }

            return Task.FromResult<IReadOnlyList<CompositorMonitorInfo>>(list);
        }

        public Task<bool> CycleMonitorAsync(bool forward = true)
        {
            if (!IsAvailable) return Task.FromResult(false);
            try
            {
                var dir = forward ? "Next" : "Previous";
                var res = SendDispatch($"hl.dsp.focus({{ monitor = \"{(forward ? "+1" : "-1")}\" }})", $"focusmonitor {(forward ? "+1" : "-1")}");
                return Task.FromResult(res.Equals("ok", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public Task<bool> MapTabletToActiveWindowAsync(TabletReference tablet)
        {
            return Task.FromResult(false);
        }
    }
}
