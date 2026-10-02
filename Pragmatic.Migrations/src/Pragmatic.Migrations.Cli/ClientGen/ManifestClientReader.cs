using System.Reflection;
using System.Text.Json;

namespace Pragmatic.Migrations.Cli.ClientGen;

/// <summary>
///     Reads PragmaticManifest JSON from a compiled assembly or a JSON file.
/// </summary>
public static class ManifestClientReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Reads manifest from a JSON file.
    /// </summary>
    public static ClientManifest ReadFromFile(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<ClientManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse manifest from {jsonPath}");
    }

    /// <summary>
    ///     Reads all manifests from a compiled assembly.
    /// </summary>
    public static IReadOnlyList<ClientManifest> ReadFromAssembly(string assemblyPath)
    {
        var results = new List<ClientManifest>();
        var loadContext = new Discovery.AssemblySchemaDiscovery.SchemaLoadContextPublic(assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

        Type[] types;
        try { types = assembly.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        foreach (var type in types)
        {
            if (type.Name != "PragmaticManifest") continue;
            var field = type.GetField("Json", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field?.GetValue(null) is not string json) continue;

            // Surface parse failures on the exported manifest rather than swallowing them:
            // a malformed embedded manifest is a build/SG bug the caller must see.
            ClientManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ClientManifest>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse embedded PragmaticManifest from '{type.FullName}' in '{assemblyPath}': {ex.Message}", ex);
            }

            if (manifest is not null)
                results.Add(manifest);
        }

        // Also scan non-exported types (internal)
        var knownAssemblies = new HashSet<string?>(results.Select(r => r.Assembly));
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name != "PragmaticManifest" || type.IsPublic) continue;
                var field = type.GetField("Json", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field?.GetValue(null) is not string json) continue;

                try
                {
                    var manifest = JsonSerializer.Deserialize<ClientManifest>(json, JsonOptions);
                    if (manifest is not null && knownAssemblies.Add(manifest.Assembly))
                        results.Add(manifest);
                }
                catch (JsonException ex)
                {
                    // Surface (don't silently swallow) a malformed internal manifest, but keep
                    // scanning the rest — other assemblies in the file may still be valid.
                    Console.Error.WriteLine(
                        $"[warning] Skipping malformed PragmaticManifest in '{type.FullName}': {ex.Message}");
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types failed to load (missing deps) — process what we have
            foreach (var type in ex.Types.Where(t => t is not null && t.Name == "PragmaticManifest" && !t!.IsPublic))
            {
                var field = type!.GetField("Json", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field?.GetValue(null) is not string json) continue;
                try
                {
                    var manifest = JsonSerializer.Deserialize<ClientManifest>(json, JsonOptions);
                    if (manifest is not null && knownAssemblies.Add(manifest.Assembly))
                        results.Add(manifest);
                }
                catch (JsonException jex)
                {
                    Console.Error.WriteLine(
                        $"[warning] Skipping malformed PragmaticManifest in '{type!.FullName}': {jex.Message}");
                }
            }
        }

        return results;
    }
}
