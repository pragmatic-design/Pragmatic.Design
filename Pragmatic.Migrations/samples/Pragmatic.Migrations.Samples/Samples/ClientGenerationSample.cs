using Pragmatic.Migrations.Cli.ClientGen;

namespace Pragmatic.Migrations.Samples.Samples;

/// <summary>
///     Scenario 12 — typed client generation. The CLI <c>manifest</c> command emits a
///     <see cref="ClientManifest" /> (endpoints + types) from a host assembly; <c>generate</c>
///     then turns that manifest into a typed client via <see cref="CSharpClientGenerator" /> or
///     <see cref="TypeScriptClientGenerator" />. Both generators write files to an output directory.
///     Here a manifest is built by hand and both clients are generated to a temp folder, then the
///     produced files are listed.
/// </summary>
public static class ClientGenerationSample
{
    public static void Run()
    {
        Console.WriteLine("--- Scenario 12: Typed client generation (C# + TypeScript) ---");

        var manifest = BuildManifest();
        var outRoot = Path.Combine(Path.GetTempPath(), $"prag-clientgen-{Guid.NewGuid():N}");

        try
        {
            var csOut = Path.Combine(outRoot, "csharp");
            new CSharpClientGenerator(manifest, "Catalog.Client", csOut).Generate();
            Console.WriteLine($"  C# client generated → {csOut}");
            foreach (var f in EnumerateFiles(csOut))
                Console.WriteLine($"    - {f}");

            var tsOut = Path.Combine(outRoot, "typescript");
            new TypeScriptClientGenerator(manifest, "@catalog/client", tsOut).Generate();
            Console.WriteLine($"  TypeScript client generated → {tsOut}");
            foreach (var f in EnumerateFiles(tsOut))
                Console.WriteLine($"    - {f}");

            // Show a slice of the generated TS client so the output is tangible.
            var clientTs = Path.Combine(tsOut, "src", "client.ts");
            if (File.Exists(clientTs))
            {
                Console.WriteLine("  src/client.ts (excerpt):");
                foreach (var line in File.ReadLines(clientTs).Take(12))
                    Console.WriteLine($"    | {line}");
            }

            Console.WriteLine("  verdict            : one manifest drives both a C# project and a TypeScript package.");
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }

        Console.WriteLine();
    }

    private static IEnumerable<string> EnumerateFiles(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                       .Select(p => Path.GetRelativePath(root, p))
                       .OrderBy(p => p)
            : [];

    private static ClientManifest BuildManifest() => new()
    {
        Assembly = "Showcase.Catalog",
        Endpoints = new List<ClientEndpoint>
        {
            new()
            {
                OperationId = "Catalog.GetProduct",
                HttpMethod = "GET",
                FullRoute = "/products/{id}",
                Summary = "Get a product by id.",
                Response = new ClientResponse { Type = "ProductDto" },
                Parameters = new List<ClientParam>
                {
                    new() { Name = "id", In = "path", Type = "int", IsRequired = true },
                },
            },
            new()
            {
                OperationId = "Catalog.SearchProducts",
                HttpMethod = "GET",
                FullRoute = "/products",
                Summary = "Search products.",
                Response = new ClientResponse { Type = "ProductDto", IsPaged = true },
                Parameters = new List<ClientParam>
                {
                    new() { Name = "query", In = "query", Type = "string", IsRequired = false },
                },
            },
        },
        Types = new List<ClientType>
        {
            new()
            {
                SimpleName = "Product",
                Kind = "entity",
                Properties = new List<ClientProperty>
                {
                    new() { Name = "Id", Type = "int" },
                    new() { Name = "Name", Type = "string" },
                    new() { Name = "Price", Type = "decimal" },
                },
            },
        },
    };
}
