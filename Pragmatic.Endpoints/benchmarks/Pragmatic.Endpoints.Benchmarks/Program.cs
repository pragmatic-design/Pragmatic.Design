using BenchmarkDotNet.Running;
using Pragmatic.Endpoints.Benchmarks;
using Pragmatic.Endpoints.Benchmarks.Documents;

// `verify` runs the equivalence check of every workload and times nothing: the quick way to know the
// competitors still write the same document. Anything else goes to BenchmarkDotNet.
if (args is ["verify"])
{
    Verify<Twitter.Root>();
    Verify<CitmCatalog.Root>();
    Verify<Canada.Root>();
    Verify<ReservationPage>();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(ResponseSerializationBenchmarks<>).Assembly).Run(args);

static void Verify<T>()
{
    new ResponseSerializationBenchmarks<T>().Setup();
    Console.WriteLine($"{typeof(T).FullName}: the four competitors write the same document.");
}
