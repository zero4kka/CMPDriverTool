using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CMPDriverTool.Core.Models;
using Microsoft.Win32;

namespace CMPDriverTool.Core.Gaming;

public class DirectXRegistryManager
{
    private const string UserGpuPreferencesSubKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public Dictionary<string, DirectXGpuPreference> GetAllPreferences()
    {
        var result = new Dictionary<string, DirectXGpuPreference>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPreferencesSubKey, false);
            if (key == null) return result;

            foreach (var valueName in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName) || valueName.Equals("DirectXUserGlobalSettings", StringComparison.OrdinalIgnoreCase))
                    continue;

                object? rawVal = key.GetValue(valueName);
                if (rawVal is string strVal)
                {
                    result[valueName] = ParsePreferenceString(strVal);
                }
            }
        }
        catch
        {
            // Ignore registry read errors
        }

        return result;
    }

    public static DirectXGpuPreference ParsePreferenceString(string preferenceString)
    {
        if (string.IsNullOrWhiteSpace(preferenceString))
            return DirectXGpuPreference.Unspecified;

        // Pattern: GpuPreference=(\d+)
        var match = Regex.Match(preferenceString, @"GpuPreference=(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int prefVal))
        {
            return prefVal switch
            {
                1 => DirectXGpuPreference.PowerSaving,
                2 => DirectXGpuPreference.HighPerformance,
                _ => DirectXGpuPreference.Unspecified
            };
        }

        return DirectXGpuPreference.Unspecified;
    }

    public bool SetGpuPreference(string executablePath, DirectXGpuPreference preference)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UserGpuPreferencesSubKey, true);
            if (key == null) return false;

            if (preference == DirectXGpuPreference.Unspecified)
            {
                // Delete or set to 0
                string? existing = key.GetValue(executablePath)?.ToString();
                if (!string.IsNullOrEmpty(existing))
                {
                    key.DeleteValue(executablePath, false);
                }
                return true;
            }

            int valNumber = (int)preference;
            string newValueString = $"GpuPreference={valNumber};";

            // If there was already existing AutoData or other flags, preserve them
            string? currentVal = key.GetValue(executablePath)?.ToString();
            if (!string.IsNullOrEmpty(currentVal))
            {
                if (Regex.IsMatch(currentVal, @"GpuPreference=\d+;?"))
                {
                    newValueString = Regex.Replace(currentVal, @"GpuPreference=\d+;?", $"GpuPreference={valNumber};");
                }
                else
                {
                    newValueString = currentVal.TrimEnd(';') + $";GpuPreference={valNumber};";
                }
            }

            key.SetValue(executablePath, newValueString, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RemoveGpuPreference(string executablePath)
    {
        return SetGpuPreference(executablePath, DirectXGpuPreference.Unspecified);
    }
}
