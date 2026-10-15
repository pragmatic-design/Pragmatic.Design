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

BenchmarkSwitcher.FromAssembly(typeof(TemplateRenderBenchmarks).Assembly).Run(args);
