using Newtonsoft.Json;

namespace OpenTabletDriver.Desktop.Compositor
{
    public class CompositorSettings
    {
        [JsonProperty("CompositorType")]
        public string CompositorType { get; set; } = "Auto";

        [JsonProperty("EnableCompositorIntegration")]
        public bool EnableCompositorIntegration { get; set; } = true;

        [JsonProperty("FollowFocusOnWindowMove")]
        public bool FollowFocusOnWindowMove { get; set; } = true;

        [JsonProperty("SpecialWorkspaceName")]
        public string SpecialWorkspaceName { get; set; } = "special:scratchpad";

        [JsonProperty("MaxHudWorkspaceSlots")]
        public int MaxHudWorkspaceSlots { get; set; } = 8;
    }
}
