using System.Reflection;
using System.Runtime.Loader;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Cli.Discovery;

/// <summary>
///     Discovers SG-generated SchemaVersion instances from a host assembly.
///     Uses a custom AssemblyLoadContext to isolate the host assembly and share
///     the Pragmatic.Migrations types with the CLI.
/// </summary>
/// <remarks>
///     <para><b>SECURITY — TRUST ASSUMPTION:</b> <see cref="Discover"/> <i>loads and EXECUTES code</i>
///     from the target assembly path. Loading the assembly and reading the value of a static
///     <c>SchemaVersion</c> field triggers the type's static constructor and any module/static
///     initializers in the target assembly. The schema values are runtime field values (not just
///     metadata), so a metadata-only inspection context (<c>MetadataLoadContext</c>) cannot be used
///     here — reading the values inherently requires execution.</para>
///     <para>Only ever point this CLI at an assembly you built and trust. Do NOT run it against
///     untrusted or third-party assemblies: arbitrary code in them will run with the CLI's
///     privileges. The method validates the path exists but performs no sandboxing.</para>
/// </remarks>
public static class AssemblySchemaDiscovery
{
    /// <summary>
    ///     Loads the host assembly and finds all static SchemaVersion fields.
    /// </summary>
    /// <remarks>
    ///     Executes code from <paramref name="assemblyPath"/> (static initializers run on load).
    ///     See the type-level security remarks: only use against trusted, self-built assemblies.
    /// </remarks>
    public static IReadOnlyList<SchemaVersion> Discover(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Assembly not found: {fullPath}");

        // The CLI is about to load and EXECUTE code from this assembly (static initializers).
        // Make that explicit at runtime so it is never a silent side-effect.
        Console.Error.WriteLine(
            $"[warning] Loading and executing code from '{fullPath}' to read schema metadata. " +
            "Only run this against assemblies you trust.");

        var loadContext = new SchemaLoadContext(fullPath);
        var assembly = loadContext.LoadFromAssemblyPath(fullPath);

        var schemas = new List<SchemaVersion>();

        // Use GetTypes with error handling — host assembly has ASP.NET Core deps
        // that may not be loadable in console context. SchemaVersion types don't need them.
        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Surface the partial-load failure (do not swallow it silently) and continue with
            // whatever types did load — the SchemaVersion holders rarely need the failed deps.
            Console.Error.WriteLine(
                $"[warning] Some types in '{fullPath}' failed to load ({ex.LoaderExceptions.Length} loader error(s)); " +
                "continuing with the types that loaded.");
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            // Look for static fields or properties of type SchemaVersion
            var field = type.GetField("Current", BindingFlags.Public | BindingFlags.Static);
            if (field is not null && field.FieldType.FullName == typeof(SchemaVersion).FullName)
            {
                if (field.GetValue(null) is SchemaVersion schema)
                    schemas.Add(schema);
                continue;
            }

            // Also check static properties (some SG patterns use properties)
            var prop = type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            if (prop is not null && prop.PropertyType.FullName == typeof(SchemaVersion).FullName)
            {
                if (prop.GetValue(null) is SchemaVersion schema)
                    schemas.Add(schema);
            }
        }

        return schemas;
    }

    /// <summary>
    ///     Auto-discovers the assembly path from the current directory.
    ///     Looks for a single .csproj, resolves the output directory, finds the .dll.
    /// </summary>
    public static string? AutoDiscoverAssemblyPath()
    {
        var csprojFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        if (csprojFiles.Length != 1) return null;

        var projectDir = Path.GetDirectoryName(csprojFiles[0])!;
        var projectName = Path.GetFileNameWithoutExtension(csprojFiles[0]);

        // Check common output paths
        string[] candidates =
        [
            Path.Combine(projectDir, "bin", "Debug", "net10.0", $"{projectName}.dll"),
            Path.Combine(projectDir, "bin", "Release", "net10.0", $"{projectName}.dll"),
            Path.Combine(projectDir, "bin", "Debug", "net9.0", $"{projectName}.dll")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    ///     Isolated load context that shares Pragmatic.Migrations types
    ///     with the CLI to ensure SchemaVersion type identity.
    /// </summary>
    /// <summary>Public alias for use by ManifestClientReader.</summary>
    public sealed class SchemaLoadContextPublic(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly string _assemblyDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        private readonly AssemblyDependencyResolver _resolver = new(Path.GetFullPath(assemblyPath));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is not null) return LoadFromAssemblyPath(resolved);
            var path = Path.Combine(_assemblyDir, $"{assemblyName.Name}.dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    private sealed class SchemaLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly string _assemblyDir = Path.GetDirectoryName(assemblyPath)!;
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Share Pragmatic.Migrations types with the CLI context for type identity
            if (assemblyName.Name == "Pragmatic.Migrations")
                return typeof(SchemaVersion).Assembly;

            // Use deps.json resolver (handles shared frameworks like ASP.NET Core)
            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is not null)
                return LoadFromAssemblyPath(resolved);

            // Fallback: try the assembly's directory
            var path = Path.Combine(_assemblyDir, $"{assemblyName.Name}.dll");
            if (File.Exists(path))
                return LoadFromAssemblyPath(path);

            return null;
        }
    }
}
