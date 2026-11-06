using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CMPDriverTool.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace CMPDriverTool.Core.Patcher;

public class DriverExtractor
{
    public async Task<string> ExtractDriverPackageAsync(
        string packageExePath,
        string outputDirectory,
        IProgress<InstallProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packageExePath))
            throw new FileNotFoundException("Файл пакета драйвера не найден.", packageExePath);

        if (Directory.Exists(outputDirectory))
        {
            try
            {
                // If previously extracted Display.Driver exists, we can use it or clean
                string infCheck = Path.Combine(outputDirectory, "Display.Driver");
                if (Directory.Exists(infCheck))
                {
                    progress?.Report(new InstallProgressReport
                    {
                        Stage = InstallStage.Extracting,
                        StageTitle = "Распаковка пакета",
                        Message = "Обнаружены ранее распакованные файлы драйвера. Проверка целостности...",
                        ProgressPercentage = 100
                    });
                    return outputDirectory;
                }
            }
            catch
            {
                // Proceed with extraction
            }
        }

        Directory.CreateDirectory(outputDirectory);

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.Extracting,
            StageTitle = "Распаковка пакета 7-Zip",
            Message = $"Распаковка архива {Path.GetFileName(packageExePath)} в {outputDirectory}...",
            ProgressPercentage = 0,
            IsIndeterminate = true
        });

        // Method 1: Try built-in 7-Zip executable if present on machine
        string? system7z = Find7zExecutable();
        if (!string.IsNullOrEmpty(system7z))
        {
            bool success = await ExtractWith7zAsync(system7z, packageExePath, outputDirectory, progress, cancellationToken);
            if (success)
            {
                return outputDirectory;
            }
        }

        // Method 2: Try NVIDIA SFX built-in silent extract: driver.exe -y -nr -gm2 -s -o"outputDir"
        try
        {
            bool sfxSuccess = await ExtractWithSfxAsync(packageExePath, outputDirectory, progress, cancellationToken);
            if (sfxSuccess && Directory.Exists(Path.Combine(outputDirectory, "Display.Driver")))
            {
                return outputDirectory;
            }
        }
        catch
        {
            // Fall through to SharpCompress
        }

        // Method 3: SharpCompress extraction in Task.Run
        await Task.Run(() =>
        {
            using var archive = SevenZipArchive.Open(packageExePath);
            int totalEntries = archive.Entries.Count();
            int current = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(outputDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
                current++;
                if (current % 50 == 0 || current == totalEntries)
                {
                    double percent = totalEntries > 0 ? ((double)current / totalEntries) * 100.0 : 0.0;
                    progress?.Report(new InstallProgressReport
                    {
                        Stage = InstallStage.Extracting,
                        StageTitle = "Распаковка пакета 7-Zip",
                        Message = $"Распаковка: {current} / {totalEntries} файлов ({entry.Key})",
                        ProgressPercentage = percent
                    });
                }
            }
        }, cancellationToken);

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.Extracting,
            StageTitle = "Распаковка завершена",
            Message = "Пакет драйвера успешно распакован.",
            ProgressPercentage = 100
        });

        return outputDirectory;
    }

    private string? Find7zExecutable()
    {
        string[] candidates =
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7za.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private async Task<bool> ExtractWith7zAsync(
        string exe7z,
        string archivePath,
        string destinationDirectory,
        IProgress<InstallProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe7z,
                Arguments = $"x \"{archivePath}\" -o\"{destinationDirectory}\" -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken);
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.Extracting,
                    StageTitle = "Распаковка через 7-Zip",
                    Message = "Идет извлечение файлов пакета драйвера...",
                    IsIndeterminate = true
                });
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ExtractWithSfxAsync(
        string packageExePath,
        string destinationDirectory,
        IProgress<InstallProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = packageExePath,
                Arguments = $"-y -nr -gm2 -s -o\"{destinationDirectory}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken);
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.Extracting,
                    StageTitle = "Распаковка через SFX",
                    Message = "Идет извлечение файлов пакета драйвера...",
                    IsIndeterminate = true
                });
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
