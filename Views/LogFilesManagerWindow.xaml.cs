using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SuspensionPCB_CAN_WPF.Services;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Views
{
    public partial class LogFilesManagerWindow : Window
    {
        public class LogFileInfo
        {
            public bool IsSelected { get; set; }
            public string FileName { get; set; } = "";
            public string FileType { get; set; } = "";
            public long SizeBytes { get; set; }
            public string SizeText { get; set; } = "";
            public DateTime Created { get; set; }
            public string CreatedText { get; set; } = "";
            public string FullPath { get; set; } = "";
        }

        private ObservableCollection<LogFileInfo> _allFiles = new ObservableCollection<LogFileInfo>();
        private ObservableCollection<LogFileInfo> _filteredFiles = new ObservableCollection<LogFileInfo>();

        public LogFilesManagerWindow()
        {
            InitializeComponent();
            FilesDataGrid.ItemsSource = _filteredFiles;
            LoadFiles();
        }

        private void LoadFiles()
        {
            try
            {
                _allFiles.Clear();
                _filteredFiles.Clear();

                // Get all log directories
                string dataDir = SettingsManager.Instance.Settings.SaveDirectory;
                string logsDir = PathHelper.GetLogsDirectory();

                // Load CSV data log files from Data directory
                if (Directory.Exists(dataDir))
                {
                    var csvFiles = Directory.GetFiles(dataDir, "suspension_log_*.csv", SearchOption.TopDirectoryOnly);
                    foreach (var file in csvFiles)
                    {
                        AddFileInfo(file, "Data Log (CSV)");
                    }
                }

                // Load production log files from Logs directory
                if (Directory.Exists(logsDir))
                {
                    var txtFiles = Directory.GetFiles(logsDir, "suspension_log_*.txt", SearchOption.TopDirectoryOnly);
                    foreach (var file in txtFiles)
                    {
                        AddFileInfo(file, "Production Log (TXT)");
                    }
                }

                // Load CAN monitor export files (check both directories for can_monitor_*.csv)
                if (Directory.Exists(dataDir))
                {
                    var canFiles = Directory.GetFiles(dataDir, "can_monitor_*.csv", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(dataDir, "can_monitor_*.txt", SearchOption.TopDirectoryOnly));
                    foreach (var file in canFiles)
                    {
                        AddFileInfo(file, "CAN Monitor Export");
                    }
                }

                // Sort by creation date (newest first)
                var sorted = _allFiles.OrderByDescending(f => f.Created).ToList();
                _allFiles.Clear();
                foreach (var file in sorted)
                {
                    _allFiles.Add(file);
                }

                ApplyFilter();
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddFileInfo(string filePath, string fileType)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return;

                var logFile = new LogFileInfo
                {
                    FileName = fileInfo.Name,
                    FileType = fileType,
                    SizeBytes = fileInfo.Length,
                    SizeText = FormatFileSize(fileInfo.Length),
                    Created = fileInfo.CreationTime,
                    CreatedText = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    FullPath = filePath
                };

                _allFiles.Add(logFile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding file info: {ex.Message}");
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        private void ApplyFilter()
        {
            try
            {
                _filteredFiles.Clear();

                string filterType = "All";
                if (FileTypeFilterCombo?.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    filterType = item.Tag.ToString() ?? "All";
                }

                foreach (var file in _allFiles)
                {
                    bool include = true;

                    if (filterType == "CSV")
                    {
                        include = file.FileType.Contains("CSV");
                    }
                    else if (filterType == "TXT")
                    {
                        include = file.FileType.Contains("TXT");
                    }
                    else if (filterType == "CAN")
                    {
                        include = file.FileType.Contains("CAN Monitor");
                    }

                    if (include)
                    {
                        _filteredFiles.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Filter error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStatistics()
        {
            try
            {
                int totalFiles = _filteredFiles.Count;
                long totalSize = _filteredFiles.Sum(f => f.SizeBytes);

                if (FileCountTxt != null)
                {
                    FileCountTxt.Text = $"{totalFiles} file{(totalFiles == 1 ? "" : "s")}";
                }

                if (TotalSizeTxt != null)
                {
                    TotalSizeTxt.Text = $"Total size: {FormatFileSize(totalSize)}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStatistics error: {ex.Message}");
            }
        }

        private void FileTypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
            UpdateStatistics();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadFiles();
        }

        private void FilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update delete button state based on selection
            try
            {
                bool hasSelection = _filteredFiles.Any(f => f.IsSelected);
                if (DeleteSelectedBtn != null)
                {
                    DeleteSelectedBtn.IsEnabled = hasSelection;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Selection changed error: {ex.Message}");
            }
        }

        private void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedFiles = _filteredFiles.Where(f => f.IsSelected).ToList();
                if (selectedFiles.Count == 0)
                {
                    MessageBox.Show("Please select files to delete.", "No Selection", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string message = $"Are you sure you want to delete {selectedFiles.Count} file(s)?\n\n" +
                               "This action cannot be undone.";
                var result = MessageBox.Show(message, "Confirm Delete", 
                                            MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    int deleted = 0;
                    int failed = 0;

                    foreach (var file in selectedFiles)
                    {
                        try
                        {
                            if (File.Exists(file.FullPath))
                            {
                                File.Delete(file.FullPath);
                                deleted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            System.Diagnostics.Debug.WriteLine($"Error deleting {file.FileName}: {ex.Message}");
                        }
                    }

                    string resultMessage = $"Deleted {deleted} file(s).";
                    if (failed > 0)
                    {
                        resultMessage += $"\n{failed} file(s) could not be deleted.";
                    }

                    MessageBox.Show(resultMessage, "Delete Complete", 
                                  MessageBoxButton.OK, 
                                  failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    LoadFiles();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearAllBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int totalFiles = _filteredFiles.Count;
                if (totalFiles == 0)
                {
                    MessageBox.Show("No files to clear.", "Clear Cache", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string message = $"Are you sure you want to delete ALL {totalFiles} file(s)?\n\n" +
                               "This will clear all log files and cannot be undone.";
                var result = MessageBox.Show(message, "Confirm Clear All", 
                                            MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    int deleted = 0;
                    int failed = 0;

                    foreach (var file in _filteredFiles.ToList())
                    {
                        try
                        {
                            if (File.Exists(file.FullPath))
                            {
                                File.Delete(file.FullPath);
                                deleted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            System.Diagnostics.Debug.WriteLine($"Error deleting {file.FileName}: {ex.Message}");
                        }
                    }

                    string resultMessage = $"Cleared {deleted} file(s).";
                    if (failed > 0)
                    {
                        resultMessage += $"\n{failed} file(s) could not be deleted.";
                    }

                    MessageBox.Show(resultMessage, "Clear Complete", 
                                  MessageBoxButton.OK, 
                                  failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    LoadFiles();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Clear error: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dataDir = SettingsManager.Instance.Settings.SaveDirectory;
                if (Directory.Exists(dataDir))
                {
                    System.Diagnostics.Process.Start("explorer.exe", dataDir);
                }
                else
                {
                    MessageBox.Show("Data directory does not exist.", "Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening folder: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

