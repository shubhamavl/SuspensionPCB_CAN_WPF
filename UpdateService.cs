using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SuspensionPCB_CAN_WPF
{
    /// <summary>
    /// Handles checking GitHub Releases for newer versions and downloading update packages.
    /// This class is purely client-side (no UI) and is driven by MainWindow.
    /// </summary>
    public sealed class UpdateService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        // NOTE: This must match your real GitHub repository so the client can reach Releases API.
        // Repo: https://github.com/shubhamavl/SuspensionPCB_CAN_WPF
        private const string RepositoryOwner = "shubhamavl";
        private const string RepositoryName = "SuspensionPCB_CAN_WPF";

        // We look for an asset that contains this prefix and has .zip extension.
        // Example: SuspensionPCB_CAN_WPF_Portable_v1.2.0.zip
        private const string AssetNameSubstring = "SuspensionPCB_CAN_WPF_Portable";
        private const string AssetExtension = ".zip";

        private readonly ProductionLogger _logger = ProductionLogger.Instance;

        public sealed class UpdateInfo
        {
            public Version CurrentVersion { get; init; } = new Version(0, 0, 0, 0);
            public Version LatestVersion { get; init; } = new Version(0, 0, 0, 0);
            public string DownloadUrl { get; init; } = string.Empty;
            public string AssetFileName { get; init; } = string.Empty;
            public string? ReleaseNotes { get; init; }

            public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
        }

        /// <summary>
        /// Queries GitHub Releases API for the latest release and compares it with the current app version.
        /// Returns null on failure (network errors, parse errors, etc.).
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var currentVersion = GetCurrentVersion();
                var latest = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                if (latest == null)
                {
                    return null;
                }

                // Find a suitable asset
                var asset = latest.Assets?.Find(a =>
                    !string.IsNullOrEmpty(a.BrowserDownloadUrl) &&
                    !string.IsNullOrEmpty(a.Name) &&
                    a.Name.Contains(AssetNameSubstring, StringComparison.OrdinalIgnoreCase) &&
                    a.Name.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                {
                    _logger.LogWarning("Latest GitHub release found, but no matching portable ZIP asset.", "UpdateService");
                    return null;
                }

                var latestVersion = ParseVersionFromTag(latest.TagName) ?? currentVersion;

                return new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion,
                    DownloadUrl = asset.BrowserDownloadUrl ?? string.Empty,
                    AssetFileName = asset.Name ?? "update.zip",
                    ReleaseNotes = latest.Body
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Update check failed: {ex.Message}", "UpdateService");
                return null;
            }
        }

        /// <summary>
        /// Downloads the update package to the local Update directory.
        /// Returns the full path to the downloaded file, or null on failure.
        /// </summary>
        public async Task<string?> DownloadUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
                return null;

            try
            {
                string updateDir = PathHelper.GetUpdateDirectory();
                string targetPath = Path.Combine(updateDir, info.AssetFileName);

                using var response = await HttpClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;

                await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalRead += read;

                    if (contentLength.HasValue && contentLength.Value > 0 && progress != null)
                    {
                        double percent = (double)totalRead / contentLength.Value * 100.0;
                        progress.Report(percent);
                    }
                }

                _logger.LogInfo($"Update package downloaded to {targetPath}", "UpdateService");
                return targetPath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Update download failed: {ex.Message}", "UpdateService");
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("SuspensionPCB_CAN_WPF/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            return client;
        }

        private async Task<GithubReleaseDto?> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            var url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"GitHub latest release API returned {(int)response.StatusCode}", "UpdateService");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            var dto = await JsonSerializer.DeserializeAsync<GithubReleaseDto>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, cancellationToken).ConfigureAwait(false);

            return dto;
        }

        private static Version GetCurrentVersion()
        {
            try
            {
                var assembly = typeof(App).Assembly;

                // Prefer informational version if available (e.g. 1.2.3)
                var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion)
                    && Version.TryParse(infoAttr.InformationalVersion.Split('+')[0], out var infoVersion))
                {
                    return infoVersion;
                }

                var asmVersion = assembly.GetName().Version;
                if (asmVersion != null)
                    return asmVersion;
            }
            catch
            {
                // Fall through to default
            }

            return new Version(1, 0, 0, 0);
        }

        private static Version? ParseVersionFromTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            // Common patterns: "v1.2.3", "1.2.3"
            var trimmed = tag.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..];

            if (Version.TryParse(trimmed, out var version))
                return version;

            return null;
        }

        #region GitHub DTOs

        private sealed class GithubReleaseDto
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("assets")]
            public System.Collections.Generic.List<GithubAssetDto>? Assets { get; set; }
        }

        private sealed class GithubAssetDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }

        #endregion
    }
}


