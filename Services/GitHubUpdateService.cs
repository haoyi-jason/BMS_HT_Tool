using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BmsHostUi.Services
{
    public sealed class GitHubUpdateService
    {
        private const string ApiTemplate = "https://api.github.com/repos/{0}/{1}/releases/latest";

        public sealed class ReleaseInfo
        {
            public Version Version { get; set; }
            public string TagName { get; set; }
            public string Name { get; set; }
            public string HtmlUrl { get; set; }
            public string Body { get; set; }
            public string InstallerAssetUrl { get; set; }
            public string InstallerAssetName { get; set; }
        }

        [DataContract]
        private sealed class GitHubReleaseDto
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "html_url")]
            public string HtmlUrl { get; set; }

            [DataMember(Name = "body")]
            public string Body { get; set; }

            [DataMember(Name = "assets")]
            public List<GitHubAssetDto> Assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAssetDto
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }

        public async Task<ReleaseInfo> GetLatestReleaseAsync(string owner, string repo, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                throw new InvalidOperationException("GitHub owner/repo is not configured.");
            }

            string url = string.Format(ApiTemplate, owner.Trim(), repo.Trim());
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "application/vnd.github+json";
            request.UserAgent = "Grididea-BMS-HT-Tool";

            using (cancellationToken.Register(() => request.Abort()))
            using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
            using (var stream = response.GetResponseStream())
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("GitHub response stream is empty.");
                }

                var serializer = new DataContractJsonSerializer(typeof(GitHubReleaseDto));
                var dto = serializer.ReadObject(stream) as GitHubReleaseDto;
                if (dto == null)
                {
                    throw new InvalidOperationException("Cannot parse GitHub release response.");
                }

                Version parsedVersion;
                if (!TryParseVersion(dto.TagName, out parsedVersion))
                {
                    parsedVersion = new Version(0, 0, 0, 0);
                }

                var exeAsset = dto.Assets
                    ?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Name)
                        && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                return new ReleaseInfo
                {
                    Version = parsedVersion,
                    TagName = dto.TagName ?? string.Empty,
                    Name = dto.Name ?? string.Empty,
                    HtmlUrl = dto.HtmlUrl ?? string.Empty,
                    Body = dto.Body ?? string.Empty,
                    InstallerAssetUrl = exeAsset?.BrowserDownloadUrl ?? string.Empty,
                    InstallerAssetName = exeAsset?.Name ?? string.Empty,
                };
            }
        }

        public async Task<string> DownloadInstallerAsync(string downloadUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidOperationException("Installer download URL is empty.");
            }

            var uri = new Uri(downloadUrl);
            string fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "Grididea_BMS_HT_Update.exe";
            }

            string tempPath = Path.Combine(Path.GetTempPath(), fileName);
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Grididea-BMS-HT-Tool";
                using (cancellationToken.Register(() => client.CancelAsync()))
                {
                    await client.DownloadFileTaskAsync(uri, tempPath).ConfigureAwait(false);
                }
            }

            return tempPath;
        }

        private static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            string cleaned = tag.Trim();
            if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(1);
            }

            var parts = cleaned.Split('.').ToList();
            while (parts.Count < 4)
            {
                parts.Add("0");
            }

            string normalized = string.Join(".", parts.Take(4));
            return Version.TryParse(normalized, out version);
        }
    }
}
