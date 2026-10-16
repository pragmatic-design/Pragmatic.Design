using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     A classic call — Microsoft's <c>[LoggerMessage]</c>, <c>logger.LogInformation(template, args)</c> — written
///     by the JSON provider goes through a writer and a buffer the provider keeps, and reaches the output as the
///     line's bytes: no stream, writer, array or string per line (#284).
/// </summary>
public class AClassicCallsJsonLineAllocatesNothingOfItsOwnTests
{
    private const string Template = "Order {OrderId} placed by {Customer} for {Amount}";

    /// <summary>
    ///     With the state and its message already built, every byte a call allocates is the provider's own. There
    ///     are none, to a stream and to a file.
    /// </summary>
    /// <remarks>
    ///     The state is a class, as in <see cref="AClassicCallReachesTheProviderWithoutAnEntryTests" />: MEL's own
    ///     is a struct, read without boxing only by optimized code, which a Debug build never runs.
    /// </remarks>
    [Fact]
    public void ToAStream_AtSteadyState_TheProviderAllocatesNothing()
    {
        var output = new LastLine();
        using var provider = new PragmaticJsonProvider("json", Configuration(), output);

        var allocated = Allocated(provider.CreateLogger("Orders"));

        provider.GetMetrics().LastError.Should().BeNull();
        allocated.Fewest.Should().Be(0L, $"no line allocates in the provider (samples: {allocated.Samples})");
        output.Text.Should().Contain("\"@message\":\"Order 42 placed by jane for 19.99\"")
            .And.Contain("\"@properties\":{\"OrderId\":42,\"Customer\":\"jane\",\"Amount\":19.99}");
    }

    [Fact]
    public void ToAFile_AtSteadyState_TheProviderAllocatesNothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pragmatic-json-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "log.json");
        try
        {
            var configuration = Configuration();
            configuration.CustomProperties["RollByDate"] = false;
            (long Fewest, string Samples) allocated;
            string? lastError;
            using (var provider = new PragmaticJsonProvider("json", configuration, file))
            {
                allocated = Allocated(provider.CreateLogger("Orders"));
                lastError = provider.GetMetrics().LastError;
            }

            lastError.Should().BeNull();
            allocated.Fewest.Should().Be(0L, $"no line allocates in the provider (samples: {allocated.Samples})");
            var lines = File.ReadAllLines(file);
            lines.Should().HaveCount(1200);
            lines[^1].Should().Contain("\"@properties\":{\"OrderId\":42,\"Customer\":\"jane\",\"Amount\":19.99}");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    ///     A value whose text logs through the same provider while the line that carries it is being written: the inner
    ///     line has a writer of its own, and the outer one is written whole after it.
    /// </summary>
    [Fact]
    public void ALineWrittenWhileAnotherIsBeingWritten_LeavesItWhole()
    {
        var output = new MemoryStream();
        var configuration = Configuration();
        configuration.CustomProperties["SerializeComplexObjects"] = false;
        using (var provider = new PragmaticJsonProvider("json", configuration, output))
        {
            var logger = provider.CreateLogger("Orders");
            logger.LogInformation("Order {Order} placed", new LoggingValue(logger));
        }

        var lines = Encoding.UTF8.GetString(output.ToArray()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("\"@message\":\"inner\"");
        lines[1].Should().Contain("\"@message\":\"Order order-7 placed\"").And.Contain("\"@properties\":{\"Order\":\"order-7\"}");
        foreach (var line in lines)
            System.Text.Json.JsonDocument.Parse(line).Dispose();
    }

    // Turned into text twice: for the message, before the provider writes; for the property, while it writes. It logs
    // the second time.
    private sealed class LoggingValue(ILogger logger)
    {
        private int _calls;

        public override string ToString()
        {
            if (++_calls == 2)
                logger.LogInformation("inner");

            return "order-7";
        }
    }

    /// <summary>
    ///     The fewest bytes 200 calls allocated, over five batches after 200 calls of warm-up, as text that carries
    ///     every sample for the failure message.
    /// </summary>
    /// <remarks>
    ///     A byte a line costs is in every batch, the smallest included. The minimum leaves out what the runtime
    ///     allocates on the thread only now and then: under the gate's load, a batch of a path that allocates nothing
    ///     has measured 12,360 B once, in this test and in a generated call site's, and passed every run on its own.
    /// </remarks>
    private static (long Fewest, string Samples) Allocated(ILogger logger)
    {
        var state = new OrderState(42, "jane", 19.99m);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            for (var i = 0; i < 200; i++)
                logger.Log(LogLevel.Information, new EventId(2001, "OrderPlaced"), state, null, OrderState.Format);

            var samples = new long[5];
            for (var batch = 0; batch < samples.Length; batch++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 200; i++)
                    logger.Log(LogLevel.Information, new EventId(2001, "OrderPlaced"), state, null, OrderState.Format);
                samples[batch] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return (samples.Min(), string.Join(", ", samples));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static PragmaticProviderConfiguration Configuration()
    {
        var configuration = PragmaticJsonConfiguration.ForJson();
        configuration.IncludeContextEnrichment = false;
        configuration.CustomProperties["AutoFlush"] = true;
        return configuration;
    }

    // A call's state as MEL's is shaped — the pairs, then the template — built once, with its message.
    private sealed class OrderState(int orderId, string customer, decimal amount) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _pairs =
        [
            new("OrderId", orderId),
            new("Customer", customer),
            new("Amount", amount),
            new("{OriginalFormat}", Template),
        ];

        private readonly string _message = string.Create(CultureInfo.InvariantCulture, $"Order {orderId} placed by {customer} for {amount}");

        public static readonly Func<OrderState, Exception?, string> Format = static (state, _) => state._message;

        public int Count => _pairs.Length;

        public KeyValuePair<string, object?> this[int index] => _pairs[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_pairs).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Keeps the bytes of the last line only, so the output does not grow while the calls are measured.
    private sealed class LastLine : Stream
    {
        private static readonly byte[] NewLine = Encoding.UTF8.GetBytes(Environment.NewLine);

        private readonly byte[] _buffer = new byte[4096];
        private int _length;
        private bool _lineEnded;

        public string Text => Encoding.UTF8.GetString(_buffer, 0, _length).TrimEnd();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_lineEnded)
            {
                _length = 0;
                _lineEnded = false;
            }

            buffer.CopyTo(_buffer.AsSpan(_length));
            _length += buffer.Length;
            _lineEnded = buffer.EndsWith(NewLine);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
