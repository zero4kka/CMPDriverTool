using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.Core.Downloader;

public class DriverDownloader
{
    private readonly HttpClient _httpClient;

    public DriverDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromHours(1) };
    }

    public async Task<string> DownloadDriverAsync(
        DriverManifestItem item,
        string destinationDirectory,
        IProgress<InstallProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        string fileName = $"NVIDIA-{item.Version}-{item.Branch.Replace(" ", "")}-x64.exe";
        string targetFilePath = Path.Combine(destinationDirectory, fileName);
        string tempFilePath = targetFilePath + ".tmp";

        // Check if already downloaded and verified
        if (File.Exists(targetFilePath))
        {
            var existingFi = new FileInfo(targetFilePath);
            if (existingFi.Length > 100 * 1024 * 1024)
            {
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.Downloading,
                    StageTitle = "Загрузка драйвера",
                    Message = $"Файл драйвера v{item.Version} уже загружен и готов к использованию: {targetFilePath}",
                    ProgressPercentage = 100
                });
                return targetFilePath;
            }
        }

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.Downloading,
            StageTitle = "Загрузка драйвера",
            Message = $"Подключение к серверу NVIDIA для загрузки v{item.Version}...",
            ProgressPercentage = 0,
            IsIndeterminate = true
        });

        using var response = await _httpClient.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        long totalReadBytes = 0;
        byte[] buffer = new byte[81920]; // 80 KB buffer

        var stopwatch = Stopwatch.StartNew();
        long lastSpeedBytes = 0;
        double currentSpeedMbPerSec = 0;
        var speedTimer = Stopwatch.StartNew();

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalReadBytes += read;

                if (speedTimer.ElapsedMilliseconds >= 500)
                {
                    long bytesDelta = totalReadBytes - lastSpeedBytes;
                    double sec = speedTimer.Elapsed.TotalSeconds;
                    currentSpeedMbPerSec = (bytesDelta / (1024.0 * 1024.0)) / (sec > 0 ? sec : 1.0);
                    lastSpeedBytes = totalReadBytes;
                    speedTimer.Restart();

                    double percent = totalBytes.HasValue && totalBytes.Value > 0
                        ? ((double)totalReadBytes / totalBytes.Value) * 100.0
                        : 0.0;

                    string mbRead = (totalReadBytes / (1024.0 * 1024.0)).ToString("F1");
                    string mbTotal = totalBytes.HasValue ? (totalBytes.Value / (1024.0 * 1024.0)).ToString("F1") : "?";

                    progress?.Report(new InstallProgressReport
                    {
                        Stage = InstallStage.Downloading,
                        StageTitle = "Загрузка драйвера",
                        Message = $"Загрузка: {mbRead} МБ / {mbTotal} МБ ({currentSpeedMbPerSec:F1} МБ/с)",
                        ProgressPercentage = Math.Min(percent, 100),
                        IsIndeterminate = !totalBytes.HasValue
                    });
                }
            }
        }

        if (File.Exists(targetFilePath))
        {
            File.Delete(targetFilePath);
        }
        File.Move(tempFilePath, targetFilePath);

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.Downloading,
            StageTitle = "Загрузка завершена",
            Message = $"Файл драйвера успешно загружен ({totalReadBytes / (1024 * 1024)} МБ)",
            ProgressPercentage = 100
        });

        return targetFilePath;
    }
}
