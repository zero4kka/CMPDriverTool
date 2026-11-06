using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CMPDriverTool.Core.Hardware;
using CMPDriverTool.Core.Manifest;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.Core.Certificate;

public class CertificateResult
{
    public bool Success { get; set; }
    public string CertificateThumbprint { get; set; } = string.Empty;
    public string CertificatePath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class CertificateManager
{
    private const string CertificateSubject = "CN=CMP Gaming Driver Test CA, O=CMP Mod Community, OU=Driver Signing, C=RU";
    private const string FriendlyName = "CMP Gaming Driver Test Signing Certificate";

    public CertificateResult EnsureTestCertificate(string exportDirectory, IProgress<InstallProgressReport>? progress = null)
    {
        var result = new CertificateResult();
        Directory.CreateDirectory(exportDirectory);

        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.GeneratingCertificate,
            StageTitle = "Создание сертификата",
            Message = "Генерация тестового сертификата для подписи драйвера...",
            ProgressPercentage = 20
        });

        try
        {
            string cerPath = Path.Combine(exportDirectory, "CMPTestDriver.cer");
            string pfxPath = Path.Combine(exportDirectory, "CMPTestDriver.pfx");
            X509Certificate2? cert = null;

            // Check if existing valid PFX with private key is on disk
            if (File.Exists(pfxPath))
            {
                try
                {
                    var loaded = new X509Certificate2(pfxPath, "", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                    if (loaded.HasPrivateKey && loaded.NotAfter > DateTime.UtcNow)
                    {
                        cert = loaded;
                        progress?.Report(new InstallProgressReport
                        {
                            Stage = InstallStage.GeneratingCertificate,
                            StageTitle = "Сертификат найден",
                            Message = $"Используется ранее созданный тестовый сертификат (Отпечаток: {cert.Thumbprint[..8]}...)",
                            ProgressPercentage = 60
                        });
                    }
                }
                catch { }
            }

            if (cert == null)
            {
                // Generate new self-signed Code Signing certificate with exported private key
                cert = CreateSelfSignedCodeSigningCertificate();
                progress?.Report(new InstallProgressReport
                {
                    Stage = InstallStage.GeneratingCertificate,
                    StageTitle = "Установка сертификата",
                    Message = "Добавление сертификата в доверенные корневые центры и издатели...",
                    ProgressPercentage = 50
                });

                InstallToStores(cert);

                // Export to CER and PFX in export directory
                File.WriteAllBytes(cerPath, cert.Export(X509ContentType.Cert));
                byte[] pfxBytes = cert.Export(X509ContentType.Pfx, "");
                File.WriteAllBytes(pfxPath, pfxBytes);
            }
            else
            {
                InstallToStores(cert);
                if (!File.Exists(cerPath))
                {
                    File.WriteAllBytes(cerPath, cert.Export(X509ContentType.Cert));
                }
            }

            result.Success = true;
            result.CertificateThumbprint = cert.Thumbprint;
            result.CertificatePath = cerPath;
            result.Message = "Тестовый сертификат успешно подготовлен и установлен в систему.";

            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.GeneratingCertificate,
                StageTitle = "Сертификат готов",
                Message = $"Сертификат готов к использованию (Отпечаток: {cert.Thumbprint})",
                ProgressPercentage = 100
            });

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Ошибка создания/установки сертификата: {ex.Message}";
            return result;
        }
    }

    public X509Certificate2? FindExistingCertificate()
    {
        try
        {
            string pfxPath = Path.Combine(ManifestManager.DefaultWorkDirectory, "Certs", "CMPTestDriver.pfx");
            if (File.Exists(pfxPath))
            {
                var cert = new X509Certificate2(pfxPath, "", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                if (cert.HasPrivateKey && cert.NotAfter > DateTime.UtcNow) return cert;
            }

            using var myStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            myStore.Open(OpenFlags.ReadOnly);
            foreach (var cert in myStore.Certificates)
            {
                if (cert.Subject.Contains("CMP Gaming Driver Test CA", StringComparison.OrdinalIgnoreCase) && cert.HasPrivateKey)
                {
                    return cert;
                }
            }

            using var pubStore = new X509Store(StoreName.TrustedPublisher, StoreLocation.LocalMachine);
            pubStore.Open(OpenFlags.ReadOnly);
            foreach (var cert in pubStore.Certificates)
            {
                if (cert.Subject.Contains("CMP Gaming Driver Test CA", StringComparison.OrdinalIgnoreCase))
                {
                    return cert;
                }
            }
        }
        catch
        {
            // Ignore
        }
        return null;
    }

    public static X509Certificate2 CreateSelfSignedCodeSigningCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            CertificateSubject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Key usage: Digital Signature
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));

        // Enhanced Key Usage: Code Signing (1.3.6.1.5.5.7.3.3)
        var eku = new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") };
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, critical: false));

        // Basic Constraints: Subject Type = End Entity or CA
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));

        // Subject Key Identifier
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        cert.FriendlyName = FriendlyName;
        return cert;
    }

    public void InstallToStores(X509Certificate2 cert)
    {
        // Add to LocalMachine (silent: succeeds if elevated, throws AccessDenied without popup if not)
        bool rootAdded = TryAddToStore(cert, StoreName.Root, StoreLocation.LocalMachine);
        bool pubAdded = TryAddToStore(cert, StoreName.TrustedPublisher, StoreLocation.LocalMachine);
        bool myAdded = TryAddToStore(cert, StoreName.My, StoreLocation.LocalMachine);

        // Also add to CurrentUser TrustedPublisher and My (these NEVER trigger modal dialogs)
        TryAddToStore(cert, StoreName.TrustedPublisher, StoreLocation.CurrentUser);
        TryAddToStore(cert, StoreName.My, StoreLocation.CurrentUser);

        // Fallback to certutil ONLY if elevated
        if (SecurityService.CheckIsAdministrator() && (!rootAdded || !pubAdded))
        {
            string tempCer = Path.Combine(Path.GetTempPath(), "cmp_temp_install.cer");
            try
            {
                File.WriteAllBytes(tempCer, cert.Export(X509ContentType.Cert));
                RunCertutilAddStore("Root", tempCer);
                RunCertutilAddStore("TrustedPublisher", tempCer);
            }
            finally
            {
                try { File.Delete(tempCer); } catch { }
            }
        }
    }

    private bool TryAddToStore(X509Certificate2 cert, StoreName storeName, StoreLocation location = StoreLocation.LocalMachine)
    {
        // Never attempt CurrentUser\Root because Windows OS displays an interactive security modal prompt
        if (location == StoreLocation.CurrentUser && storeName == StoreName.Root)
            return false;

        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RunCertutilAddStore(string storeName, string certFile)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "certutil.exe",
                Arguments = $"-f -addstore {storeName} \"{certFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch
        {
            // Ignore
        }
    }

    public bool SignDriverPackage(string driverRootDirectory, X509Certificate2? cert = null, IProgress<InstallProgressReport>? progress = null)
    {
        progress?.Report(new InstallProgressReport
        {
            Stage = InstallStage.GeneratingCertificate,
            StageTitle = "Подпись компонентов драйвера",
            Message = "Подпись бинарных файлов и каталогов тестовым сертификатом (Code Signing)...",
            ProgressPercentage = 70
        });

        try
        {
            cert ??= FindExistingCertificate();
            string pfxPath = Path.Combine(ManifestManager.DefaultWorkDirectory, "Certs", "CMPTestDriver.pfx");
            string displayDriverDir = Path.Combine(driverRootDirectory, "Display.Driver");
            if (!Directory.Exists(displayDriverDir)) displayDriverDir = driverRootDirectory;

            string? signtool = FindSignTool();
            var filesToSign = new List<string>();

            if (Directory.Exists(displayDriverDir))
            {
                filesToSign.AddRange(Directory.GetFiles(displayDriverDir, "*.cat"));
                filesToSign.AddRange(Directory.GetFiles(displayDriverDir, "*.sys"));
                string[] keyDlls = { "nvd3dum.dll", "nvd3dumx.dll", "nvwgf2um.dll", "nvwgf2umx.dll" };
                foreach (var dll in keyDlls)
                {
                    string fullDll = Path.Combine(displayDriverDir, dll);
                    if (File.Exists(fullDll)) filesToSign.Add(fullDll);
                }
            }

            if (filesToSign.Count == 0)
            {
                return true;
            }

            int signedCount = 0;
            foreach (var file in filesToSign)
            {
                bool signed = false;
                if (!string.IsNullOrEmpty(signtool) && File.Exists(pfxPath))
                {
                    signed = RunSignToolWithPfx(signtool, file, pfxPath);
                }

                if (!signed && !string.IsNullOrEmpty(signtool) && cert != null)
                {
                    signed = RunSignToolWithThumbprint(signtool, file, cert.Thumbprint);
                }

                if (!signed && File.Exists(pfxPath))
                {
                    signed = SignViaPowerShellPfx(file, pfxPath);
                }

                if (signed) signedCount++;
            }

            progress?.Report(new InstallProgressReport
            {
                Stage = InstallStage.GeneratingCertificate,
                StageTitle = "Подпись завершена",
                Message = $"Успешно подписано компонентов драйвера: {signedCount} из {filesToSign.Count}",
                ProgressPercentage = 100
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private string? FindSignTool()
    {
        string[] candidates =
        {
            Path.Combine(ManifestManager.DefaultWorkDirectory, "DartraidenPatcher", "_Tools", "_Utils", "x64", "ms", "signtool.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CMPDriverTool", "Work", "DartraidenPatcher", "_Tools", "_Utils", "x64", "ms", "signtool.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signtool.exe"),
            @"C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
            @"C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe",
            @"C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    private bool RunSignToolWithPfx(string signtoolExe, string targetFilePath, string pfxPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = signtoolExe,
                Arguments = $"sign /fd sha256 /f \"{pfxPath}\" \"{targetFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool RunSignToolWithThumbprint(string signtoolExe, string targetFilePath, string thumbprint)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = signtoolExe,
                Arguments = $"sign /v /s TrustedPublisher /sha1 {thumbprint} /fd SHA256 \"{targetFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool SignViaPowerShellPfx(string targetFilePath, string pfxPath)
    {
        try
        {
            string script = $"$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('{pfxPath.Replace("'", "''")}', ''); " +
                            $"Set-AuthenticodeSignature -FilePath '{targetFilePath.Replace("'", "''")}' -Certificate $cert -HashAlgorithm SHA256 -ErrorAction SilentlyContinue";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

