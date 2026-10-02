using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Gdk;
using Gtk;

namespace OpenTabletDriver.UX.Gtk.Hud
{
    public static class AppIconCache
    {
        private static readonly ConcurrentDictionary<string, Pixbuf?> _cache = new();
        private static readonly Dictionary<string, string[]> _aliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "code-url-handler", new[] { "visual-studio-code", "code", "com.visualstudio.code" } },
            { "code", new[] { "visual-studio-code", "code", "com.visualstudio.code" } },
            { "code-oss", new[] { "code-oss", "code", "visual-studio-code" } },
            { "vscodium", new[] { "vscodium", "code" } },
            { "google-chrome", new[] { "google-chrome", "google-chrome-stable", "chromium-browser", "chromium" } },
            { "chrome", new[] { "google-chrome", "google-chrome-stable", "chromium" } },
            { "chromium", new[] { "chromium", "chromium-browser" } },
            { "firefox", new[] { "firefox", "firefox-esr", "firefox-bin" } },
            { "kitty", new[] { "kitty" } },
            { "alacritty", new[] { "alacritty", "Alacritty" } },
            { "foot", new[] { "foot", "footclient" } },
            { "wezterm-gui", new[] { "org.wezfurlong.wezterm", "wezterm" } },
            { "wezterm", new[] { "org.wezfurlong.wezterm", "wezterm" } },
            { "spotify", new[] { "spotify", "spotify-client", "com.spotify.Client" } },
            { "discord", new[] { "discord", "com.discordapp.Discord" } },
            { "slack", new[] { "slack", "com.slack.Slack" } },
            { "telegramdesktop", new[] { "telegram", "telegram-desktop", "org.telegram.desktop" } },
            { "obsidian", new[] { "obsidian", "md.obsidian.Obsidian" } },
            { "steam", new[] { "steam", "com.valvesoftware.Steam" } },
            { "steamwebhelper", new[] { "steam", "com.valvesoftware.Steam" } },
            { "thunar", new[] { "thunar", "system-file-manager" } },
            { "nautilus", new[] { "org.gnome.Nautilus", "nautilus", "system-file-manager" } },
            { "org.gnome.nautilus", new[] { "org.gnome.Nautilus", "nautilus", "system-file-manager" } },
            { "dolphin", new[] { "org.kde.dolphin", "dolphin", "system-file-manager" } },
            { "org.kde.dolphin", new[] { "org.kde.dolphin", "dolphin", "system-file-manager" } },
            { "xournalpp", new[] { "xournalpp", "com.github.xournalpp.xournalpp" } },
            { "gimp", new[] { "gimp", "org.gimp.GIMP" } },
            { "inkscape", new[] { "inkscape", "org.inkscape.Inkscape" } },
            { "blender", new[] { "blender", "org.blender.Blender" } },
            { "mpv", new[] { "mpv", "io.mpv.Mpv" } },
            { "vlc", new[] { "vlc", "org.videolan.VLC" } }
        };

        public static Pixbuf? GetIcon(string? appClass, int size)
        {
            if (string.IsNullOrWhiteSpace(appClass))
                return null;

            var trimmed = appClass.Trim();
            var cacheKey = $"{trimmed}_{size}";
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var pixbuf = ResolveIcon(trimmed, size);
            _cache[cacheKey] = pixbuf;
            return pixbuf;
        }

        private static Pixbuf? ResolveIcon(string rawClass, int size)
        {
            try
            {
                var iconTheme = IconTheme.Default;
                var candidates = BuildCandidates(rawClass);

                if (iconTheme != null)
                {
                    foreach (var candidate in candidates)
                    {
                        if (iconTheme.HasIcon(candidate))
                        {
                            try
                            {
                                var pb = iconTheme.LoadIcon(candidate, size, (IconLookupFlags)0);
                                if (pb != null)
                                    return pb;
                            }
                            catch
                            {
                                // Continue trying next candidate
                            }
                        }
                    }
                }

                // Fallback to checking /usr/share/pixmaps
                foreach (var candidate in candidates)
                {
                    var pixmapPath = $"/usr/share/pixmaps/{candidate}.png";
                    if (File.Exists(pixmapPath))
                    {
                        try
                        {
                            return new Pixbuf(pixmapPath, size, size);
                        }
                        catch
                        {
                        }
                    }

                    var pixmapSvg = $"/usr/share/pixmaps/{candidate}.svg";
                    if (File.Exists(pixmapSvg))
                    {
                        try
                        {
                            return new Pixbuf(pixmapSvg, size, size);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static List<string> BuildCandidates(string rawClass)
        {
            var list = new List<string>();
            var lower = rawClass.ToLowerInvariant();

            if (_aliasMap.TryGetValue(rawClass, out var aliases) || _aliasMap.TryGetValue(lower, out aliases))
            {
                list.AddRange(aliases);
            }

            list.Add(rawClass);
            if (!list.Contains(lower))
                list.Add(lower);

            // Strip reverse DNS e.g. org.kde.dolphin -> dolphin
            if (lower.Contains('.'))
            {
                var parts = lower.Split('.');
                var lastPart = parts[^1];
                if (!string.IsNullOrWhiteSpace(lastPart) && !list.Contains(lastPart))
                    list.Add(lastPart);
            }

            // Strip dash suffixes e.g. "libreoffice-writer" -> "libreoffice"
            if (lower.Contains('-'))
            {
                var prefix = lower.Substring(0, lower.IndexOf('-'));
                if (!string.IsNullOrWhiteSpace(prefix) && !list.Contains(prefix))
                    list.Add(prefix);
            }

            return list;
        }
    }
}
