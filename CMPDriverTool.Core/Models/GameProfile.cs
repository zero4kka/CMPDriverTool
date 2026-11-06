using System.IO;

namespace CMPDriverTool.Core.Models;

public enum DirectXGpuPreference
{
    Unspecified = 0,
    PowerSaving = 1,      // Typically iGPU
    HighPerformance = 2   // Target CMP card
}

public class GameProfile
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = "Steam"; // Steam, Epic, Manual
    public DirectXGpuPreference Preference { get; set; } = DirectXGpuPreference.Unspecified;

    public bool IsCmpPreferred
    {
        get => Preference == DirectXGpuPreference.HighPerformance;
        set => Preference = value ? DirectXGpuPreference.HighPerformance : DirectXGpuPreference.Unspecified;
    }

    public string ExecutableFileName => string.IsNullOrEmpty(ExecutablePath) ? string.Empty : Path.GetFileName(ExecutablePath);
    public string DirectoryPath => string.IsNullOrEmpty(ExecutablePath) ? string.Empty : (Path.GetDirectoryName(ExecutablePath) ?? string.Empty);
}
