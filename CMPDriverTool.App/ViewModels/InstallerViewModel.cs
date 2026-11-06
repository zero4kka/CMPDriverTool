using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CMPDriverTool.Core;
using CMPDriverTool.Core.Hardware;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.App.ViewModels;

public enum StepState
{
    Pending,
    Active,
    Success,
    Failed
}

public partial class InstallerViewModel : ObservableObject
{
    private readonly DriverWorkflowOrchestrator _orchestrator;
    private readonly Action _onReturnToDashboard;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _driverVersion = string.Empty;

    [ObservableProperty]
    private string _currentStepTitle = "Ожидание запуска";

    [ObservableProperty]
    private string _currentStepMessage = "Подготовка к установке...";

    [ObservableProperty]
    private double _overallProgress = 0;

    [ObservableProperty]
    private bool _isIndeterminate = false;

    [ObservableProperty]
    private bool _isInstalling = false;

    [ObservableProperty]
    private bool _isCompleted = false;

    [ObservableProperty]
    private bool _isFailed = false;

    [ObservableProperty]
    private bool _isTestModeFailure = false;

    [ObservableProperty]
    private bool _rebootRequired = false;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLogExpanded = true;

    // 6 Visual Steps Status
    [ObservableProperty] private StepState _step1Download = StepState.Pending;
    [ObservableProperty] private StepState _step2Extract = StepState.Pending;
    [ObservableProperty] private StepState _step3Debloat = StepState.Pending;
    [ObservableProperty] private StepState _step4PatchInf = StepState.Pending;
    [ObservableProperty] private StepState _step5Certificate = StepState.Pending;
    [ObservableProperty] private StepState _step6Install = StepState.Pending;

    [ObservableProperty]
    private ObservableCollection<string> _logLines = new();

    public InstallerViewModel(
        Action onReturnToDashboard,
        DriverWorkflowOrchestrator? orchestrator = null)
    {
        _onReturnToDashboard = onReturnToDashboard;
        _orchestrator = orchestrator ?? new DriverWorkflowOrchestrator();
    }

    public async Task StartInstallationAsync(
        DriverManifestItem driver,
        string? localPackagePath,
        bool useNvenc = false,
        string? customGpuName = null)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        DriverVersion = driver.Version;
        IsInstalling = true;
        IsCompleted = false;
        IsFailed = false;
        IsTestModeFailure = false;
        RebootRequired = false;
        ErrorMessage = null;
        OverallProgress = 0;
        LogLines.Clear();

        ResetSteps();

        AppendLog($"=== Запуск установки модифицированного драйвера v{driver.Version} ===");
        AppendLog("Патчер: официальный dartraiden/NVIDIA-patcher (https://github.com/dartraiden/NVIDIA-patcher)");
        if (!string.IsNullOrWhiteSpace(customGpuName))
        {
            AppendLog($"Отображаемое имя в системе (Диспетчер задач): {customGpuName}");
        }
        if (useNvenc) AppendLog("Режим NVENC: включен пресет Presets_NVENC.txt (снятие лимита сессий)");
        if (!string.IsNullOrEmpty(localPackagePath))
        {
            AppendLog($"Используется локальный установочный пакет: {localPackagePath}");
        }

        var progress = new Progress<InstallProgressReport>(OnProgressReported);

        try
        {
            var result = await _orchestrator.ExecuteInstallationWorkflowAsync(
                driver, localPackagePath, useNvenc, customGpuName, progress, _cts.Token);

            IsInstalling = false;
            if (result.Success)
            {
                IsCompleted = true;
                RebootRequired = result.RebootRequired;
                OverallProgress = 100;
                CurrentStepTitle = "Установка завершена успешно!";
                CurrentStepMessage = RebootRequired
                    ? "Драйвер установлен. Требуется перезагрузка компьютера для применения."
                    : "Модифицированный драйвер успешно установлен и готов к работе в играх!";
                Step6Install = StepState.Success;
                AppendLog(">>> [УСПЕХ] Установка модифицированного драйвера завершена успешно!");
            }
            else
            {
                IsFailed = true;
                IsTestModeFailure = result.IsTestModeIssue || !SecurityService.CheckTestMode();
                ErrorMessage = result.ErrorMessage;
                CurrentStepTitle = "Сбой установки драйвера";
                CurrentStepMessage = result.ErrorMessage ?? "Произошла ошибка при выполнении операции.";
                AppendLog($">>> [ОШИБКА] {result.ErrorMessage}");
                if (IsTestModeFailure)
                {
                    AppendLog(">>> [РЕШЕНИЕ] Включите тестовый режим (кнопка ниже) и перезагрузите ПК, затем повторите установку.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            IsInstalling = false;
            IsFailed = true;
            CurrentStepTitle = "Установка отменена";
            CurrentStepMessage = "Операция была отменена пользователем.";
            AppendLog(">>> [ОТМЕНА] Установка отменена пользователем.");
        }
        catch (Exception ex)
        {
            IsInstalling = false;
            IsFailed = true;
            ErrorMessage = ex.Message;
            CurrentStepTitle = "Критическая ошибка";
            CurrentStepMessage = ex.Message;
            AppendLog($">>> [ИСКЛЮЧЕНИЕ] {ex}");
        }
    }

    private void ResetSteps()
    {
        Step1Download = StepState.Pending;
        Step2Extract = StepState.Pending;
        Step3Debloat = StepState.Pending;
        Step4PatchInf = StepState.Pending;
        Step5Certificate = StepState.Pending;
        Step6Install = StepState.Pending;
    }

    private void OnProgressReported(InstallProgressReport report)
    {
        CurrentStepTitle = report.StageTitle;
        CurrentStepMessage = report.Message;
        IsIndeterminate = report.IsIndeterminate;

        switch (report.Stage)
        {
            case InstallStage.Downloading:
                Step1Download = StepState.Active;
                OverallProgress = Math.Max(OverallProgress, report.ProgressPercentage * 0.25);
                if (report.ProgressPercentage >= 100) Step1Download = StepState.Success;
                break;

            case InstallStage.Extracting:
                Step1Download = StepState.Success;
                Step2Extract = StepState.Active;
                OverallProgress = 25 + (report.ProgressPercentage * 0.25);
                if (report.ProgressPercentage >= 100) Step2Extract = StepState.Success;
                break;

            case InstallStage.Debloating:
                Step2Extract = StepState.Success;
                Step3Debloat = StepState.Active;
                OverallProgress = 50 + (report.ProgressPercentage * 0.10);
                if (report.ProgressPercentage >= 100) Step3Debloat = StepState.Success;
                break;

            case InstallStage.PatchingInf:
                Step3Debloat = StepState.Success;
                Step4PatchInf = StepState.Active;
                OverallProgress = 60 + (report.ProgressPercentage * 0.15);
                if (report.ProgressPercentage >= 100) Step4PatchInf = StepState.Success;
                break;

            case InstallStage.GeneratingCertificate:
                Step4PatchInf = StepState.Success;
                Step5Certificate = StepState.Active;
                OverallProgress = 75 + (report.ProgressPercentage * 0.10);
                if (report.ProgressPercentage >= 100) Step5Certificate = StepState.Success;
                break;

            case InstallStage.InstallingDriver:
                Step5Certificate = StepState.Success;
                Step6Install = StepState.Active;
                OverallProgress = 85 + (report.ProgressPercentage * 0.15);
                break;

            case InstallStage.Completed:
                Step6Install = StepState.Success;
                OverallProgress = 100;
                break;

            case InstallStage.Failed:
                MarkCurrentStepFailed();
                break;
        }

        AppendLog($"[{report.FormattedTimestamp}] {report.Message}");
    }

    private void MarkCurrentStepFailed()
    {
        if (Step6Install == StepState.Active) Step6Install = StepState.Failed;
        else if (Step5Certificate == StepState.Active) Step5Certificate = StepState.Failed;
        else if (Step4PatchInf == StepState.Active) Step4PatchInf = StepState.Failed;
        else if (Step3Debloat == StepState.Active) Step3Debloat = StepState.Failed;
        else if (Step2Extract == StepState.Active) Step2Extract = StepState.Failed;
        else if (Step1Download == StepState.Active) Step1Download = StepState.Failed;
    }

    private void AppendLog(string text)
    {
        if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => LogLines.Add(text));
        }
        else
        {
            LogLines.Add(text);
        }
    }

    [RelayCommand]
    private void ToggleLogExpanded()
    {
        IsLogExpanded = !IsLogExpanded;
    }

    [RelayCommand]
    private void CancelInstall()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void CopyLog()
    {
        string fullLog = string.Join(Environment.NewLine, LogLines);
        Clipboard.SetText(fullLog);
        MessageBox.Show("Лог операций скопирован в буфер обмена.", "Скопировано", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void RebootComputer()
    {
        var res = MessageBox.Show(
            "Вы уверены, что хотите перезагрузить компьютер прямо сейчас?",
            "Перезагрузка системы",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 0",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
    }

    [RelayCommand]
    private async Task EnableTestModeAndRebootAsync()
    {
        var securityService = new SecurityService();
        var (ok, msg) = await Task.Run(() => securityService.SetTestMode(true));
        if (ok)
        {
            var res = MessageBox.Show(
                "Тестовый режим (Testsigning) успешно активирован в конфигурации загрузки Windows!\n\n" +
                "Для вступления изменений в силу компьютер необходимо перезагрузить прямо сейчас.\n\n" +
                "Перезагрузить компьютер сейчас?",
                "Перезагрузка ПК",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/r /t 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        else
        {
            MessageBox.Show($"Не удалось активировать Test Mode: {msg}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ReturnToDashboard()
    {
        _onReturnToDashboard();
    }
}
