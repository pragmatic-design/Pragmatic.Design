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
/// <remarks>
///     Every step is written once, as a <c>…CoreAsync(useAsync)</c>: with <c>useAsync</c> it awaits the stream's asynchronous calls, without it
///     it makes the synchronous ones and completes before it returns, so the synchronous methods read a finished result
///     (<see cref="Synchronous.Result{T}" />) and never block on asynchronous work. The synchronous path is what a transaction's synchronous
///     dispose sends its abort through.
/// </remarks>
internal sealed class MongoConnection : IAsyncDisposable, IDisposable
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

    public static Task<MongoConnection> OpenAsync(MongoSettings settings, CancellationToken ct) => OpenCoreAsync(settings, useAsync: true, ct).AsTask();

    /// <summary>The connection opened synchronously: the same steps, on the socket's and the stream's synchronous calls.</summary>
    public static MongoConnection Open(MongoSettings settings) => Synchronous.Result(OpenCoreAsync(settings, useAsync: false, CancellationToken.None));

    private static async ValueTask<MongoConnection> OpenCoreAsync(MongoSettings settings, bool useAsync, CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            if (useAsync)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(settings.ConnectTimeout);
                await client.ConnectAsync(settings.Host, settings.Port, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                Connect(client, settings);
            }

            Stream stream = client.GetStream();
            if (settings.Tls)
            {
                var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                var options = new SslClientAuthenticationOptions { TargetHost = settings.Host };
                if (settings.AllowInvalidCertificates)
                    options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                if (useAsync)
                    await tls.AuthenticateAsClientAsync(options, ct).ConfigureAwait(false);
                else
                    tls.AuthenticateAsClient(options);
                stream = tls;
            }

            var connection = new MongoConnection(client, stream);
            await connection.HelloCoreAsync(settings, useAsync, ct).ConfigureAwait(false);
            if (settings.User is not null)
                await MongoScram.AuthenticateCoreAsync(connection, settings.User, settings.Password!, settings.AuthSource, useAsync, ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>The synchronous connect, within the settings' connect timeout as the asynchronous one.</summary>
    private static void Connect(TcpClient client, MongoSettings settings)
    {
        var pending = client.BeginConnect(settings.Host, settings.Port, null, null);
        using (pending.AsyncWaitHandle)
        {
            if (!pending.AsyncWaitHandle.WaitOne(settings.ConnectTimeout))
            {
                client.Dispose(); // ends the pending connect; EndConnect is not called on a closed client
                throw new TimeoutException($"MongoDB at {settings.Host}:{settings.Port} did not accept a connection within {settings.ConnectTimeout}.");
            }

            client.EndConnect(pending);
        }
    }

    /// <summary>Sends one command (a whole BSON document, its <c>$db</c> included) and returns the reply's body, <c>ok</c> checked.</summary>
    public Task<BsonDocument> CommandAsync(ReadOnlyMemory<byte> command, CancellationToken ct) => CommandCoreAsync(command, useAsync: true, ct).AsTask();

    /// <summary>The command sent and its reply read synchronously.</summary>
    public BsonDocument Command(ReadOnlyMemory<byte> command) => Synchronous.Result(CommandCoreAsync(command, useAsync: false, CancellationToken.None));

    internal async ValueTask<BsonDocument> CommandCoreAsync(ReadOnlyMemory<byte> command, bool useAsync, CancellationToken ct)
    {
        var reply = await RoundTripAsync(command, useAsync, ct).ConfigureAwait(false);
        MongoReply.ThrowIfFailed(reply);
        return reply;
    }

    private async ValueTask<BsonDocument> RoundTripAsync(ReadOnlyMemory<byte> command, bool useAsync, CancellationToken ct)
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
            if (useAsync)
            {
                await _stream.WriteAsync(_header, ct).ConfigureAwait(false);
                await _stream.WriteAsync(command, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            else
            {
                _stream.Write(_header);
                _stream.Write(command.Span);
                _stream.Flush();
            }

            return await ReadReplyAsync(requestId, useAsync, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or FormatException)
        {
            Healthy = false; // a half-written request or half-read reply: the stream cannot be trusted any more
            throw;
        }
    }

    private async ValueTask<BsonDocument> ReadReplyAsync(int requestId, bool useAsync, CancellationToken ct)
    {
        var header = new byte[16];
        await ReadExactlyAsync(header, useAsync, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        var responseTo = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        var opCode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
        if (length < 21 || length > MaxMessageSize + 16 * 1024)
            throw new FormatException($"The reply's length ({length}) is not a message's.");
        var message = new byte[length - 16];
        await ReadExactlyAsync(message, useAsync, ct).ConfigureAwait(false);
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

    private ValueTask ReadExactlyAsync(byte[] buffer, bool useAsync, CancellationToken ct)
    {
        if (useAsync)
            return _stream.ReadExactlyAsync(buffer, ct);
        _stream.ReadExactly(buffer);
        return ValueTask.CompletedTask;
    }

    private async ValueTask HelloCoreAsync(MongoSettings settings, bool useAsync, CancellationToken ct)
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
        var reply = await CommandCoreAsync(writer.Written, useAsync, ct).ConfigureAwait(false);
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

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }
}
