using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.NativeHost;

/// <summary>
/// Bridges the browser extension and the Windows service.
///
/// The extension now keeps ONE long lived connection open (connectNative) instead of
/// spawning a fresh host process per message, so this loop handles many requests.
/// Every response echoes back the requestId so the extension can match them up.
/// </summary>
public sealed class NativeMessagingServer
{
    private const int MaximumMessageBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConfigurationStore _store;
    private readonly PolicyEngine _engine;

    public NativeMessagingServer(ConfigurationStore store, PolicyEngine engine)
    {
        _store = store;
        _engine = engine;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();

        while (!cancellationToken.IsCancellationRequested)
        {
            var lengthBytes = new byte[sizeof(int)];
            if (!await ReadExactlyAsync(input, lengthBytes, cancellationToken))
            {
                return;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length <= 0 || length > MaximumMessageBytes)
            {
                return;
            }

            var payload = new byte[length];
            if (!await ReadExactlyAsync(input, payload, cancellationToken))
            {
                return;
            }

            var response = await HandleAsync(payload, cancellationToken);
            await WriteMessageAsync(output, response, cancellationToken);
        }
    }

    private async Task<byte[]> HandleAsync(byte[] payload, CancellationToken cancellationToken)
    {
        string? requestId = null;
        try
        {
            using (var document = JsonDocument.Parse(payload))
            {
                if (document.RootElement.TryGetProperty("requestId", out var idElement))
                {
                    requestId = idElement.GetString();
                }
            }

            var message = JsonSerializer.Deserialize<NativeMessage>(payload, JsonOptions) ?? new NativeMessage();
            var configuration = _store.Load();
            var now = DateTimeOffset.Now;

            switch (message.Type)
            {
                case "getPolicy":
                    return Serialize(new
                    {
                        requestId,
                        ok = true,
                        policy = _engine.Evaluate(configuration, now)
                    });

                case "accountDecision":
                {
                    var request = message.Account ?? new AccountDecisionRequest
                    {
                        Service = message.Service ?? string.Empty,
                        Origin = message.Origin ?? string.Empty
                    };

                    var decision = _engine.Decide(configuration, request, now);
                    return Serialize(new
                    {
                        requestId,
                        ok = true,
                        blocked = decision.Blocked,
                        identityKnown = decision.IdentityKnown,
                        reason = decision.Reason
                    });
                }

                case "reportDiscovery":
                {
                    var ok = await ForwardDiscoveryAsync(
                        message.Origin ?? string.Empty,
                        message.Email ?? string.Empty,
                        cancellationToken);
                    return Serialize(new { requestId, ok });
                }

                case "heartbeat":
                    return Serialize(new { requestId, ok = true, receivedAtUtc = DateTimeOffset.UtcNow });

                default:
                    return Serialize(new { requestId, ok = false, error = "Unknown message type" });
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return Serialize(new { requestId, ok = false, error = "Invalid request" });
        }
        catch (Exception)
        {
            return Serialize(new { requestId, ok = false, error = "Policy service unavailable" });
        }
    }

    /// <summary>
    /// The config file is not writable by a standard user, so discoveries are sent to
    /// the service over the control pipe. No password is needed: this only appends to a
    /// review list that a parent must act on before anything is enforced.
    /// </summary>
    private static async Task<bool> ForwardDiscoveryAsync(string origin, string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", PipeNames.Control, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000, cancellationToken);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, true, 4096, leaveOpen: true);

            var command = new GuardianCommand
            {
                Type = "reportDiscovery",
                Origin = origin,
                Email = email
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(command, JsonOptions));
            var responseJson = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return false;
            }

            var response = JsonSerializer.Deserialize<GuardianCommandResponse>(responseJson, JsonOptions);
            return response?.Ok == true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static async Task WriteMessageAsync(Stream output, byte[] payload, CancellationToken cancellationToken)
    {
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await output.WriteAsync(length, cancellationToken);
        await output.WriteAsync(payload, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<bool> ReadExactlyAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
