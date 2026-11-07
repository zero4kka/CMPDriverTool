using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CMPDriverTool.Core;
using CMPDriverTool.Core.Hardware;
using CMPDriverTool.Core.Manifest;
using CMPDriverTool.Core.Models;
using CMPDriverTool.Core.Tools;
using Microsoft.Win32;

namespace CMPDriverTool.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly GpuDetector _gpuDetector;
    private readonly SecurityService _securityService;
    private readonly ManifestManager _manifestManager;
    private readonly SystemToolsService _toolsService;
    private readonly Action<DriverManifestItem, string?, bool, string?, bool> _onNavigateToInstall;

    [ObservableProperty]
    private ObservableCollection<GpuDeviceInfo> _gpus = new();

    [ObservableProperty]
    private GpuDeviceInfo? _selectedGpu;

    partial void OnSelectedGpuChanged(GpuDeviceInfo? value)
    {
        HasPcieWarning = value?.HasPcieLaneWarning ?? false;
        IsPciePowerSaving = value?.IsPciePowerSaving ?? false;
        PcieWarningMessage = value?.PcieWarningMessage ?? string.Empty;
        UpdateGpuNamePresets(value);
    }

    [ObservableProperty]
    private GpuDeviceInfo? _detectedCmpCard;

    [ObservableProperty]
    private DisplayCheckResult? _displayCheck;

    [ObservableProperty]
    private SystemSecurityInfo? _securityInfo;

    [ObservableProperty]
    private ObservableCollection<DriverManifestItem> _availableDrivers = new();

    [ObservableProperty]
    private DriverManifestItem? _selectedDriver;

    [ObservableProperty]
    private ObservableCollection<GpuDisplayNameOption> _gpuNamePresets = new();

    [ObservableProperty]
    private GpuDisplayNameOption? _selectedGpuNameOption;

    partial void OnSelectedGpuNameOptionChanged(GpuDisplayNameOption? value)
    {
        IsCustomGpuName = value?.IsCustom ?? false;
        if (value != null && !value.IsCustom && !string.IsNullOrWhiteSpace(value.NameValue))
        {
            CustomGpuNameText = value.NameValue;
        }
    }

    [ObservableProperty]
    private string _customGpuNameText = "NVIDIA GeForce GTX 1660 SUPER";

    [ObservableProperty]
    private bool _isCustomGpuName;

    [ObservableProperty]
    private bool _isRenamingGpu;

    [ObservableProperty]
    private string? _customPackagePath;

    [ObservableProperty]
    private bool _useNvencPreset = false;

    [ObservableProperty]
    private bool _launchSetupExe = true;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCmpDetected;

    [ObservableProperty]
    private bool _hasPcieWarning;

    [ObservableProperty]
    private bool _isPciePowerSaving;

    [ObservableProperty]
    private string _pcieWarningMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Готов к диагностике";

    public bool HasCustomPackage => !string.IsNullOrEmpty(CustomPackagePath);

    public DashboardViewModel(
        Action<DriverManifestItem, string?, bool, string?, bool> onNavigateToInstall,
        GpuDetector? gpuDetector = null,
        SecurityService? securityService = null,
        ManifestManager? manifestManager = null,
        SystemToolsService? toolsService = null)
    {
        _onNavigateToInstall = onNavigateToInstall;
        _gpuDetector = gpuDetector ?? new GpuDetector();
        _securityService = securityService ?? new SecurityService();
        _manifestManager = manifestManager ?? new ManifestManager();
        _toolsService = toolsService ?? new SystemToolsService();
        UpdateGpuNamePresets(null);
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IsScanning = true;
        StatusMessage = "Выполняется диагностика видеокарт и системных параметров...";

        try
        {
            // 1. Detect GPUs and PCIe lanes
            var detected = await Task.Run(() => _gpuDetector.DetectGpus());
            Gpus.Clear();
            foreach (var g in detected) Gpus.Add(g);

            DetectedCmpCard = detected.FirstOrDefault(g => g.IsCmpCard);
            IsCmpDetected = DetectedCmpCard != null;

            if (DetectedCmpCard != null)
            {
                SelectedGpu = DetectedCmpCard;
                HasPcieWarning = DetectedCmpCard.HasPcieLaneWarning;
                IsPciePowerSaving = DetectedCmpCard.IsPciePowerSaving;
                PcieWarningMessage = DetectedCmpCard.PcieWarningMessage;
            }
            else
            {
                SelectedGpu = detected.FirstOrDefault();
                HasPcieWarning = SelectedGpu?.HasPcieLaneWarning ?? false;
                IsPciePowerSaving = SelectedGpu?.IsPciePowerSaving ?? false;
                PcieWarningMessage = SelectedGpu?.PcieWarningMessage ?? string.Empty;
            }

            // 2. Display Output check
            DisplayCheck = await Task.Run(() => _gpuDetector.CheckDisplayOutputConfiguration(detected));

            // 3. System Security check (Secure Boot, Test Mode)
            SecurityInfo = await Task.Run(() => _securityService.CheckSystemSecurity());

            // 4. Manifest drivers
            var drivers = await _manifestManager.GetAvailableDriversAsync();
            AvailableDrivers.Clear();
            foreach (var d in drivers) AvailableDrivers.Add(d);

            SelectedDriver = AvailableDrivers.FirstOrDefault(d => d.IsRecommended) ?? AvailableDrivers.FirstOrDefault();

            // 5. Update GPU Display Name presets
            UpdateGpuNamePresets(DetectedCmpCard ?? SelectedGpu);

            StatusMessage = IsCmpDetected
                ? $"Обнаружена видеокарта {DetectedCmpCard?.Name} ({DetectedCmpCard?.DeviceId})!"
                : "Видеокарта CMP не обнаружена (проверьте подключение в слот PCIe).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка при диагностике: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void BrowseCustomPackage()
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Исполняемые файлы NVIDIA (*.exe)|*.exe|Все файлы (*.*)|*.*",
            Title = "Выберите скачанный установщик драйвера NVIDIA"
        };

        if (ofd.ShowDialog() == true)
        {
            CustomPackagePath = ofd.FileName;
            OnPropertyChanged(nameof(HasCustomPackage));
            StatusMessage = $"Выбран локальный файл пакета: {Path.GetFileName(CustomPackagePath)}";
        }
    }

    [RelayCommand]
    private void ClearCustomPackage()
    {
        CustomPackagePath = null;
        OnPropertyChanged(nameof(HasCustomPackage));
    }

    [RelayCommand]
    private async Task StartInstallAsync()
    {
        if (SelectedDriver == null)
        {
            MessageBox.Show("Пожалуйста, выберите версию драйвера для установки.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SecurityInfo?.SecureBootEnabled == true)
        {
            var res = MessageBox.Show(
                "Secure Boot включен в UEFI вашей системы!\n\n" +
                "Модифицированный драйвер требует выключенного Secure Boot, иначе Windows заблокирует его загрузку.\n\n" +
                "Вы хотите продолжить установку несмотря на это?",
                "Внимание: Secure Boot активен",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;
        }

        if (SecurityInfo != null && !SecurityInfo.TestModeEnabled)
        {
            var res = MessageBox.Show(
                "Внимание: Тестовый режим Windows (Test Mode) в данный момент выключен!\n\n" +
                "64-битная система Windows строго блокирует установку любых модифицированных драйверов (код ошибки 0xE000022F), если тестовый режим не активирован.\n\n" +
                "Включить тестовый режим прямо сейчас?\n" +
                "(После включения система предложит перезагрузить компьютер, после чего можно будет продолжить установку).",
                "Требуется активация Test Mode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                var (ok, msg) = await Task.Run(() => _securityService.SetTestMode(true));
                if (ok)
                {
                    var rebootChoice = MessageBox.Show(
                        "Тестовый режим успешно включен в конфигурации загрузки Windows (bcdedit)!\n\n" +
                        "Для вступления изменений в силу необходимо перезагрузить компьютер.\n\n" +
                        "Перезагрузить компьютер прямо сейчас?",
                        "Перезагрузка для применения Test Mode",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (rebootChoice == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = "/r /t 0",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        return;
                    }

                    await RefreshAllAsync();
                    return;
                }
                else
                {
                    MessageBox.Show($"Не удалось включить тестовый режим: {msg}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (res == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        string chosenName = (IsCustomGpuName ? CustomGpuNameText : SelectedGpuNameOption?.NameValue)?.Trim() ?? "NVIDIA GeForce GTX 1660 SUPER";
        _onNavigateToInstall(SelectedDriver, CustomPackagePath, UseNvencPreset, chosenName, LaunchSetupExe);
    }

    private void UpdateGpuNamePresets(GpuDeviceInfo? gpu)
    {
        var presets = GpuDisplayNamePresets.GetPresetsForGpu(gpu);
        GpuNamePresets.Clear();
        foreach (var p in presets)
        {
            GpuNamePresets.Add(p);
        }
        SelectedGpuNameOption = GpuNamePresets.FirstOrDefault();
        CustomGpuNameText = SelectedGpuNameOption?.NameValue ?? "NVIDIA GeForce GTX 1660 SUPER";
    }

    [RelayCommand]
    public async Task ApplyGpuRenameAsync()
    {
        string targetName = (IsCustomGpuName ? CustomGpuNameText : SelectedGpuNameOption?.NameValue)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            MessageBox.Show("Пожалуйста, укажите имя для видеокарты.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsRenamingGpu = true;
        StatusMessage = $"Применение имени \"{targetName}\" в системе...";

        try
        {
            var (ok, msg) = await Task.Run(() => _toolsService.RenameInstalledGpu(targetName, SelectedGpu?.DeviceId));
            StatusMessage = msg;
            MessageBox.Show(msg, ok ? "Имя видеокарты изменено" : "Внимание", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (ok)
            {
                await RefreshAllAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка изменения имени: {ex.Message}";
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsRenamingGpu = false;
        }
    }

    [RelayCommand]
    public async Task QuickToggleTestModeAsync()
    {
        if (SecurityInfo == null) return;
        bool targetState = !SecurityInfo.TestModeEnabled;
        var (ok, msg) = await Task.Run(() => _securityService.SetTestMode(targetState));

        MessageBox.Show(msg, ok ? "Успех" : "Внимание", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await RefreshAllAsync();
    }

    [RelayCommand]
    public async Task CleanUninstallDriversAsync()
    {
        var confirm = MessageBox.Show(
            "Вы действительно хотите выполнить чистое удаление видеодрайвера?\n\n" +
            "Это действие:\n" +
            "1. Принудительно удалит все установленные OEM-пакеты NVIDIA из системного хранилища (DriverStore).\n" +
            "2. Сбросит видеокарту в состояние стандартного базового видеоадаптера Windows.\n" +
            "3. Экран может кратковременно мигнуть или сменить разрешение.\n\n" +
            "Продолжить удаление?",
            "Подтверждение чистой деинсталляции",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsScanning = true;
        StatusMessage = "Выполняется чистое удаление видеодрайверов NVIDIA из системы...";

        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var (deletedCount, logs) = await Task.Run(() => _toolsService.CleanUninstallAllNvidiaDriversAsync(progress));

            string logSummary = string.Join("\n", logs);
            MessageBox.Show(
                $"Чистая деинсталляция завершена!\n\nУдалено пакетов: {deletedCount}\n\nПодробности:\n{logSummary}",
                "Удаление завершено",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusMessage = $"Зачистка завершена. Удалено пакетов: {deletedCount}. Обновление оборудования...";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при удалении драйверов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = $"Ошибка удаления: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
}
