using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SuspensionPCB_CAN_WPF.Services;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class VersionSelectionDialog : Window
    {
        private readonly UpdateService _updateService = new UpdateService();
        private readonly Version _currentVersion;
        private CancellationTokenSource? _cancellationTokenSource;
        
        public UpdateService.UpdateInfo? SelectedVersionInfo { get; private set; }

        // Data model for ListView items
        private sealed class ReleaseItem
        {
            public string VersionText { get; set; } = string.Empty;
            public string ReleaseName { get; set; } = string.Empty;
            public string ReleaseDate { get; set; } = string.Empty;
            public Version Version { get; set; } = new Version(0, 0, 0, 0);
            public string? ReleaseNotes { get; set; }
            public bool IsCurrentVersion { get; set; }
            public UpdateService.GithubReleaseDto ReleaseDto { get; set; } = null!;
        }

        private ObservableCollection<ReleaseItem> _releases = new ObservableCollection<ReleaseItem>();

        public VersionSelectionDialog()
        {
            InitializeComponent();
            _currentVersion = GetCurrentVersion();
            CurrentVersionText.Text = $"Current version: {_currentVersion}";
            ReleasesListView.ItemsSource = _releases;
            
            // Load releases asynchronously
            _ = LoadReleasesAsync();
        }

        private Version GetCurrentVersion()
        {
            try
            {
                var assembly = typeof(App).Assembly;
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

        private async Task LoadReleasesAsync()
        {
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                ErrorPanel.Visibility = Visibility.Collapsed;
                ReleasesPanel.Visibility = Visibility.Collapsed;
                ReleaseNotesPanel.Visibility = Visibility.Collapsed;
                LoadingText.Text = "Loading releases from GitHub...";
                InstallBtn.IsEnabled = false;

                _cancellationTokenSource = new CancellationTokenSource();
                var releases = await _updateService.GetAllReleasesAsync(_cancellationTokenSource.Token);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (releases == null || releases.Count == 0)
                    {
                        ShowError("No releases found. The repository may not have any published releases yet.");
                        return;
                    }

                    _releases.Clear();
                    foreach (var release in releases)
                    {
                        var version = ParseVersionFromTag(release.TagName);
                        if (version == null)
                            continue;

                        var releaseItem = new ReleaseItem
                        {
                            VersionText = $"v{version}",
                            ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                            Version = version,
                            ReleaseNotes = release.Body,
                            IsCurrentVersion = version.Equals(_currentVersion),
                            ReleaseDto = release
                        };

                        // Format release date
                        if (!string.IsNullOrWhiteSpace(release.PublishedAt))
                        {
                            if (DateTime.TryParse(release.PublishedAt, out var publishedDate))
                            {
                                releaseItem.ReleaseDate = publishedDate.ToString("yyyy-MM-dd");
                            }
                            else
                            {
                                releaseItem.ReleaseDate = "Unknown date";
                            }
                        }
                        else
                        {
                            releaseItem.ReleaseDate = "Unknown date";
                        }

                        _releases.Add(releaseItem);
                    }

                    // Show UI
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    ReleasesPanel.Visibility = Visibility.Visible;
                    CurrentVersionText.Text = $"Current version: {_currentVersion}";
                });
            }
            catch (TaskCanceledException)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowError("Request was cancelled.");
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowError($"Error loading releases: {ex.Message}");
                });
            }
        }

        private static Version? ParseVersionFromTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var trimmed = tag.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..];

            if (Version.TryParse(trimmed, out var version))
                return version;

            return null;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ReleasesPanel.Visibility = Visibility.Collapsed;
            ReleaseNotesPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
            InstallBtn.IsEnabled = false;
        }

        private void RetryBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadReleasesAsync();
        }

        private void ReleasesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReleasesListView.SelectedItem is ReleaseItem selectedItem)
            {
                SelectedVersionInfo = _updateService.ConvertReleaseToUpdateInfo(selectedItem.ReleaseDto);
                
                // Show release notes
                if (!string.IsNullOrWhiteSpace(selectedItem.ReleaseNotes))
                {
                    ReleaseNotesPanel.Visibility = Visibility.Visible;
                    ReleaseNotesText.Text = selectedItem.ReleaseNotes;
                }
                else
                {
                    ReleaseNotesPanel.Visibility = Visibility.Collapsed;
                }

                // Enable install button
                InstallBtn.IsEnabled = true;
                
                // Update button text based on whether it's an upgrade or downgrade
                if (selectedItem.Version > _currentVersion)
                {
                    InstallBtn.Content = "Install Update";
                }
                else if (selectedItem.Version < _currentVersion)
                {
                    InstallBtn.Content = "Install (Downgrade)";
                }
                else
                {
                    InstallBtn.Content = "Reinstall Current Version";
                }
            }
            else
            {
                SelectedVersionInfo = null;
                ReleaseNotesPanel.Visibility = Visibility.Collapsed;
                InstallBtn.IsEnabled = false;
            }
        }

        private void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVersionInfo != null)
            {
                DialogResult = true;
                Close();
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            base.OnClosed(e);
        }
    }
}

