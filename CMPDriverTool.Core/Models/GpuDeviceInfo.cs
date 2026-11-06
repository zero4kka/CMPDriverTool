using System;

namespace CMPDriverTool.Core.Models;

public enum GpuArchitectureFamily
{
    Unknown,
    TuringTu116_CMP30HX,
    TuringTu106_CMP40HX,
    GeForceDesktop,
    IntegratedGraphics,
    Other
}

public class GpuDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string VendorId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string SubsystemId { get; set; } = string.Empty;
    public string RevisionId { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string DeviceInstanceId { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string DriverDate { get; set; } = string.Empty;
    public string Status { get; set; } = "OK";
    public string VideoProcessor { get; set; } = string.Empty;
    public ulong AdapterRamBytes { get; set; }

    public bool IsCmp30Hx => DeviceId.Equals("DEV_2187", StringComparison.OrdinalIgnoreCase) ||
                             DeviceId.Equals("DEV_2188", StringComparison.OrdinalIgnoreCase) ||
                             HardwareId.Contains("DEV_2187", StringComparison.OrdinalIgnoreCase) ||
                             HardwareId.Contains("DEV_2188", StringComparison.OrdinalIgnoreCase) ||
                             VideoProcessor.Contains("CMP 30HX", StringComparison.OrdinalIgnoreCase) ||
                             Name.Contains("CMP 30HX", StringComparison.OrdinalIgnoreCase) ||
                             (DeviceId.Equals("DEV_2189", StringComparison.OrdinalIgnoreCase) &&
                              (VideoProcessor.Contains("CMP", StringComparison.OrdinalIgnoreCase) ||
                               Name.Contains("CMP", StringComparison.OrdinalIgnoreCase) ||
                               SubsystemId.Equals("8E951462", StringComparison.OrdinalIgnoreCase) ||
                               HardwareId.Contains("8E951462", StringComparison.OrdinalIgnoreCase)));

    public bool IsCmp40Hx => DeviceId.Equals("DEV_2184", StringComparison.OrdinalIgnoreCase) ||
                             DeviceId.Equals("DEV_1F07", StringComparison.OrdinalIgnoreCase) ||
                             DeviceId.Equals("DEV_1F02", StringComparison.OrdinalIgnoreCase) ||
                             DeviceId.Equals("DEV_1F06", StringComparison.OrdinalIgnoreCase) ||
                             HardwareId.Contains("DEV_2184", StringComparison.OrdinalIgnoreCase) ||
                             HardwareId.Contains("DEV_1F07", StringComparison.OrdinalIgnoreCase) ||
                             VideoProcessor.Contains("CMP 40HX", StringComparison.OrdinalIgnoreCase) ||
                             Name.Contains("CMP 40HX", StringComparison.OrdinalIgnoreCase);

    public bool IsCmpCard => IsCmp30Hx || IsCmp40Hx;

    public bool IsIntegratedGpu { get; set; }
    public bool IsDisplayOutputActive { get; set; }

    public int CurrentPcieLinkWidth { get; set; } = 0; // 1, 4, 8, 16
    public int MaxPcieLinkWidth { get; set; } = 0;
    public int CurrentPcieLinkSpeed { get; set; } = 0; // 1 = 2.5GT/s, 2 = 5GT/s, 3 = 8GT/s, 4 = 16GT/s
    public int MaxPcieLinkSpeed { get; set; } = 0;

    public GpuArchitectureFamily ArchitectureFamily
    {
        get
        {
            if (IsCmp30Hx) return GpuArchitectureFamily.TuringTu116_CMP30HX;
            if (IsCmp40Hx) return GpuArchitectureFamily.TuringTu106_CMP40HX;
            if (IsIntegratedGpu) return GpuArchitectureFamily.IntegratedGraphics;
            if (VendorId.Equals("10DE", StringComparison.OrdinalIgnoreCase)) return GpuArchitectureFamily.GeForceDesktop;
            return GpuArchitectureFamily.Other;
        }
    }

    public string ArchitectureDisplayName => ArchitectureFamily switch
    {
        GpuArchitectureFamily.TuringTu116_CMP30HX => "NVIDIA Turing TU116 (CMP 30HX 6GB)",
        GpuArchitectureFamily.TuringTu106_CMP40HX => "NVIDIA Turing TU106 (CMP 40HX 8GB)",
        GpuArchitectureFamily.GeForceDesktop => "NVIDIA GeForce / Quadro",
        GpuArchitectureFamily.IntegratedGraphics => "Встроенное видеоядро (iGPU)",
        _ => "Видеоадаптер"
    };

    public bool HasPcieLaneWarning => IsCmpCard && MaxPcieLinkWidth > 0 && MaxPcieLinkWidth <= 1;

    public bool IsPciePowerSaving => CurrentPcieLinkWidth == 1 && MaxPcieLinkWidth >= 4;

    public string PcieLinkSummary
    {
        get
        {
            if (CurrentPcieLinkWidth <= 0 && MaxPcieLinkWidth <= 0)
                return "Не определено (требуются права администратора или драйвер)";

            string max = MaxPcieLinkWidth > 0 ? $"x{MaxPcieLinkWidth}" : "x16";

            if (CurrentPcieLinkWidth == 1 && MaxPcieLinkWidth >= 4)
            {
                return $"x1 (Энергосбережение в простое, Макс: {max})";
            }

            string curr = CurrentPcieLinkWidth > 0 ? $"x{CurrentPcieLinkWidth}" : "x?";
            return $"{curr} (Макс: {max})";
        }
    }

    public string PcieWarningMessage => HasPcieLaneWarning
        ? "Внимание: Видеокарта аппаратно ограничена шиной PCIe x1 (Max Link Width = x1)! На данной майнинговой плате CMP не распаяны SMD-конденсаторы PCIe (0.1 мкФ / 100 нФ) по линиям 2-16. В современных 3D-играх это приводит к сильным статтерам, микрофризам и задержкам текстурирования. Для стабильной игры на x16 рекомендуется допаять конденсаторы типоразмера 0402 0.1 uF."
        : string.Empty;
}
