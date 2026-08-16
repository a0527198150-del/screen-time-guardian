using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.NativeHost;

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
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();

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
                throw new InvalidDataException("Invalid native messaging payload length");
            }

            var payload = new byte[length];
            if (!await ReadExactlyAsync(input, payload, cancellationToken))
            {
                return;
            }

            var response = Handle(payload);
            await WriteMessageAsync(output, response, cancellationToken);
        }
    }

    private byte[] Handle(byte[] payload)
    {
        try
        {
            var message = JsonSerializer.Deserialize<NativeMessage>(payload, JsonOptions)
                ?? new NativeMessage();
            var configuration = _store.Load();
            var now = DateTimeOffset.Now;

            return message.Type switch
            {
                "getPolicy" => Serialize(new
                {
                    ok = true,
                    policy = _engine.Evaluate(configuration, now)
                }),
                "accountDecision" => Serialize(_engine.Decide(
                    configuration,
                    message.Account ?? new AccountDecisionRequest { Service = message.Service ?? string.Empty },
                    now)),
                "heartbeat" => Serialize(new { ok = true, receivedAtUtc = DateTimeOffset.UtcNow }),
                _ => Serialize(new { ok = false, error = "Unknown message type" })
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return Serialize(new { ok = false, error = "Invalid request" });
        }
        catch (Exception)
        {
            return Serialize(new { ok = false, error = "Policy service unavailable" });
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
