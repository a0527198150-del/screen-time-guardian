using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class BrowserProcessDetector
{
    private static readonly string[] KnownBrowserNames =
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "chromium", "tor"
    };

    private static readonly string[] KnownBrowserProducts =
    {
        "chrome", "microsoft edge", "firefox", "brave", "opera", "vivaldi", "chromium", "tor browser"
    };

    private readonly BrowserApprovalPolicy _approvalPolicy = new();

    public ProcessDescriptor? Describe(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var version = FileVersionInfo.GetVersionInfo(path);
            var publisher = version.CompanyName ?? string.Empty;
            var product = version.ProductName ?? string.Empty;
            var originalName = version.OriginalFilename ?? string.Empty;
            var signature = ReadSignature(path);

            return new ProcessDescriptor
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                ExecutablePath = path,
                CompanyName = publisher,
                ProductName = product,
                OriginalFilename = originalName,
                SignedPublisher = signature.Publisher,
                SignatureValid = signature.Valid
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool LooksLikeBrowser(ProcessDescriptor descriptor)
    {
        var values = new[]
        {
            descriptor.ProcessName,
            descriptor.OriginalFilename,
            descriptor.ProductName,
            descriptor.CompanyName,
            descriptor.ExecutablePath
        };

        return values.Any(value => KnownBrowserNames.Any(name => ContainsToken(value, name)))
            || values.Any(value => KnownBrowserProducts.Any(product => value.Contains(product, StringComparison.OrdinalIgnoreCase)));
    }

    public bool IsApproved(ProcessDescriptor descriptor, IReadOnlyCollection<BrowserApproval> approvals)
    {
        var decision = _approvalPolicy.Evaluate(new BrowserIdentity
        {
            DisplayName = descriptor.ProductName,
            Publisher = string.IsNullOrWhiteSpace(descriptor.SignedPublisher)
                ? descriptor.CompanyName
                : descriptor.SignedPublisher,
            ProductName = descriptor.ProductName,
            ExecutablePath = descriptor.ExecutablePath,
            SignatureValid = descriptor.SignatureValid
        }, approvals);

        return decision.Approved;
    }

    private static bool ContainsToken(string value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace('\\', ' ').Replace('/', ' ').Replace('_', ' ').Replace('-', ' ');
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static SignatureInfo ReadSignature(string path)
    {
#pragma warning disable SYSLIB0057
        try
        {
            using var certificate = X509Certificate.CreateFromSignedFile(path);
            var publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
            certificate.Verify();
            return new SignatureInfo(true, publisher);
        }
        catch (CryptographicException)
        {
            return new SignatureInfo(false, string.Empty);
        }
        catch (System.Security.SecurityException)
        {
            return new SignatureInfo(false, string.Empty);
        }
#pragma warning restore SYSLIB0057
    }

    private readonly record struct SignatureInfo(bool Valid, string Publisher);
}

public sealed class ProcessDescriptor
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string SignedPublisher { get; set; } = string.Empty;
    public bool SignatureValid { get; set; }
}
