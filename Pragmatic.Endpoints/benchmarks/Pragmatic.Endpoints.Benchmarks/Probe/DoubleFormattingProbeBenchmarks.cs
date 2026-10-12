using System.Globalization;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     What formatting a double costs on its own (#149), apart from any writer: <c>double.TryFormat</c> into
///     UTF-8, as <c>Utf8JsonRun.NumberValue(double)</c> calls it, over every coordinate of <c>canada.json</c> and
///     over a set of values chosen to take every branch of the formatter.
/// </summary>
[MemoryDiagnoser]
public class DoubleFormattingProbeBenchmarks
{
    private double[] _canada = [];
    private readonly byte[] _buffer = new byte[32];

    /// <summary>
    ///     Integers, both notations and both exponent signs, subnormals, the extremes, negative zero, and values
    ///     whose shortest form has 15, 16 and 17 significant digits.
    /// </summary>
    private static readonly double[] Branches =
    [
        0.0, -0.0, 1.0, -1.0, 60.0, 123456.0, 9007199254740992.0, 1e15, 1e16, 1e17, 1e21, 1.2345678901234568E+17,
        0.0001, 0.00001, 1e-7, 0.1, 0.3, 2.0 / 3.0, 1.0 / 3.0, Math.PI, Math.E,
        double.Epsilon, 2.2250738585072009E-308, 2.2250738585072014E-308, double.MaxValue, double.MinValue,
        123456789012345.0, 1234567890123456.0, 12345678901234567.0, 0.1 + 0.2, 5e-324, 1.7976931348623157E+308,
    ];

    [GlobalSetup]
    public void Setup()
    {
        var root = SimdJsonDocuments.Load<Canada.Root>("canada.json");
        _canada = [.. root.features!.SelectMany(f => f.geometry!.coordinates!).SelectMany(ring => ring).SelectMany(point => point)];
    }

    [Benchmark(Baseline = true)]
    public int TryFormat_Canada() => FormatAll(_canada);

    [Benchmark]
    public int TryFormat_Branches() => FormatAll(Branches);

    private int FormatAll(double[] values)
    {
        var total = 0;
        foreach (var value in values)
        {
            value.TryFormat(_buffer, out var written, provider: CultureInfo.InvariantCulture);
            total += written;
        }

        return total;
    }
}
