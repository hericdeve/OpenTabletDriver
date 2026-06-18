using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenTabletDriver.Plugin;

#nullable enable

namespace OpenTabletDriver.Desktop.Interop.AppProfiler
{
    public class HyprlandIpcService
    {
        private readonly string _socketPath;

        public HyprlandIpcService()
        {
            var hyprlandSignature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");
            var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            if (!string.IsNullOrEmpty(hyprlandSignature) && !string.IsNullOrEmpty(xdgRuntimeDir))
            {
                _socketPath = Path.Combine(xdgRuntimeDir, "hypr", hyprlandSignature, ".socket.sock");
            }
            else
            {
                _socketPath = string.Empty;
            }
        }

        public string SendCommand(string command)
        {
            if (string.IsNullOrEmpty(_socketPath) || !File.Exists(_socketPath))
                return string.Empty;

            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(_socketPath));

                var commandBytes = Encoding.UTF8.GetBytes(command);
                socket.Send(commandBytes);

                using var stream = new NetworkStream(socket);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandIpcService", $"IPC command '{command}' failed: {ex.Message}", LogLevel.Error);
                return string.Empty;
            }
        }

        public (int x, int y)? GetCursorPosition()
        {
            var response = SendCommand("j/cursorpos");
            if (string.IsNullOrEmpty(response))
                return null;

            // Zero-allocation span parsing for {"x":INT,"y":INT}
            var span = response.AsSpan();
            
            int xIndex = span.IndexOf("\"x\":");
            int yIndex = span.IndexOf("\"y\":");

            if (xIndex == -1 || yIndex == -1)
                return null;

            xIndex += 4;
            yIndex += 4;

            int commaIndex = span.Slice(xIndex).IndexOf(',');
            if (commaIndex == -1) return null;

            var xSpan = span.Slice(xIndex, commaIndex).Trim();

            int braceIndex = span.Slice(yIndex).IndexOf('}');
            if (braceIndex == -1) return null;

            var ySpan = span.Slice(yIndex, braceIndex).Trim();

            if (int.TryParse(xSpan, out int x) && int.TryParse(ySpan, out int y))
            {
                return (x, y);
            }

            return null;
        }

        public List<TrackedLayer> GetTrackedLayers(IEnumerable<string> targetNamespaces)
        {
            var layers = new List<TrackedLayer>();
            var response = SendCommand("j/layers");

            if (string.IsNullOrEmpty(response))
                return layers;

            try
            {
                using var doc = JsonDocument.Parse(response);
                var targets = new HashSet<string>(targetNamespaces);

                foreach (var monitorProperty in doc.RootElement.EnumerateObject())
                {
                    if (monitorProperty.Value.TryGetProperty("levels", out var levelsObj))
                    {
                        foreach (var levelProperty in levelsObj.EnumerateObject())
                        {
                            foreach (var layerElement in levelProperty.Value.EnumerateArray())
                            {
                                if (layerElement.TryGetProperty("namespace", out var nsElement))
                                {
                                    var ns = nsElement.GetString();
                                    if (ns != null && targets.Contains(ns))
                                    {
                                        layers.Add(new TrackedLayer
                                        {
                                            Namespace = ns,
                                            X = layerElement.GetProperty("x").GetInt32(),
                                            Y = layerElement.GetProperty("y").GetInt32(),
                                            Width = layerElement.GetProperty("w").GetInt32(),
                                            Height = layerElement.GetProperty("h").GetInt32()
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("HyprlandIpcService", $"Failed to parse j/layers: {ex.Message}", LogLevel.Error);
            }

            return layers;
        }
        
        public string GetActiveWindow()
        {
            return SendCommand("activewindow");
        }
    }
}
