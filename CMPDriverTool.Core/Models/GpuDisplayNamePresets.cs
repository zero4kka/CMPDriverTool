using System;
using System.Collections.Generic;

namespace CMPDriverTool.Core.Models;

public class GpuDisplayNameOption
{
    public string DisplayTitle { get; set; } = string.Empty;
    public string NameValue { get; set; } = string.Empty;
    public bool IsCustom { get; set; }

    public override string ToString() => DisplayTitle;
}

public static class GpuDisplayNamePresets
{
    public const string DefaultCustomPlaceholder = "NVIDIA GeForce GTX 1660 SUPER";

    public static List<GpuDisplayNameOption> GetPresetsForGpu(GpuDeviceInfo? gpu)
    {
        bool is40hx = gpu?.IsCmp40Hx == true;
        var list = new List<GpuDisplayNameOption>();

        if (is40hx)
        {
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA GeForce RTX 2060 SUPER (Рекомендуется для игр)",
                NameValue = "NVIDIA GeForce RTX 2060 SUPER"
            });
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA CMP 40HX (Заводское имя)",
                NameValue = "NVIDIA CMP 40HX"
            });
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA CMP 40HX (Gaming Edition)",
                NameValue = "NVIDIA CMP 40HX (Gaming Edition)"
            });
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA CMP 40HX / RTX 2060 SUPER (Gaming Edition - TU106)",
                NameValue = "NVIDIA CMP 40HX / RTX 2060 SUPER (Gaming Edition - TU106)"
            });
        }
        else
        {
            // Default: CMP 30HX (TU116) or general CMP
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA GeForce GTX 1660 SUPER (Рекомендуется для игр)",
                NameValue = "NVIDIA GeForce GTX 1660 SUPER"
            });
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA CMP 30HX (Заводское имя)",
                NameValue = "NVIDIA CMP 30HX"
            });
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA CMP 30HX (Gaming Edition)",
                NameValue = "NVIDIA CMP 30HX (Gaming Edition)"
            });
            list.Add(new GpuDisplayNameOption
            {
                DisplayTitle = "NVIDIA CMP 30HX / GTX 1660 SUPER (Gaming Edition - TU116)",
                NameValue = "NVIDIA CMP 30HX / GTX 1660 SUPER (Gaming Edition - TU116)"
            });
        }

        list.Add(new GpuDisplayNameOption
        {
            DisplayTitle = "✏️ Своё имя...",
            NameValue = is40hx ? "NVIDIA GeForce RTX 2060 SUPER" : "NVIDIA GeForce GTX 1660 SUPER",
            IsCustom = true
        });

        return list;
    }
}
