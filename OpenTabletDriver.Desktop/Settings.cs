using System;
using System.ComponentModel;
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
        private string revision = GetVersion();

        private bool enableAppProfiler;
        private string defaultAppProfile;
        private System.Collections.Generic.Dictionary<string, string> appProfiles = new System.Collections.Generic.Dictionary<string, string>();

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

        [JsonProperty(nameof(EnableAppProfiler))]
        public bool EnableAppProfiler
        {
            set => this.RaiseAndSetIfChanged(ref this.enableAppProfiler, value);
            get => this.enableAppProfiler;
        }

        [JsonProperty(nameof(DefaultAppProfile))]
        public string DefaultAppProfile
        {
            set => this.RaiseAndSetIfChanged(ref this.defaultAppProfile, value);
            get => this.defaultAppProfile;
        }

        [JsonProperty(nameof(AppProfiles))]
        public System.Collections.Generic.Dictionary<string, string> AppProfiles
        {
            set => this.RaiseAndSetIfChanged(ref this.appProfiles, value);
            get => this.appProfiles;
        }

        public static Settings GetDefaults()
        {
            return new Settings
            {
                Profiles = GetDefaultProfiles(),
                LockUsableAreaDisplay = true,
                LockUsableAreaTablet = true
            };
        }

        private static ProfileCollection GetDefaultProfiles()
        {
            return new ProfileCollection(AppInfo.PluginManager.GetService<IDriver>().Tablets);
        }

        #region Custom Serialization

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented
        };

        public static bool TryDeserialize(FileInfo file, out Settings settings)
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

            static Settings deserialize(FileInfo file)
            {
                using (var stream = file.OpenRead())
                using (var sr = new StreamReader(stream))
                using (var jr = new JsonTextReader(sr))
                    return serializer.Deserialize<Settings>(jr);
            }
        }

        public static string GetVersion()
        {
            return Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        }

        [Obsolete("Unused and deprecated")]
        public static void Recover(FileInfo file, Settings settings)
        {
            using (var stream = file.OpenRead())
            using (var sr = new StreamReader(stream))
            using (var jr = new JsonTextReader(sr))
            {
                void propertyWatch(object _, PropertyChangedEventArgs p)
                {
                    var prop = settings.GetType().GetProperty(p.PropertyName).GetValue(settings);
                    Log.Write("Settings", $"Recovered '{p.PropertyName}'", LogLevel.Debug);
                }
                settings.PropertyChanged += propertyWatch;

                var serializer = new JsonSerializer
                {
                    Formatting = Formatting.Indented
                };

                try
                {
                    serializer.Populate(jr, settings);
                }
                catch (JsonException e)
                {
                    Log.Write("Settings", $"Recovery ended. Reason: {e.Message}", LogLevel.Debug);
                }
                finally
                {
                    settings.PropertyChanged -= propertyWatch;
                }
            }
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

        #endregion
    }
}
