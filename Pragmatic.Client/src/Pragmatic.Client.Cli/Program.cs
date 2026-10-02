using Pragmatic.Client.Cli;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("pragmatic-client — TypeScript client generator for Pragmatic.Design APIs");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  pragmatic-client ts --manifest <PragmaticManifest.json> --out <directory>");
    Console.WriteLine();
    Console.WriteLine("Emits per module: types.ts, errors.ts, client.ts, paging.ts");
    return 0;
}

if (args[0] != "ts")
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'. Supported: ts");
    return 1;
}

string? manifestPath = null;
string? outDir = null;
for (var i = 1; i < args.Length - 1; i++)
    switch (args[i])
    {
        case "--manifest":
            manifestPath = args[++i];
            break;
        case "--out":
            outDir = args[++i];
            break;
    }

if (manifestPath is null || outDir is null)
{
    Console.Error.WriteLine("Both --manifest <path> and --out <directory> are required.");
    return 1;
}

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"Manifest not found: {manifestPath}");
    return 1;
}

var json = File.ReadAllText(manifestPath);

IReadOnlyList<ManifestJson.ManifestDoc> modules;
try
{
    modules = ManifestJson.Read(json);
}
catch (Exception e)
{
    Console.Error.WriteLine($"Manifest could not be parsed: {e.Message}");
    return 1;
}

var emitted = 0;
foreach (var module in modules)
{
    if (module.Endpoints is not { Count: > 0 })
        continue;

    var moduleDir = modules.Count > 1
        ? Path.Combine(outDir, module.BoundaryName.ToLowerInvariant())
        : outDir;
    Directory.CreateDirectory(moduleDir);

    foreach (var (file, content) in TsEmitter.Emit(module, json))
    {
        File.WriteAllText(Path.Combine(moduleDir, file), content);
        emitted++;
    }

    Console.WriteLine($"{module.BoundaryName}: {moduleDir}");
}

if (emitted == 0)
{
    Console.Error.WriteLine("No endpoints found in the manifest — nothing emitted.");
    return 1;
}

Console.WriteLine($"Emitted {emitted} file(s).");
return 0;
