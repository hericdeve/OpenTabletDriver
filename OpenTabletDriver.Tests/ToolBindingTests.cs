using System;
using System.Collections.Generic;
using System.Numerics;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Tools;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class ToolBindingTests
    {
        private class MockAppContext : IActiveAppContext
        {
            public string? CurrentWindowClass { get; set; }
            public string? CurrentWindowTitle { get; set; }
        }

        private class MockVirtualKeyboard : IVirtualKeyboard
        {
            public List<string> PressedKeys { get; } = new();
            public List<string> ReleasedKeys { get; } = new();

            public IEnumerable<string> SupportedKeys => new[]
            {
                "A", "B", "E", "H", "P", "R", "S", "T", "V", "Z", "Space", "Alt", "Control", "Shift"
            };

            public void Press(string key) => PressedKeys.Add(key);
            public void Release(string key) => ReleasedKeys.Add(key);

            public void Press(IEnumerable<string> keys) => PressedKeys.AddRange(keys);
            public void Release(IEnumerable<string> keys) => ReleasedKeys.AddRange(keys);
        }

        private static TabletReference CreateDummyTablet()
        {
            return new TabletReference
            {
                Properties = new TabletConfiguration
                {
                    Name = "Test Tablet",
                    DigitizerIdentifiers = [],
                    Specifications = new TabletSpecifications
                    {
                        Pen = new PenSpecifications
                        {
                            ButtonCount = 2,
                            MaxPressure = 8192
                        },
                        Digitizer = new DigitizerSpecifications
                        {
                            Width = 1000,
                            Height = 1000,
                            MaxX = 1000,
                            MaxY = 1000
                        }
                    }
                }
            };
        }

        [Fact]
        public void ToolBinding_ResolvesDefaultKey_WhenWindowClassUnmapped()
        {
            var keyboard = new MockVirtualKeyboard();
            var appContext = new MockAppContext { CurrentWindowClass = "some_random_app" };
            var config = ContextualToolsConfiguration.GetDefaultTools();

            var binding = new ToolBinding
            {
                Keyboard = keyboard,
                AppContext = appContext,
                CustomConfiguration = new ContextualToolsConfiguration { Tools = config },
                Tool = "Eraser",
                Mode = "Hold"
            };

            var tablet = CreateDummyTablet();
            binding.Press(tablet, null!);

            // Eraser default is "E"
            Assert.Contains("E", keyboard.PressedKeys);
            Assert.Empty(keyboard.ReleasedKeys);

            binding.Release(tablet, null!);
            Assert.Contains("E", keyboard.ReleasedKeys);
        }

        [Fact]
        public void ToolBinding_ResolvesAppOverride_OnExactOrSubstringMatch()
        {
            var keyboard = new MockVirtualKeyboard();
            var appContext = new MockAppContext { CurrentWindowClass = "com.github.xournalpp.xournalpp" };
            var config = ContextualToolsConfiguration.GetDefaultTools();

            var binding = new ToolBinding
            {
                Keyboard = keyboard,
                AppContext = appContext,
                CustomConfiguration = new ContextualToolsConfiguration { Tools = config },
                Tool = "Eraser",
                Mode = "Hold"
            };

            var tablet = CreateDummyTablet();
            binding.Press(tablet, null!);

            // xournalpp override is "Shift+Control+E"
            Assert.Contains("Shift", keyboard.PressedKeys);
            Assert.Contains("Control", keyboard.PressedKeys);
            Assert.Contains("E", keyboard.PressedKeys);

            binding.Release(tablet, null!);
            Assert.Contains("Shift", keyboard.ReleasedKeys);
            Assert.Contains("Control", keyboard.ReleasedKeys);
            Assert.Contains("E", keyboard.ReleasedKeys);
        }

        [Fact]
        public void ToolBinding_TapMode_PressesAndReleasesImmediatelyOnPress()
        {
            var keyboard = new MockVirtualKeyboard();
            var appContext = new MockAppContext { CurrentWindowClass = "obsidian" };
            var config = ContextualToolsConfiguration.GetDefaultTools();

            var binding = new ToolBinding
            {
                Keyboard = keyboard,
                AppContext = appContext,
                CustomConfiguration = new ContextualToolsConfiguration { Tools = config },
                Tool = "Selection", // Obsidian override is "V"
                Mode = "Tap / Toggle"
            };

            var tablet = CreateDummyTablet();
            binding.Press(tablet, null!);

            Assert.Contains("V", keyboard.PressedKeys);
            Assert.Contains("V", keyboard.ReleasedKeys);
        }

        [Fact]
        public void ToolBinding_CustomTool_WorksAcrossApps()
        {
            var keyboard = new MockVirtualKeyboard();
            var appContext = new MockAppContext { CurrentWindowClass = "xournalpp" };
            var customConfig = new ContextualToolsConfiguration
            {
                Tools = new List<ToolDefinition>
                {
                    new()
                    {
                        Id = "highlighter",
                        Name = "Highlighter",
                        DefaultBinding = "H",
                        AppOverrides = new List<ToolAppOverride>
                        {
                            new("xournalpp", "Shift+Control+H")
                        }
                    }
                }
            };

            var binding = new ToolBinding
            {
                Keyboard = keyboard,
                AppContext = appContext,
                CustomConfiguration = customConfig,
                Tool = "Highlighter",
                Mode = "Hold"
            };

            var tablet = CreateDummyTablet();
            binding.Press(tablet, null!);

            Assert.Contains("Shift", keyboard.PressedKeys);
            Assert.Contains("Control", keyboard.PressedKeys);
            Assert.Contains("H", keyboard.PressedKeys);
        }

        [Fact]
        public void ContextualToolsConfiguration_Serialization_DoesNotDuplicateTools()
        {
            var config = ContextualToolsConfiguration.GetDefaults();
            int initialCount = config.Tools.Count;

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(config);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<ContextualToolsConfiguration>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(initialCount, deserialized.Tools.Count);
        }

        [Fact]
        public void ContextualToolsConfiguration_Deduplicate_RemovesDuplicateToolsAndOverrides()
        {
            var config = new ContextualToolsConfiguration
            {
                Tools = new List<ToolDefinition>
                {
                    new()
                    {
                        Id = "brush",
                        Name = "Brush",
                        AppOverrides = new List<ToolAppOverride>
                        {
                            new("krita", "B"),
                            new("krita", "B")
                        }
                    },
                    new()
                    {
                        Id = "brush",
                        Name = "Brush Duplicate"
                    },
                    new()
                    {
                        Id = "eraser",
                        Name = "Eraser"
                    }
                }
            };

            config.Deduplicate();

            Assert.Equal(2, config.Tools.Count);
            Assert.Equal("brush", config.Tools[0].Id);
            Assert.Single(config.Tools[0].AppOverrides);
            Assert.Equal("eraser", config.Tools[1].Id);
        }
    }
}
