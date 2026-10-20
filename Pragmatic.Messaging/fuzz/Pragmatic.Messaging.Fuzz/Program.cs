using Pragmatic.Messaging.Fuzz;
using SharpFuzz;

// libfuzzer-dotnet starts this process with one argument, the assembly; the target comes from the environment, which
// the child inherits (scripts/fuzz.mjs sets it).
var name = Environment.GetEnvironmentVariable("PRAGMATIC_FUZZ_TARGET");
if (name is null || !FuzzTargets.All.TryGetValue(name, out var target))
{
    Console.Error.WriteLine($"PRAGMATIC_FUZZ_TARGET must name one of: {string.Join(", ", FuzzTargets.All.Keys)}.");
    return 2;
}

Fuzzer.LibFuzzer.Run(target);
return 0;
