using System.Buffers.Binary;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Socket;
using Xunit;

namespace Pragmatic.Agent.Tests.Socket;

/// <summary>
///     A client that sends a valid frame length header then stalls on the payload must
///     not pin the read-loop task forever. <see cref="ClientConnection"/> applies a per-frame read
///     deadline; on expiry it disposes the stream to unblock the (non-cancellable) payload read and
///     tears the connection down cleanly.
/// </summary>
public sealed class ClientConnectionTimeoutTests
{
    [Fact]
    public async Task ReadLoop_StalledPayload_AbortsByDeadlineAndDisconnects()
    {
        // Valid 1KB length header, then the stream stalls (never delivers the payload).
        var stream = new StallingPayloadStream(payloadLength: 1024);
        var disconnected = new TaskCompletionSource();

        var conn = new ClientConnection(
            stream,
            new NoOpHandler(),
            _ => disconnected.TrySetResult(),
            frameReadTimeout: TimeSpan.FromMilliseconds(150));

        conn.Start();

        // The loop must exit via the deadline well within a generous bound.
        var completed = await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(disconnected.Task, "the stalled payload read must be aborted by the deadline");

        stream.Disposed.Should().BeTrue("the deadline disposes the stream to unblock the read");

        conn.Dispose();
    }

    /// <summary>
    ///     A client that sends nothing for longer than the deadline is idle, not stalled. Its
    ///     next frame is read and handled, and the connection is still open.
    /// </summary>
    /// <remarks>
    ///     The deadline starts after the header. Started before it, the wait for the next message would
    ///     count: every client that does not heartbeat — the gateway — would be disconnected thirty
    ///     seconds after its last frame, and lose its registration and its subscriptions with it.
    /// </remarks>
    [Fact]
    public async Task ReadLoop_IdleLongerThanTheDeadline_ThenAFrame_StaysConnected()
    {
        var frame = FrameCodec.Encode(new AgentMessage { Type = MessageType.Heartbeat });
        var stream = new IdleThenFrameStream(frame, idleFor: TimeSpan.FromMilliseconds(600));
        var disconnected = new TaskCompletionSource();
        var handler = new RecordingHandler();

        var conn = new ClientConnection(
            stream,
            handler,
            _ => disconnected.TrySetResult(),
            frameReadTimeout: TimeSpan.FromMilliseconds(150));

        conn.Start();

        var first = await Task.WhenAny(handler.Received.Task, disconnected.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        first.Should().Be(handler.Received.Task,
            "an idle client is not a stalled one: the frame after the silence is read and handled");
        disconnected.Task.IsCompleted.Should().BeFalse("nothing closed the connection");
        stream.Disposed.Should().BeFalse("the deadline did not tear the stream down during the silence");

        conn.Dispose();
    }

    /// <summary>
    ///     Blocks for <c>idleFor</c> before delivering one complete frame, then blocks until disposed —
    ///     a client that is quiet for a while and then speaks.
    /// </summary>
    private sealed class IdleThenFrameStream(byte[] frame, TimeSpan idleFor) : Stream
    {
        private readonly SemaphoreSlim _closed = new(0, 1);
        private int _offset;
        private bool _waited;

        public bool Disposed { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_waited)
            {
                await Task.Delay(idleFor, cancellationToken).ConfigureAwait(false);
                _waited = true;
            }

            if (_offset < frame.Length)
            {
                var n = Math.Min(buffer.Length, frame.Length - _offset);
                frame.AsSpan(_offset, n).CopyTo(buffer.Span);
                _offset += n;
                return n;
            }

            await _closed.WaitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _closed.Release();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    private sealed class RecordingHandler : IMessageHandler
    {
        public TaskCompletionSource Received { get; } = new();

        public Task<AgentMessage?> HandleAsync(ClientConnection client, AgentMessage message)
        {
            Received.TrySetResult();
            return Task.FromResult<AgentMessage?>(null);
        }

        public void OnClientDisconnected(ClientConnection client) { }
    }

    /// <summary>
    ///     Returns a valid length header on the first read, then blocks forever on the payload read
    ///     until the stream is disposed — simulating a slow/malicious client that pins the connection.
    /// </summary>
    private sealed class StallingPayloadStream(int payloadLength) : Stream
    {
        private readonly byte[] _header = BuildHeader(payloadLength);
        private readonly SemaphoreSlim _stall = new(0, 1);
        private int _headerOffset;

        public bool Disposed { get; private set; }

        private static byte[] BuildHeader(int length)
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, length);
            return header;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_headerOffset < _header.Length)
            {
                var n = Math.Min(buffer.Length, _header.Length - _headerOffset);
                _header.AsSpan(_headerOffset, n).CopyTo(buffer.Span);
                _headerOffset += n;
                return n;
            }

            // Header fully delivered: stall until the stream is disposed. FrameCodec passes
            // CancellationToken.None here, so only Dispose() (via the deadline) can unblock us.
            await _stall.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(StallingPayloadStream));
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _stall.Release();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NoOpHandler : IMessageHandler
    {
        public Task<AgentMessage?> HandleAsync(ClientConnection client, AgentMessage message)
            => Task.FromResult<AgentMessage?>(null);

        public void OnClientDisconnected(ClientConnection client) { }
    }
}
