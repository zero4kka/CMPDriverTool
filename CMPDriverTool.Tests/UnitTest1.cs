using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CMPDriverTool.Core.Certificate;
using CMPDriverTool.Core.Gaming;
using CMPDriverTool.Core.Hardware;
using CMPDriverTool.Core.Manifest;
using CMPDriverTool.Core.Models;
using CMPDriverTool.Core.Patcher;
using CMPDriverTool.Core.Tools;
using Xunit;

namespace CMPDriverTool.Tests;

public class GpuDetectionTests
{
    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_2187&SUBSYS_123410DE&REV_A1", "10DE", "DEV_2187", true, false, true)]
    [InlineData("PCI\\VEN_10DE&DEV_2188&SUBSYS_567810DE&REV_A1", "10DE", "DEV_2188", true, false, true)]
    [InlineData("PCI\\VEN_10DE&DEV_2184&SUBSYS_ABCD10DE&REV_A1", "10DE", "DEV_2184", false, true, true)]
    [InlineData("PCI\\VEN_10DE&DEV_1F07&SUBSYS_DCBA10DE&REV_A1", "10DE", "DEV_1F07", false, true, true)]
    [InlineData("PCI\\VEN_10DE&DEV_2189&SUBSYS_8E951462&REV_A1", "10DE", "DEV_2189", true, false, true)]
    [InlineData("PCI\\VEN_10DE&DEV_2189&SUBSYS_123410DE&REV_A1", "10DE", "DEV_2189", false, false, false)]
    [InlineData("PCI\\VEN_8086&DEV_0412&SUBSYS_D0001458&REV_06", "8086", "DEV_0412", false, false, false)]
    public void ParsePnpId_IdentifiesExpectedCmpCards(
        string pnpId, string expectedVen, string expectedDev,
        bool expectCmp30, bool expectCmp40, bool expectCmp)
    {
        var (ven, dev, subsys, rev) = GpuDetector.ParsePnpId(pnpId);
        Assert.Equal(expectedVen, ven);
        Assert.Equal(expectedDev, dev);

        var info = new GpuDeviceInfo
        {
            VendorId = ven,
            DeviceId = dev,
            SubsystemId = subsys,
            HardwareId = pnpId
        };

        Assert.Equal(expectCmp30, info.IsCmp30Hx);
        Assert.Equal(expectCmp40, info.IsCmp40Hx);
        Assert.Equal(expectCmp, info.IsCmpCard);
    }

    [Fact]
    public void PcieLinkWarning_TriggersOnlyWhenMaxLinkWidthIsOne()
    {
        // 1. Physical x1 limited card (unpopulated capacitors on mining board)
        var cmpCardHardwareLimited = new GpuDeviceInfo
        {
            DeviceId = "DEV_2187",
            CurrentPcieLinkWidth = 1,
            MaxPcieLinkWidth = 1
        };
        Assert.True(cmpCardHardwareLimited.HasPcieLaneWarning);
        Assert.False(cmpCardHardwareLimited.IsPciePowerSaving);
        Assert.Equal("x1 (Макс: x1)", cmpCardHardwareLimited.PcieLinkSummary);
        Assert.Contains("0.1 мкФ", cmpCardHardwareLimited.PcieWarningMessage);
        Assert.Contains("конденсаторы", cmpCardHardwareLimited.PcieWarningMessage);

        // 2. Full-speed x16 card dropping to x1 in idle power saving (ASPM)
        // MUST NOT show capacitor warning! Must show clear idle status.
        var cmpCardIdlePowerSaving = new GpuDeviceInfo
        {
            DeviceId = "DEV_2187",
            CurrentPcieLinkWidth = 1,
            MaxPcieLinkWidth = 16
        };
        Assert.False(cmpCardIdlePowerSaving.HasPcieLaneWarning);
        Assert.True(cmpCardIdlePowerSaving.IsPciePowerSaving);
        Assert.Equal(string.Empty, cmpCardIdlePowerSaving.PcieWarningMessage);
        Assert.Equal("x1 (Энергосбережение в простое, Макс: x16)", cmpCardIdlePowerSaving.PcieLinkSummary);

        // 3. Full-speed x16 card under 3D load running at x16
        var cmpCardFullLoad = new GpuDeviceInfo
        {
            DeviceId = "DEV_2187",
            CurrentPcieLinkWidth = 16,
            MaxPcieLinkWidth = 16
        };
        Assert.False(cmpCardFullLoad.HasPcieLaneWarning);
        Assert.False(cmpCardFullLoad.IsPciePowerSaving);
        Assert.Equal("x16 (Макс: x16)", cmpCardFullLoad.PcieLinkSummary);

        // 4. Non-CMP GPU (regular GeForce) at x1 should never trigger CMP capacitor warning
        var regularGpu = new GpuDeviceInfo
        {
            DeviceId = "DEV_2189", // GTX 1660 Super
            CurrentPcieLinkWidth = 1,
            MaxPcieLinkWidth = 1
        };
        Assert.False(regularGpu.HasPcieLaneWarning);

        // 5. Uninitialized / unknown link width
        var uninitialized = new GpuDeviceInfo
        {
            DeviceId = "DEV_2187",
            CurrentPcieLinkWidth = 0,
            MaxPcieLinkWidth = 0
        };
        Assert.False(uninitialized.HasPcieLaneWarning);
        Assert.Contains("Не определено", uninitialized.PcieLinkSummary);
    }

    [Fact]
    public void PcieDiagnosticService_DevPropKeyPids_AreStandardSdkValues()
    {
        // Windows SDK DEVPKEY_PciDevice properties (verified on Windows PnP subsystem):
        // CurrentLinkSpeed must be pid 9 (DEVPKEY_PciDevice_CurrentLinkSpeed)
        Assert.Equal(9u, PcieDiagnosticService.PidCurrentLinkSpeed);

        // CurrentLinkWidth must be pid 10 (DEVPKEY_PciDevice_CurrentLinkWidth)
        Assert.Equal(10u, PcieDiagnosticService.PidCurrentLinkWidth);

        // MaxLinkSpeed must be pid 11 (DEVPKEY_PciDevice_MaxLinkSpeed)
        Assert.Equal(11u, PcieDiagnosticService.PidMaxLinkSpeed);

        // MaxLinkWidth must be pid 12 (DEVPKEY_PciDevice_MaxLinkWidth)
        Assert.Equal(12u, PcieDiagnosticService.PidMaxLinkWidth);
    }

    [Fact]
    public void RealSystem_GpuDetector_ExecutesWithoutException()
    {
        var detector = new GpuDetector();
        var gpus = detector.DetectGpus();

        Assert.NotNull(gpus);
        Assert.NotEmpty(gpus);

        var nvidiaGpu = gpus.FirstOrDefault(g => g.VendorId.Equals("10DE", StringComparison.OrdinalIgnoreCase));
        if (nvidiaGpu != null)
        {
            // The real user GPU physically has 16 PCIe lanes (MaxLinkWidth == 16)
            Assert.Equal(16, nvidiaGpu.MaxPcieLinkWidth);
            // Must NOT show false soldering alert!
            Assert.False(nvidiaGpu.HasPcieLaneWarning);
            Assert.Contains("x16", nvidiaGpu.PcieLinkSummary);
        }

        var displayCheck = detector.CheckDisplayOutputConfiguration(gpus);
        Assert.NotNull(displayCheck);
        Assert.False(string.IsNullOrEmpty(displayCheck.SummaryText));
    }
}

public class ManifestTests
{
    [Fact]
    public async Task ManifestManager_LoadsDriversSuccessfully()
    {
        var manager = new ManifestManager();
        var drivers = await manager.GetAvailableDriversAsync();

        Assert.NotEmpty(drivers);
        Assert.Contains(drivers, d => d.Version == "552.22");
        Assert.Contains(drivers, d => d.Version == "537.58");

        var recommended = drivers.First(d => d.IsRecommended);
        Assert.Equal("552.22", recommended.Version);
    }
}

public class DartraidenPatcherTests
{
    [Fact]
    public void PatchInfFile_InjectsCmpDevicesAndDartraidenTweaks()
    {
        string sampleInf = @"
[Version]
Signature   = ""$Windows NT$""
Provider    = %NVIDIA%
ClassGUID   = {4d36e968-e325-11ce-bfc1-08002be10318}
Class       = Display
CatalogFile = nvami.cat

[NVIDIA_Devices.NTamd64.10.0...17098]
%NVIDIA_DEV.2182% = Section012, PCI\VEN_10DE&DEV_2182
%NVIDIA_DEV.1F08% = Section015, PCI\VEN_10DE&DEV_1F08

[Section012]
AddReg = nv_commonBase_addreg

[nv_commonBase_addreg]
HKR,,InstalledDisplayDrivers,%REG_MULTI_SZ%,nvldumdx,nvumdshimx

[Strings]
NVIDIA = ""NVIDIA""
NVIDIA_DEV.2182 = ""NVIDIA GeForce GTX 1660 Ti""
NVIDIA_DEV.1F08 = ""NVIDIA GeForce RTX 2060""
";

        string tempInfPath = Path.Combine(Path.GetTempPath(), $"test_dartraiden_{Guid.NewGuid():N}.inf");
        try
        {
            File.WriteAllText(tempInfPath, sampleInf);

            var patcher = new DartraidenPatcherService();
            var res = patcher.PatchInfFile(tempInfPath, useNvencPreset: false);

            Assert.True(res.Success);
            Assert.Equal("Section012", res.SectionTU116);
            Assert.Equal("Section015", res.SectionTU106);

            string patchedContent = File.ReadAllText(tempInfPath);

            // Verify device IDs are present
            Assert.Contains("DEV_2187", patchedContent);
            Assert.Contains("DEV_2188", patchedContent);
            Assert.Contains("DEV_2189", patchedContent);
            Assert.Contains("DEV_2184", patchedContent);
            Assert.Contains("DEV_1F07", patchedContent);
            Assert.Contains("DEV_1F0B", patchedContent);

            // Verify dartraiden cross-adapter & D3D tweaks
            Assert.Contains("EnableCrossAdapterPresentation", patchedContent);
            Assert.Contains("EnableMsHybrid", patchedContent);
            Assert.Contains("AdapterType", patchedContent);
            Assert.Contains("DisableMiningLock", patchedContent);
            Assert.Contains("RmTCCMode", patchedContent);
            Assert.Contains("AddReg = dartraiden_cmp_gaming_addreg", patchedContent);

            // Verify CatalogFile is preserved for Windows Driver Store compatibility
            Assert.Contains("CatalogFile", patchedContent);
            Assert.DoesNotContain(";CatalogFile", patchedContent);

            // Verify strings table
            Assert.Contains("NVIDIA CMP 30HX (Gaming Edition - TU116)", patchedContent);
            Assert.Contains("NVIDIA CMP 40HX (Gaming Edition - TU106)", patchedContent);

            // NVENC preset was false, so limits should not be altered in this run
            Assert.DoesNotContain("NvEncMaxConcurrentSessions", patchedContent);
        }
        finally
        {
            if (File.Exists(tempInfPath)) File.Delete(tempInfPath);
            if (File.Exists(tempInfPath + ".bak")) File.Delete(tempInfPath + ".bak");
        }
    }

    [Fact]
    public void PatchInfFile_WithNvencPreset_InjectsNvencKeys()
    {
        string sampleInf = @"
[Version]
Signature   = ""$Windows NT$""
Provider    = %NVIDIA%
ClassGUID   = {4d36e968-e325-11ce-bfc1-08002be10318}
Class       = Display
CatalogFile = nvami.cat

[NVIDIA_Devices.NTamd64.10.0...17098]
%NVIDIA_DEV.2182% = Section012, PCI\VEN_10DE&DEV_2182

[Section012]
AddReg = nv_commonBase_addreg

[nv_commonBase_addreg]
HKR,,InstalledDisplayDrivers,%REG_MULTI_SZ%,nvldumdx

[Strings]
NVIDIA = ""NVIDIA""
NVIDIA_DEV.2182 = ""NVIDIA GeForce GTX 1660 Ti""
";

        string tempInfPath = Path.Combine(Path.GetTempPath(), $"test_nvenc_{Guid.NewGuid():N}.inf");
        try
        {
            File.WriteAllText(tempInfPath, sampleInf);

            var patcher = new DartraidenPatcherService();
            var res = patcher.PatchInfFile(tempInfPath, useNvencPreset: true);

            Assert.True(res.Success);
            string patchedContent = File.ReadAllText(tempInfPath);

            Assert.Contains("NvEncEnableEncodeLimits", patchedContent);
            Assert.Contains("NvEncMaxConcurrentSessions", patchedContent);
            Assert.Contains("NvFbcEnableCaptureLimits", patchedContent);
        }
        finally
        {
            if (File.Exists(tempInfPath)) File.Delete(tempInfPath);
            if (File.Exists(tempInfPath + ".bak")) File.Delete(tempInfPath + ".bak");
        }
    }

    [Fact]
    public void PatchInfFile_WithDecoratedSection_LinksAddRegSection()
    {
        string sampleInf = @"
[Version]
Signature   = ""$Windows NT$""
Provider    = %NVIDIA%
ClassGUID   = {4d36e968-e325-11ce-bfc1-08002be10318}
Class       = Display
CatalogFile = nvami.cat

[NVIDIA_Devices.NTamd64.10.0...17098]
%NVIDIA_DEV.2182% = Section012, PCI\VEN_10DE&DEV_2182

[Section012.NTamd64.10.0]
AddReg = nv_commonBase_addreg

[nv_commonBase_addreg.NTamd64]
HKR,,InstalledDisplayDrivers,%REG_MULTI_SZ%,nvldumdx

[Strings]
NVIDIA = ""NVIDIA""
NVIDIA_DEV.2182 = ""NVIDIA GeForce GTX 1660 Ti""
";

        string tempInfPath = Path.Combine(Path.GetTempPath(), $"test_decorated_{Guid.NewGuid():N}.inf");
        try
        {
            File.WriteAllText(tempInfPath, sampleInf);

            var patcher = new DartraidenPatcherService();
            var res = patcher.PatchInfFile(tempInfPath, useNvencPreset: false);

            Assert.True(res.Success);
            string patchedContent = File.ReadAllText(tempInfPath);

            // Must link AddReg = dartraiden_cmp_gaming_addreg to the decorated section
            Assert.Contains("AddReg = dartraiden_cmp_gaming_addreg", patchedContent);
            Assert.Contains("EnableCrossAdapterPresentation", patchedContent);
            Assert.Contains("EnableMsHybrid", patchedContent);
        }
        finally
        {
            if (File.Exists(tempInfPath)) File.Delete(tempInfPath);
            if (File.Exists(tempInfPath + ".bak")) File.Delete(tempInfPath + ".bak");
        }
    }

    [Fact]
    public void DartraidenPatcherService_ValidGitHubUrlsAndPaths()
    {
        var service = new DartraidenPatcherService();
        Assert.Equal("https://github.com/dartraiden/NVIDIA-patcher", DartraidenPatcherService.GitHubRepoUrl);
        Assert.Contains("master.zip", DartraidenPatcherService.GitHubZipDownloadUrl);

        string dir = service.GetPatcherDirectory(@"C:\Temp\Test");
        Assert.Equal(@"C:\Temp\Test\DartraidenPatcher", dir);
    }

    [Fact]
    public void DartraidenPatcherService_FindTargetInf_LocatesCandidateFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"inffind_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            string dummyInf = Path.Combine(tempDir, "nvami.inf");
            File.WriteAllText(dummyInf, "; test");

            var service = new DartraidenPatcherService();
            string? found = service.FindTargetInf(tempDir);

            Assert.NotNull(found);
            Assert.Equal(dummyInf, found);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

public class DebloatServiceTests
{
    [Fact]
    public void CleanDriverPackage_RemovesBloatAndTelemetryFolders()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"debloat_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "NvTelemetry"));
            File.WriteAllText(Path.Combine(tempRoot, "NvTelemetry", "telemetry.dll"), "dummy");

            Directory.CreateDirectory(Path.Combine(tempRoot, "GFExperience"));
            File.WriteAllText(Path.Combine(tempRoot, "GFExperience", "gfe.exe"), "dummy");

            Directory.CreateDirectory(Path.Combine(tempRoot, "Display.Driver"));
            File.WriteAllText(Path.Combine(tempRoot, "Display.Driver", "nvami.inf"), "dummy");
            File.WriteAllText(Path.Combine(tempRoot, "Display.Driver", "NvTelemetryAPI64.dll"), "dummy");

            var debloater = new DebloatService();
            var removed = debloater.CleanDriverPackage(tempRoot);

            Assert.NotEmpty(removed);
            Assert.False(Directory.Exists(Path.Combine(tempRoot, "NvTelemetry")));
            Assert.False(Directory.Exists(Path.Combine(tempRoot, "GFExperience")));
            Assert.False(File.Exists(Path.Combine(tempRoot, "Display.Driver", "NvTelemetryAPI64.dll")));

            // Display driver INF must remain
            Assert.True(File.Exists(Path.Combine(tempRoot, "Display.Driver", "nvami.inf")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }
}

public class DirectXRegistryTests
{
    [Theory]
    [InlineData("GpuPreference=2;", DirectXGpuPreference.HighPerformance)]
    [InlineData("GpuPreference=1;", DirectXGpuPreference.PowerSaving)]
    [InlineData("GpuPreference=0;", DirectXGpuPreference.Unspecified)]
    [InlineData("AutoData=foo;GpuPreference=2;SpecificAdapter=10DE;", DirectXGpuPreference.HighPerformance)]
    [InlineData("", DirectXGpuPreference.Unspecified)]
    public void ParsePreferenceString_MapsCorrectly(string raw, DirectXGpuPreference expected)
    {
        var result = DirectXRegistryManager.ParsePreferenceString(raw);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SetAndRemoveGpuPreference_RoundTripsInRegistry()
    {
        var manager = new DirectXRegistryManager();
        string dummyGamePath = @"C:\FakeGame\TestGame123.exe";

        bool setOk = manager.SetGpuPreference(dummyGamePath, DirectXGpuPreference.HighPerformance);
        Assert.True(setOk);

        var all = manager.GetAllPreferences();
        Assert.True(all.ContainsKey(dummyGamePath));
        Assert.Equal(DirectXGpuPreference.HighPerformance, all[dummyGamePath]);

        bool removeOk = manager.RemoveGpuPreference(dummyGamePath);
        Assert.True(removeOk);

        var afterRemove = manager.GetAllPreferences();
        Assert.False(afterRemove.ContainsKey(dummyGamePath));
    }
}

public class CertificateTests
{
    [Fact]
    public void CreateSelfSignedCodeSigningCertificate_GeneratesValidCert()
    {
        using var cert = CertificateManager.CreateSelfSignedCodeSigningCertificate();

        Assert.NotNull(cert);
        Assert.Contains("CMP Gaming Driver Test CA", cert.Subject);
        Assert.True(cert.HasPrivateKey);

        // Verify Code Signing EKU (1.3.6.1.5.5.7.3.3)
        bool hasCodeSigning = cert.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .Any(ext => ext.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.3"));

        Assert.True(hasCodeSigning);
    }

    [Fact]
    public void EnsureTestCertificate_ExportsBothCerAndPfx()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cert_test_{Guid.NewGuid():N}");
        try
        {
            var mgr = new CertificateManager();
            var res = mgr.EnsureTestCertificate(tempDir);

            Assert.True(res.Success);
            Assert.True(File.Exists(Path.Combine(tempDir, "CMPTestDriver.cer")));
            Assert.True(File.Exists(Path.Combine(tempDir, "CMPTestDriver.pfx")));

            // Test signing runner
            bool signOk = mgr.SignDriverPackage(tempDir);
            Assert.True(signOk);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

public class PnputilParserTests
{
    [Fact]
    public void ParsePnputilDrivers_ParsesOemPackages()
    {
        string sampleOutput = @"
Опубликованное имя :     oem45.inf
Исходное имя :          nvami.inf
Имя поставщика :        NVIDIA
Имя класса :            Графические адаптеры
Дата драйвера :          04/16/2024
Версия драйвера :       31.0.15.5222

Опубликованное имя :     oem12.inf
Исходное имя :          prnms001.inf
Имя поставщика :        Microsoft
Имя класса :            Принтеры
";

        var list = SystemToolsService.ParsePnputilDrivers(sampleOutput);
        Assert.Equal(2, list.Count);

        var nv = list[0];
        Assert.Equal("oem45.inf", nv.PublishedName);
        Assert.Equal("nvami.inf", nv.OriginalName);
        Assert.Equal("NVIDIA", nv.ProviderName);
        Assert.True(nv.IsNvidiaDisplayDriver);

        var ms = list[1];
        Assert.Equal("oem12.inf", ms.PublishedName);
        Assert.False(ms.IsNvidiaDisplayDriver);
    }
}

public class ConvertersTests
{
    [Fact]
    public void EnumToBooleanConverter_ConvertsCorrectly()
    {
        var conv = new CMPDriverTool.App.Converters.EnumToBooleanConverter();

        object trueRes = conv.Convert(CMPDriverTool.App.ViewModels.NavigationTab.Installer, typeof(bool), "Installer", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((bool)trueRes);

        object falseRes = conv.Convert(CMPDriverTool.App.ViewModels.NavigationTab.Dashboard, typeof(bool), "Installer", System.Globalization.CultureInfo.InvariantCulture);
        Assert.False((bool)falseRes);

        object backRes = conv.ConvertBack(true, typeof(CMPDriverTool.App.ViewModels.NavigationTab), "Games", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(CMPDriverTool.App.ViewModels.NavigationTab.Games, backRes);
    }

    [Fact]
    public void EnumToVisibilityConverter_ConvertsCorrectly()
    {
        var conv = new CMPDriverTool.App.Converters.EnumToVisibilityConverter();

        object vis = conv.Convert(CMPDriverTool.App.ViewModels.NavigationTab.Tools, typeof(System.Windows.Visibility), "Tools", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(System.Windows.Visibility.Visible, vis);

        object col = conv.Convert(CMPDriverTool.App.ViewModels.NavigationTab.Dashboard, typeof(System.Windows.Visibility), "Tools", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(System.Windows.Visibility.Collapsed, col);
    }

    [Fact]
    public void BoolToStringConverter_FormatsCorrectly()
    {
        var conv = new CMPDriverTool.App.Converters.BoolToStringConverter();

        object trueStr = conv.Convert(true, typeof(string), "Да|Нет", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("Да", trueStr);

        object falseStr = conv.Convert(false, typeof(string), "Да|Нет", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("Нет", falseStr);
    }
}

public class ViewModelTests
{
    [Fact]
    public void MainViewModel_NavigationCommand_SwitchesCurrentTab()
    {
        var vm = new CMPDriverTool.App.ViewModels.MainViewModel();
        Assert.Equal(CMPDriverTool.App.ViewModels.NavigationTab.Dashboard, vm.CurrentTab);

        vm.NavigateCommand.Execute(CMPDriverTool.App.ViewModels.NavigationTab.Games);
        Assert.Equal(CMPDriverTool.App.ViewModels.NavigationTab.Games, vm.CurrentTab);

        vm.NavigateCommand.Execute(CMPDriverTool.App.ViewModels.NavigationTab.Tools);
        Assert.Equal(CMPDriverTool.App.ViewModels.NavigationTab.Tools, vm.CurrentTab);
    }

    [Fact]
    public void InstallerViewModel_ToggleLogExpanded_ChangesState()
    {
        var vm = new CMPDriverTool.App.ViewModels.InstallerViewModel(() => { });
        Assert.True(vm.IsLogExpanded);

        vm.ToggleLogExpandedCommand.Execute(null);
        Assert.False(vm.IsLogExpanded);

        vm.ToggleLogExpandedCommand.Execute(null);
        Assert.True(vm.IsLogExpanded);
    }

    [Fact]
    public void DashboardViewModel_StartInstall_PassesNvencOptionToCallback()
    {
        DriverManifestItem? passedDriver = null;
        string? passedPath = null;
        bool? passedNvenc = null;
        string? passedGpuName = null;
        bool? passedLaunchSetup = null;

        var vm = new CMPDriverTool.App.ViewModels.DashboardViewModel(
            onNavigateToInstall: (driver, path, nvenc, name, launchSetup) =>
            {
                passedDriver = driver;
                passedPath = path;
                passedNvenc = nvenc;
                passedGpuName = name;
                passedLaunchSetup = launchSetup;
            });

        var dummyDriver = new DriverManifestItem { Version = "552.22" };
        vm.AvailableDrivers.Add(dummyDriver);
        vm.SelectedDriver = dummyDriver;
        vm.UseNvencPreset = true;

        vm.StartInstallCommand.Execute(null);

        Assert.NotNull(passedDriver);
        Assert.Equal("552.22", passedDriver.Version);
        Assert.Null(passedPath);
        Assert.True(passedNvenc);
        Assert.False(string.IsNullOrWhiteSpace(passedGpuName));
        Assert.True(passedLaunchSetup);
    }

    [Fact]
    public void GpuDisplayNamePresets_ProvidesExpectedPresetsFor30HxAnd40Hx()
    {
        var gpu30 = new GpuDeviceInfo { DeviceId = "DEV_2189" };
        var presets30 = GpuDisplayNamePresets.GetPresetsForGpu(gpu30);
        Assert.Contains(presets30, p => p.NameValue == "NVIDIA GeForce GTX 1660 SUPER");
        Assert.Contains(presets30, p => p.NameValue == "NVIDIA CMP 30HX");
        Assert.Contains(presets30, p => p.IsCustom);

        var gpu40 = new GpuDeviceInfo { DeviceId = "DEV_2184" };
        var presets40 = GpuDisplayNamePresets.GetPresetsForGpu(gpu40);
        Assert.Contains(presets40, p => p.NameValue == "NVIDIA GeForce RTX 2060 SUPER");
        Assert.Contains(presets40, p => p.NameValue == "NVIDIA CMP 40HX");
        Assert.Contains(presets40, p => p.IsCustom);
    }

    [Fact]
    public void PatchInfFile_InjectsCustomGpuNameIntoStringsSection()
    {
        string sampleInf =
            "[Version]\n" +
            "Signature = \"$Windows NT$\"\n" +
            "CatalogFile = NV_DISP.CAT\n\n" +
            "[NVIDIA_Devices.NTamd64.10.0...14393]\n" +
            "%NVIDIA_DEV.2182% = Section001, PCI\\VEN_10DE&DEV_2182\n\n" +
            "[Section001]\n" +
            "AddReg = nv_commonBase_addreg\n\n" +
            "[nv_commonBase_addreg]\n\n" +
            "[Strings]\n" +
            "NVIDIA_DEV.2182 = \"NVIDIA GeForce GTX 1660 Ti\"\n";

        string tempInf = Path.Combine(Path.GetTempPath(), $"test_custom_name_{Guid.NewGuid():N}.inf");
        File.WriteAllText(tempInf, sampleInf);

        try
        {
            var patcher = new DartraidenPatcherService();
            string customName = "NVIDIA GeForce GTX 1660 SUPER";
            var result = patcher.PatchInfFile(tempInf, useNvencPreset: false, customGpuName: customName);

            Assert.True(result.Success);
            string patched = File.ReadAllText(tempInf);
            Assert.Contains($"NVIDIA_DEV.2189 = \"{customName}\"", patched);
            Assert.Contains($"NVIDIA_DEV.2187 = \"{customName}\"", patched);
        }
        finally
        {
            if (File.Exists(tempInf)) File.Delete(tempInf);
            if (File.Exists(tempInf + ".bak")) File.Delete(tempInf + ".bak");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RenameInstalledGpu_ValidatesEmptyOrNullName(string? invalidName)
    {
        var service = new SystemToolsService();
        var (ok, msg) = service.RenameInstalledGpu(invalidName!);
        Assert.False(ok);
        Assert.Contains("не может быть пустым", msg);
    }

    [Fact]
    public void DashboardViewModel_NameSelection_UpdatesCustomFlagAndName()
    {
        var vm = new CMPDriverTool.App.ViewModels.DashboardViewModel(
            onNavigateToInstall: (driver, path, nvenc, name, launchSetup) => { });

        Assert.NotEmpty(vm.GpuNamePresets);
        var customOption = vm.GpuNamePresets.FirstOrDefault(p => p.IsCustom);
        Assert.NotNull(customOption);

        vm.SelectedGpuNameOption = customOption;
        Assert.True(vm.IsCustomGpuName);

        var presetOption = vm.GpuNamePresets.FirstOrDefault(p => !p.IsCustom);
        Assert.NotNull(presetOption);

        vm.SelectedGpuNameOption = presetOption;
        Assert.False(vm.IsCustomGpuName);
        Assert.Equal(presetOption.NameValue, vm.CustomGpuNameText);
    }
}

public class XamlResourceTests
{
    [Fact]
    public void AppResources_ContainRequiredStylesAndCanInstantiateViews()
    {
        Exception? threadEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new CMPDriverTool.App.App();
                    app.InitializeComponent();
                }

                var resources = System.Windows.Application.Current.Resources;
                Assert.NotNull(resources["PrimaryButtonStyle"]);
                Assert.NotNull(resources["SecondaryButtonStyle"]);
                Assert.NotNull(resources["AccentButtonStyle"]);
                Assert.NotNull(resources["TextPrimary"]);
                Assert.NotNull(resources["TextSecondary"]);
                Assert.NotNull(resources["WarningBrush"]);

                var view = new CMPDriverTool.App.Views.InstallerView();
                Assert.NotNull(view);

                var dashView = new CMPDriverTool.App.Views.DashboardView();
                Assert.NotNull(dashView);

                var toolsView = new CMPDriverTool.App.Views.ToolsView();
                Assert.NotNull(toolsView);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw new Exception($"STA Thread failed: {threadEx.Message}", threadEx);
        }
    }
}
