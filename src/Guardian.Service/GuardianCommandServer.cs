using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class GuardianCommandServer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const int MaxFailedAttempts = 5;
    private const int MaxRequestCharacters = 256_000;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DiscoveryThrottle = TimeSpan.FromSeconds(2);

    private readonly ConfigurationStore _store;
    private readonly SafetyEnvelope _safety;
    private readonly ChangeCoordinator _changes;
    private readonly ServiceStatusHolder _status;
    private readonly ILogger<GuardianCommandServer> _logger;

    private readonly object _authenticationSync = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _failedAttemptsByClient = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastDiscovery = DateTimeOffset.MinValue;
    private const int MaxDiscoveredSites = 200;

    public GuardianCommandServer(
        ConfigurationStore store,
        SafetyEnvelope safety,
        ChangeCoordinator changes,
        ServiceStatusHolder status,
        ILogger<GuardianCommandServer> logger)
    {
        _store = store;
        _safety = safety;
        _changes = changes;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException exception)
            {
                _logger.LogDebug(exception, "Control pipe connection ended");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Control pipe failed");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(users, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(administrators, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeNames.Control,
            PipeDirection.InOut,
            // More than one instance so a stuck client cannot lock out the control panel.
            4,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            8192,
            8192,
            security);
    }

    private async Task HandleClientAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, true, 8192, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = true };

        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(TimeSpan.FromSeconds(15));

        string? requestJson;
        try
        {
            requestJson = await ReadBoundedLineAsync(reader, MaxRequestCharacters, readTimeout.Token);
        }
        catch (InvalidDataException)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(Error("הפקודה גדולה מדי."), JsonOptions));
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return;
        }

        GuardianCommandResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<GuardianCommand>(requestJson, JsonOptions) ?? new GuardianCommand();
            var client = GetClientIdentity(pipe);
            response = ExecuteCommand(request, client.IsAdministrator, client.Sid);
        }
        catch (JsonException)
        {
            response = Error("פקודה לא תקינה.");
        }
        catch (ArgumentException exception)
        {
            response = Error(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            response = Error(exception.Message.Length == 0 ? "הסיסמה אינה נכונה." : exception.Message);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static async Task<string?> ReadBoundedLineAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 8192));
        var buffer = new char[4096];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            var newlineIndex = Array.IndexOf(buffer, '\n', 0, read);
            var charactersToAppend = newlineIndex >= 0 ? newlineIndex : read;
            if (builder.Length + charactersToAppend > maximumCharacters)
            {
                throw new InvalidDataException("Request exceeds the configured size limit.");
            }

            if (charactersToAppend > 0)
            {
                builder.Append(buffer, 0, charactersToAppend);
            }

            if (newlineIndex >= 0)
            {
                return builder.ToString().TrimEnd('\r');
            }
        }
    }

    private GuardianCommandResponse ExecuteCommand(GuardianCommand request, bool clientIsAdministrator, string clientSid)
    {
        var configuration = _store.Load();

        // Discovery is intentionally unauthenticated so the browser extension can report
        // sign-ins, but it must never be allowed to mutate any other command path.
        if (string.Equals(request.Type, "reportDiscovery", StringComparison.OrdinalIgnoreCase))
        {
            return configuration.Security.IsConfigured
                ? HandleDiscovery(configuration, request)
                : Error("יש להגדיר את סיסמת האפליקציה בפעם הראשונה.");
        }

        if (!configuration.Security.IsConfigured)
        {
            if (string.Equals(request.Type, "initializePassword", StringComparison.OrdinalIgnoreCase))
            {
                if (!clientIsAdministrator)
                {
                    _logger.LogWarning("Rejected password initialization from a non-administrator client");
                    return Error("אתחול הסיסמה הראשון מותר למנהל בלבד.");
                }

                try
                {
                    ApplicationPassword.Validate(request.Password);
                }
                catch (ArgumentException exception)
                {
                    return Error(exception.Message);
                }

                configuration.Security = ApplicationPassword.Create(request.Password);
                _store.Save(configuration);
                return Success(configuration);
            }

            return string.Equals(request.Type, "getConfiguration", StringComparison.OrdinalIgnoreCase)
                ? new GuardianCommandResponse { NeedsInitialization = true }
                : Error("יש להגדיר את סיסמת האפליקציה בפעם הראשונה.");
        }

        EnsureNotLockedOut(clientSid);

        if (!ApplicationPassword.Verify(request.Password, configuration.Security))
        {
            RegisterFailure(clientSid);
            throw new UnauthorizedAccessException("הסיסמה אינה נכונה.");
        }

        ClearFailures(clientSid);

        if (string.Equals(request.Type, "getUpcoming", StringComparison.OrdinalIgnoreCase))
        {
            return new GuardianCommandResponse
            {
                Ok = true,
                Upcoming = UpcomingCalculator.Calculate(configuration, DateTimeOffset.Now)
            };
        }

        if (string.Equals(request.Type, "getConfiguration", StringComparison.OrdinalIgnoreCase))
        {
            return Success(configuration);
        }

        if (string.Equals(request.Type, "getStatus", StringComparison.OrdinalIgnoreCase))
        {
            return new GuardianCommandResponse { Ok = true, Status = _status.Current };
        }

        if (string.Equals(request.Type, "clearSafeMode", StringComparison.OrdinalIgnoreCase))
        {
            _safety.ClearSafeMode();
            return new GuardianCommandResponse { Ok = true, Status = _status.Current };
        }

        if (string.Equals(request.Type, "saveConfiguration", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Configuration is null)
            {
                return Error("לא התקבלה תצורה לשמירה.");
            }

            // Every save goes through change control. Tightening applies at once;
            // loosening waits out the cooling off delay if one is configured.
            var result = _changes.Submit(configuration, request.Configuration, DateTimeOffset.Now);
            var stored = _store.Load();

            var response = Success(stored);
            response.Notice = result.Message;
            _logger.LogInformation("Configuration submitted: {Message}", result.Message);
            return response;
        }

        if (string.Equals(request.Type, "cancelPendingChange", StringComparison.OrdinalIgnoreCase))
        {
            // Cancelling a queued relaxation keeps the stricter setting, so it is
            // always permitted with no delay of its own.
            _changes.CancelPending(configuration);
            return Success(_store.Load());
        }

        if (string.Equals(request.Type, "changePassword", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ApplicationPassword.Validate(request.NewPassword);
            }
            catch (ArgumentException exception)
            {
                return Error(exception.Message);
            }

            if (ApplicationPassword.Verify(request.NewPassword, configuration.Security))
            {
                return Error("הסיסמה החדשה זהה לסיסמה הנוכחית.");
            }

            configuration.Security = ApplicationPassword.Create(request.NewPassword);
            _store.Save(configuration);
            _logger.LogWarning("Application password changed");
            return Success(configuration);
        }

        return Error("סוג פקודה לא מוכר.");
    }

    private GuardianCommandResponse HandleDiscovery(ConfigurationDocument configuration, GuardianCommand request)
    {
        if (DateTimeOffset.UtcNow - _lastDiscovery < DiscoveryThrottle)
        {
            return new GuardianCommandResponse { Ok = true };
        }

        _lastDiscovery = DateTimeOffset.UtcNow;

        var origin = ConfigurationValidation.NormalizeOrigin(request.Origin ?? string.Empty);
        var email = (request.Email ?? string.Empty).Trim();

        if (origin.Length == 0 || !ConfigurationValidation.IsValidEmail(email))
        {
            return Error("דיווח גילוי לא תקין.");
        }

        var existing = configuration.DiscoveredSites.FirstOrDefault(site =>
            string.Equals(site.Origin, origin, StringComparison.OrdinalIgnoreCase)
            && string.Equals(site.Email, email, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.LastSeenUtc = DateTimeOffset.UtcNow;
        }
        else if (configuration.DiscoveredSites.Count >= MaxDiscoveredSites)
        {
            _logger.LogWarning("Discovery list limit reached at {Limit}; report rejected", MaxDiscoveredSites);
            return Error("רשימת האתרים שהתגלו מלאה. מחק פריטים ישנים לפני הוספה נוספת.");
        }
        else
        {
            configuration.DiscoveredSites.Add(new DiscoveredSite { Origin = origin, Email = email });
            _logger.LogInformation("Discovered Google sign-in on {Origin}", origin);
        }

        _store.Save(configuration);
        return new GuardianCommandResponse { Ok = true };
    }

    private static (string Sid, bool IsAdministrator) GetClientIdentity(Stream pipe)
    {
        if (pipe is not NamedPipeServerStream serverPipe)
        {
            return ("unknown", false);
        }

        var sid = "unknown";
        var isAdministrator = false;
        serverPipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            sid = identity.User?.Value ?? "unknown";
            isAdministrator = identity.User is not null
                && new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        });
        return (sid, isAdministrator);
    }

    private void EnsureNotLockedOut(string clientSid)
    {
        lock (_authenticationSync)
        {
            var attempts = GetAttempts(clientSid);
            var now = DateTimeOffset.UtcNow;
            attempts.RemoveAll(stamp => now - stamp > LockoutWindow);
            if (attempts.Count >= MaxFailedAttempts)
            {
                _logger.LogWarning("Control pipe authentication lockout reached for client {ClientSid}", clientSid);
                throw new UnauthorizedAccessException(
                    $"יותר מדי ניסיונות שגויים. נסה שוב בעוד {(int)(LockoutWindow - (now - attempts[0])).TotalMinutes + 1} דקות.");
            }
        }
    }

    private void RegisterFailure(string clientSid)
    {
        lock (_authenticationSync)
        {
            var attempts = GetAttempts(clientSid);
            attempts.Add(DateTimeOffset.UtcNow);
            _logger.LogWarning(
                "Failed control pipe authentication attempt for client {ClientSid} ({Count} in the current window)",
                clientSid,
                attempts.Count);
        }
    }

    private void ClearFailures(string clientSid)
    {
        lock (_authenticationSync)
        {
            _failedAttemptsByClient.Remove(clientSid);
        }
    }

    private List<DateTimeOffset> GetAttempts(string clientSid)
    {
        if (!_failedAttemptsByClient.TryGetValue(clientSid, out var attempts))
        {
            attempts = new List<DateTimeOffset>();
            _failedAttemptsByClient[clientSid] = attempts;
        }

        return attempts;
    }

    /// <summary>
    /// Returns a DEEP COPY with the password hash stripped. The previous version assigned
    /// the same reference, which silently blanked the in memory password hash.
    /// </summary>
    private static GuardianCommandResponse Success(ConfigurationDocument configuration)
    {
        var copy = ConfigurationStore.Clone(configuration);
        copy.Security = new ApplicationSecurity();
        return new GuardianCommandResponse { Ok = true, Configuration = copy };
    }

    private static GuardianCommandResponse Error(string message) => new() { Ok = false, Error = message };
}
