using System;
using System.Diagnostics;
using System.Security.Principal;
using CMPDriverTool.Core.Certificate;
using CMPDriverTool.Core.Models;
using Microsoft.Win32;

namespace CMPDriverTool.Core.Hardware;

public class SecurityService
{
    public SystemSecurityInfo CheckSystemSecurity()
    {
        var info = new SystemSecurityInfo
        {
            IsAdministrator = CheckIsAdministrator(),
            OsVersion = Environment.OSVersion.VersionString,
            Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86"
        };

        // 1. Check Secure Boot
        info.SecureBootEnabled = CheckSecureBoot();

        // 2. Check Test Mode & NoIntegrityChecks
        info.TestModeEnabled = CheckTestMode();
        info.NoIntegrityChecksEnabled = CheckNoIntegrityChecks();

        // 3. Assemble recommendations
        if (info.SecureBootEnabled == true)
        {
            info.Warnings.Add("Secure Boot включен в UEFI. Он блокирует загрузку неподписанных/модифицированных драйверов и активацию Test Mode. Для работы модифицированного драйвера необходимо временно отключить Secure Boot в BIOS/UEFI материнской платы.");
        }

        if (!info.TestModeEnabled)
        {
            info.Warnings.Add("Тестовый режим (Testsigning) отключен. Модифицированный драйвер требует включения Test Mode для загрузки ядра видеодрайвера.");
        }

        if (!info.IsAdministrator)
        {
            info.Warnings.Add("Программа запущена без прав администратора. Установка драйверов и настройка bcdedit потребуют повышения привилегий.");
        }

        return info;
    }

    public static bool CheckIsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool? CheckSecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key != null)
            {
                object? val = key.GetValue("UEFISecureBootEnabled");
                if (val is int intVal)
                {
                    return intVal == 1;
                }
            }
        }
        catch
        {
            // Fall back
        }

        // Silent PowerShell fallback
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"Confirm-SecureBootUEFI\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(2000);
                if (bool.TryParse(output, out bool res))
                {
                    return res;
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    public static bool CheckTestMode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            if (key != null)
            {
                string? options = key.GetValue("SystemStartOptions")?.ToString();
                if (!string.IsNullOrEmpty(options) && options.IndexOf("TESTSIGNING", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore
        }

        // Check via bcdedit if elevated
        try
        {
            var (exitCode, stdout, _) = RunCommand("bcdedit.exe", "/enum {current}");
            if (exitCode == 0 && (stdout.IndexOf("testsigning             Yes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  stdout.IndexOf("testsigning             Да", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  System.Text.RegularExpressions.Regex.IsMatch(stdout, @"testsigning\s+(?:Yes|Да|True|1)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    public static bool CheckNoIntegrityChecks()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            if (key != null)
            {
                string? options = key.GetValue("SystemStartOptions")?.ToString();
                if (!string.IsNullOrEmpty(options) && options.IndexOf("NOINTEGRITYCHECKS", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore
        }

        try
        {
            var (exitCode, stdout, _) = RunCommand("bcdedit.exe", "/enum {current}");
            if (exitCode == 0 && (stdout.IndexOf("nointegritychecks       Yes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  stdout.IndexOf("nointegritychecks       Да", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  System.Text.RegularExpressions.Regex.IsMatch(stdout, @"nointegritychecks\s+(?:Yes|Да|True|1)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    public (bool Success, string Message) SetTestMode(bool enable)
    {
        if (!CheckIsAdministrator())
        {
            return (false, "Для изменения параметров BCD требуются права Администратора.");
        }

        string arg = enable ? "on" : "off";
        var res1 = RunCommand("bcdedit.exe", $"/set testsigning {arg}");
        if (res1.ExitCode != 0)
        {
            return (false, $"Ошибка bcdedit /set testsigning {arg}: {res1.StdErr}\n{res1.StdOut}");
        }

        // Also configure nointegritychecks accordingly
        if (enable)
        {
            RunCommand("bcdedit.exe", "/set nointegritychecks on");
        }
        else
        {
            RunCommand("bcdedit.exe", "/set nointegritychecks off");
        }

        string action = enable ? "включен" : "отключен";
        return (true, $"Тестовый режим (Testsigning) успешно {action}. Для применения изменений требуется перезагрузка Windows.");
    }

    public static (int ExitCode, string StdOut, string StdErr) RunCommand(string fileName, string arguments)
    {
        try
        {
            var encoding = DriverInstaller.GetConsoleEncoding();
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (-1, string.Empty, "Не удалось запустить процесс.");
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
