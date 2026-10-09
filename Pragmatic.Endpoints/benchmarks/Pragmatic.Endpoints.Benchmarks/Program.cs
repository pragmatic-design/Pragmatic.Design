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
    new Pragmatic.Endpoints.Benchmarks.Probe.CitmProbeBenchmarks().Setup();
    new Pragmatic.Endpoints.Benchmarks.Probe.PageProbeBenchmarks().Setup();
    new Pragmatic.Endpoints.Benchmarks.Probe.CanadaProbeBenchmarks().Setup();
    Console.WriteLine("The probes' candidates write the host's very bytes.");
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(ResponseSerializationBenchmarks<>).Assembly).Run(args);

static void Verify<T>()
{
    new ResponseSerializationBenchmarks<T>().Setup();
    Console.WriteLine($"{typeof(T).FullName}: the competitors write the same document"
                      + (Pragmatic.Endpoints.Benchmarks.Serialization.GeneratedWriters.For<T>() is null
                          ? "; the generator wrote no writer for it."
                          : ", and the generated writer the host's very bytes."));
}
