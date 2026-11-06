using System.Collections.Generic;

namespace CMPDriverTool.Core.Models;

public class SystemSecurityInfo
{
    public bool? SecureBootEnabled { get; set; }
    public bool TestModeEnabled { get; set; }
    public bool NoIntegrityChecksEnabled { get; set; }
    public bool IsAdministrator { get; set; }
    public string OsVersion { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;

    public bool CanInstallCustomDriver => !SecureBootEnabled.GetValueOrDefault(false) && (TestModeEnabled || IsAdministrator);

    public List<string> Warnings { get; } = new();

    public string SecureBootStatusText => SecureBootEnabled switch
    {
        true => "Включен (Secure Boot ON)",
        false => "Отключен (Secure Boot OFF)",
        null => "Не удалось определить (Legacy BIOS или без UEFI)"
    };

    public string TestModeStatusText => TestModeEnabled
        ? "Включен (Test Mode ON)"
        : "Выключен (Test Mode OFF)";
}
