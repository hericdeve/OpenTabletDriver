using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Desktop
{
    public class Settings : ViewModel
    {
        private ProfileCollection profiles = new ProfileCollection();
        private bool lockUsableAreaDisplay, lockUsableAreaTablet;
        private PluginSettingStoreCollection tools = new PluginSettingStoreCollection();
        private OpenTabletDriver.Desktop.Hud.HudConfiguration? hud;
        private OpenTabletDriver.Desktop.Tools.ContextualToolsConfiguration? contextualTools;
        private OpenTabletDriver.Desktop.Compositor.CompositorSettings? compositorSettings;
        private string revision = GetVersion();

        [JsonProperty(nameof(Revision))]
        public string Revision
        {
            set => this.RaiseAndSetIfChanged(ref revision, value);
            get => revision;
        }

        [JsonProperty(nameof(Profiles))]
        public ProfileCollection Profiles
        {
            set => this.RaiseAndSetIfChanged(ref profiles, value);
            get => profiles;
        }

        [JsonProperty(nameof(LockUsableAreaDisplay))]
        public bool LockUsableAreaDisplay
        {
            set => this.RaiseAndSetIfChanged(ref this.lockUsableAreaDisplay, value);
            get => this.lockUsableAreaDisplay;
        }

        [JsonProperty(nameof(LockUsableAreaTablet))]
        public bool LockUsableAreaTablet
        {
            set => this.RaiseAndSetIfChanged(ref this.lockUsableAreaTablet, value);
            get => this.lockUsableAreaTablet;
        }

        [JsonProperty(nameof(Tools))]
        public PluginSettingStoreCollection Tools
        {
            set => RaiseAndSetIfChanged(ref this.tools, value);
            get => this.tools;
        }

        [JsonProperty(nameof(Hud))]
        public Hud.HudConfiguration Hud
        {
            set => RaiseAndSetIfChanged(ref this.hud, value);
            get => this.hud ??= OpenTabletDriver.Desktop.Hud.HudConfiguration.GetDefaults();
        }

        [JsonProperty(nameof(ContextualTools))]
        public OpenTabletDriver.Desktop.Tools.ContextualToolsConfiguration ContextualTools
        {
            set => RaiseAndSetIfChanged(ref this.contextualTools, value);
            get
            {
                if (this.contextualTools == null)
                    this.contextualTools = OpenTabletDriver.Desktop.Tools.ContextualToolsConfiguration.GetDefaults();
                else
                    this.contextualTools.Deduplicate();
                return this.contextualTools;
            }
        }

        [JsonProperty(nameof(CompositorSettings))]
        public OpenTabletDriver.Desktop.Compositor.CompositorSettings CompositorSettings
        {
            set => RaiseAndSetIfChanged(ref this.compositorSettings, value);
            get => this.compositorSettings ??= new OpenTabletDriver.Desktop.Compositor.CompositorSettings();
        }

        public static Settings GetDefaults()
        {
            return new Settings
            {
                Profiles = GetDefaultProfiles(),
                LockUsableAreaDisplay = true,
                LockUsableAreaTablet = true,
                Hud = OpenTabletDriver.Desktop.Hud.HudConfiguration.GetDefaults(),
                ContextualTools = OpenTabletDriver.Desktop.Tools.ContextualToolsConfiguration.GetDefaults(),
                CompositorSettings = new OpenTabletDriver.Desktop.Compositor.CompositorSettings()
            };
        }

        private static ProfileCollection GetDefaultProfiles()
        {
            // nullable warning suppressed because IDriver should always be provided by DI
            return new ProfileCollection(AppInfo.PluginManager.GetService<IDriver>()!.Tablets);
        }

        #region Custom Serialization

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static bool TryDeserialize(FileInfo file, [NotNullWhen(true)] out Settings? settings)
        {
            try
            {
                settings = deserialize(file);
                return settings != null;
            }
            catch (JsonException ex)
            {
                Log.Exception(ex);
                settings = default;
                return false;
            }

            static Settings? deserialize(FileInfo file)
            {
                using (var stream = file.OpenRead())
                using (var sr = new StreamReader(stream))
                using (var jr = new JsonTextReader(sr))
                    return serializer.Deserialize<Settings>(jr);
            }
        }

        public static string GetVersion()
        {
            // null warning suppressed because this should always succeed in desktop releases
            return Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion!;
        }

        public void Serialize(FileInfo file)
        {
            try
            {
                if (file.Exists)
                    file.Delete();

                using (var sw = file.CreateText())
                using (var jw = new JsonTextWriter(sw))
                    serializer.Serialize(jw, this);
            }
            catch (UnauthorizedAccessException)
            {
                Log.Write("Settings", $"OpenTabletDriver doesn't have permission to save persistent settings to {file.DirectoryName}", LogLevel.Error);
            }
        }

        public Settings Clone()
        {
            using var sw = new StringWriter();
            using (var jw = new JsonTextWriter(sw))
                serializer.Serialize(jw, this);

            using var sr = new StringReader(sw.ToString());
            using var jr = new JsonTextReader(sr);
            return serializer.Deserialize<Settings>(jr)!;
        }

        #endregion
    }
}
