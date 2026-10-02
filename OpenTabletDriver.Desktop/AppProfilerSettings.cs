using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.AppProfiler;

#nullable enable

namespace OpenTabletDriver.Desktop
{
    public class AppProfilerSettings
    {
        [JsonProperty("enableAppProfiler")]
        public bool EnableAppProfiler { get; set; } = false;

        [JsonProperty("syncFocus")]
        public bool SyncFocus { get; set; } = false;

        [JsonProperty("defaultAppProfile")]
        public string? DefaultAppProfile { get; set; }

        [JsonProperty("defaultOutputMode")]
        public string? DefaultOutputMode { get; set; }

        [JsonProperty("rules", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<AppProfileRule> Rules { get; set; } = new List<AppProfileRule>();

        [JsonProperty("appProfiles")]
        public Dictionary<string, string> AppProfiles { get; set; } = new Dictionary<string, string>();

        [JsonProperty("appOutputModes")]
        public Dictionary<string, string> AppOutputModes { get; set; } = new Dictionary<string, string>();

        [JsonProperty("trackedNamespaces")]
        public List<string> TrackedNamespaces { get; set; } = new List<string>();

        [JsonProperty("namespaceProfiles")]
        public Dictionary<string, string> NamespaceProfiles { get; set; } = new Dictionary<string, string>();

        [JsonProperty("namespaceOutputModes")]
        public Dictionary<string, string> NamespaceOutputModes { get; set; } = new Dictionary<string, string>();

        public void MigrateLegacySettings()
        {
            Rules ??= new List<AppProfileRule>();

            // If rules already exist, don't re-migrate legacy dictionaries
            if (Rules.Count > 0)
                return;

            // 1. Migrate App Window Classes
            var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AppProfiles != null) classes.UnionWith(AppProfiles.Keys);
            if (AppOutputModes != null) classes.UnionWith(AppOutputModes.Keys);

            foreach (var cls in classes.OrderBy(c => c))
            {
                string? preset = AppProfiles != null && AppProfiles.TryGetValue(cls, out var p) ? p : null;
                string? mode = AppOutputModes != null && AppOutputModes.TryGetValue(cls, out var m) ? m : null;

                Rules.Add(new AppProfileRule
                {
                    Name = cls,
                    TargetType = RuleTargetType.WindowClass,
                    MatchType = RuleMatchType.Exact,
                    Pattern = cls,
                    PresetName = preset,
                    OutputMode = mode,
                    DisplayMapping = SyncFocus ? RuleDisplayMapping.FollowFocus : RuleDisplayMapping.Inherit
                });
            }

            // 2. Migrate Wayland Layer Namespaces
            var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (TrackedNamespaces != null) namespaces.UnionWith(TrackedNamespaces);
            if (NamespaceProfiles != null) namespaces.UnionWith(NamespaceProfiles.Keys);
            if (NamespaceOutputModes != null) namespaces.UnionWith(NamespaceOutputModes.Keys);

            foreach (var ns in namespaces.OrderBy(n => n))
            {
                string? preset = NamespaceProfiles != null && NamespaceProfiles.TryGetValue(ns, out var p) ? p : null;
                string? mode = NamespaceOutputModes != null && NamespaceOutputModes.TryGetValue(ns, out var m) ? m : null;

                Rules.Add(new AppProfileRule
                {
                    Name = $"Layer: {ns}",
                    TargetType = RuleTargetType.LayerNamespace,
                    MatchType = RuleMatchType.Exact,
                    Pattern = ns,
                    PresetName = preset,
                    OutputMode = mode,
                    DisplayMapping = RuleDisplayMapping.Inherit
                });
            }
        }

        public void SyncLegacyProperties()
        {
            AppProfiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AppOutputModes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TrackedNamespaces ??= new List<string>();
            NamespaceProfiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            NamespaceOutputModes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AppProfiles.Clear();
            AppOutputModes.Clear();
            TrackedNamespaces.Clear();
            NamespaceProfiles.Clear();
            NamespaceOutputModes.Clear();

            if (Rules == null) return;

            foreach (var rule in Rules)
            {
                if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
                    continue;

                if (rule.TargetType == RuleTargetType.WindowClass && rule.MatchType == RuleMatchType.Exact)
                {
                    if (!string.IsNullOrEmpty(rule.PresetName))
                        AppProfiles[rule.Pattern] = rule.PresetName!;
                    if (!string.IsNullOrEmpty(rule.OutputMode))
                        AppOutputModes[rule.Pattern] = rule.OutputMode!;
                }
                else if (rule.TargetType == RuleTargetType.LayerNamespace)
                {
                    if (!TrackedNamespaces.Contains(rule.Pattern, StringComparer.OrdinalIgnoreCase))
                        TrackedNamespaces.Add(rule.Pattern);

                    if (!string.IsNullOrEmpty(rule.PresetName))
                        NamespaceProfiles[rule.Pattern] = rule.PresetName!;
                    if (!string.IsNullOrEmpty(rule.OutputMode))
                        NamespaceOutputModes[rule.Pattern] = rule.OutputMode!;
                }
            }
        }

        public void Serialize(FileInfo file)
        {
            SyncLegacyProperties();

            var serializer = new JsonSerializer { Formatting = Formatting.Indented };

            if (file.Directory != null && !file.Directory.Exists)
                file.Directory.Create();

            if (file.Exists)
                file.Delete();

            using var sw = file.CreateText();
            using var jw = new JsonTextWriter(sw);
            serializer.Serialize(jw, this);
        }

        public static bool TryDeserialize(FileInfo file, out AppProfilerSettings? settings)
        {
            settings = null;
            if (!file.Exists) return false;

            try
            {
                var serializer = new JsonSerializer();
                using var sr = file.OpenText();
                using var jr = new JsonTextReader(sr);
                settings = serializer.Deserialize<AppProfilerSettings>(jr);
                if (settings != null)
                {
                    settings.MigrateLegacySettings();
                }
                return settings != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
