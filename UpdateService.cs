using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Linq;

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

        // Allowed download domains for security (only GitHub)
        private static readonly string[] AllowedDomains = { "github.com", "githubusercontent.com" };

        // Rate limiting: minimum time between update checks (1 hour)
        private static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromHours(1);
        private static readonly string LastCheckTimeFile = Path.Combine(PathHelper.ApplicationDirectory, "Data", "last_update_check.txt");

        private readonly ProductionLogger _logger = ProductionLogger.Instance;

        public sealed class UpdateInfo
        {
            public Version CurrentVersion { get; init; } = new Version(0, 0, 0, 0);
            public Version LatestVersion { get; init; } = new Version(0, 0, 0, 0);
            public string DownloadUrl { get; init; } = string.Empty;
            public string AssetFileName { get; init; } = string.Empty;
            public string? ReleaseNotes { get; init; }
            public string? ExpectedSha256Hash { get; init; }  // SHA-256 hash for integrity verification

            public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
        }

        /// <summary>
        /// Queries GitHub Releases API for the latest release and compares it with the current app version.
        /// Returns null on failure (network errors, parse errors, rate limiting, etc.).
        /// Implements rate limiting to prevent excessive API calls.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Rate limiting: check if enough time has passed since last check
                if (!ShouldAllowUpdateCheck())
                {
                    _logger.LogInfo("Update check skipped due to rate limiting (minimum 1 hour between checks)", "UpdateService");
                    return null;
                }

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

                // Validate download URL is from allowed domain
                if (!IsValidDownloadUrl(asset.BrowserDownloadUrl))
                {
                    _logger.LogError($"Download URL validation failed: {asset.BrowserDownloadUrl}", "UpdateService");
                    return null;
                }

                var latestVersion = ParseVersionFromTag(latest.TagName) ?? currentVersion;

                // Extract SHA-256 hash from release notes (format: SHA-256: `hash` or **SHA-256 Hash:** `hash`)
                var expectedHash = ExtractSha256FromReleaseNotes(latest.Body);

                // Record successful check time for rate limiting
                RecordUpdateCheckTime();

                return new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion,
                    DownloadUrl = asset.BrowserDownloadUrl ?? string.Empty,
                    AssetFileName = asset.Name ?? "update.zip",
                    ReleaseNotes = latest.Body,
                    ExpectedSha256Hash = expectedHash
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Update check failed: {ex.Message}", "UpdateService");
                return null;
            }
        }

        /// <summary>
        /// Downloads the update package to the local Update directory and verifies its integrity.
        /// Returns the full path to the downloaded file, or null on failure.
        /// Performs SHA-256 hash verification if hash is available in UpdateInfo.
        /// </summary>
        public async Task<string?> DownloadUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
                return null;

            // Validate URL again before downloading
            if (!IsValidDownloadUrl(info.DownloadUrl))
            {
                _logger.LogError($"Download URL validation failed: {info.DownloadUrl}", "UpdateService");
                return null;
            }

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

                // Verify SHA-256 hash if available
                if (!string.IsNullOrWhiteSpace(info.ExpectedSha256Hash))
                {
                    if (!VerifyFileHash(targetPath, info.ExpectedSha256Hash))
                    {
                        _logger.LogError($"SHA-256 hash verification failed for downloaded file. File may be corrupted or tampered with.", "UpdateService");
                        try { File.Delete(targetPath); } catch { }
                        return null;
                    }
                    _logger.LogInfo("SHA-256 hash verification passed", "UpdateService");
                }
                else
                {
                    _logger.LogWarning("No SHA-256 hash available for verification. Download integrity not verified.", "UpdateService");
                }

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

        /// <summary>
        /// Validates that the download URL is from an allowed domain (GitHub only).
        /// </summary>
        private static bool IsValidDownloadUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();

                // Check if host matches any allowed domain
                foreach (var allowedDomain in AllowedDomains)
                {
                    if (host == allowedDomain || host.EndsWith($".{allowedDomain}", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts SHA-256 hash from release notes.
        /// Looks for patterns like: "SHA-256: `hash`" or "**SHA-256 Hash:** `hash`"
        /// </summary>
        private static string? ExtractSha256FromReleaseNotes(string? releaseNotes)
        {
            if (string.IsNullOrWhiteSpace(releaseNotes))
                return null;

            // Pattern 1: SHA-256: `hash` or **SHA-256 Hash:** `hash`
            var pattern1 = new Regex(@"SHA-256[:\s]+`?([a-fA-F0-9]{64})`?", RegexOptions.IgnoreCase);
            var match1 = pattern1.Match(releaseNotes);
            if (match1.Success && match1.Groups.Count > 1)
            {
                return match1.Groups[1].Value.ToUpperInvariant();
            }

            // Pattern 2: Just a 64-character hex string (less reliable, but fallback)
            var pattern2 = new Regex(@"\b([a-fA-F0-9]{64})\b");
            var match2 = pattern2.Match(releaseNotes);
            if (match2.Success && match2.Groups.Count > 1)
            {
                return match2.Groups[1].Value.ToUpperInvariant();
            }

            return null;
        }

        /// <summary>
        /// Verifies the SHA-256 hash of a file against the expected hash.
        /// </summary>
        private static bool VerifyFileHash(string filePath, string expectedHash)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                using var sha256 = SHA256.Create();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var computedHash = sha256.ComputeHash(fileStream);
                var computedHashString = BitConverter.ToString(computedHash).Replace("-", "").ToUpperInvariant();

                var expectedHashUpper = expectedHash.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");

                return string.Equals(computedHashString, expectedHashUpper, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if enough time has passed since the last update check (rate limiting).
        /// </summary>
        private static bool ShouldAllowUpdateCheck()
        {
            try
            {
                if (!File.Exists(LastCheckTimeFile))
                    return true;  // First check, allow it

                var lastCheckText = File.ReadAllText(LastCheckTimeFile).Trim();
                if (string.IsNullOrWhiteSpace(lastCheckText))
                    return true;

                if (DateTime.TryParse(lastCheckText, out var lastCheckTime))
                {
                    var timeSinceLastCheck = DateTime.UtcNow - lastCheckTime;
                    return timeSinceLastCheck >= MinimumCheckInterval;
                }

                return true;  // If we can't parse, allow the check
            }
            catch
            {
                return true;  // On error, allow the check (fail open)
            }
        }

        /// <summary>
        /// Records the current time as the last update check time for rate limiting.
        /// </summary>
        private static void RecordUpdateCheckTime()
        {
            try
            {
                var dataDir = PathHelper.GetDataDirectory();
                var filePath = Path.Combine(dataDir, "last_update_check.txt");
                File.WriteAllText(filePath, DateTime.UtcNow.ToString("O"));  // ISO 8601 format
            }
            catch
            {
                // Silently fail - rate limiting is best-effort
            }
        }

        /// <summary>
        /// Verifies code signing of an executable file (if code signing is enabled).
        /// Returns true if verification passes or if code signing is not configured.
        /// Returns false if code signing verification fails.
        /// 
        /// NOTE: This requires a code signing certificate. See documentation for setup.
        /// </summary>
        private static bool VerifyCodeSignature(string exePath)
        {
            // TODO: Implement code signing verification when certificate is obtained
            // For now, this is a placeholder that always returns true
            // 
            // Implementation would use:
            // - System.Security.Cryptography.X509Certificates
            // - Authenticode verification APIs
            // - Or external tools like signtool.exe
            //
            // Example approach:
            // 1. Extract certificate from signed executable
            // 2. Verify certificate chain against trusted root CAs
            // 3. Check certificate hasn't expired
            // 4. Verify certificate subject matches expected publisher
            //
            // When ready, uncomment and implement:
            /*
            try
            {
                // Use signtool or .NET APIs to verify signature
                // Return true only if signature is valid
                return true;  // Placeholder
            }
            catch
            {
                return false;
            }
            */

            // For now, skip verification (fail open) until certificate is obtained
            return true;
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


