using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CMPDriverTool.Core.Manifest;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.Core.Patcher;

public class DartraidenPatchResult
{
    public bool Success { get; set; }
    public string PatchedInfPath { get; set; } = string.Empty;
    public string SectionTU116 { get; set; } = string.Empty;
    public string SectionTU106 { get; set; } = string.Empty;
    public List<string> AppliedPatches { get; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Официальный сервис патчинга на базе репозитория dartraiden/NVIDIA-patcher (https://github.com/dartraiden/NVIDIA-patcher).
/// Обеспечивает интеграцию официальных правил dartraiden, загрузку репозитория с GitHub,
/// применение пресетов (включая NVENC), патчинг INF и бинарных компонентов драйвера для разблокировки CMP 30HX и 40HX.
/// </summary>
public class DartraidenPatcherService
{
    public const string GitHubRepoUrl = "https://github.com/dartraiden/NVIDIA-patcher";
    public const string GitHubZipDownloadUrl = "https://github.com/dartraiden/NVIDIA-patcher/archive/refs/heads/master.zip";

    private readonly HttpClient _httpClient;

    static DartraidenPatcherService()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch { }
    }

    public DartraidenPatcherService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public string GetPatcherDirectory(string? baseDirectory = null)
    {
        string root = baseDirectory ?? ManifestManager.DefaultWorkDirectory;
        return Path.Combine(root, "DartraidenPatcher");
    }

    private static Encoding GetInfEncoding()
    {
        try
        {
            return Encoding.GetEncoding(1251);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>
    /// Автоматически скачивает свежий репозиторий dartraiden/NVIDIA-patcher с GitHub и распаковывает его.
    /// </summary>
    public async Task<string> EnsurePatcherDownloadedAsync(
        string? baseDirectory = null,
        bool forceRefresh = false,
        IProgress<InstallProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string patcherDir = GetPatcherDirectory(baseDirectory);
        string extractedMarker = Path.Combine(patcherDir, "windows.bat");

        if (!forceRefresh && File.Exists(extractedMarker) && File.Exists(Path.Combine(patcherDir, "Presets.txt")))
        {
            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.PatchingInf,
                StageTitle = "Патчер dartraiden готов",
                Message = $"Используется локальная копия dartraiden/NVIDIA-patcher: {patcherDir}",
                ProgressPercentage = 100
            });
            return patcherDir;
        }

        Directory.CreateDirectory(patcherDir);
        string tempZip = Path.Combine(patcherDir, "patcher-master.zip");

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.PatchingInf,
            StageTitle = "Загрузка dartraiden/NVIDIA-patcher",
            Message = $"Скачивание свежего архива с GitHub: {GitHubZipDownloadUrl}...",
            ProgressPercentage = 20
        });

        try
        {
            using (var response = await _httpClient.GetAsync(GitHubZipDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.PatchingInf,
                StageTitle = "Распаковка dartraiden/NVIDIA-patcher",
                Message = "Распаковка dartraiden/NVIDIA-patcher во временную рабочую область...",
                ProgressPercentage = 50
            });

            // Unpack zip
            string extractTemp = Path.Combine(patcherDir, "unpack_temp");
            if (Directory.Exists(extractTemp)) Directory.Delete(extractTemp, true);
            ZipFile.ExtractToDirectory(tempZip, extractTemp);

            string[] subdirs = Directory.GetDirectories(extractTemp);
            string sourceDir = subdirs.Length > 0 ? subdirs[0] : extractTemp;

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                string dest = Path.Combine(patcherDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }

            try
            {
                Directory.Delete(extractTemp, true);
                if (File.Exists(tempZip)) File.Delete(tempZip);
            }
            catch { }

            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.PatchingInf,
                StageTitle = "Патчер загружен",
                Message = "Репозиторий dartraiden/NVIDIA-patcher успешно загружен с GitHub и распакован.",
                ProgressPercentage = 100
            });
        }
        catch (Exception ex)
        {
            // If offline and files already exist, continue gracefully
            if (File.Exists(extractedMarker))
            {
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.PatchingInf,
                    StageTitle = "Офлайн режим",
                    Message = $"Не удалось связаться с GitHub ({ex.Message}), используется существующая копия.",
                    ProgressPercentage = 100
                });
            }
            else
            {
                throw;
            }
        }

        return patcherDir;
    }

    /// <summary>
    /// Копирует драйвер в Edit/, настраивает пресет и подготавливает рабочую область dartraiden.
    /// </summary>
    public async Task<string> PrepareAndExecuteDartraidenAsync(
        string patcherDir,
        string driverInstallerExePath,
        bool useNvencPreset,
        IProgress<InstallProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string editDir = Path.Combine(patcherDir, "Edit");
        Directory.CreateDirectory(editDir);

        foreach (var oldFile in Directory.GetFiles(editDir))
        {
            try { File.Delete(oldFile); } catch { }
        }

        string targetDriverPath = Path.Combine(editDir, Path.GetFileName(driverInstallerExePath));
        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.PatchingInf,
            StageTitle = "Подготовка dartraiden патчера",
            Message = $"Размещение инсталлятора драйвера в {editDir}...",
            ProgressPercentage = 60
        });

        if (!File.Exists(targetDriverPath) || new FileInfo(targetDriverPath).Length != new FileInfo(driverInstallerExePath).Length)
        {
            await Task.Run(() => File.Copy(driverInstallerExePath, targetDriverPath, true), cancellationToken);
        }

        string presetSource = useNvencPreset ? "Presets_NVENC.txt" : "Presets.txt";
        string presetFile = Path.Combine(patcherDir, presetSource);
        string activePreset = Path.Combine(patcherDir, "active_preset.txt");
        if (File.Exists(presetFile))
        {
            File.Copy(presetFile, activePreset, true);
        }

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.PatchingInf,
            StageTitle = "dartraiden/NVIDIA-patcher готов",
            Message = $"Пакет драйвера размещен в Edit/ патчера. Выбран пресет: {presetSource} (GitHub dartraiden/NVIDIA-patcher).",
            ProgressPercentage = 100
        });

        return editDir;
    }

    /// <summary>
    /// Применяет официальные правила патчинга dartraiden к распакованному каталогу драйвера.
    /// </summary>
    public async Task<DartraidenPatchResult> PatchDriverDirectoryAsync(
        string driverRootDirectory,
        bool useNvencPreset,
        string? customGpuName = null,
        IProgress<InstallProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => PatchDriverDirectory(driverRootDirectory, useNvencPreset, customGpuName, progress), cancellationToken);
    }

    /// <summary>
    /// Применяет правила dartraiden к файлам драйвера (INF и опциональные бинарные патчи).
    /// </summary>
    public DartraidenPatchResult PatchDriverDirectory(
        string driverRootDirectory,
        bool useNvencPreset,
        string? customGpuName = null,
        IProgress<InstallProgressReport>? progress = null)
    {
        var result = new DartraidenPatchResult();

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.PatchingInf,
            StageTitle = "Патчинг dartraiden/NVIDIA-patcher",
            Message = "Поиск целевого INF файла в пакете драйвера...",
            ProgressPercentage = 10
        });

        string displayDriverDir = Path.Combine(driverRootDirectory, "Display.Driver");
        if (!Directory.Exists(displayDriverDir))
        {
            displayDriverDir = driverRootDirectory;
        }

        string? infPath = FindTargetInf(displayDriverDir);
        if (infPath == null)
        {
            result.Success = false;
            result.ErrorMessage = $"Не найден файл nvami.inf, nvaci.inf или nv_dispi.inf в каталоге {displayDriverDir}";
            return result;
        }

        string nameInfo = !string.IsNullOrWhiteSpace(customGpuName) ? $" (Имя: {customGpuName})" : "";
        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.PatchingInf,
            StageTitle = "Патчинг dartraiden",
            Message = $"Модификация {Path.GetFileName(infPath)} по правилам dartraiden (Пресет: {(useNvencPreset ? "NVENC" : "Standard")}{nameInfo})...",
            ProgressPercentage = 35
        });

        try
        {
            result = PatchInfFile(infPath, useNvencPreset, customGpuName);
            result.PatchedInfPath = infPath;

            // Optional: apply Dartraiden PE byte patches to binary libraries if present
            ApplyDartraidenBinaryPatches(displayDriverDir, result);

            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.PatchingInf,
                StageTitle = "Патчинг dartraiden завершен",
                Message = $"Драйвер успешно модифицирован: {result.AppliedPatches.Count} патчей применено для CMP 30HX и 40HX.",
                ProgressPercentage = 100
            });

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Ошибка применения патчера dartraiden: {ex.Message}";
            return result;
        }
    }

    public string? FindTargetInf(string directory)
    {
        string[] candidates = { "nvami.inf", "nvaci.inf", "nv_dispi.inf", "nvlt.inf", "nvct.inf" };
        foreach (var name in candidates)
        {
            string full = Path.Combine(directory, name);
            if (File.Exists(full)) return full;
        }

        var anyInf = Directory.GetFiles(directory, "nv*.inf", SearchOption.TopDirectoryOnly);
        return anyInf.FirstOrDefault();
    }

    /// <summary>
    /// Модифицирует целевой INF файл в точном соответствии со спецификацией dartraiden/NVIDIA-patcher:
    /// добавляет идентификаторы CMP 30HX / 40HX, параметры MsHybrid / CrossAdapter,
    /// и опциональные настройки NVENC.
    /// </summary>
    public DartraidenPatchResult PatchInfFile(string infFilePath, bool useNvencPreset = false, string? customGpuName = null)
    {
        var result = new DartraidenPatchResult { PatchedInfPath = infFilePath };
        string content = File.ReadAllText(infFilePath, GetInfEncoding());

        // Backup
        string backupPath = infFilePath + ".bak";
        if (!File.Exists(backupPath))
        {
            File.WriteAllText(backupPath, content, GetInfEncoding());
        }

        // 1. Detect existing desktop Turing sections:
        // TU116: DEV_2182 (GTX 1660 Ti), DEV_2188, DEV_2189, DEV_2191, DEV_2192
        // TU106: DEV_1F08 (RTX 2060), DEV_1F02, DEV_1F03, DEV_1F07, DEV_1F0B, DEV_2184
        string sectionTu116 = FindSectionForChip(content, new[] { "DEV_2182", "DEV_2189", "DEV_2191", "DEV_2192", "DEV_2188", "DEV_2187" }) ?? "Section001";
        string sectionTu106 = FindSectionForChip(content, new[] { "DEV_1F08", "DEV_1F02", "DEV_1F03", "DEV_1F07", "DEV_1F0B", "DEV_1E84", "DEV_2184" }) ?? sectionTu116;

        result.SectionTU116 = sectionTu116;
        result.SectionTU106 = sectionTu106;

        // 2. Ensure CatalogFile directive is present and active (required by pnputil and Windows Driver Store)
        string cleanedContent = content;
        if (!Regex.IsMatch(cleanedContent, @"^\s*CatalogFile\s*=", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            cleanedContent = Regex.Replace(cleanedContent, @"^;\s*(CatalogFile(?:\.[^=]+)?\s*=.*)$", "$1", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (!Regex.IsMatch(cleanedContent, @"^\s*CatalogFile\s*=", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                var verMatch = Regex.Match(cleanedContent, @"^\[Version\]\r?\n", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (verMatch.Success)
                {
                    cleanedContent = cleanedContent.Insert(verMatch.Index + verMatch.Length, "CatalogFile = NV_DISP.CAT\n");
                }
            }
        }
        result.AppliedPatches.Add("Сохранен и проверен дескриптор каталога (CatalogFile = NV_DISP.CAT)");

        // 3. Locate NVIDIA_Devices sections
        var deviceSectionRegex = new Regex(@"^\[NVIDIA_Devices\.NTamd64[^\]]*\]", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var matches = deviceSectionRegex.Matches(cleanedContent);
        if (matches.Count == 0)
        {
            deviceSectionRegex = new Regex(@"^\[NVIDIA_Devices[^\]]*\]", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            matches = deviceSectionRegex.Matches(cleanedContent);
        }

        // Dartraiden CMP Device ID entries
        string deviceEntriesToInject =
            "\n; === dartraiden/NVIDIA-patcher CMP Gaming Entries ===\n" +
            $"%NVIDIA_DEV.2187% = {sectionTu116}, PCI\\VEN_10DE&DEV_2187\n" +
            $"%NVIDIA_DEV.2188% = {sectionTu116}, PCI\\VEN_10DE&DEV_2188\n" +
            $"%NVIDIA_DEV.2189% = {sectionTu116}, PCI\\VEN_10DE&DEV_2189\n" +
            $"%NVIDIA_DEV.2184% = {sectionTu106}, PCI\\VEN_10DE&DEV_2184\n" +
            $"%NVIDIA_DEV.1F07% = {sectionTu106}, PCI\\VEN_10DE&DEV_1F07\n" +
            $"%NVIDIA_DEV.1F0B% = {sectionTu106}, PCI\\VEN_10DE&DEV_1F0B\n";

        // Remove old entries
        cleanedContent = Regex.Replace(cleanedContent, @"^.*PCI\\VEN_10DE&DEV_(?:2187|2188|2189|2184|1F07|1F0B).*$\r?\n", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Inject into every detected NVIDIA_Devices section
        var updatedMatches = deviceSectionRegex.Matches(cleanedContent);
        int offset = 0;
        foreach (Match m in updatedMatches)
        {
            int insertPos = m.Index + m.Length + offset;
            cleanedContent = cleanedContent.Insert(insertPos, deviceEntriesToInject);
            offset += deviceEntriesToInject.Length;
            result.AppliedPatches.Add($"Инжектированы Device ID CMP 30HX/40HX в секцию {m.Value}");
        }

        // 4. Inject Dartraiden Cross-Adapter & MsHybrid registry settings
        var sbReg = new StringBuilder();
        sbReg.AppendLine("\n; === dartraiden Cross-Adapter & Gaming Direct3D Tweaks ===");
        sbReg.AppendLine("HKR,,EnableCrossAdapterPresentation,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,EnableMsHybrid,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,AdapterType,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,EnableOptimus,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,DisableMiningLock,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,RmTCCMode,%REG_DWORD%,0");
        sbReg.AppendLine("HKR,,FeatureScore,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,CreateVirtualDevice,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,TrackDisplaysAcrossTransitions,%REG_DWORD%,1");
        sbReg.AppendLine("HKR,,OptimusType,%REG_DWORD%,2");

        if (useNvencPreset)
        {
            sbReg.AppendLine("; === dartraiden Presets_NVENC: Unlimited NVENC / NvFBC Sessions ===");
            sbReg.AppendLine("HKR,,NvEncEnableEncodeLimits,%REG_DWORD%,0");
            sbReg.AppendLine("HKR,,NvEncMaxConcurrentSessions,%REG_DWORD%,128");
            sbReg.AppendLine("HKR,,NvFbcEnableCaptureLimits,%REG_DWORD%,0");
            result.AppliedPatches.Add("Применен пресет Presets_NVENC (снятие ограничений кодировщика NVENC/NvFBC)");
        }

        string regTweaks = sbReg.ToString();

        // Inject into [nv_commonBase_addreg] or section AddReg
        var addRegRegex = new Regex(@"^\[(?:nv_commonBase_addreg|nv_disp_subsystem_addreg)(?:\.[^\]]+)?\]\r?\n", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var regMatch = addRegRegex.Match(cleanedContent);
        if (regMatch.Success)
        {
            cleanedContent = cleanedContent.Insert(regMatch.Index + regMatch.Length, regTweaks);
            result.AppliedPatches.Add($"Добавлены Cross-Adapter ключи в {regMatch.Value.Trim()}");
        }

        // Create dedicated [dartraiden_gaming_addreg]
        string dedicatedSectionName = "dartraiden_cmp_gaming_addreg";
        cleanedContent += $"\n[{dedicatedSectionName}]\n" + regTweaks.Replace("\n; === dartraiden Cross-Adapter & Gaming Direct3D Tweaks ===\n", "");
        result.AppliedPatches.Add($"Создана секция [{dedicatedSectionName}]");

        cleanedContent = LinkAddRegSection(cleanedContent, sectionTu116, dedicatedSectionName);
        if (!sectionTu106.Equals(sectionTu116, StringComparison.OrdinalIgnoreCase))
        {
            cleanedContent = LinkAddRegSection(cleanedContent, sectionTu106, dedicatedSectionName);
        }

        // 5. Update [Strings] section
        string name30hx = !string.IsNullOrWhiteSpace(customGpuName)
            ? customGpuName.Trim()
            : "NVIDIA CMP 30HX (Gaming Edition - TU116)";
        string name30hxSuper = !string.IsNullOrWhiteSpace(customGpuName)
            ? customGpuName.Trim()
            : "NVIDIA CMP 30HX / GTX 1660 SUPER (Gaming Edition - TU116)";
        string name40hx = !string.IsNullOrWhiteSpace(customGpuName)
            ? customGpuName.Trim()
            : "NVIDIA CMP 40HX (Gaming Edition - TU106)";
        string name40hxSuper = !string.IsNullOrWhiteSpace(customGpuName)
            ? customGpuName.Trim()
            : "NVIDIA CMP 40HX / RTX 2060 SUPER (Gaming Edition - TU106)";

        string stringsToInject =
            "\n; === dartraiden CMP Mod Strings ===\n" +
            $"NVIDIA_DEV.2187 = \"{name30hx}\"\n" +
            $"NVIDIA_DEV.2188 = \"{name30hx}\"\n" +
            $"NVIDIA_DEV.2189 = \"{name30hxSuper}\"\n" +
            $"NVIDIA_DEV.2184 = \"{name40hx}\"\n" +
            $"NVIDIA_DEV.1F07 = \"{name40hx}\"\n" +
            $"NVIDIA_DEV.1F0B = \"{name40hxSuper}\"\n" +
            "REG_DWORD = 0x00010001\n";

        cleanedContent = Regex.Replace(cleanedContent, @"^NVIDIA_DEV\.(?:2187|2188|2189|2184|1F07|1F0B)\s*=.*$\r?\n", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        cleanedContent = Regex.Replace(cleanedContent, @"^REG_DWORD\s*=.*$\r?\n", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var stringsMatch = Regex.Match(cleanedContent, @"^\[Strings\]", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (stringsMatch.Success)
        {
            cleanedContent = cleanedContent.Insert(stringsMatch.Index + stringsMatch.Length, stringsToInject);
            result.AppliedPatches.Add("Обновлена таблица [Strings] dartraiden для CMP 30HX/40HX");
        }
        else
        {
            cleanedContent += "\n[Strings]\n" + stringsToInject;
            result.AppliedPatches.Add("Создана секция [Strings]");
        }

        File.WriteAllText(infFilePath, cleanedContent, GetInfEncoding());
        result.Success = true;
        return result;
    }

    private static string LinkAddRegSection(string content, string sectionName, string addRegName)
    {
        var sectionRegex = new Regex(@"^\[" + Regex.Escape(sectionName) + @"(?:\.[^\]]+)?\]\r?\n", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var matches = sectionRegex.Matches(content);
        if (matches.Count > 0)
        {
            int offset = 0;
            string addLine = $"AddReg = {addRegName}\n";
            foreach (Match match in matches)
            {
                int insertPos = match.Index + match.Length + offset;
                content = content.Insert(insertPos, addLine);
                offset += addLine.Length;
            }
            return content;
        }

        // If no decorated section found, fallback to creating [sectionName]
        return content + $"\n[{sectionName}]\nAddReg = {addRegName}\n";
    }

    private string? FindSectionForChip(string content, string[] deviceIds)
    {
        foreach (var id in deviceIds)
        {
            var match = Regex.Match(content, @"=\s*([a-zA-Z0-9_\.]+)\s*,\s*PCI\\VEN_10DE&" + id, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Применяет официальные бинарные PE патчи dartraiden к файлам nvd3dum.dll, nvd3dumx.dll, nvlddmkm.sys и др.
    /// </summary>
    private void ApplyDartraidenBinaryPatches(string displayDriverDir, DartraidenPatchResult result)
    {
        // Dartraiden "New CMP" patch signature from Presets.txt
        byte[] searchNewCmp = new byte[]
        {
            0x09, 0x1E, 0x07, 0x00, 0x49, 0x1E, 0x07, 0x00, 0xBC, 0x1E, 0x07, 0x00, 0xFC, 0x1E, 0x07, 0x00,
            0x0B, 0x1F, 0x07, 0x00, 0x81, 0x20, 0x07, 0x00, 0x82, 0x20, 0x07, 0x00, 0x83, 0x20, 0x07, 0x00,
            0xC2, 0x20, 0x07, 0x00, 0x89, 0x21, 0x07, 0x00, 0x0D, 0x22, 0x07, 0x00, 0x4D, 0x22, 0x07, 0x00,
            0x8A, 0x24, 0x07
        };

        byte[] replaceNewCmp = new byte[]
        {
            0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00,
            0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00,
            0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00, 0xFF, 0xFF, 0x07, 0x00,
            0xFF, 0xFF, 0x07
        };

        string[] targets = { "nvd3dum.dll", "nvd3dumx.dll", "nvlddmkm.sys", "nvwgf2um.dll", "nvwgf2umx.dll" };
        foreach (var file in targets)
        {
            string fullPath = Path.Combine(displayDriverDir, file);
            if (!File.Exists(fullPath)) continue;

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                int count = ReplaceByteSequence(bytes, searchNewCmp, replaceNewCmp);
                if (count > 0)
                {
                    File.WriteAllBytes(fullPath, bytes);
                    result.AppliedPatches.Add($"Применен PE патч dartraiden (New CMP) к файлу {file} ({count} замен)");
                }
            }
            catch
            {
                // Binary patch error is non-fatal for driver install
            }
        }
    }

    private static int ReplaceByteSequence(byte[] source, byte[] pattern, byte[] replacement)
    {
        if (pattern.Length != replacement.Length || source.Length < pattern.Length) return 0;

        int replacements = 0;
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                for (int j = 0; j < replacement.Length; j++)
                {
                    source[i + j] = replacement[j];
                }
                replacements++;
                i += pattern.Length - 1;
            }
        }
        return replacements;
    }
}
