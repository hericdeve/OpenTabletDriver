using Newtonsoft.Json;

#nullable enable

namespace OpenTabletDriver.Desktop.AppProfiler
{
    public class ActiveAppProfileContext
    {
        [JsonProperty("windowClass")]
        public string WindowClass { get; set; } = string.Empty;

        [JsonProperty("windowTitle")]
        public string WindowTitle { get; set; } = string.Empty;

        [JsonProperty("isHoveringLayer")]
        public bool IsHoveringLayer { get; set; }

        [JsonProperty("layerNamespace")]
        public string? LayerNamespace { get; set; }

        [JsonProperty("matchedRuleId")]
        public string? MatchedRuleId { get; set; }

        [JsonProperty("matchedRuleName")]
        public string? MatchedRuleName { get; set; }

        [JsonProperty("activePreset")]
        public string? ActivePreset { get; set; }

        [JsonProperty("activeOutputMode")]
        public string? ActiveOutputMode { get; set; }
    }
}
