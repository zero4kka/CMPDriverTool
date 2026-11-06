using System;
using System.Collections.Generic;
using System.IO;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.Core.Patcher;

public class DebloatService
{
    private static readonly string[] TelemetryAndBloatFolders =
    {
        "NvTelemetry",
        "ShieldWirelessController",
        "GFExperience",
        "GFExperience.NvStreamKms",
        "Update.Core",
        "NvVulkanSysinfo",
        "PPC",
        "ShadowPlay",
        "NodeJS",
        "NvContainer"
    };

    public List<string> CleanDriverPackage(string driverDirectory, IProgress<InstallProgressReport>? progress = null)
    {
        var removedItems = new List<string>();

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.Debloating,
            StageTitle = "Удаление телеметрии (Debloat)",
            Message = "Очистка пакета от телеметрии, GeForce Experience и лишних компонентов...",
            ProgressPercentage = 10
        });

        foreach (var folder in TelemetryAndBloatFolders)
        {
            string targetPath = Path.Combine(driverDirectory, folder);
            if (Directory.Exists(targetPath))
            {
                try
                {
                    Directory.Delete(targetPath, true);
                    removedItems.Add($"Удалена папка телеметрии: {folder}");
                    progress?.Report(new InstallProgressReport
                    {
                        Stage = InstallStage.Debloating,
                        StageTitle = "Удаление телеметрии",
                        Message = $"Компонент удален: {folder}",
                        ProgressPercentage = 50
                    });
                }
                catch (Exception ex)
                {
                    removedItems.Add($"Пропущен {folder}: {ex.Message}");
                }
            }
        }

        // Also check inside Display.Driver for telemetry dlls if present
        string displayDriverDir = Path.Combine(driverDirectory, "Display.Driver");
        if (Directory.Exists(displayDriverDir))
        {
            string[] telemetryFiles = { "NvTelemetryAPI64.dll", "NvTelemetryAPI32.dll", "nvtelemetry.dll" };
            foreach (var tf in telemetryFiles)
            {
                string filePath = Path.Combine(displayDriverDir, tf);
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        removedItems.Add($"Удален файл телеметрии: {tf}");
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.Debloating,
            StageTitle = "Очистка завершена",
            Message = $"Пакет драйвера очищен. Удалено компонентов: {removedItems.Count}",
            ProgressPercentage = 100
        });

        return removedItems;
    }
}
