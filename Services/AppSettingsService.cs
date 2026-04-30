using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BmsHostUi.Services
{
    public sealed class AppSettingsService
    {
        [DataContract]
        public sealed class AppSettings
        {
            [DataMember(Name = "github_owner")]
            public string GitHubOwner { get; set; }

            [DataMember(Name = "github_repo")]
            public string GitHubRepo { get; set; }
        }

        private static string SettingsFilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Grididea",
                    "BmsHostUi");
                return Path.Combine(dir, "appsettings.json");
            }
        }

        public AppSettings Load()
        {
            string path = SettingsFilePath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = serializer.ReadObject(stream) as AppSettings;
                    return settings ?? new AppSettings();
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            string path = SettingsFilePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var stream = File.Create(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings ?? new AppSettings());
            }
        }
    }
}
