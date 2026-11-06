using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using CMPDriverTool.Core.Certificate;

namespace CMPDriverTool.Core.Tools;

public class OemDriverPackage
{
    public string PublishedName { get; set; } = string.Empty; // e.g. oem42.inf
    public string OriginalName { get; set; } = string.Empty;  // e.g. nvami.inf
    public string ProviderName { get; set; } = string.Empty;  // e.g. NVIDIA
    public string ClassName { get; set; } = string.Empty;     // e.g. Display
    public string DriverVersion { get; set; } = string.Empty;
    public string DriverDate { get; set; } = string.Empty;

    public bool IsNvidiaDisplayDriver =>
        (ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase) ||
         ClassName.Equals("Графические адаптеры", StringComparison.OrdinalIgnoreCase)) &&
        ProviderName.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
}

public class SystemToolsService
{
    private const byte VK_LWIN = 0x5B;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_B = 0x42;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet,
        uint MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        byte[]? PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiSetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        byte[]? PropertyBuffer,
        uint PropertyBufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint SPDRP_DEVICEDESC = 0x00000000;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;

    private static readonly Guid DisplayClassGuid = new Guid("{4d36e968-e325-11ce-bfc1-08002be10318}");

    public bool RestartGpuDriver()
    {
        try
        {
            // Simulate: Win + Ctrl + Shift + B press
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            keybd_event(VK_B, 0, 0, UIntPtr.Zero);

            // Small delay
            System.Threading.Thread.Sleep(50);

            // Release keys in reverse
            keybd_event(VK_B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RestartGpuDriver] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<List<OemDriverPackage>> EnumerateDisplayDriversAsync()
    {
        var list = new List<OemDriverPackage>();

        try
        {
            var encoding = DriverInstaller.GetConsoleEncoding();
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = "/enum-drivers",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            };

            using var process = Process.Start(psi);
            if (process == null) return list;

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            list = ParsePnputilDrivers(output);
        }
        catch { }

        return list;
    }

    public static List<OemDriverPackage> ParsePnputilDrivers(string output)
    {
        var list = new List<OemDriverPackage>();
        var blocks = output.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var oem = new OemDriverPackage();
            var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line[..colon].Trim();
                string val = line[(colon + 1)..].Trim();

                if (key.Contains("Опубликованное имя") || key.Contains("Published Name"))
                    oem.PublishedName = val;
                else if (key.Contains("Исходное имя") || key.Contains("Original Name"))
                    oem.OriginalName = val;
                else if (key.Contains("Имя поставщика") || key.Contains("Provider Name"))
                    oem.ProviderName = val;
                else if (key.Contains("Имя класса") || key.Contains("Class Name"))
                    oem.ClassName = val;
                else if (key.Contains("Версия драйвера") || key.Contains("Driver Version"))
                    oem.DriverVersion = val;
                else if (key.Contains("Дата драйвера") || key.Contains("Driver Date"))
                    oem.DriverDate = val;
            }

            if (!string.IsNullOrEmpty(oem.PublishedName))
            {
                list.Add(oem);
            }
        }

        return list;
    }

    public async Task<(bool Success, string Message)> DeleteDriverPackageAsync(string publishedOemName, bool force = true)
    {
        try
        {
            string forceArg = force ? "/force" : "";
            var encoding = DriverInstaller.GetConsoleEncoding();
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/delete-driver {publishedOemName} /uninstall {forceArg}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            };

            using var process = Process.Start(psi);
            if (process == null) return (false, "Не удалось запустить pnputil.exe.");

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                return (true, $"Пакет драйвера {publishedOemName} успешно удален.");
            }
            return (false, $"Ошибка удаления {publishedOemName}: {stdout} {stderr}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Полная автоматическая DDU-подобная зачистка всех видеодрайверов NVIDIA из DriverStore
    /// </summary>
    public async Task<(int DeletedCount, List<string> Logs)> CleanUninstallAllNvidiaDriversAsync(IProgress<string>? progress = null)
    {
        var logs = new List<string>();
        int deletedCount = 0;

        progress?.Report("Поиск установленных видеодрайверов NVIDIA в хранилище DriverStore...");
        var allDrivers = await EnumerateDisplayDriversAsync();

        var nvidiaDrivers = new List<OemDriverPackage>();
        foreach (var d in allDrivers)
        {
            if (d.IsNvidiaDisplayDriver ||
                d.OriginalName.StartsWith("nv", StringComparison.OrdinalIgnoreCase) ||
                d.PublishedName.Equals("oem1.inf", StringComparison.OrdinalIgnoreCase) && d.ProviderName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                nvidiaDrivers.Add(d);
            }
        }

        if (nvidiaDrivers.Count == 0)
        {
            logs.Add("Видеодрайверов NVIDIA в хранилище DriverStore не обнаружено.");
            return (0, logs);
        }

        foreach (var drv in nvidiaDrivers)
        {
            progress?.Report($"Удаление пакета {drv.PublishedName} ({drv.OriginalName} v{drv.DriverVersion})...");
            var (ok, msg) = await DeleteDriverPackageAsync(drv.PublishedName, true);
            string status = ok ? "УСПЕШНО УДАЛЕН" : $"ОШИБКА ({msg})";
            logs.Add($"{drv.PublishedName} ({drv.OriginalName}): {status}");
            if (ok) deletedCount++;
        }

        // Send GPU reset signal to restore basic display adapter
        progress?.Report("Сброс графического стека Windows...");
        RestartGpuDriver();

        return (deletedCount, logs);
    }

    /// <summary>
    /// Переименовывает установленную видеокарту CMP в системе (в Диспетчере задач, Диспетчере устройств и dxdiag)
    /// без необходимости переустанавливать драйвер.
    /// </summary>
    public (bool Success, string Message) RenameInstalledGpu(string newDisplayName, string? targetHardwareId = null)
    {
        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            return (false, "Имя видеокарты не может быть пустым.");
        }

        newDisplayName = newDisplayName.Trim();
        int registryClassUpdated = 0;
        int setupApiUpdated = 0;

        // 1. Update HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}
        // This is the primary key that Task Manager (Диспетчер задач -> Производительность -> GPU) and dxdiag read!
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true);
            if (classKey != null)
            {
                foreach (string subkeyName in classKey.GetSubKeyNames())
                {
                    if (int.TryParse(subkeyName, out _))
                    {
                        try
                        {
                            using var devSubKey = classKey.OpenSubKey(subkeyName, true);
                            if (devSubKey != null)
                            {
                                string matchDevId = devSubKey.GetValue("MatchingDeviceId")?.ToString() ?? "";
                                string driverDesc = devSubKey.GetValue("DriverDesc")?.ToString() ?? "";
                                string provider = devSubKey.GetValue("ProviderName")?.ToString() ?? "";

                                bool isMatch = false;
                                if (!string.IsNullOrEmpty(targetHardwareId) && matchDevId.IndexOf(targetHardwareId, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    isMatch = true;
                                }
                                else if (matchDevId.IndexOf("2189", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         matchDevId.IndexOf("2188", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         matchDevId.IndexOf("2187", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         matchDevId.IndexOf("2184", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         matchDevId.IndexOf("1F07", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         matchDevId.IndexOf("1F0B", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         driverDesc.IndexOf("CMP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         (provider.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                          driverDesc.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) < 0 &&
                                          driverDesc.IndexOf("spacedesk", StringComparison.OrdinalIgnoreCase) < 0))
                                {
                                    isMatch = true;
                                }

                                if (isMatch)
                                {
                                    devSubKey.SetValue("DriverDesc", newDisplayName, RegistryValueKind.String);
                                    registryClassUpdated++;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RenameInstalledGpu] Registry Class error: {ex.Message}");
        }

        // 2. Update via SetupAPI (Official Windows PnP API for Device Manager)
        try
        {
            Guid classGuid = DisplayClassGuid;
            IntPtr hDevInfo = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
            if (hDevInfo != IntPtr.Zero && hDevInfo.ToInt64() != -1)
            {
                try
                {
                    SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                    for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref devInfoData); i++)
                    {
                        string hwId = GetDevicePropertyString(hDevInfo, ref devInfoData, SPDRP_HARDWAREID);
                        string desc = GetDevicePropertyString(hDevInfo, ref devInfoData, SPDRP_DEVICEDESC);

                        bool isMatch = false;
                        if (!string.IsNullOrEmpty(targetHardwareId) && hwId.IndexOf(targetHardwareId, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isMatch = true;
                        }
                        else if (hwId.IndexOf("2189", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 hwId.IndexOf("2188", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 hwId.IndexOf("2187", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 hwId.IndexOf("2184", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 hwId.IndexOf("1F07", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 hwId.IndexOf("1F0B", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 desc.IndexOf("CMP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isMatch = true;
                        }

                        if (isMatch)
                        {
                            byte[] nameBytes = Encoding.Unicode.GetBytes(newDisplayName + "\0");
                            // Set FriendlyName (shown in Device Manager)
                            SetupDiSetDeviceRegistryProperty(hDevInfo, ref devInfoData, SPDRP_FRIENDLYNAME, nameBytes, (uint)nameBytes.Length);
                            // Set DeviceDesc
                            SetupDiSetDeviceRegistryProperty(hDevInfo, ref devInfoData, SPDRP_DEVICEDESC, nameBytes, (uint)nameBytes.Length);
                            setupApiUpdated++;
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(hDevInfo);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RenameInstalledGpu] SetupAPI error: {ex.Message}");
        }

        // 3. Update Enum\PCI directly if accessible
        try
        {
            using var pciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (pciKey != null)
            {
                foreach (string devFolder in pciKey.GetSubKeyNames())
                {
                    if (devFolder.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        (devFolder.IndexOf("2189", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         devFolder.IndexOf("2188", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         devFolder.IndexOf("2187", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         devFolder.IndexOf("2184", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         devFolder.IndexOf("1F07", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         devFolder.IndexOf("1F0B", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (!string.IsNullOrEmpty(targetHardwareId) && devFolder.IndexOf(targetHardwareId, StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        using var devSubKey = pciKey.OpenSubKey(devFolder);
                        if (devSubKey != null)
                        {
                            foreach (string instName in devSubKey.GetSubKeyNames())
                            {
                                try
                                {
                                    using var instKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\PCI\{devFolder}\{instName}", true);
                                    if (instKey != null)
                                    {
                                        instKey.SetValue("FriendlyName", newDisplayName, RegistryValueKind.String);
                                        instKey.SetValue("DeviceDesc", newDisplayName, RegistryValueKind.String);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 4. Send GPU driver reset (Win+Ctrl+Shift+B) so Task Manager picks up new DriverDesc
        RestartGpuDriver();

        if (registryClassUpdated > 0 || setupApiUpdated > 0)
        {
            return (true, $"Имя видеокарты успешно изменено на \"{newDisplayName}\"! Оно обновлено в Диспетчере задач и Диспетчере устройств.");
        }
        else
        {
            return (false, "Не удалось найти подходящую видеокарту CMP в реестре или требуются права администратора.");
        }
    }

    private static string GetDevicePropertyString(IntPtr hDevInfo, ref SP_DEVINFO_DATA devInfoData, uint property)
    {
        byte[] buffer = new byte[1024];
        if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, property, out _, buffer, (uint)buffer.Length, out uint reqSize))
        {
            return Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
        }
        return string.Empty;
    }
}
