using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.ZeroAllocation;

namespace Pragmatic.Logging.Benchmarks;

[Config(typeof(DefaultExportConfig))]
[MemoryDiagnoser]
[DisassemblyDiagnoser]
public class ZeroAllocationBenchmark
{
    private const string Template = "User {UserId} logged in from {Location} at {Timestamp} with session {SessionId}";
    private readonly TestData _testData = new();
    private readonly ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public class TestData
    {
        public string UserId { get; set; } = "user123";
        public string Location { get; set; } = "New York";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public Guid SessionId { get; set; } = Guid.NewGuid();
    }

    [Benchmark(Baseline = true)]
    public string StandardStringInterpolation()
    {
        return $"User {_testData.UserId} logged in from {_testData.Location} at {_testData.Timestamp} with session {_testData.SessionId}";
    }

    [Benchmark]
    public string StandardStringFormat()
    {
        return string.Format(CultureInfo.InvariantCulture, "User {0} logged in from {1} at {2} with session {3}",
            _testData.UserId, _testData.Location, _testData.Timestamp, _testData.SessionId);
    }

    [Benchmark(Description = "MessageFormatter.Format (legacy reflection-based)")]
    public string MessageFormatterFormat()
    {
        return MessageFormatter.Format(Template, _testData);
    }

    [Benchmark]
    public string ZeroAllocMessageFormatterFormat()
    {
        var parameters = new object?[] { _testData.UserId, _testData.Location };
        return ZeroAllocMessageFormatter.Format(Template.AsSpan(), parameters);
    }

    [Benchmark]
    public bool ZeroAllocMessageFormatterTryFormat()
    {
        Span<char> buffer = stackalloc char[200];
        var parameters = new object?[] { _testData.UserId, _testData.Location };
        return ZeroAllocMessageFormatter.TryFormat(Template.AsSpan(), parameters, buffer, out _);
    }

    [Benchmark]
    public string LogMessageToString()
    {
        var logMessage = new LogMessage<TestData>(LogLevel.Information, Template, _testData);
        return logMessage.ToString();
    }

    [Benchmark]
    public bool LogMessageTryFormat()
    {
        var logMessage = new LogMessage<TestData>(LogLevel.Information, Template, _testData);
        Span<char> buffer = stackalloc char[200];
        return logMessage.TryFormat(buffer, out _);
    }

    [Benchmark]
    public string StructuredLoggingWithILogger()
    {
        // Simulate what standard ILogger would do
        return string.Format(CultureInfo.InvariantCulture, Template.Replace("{UserId}", "{0}")
            .Replace("{Location}", "{1}")
            .Replace("{Timestamp}", "{2}")
            .Replace("{SessionId}", "{3}"),
            _testData.UserId, _testData.Location, _testData.Timestamp, _testData.SessionId);
    }
}

[Config(typeof(DefaultExportConfig))]
[MemoryDiagnoser]
public class AllocationComparisonBenchmark
{
    private const int IterationCount = 1000;
    private readonly TestMessage[] _messages;

    public class TestMessage
    {
        public string Template { get; set; } = "";
        public object Properties { get; set; } = new();
    }

    public AllocationComparisonBenchmark()
    {
        _messages = new TestMessage[IterationCount];
        for (int i = 0; i < IterationCount; i++)
        {
            _messages[i] = new TestMessage
            {
                Template = $"Processing item {{ItemId}} of {{TotalItems}} at {{Timestamp}}",
                Properties = new { ItemId = i, TotalItems = IterationCount, Timestamp = DateTime.Now }
            };
        }
    }

    [Benchmark(Baseline = true)]
    public int StandardFormatting()
    {
        int totalLength = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            var msg = _messages[i];
            string formatted = MessageFormatter.Format(msg.Template, msg.Properties);
            totalLength += formatted.Length;
        }
        return totalLength;
    }

    [Benchmark]
    public int ZeroAllocFormatting()
    {
        int totalLength = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            var msg = _messages[i];
            string formatted = ZeroAllocMessageFormatter.Format(msg.Template.AsSpan(), msg.Properties);
            totalLength += formatted.Length;
        }
        return totalLength;
    }

    [Benchmark]
    public int ZeroAllocFormattingWithStackBuffer()
    {
        int totalLength = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            var msg = _messages[i];
            Span<char> buffer = stackalloc char[256];
            // Convert anonymous object to object array for formatter
            var props = (dynamic)msg.Properties;
            var parameters = new object?[] { props.ItemId, props.TotalItems, props.Timestamp };
            if (ZeroAllocMessageFormatter.TryFormat(msg.Template.AsSpan(), parameters, buffer, out int charsWritten))
            {
                totalLength += charsWritten;
            }
            else
            {
                // Fallback if buffer too small
                var fallbackProps = (dynamic)msg.Properties;
                var fallbackParameters = new object?[] { fallbackProps.ItemId, fallbackProps.TotalItems, fallbackProps.Timestamp };
                string formatted = ZeroAllocMessageFormatter.Format(msg.Template.AsSpan(), fallbackParameters);
                totalLength += formatted.Length;
            }
        }
        return totalLength;
    }
}