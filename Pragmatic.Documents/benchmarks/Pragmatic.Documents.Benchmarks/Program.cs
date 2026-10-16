using BenchmarkDotNet.Running;
using Pragmatic.Documents.Benchmarks;

// `verify` runs the setup checks of every benchmark and times nothing: the two data paths resolve the same
// document, the PDF is one, and both XLSX writers write the same cells. Anything else goes to BenchmarkDotNet.
if (args is ["verify"])
{
    new TemplateRenderBenchmarks().Setup();
    new SpreadsheetRenderBenchmarks().Setup();
    Console.WriteLine("Both data paths resolve the same document, and both writers the same sheet.");
    return;
}

// `memory` measures the peak memory of one render as the report grows, each size in a process of its own
// (`memory-one <format> <rows>` is that child). It is a measure, not a benchmark: BenchmarkDotNet counts bytes
// allocated per call, not bytes alive at once.
if (args is ["memory"])
{
    PeakMemory.RunAll();
    return;
}

if (args is ["memory-one", var format, var rows])
{
    PeakMemory.RunOne(format, int.Parse(rows, System.Globalization.CultureInfo.InvariantCulture));
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(TemplateRenderBenchmarks).Assembly).Run(args);
