using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CMPDriverTool.Core.Models;
using Microsoft.Win32;

namespace CMPDriverTool.Core.Gaming;

public class GameDetector
{
    private readonly DirectXRegistryManager _registryManager;

    public GameDetector(DirectXRegistryManager? registryManager = null)
    {
        _registryManager = registryManager ?? new DirectXRegistryManager();
    }

    public List<GameProfile> DetectGames()
    {
        var gamesMap = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);

        // 1. First, check already configured preferences in Windows DirectX Registry
        var existingPrefs = _registryManager.GetAllPreferences();
        foreach (var (exePath, pref) in existingPrefs)
        {
            if (File.Exists(exePath))
            {
                string title = GuessGameTitleFromPath(exePath);
                gamesMap[exePath] = new GameProfile
                {
                    ExecutablePath = exePath,
                    Title = title,
                    Source = "Windows Registry",
                    Preference = pref
                };
            }
        }

        // 2. Scan Steam libraries
        ScanSteamGames(gamesMap, existingPrefs);

        // 3. Scan Epic Games
        ScanEpicGames(gamesMap, existingPrefs);

        return gamesMap.Values
            .OrderByDescending(g => g.IsCmpPreferred)
            .ThenBy(g => g.Title)
            .ToList();
    }

    private void ScanSteamGames(Dictionary<string, GameProfile> gamesMap, Dictionary<string, DirectXGpuPreference> existingPrefs)
    {
        var steamPaths = new List<string>();

        // Check registry for Steam install path
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam") ??
                            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam") ??
                            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key != null)
            {
                string? p = key.GetValue("SteamPath")?.ToString() ?? key.GetValue("InstallPath")?.ToString();
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                {
                    steamPaths.Add(p.Replace("/", "\\"));
                }
            }
        }
        catch { }

        // Common defaults
        string defaultSteam = @"C:\Program Files (x86)\Steam";
        if (Directory.Exists(defaultSteam) && !steamPaths.Contains(defaultSteam, StringComparer.OrdinalIgnoreCase))
        {
            steamPaths.Add(defaultSteam);
        }

        var libraryFolders = new List<string>();
        foreach (var steamRoot in steamPaths)
        {
            string common = Path.Combine(steamRoot, "steamapps", "common");
            if (Directory.Exists(common)) libraryFolders.Add(common);

            string vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    string vdfContent = File.ReadAllText(vdfPath);
                    var matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        string folder = m.Groups[1].Value.Replace(@"\\", @"\");
                        string libCommon = Path.Combine(folder, "steamapps", "common");
                        if (Directory.Exists(libCommon) && !libraryFolders.Contains(libCommon, StringComparer.OrdinalIgnoreCase))
                        {
                            libraryFolders.Add(libCommon);
                        }
                    }
                }
                catch { }
            }
        }

        foreach (var lib in libraryFolders)
        {
            try
            {
                foreach (var gameDir in Directory.GetDirectories(lib))
                {
                    string gameName = Path.GetFileName(gameDir);
                    // Search for .exe in game root and bin64/win64 subdirs (up to 2 levels)
                    var exes = Directory.GetFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.AllDirectories).Take(25));

                    foreach (var exe in exes)
                    {
                        string fileName = Path.GetFileName(exe);
                        // Filter out uninstallers, crash dumpers, redists, unity crash handlers
                        if (IsLikelyGameExecutable(fileName, exe))
                        {
                            if (!gamesMap.ContainsKey(exe))
                            {
                                existingPrefs.TryGetValue(exe, out var pref);
                                gamesMap[exe] = new GameProfile
                                {
                                    ExecutablePath = exe,
                                    Title = $"{gameName} ({fileName})",
                                    Source = "Steam",
                                    Preference = pref
                                };
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void ScanEpicGames(Dictionary<string, GameProfile> gamesMap, Dictionary<string, DirectXGpuPreference> existingPrefs)
    {
        string epicManifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(epicManifestsDir)) return;

        try
        {
            foreach (var itemFile in Directory.GetFiles(epicManifestsDir, "*.item"))
            {
                try
                {
                    string json = File.ReadAllText(itemFile);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    string installLoc = root.TryGetProperty("InstallLocation", out var pLoc) ? pLoc.GetString() ?? "" : "";
                    string launchExe = root.TryGetProperty("LaunchExecutable", out var pExe) ? pExe.GetString() ?? "" : "";
                    string appTitle = root.TryGetProperty("DisplayName", out var pName) ? pName.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(installLoc) && !string.IsNullOrEmpty(launchExe))
                    {
                        string fullPath = Path.Combine(installLoc, launchExe);
                        if (File.Exists(fullPath) && !gamesMap.ContainsKey(fullPath))
                        {
                            existingPrefs.TryGetValue(fullPath, out var pref);
                            gamesMap[fullPath] = new GameProfile
                            {
                                ExecutablePath = fullPath,
                                Title = string.IsNullOrEmpty(appTitle) ? Path.GetFileName(installLoc) : appTitle,
                                Source = "Epic Games",
                                Preference = pref
                            };
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool IsLikelyGameExecutable(string fileName, string fullPath)
    {
        string lower = fileName.ToLowerInvariant();
        if (lower.Contains("unins") || lower.Contains("setup") || lower.Contains("installer") ||
            lower.Contains("crash") || lower.Contains("report") || lower.Contains("dxsetup") ||
            lower.Contains("vcredist") || lower.Contains("update") || lower.Contains("tool") ||
            lower.Contains("patch") || lower.Contains("eac_") || lower.Contains("easyanticheat") ||
            lower.Contains("battleye") || lower.Contains("launcher_helper"))
        {
            return false;
        }

        try
        {
            var fi = new FileInfo(fullPath);
            return fi.Exists && fi.Length > 200 * 1024;
        }
        catch
        {
            return false;
        }
    }

    private static string GuessGameTitleFromPath(string exePath)
    {
        string? parent = Path.GetFileName(Path.GetDirectoryName(exePath));
        string file = Path.GetFileNameWithoutExtension(exePath);
        if (!string.IsNullOrEmpty(parent) && !parent.Equals("bin", StringComparison.OrdinalIgnoreCase) && !parent.Equals("win64", StringComparison.OrdinalIgnoreCase))
        {
            return $"{parent} ({file})";
        }
        return file;
    }
}
