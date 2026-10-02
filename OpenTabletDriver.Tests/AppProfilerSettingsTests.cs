using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.AppProfiler;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class AppProfilerSettingsTests
    {
        [Fact]
        public void AppProfilerSettings_MigratesLegacyDictionaries_Correctly()
        {
            var legacyJson = @"{
                ""enableAppProfiler"": true,
                ""syncFocus"": true,
                ""defaultAppProfile"": ""Drawing"",
                ""appProfiles"": {
                    ""krita"": ""Krita-Preset"",
                    ""obsidian"": ""Note-Taking""
                },
                ""appOutputModes"": {
                    ""krita"": ""OpenTabletDriver.Desktop.Output.AbsoluteMode"",
                    ""osu"": ""OpenTabletDriver.Desktop.Output.RelativeMode""
                },
                ""trackedNamespaces"": [ ""noctalia-overview"" ],
                ""namespaceProfiles"": {
                    ""noctalia-overview"": ""Overview-Preset""
                }
            }";

            var settings = JsonConvert.DeserializeObject<AppProfilerSettings>(legacyJson);
            Assert.NotNull(settings);
            settings.MigrateLegacySettings();

            Assert.True(settings.EnableAppProfiler);
            Assert.True(settings.SyncFocus);
            Assert.Equal("Drawing", settings.DefaultAppProfile);

            // Should have migrated: krita, obsidian, osu (window classes) + noctalia-overview (layer) = 4 rules
            Assert.Equal(4, settings.Rules.Count);

            var kritaRule = settings.Rules.Find(r => r.Pattern == "krita");
            Assert.NotNull(kritaRule);
            Assert.Equal(RuleTargetType.WindowClass, kritaRule.TargetType);
            Assert.Equal("Krita-Preset", kritaRule.PresetName);
            Assert.Equal("OpenTabletDriver.Desktop.Output.AbsoluteMode", kritaRule.OutputMode);
            Assert.Equal(RuleDisplayMapping.FollowFocus, kritaRule.DisplayMapping);

            var osuRule = settings.Rules.Find(r => r.Pattern == "osu");
            Assert.NotNull(osuRule);
            Assert.Equal("OpenTabletDriver.Desktop.Output.RelativeMode", osuRule.OutputMode);
            Assert.Null(osuRule.PresetName);

            var layerRule = settings.Rules.Find(r => r.Pattern == "noctalia-overview");
            Assert.NotNull(layerRule);
            Assert.Equal(RuleTargetType.LayerNamespace, layerRule.TargetType);
            Assert.Equal("Overview-Preset", layerRule.PresetName);
        }

        [Fact]
        public void AppProfilerSettings_RulesSerialization_PreservesRulesWithoutDuplicates()
        {
            var settings = new AppProfilerSettings
            {
                EnableAppProfiler = true,
                Rules = new List<AppProfileRule>
                {
                    new AppProfileRule
                    {
                        Name = "Krita Painting",
                        Pattern = ".*krita.*",
                        TargetType = RuleTargetType.WindowClass,
                        MatchType = RuleMatchType.Regex,
                        PresetName = "ArtPreset",
                        DisplayMapping = RuleDisplayMapping.SpecificMonitor,
                        TargetMonitor = "DP-2"
                    }
                }
            };

            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            var deserialized = JsonConvert.DeserializeObject<AppProfilerSettings>(json);

            Assert.NotNull(deserialized);
            deserialized.MigrateLegacySettings(); // should be a no-op since Rules.Count == 1

            Assert.Single(deserialized.Rules);
            var rule = deserialized.Rules[0];
            Assert.Equal("Krita Painting", rule.Name);
            Assert.Equal(".*krita.*", rule.Pattern);
            Assert.Equal(RuleTargetType.WindowClass, rule.TargetType);
            Assert.Equal(RuleMatchType.Regex, rule.MatchType);
            Assert.Equal("ArtPreset", rule.PresetName);
            Assert.Equal(RuleDisplayMapping.SpecificMonitor, rule.DisplayMapping);
            Assert.Equal("DP-2", rule.TargetMonitor);
        }

        [Fact]
        public void AppProfilerSettings_SyncLegacyProperties_PopulatesExactWindowRules()
        {
            var settings = new AppProfilerSettings
            {
                Rules = new List<AppProfileRule>
                {
                    new AppProfileRule
                    {
                        Name = "Exact App",
                        Pattern = "xournalpp",
                        TargetType = RuleTargetType.WindowClass,
                        MatchType = RuleMatchType.Exact,
                        PresetName = "NotesPreset",
                        OutputMode = "AbsoluteMode"
                    },
                    new AppProfileRule
                    {
                        Name = "Layer",
                        Pattern = "rofi",
                        TargetType = RuleTargetType.LayerNamespace,
                        MatchType = RuleMatchType.Exact,
                        PresetName = "RofiPreset"
                    }
                }
            };

            settings.SyncLegacyProperties();

            Assert.Equal("NotesPreset", settings.AppProfiles["xournalpp"]);
            Assert.Equal("AbsoluteMode", settings.AppOutputModes["xournalpp"]);
            Assert.Contains("rofi", settings.TrackedNamespaces);
            Assert.Equal("RofiPreset", settings.NamespaceProfiles["rofi"]);
        }

        [Fact]
        public void AppProfileRule_Clone_CreatesDeepCopy()
        {
            var rule = new AppProfileRule
            {
                Name = "Original",
                Pattern = "test",
                TargetType = RuleTargetType.WindowClass,
                MatchType = RuleMatchType.Contains,
                PresetName = "TestPreset",
                OutputMode = "TestMode",
                DisplayMapping = RuleDisplayMapping.SpecificMonitor,
                TargetMonitor = "DP-1"
            };

            var clone = rule.Clone();

            Assert.NotEqual(rule.Id, clone.Id);
            Assert.Equal(rule.Name, clone.Name);
            Assert.Equal(rule.Pattern, clone.Pattern);
            Assert.Equal(rule.TargetType, clone.TargetType);
            Assert.Equal(rule.MatchType, clone.MatchType);
            Assert.Equal(rule.PresetName, clone.PresetName);
            Assert.Equal(rule.OutputMode, clone.OutputMode);
            Assert.Equal(rule.DisplayMapping, clone.DisplayMapping);
            Assert.Equal(rule.TargetMonitor, clone.TargetMonitor);
        }
    }
}
