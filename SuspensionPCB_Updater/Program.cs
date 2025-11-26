using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace SuspensionPCB_Updater
{
    internal static class Program
    {
        /// <summary>
        /// Simple external updater that replaces the main application files with a newly downloaded package
        /// and restarts the main executable.
        /// </summary>
        /// <param name="args">
        /// args[0] = target application directory (where the main EXE lives)
        /// args[1] = path to downloaded update package (.zip or .exe)
        /// args[2] = main executable file name (e.g. SuspensionPCB_CAN_WPF.exe)
        /// </param>
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: SuspensionPCB_Updater <targetDir> <packagePath> <mainExeName>");
                return 1;
            }

            string targetDir = args[0];
            string packagePath = args[1];
            string mainExeName = args[2];

            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Console.Error.WriteLine($"Target directory not found: {targetDir}");
                    return 2;
                }

                if (!File.Exists(packagePath))
                {
                    Console.Error.WriteLine($"Package not found: {packagePath}");
                    return 3;
                }

                // Give the main application some time to exit and release file locks
                Thread.Sleep(1500);

                string backupDir = Path.Combine(targetDir, "Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                try
                {
                    Directory.CreateDirectory(backupDir);
                    CopyDirectory(targetDir, backupDir, excludeUpdater: true);
                }
                catch
                {
                    // Backup failures should not block the update entirely
                }

                // Apply the update
                if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyZipUpdate(targetDir, packagePath);
                }
                else if (packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    ApplySingleFileUpdate(targetDir, packagePath, mainExeName);
                }
                else
                {
                    Console.Error.WriteLine($"Unsupported package type: {packagePath}");
                    return 4;
                }

                // Restart main application
                string mainExePath = Path.Combine(targetDir, mainExeName);
                if (File.Exists(mainExePath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = mainExePath,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Update failed: {ex}");
                return 5;
            }
        }

        private static void ApplyZipUpdate(string targetDir, string zipPath)
        {
            // Extract ZIP to a temporary directory first
            string tempDir = Path.Combine(Path.GetTempPath(), "SuspensionPCB_Update_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

                // Copy extracted files into target directory
                CopyDirectory(tempDir, targetDir, excludeUpdater: false);
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup only
                }
            }
        }

        private static void ApplySingleFileUpdate(string targetDir, string exePath, string mainExeName)
        {
            string targetExe = Path.Combine(targetDir, mainExeName);
            File.Copy(exePath, targetExe, overwrite: true);
        }

        private static void CopyDirectory(string sourceDir, string targetDir, bool excludeUpdater)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);

                // Avoid copying the updater onto itself if we are backing up
                if (excludeUpdater && fileName.Equals("SuspensionPCB_Updater.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destFile = Path.Combine(targetDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith("Backup_", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destSubDir = Path.Combine(targetDir, dirName);
                Directory.CreateDirectory(destSubDir);
                CopyDirectory(dir, destSubDir, excludeUpdater);
            }
        }
    }
}


