using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.Core.Hardware;

public class DisplayCheckResult
{
    public bool HasActiveDisplayGpu { get; set; }
    public bool HasIntegratedGpu { get; set; }
    public string IntegratedGpuName { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public int ConnectedDisplayCount { get; set; }
}

public class GpuDetector
{
    private readonly PcieDiagnosticService _pcieDiagnosticService;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int smIndex);
    private const int SM_CMONITORS = 80;

    public GpuDetector(PcieDiagnosticService? pcieDiagnosticService = null)
    {
        _pcieDiagnosticService = pcieDiagnosticService ?? new PcieDiagnosticService();
    }

    public List<GpuDeviceInfo> DetectGpus()
    {
        var gpus = new List<GpuDeviceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var gpu = MapFromWmi(obj);
                if (gpu != null)
                {
                    // Enhance with PCIe Link Width diagnostics
                    if (!string.IsNullOrEmpty(gpu.DeviceInstanceId))
                    {
                        var linkInfo = _pcieDiagnosticService.QueryPcieLinkInfo(gpu.DeviceInstanceId);
                        gpu.CurrentPcieLinkWidth = linkInfo.CurrentLinkWidth;
                        gpu.MaxPcieLinkWidth = linkInfo.MaxLinkWidth;
                        gpu.CurrentPcieLinkSpeed = linkInfo.CurrentLinkSpeed;
                        gpu.MaxPcieLinkSpeed = linkInfo.MaxLinkSpeed;
                    }

                    gpus.Add(gpu);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuDetector] WMI Win32_VideoController error: {ex.Message}");
        }

        // If WMI was empty, fallback to SetupDi or registry
        if (gpus.Count == 0)
        {
            gpus = FallbackDetectFromRegistry();
        }

        // Evaluate display output capabilities
        EvaluateDisplayCapabilities(gpus);

        return gpus;
    }

    private GpuDeviceInfo? MapFromWmi(ManagementObject obj)
    {
        string name = obj["Name"]?.ToString() ?? "Unknown Video Adapter";
        string pnpId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
        string driverVersion = obj["DriverVersion"]?.ToString() ?? string.Empty;
        string driverDate = obj["DriverDate"]?.ToString() ?? string.Empty;
        string status = obj["Status"]?.ToString() ?? "OK";
        string videoProcessor = obj["VideoProcessor"]?.ToString() ?? string.Empty;
        ulong ram = 0;
        if (obj["AdapterRAM"] != null && ulong.TryParse(obj["AdapterRAM"].ToString(), out ulong parsedRam))
        {
            ram = parsedRam;
        }

        var (vendorId, deviceId, subsys, rev) = ParsePnpId(pnpId);

        var info = new GpuDeviceInfo
        {
            Name = name,
            HardwareId = pnpId,
            DeviceInstanceId = pnpId,
            VendorId = vendorId,
            DeviceId = deviceId,
            SubsystemId = subsys,
            RevisionId = rev,
            VideoProcessor = videoProcessor,
            DriverVersion = driverVersion,
            DriverDate = driverDate,
            Status = status,
            AdapterRamBytes = ram
        };

        info.IsIntegratedGpu = DetermineIfIntegrated(vendorId, name);

        return info;
    }

    public static (string VendorId, string DeviceId, string SubsystemId, string RevisionId) ParsePnpId(string pnpId)
    {
        string ven = string.Empty;
        string dev = string.Empty;
        string subsys = string.Empty;
        string rev = string.Empty;

        if (string.IsNullOrWhiteSpace(pnpId))
            return (ven, dev, subsys, rev);

        var venMatch = Regex.Match(pnpId, @"VEN_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
        if (venMatch.Success) ven = venMatch.Groups[1].Value.ToUpperInvariant();

        var devMatch = Regex.Match(pnpId, @"DEV_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
        if (devMatch.Success) dev = "DEV_" + devMatch.Groups[1].Value.ToUpperInvariant();

        var subsysMatch = Regex.Match(pnpId, @"SUBSYS_([0-9A-Fa-f]{8})", RegexOptions.IgnoreCase);
        if (subsysMatch.Success) subsys = subsysMatch.Groups[1].Value.ToUpperInvariant();

        var revMatch = Regex.Match(pnpId, @"REV_([0-9A-Fa-f]{2})", RegexOptions.IgnoreCase);
        if (revMatch.Success) rev = revMatch.Groups[1].Value.ToUpperInvariant();

        return (ven, dev, subsys, rev);
    }

    private bool DetermineIfIntegrated(string vendorId, string name)
    {
        string lowerName = name.ToLowerInvariant();
        // Intel iGPU: VEN_8086 with HD/UHD/Iris
        if (vendorId.Equals("8086", StringComparison.OrdinalIgnoreCase))
        {
            if (lowerName.Contains("intel") || lowerName.Contains("hd graphics") ||
                lowerName.Contains("uhd graphics") || lowerName.Contains("iris"))
            {
                return true;
            }
        }

        // AMD APU iGPU: VEN_1002
        if (vendorId.Equals("1002", StringComparison.OrdinalIgnoreCase))
        {
            if (lowerName.Contains("radeon(tm) graphics") || lowerName.Contains("radeon graphics") ||
                lowerName.Contains("vega") || lowerName.Contains("apu"))
            {
                return true;
            }
        }

        return false;
    }

    private void EvaluateDisplayCapabilities(List<GpuDeviceInfo> gpus)
    {
        int monitorCount = GetSystemMetrics(SM_CMONITORS);
        if (monitorCount <= 0) monitorCount = 1;

        var iGpu = gpus.FirstOrDefault(g => g.IsIntegratedGpu);
        if (iGpu != null)
        {
            iGpu.IsDisplayOutputActive = true;
        }

        // Also check if any non-CMP card exists with active status
        var nonCmpDiscrete = gpus.FirstOrDefault(g => !g.IsCmpCard && !g.IsIntegratedGpu && g.Status == "OK");
        if (nonCmpDiscrete != null)
        {
            nonCmpDiscrete.IsDisplayOutputActive = true;
        }
    }

    public DisplayCheckResult CheckDisplayOutputConfiguration(List<GpuDeviceInfo> gpus)
    {
        int monitorCount = GetSystemMetrics(SM_CMONITORS);
        var iGpu = gpus.FirstOrDefault(g => g.IsIntegratedGpu);
        var secondaryDisplayGpu = gpus.FirstOrDefault(g => !g.IsCmpCard && !g.IsIntegratedGpu);

        bool hasOutputGpu = (iGpu != null) || (secondaryDisplayGpu != null);

        string summary;
        if (iGpu != null)
        {
            summary = $"Обнаружено активное видеоядро {iGpu.Name}. Вывод изображения и Cross-Adapter Presentation будут работать штатно через монитор, подключенный к материнской плате.";
        }
        else if (secondaryDisplayGpu != null)
        {
            summary = $"Обнаружен второй видеоадаптер ({secondaryDisplayGpu.Name}) для вывода картинки на монитор. Рендер игр будет перенаправлен на CMP карту.";
        }
        else
        {
            summary = "Внимание: Видеокарты CMP 30HX и 40HX не имеют физических видеовыходов! Для вывода изображения в играх требуется активный iGPU (Intel HD/UHD или AMD Radeon) либо вторая видеокарта. Если процессор без встроенного видеоядра (например, Intel F-серии), потребуется дополнительная видеокарта-затычка.";
        }

        return new DisplayCheckResult
        {
            HasActiveDisplayGpu = hasOutputGpu,
            HasIntegratedGpu = iGpu != null,
            IntegratedGpuName = iGpu?.Name ?? string.Empty,
            ConnectedDisplayCount = monitorCount,
            SummaryText = summary
        };
    }

    private List<GpuDeviceInfo> FallbackDetectFromRegistry()
    {
        var list = new List<GpuDeviceInfo>();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (key == null) return list;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                if (!Regex.IsMatch(subKeyName, @"^\d{4}$")) continue;
                using var item = key.OpenSubKey(subKeyName);
                if (item == null) continue;

                string name = item.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                string pnpId = item.GetValue("MatchingDeviceId")?.ToString() ?? string.Empty;
                string ver = item.GetValue("DriverVersion")?.ToString() ?? string.Empty;

                if (!string.IsNullOrEmpty(name))
                {
                    var (ven, dev, subsys, rev) = ParsePnpId(pnpId);
                    list.Add(new GpuDeviceInfo
                    {
                        Name = name,
                        HardwareId = pnpId,
                        DeviceInstanceId = pnpId,
                        VendorId = ven,
                        DeviceId = dev,
                        SubsystemId = subsys,
                        RevisionId = rev,
                        DriverVersion = ver,
                        Status = "OK",
                        IsIntegratedGpu = DetermineIfIntegrated(ven, name)
                    });
                }
            }
        }
        catch
        {
            // Ignore registry read errors
        }

        return list;
    }
}
