using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CMPDriverTool.Core.Hardware;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.Core.Certificate;

public class DriverInstallResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string OutputLog { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public bool RebootRequired { get; set; }
    public bool IsTestModeIssue { get; set; }
}

public class DriverInstaller
{
    public static Encoding GetConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // On Russian Windows, pnputil console output uses Windows-1251 (ANSI)
            int ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            if (ansiCodePage > 0)
            {
                return Encoding.GetEncoding(ansiCodePage);
            }
        }
        catch { }

        try
        {
            return Encoding.GetEncoding(1251);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    public async Task<DriverInstallResult> InstallDriverAsync(
        string infFilePath,
        IProgress<InstallProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new DriverInstallResult();

        if (!File.Exists(infFilePath))
        {
            result.Success = false;
            result.ErrorMessage = $"INF файл драйвера не найден: {infFilePath}";
            return result;
        }

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.InstallingDriver,
            StageTitle = "Установка через pnputil",
            Message = $"Запуск pnputil.exe для добавления и установки драйвера ({Path.GetFileName(infFilePath)})...",
            ProgressPercentage = 20
        });

        var logBuilder = new StringBuilder();

        try
        {
            var encoding = GetConsoleEncoding();

            // Command: pnputil.exe /add-driver "<infFilePath>" /install
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/add-driver \"{infFilePath}\" /install",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logBuilder.AppendLine(e.Data);
                    progress?.Report(new InstallProgressReport
                    {
                        Stage = InstallStage.InstallingDriver,
                        StageTitle = "Установка драйвера",
                        Message = e.Data,
                        ProgressPercentage = 50
                    });

                    if (e.Data.IndexOf("перезагрузка", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.Data.IndexOf("reboot", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.RebootRequired = true;
                    }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logBuilder.AppendLine("[STDERR] " + e.Data);
                    progress?.Report(new InstallProgressReport
                    {
                        Stage = InstallStage.InstallingDriver,
                        StageTitle = "Установка драйвера",
                        Message = "[Предупреждение] " + e.Data,
                        ProgressPercentage = 50
                    });
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            result.ExitCode = process.ExitCode;
            result.OutputLog = logBuilder.ToString();

            // Pnputil exit code 0 = success, 3010 = success but reboot required
            if (process.ExitCode == 0 || process.ExitCode == 3010)
            {
                result.Success = true;
                if (process.ExitCode == 3010) result.RebootRequired = true;

                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.Completed,
                    StageTitle = "Установка завершена успешно",
                    Message = result.RebootRequired
                        ? "Драйвер успешно установлен! Требуется перезагрузка компьютера для применения изменений."
                        : "Модифицированный драйвер успешно установлен и активирован в системе!",
                    ProgressPercentage = 100
                });
                return result;
            }

            // Fallback: If pnputil failed, attempt installation via NVIDIA Setup (setup.exe -s -noreboot)
            string displayDir = Path.GetDirectoryName(infFilePath)!;
            string setupCandidate = Path.GetFullPath(Path.Combine(displayDir, "..", "setup.exe"));
            if (!File.Exists(setupCandidate)) setupCandidate = Path.Combine(displayDir, "setup.exe");

            if (File.Exists(setupCandidate))
            {
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.InstallingDriver,
                    StageTitle = "Установка через установщик NVIDIA",
                    Message = $"Запуск резервной установки через NVIDIA Setup ({Path.GetFileName(setupCandidate)} -s -noreboot)...",
                    ProgressPercentage = 70
                });

                try
                {
                    var setupPsi = new ProcessStartInfo
                    {
                        FileName = setupCandidate,
                        Arguments = "-s -noreboot",
                        WorkingDirectory = Path.GetDirectoryName(setupCandidate)!,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = encoding,
                        StandardErrorEncoding = encoding
                    };

                    using var setupProcess = Process.Start(setupPsi);
                    if (setupProcess != null)
                    {
                        await setupProcess.WaitForExitAsync(cancellationToken);
                        if (setupProcess.ExitCode == 0 || setupProcess.ExitCode == 1 || setupProcess.ExitCode == 3010)
                        {
                            result.Success = true;
                            result.RebootRequired = true;
                            result.ExitCode = setupProcess.ExitCode;

                            progress?.Report(new InstallProgressReport
                            {
                                Stage = InstallStage.Completed,
                                StageTitle = "Установка завершена успешно",
                                Message = "Модифицированный драйвер успешно установлен через установщик NVIDIA! Требуется перезагрузка компьютера.",
                                ProgressPercentage = 100
                            });

                            return result;
                        }
                    }
                }
                catch { }
            }

            // If both failed:
            result.Success = false;

            bool isTestModeOff = !SecurityService.CheckTestMode();
            if (isTestModeOff)
            {
                result.IsTestModeIssue = true;
                result.ErrorMessage = "Windows отклонила установку модифицированного драйвера (проверка целостности кода).\n" +
                                     "Причина: Тестовый режим Windows (Test Mode / testsigning) отключен.\n" +
                                     "Для установки модифицированных драйверов необходимо включить Test Mode и перезагрузить компьютер.";
            }
            else
            {
                result.ErrorMessage = $"pnputil завершился с кодом ошибки {process.ExitCode}. Подробности:\n{ExtractPnputilError(result.OutputLog)}";
            }

            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.Failed,
                StageTitle = "Ошибка установки драйвера",
                Message = result.ErrorMessage,
                ErrorMessage = result.OutputLog,
                ProgressPercentage = 100
            });

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Исключение при установке: {ex.Message}";
            result.OutputLog = logBuilder.ToString();
            return result;
        }
    }

    private static string ExtractPnputilError(string log)
    {
        var lines = log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var errors = new StringBuilder();
        foreach (var l in lines)
        {
            if (l.Contains("Не удалось", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("отказано", StringComparison.OrdinalIgnoreCase))
            {
                errors.AppendLine(l.Trim());
            }
        }
        return errors.Length > 0 ? errors.ToString().TrimEnd() : log.Trim();
    }
}
