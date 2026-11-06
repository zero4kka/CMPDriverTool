using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CMPDriverTool.Core.Certificate;
using CMPDriverTool.Core.Downloader;
using CMPDriverTool.Core.Manifest;
using CMPDriverTool.Core.Models;
using CMPDriverTool.Core.Patcher;

namespace CMPDriverTool.Core;

public class DriverWorkflowOrchestrator
{
    private readonly DriverDownloader _downloader;
    private readonly DriverExtractor _extractor;
    private readonly DebloatService _debloatService;
    private readonly DartraidenPatcherService _dartraidenPatcher;
    private readonly CertificateManager _certificateManager;
    private readonly DriverInstaller _driverInstaller;

    public DriverWorkflowOrchestrator(
        DriverDownloader? downloader = null,
        DriverExtractor? extractor = null,
        DebloatService? debloatService = null,
        DartraidenPatcherService? dartraidenPatcher = null,
        CertificateManager? certificateManager = null,
        DriverInstaller? driverInstaller = null)
    {
        _downloader = downloader ?? new DriverDownloader();
        _extractor = extractor ?? new DriverExtractor();
        _debloatService = debloatService ?? new DebloatService();
        _dartraidenPatcher = dartraidenPatcher ?? new DartraidenPatcherService();
        _certificateManager = certificateManager ?? new CertificateManager();
        _driverInstaller = driverInstaller ?? new DriverInstaller();
    }

    public async Task<DriverInstallResult> ExecuteInstallationWorkflowAsync(
        DriverManifestItem selectedDriver,
        string? localPackageOverridePath = null,
        bool useNvencPreset = false,
        string? customGpuName = null,
        IProgress<InstallProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string workBase = ManifestManager.DefaultWorkDirectory;
        string downloadsBase = ManifestManager.DefaultCacheDirectory;
        string extractDir = Path.Combine(workBase, $"Driver_{selectedDriver.Version}");

        try
        {
            // 1. Download Stage
            string packagePath;
            if (!string.IsNullOrEmpty(localPackageOverridePath) && File.Exists(localPackageOverridePath))
            {
                packagePath = localPackageOverridePath;
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.Downloading,
                    StageTitle = "Использование локального пакета",
                    Message = $"Выбран локальный файл: {packagePath}",
                    ProgressPercentage = 100
                });
            }
            else
            {
                packagePath = await _downloader.DownloadDriverAsync(selectedDriver, downloadsBase, progress, cancellationToken);
            }

            // 1.5. Ensure official dartraiden/NVIDIA-patcher is downloaded from GitHub and prepared
            try
            {
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.PatchingInf,
                    StageTitle = "Загрузка dartraiden/NVIDIA-patcher",
                    Message = "Синхронизация с официальным репозиторием https://github.com/dartraiden/NVIDIA-patcher...",
                    ProgressPercentage = 25
                });
                string patcherDir = await _dartraidenPatcher.EnsurePatcherDownloadedAsync(workBase, false, progress, cancellationToken);
                await _dartraidenPatcher.PrepareAndExecuteDartraidenAsync(patcherDir, packagePath, useNvencPreset, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                // Fallback to built-in rules if GitHub repository is unreachable (offline mode)
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.PatchingInf,
                    StageTitle = "Офлайн-режим dartraiden",
                    Message = $"Синхронизация с GitHub пропущена ({ex.Message}). Применяются встроенные правила dartraiden/NVIDIA-patcher.",
                    ProgressPercentage = 25
                });
            }

            // 2. Extract Stage (7-Zip)
            string extractedPath = await _extractor.ExtractDriverPackageAsync(packagePath, extractDir, progress, cancellationToken);

            // 3. Debloat Stage
            var debloated = _debloatService.CleanDriverPackage(extractedPath, progress);

            // 4. Apply dartraiden/NVIDIA-patcher modifications
            var patchResult = await _dartraidenPatcher.PatchDriverDirectoryAsync(extractedPath, useNvencPreset, customGpuName, progress, cancellationToken);
            if (!patchResult.Success)
            {
                return new DriverInstallResult
                {
                    Success = false,
                    ErrorMessage = $"Ошибка модификации драйвера патчером dartraiden: {patchResult.ErrorMessage}"
                };
            }

            // 5. Certificate & Signing Stage
            var certResult = _certificateManager.EnsureTestCertificate(Path.Combine(workBase, "Certs"), progress);
            if (!certResult.Success)
            {
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.GeneratingCertificate,
                    StageTitle = "Предупреждение о сертификате",
                    Message = $"Предупреждение: {certResult.Message}. Продолжение установки...",
                    ProgressPercentage = 100
                });
            }

            // Perform signing on the patched driver package files
            _certificateManager.SignDriverPackage(extractedPath, null, progress);

            // 6. Driver Installation via pnputil
            var installResult = await _driverInstaller.InstallDriverAsync(patchResult.PatchedInfPath, progress, cancellationToken);
            return installResult;
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.Failed,
                StageTitle = "Отменено пользователем",
                Message = "Процесс установки был остановлен пользователем.",
                ProgressPercentage = 0
            });
            return new DriverInstallResult { Success = false, ErrorMessage = "Операция отменена пользователем." };
        }
        catch (Exception ex)
        {
            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.Failed,
                StageTitle = "Критическая ошибка",
                Message = ex.Message,
                ErrorMessage = ex.ToString(),
                ProgressPercentage = 0
            });
            return new DriverInstallResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
