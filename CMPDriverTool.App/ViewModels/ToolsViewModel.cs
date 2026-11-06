using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CMPDriverTool.Core.Hardware;
using CMPDriverTool.Core.Models;
using CMPDriverTool.Core.Tools;

namespace CMPDriverTool.App.ViewModels;

public partial class ToolsViewModel : ObservableObject
{
    private readonly SystemToolsService _toolsService;
    private readonly SecurityService _securityService;
    private readonly GpuDetector _gpuDetector;

    [ObservableProperty]
    private bool _isRestartingDriver;

    [ObservableProperty]
    private string _restartDriverMessage = "Перезапуск DWM и WDDM видеодрайвера без перезагрузки Windows (аналог горячих клавиш Win+Ctrl+Shift+B).";

    [ObservableProperty]
    private bool _testModeEnabled;

    [ObservableProperty]
    private string _testModeStatus = "Определение...";

    [ObservableProperty]
    private bool _isChangingTestMode;

    [ObservableProperty]
    private ObservableCollection<OemDriverPackage> _installedDrivers = new();

    [ObservableProperty]
    private OemDriverPackage? _selectedDriver;

    [ObservableProperty]
    private bool _isScanningDrivers;

    [ObservableProperty]
    private bool _showCapacitorGuide = false;

    [ObservableProperty]
    private string _currentInstalledGpuName = "Определение...";

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
    private string _statusMessage = "Готов к работе";

    public ToolsViewModel(SystemToolsService? toolsService = null, SecurityService? securityService = null, GpuDetector? gpuDetector = null)
    {
        _toolsService = toolsService ?? new SystemToolsService();
        _securityService = securityService ?? new SecurityService();
        _gpuDetector = gpuDetector ?? new GpuDetector();
        UpdateGpuNamePresets(null);
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        await CheckTestModeStatusAsync();
        await RefreshDriversAsync();
        await RefreshGpuInfoAsync();
    }

    private async Task RefreshGpuInfoAsync()
    {
        try
        {
            var gpus = await Task.Run(() => _gpuDetector.DetectGpus());
            var cmp = gpus.FirstOrDefault(g => g.IsCmpCard) ?? gpus.FirstOrDefault();
            if (cmp != null)
            {
                CurrentInstalledGpuName = cmp.Name;
                UpdateGpuNamePresets(cmp);
            }
        }
        catch { }
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
            var (ok, msg) = await Task.Run(() => _toolsService.RenameInstalledGpu(targetName));
            StatusMessage = msg;
            MessageBox.Show(msg, ok ? "Имя видеокарты изменено" : "Внимание", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (ok)
            {
                await RefreshGpuInfoAsync();
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
    public async Task RestartDriverAsync()
    {
        IsRestartingDriver = true;
        StatusMessage = "Отправка сигнала перезапуска видеодрайвера (Win+Ctrl+Shift+B)...";

        try
        {
            await Task.Delay(200); // Visual cue
            bool ok = await Task.Run(() => _toolsService.RestartGpuDriver());

            if (ok)
            {
                StatusMessage = "Сигнал перезапуска драйвера успешно отправлен в систему (экран мог мигнуть).";
            }
            else
            {
                StatusMessage = "Не удалось отправить горячие клавиши перезапуска.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка перезапуска: {ex.Message}";
        }
        finally
        {
            IsRestartingDriver = false;
        }
    }

    [RelayCommand]
    public async Task ToggleTestModeAsync()
    {
        IsChangingTestMode = true;
        bool target = !TestModeEnabled;
        StatusMessage = $"Изменение настроек bcdedit: testsigning {(target ? "on" : "off")}...";

        try
        {
            var (ok, msg) = await Task.Run(() => _securityService.SetTestMode(target));
            StatusMessage = msg;
            MessageBox.Show(msg, ok ? "BCDEDIT" : "Ошибка", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await CheckTestModeStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка BCDEDIT: {ex.Message}";
        }
        finally
        {
            IsChangingTestMode = false;
        }
    }

    private async Task CheckTestModeStatusAsync()
    {
        TestModeEnabled = await Task.Run(() => SecurityService.CheckTestMode());
        TestModeStatus = TestModeEnabled
            ? "Включен (Test Mode ON: загрузка тестовых драйверов разрешена)"
            : "Отключен (Test Mode OFF: требуется включить для модифицированных драйверов)";
    }

    [RelayCommand]
    public async Task RefreshDriversAsync()
    {
        IsScanningDrivers = true;
        StatusMessage = "Сканирование установленных OEM-драйверов в DriverStore через pnputil...";

        try
        {
            var drivers = await _toolsService.EnumerateDisplayDriversAsync();
            InstalledDrivers.Clear();

            // Display NVIDIA drivers first, then others
            foreach (var d in drivers.OrderByDescending(x => x.IsNvidiaDisplayDriver))
            {
                InstalledDrivers.Add(d);
            }

            StatusMessage = $"Найдено пакетов драйверов в системе: {InstalledDrivers.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка чтения драйверов: {ex.Message}";
        }
        finally
        {
            IsScanningDrivers = false;
        }
    }

    [RelayCommand]
    public async Task UninstallDriverPackageAsync(OemDriverPackage package)
    {
        if (package == null) return;

        var confirm = MessageBox.Show(
            $"Вы действительно хотите полностью удалить пакет драйвера '{package.PublishedName}' ({package.OriginalName} / {package.DriverVersion}) из хранилища DriverStore?",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = $"Удаление пакета {package.PublishedName} из хранилища драйверов...";
        var (ok, msg) = await _toolsService.DeleteDriverPackageAsync(package.PublishedName, true);

        MessageBox.Show(msg, ok ? "Успех" : "Внимание", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

        if (ok)
        {
            InstalledDrivers.Remove(package);
        }
        StatusMessage = msg;
    }

    [RelayCommand]
    public async Task CleanAllDriversAsync()
    {
        var confirm = MessageBox.Show(
            "Вы действительно хотите выполнить чистое удаление ВСЕХ видеодрайверов NVIDIA из хранилища?\n\n" +
            "Это действие принудительно удалит все OEM-пакеты дисплея NVIDIA и сбросит видеокарту в базовый адаптер Windows.\n\nПродолжить?",
            "Подтверждение удаления всех пакетов",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsScanningDrivers = true;
        StatusMessage = "Выполняется полная зачистка DriverStore...";

        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var (deletedCount, logs) = await _toolsService.CleanUninstallAllNvidiaDriversAsync(progress);

            MessageBox.Show($"Успешно удалено пакетов: {deletedCount}\n\n{string.Join("\n", logs)}", "Зачистка завершена", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshDriversAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanningDrivers = false;
        }
    }

    [RelayCommand]
    private void OpenDeviceManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "devmgmt.msc", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть Диспетчер устройств: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ToggleCapacitorGuide()
    {
        ShowCapacitorGuide = !ShowCapacitorGuide;
    }
}
