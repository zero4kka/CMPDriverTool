using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.Core.Manifest;

public class ManifestManager
{
    private readonly HttpClient _httpClient;
    private DriverManifestRoot? _cachedManifest;

    public ManifestManager(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CMPDriverTool", "Downloads");

    public static string DefaultWorkDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CMPDriverTool", "Work");

    public async Task<List<DriverManifestItem>> GetAvailableDriversAsync(string? onlineUrl = null)
    {
        if (_cachedManifest != null && _cachedManifest.Drivers.Count > 0)
        {
            return _cachedManifest.Drivers;
        }

        // 1. Try reading from working directory or app base directory
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers.json");
        if (File.Exists(localPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(localPath);
                var root = JsonSerializer.Deserialize<DriverManifestRoot>(json);
                if (root != null && root.Drivers.Count > 0)
                {
                    _cachedManifest = root;
                    return root.Drivers;
                }
            }
            catch
            {
                // Fallback to embedded
            }
        }

        // 2. Try embedded resource from assembly
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("CMPDriverTool.Core.Manifest.drivers.json");
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync();
                var root = JsonSerializer.Deserialize<DriverManifestRoot>(json);
                if (root != null && root.Drivers.Count > 0)
                {
                    _cachedManifest = root;
                    return root.Drivers;
                }
            }
        }
        catch
        {
            // Ignore
        }

        // 3. Optional online update if requested
        if (!string.IsNullOrEmpty(onlineUrl))
        {
            try
            {
                string onlineJson = await _httpClient.GetStringAsync(onlineUrl);
                var root = JsonSerializer.Deserialize<DriverManifestRoot>(onlineJson);
                if (root != null && root.Drivers.Count > 0)
                {
                    _cachedManifest = root;
                    return root.Drivers;
                }
            }
            catch
            {
                // Fall back
            }
        }

        // 4. Default hardcoded fallback if all else fails
        var fallback = GetHardcodedFallback();
        _cachedManifest = fallback;
        return fallback.Drivers;
    }

    public static string GetExpectedPackagePath(DriverManifestItem driver)
    {
        string fileName = $"NVIDIA-{driver.Version}-{driver.Branch.Replace(" ", "")}-x64.exe";
        return Path.Combine(DefaultCacheDirectory, fileName);
    }

    public static bool IsDriverDownloaded(DriverManifestItem driver)
    {
        string path = GetExpectedPackagePath(driver);
        if (!File.Exists(path)) return false;
        var fi = new FileInfo(path);
        // Valid if file is larger than 100 MB
        return fi.Length > 100 * 1024 * 1024;
    }

    private static DriverManifestRoot GetHardcodedFallback()
    {
        return new DriverManifestRoot
        {
            LastUpdated = "2026-11-01",
            Drivers = new List<DriverManifestItem>
            {
                new()
                {
                    Version = "552.22",
                    Name = "NVIDIA GeForce Game Ready Driver",
                    Branch = "Game Ready",
                    ReleaseDate = "2024-04-16",
                    DownloadUrl = "https://ru.download.nvidia.com/Windows/552.22/552.22-desktop-win10-win11-64bit-international-dch-whql.exe",
                    SizeMb = 668.5,
                    Description = "Рекомендуемая стабильная версия для CMP 30HX / 40HX.",
                    IsRecommended = true
                },
                new()
                {
                    Version = "537.58",
                    Name = "NVIDIA GeForce Game Ready Driver",
                    Branch = "Game Ready",
                    ReleaseDate = "2023-10-10",
                    DownloadUrl = "https://ru.download.nvidia.com/Windows/537.58/537.58-desktop-win10-win11-64bit-international-dch-whql.exe",
                    SizeMb = 655.2,
                    Description = "Проверенная временем версия с минимальным инпут-лагом.",
                    IsRecommended = false
                }
            }
        };
    }
}
