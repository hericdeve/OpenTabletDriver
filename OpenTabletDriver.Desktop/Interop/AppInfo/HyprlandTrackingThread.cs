using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;

#nullable enable

namespace OpenTabletDriver.Desktop.Interop.AppProfiler
{
    public class HyprlandTrackingThread : IDisposable
    {
        private readonly HyprlandIpcService _ipcService;
        private readonly IEnumerable<string> _targetNamespaces;
        private readonly string _socket2Path;
        private List<TrackedLayer> _cachedLayers = new List<TrackedLayer>();
        private readonly object _layerLock = new object();
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _eventTask;
        private Task? _pollingTask;
        private bool _isHoveringLayer = false;
        private string? _hoveredNamespace = null;

        public class LayerHoverStateChangedEventArgs : EventArgs
        {
            public bool IsHovering { get; }
            public string? Namespace { get; }

            public LayerHoverStateChangedEventArgs(bool isHovering, string? ns)
            {
                IsHovering = isHovering;
                Namespace = ns;
            }
        }

        public event EventHandler<LayerHoverStateChangedEventArgs>? LayerHoverStateChanged;

        public HyprlandTrackingThread(IEnumerable<string> targetNamespaces)
        {
            _targetNamespaces = targetNamespaces.ToList();
            _ipcService = new HyprlandIpcService();

            var hyprlandSignature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");
            var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            if (!string.IsNullOrEmpty(hyprlandSignature) && !string.IsNullOrEmpty(xdgRuntimeDir))
            {
                _socket2Path = Path.Combine(xdgRuntimeDir, "hypr", hyprlandSignature, ".socket2.sock");
            }
            else
            {
                _socket2Path = string.Empty;
            }
        }

        public void Start()
        {
            if (string.IsNullOrEmpty(_socket2Path))
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            RefreshLayerCache();

            _eventTask = Task.Run(() => MonitorEventsAsync(token), token);
            _pollingTask = Task.Run(() => PollingLoopAsync(token), token);
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            try
            {
                if (_eventTask != null)
                    _eventTask.Wait();
                if (_pollingTask != null)
                    _pollingTask.Wait();
            }
            catch (AggregateException)
            {
                // Task cancelled
            }
        }

        private void RefreshLayerCache()
        {
            var newLayers = _ipcService.GetTrackedLayers(_targetNamespaces);
            lock (_layerLock)
            {
                _cachedLayers = newLayers;
            }
        }

        private async Task MonitorEventsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socket2Path), token);

                    using var stream = new NetworkStream(socket);
                    using var reader = new StreamReader(stream, Encoding.UTF8);

                    while (!token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;

                        if (line.StartsWith("openlayer>>") || line.StartsWith("closelayer>>"))
                        {
                            // Asynchronously trigger refresh
                            _ = Task.Run(() => RefreshLayerCache(), token);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("HyprlandTrackingThread", $"Event socket error: {ex.Message}", LogLevel.Error);
                    await Task.Delay(2000, token); // wait before reconnecting
                }
            }
        }

        private async Task PollingLoopAsync(CancellationToken token)
        {
            int sleepInterval = 200;

            while (!token.IsCancellationRequested)
            {
                var pos = _ipcService.GetCursorPosition();
                if (pos.HasValue)
                {
                    int cx = pos.Value.x;
                    int cy = pos.Value.y;

                    bool isInside = false;
                    bool isNear = false;
                    string? currentNamespace = null;
                    const int proximity = 150;

                    lock (_layerLock)
                    {
                        foreach (var layer in _cachedLayers)
                        {
                            if (layer.Contains(cx, cy))
                            {
                                isInside = true;
                                isNear = true;
                                currentNamespace = layer.Namespace;
                                break;
                            }
                            
                            // Check proximity
                            int distX = Math.Max(0, Math.Max(layer.X - cx, cx - (layer.X + layer.Width)));
                            int distY = Math.Max(0, Math.Max(layer.Y - cy, cy - (layer.Y + layer.Height)));
                            if (distX <= proximity && distY <= proximity)
                            {
                                isNear = true;
                            }
                        }
                    }

                    if (isInside != _isHoveringLayer || currentNamespace != _hoveredNamespace)
                    {
                        _isHoveringLayer = isInside;
                        _hoveredNamespace = currentNamespace;
                        LayerHoverStateChanged?.Invoke(this, new LayerHoverStateChangedEventArgs(_isHoveringLayer, _hoveredNamespace));
                    }

                    sleepInterval = isNear ? 25 : 200;
                }
                else
                {
                    sleepInterval = 200;
                }

                await Task.Delay(sleepInterval, token);
            }
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}
