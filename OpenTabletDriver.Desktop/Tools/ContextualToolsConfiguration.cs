using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace OpenTabletDriver.Desktop.Tools
{
    public class ContextualToolsConfiguration
    {
        [JsonProperty("Tools", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<ToolDefinition> Tools { get; set; } = new();

        public static ContextualToolsConfiguration GetDefaults()
        {
            return new ContextualToolsConfiguration
            {
                Tools = GetDefaultTools()
            };
        }

        public void Deduplicate()
        {
            if (Tools == null || Tools.Count == 0)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<ToolDefinition>();
            foreach (var tool in Tools)
            {
                tool.DeduplicateOverrides();
                var key = !string.IsNullOrWhiteSpace(tool.Id) ? tool.Id : tool.Name;
                if (!string.IsNullOrWhiteSpace(key) && seen.Add(key))
                {
                    unique.Add(tool);
                }
            }
            Tools = unique;
        }

        public ToolDefinition? FindTool(string? toolIdOrName)
        {
            if (string.IsNullOrWhiteSpace(toolIdOrName))
                return null;

            return Tools.FirstOrDefault(t =>
                string.Equals(t.Id, toolIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, toolIdOrName, StringComparison.OrdinalIgnoreCase));
        }

        public static List<ToolDefinition> GetDefaultTools()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Id = "brush",
                    Name = "Brush / Pen",
                    IsBuiltIn = true,
                    DefaultBinding = "B",
                    AppOverrides = new List<ToolAppOverride>
                    {
                        new("xournalpp", "P"),
                        new("obsidian", "P"),
                        new("krita", "B"),
                        new("gimp", "P"),
                        new("inkscape", "P")
                    }
                },
                new ToolDefinition
                {
                    Id = "eraser",
                    Name = "Eraser",
                    IsBuiltIn = true,
                    DefaultBinding = "E",
                    AppOverrides = new List<ToolAppOverride>
                    {
                        new("xournalpp", "Shift+Control+E"),
                        new("obsidian", "Control+Shift+E"),
                        new("krita", "E"),
                        new("gimp", "Shift+E"),
                        new("inkscape", "Shift+E")
                    }
                },
                new ToolDefinition
                {
                    Id = "selection",
                    Name = "Selection",
                    IsBuiltIn = true,
                    DefaultBinding = "S",
                    AppOverrides = new List<ToolAppOverride>
                    {
                        new("xournalpp", "Shift+Control+R"),
                        new("obsidian", "V"),
                        new("krita", "T"),
                        new("gimp", "R"),
                        new("inkscape", "S")
                    }
                },
                new ToolDefinition
                {
                    Id = "hand",
                    Name = "Hand / Pan",
                    IsBuiltIn = true,
                    DefaultBinding = "Space",
                    AppOverrides = new List<ToolAppOverride>
                    {
                        new("xournalpp", "Shift+Control+H"),
                        new("obsidian", "H"),
                        new("krita", "Space"),
                        new("gimp", "Space")
                    }
                },
                new ToolDefinition
                {
                    Id = "color_picker",
                    Name = "Color Picker",
                    IsBuiltIn = true,
                    DefaultBinding = "Alt",
                    AppOverrides = new List<ToolAppOverride>
                    {
                        new("xournalpp", "Shift+Control+C"),
                        new("krita", "Control"),
                        new("gimp", "O"),
                        new("inkscape", "D")
                    }
                },
                new ToolDefinition
                {
                    Id = "undo",
                    Name = "Undo",
                    IsBuiltIn = true,
                    DefaultBinding = "Control+Z",
                    AppOverrides = new List<ToolAppOverride>()
                },
                new ToolDefinition
                {
                    Id = "redo",
                    Name = "Redo",
                    IsBuiltIn = true,
                    DefaultBinding = "Control+Y",
                    AppOverrides = new List<ToolAppOverride>
                    {
                        new("xournalpp", "Control+Shift+Z"),
                        new("obsidian", "Control+Shift+Z"),
                        new("krita", "Control+Shift+Z")
                    }
                },
                new ToolDefinition
                {
                    Id = "zoom",
                    Name = "Zoom",
                    IsBuiltIn = true,
                    DefaultBinding = "Z",
                    AppOverrides = new List<ToolAppOverride>()
                }
            };
        }
    }

    public class ToolDefinition
    {
        [JsonProperty("Id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        [JsonProperty("Name")]
        public string Name { get; set; } = "New Tool";

        [JsonProperty("IsBuiltIn")]
        public bool IsBuiltIn { get; set; }

        [JsonProperty("DefaultBinding")]
        public string DefaultBinding { get; set; } = string.Empty;

        [JsonProperty("AppOverrides", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<ToolAppOverride> AppOverrides { get; set; } = new();

        public void DeduplicateOverrides()
        {
            if (AppOverrides == null || AppOverrides.Count == 0)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<ToolAppOverride>();
            foreach (var o in AppOverrides)
            {
                if (!string.IsNullOrWhiteSpace(o.WindowClass) && seen.Add(o.WindowClass))
                {
                    unique.Add(o);
                }
            }
            AppOverrides = unique;
        }

        public string ResolveKeySequence(string? windowClass)
        {
            if (!string.IsNullOrWhiteSpace(windowClass))
            {
                // Exact match (case-insensitive)
                var exact = AppOverrides.FirstOrDefault(o =>
                    string.Equals(o.WindowClass, windowClass, StringComparison.OrdinalIgnoreCase));
                if (exact != null && !string.IsNullOrWhiteSpace(exact.KeySequence))
                    return exact.KeySequence;

                // Substring / contains match (e.g. window class "com.github.xournalpp.xournalpp" contains "xournalpp")
                var partial = AppOverrides.FirstOrDefault(o =>
                    !string.IsNullOrWhiteSpace(o.WindowClass) &&
                    (windowClass.Contains(o.WindowClass, StringComparison.OrdinalIgnoreCase) ||
                     o.WindowClass.Contains(windowClass, StringComparison.OrdinalIgnoreCase)));
                if (partial != null && !string.IsNullOrWhiteSpace(partial.KeySequence))
                    return partial.KeySequence;
            }

            return DefaultBinding;
        }

        public override string ToString() => Name;
    }

    public class ToolAppOverride
    {
        public ToolAppOverride() { }

        public ToolAppOverride(string windowClass, string keySequence)
        {
            WindowClass = windowClass;
            KeySequence = keySequence;
        }

        [JsonProperty("WindowClass")]
        public string WindowClass { get; set; } = string.Empty;

        [JsonProperty("KeySequence")]
        public string KeySequence { get; set; } = string.Empty;
    }
}
