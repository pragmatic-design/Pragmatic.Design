using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     The providers that write text start their output with the first record, not a UTF-8 byte order
///     mark.
/// </summary>
/// <remarks>
///     <c>Encoding.UTF8</c> carries the BOM as its preamble, and a <c>StreamWriter</c> built with it
///     writes EF BB BF at the start of a new file or stream. A JSON-lines reader then fails on the first
///     record (<c>'0xEF' is an invalid start of a value</c>), and so does any shipper that parses line by
///     line. Each test reads the first byte of what the provider wrote.
/// </remarks>
public class NoByteOrderMarkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pragmatic-bom-{Guid.NewGuid():N}");

    public NoByteOrderMarkTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void JsonProvider_ToAStream_StartsWithTheRecord()
    {
        var output = new MemoryStream();
        using (var provider = new PragmaticJsonProvider("json", PragmaticJsonConfiguration.ForJson(), output))
            provider.WriteLog(Entry());

        output.ToArray().Should().NotBeEmpty();
        output.ToArray()[0].Should().Be((byte)'{');
    }

    [Fact]
    public void JsonProvider_ToANewFile_StartsWithTheRecord()
    {
        using (var provider = new PragmaticJsonProvider("json", PragmaticJsonConfiguration.ForJson(), Path.Combine(_directory, "log.json")))
            provider.WriteLog(Entry());

        FirstByteOfTheOnlyFile().Should().Be((byte)'{');
    }

    [Fact]
    public void EnhancedJsonProvider_ToANewFile_StartsWithTheRecord()
    {
        var config = new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            Performance = new PerformanceConfiguration { EnableBatching = true },
        };
        config.CustomProperties["EnableNDJSON"] = true;

        using (var provider = new PragmaticEnhancedJsonProvider("enhanced", config, Path.Combine(_directory, "log.ndjson")))
            provider.WriteLog(Entry());

        FirstByteOfTheOnlyFile().Should().Be((byte)'{');
    }

    /// <summary>The file provider writes text, not JSON: the first byte is anything but the BOM's first.</summary>
    [Fact]
    public void FileProvider_ToANewFile_DoesNotStartWithAByteOrderMark()
    {
        using (var provider = new PragmaticFileProvider("file", PragmaticProviderConfiguration.ForFile(), Path.Combine(_directory, "log.txt")))
            provider.WriteLog(Entry());

        FirstByteOfTheOnlyFile().Should().NotBe((byte)0xEF);
    }

    private byte FirstByteOfTheOnlyFile()
    {
        var files = Directory.GetFiles(_directory, "*", SearchOption.AllDirectories);
        files.Should().ContainSingle();

        var bytes = File.ReadAllBytes(files[0]);
        bytes.Should().NotBeEmpty();
        return bytes[0];
    }

    private static LogEntry Entry() => new()
    {
        Timestamp = DateTime.UtcNow,
        LogLevel = LogLevel.Information,
        Category = "Bom",
        Message = "first record",
    };
}
