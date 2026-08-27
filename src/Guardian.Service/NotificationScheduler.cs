using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class NotificationScheduler
{
    private const string TaskName = "ScreenTimeGuardian Notify";
    private readonly ILogger<NotificationScheduler> _logger;

    public NotificationScheduler(ILogger<NotificationScheduler> logger)
    {
        _logger = logger;
    }

    public void Refresh(ConfigurationDocument configuration)
    {
        DeleteTask();
        if (configuration.NotificationLeadMinutes <= 0)
        {
            return;
        }

        var agent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ScreenTimeGuardian", "Agent", "ScreenTimeGuardian.Agent.exe");
        if (!File.Exists(agent))
        {
            _logger.LogWarning("Notification agent was not found at {Path}", agent);
            return;
        }

        var now = DateTimeOffset.Now;
        var triggers = new List<(DateTimeOffset At, string Message)>();
        foreach (var rule in EnumerateRules(configuration))
        {
            if (!rule.Enabled) continue;
            foreach (var window in rule.Windows)
            {
                foreach (var edge in window.GetEdgeInstants(now, 7))
                {
                    var at = edge - TimeSpan.FromMinutes(configuration.NotificationLeadMinutes);
                    if (at > now)
                    {
                        triggers.Add((at, $"{rule.Name} starts in {configuration.NotificationLeadMinutes} minutes"));
                    }
                }
            }
        }

        var distinct = triggers
            .GroupBy(item => item.At.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm"), StringComparer.Ordinal)
            .Select(group => new ScheduledNotification(
                group.Min(item => item.At),
                string.Join("; ", group.Select(item => item.Message).Distinct(StringComparer.Ordinal))))
            .OrderBy(item => item.At)
            .Take(60)
            .ToList();

        if (triggers.Count > distinct.Count)
        {
            _logger.LogWarning("Notification schedule limited to 60 triggers (requested {Count})", triggers.Count);
        }
        if (distinct.Count == 0) return;

        var taskXml = BuildTaskXml(agent, distinct.Select(item => (item.At, item.Message)).ToList());
        var xmlPath = WriteTemporaryXml(taskXml);
        try
        {
            Run("/Create", $"/TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { }
        }
        _logger.LogInformation("Notification task registered with {Count} trigger(s)", distinct.Count);
    }

    public void DeleteTask()
    {
        Run("/Delete", $"/TN \"{TaskName}\" /F", ignoreFailure: true);
    }

    private sealed record ScheduledNotification(DateTimeOffset At, string Message);

    private static IEnumerable<ScheduledRule> EnumerateRules(ConfigurationDocument configuration)
    {
        foreach (var rule in configuration.Applications) yield return rule;
        foreach (var rule in configuration.Websites) yield return rule;
        foreach (var rule in configuration.GoogleAccounts) yield return rule;
    }

    private static string BuildTaskXml(string agent, IReadOnlyList<(DateTimeOffset At, string Message)> triggers)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var root = new XElement(ns + "Task", new XAttribute("version", "1.4"),
            new XElement(ns + "Triggers", triggers.Select(item =>
                new XElement(ns + "TimeTrigger",
                    new XElement(ns + "StartBoundary", item.At.ToUniversalTime().ToString("s") + "Z"),
                    new XElement(ns + "Enabled", "true")))),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"),
                new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "StartWhenAvailable", "true"), new XElement(ns + "ExecutionTimeLimit", "PT2M")),
            new XElement(ns + "Actions", new XElement(ns + "Exec",
                new XElement(ns + "Command", agent),
                new XElement(ns + "Arguments", "--message \"התראה מתוזמנת של שומר זמן מסך\""))));
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static string WriteTemporaryXml(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stg-notify-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml, new System.Text.UTF8Encoding(false));
        return path;
    }

    private void Run(string command, string arguments, bool ignoreFailure = false)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe", Arguments = $"{command} {arguments}",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        });
        if (process is null) return;
        process.WaitForExit(15_000);
        if (process.ExitCode != 0 && !ignoreFailure)
        {
            _logger.LogWarning("schtasks {Command} failed: {Error}", command, process.StandardError.ReadToEnd().Trim());
        }
    }

}
