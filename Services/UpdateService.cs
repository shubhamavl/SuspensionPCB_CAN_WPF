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
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Services
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

        public sealed class UpdateCheckResult
        {
            public UpdateInfo? Info { get; init; }
            public string? ErrorMessage { get; init; }
            public bool IsRateLimited { get; init; }
            public bool IsNetworkError { get; init; }
            public bool IsSuccess => Info != null && ErrorMessage == null;

            public static UpdateCheckResult Success(UpdateInfo info) => new() { Info = info };
            public static UpdateCheckResult RateLimited() => new() { ErrorMessage = "Update check was performed recently. Please wait at least 1 hour between checks.", IsRateLimited = true };
            public static UpdateCheckResult NetworkError(string message) => new() { ErrorMessage = message, IsNetworkError = true };
            public static UpdateCheckResult Error(string message) => new() { ErrorMessage = message };
        }

        /// <summary>
        /// Queries GitHub Releases API for the latest release and compares it with the current app version.
        /// Returns detailed result with error information for better user feedback.
        /// Rate limiting is currently disabled but can be re-enabled if needed.
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Rate limiting disabled for now - can be re-enabled if needed
                // if (!ShouldAllowUpdateCheck())
                // {
                //     _logger.LogInfo("Update check skipped due to rate limiting (minimum 1 hour between checks)", "UpdateService");
                //     return UpdateCheckResult.RateLimited();
                // }

                var currentVersion = GetCurrentVersion();
                GithubReleaseDto? latest;
                try
                {
                    latest = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogWarning($"Network error during update check: {httpEx.Message}", "UpdateService");
                    return UpdateCheckResult.NetworkError($"Network error: {httpEx.Message}. Please check your internet connection.");
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Update check timed out", "UpdateService");
                    return UpdateCheckResult.NetworkError("Update check timed out. Please check your internet connection and try again.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error fetching release from GitHub: {ex.Message}", "UpdateService");
                    return UpdateCheckResult.Error($"Failed to connect to GitHub: {ex.Message}");
                }

                if (latest == null)
                {
                    return UpdateCheckResult.Error("No releases found on GitHub. The repository may not have any published releases yet.");
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
                    return UpdateCheckResult.Error($"Release '{latest.TagName}' found, but no matching portable ZIP file was found. Expected filename containing '{AssetNameSubstring}'.");
                }

                // Validate download URL is from allowed domain
                if (!IsValidDownloadUrl(asset.BrowserDownloadUrl))
                {
                    _logger.LogError($"Download URL validation failed: {asset.BrowserDownloadUrl}", "UpdateService");
                    return UpdateCheckResult.Error("Download URL validation failed. The release asset URL is not from a trusted source.");
                }

                var latestVersion = ParseVersionFromTag(latest.TagName);
                if (latestVersion == null)
                {
                    _logger.LogWarning($"Could not parse version from tag: {latest.TagName}", "UpdateService");
                    return UpdateCheckResult.Error($"Could not parse version from release tag '{latest.TagName}'. Expected format: v1.2.3 or 1.2.3");
                }

                // Extract SHA-256 hash from release notes (format: SHA-256: `hash` or **SHA-256 Hash:** `hash`)
                var expectedHash = ExtractSha256FromReleaseNotes(latest.Body);

                // Record successful check time for rate limiting (disabled for now, but kept for future use)
                // RecordUpdateCheckTime();

                return UpdateCheckResult.Success(new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion,
                    DownloadUrl = asset.BrowserDownloadUrl ?? string.Empty,
                    AssetFileName = asset.Name ?? "update.zip",
                    ReleaseNotes = latest.Body,
                    ExpectedSha256Hash = expectedHash
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error during update check: {ex.Message}", "UpdateService");
                return UpdateCheckResult.Error($"Unexpected error: {ex.Message}");
            }
        }

        public sealed class DownloadResult
        {
            public string? FilePath { get; init; }
            public string? ErrorMessage { get; init; }
            public bool IsSuccess => FilePath != null && ErrorMessage == null;
            public bool IsNetworkError { get; init; }
            public bool IsHashMismatch { get; init; }

            public static DownloadResult Success(string filePath) => new() { FilePath = filePath };
            public static DownloadResult NetworkError(string message) => new() { ErrorMessage = message, IsNetworkError = true };
            public static DownloadResult HashMismatch(string message) => new() { ErrorMessage = message, IsHashMismatch = true };
            public static DownloadResult Error(string message) => new() { ErrorMessage = message };
        }

        /// <summary>
        /// Downloads the update package to the local Update directory and verifies its integrity.
        /// Returns detailed result with error information for better diagnostics.
        /// Performs SHA-256 hash verification if hash is available in UpdateInfo.
        /// </summary>
        public async Task<DownloadResult> DownloadUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
                return DownloadResult.Error("Invalid update information: download URL is missing.");

            // Validate URL again before downloading
            if (!IsValidDownloadUrl(info.DownloadUrl))
            {
                _logger.LogError($"Download URL validation failed: {info.DownloadUrl}", "UpdateService");
                return DownloadResult.Error($"Download URL validation failed. URL is not from a trusted source: {info.DownloadUrl}");
            }

            try
            {
                string updateDir = PathHelper.GetUpdateDirectory();
                string targetPath = Path.Combine(updateDir, info.AssetFileName);

                // Check if directory exists and is writable
                try
                {
                    if (!Directory.Exists(updateDir))
                    {
                        Directory.CreateDirectory(updateDir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to create update directory: {ex.Message}", "UpdateService");
                    return DownloadResult.Error($"Cannot create update directory: {ex.Message}. Check file permissions.");
                }

                // Delete existing file if it exists
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Delete(targetPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not delete existing update file: {ex.Message}", "UpdateService");
                    }
                }

                _logger.LogInfo($"Starting download from: {info.DownloadUrl}", "UpdateService");
                _logger.LogInfo($"Target path: {targetPath}", "UpdateService");

                using var response = await HttpClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorMsg = $"HTTP {statusCode}: {response.ReasonPhrase}";
                    _logger.LogError($"Download failed with status {statusCode}: {errorMsg}", "UpdateService");
                    
                    if (statusCode == 404)
                        return DownloadResult.Error($"Update file not found on server (404). The release asset may have been removed.");
                    if (statusCode == 403)
                        return DownloadResult.Error($"Access denied (403). GitHub may be rate limiting requests.");
                    if (statusCode >= 500)
                        return DownloadResult.NetworkError($"Server error ({statusCode}). Please try again later.");
                    
                    return DownloadResult.Error($"Download failed: {errorMsg}");
                }

                var contentLength = response.Content.Headers.ContentLength;
                _logger.LogInfo($"Content length: {contentLength} bytes", "UpdateService");

                // Download to temporary file first, then rename to avoid file locking issues
                string tempPath = targetPath + ".tmp";
                
                await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
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
                }

                // File stream is now closed, rename temp file to final location
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Delete(targetPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not delete existing file before rename: {ex.Message}", "UpdateService");
                    }
                }

                File.Move(tempPath, targetPath);
                _logger.LogInfo($"Update package downloaded successfully: {targetPath}", "UpdateService");

                // Verify file exists and has content
                if (!File.Exists(targetPath))
                {
                    return DownloadResult.Error("Download completed but file was not found on disk.");
                }

                var fileInfo = new FileInfo(targetPath);
                if (fileInfo.Length == 0)
                {
                    try { File.Delete(targetPath); } catch { }
                    return DownloadResult.Error("Downloaded file is empty (0 bytes). The file may be corrupted.");
                }

                // Verify SHA-256 hash if available
                if (!string.IsNullOrWhiteSpace(info.ExpectedSha256Hash))
                {
                    _logger.LogInfo("Verifying SHA-256 hash...", "UpdateService");
                    var hashResult = await VerifyFileHashAsync(targetPath, info.ExpectedSha256Hash);
                    if (!hashResult.IsValid)
                    {
                        _logger.LogError($"SHA-256 hash verification failed. Expected: {info.ExpectedSha256Hash}, Computed: {hashResult.ComputedHash}", "UpdateService");
                        try { File.Delete(targetPath); } catch { }
                        return DownloadResult.HashMismatch(
                            $"File integrity check failed. The downloaded file does not match the expected SHA-256 hash.\n\n" +
                            $"Expected: {info.ExpectedSha256Hash}\n" +
                            $"Computed: {hashResult.ComputedHash}\n\n" +
                            $"This could indicate:\n" +
                            $"• File corruption during download\n" +
                            $"• Network interference\n" +
                            $"• Incorrect hash in release notes\n\n" +
                            $"Please try downloading again.");
                    }
                    _logger.LogInfo($"SHA-256 hash verification passed: {hashResult.ComputedHash}", "UpdateService");
                }
                else
                {
                    _logger.LogWarning("No SHA-256 hash available for verification. Download integrity not verified.", "UpdateService");
                }

                return DownloadResult.Success(targetPath);
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError($"Network error during download: {httpEx.Message}", "UpdateService");
                return DownloadResult.NetworkError($"Network error: {httpEx.Message}. Please check your internet connection and try again.");
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("Download timed out", "UpdateService");
                return DownloadResult.NetworkError("Download timed out. Please check your internet connection and try again.");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError($"Permission error: {ex.Message}", "UpdateService");
                return DownloadResult.Error($"Permission denied: {ex.Message}. Please run the application with administrator privileges or check file permissions.");
            }
            catch (IOException ioEx)
            {
                _logger.LogError($"I/O error: {ioEx.Message}", "UpdateService");
                return DownloadResult.Error($"File system error: {ioEx.Message}. Check disk space and file permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error during download: {ex.Message}\n{ex.StackTrace}", "UpdateService");
                return DownloadResult.Error($"Unexpected error: {ex.Message}");
            }
        }

        private sealed class HashVerificationResult
        {
            public bool IsValid { get; init; }
            public string ComputedHash { get; init; } = string.Empty;
        }

        private async Task<HashVerificationResult> VerifyFileHashAsync(string filePath, string expectedHash)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (!File.Exists(filePath))
                        return new HashVerificationResult { IsValid = false, ComputedHash = "FILE_NOT_FOUND" };

                    // Use FileShare.ReadWrite to allow other processes to read while we verify
                    using var sha256 = SHA256.Create();
                    await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var computedHash = sha256.ComputeHash(fileStream);
                    var computedHashString = BitConverter.ToString(computedHash).Replace("-", "").ToUpperInvariant();

                    var expectedHashUpper = expectedHash.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");

                    return new HashVerificationResult
                    {
                        IsValid = string.Equals(computedHashString, expectedHashUpper, StringComparison.OrdinalIgnoreCase),
                        ComputedHash = computedHashString
                    };
                }
                catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process") && attempt < maxRetries)
                {
                    _logger.LogWarning($"File locked on attempt {attempt}, retrying in {retryDelayMs * attempt}ms...", "UpdateService");
                    await Task.Delay(retryDelayMs * attempt); // Exponential backoff
                    continue;
                }
                catch (UnauthorizedAccessException) when (attempt < maxRetries)
                {
                    _logger.LogWarning($"Access denied on attempt {attempt}, retrying in {retryDelayMs * attempt}ms...", "UpdateService");
                    await Task.Delay(retryDelayMs * attempt);
                    continue;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        _logger.LogError($"Hash verification failed after {maxRetries} attempts: {ex.Message}", "UpdateService");
                        return new HashVerificationResult { IsValid = false, ComputedHash = $"ERROR: {ex.Message}" };
                    }
                    _logger.LogWarning($"Hash verification attempt {attempt} failed, retrying: {ex.Message}", "UpdateService");
                    await Task.Delay(retryDelayMs * attempt);
                }
            }

            return new HashVerificationResult { IsValid = false, ComputedHash = "ERROR: Max retries exceeded" };
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
                var statusCode = (int)response.StatusCode;
                _logger.LogWarning($"GitHub latest release API returned {statusCode}", "UpdateService");
                
                if (statusCode == 404)
                {
                    _logger.LogWarning($"Repository or releases not found: {RepositoryOwner}/{RepositoryName}", "UpdateService");
                }
                else if (statusCode == 403)
                {
                    _logger.LogWarning("GitHub API rate limit may have been exceeded", "UpdateService");
                }
                
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            var dto = await JsonSerializer.DeserializeAsync<GithubReleaseDto>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, cancellationToken).ConfigureAwait(false);

            return dto;
        }

        /// <summary>
        /// Fetches all releases from GitHub, sorted by version (newest first).
        /// Only includes releases that have a valid portable ZIP asset.
        /// Handles pagination if there are more than 30 releases.
        /// </summary>
        public async Task<List<GithubReleaseDto>> GetAllReleasesAsync(CancellationToken cancellationToken = default)
        {
            var allReleases = new List<GithubReleaseDto>();
            int page = 1;
            const int perPage = 30; // GitHub API default is 30, max is 100

            try
            {
                while (true)
                {
                    var url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?page={page}&per_page={perPage}";
                    using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var statusCode = (int)response.StatusCode;
                        _logger.LogWarning($"GitHub releases API returned {statusCode} for page {page}", "UpdateService");
                        
                        if (statusCode == 404)
                        {
                            _logger.LogWarning($"Repository or releases not found: {RepositoryOwner}/{RepositoryName}", "UpdateService");
                            break;
                        }
                        else if (statusCode == 403)
                        {
                            _logger.LogWarning("GitHub API rate limit may have been exceeded", "UpdateService");
                            break;
                        }
                        
                        // For other errors, try to continue with what we have
                        if (allReleases.Count > 0)
                            break;
                        return allReleases;
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                    var releases = await JsonSerializer.DeserializeAsync<List<GithubReleaseDto>>(stream, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }, cancellationToken).ConfigureAwait(false);

                    if (releases == null || releases.Count == 0)
                    {
                        // No more releases
                        break;
                    }

                    // Filter releases to only include those with valid portable ZIP assets
                    foreach (var release in releases)
                    {
                        var asset = release.Assets?.Find(a =>
                            !string.IsNullOrEmpty(a.BrowserDownloadUrl) &&
                            !string.IsNullOrEmpty(a.Name) &&
                            a.Name.Contains(AssetNameSubstring, StringComparison.OrdinalIgnoreCase) &&
                            a.Name.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase));

                        if (asset != null && IsValidDownloadUrl(asset.BrowserDownloadUrl))
                        {
                            // Only include releases with valid version tags
                            if (ParseVersionFromTag(release.TagName) != null)
                            {
                                allReleases.Add(release);
                            }
                            else
                            {
                                _logger.LogWarning($"Skipping release '{release.TagName}' - invalid version format", "UpdateService");
                            }
                        }
                    }

                    // If we got fewer than perPage releases, we've reached the end
                    if (releases.Count < perPage)
                    {
                        break;
                    }

                    page++;
                }

                // Sort by version (newest first)
                allReleases.Sort((a, b) =>
                {
                    var versionA = ParseVersionFromTag(a.TagName);
                    var versionB = ParseVersionFromTag(b.TagName);
                    
                    if (versionA == null && versionB == null) return 0;
                    if (versionA == null) return 1;
                    if (versionB == null) return -1;
                    
                    return versionB.CompareTo(versionA); // Descending order (newest first)
                });

                _logger.LogInfo($"Fetched {allReleases.Count} valid releases from GitHub", "UpdateService");
                return allReleases;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogWarning($"Network error while fetching releases: {httpEx.Message}", "UpdateService");
                // Return what we have so far, if any
                return allReleases;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Fetching releases timed out", "UpdateService");
                return allReleases;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error while fetching releases: {ex.Message}", "UpdateService");
                return allReleases;
            }
        }

        /// <summary>
        /// Converts a GithubReleaseDto to UpdateInfo for use with download/install logic.
        /// </summary>
        public UpdateInfo? ConvertReleaseToUpdateInfo(GithubReleaseDto release)
        {
            if (release == null)
                return null;

            var asset = release.Assets?.Find(a =>
                !string.IsNullOrEmpty(a.BrowserDownloadUrl) &&
                !string.IsNullOrEmpty(a.Name) &&
                a.Name.Contains(AssetNameSubstring, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase));

            if (asset == null)
                return null;

            if (!IsValidDownloadUrl(asset.BrowserDownloadUrl))
                return null;

            var version = ParseVersionFromTag(release.TagName);
            if (version == null)
                return null;

            var currentVersion = GetCurrentVersion();
            var expectedHash = ExtractSha256FromReleaseNotes(release.Body);

            return new UpdateInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = version,
                DownloadUrl = asset.BrowserDownloadUrl ?? string.Empty,
                AssetFileName = asset.Name ?? "update.zip",
                ReleaseNotes = release.Body,
                ExpectedSha256Hash = expectedHash
            };
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

            // Pattern 1: **SHA-256 Hash:** `hash` (GitHub release format)
            var pattern1 = new Regex(@"\*\*SHA-256\s+Hash:\*\*\s*`([a-fA-F0-9]{64})`", RegexOptions.IgnoreCase);
            var match1 = pattern1.Match(releaseNotes);
            if (match1.Success && match1.Groups.Count > 1)
            {
                return match1.Groups[1].Value.ToUpperInvariant();
            }

            // Pattern 2: SHA-256: `hash` or SHA-256 Hash: `hash`
            var pattern2 = new Regex(@"SHA-256[:\s]+`?([a-fA-F0-9]{64})`?", RegexOptions.IgnoreCase);
            var match2 = pattern2.Match(releaseNotes);
            if (match2.Success && match2.Groups.Count > 1)
            {
                return match2.Groups[1].Value.ToUpperInvariant();
            }

            // Pattern 3: Just a 64-character hex string (less reliable, but fallback)
            var pattern3 = new Regex(@"\b([a-fA-F0-9]{64})\b");
            var match3 = pattern3.Match(releaseNotes);
            if (match3.Success && match3.Groups.Count > 1)
            {
                return match3.Groups[1].Value.ToUpperInvariant();
            }

            return null;
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
            // Code signing verification not yet implemented
            // Returns true to allow updates until certificate is obtained
            return true;
        }

        #region GitHub DTOs

        public sealed class GithubReleaseDto
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("published_at")]
            public string? PublishedAt { get; set; }

            [JsonPropertyName("assets")]
            public System.Collections.Generic.List<GithubAssetDto>? Assets { get; set; }
        }

        public sealed class GithubAssetDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }

        #endregion
    }
}


