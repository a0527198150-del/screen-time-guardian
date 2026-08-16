using System.IO.Pipes;
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

    private readonly ConfigurationStore _store;
    private readonly ILogger<GuardianCommandServer> _logger;

    public GuardianCommandServer(ConfigurationStore store, ILogger<GuardianCommandServer> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeNames.Control,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Control pipe connection ended unexpectedly");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Control pipe failed");
            }
        }
    }

    private async Task HandleClientAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return;
        }

        GuardianCommandResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<GuardianCommand>(requestJson, JsonOptions)
                ?? new GuardianCommand();
            response = ExecuteCommand(request);
        }
        catch (JsonException)
        {
            response = Error("פקודה לא תקינה.");
        }
        catch (ArgumentException exception)
        {
            response = Error(exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            response = Error("הסיסמה אינה נכונה.");
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private GuardianCommandResponse ExecuteCommand(GuardianCommand request)
    {
        var configuration = _store.Load();
        if (string.Equals(request.Type, "initializePassword", StringComparison.OrdinalIgnoreCase))
        {
            if (configuration.Security.IsConfigured)
            {
                return Error("סיסמת האפליקציה כבר הוגדרה.");
            }

            configuration.Security = ApplicationPassword.Create(request.Password);
            _store.Save(configuration);
            return Success(configuration);
        }

        if (!configuration.Security.IsConfigured)
        {
            if (string.Equals(request.Type, "getConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                return new GuardianCommandResponse { NeedsInitialization = true };
            }

            return Error("יש להגדיר את סיסמת האפליקציה בפעם הראשונה.");
        }

        if (!ApplicationPassword.Verify(request.Password, configuration.Security))
        {
            throw new UnauthorizedAccessException();
        }

        if (string.Equals(request.Type, "getConfiguration", StringComparison.OrdinalIgnoreCase))
        {
            return Success(configuration);
        }

        if (string.Equals(request.Type, "saveConfiguration", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Configuration is null)
            {
                return Error("לא התקבלה תצורה לשמירה.");
            }

            request.Configuration.Security = configuration.Security;
            _store.Save(request.Configuration);
            return Success(request.Configuration);
        }

        if (string.Equals(request.Type, "changePassword", StringComparison.OrdinalIgnoreCase))
        {
            configuration.Security = ApplicationPassword.Create(request.NewPassword);
            _store.Save(configuration);
            return Success(configuration);
        }

        return Error("סוג פקודה לא מוכר.");
    }

    private static GuardianCommandResponse Success(ConfigurationDocument configuration)
    {
        var safeConfiguration = configuration;
        safeConfiguration.Security = new ApplicationSecurity();
        return new GuardianCommandResponse
        {
            Ok = true,
            Configuration = safeConfiguration
        };
    }

    private static GuardianCommandResponse Error(string message) => new()
    {
        Ok = false,
        Error = message
    };
}
