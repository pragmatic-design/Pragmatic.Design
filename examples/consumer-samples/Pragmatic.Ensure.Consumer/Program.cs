using Pragmatic.Ensure;

Console.WriteLine("=== Pragmatic.Ensure.Consumer (PackageReference) ===");

var name = "acme";
Ensure.ThrowIfNullOrWhiteSpace(name);
Console.WriteLine($"  non-null guard passed for name='{name}'");

try
{
    string? empty = null;
    Ensure.ThrowIfNullOrWhiteSpace(empty!);
}
catch (ArgumentException ex)
{
    Console.WriteLine($"  null guard threw as expected: {ex.ParamName} -> {ex.Message.Split('(')[0].Trim()}");
}

var count = 7;
Ensure.ThrowIfLessThan(count, 1);
Console.WriteLine($"  lower-bound guard passed for count={count}");

Console.WriteLine("=== done ===");
