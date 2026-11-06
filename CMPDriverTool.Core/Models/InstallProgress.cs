using System;

namespace CMPDriverTool.Core.Models;

public enum InstallStage
{
    Idle,
    Downloading,
    Extracting,
    Debloating,
    PatchingInf,
    GeneratingCertificate,
    InstallingDriver,
    Completed,
    Failed
}

public class InstallProgressReport
{
    public InstallStage Stage { get; set; }
    public string StageTitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; } // 0..100
    public bool IsIndeterminate { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");
}
