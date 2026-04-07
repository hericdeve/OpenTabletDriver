using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;

#nullable enable

namespace OpenTabletDriver.Desktop.Interop.AppProfiler
{
    public class HyprlandWindowProvider : IActiveWindowProvider
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _readTask;

        public event EventHandler<ActiveWindowChangedEventArgs>? ActiveWindowChanged;

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
    }
}
