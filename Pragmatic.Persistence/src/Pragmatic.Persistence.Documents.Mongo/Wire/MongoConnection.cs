using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Mongo.Wire;

/// <summary>
///     One connection to the server: a socket (in TLS when the settings ask), the handshake (<c>hello</c>), SCRAM-SHA-256 when there are
///     credentials, then one command at a time as an OP_MSG (opcode 2013, one body section; the server's reply is an OP_MSG too). Not
///     thread-safe: the pool hands it to one caller at a time.
/// </summary>
internal sealed class MongoConnection : IAsyncDisposable
{
    private const int OpMsg = 2013;
    private const int MinWireVersion = 6; // MongoDB 3.6: OP_MSG
    private static int _nextRequestId;
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly byte[] _header = new byte[21];

    private MongoConnection(TcpClient client, Stream stream)
    {
        _client = client;
        _stream = stream;
    }

    /// <summary>False once a read or write failed: the stream is out of step with the server, the pool drops the connection.</summary>
    public bool Healthy { get; private set; } = true;

    /// <summary>The largest message the server takes (<c>maxMessageSizeBytes</c> from the handshake).</summary>
    public int MaxMessageSize { get; private set; } = 48_000_000;

    public static async Task<MongoConnection> OpenAsync(MongoSettings settings, CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(settings.ConnectTimeout);
                await client.ConnectAsync(settings.Host, settings.Port, timeout.Token).ConfigureAwait(false);
            }

            Stream stream = client.GetStream();
            if (settings.Tls)
            {
                var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                var options = new SslClientAuthenticationOptions { TargetHost = settings.Host };
                if (settings.AllowInvalidCertificates)
                    options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                await tls.AuthenticateAsClientAsync(options, ct).ConfigureAwait(false);
                stream = tls;
            }

            var connection = new MongoConnection(client, stream);
            await connection.HelloAsync(settings, ct).ConfigureAwait(false);
            if (settings.User is not null)
                await MongoScram.AuthenticateAsync(connection, settings.User, settings.Password!, settings.AuthSource, ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Sends one command (a whole BSON document, its <c>$db</c> included) and returns the reply's body, <c>ok</c> checked.</summary>
    public async Task<BsonDocument> CommandAsync(ReadOnlyMemory<byte> command, CancellationToken ct)
    {
        var reply = await RoundTripAsync(command, ct).ConfigureAwait(false);
        MongoReply.ThrowIfFailed(reply);
        return reply;
    }

    private async Task<BsonDocument> RoundTripAsync(ReadOnlyMemory<byte> command, CancellationToken ct)
    {
        if (command.Length + 21 > MaxMessageSize)
            throw new MongoException($"The command is {command.Length} bytes: over the server's message limit ({MaxMessageSize}).");
        var requestId = Interlocked.Increment(ref _nextRequestId);
        try
        {
            // Header (length, request id, response to, opcode), flag bits, section kind 0, then the body.
            BinaryPrimitives.WriteInt32LittleEndian(_header, 21 + command.Length);
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(4), requestId);
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(8), 0);
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(12), OpMsg);
            BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(16), 0);
            _header[20] = 0;
            await _stream.WriteAsync(_header, ct).ConfigureAwait(false);
            await _stream.WriteAsync(command, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            return await ReadReplyAsync(requestId, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or FormatException)
        {
            Healthy = false; // a half-written request or half-read reply: the stream cannot be trusted any more
            throw;
        }
    }

    private async Task<BsonDocument> ReadReplyAsync(int requestId, CancellationToken ct)
    {
        var header = new byte[16];
        await _stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        var responseTo = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        var opCode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
        if (length < 21 || length > MaxMessageSize + 16 * 1024)
            throw new FormatException($"The reply's length ({length}) is not a message's.");
        var message = new byte[length - 16];
        await _stream.ReadExactlyAsync(message, ct).ConfigureAwait(false);
        if (opCode != OpMsg)
            throw new FormatException($"The reply's opcode is {opCode}, not OP_MSG.");
        if (responseTo != requestId)
            throw new FormatException($"The reply answers request {responseTo}, not {requestId}.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(message);
        var end = (flags & 1) != 0 ? message.Length - 4 : message.Length; // checksumPresent: the CRC-32C trails the sections
        var position = 4;
        BsonDocument? body = null;
        while (position < end)
        {
            var kind = message[position++];
            var size = BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(position));
            if (kind == 0)
                body = new BsonDocument(message.AsMemory(position, size));
            else if (kind != 1)
                throw new FormatException($"Unknown OP_MSG section kind {kind}.");
            position += size; // a kind 1 section (a document sequence) is not something a reply to these commands carries: skipped
        }

        return body ?? throw new FormatException("The reply has no body section.");
    }

    private async Task HelloAsync(MongoSettings settings, CancellationToken ct)
    {
        var writer = new BsonWriter();
        writer.StartDocument();
        writer.Write("hello", 1);
        writer.StartDocument("client");
        writer.StartDocument("driver");
        writer.Write("name", "Pragmatic.Persistence.Documents.Mongo");
        writer.Write("version", "0.1");
        writer.EndDocument();
        writer.StartDocument("os");
        writer.Write("type", OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "Darwin" : "unknown");
        writer.EndDocument();
        writer.StartDocument("application");
        writer.Write("name", settings.AppName);
        writer.EndDocument();
        writer.EndDocument();
        writer.Write("$db", "admin");
        writer.EndDocument();
        var reply = await CommandAsync(writer.Written, ct).ConfigureAwait(false);
        if (!reply.TryGetNumber("maxWireVersion", out var wire) || wire < MinWireVersion)
            throw new MongoException($"The server speaks wire version {wire}: this driver needs {MinWireVersion} (MongoDB 3.6) or later.");
        if (reply.TryGetNumber("maxMessageSizeBytes", out var size))
            MaxMessageSize = (int)size;
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
    }
}
