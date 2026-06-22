using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

#nullable enable

namespace OpenTabletDriver.Desktop
{
    public class AppProfilerSettings
    {
        [JsonProperty("enableAppProfiler")]
        public bool EnableAppProfiler { get; set; } = false;

        [JsonProperty("defaultAppProfile")]
        public string? DefaultAppProfile { get; set; }

        [JsonProperty("appProfiles")]
        public Dictionary<string, string> AppProfiles { get; set; } = new Dictionary<string, string>();

        [JsonProperty("defaultOutputMode")]
        public string? DefaultOutputMode { get; set; }

        [JsonProperty("appOutputModes")]
        public Dictionary<string, string> AppOutputModes { get; set; } = new Dictionary<string, string>();

        [JsonProperty("trackedNamespaces")]
        public List<string> TrackedNamespaces { get; set; } = new List<string>();

        [JsonProperty("namespaceProfiles")]
        public Dictionary<string, string> NamespaceProfiles { get; set; } = new Dictionary<string, string>();

        [JsonProperty("namespaceOutputModes")]
        public Dictionary<string, string> NamespaceOutputModes { get; set; } = new Dictionary<string, string>();

        public void Serialize(FileInfo file)
        {
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
                return settings != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
