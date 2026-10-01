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

        private class DummyTabletReport : IAbsolutePositionReport, ITabletReport
        {
            public Vector2 Position { get; set; }
            public uint Pressure { get; set; }
            public Vector2 Tilt { get; set; }
            public bool[] PenButtons { get; set; } = new bool[2];
            public byte[] Raw { get; set; } = Array.Empty<byte>();
        }

        [Fact]
        public void ToolBinding_HoldMode_WithLiftAction_TriggersOnButtonRelease()
        {
            var keyboard = new MockVirtualKeyboard();
            var appContext = new MockAppContext { CurrentWindowClass = "xournalpp" };
            var config = ContextualToolsConfiguration.GetDefaultTools();

            var binding = new ToolBinding
            {
                Keyboard = keyboard,
                AppContext = appContext,
                CustomConfiguration = new ContextualToolsConfiguration { Tools = config },
                Tool = "Eraser", // in xournalpp, override is Shift+Control+E
                Mode = "Hold",
                OnLiftAction = "Brush / Pen", // in xournalpp, override is P
                LiftTrigger = "Button Release"
            };

            var tablet = CreateDummyTablet();
            var report = new DummyTabletReport { Pressure = 2000 };

            // Press hold button
            binding.Press(tablet, report);
            Assert.Contains("Shift", keyboard.PressedKeys);
            Assert.Contains("Control", keyboard.PressedKeys);
            Assert.Contains("E", keyboard.PressedKeys);
            Assert.DoesNotContain("P", keyboard.PressedKeys);

            // Release button -> Eraser released, Pen evoked (pressed then released)
            binding.Release(tablet, report);
            Assert.Contains("E", keyboard.ReleasedKeys);
            Assert.Contains("P", keyboard.PressedKeys);
            Assert.Contains("P", keyboard.ReleasedKeys);
        }

        [Fact]
        public void ToolBinding_HoldMode_WithLiftAction_TriggersOnPenTipLift()
        {
            var keyboard = new MockVirtualKeyboard();
            var appContext = new MockAppContext { CurrentWindowClass = "xournalpp" };
            var config = ContextualToolsConfiguration.GetDefaultTools();

            var binding = new ToolBinding
            {
                Keyboard = keyboard,
                AppContext = appContext,
                CustomConfiguration = new ContextualToolsConfiguration { Tools = config },
                Tool = "Eraser",
                Mode = "Hold",
                OnLiftAction = "Brush / Pen",
                LiftTrigger = "Pen Tip Lift"
            };

            var tablet = CreateDummyTablet();
            var downReport = new DummyTabletReport { Pressure = 3000 };

            // Press while drawing (tip is down)
            binding.Press(tablet, downReport);
            Assert.Contains("E", keyboard.PressedKeys);
            Assert.DoesNotContain("P", keyboard.PressedKeys);

            // While button remains held, pen tip lifts off tablet (Pressure = 0)
            var upReport = new DummyTabletReport { Pressure = 0 };
            binding.Update(tablet, upReport);

            // Lift action fires immediately on pen lift: Eraser released, Pen evoked
            Assert.Contains("E", keyboard.ReleasedKeys);
            Assert.Contains("P", keyboard.PressedKeys);
            Assert.Contains("P", keyboard.ReleasedKeys);

            // Subsequent button release does not double-fire the lift action
            int pCount = keyboard.PressedKeys.FindAll(k => k == "P").Count;
            binding.Release(tablet, upReport);
            Assert.Equal(pCount, keyboard.PressedKeys.FindAll(k => k == "P").Count);
        }
    }
}
