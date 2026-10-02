using System;
using Newtonsoft.Json;

#nullable enable

namespace OpenTabletDriver.Desktop.AppProfiler
{
    public class AppProfileRule
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("targetType")]
        public RuleTargetType TargetType { get; set; } = RuleTargetType.WindowClass;

        [JsonProperty("matchType")]
        public RuleMatchType MatchType { get; set; } = RuleMatchType.Exact;

        [JsonProperty("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonProperty("presetName")]
        public string? PresetName { get; set; }

        [JsonProperty("outputMode")]
        public string? OutputMode { get; set; }

        [JsonProperty("displayMapping")]
        public RuleDisplayMapping DisplayMapping { get; set; } = RuleDisplayMapping.Inherit;

        [JsonProperty("targetMonitor")]
        public string? TargetMonitor { get; set; }

        public AppProfileRule Clone()
        {
            return new AppProfileRule
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name,
                Enabled = Enabled,
                TargetType = TargetType,
                MatchType = MatchType,
                Pattern = Pattern,
                PresetName = PresetName,
                OutputMode = OutputMode,
                DisplayMapping = DisplayMapping,
                TargetMonitor = TargetMonitor
            };
        }
    }
}
