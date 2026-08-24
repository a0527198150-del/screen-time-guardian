using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public sealed class GuardianPipeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<GuardianCommandResponse> InitializePasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "initializePassword",
            Password = password
        }, cancellationToken);
    }

    public async Task<GuardianCommandResponse> GetConfigurationAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "getConfiguration",
            Password = password
        }, cancellationToken);
    }

    public async Task<GuardianCommandResponse> SaveConfigurationAsync(
        string password,
        ConfigurationDocument configuration,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "saveConfiguration",
            Password = password,
            Configuration = configuration
        }, cancellationToken);
    }

    public async Task<GuardianCommandResponse> GetStatusAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "getStatus",
            Password = password
        }, cancellationToken);
    }

    public async Task<GuardianCommandResponse> GetUpcomingAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "getUpcoming",
            Password = password
        }, cancellationToken);
    }

    public async Task<GuardianCommandResponse> ClearSafeModeAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "clearSafeMode",
            Password = password
        }, cancellationToken);
    }

    public async Task<GuardianCommandResponse> ChangePasswordAsync(
        string password,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "changePassword",
            Password = password,
            NewPassword = newPassword
        }, cancellationToken);
    }

    public async Task<GuardianCommandResponse> CancelPendingChangeAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(new GuardianCommand
        {
            Type = "cancelPendingChange",
            Password = password
        }, cancellationToken);
    }

    private static async Task<GuardianCommandResponse> SendAsync(
        GuardianCommand command,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeNames.Control,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000, cancellationToken);

        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 4096, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(command, JsonOptions));
        var responseJson = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new IOException("השירות לא החזיר תשובה.");
        }

        return JsonSerializer.Deserialize<GuardianCommandResponse>(responseJson, JsonOptions)
            ?? throw new IOException("תשובת השירות אינה תקינה.");
    }
}
