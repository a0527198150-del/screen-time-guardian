namespace ScreenTimeGuardian.Contracts;

public sealed class BrowserIdentity
{
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public bool SignatureValid { get; set; }
}

public sealed class BrowserApprovalDecision
{
    public bool Approved { get; set; }
    public bool RequiresAdministrator { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class BrowserApprovalPolicy
{
    public BrowserApprovalDecision Evaluate(
        BrowserIdentity identity,
        IEnumerable<BrowserApproval> approvals)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(approvals);

        var matchingApproval = approvals.FirstOrDefault(approval =>
            approval.Enabled
            && approval.RequiresManagedExtension
            && string.Equals(approval.Publisher, identity.Publisher, StringComparison.OrdinalIgnoreCase)
            && string.Equals(approval.ProductName, identity.ProductName, StringComparison.OrdinalIgnoreCase)
            && identity.SignatureValid
            && PathsMatch(approval.ExecutablePath, identity.ExecutablePath));

        return matchingApproval is null
            ? new BrowserApprovalDecision
            {
                Approved = false,
                RequiresAdministrator = true,
                Reason = "הדפדפן אינו מאושר או שלא ניתן לאמת את חתימתו."
            }
            : new BrowserApprovalDecision
            {
                Approved = true,
                RequiresAdministrator = false,
                Reason = "הדפדפן מאושר להפעלה. תוכן חסום עדיין כפוף ללוח הזמנים."
            };
    }

    private static bool PathsMatch(string approvedPath, string actualPath)
    {
        if (string.IsNullOrWhiteSpace(approvedPath) || string.IsNullOrWhiteSpace(actualPath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(approvedPath).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(actualPath).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
