using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using OpenTabletDriver.Plugin;

#nullable enable

namespace OpenTabletDriver.Desktop.Interop.AppProfiler
{
    public class HyprlandWindowProvider : IActiveWindowProvider
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _readTask;

        public event EventHandler<ActiveWindowChangedEventArgs>? ActiveWindowChanged;
        public event EventHandler? MonitorsChanged;

        public bool IsSupported => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

        public void Start()
        {
            if (!IsSupported)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadSocketAsync(_cancellationTokenSource.Token));
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            try
            {
                _readTask?.Wait();
            }
            catch (AggregateException)
            {
                // Expected when task is cancelled
            }
        }

        public void ForceRefreshActiveWindow()
        {
            var ipcService = new HyprlandIpcService();
            var response = ipcService.SendCommand("j/activewindow");
            if (string.IsNullOrEmpty(response))
                return;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("class", out var classElement) && 
                    doc.RootElement.TryGetProperty("title", out var titleElement))
                {
                    var windowClass = classElement.GetString() ?? string.Empty;
                    var windowTitle = titleElement.GetString() ?? string.Empty;
                    ActiveWindowChanged?.Invoke(this, new ActiveWindowChangedEventArgs(windowClass, windowTitle));
                }
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandAppProfiler", $"Failed to force refresh active window: {ex.Message}", LogLevel.Error);
            }
        }

        private async Task ReadSocketAsync(CancellationToken token)
        {
            var hyprlandSignature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");
            var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            if (string.IsNullOrEmpty(hyprlandSignature) || string.IsNullOrEmpty(xdgRuntimeDir))
            {
                Log.Write("HyprlandAppProfiler", "Missing required environment variables for Hyprland IPC.", LogLevel.Error);
                return;
            }

            var socketPath = Path.Combine(xdgRuntimeDir, "hypr", hyprlandSignature, ".socket2.sock");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);

                    using var stream = new NetworkStream(socket);
                    using var reader = new StreamReader(stream, Encoding.UTF8);

                    Log.Write("HyprlandAppProfiler", "Connected to Hyprland IPC socket.", LogLevel.Debug);

                    while (!token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;

                        if (line.StartsWith("activewindow>>"))
                        {
                            var data = line.Substring("activewindow>>".Length);
                            var splitIndex = data.IndexOf(',');

                            string windowClass = string.Empty;
                            string windowTitle = string.Empty;

                            if (splitIndex >= 0)
                            {
                                windowClass = data.Substring(0, splitIndex);
                                windowTitle = data.Substring(splitIndex + 1);
                            }
                            else
                            {
                                windowClass = data;
                            }

                            ActiveWindowChanged?.Invoke(this, new ActiveWindowChangedEventArgs(windowClass, windowTitle));
                        }
                        else if (line.StartsWith("monitoradded>>") || line.StartsWith("monitorremoved>>"))
                        {
                            MonitorsChanged?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Write("HyprlandAppProfiler", $"Hyprland IPC connection error: {ex.Message}. Retrying in 5s...", LogLevel.Error);
                    try
                    {
                        await Task.Delay(5000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        public static OpenTabletDriver.Desktop.Profiles.AreaSettings? GetVirtualScreenArea()
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo("hyprctl")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                process.StartInfo.ArgumentList.Add("monitors");
                process.StartInfo.ArgumentList.Add("-j");

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var monitors = Newtonsoft.Json.Linq.JArray.Parse(output);
                    
                    float minX = float.MaxValue;
                    float minY = float.MaxValue;
                    float maxX = float.MinValue;
                    float maxY = float.MinValue;
                    bool found = false;

                    foreach (var m in monitors)
                    {
                        if (m.Value<bool?>("disabled") == true)
                            continue;

                        var x = m.Value<float?>("x") ?? 0;
                        var y = m.Value<float?>("y") ?? 0;
                        var width = m.Value<float?>("width") ?? 0;
                        var height = m.Value<float?>("height") ?? 0;
                        var scale = m.Value<float?>("scale") ?? 1.0f;
                        if (scale <= 0) scale = 1.0f;
                        var transform = m.Value<int?>("transform") ?? 0;

                        var isRotated = transform % 2 != 0;
                        var actualWidth = isRotated ? height : width;
                        var actualHeight = isRotated ? width : height;

                        var logicalWidth = actualWidth / scale;
                        var logicalHeight = actualHeight / scale;

                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x + logicalWidth);
                        maxY = Math.Max(maxY, y + logicalHeight);
                        found = true;
                    }

                    if (found)
                    {
                        var totalWidth = maxX - minX;
                        var totalHeight = maxY - minY;
                        // Normalize to (0,0) origin matching VirtualScreen/evdev
                        return new OpenTabletDriver.Desktop.Profiles.AreaSettings
                        {
                            Width = totalWidth,
                            Height = totalHeight,
                            X = totalWidth / 2,
                            Y = totalHeight / 2,
                            Rotation = 0
                        };
                    }
                }
            }
            catch {}
            return null;
        }

        public static System.Collections.Generic.List<OpenTabletDriver.Desktop.Profiles.AreaSettings> GetMonitors()
        {
            var result = new System.Collections.Generic.List<OpenTabletDriver.Desktop.Profiles.AreaSettings>();
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo("hyprctl")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                process.StartInfo.ArgumentList.Add("monitors");
                process.StartInfo.ArgumentList.Add("-j");

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var monitors = Newtonsoft.Json.Linq.JArray.Parse(output);

                    // First pass: collect raw monitor data
                    var rawMonitors = new System.Collections.Generic.List<(float x, float y, float w, float h)>();

                    foreach (var m in monitors)
                    {
                        if (m.Value<bool?>("disabled") == true)
                            continue;

                        var x = m.Value<float?>("x") ?? 0;
                        var y = m.Value<float?>("y") ?? 0;
                        var width = m.Value<float?>("width") ?? 0;
                        var height = m.Value<float?>("height") ?? 0;
                        var scale = m.Value<float?>("scale") ?? 1.0f;
                        if (scale <= 0) scale = 1.0f;
                        var transform = m.Value<int?>("transform") ?? 0;

                        var isRotated = transform % 2 != 0;
                        var actualWidth = isRotated ? height : width;
                        var actualHeight = isRotated ? width : height;

                        var logicalWidth = actualWidth / scale;
                        var logicalHeight = actualHeight / scale;

                        rawMonitors.Add((x, y, logicalWidth, logicalHeight));
                    }

                    // Compute origin offset to normalize to (0,0)
                    float originX = 0, originY = 0;
                    if (rawMonitors.Count > 0)
                    {
                        originX = rawMonitors.Min(m => m.x);
                        originY = rawMonitors.Min(m => m.y);
                    }

                    foreach (var (x, y, w, h) in rawMonitors)
                    {
                        result.Add(new OpenTabletDriver.Desktop.Profiles.AreaSettings
                        {
                            Width = w,
                            Height = h,
                            X = (x - originX) + w / 2,
                            Y = (y - originY) + h / 2,
                            Rotation = 0
                        });
                    }
                }
            }
            catch {}
            return result;
        }
    }
}
