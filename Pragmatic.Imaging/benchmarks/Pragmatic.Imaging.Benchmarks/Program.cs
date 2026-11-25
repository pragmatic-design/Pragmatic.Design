using BenchmarkDotNet.Running;
using Pragmatic.Imaging.Benchmarks;

// `verify` checks that every engine computes what the benchmark names say, without timing anything: it
// throws, and exits non-zero, when one does not.
if (args is ["verify"])
{
    Verification.Run();
    Console.WriteLine("✅ Both engines computed what the benchmark names say.");
    return;
}

Verification.Run();
BenchmarkSwitcher.FromAssembly(typeof(UploadPathBenchmarks).Assembly).Run(args);
