using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CMPDriverTool.Core.Hardware;

[StructLayout(LayoutKind.Sequential)]
public struct DEVPROPKEY
{
    public Guid fmtid;
    public uint pid;

    public DEVPROPKEY(Guid guid, uint propId)
    {
        fmtid = guid;
        pid = propId;
    }
}

public class PcieLinkWidthInfo
{
    public int CurrentLinkWidth { get; set; }
    public int MaxLinkWidth { get; set; }
    public int CurrentLinkSpeed { get; set; }
    public int MaxLinkSpeed { get; set; }
}

public class PcieDiagnosticService
{
    private static readonly Guid PciDevicePropertyGuid = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
    private const uint CR_SUCCESS = 0x00000000;
    private const uint DEVPROP_TYPE_UINT32 = 0x00000007;

    [DllImport("CfgMgr32.dll", EntryPoint = "CM_Locate_DevNodeW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNode(out uint dnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_Property(
        uint dnDevInst,
        ref DEVPROPKEY propertyKey,
        out uint propertyType,
        IntPtr propertyBuffer,
        ref uint propertyBufferSize,
        uint ulFlags);

    // Windows PnP DEVPKEY_PciDevice properties (GUID {3AB22E31-8264-4B4E-9AF5-A8D2D8E33E62})
    // Verified on Windows PnP:
    // PID 8  = DEVPKEY_PciDevice_MaxReadRequestSize
    // PID 9  = DEVPKEY_PciDevice_CurrentLinkSpeed
    // PID 10 = DEVPKEY_PciDevice_CurrentLinkWidth
    // PID 11 = DEVPKEY_PciDevice_MaxLinkSpeed
    // PID 12 = DEVPKEY_PciDevice_MaxLinkWidth
    public const uint PidCurrentLinkSpeed = 9;
    public const uint PidCurrentLinkWidth = 10;
    public const uint PidMaxLinkSpeed = 11;
    public const uint PidMaxLinkWidth = 12;

    public PcieLinkWidthInfo QueryPcieLinkInfo(string deviceInstanceId)
    {
        var info = new PcieLinkWidthInfo();
        if (string.IsNullOrWhiteSpace(deviceInstanceId))
            return info;

        // 1. First attempt: Direct CfgMgr32 API
        try
        {
            if (TryQueryDevNodeProperties(deviceInstanceId, out var directInfo))
            {
                return directInfo;
            }
        }
        catch
        {
            // Fall through to fallback
        }

        // 2. Second attempt: Fallback using PowerShell Get-PnpDeviceProperty (silent)
        try
        {
            var fallbackInfo = QueryViaPowerShell(deviceInstanceId);
            if (fallbackInfo.CurrentLinkWidth > 0 || fallbackInfo.MaxLinkWidth > 0)
            {
                return fallbackInfo;
            }
        }
        catch
        {
            // Ignore fallback errors
        }

        return info;
    }

    private bool TryQueryDevNodeProperties(string deviceInstanceId, out PcieLinkWidthInfo result)
    {
        result = new PcieLinkWidthInfo();

        uint cr = CM_Locate_DevNode(out uint devInst, deviceInstanceId, 0);
        if (cr != CR_SUCCESS)
            return false;

        result.CurrentLinkSpeed = ReadUInt32Property(devInst, PidCurrentLinkSpeed);
        result.CurrentLinkWidth = ReadUInt32Property(devInst, PidCurrentLinkWidth);
        result.MaxLinkSpeed = ReadUInt32Property(devInst, PidMaxLinkSpeed);
        result.MaxLinkWidth = ReadUInt32Property(devInst, PidMaxLinkWidth);

        return result.CurrentLinkWidth > 0 || result.MaxLinkWidth > 0;
    }

    private int ReadUInt32Property(uint devInst, uint propId)
    {
        var key = new DEVPROPKEY(PciDevicePropertyGuid, propId);
        uint bufferSize = 64;
        IntPtr buffer = Marshal.AllocHGlobal(64);
        try
        {
            uint cr = CM_Get_DevNode_Property(devInst, ref key, out uint propType, buffer, ref bufferSize, 0);
            if (cr == CR_SUCCESS && bufferSize > 0)
            {
                if (bufferSize >= 4)
                    return Marshal.ReadInt32(buffer);
                if (bufferSize == 2)
                    return Marshal.ReadInt16(buffer);
                if (bufferSize == 1)
                    return Marshal.ReadByte(buffer);
            }
        }
        catch
        {
            // Ignore read errors
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return 0;
    }

    private PcieLinkWidthInfo QueryViaPowerShell(string deviceInstanceId)
    {
        var info = new PcieLinkWidthInfo();
        string escapedId = deviceInstanceId.Replace("\"", "`\"");
        string script = $"$p = Get-PnpDeviceProperty -InstanceId '{escapedId}' -ErrorAction SilentlyContinue; " +
                        "$cw = ($p | Where-Object { $_.KeyName -like '*CurrentLinkWidth*' }).Data; " +
                        "$mw = ($p | Where-Object { $_.KeyName -like '*MaxLinkWidth*' }).Data; " +
                        "$cs = ($p | Where-Object { $_.KeyName -like '*CurrentLinkSpeed*' }).Data; " +
                        "$ms = ($p | Where-Object { $_.KeyName -like '*MaxLinkSpeed*' }).Data; " +
                        "Write-Output \"$cw|$mw|$cs|$ms\"";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return info;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);

        if (!string.IsNullOrWhiteSpace(output))
        {
            string[] parts = output.Trim().Split('|');
            if (parts.Length >= 1 && int.TryParse(parts[0], out int cw)) info.CurrentLinkWidth = cw;
            if (parts.Length >= 2 && int.TryParse(parts[1], out int mw)) info.MaxLinkWidth = mw;
            if (parts.Length >= 3 && int.TryParse(parts[2], out int cs)) info.CurrentLinkSpeed = cs;
            if (parts.Length >= 4 && int.TryParse(parts[3], out int ms)) info.MaxLinkSpeed = ms;
        }

        return info;
    }
}
