using System;
using Gtk;
using OpenTabletDriver.Plugin;

#nullable enable

namespace OpenTabletDriver.UX.Gtk.Theme
{
    public static class GtkThemeManager
    {
        private const string ModernTabsCss = @"
/* =======================================================
   Native-Consistent Segmented Tabs for OpenTabletDriver (GTK3)
   ======================================================= */

notebook {
    background-color: transparent;
    border: none;
    padding: 0;
}

/* Tab header container: completely seamless with window background */
notebook > header,
notebook > header.top {
    background-color: transparent;
    background-image: none;
    border: none;
    padding: 6px 10px 8px 10px;
}

/* Base surface for all tabs (matching native GTK button tokens) */
notebook tab,
notebook > header tab,
notebook > header > tabs > tab {
    min-height: 24px;
    min-width: 16px;
    padding: 5px 12px;
    margin: 2px 3px;
    border: 1px solid transparent;
    border-radius: 9px;
    outline: 0 solid transparent;
    background-image: none;
    background-color: mix(@window_fg_color, @window_bg_color, 0.9);
    color: @window_fg_color;
    font-weight: bold;
    box-shadow: none;
    text-shadow: none;
    -gtk-icon-shadow: none;
    transition: background 200ms cubic-bezier(0.25, 0.46, 0.45, 0.94),
                box-shadow 200ms cubic-bezier(0.25, 0.46, 0.45, 0.94);
}

/* Hover state (matching native GTK button:hover) */
notebook tab:hover,
notebook > header tab:hover,
notebook > header > tabs > tab:hover {
    background-color: mix(@window_fg_color, @window_bg_color, 0.85);
    color: @window_fg_color;
    box-shadow: none;
}

/* Active / Checked state (matching native GTK button:checked / stackswitcher) */
notebook tab:checked,
notebook > header tab:checked,
notebook tab:active,
notebook > header tab:active,
notebook > header > tabs > tab:checked,
notebook > header > tabs > tab:active {
    background-color: mix(@window_fg_color, @window_bg_color, 0.7);
    color: @window_fg_color;
    box-shadow: none;
}

/* Active + Hover state */
notebook tab:checked:hover,
notebook > header tab:checked:hover,
notebook tab:active:hover,
notebook > header tab:active:hover,
notebook > header > tabs > tab:checked:hover,
notebook > header > tabs > tab:active:hover {
    background-color: mix(@window_fg_color, @window_bg_color, 0.65);
    color: @window_fg_color;
    box-shadow: none;
}

notebook tab:focus,
notebook > header tab:focus {
    outline: none;
}

notebook tab label,
notebook > header tab label {
    color: inherit;
    font-weight: inherit;
    padding: 0;
}

notebook > stack {
    background-color: transparent;
    border: none;
    padding: 0;
}

/* Scroll arrows if tabs overflow window width (matching native GTK button) */
notebook > header > tabs > arrow,
notebook > header button {
    min-height: 24px;
    min-width: 16px;
    padding: 4px 8px;
    margin: 2px;
    border: 1px solid transparent;
    border-radius: 9px;
    background-color: mix(@window_fg_color, @window_bg_color, 0.9);
    color: @window_fg_color;
    box-shadow: none;
    transition: background 200ms cubic-bezier(0.25, 0.46, 0.45, 0.94);
}

notebook > header > tabs > arrow:hover,
notebook > header button:hover {
    background-color: mix(@window_fg_color, @window_bg_color, 0.85);
    color: @window_fg_color;
}
";

        private static CssProvider? _cssProvider;

        public static void Initialize()
        {
            try
            {
                var screen = Gdk.Screen.Default;
                if (screen == null)
                    return;

                _cssProvider = new CssProvider();
                _cssProvider.LoadFromData(ModernTabsCss);

                // Priority User (800) ensures custom styles take precedence over generic desktop themes
                StyleContext.AddProviderForScreen(
                    screen,
                    _cssProvider,
                    (uint)StyleProviderPriority.User
                );

                Log.Debug("ThemeManager", "Modern segmented pill tab styling loaded successfully.");
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }
    }
}
