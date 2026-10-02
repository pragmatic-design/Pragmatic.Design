// Pragmatic.Discovery - Host Topology Info

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Metadata;

namespace Pragmatic.Discovery.Models;

/// <summary>
/// Represents the deployment topology of a single host.
/// Parsed from the <c>[assembly: PragmaticMetadata(HostTopology, ...)]</c> attribute
/// emitted by the Composition source generator.
/// </summary>
public sealed record HostTopologyInfo
{
    /// <summary>The logical host name (from the root namespace of the host project).</summary>
    public required string HostName { get; init; }

    /// <summary>All modules deployed in this host, with their database assignments.</summary>
    public IReadOnlyList<ModuleDeploymentInfo> Modules { get; init; } = [];

    /// <summary>ReadAccess declarations per boundary (cross-boundary SQL join intent).</summary>
    public IReadOnlyList<BoundaryReadAccessInfo> Boundaries { get; init; } = [];

    /// <summary>UTC timestamp when this topology was registered.</summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The HostTopology schema major version this build understands.</summary>
    public const int SchemaMajor = 1;

    /// <summary>
    /// Whether a metadata schema version is compatible with this consumer: the MAJOR component must equal
    /// <see cref="SchemaMajor"/>. Minor/patch differences are tolerated (best-effort compatibility). A
    /// missing or unparseable version is treated as compatible — the emitter owns the format, so an
    /// unrecognized string is not evidence of a breaking change.
    /// </summary>
    public static bool IsCompatibleVersion(string? schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
            return true;

        var dot = schemaVersion!.IndexOf('.');
        var majorText = dot < 0 ? schemaVersion : schemaVersion[..dot];
        return !int.TryParse(majorText, out var major) || major == SchemaMajor;
    }

    /// <summary>
    /// Reads the HostTopology metadata from the <see cref="AssemblyMetadataRegistry"/> (zero-reflection).
    /// Returns null if no HostTopology metadata is found or its schema major version is incompatible with
    /// <see cref="SchemaMajor"/> (a major mismatch is refused rather than best-effort mis-parsed).
    /// </summary>
    public static HostTopologyInfo? FromRegistry()
    {
        var entry = AssemblyMetadataRegistry.FindByCategory(MetadataCategory.HostTopology);
        if (!entry.HasValue)
            return null;

        return IsCompatibleVersion(entry.Value.SchemaVersion) ? Parse(entry.Value.JsonData) : null;
    }

    /// <summary>
    /// Reads the HostTopology metadata attribute from the given assembly and parses it.
    /// Returns null if the assembly has no HostTopology metadata or its schema major version is
    /// incompatible with <see cref="SchemaMajor"/>.
    /// </summary>
    [RequiresUnreferencedCode("Uses assembly attribute reflection. Prefer FromRegistry() for AOT-safe access.")]
    public static HostTopologyInfo? FromAssembly(Assembly assembly)
    {
        var attr = assembly
            .GetCustomAttributes<PragmaticMetadataAttribute>()
            .FirstOrDefault(a => a.Category == MetadataCategory.HostTopology);

        if (attr is null)
            return null;

        return IsCompatibleVersion(attr.SchemaVersion) ? Parse(attr.JsonData) : null;
    }

    /// <summary>
    /// Reads the HostTopology metadata registered by the generator for the running host.
    /// Returns null if no metadata is registered.
    /// </summary>
    public static HostTopologyInfo? FromEntryAssembly()
    {
        // Registry only. The generator fills it from a module initializer, so a host it built has the
        // entry; where it did not, reading the same attribute reflectively would have found nothing
        // either. Callers holding only a DLL want FromAssembly, which says what it does.
        return FromRegistry();
    }

    /// <summary>
    /// Parses a HostTopology JSON string into a <see cref="HostTopologyInfo"/>.
    /// JSON format:
    /// <code>
    /// {
    ///   "host": "Host",
    ///   "includes": [
    ///     { "module": "BillingModule", "database": "ShowcaseFinancialDatabase", "provider": "InMemory" }
    ///   ],
    ///   "boundaries": [
    ///     { "name": "Booking", "readAccess": ["Property", "RoomType"] }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    public static HostTopologyInfo? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var hostName = root.TryGetProperty("host", out var hostProp)
                ? hostProp.GetString() ?? "Unknown"
                : "Unknown";

            var modules = new List<ModuleDeploymentInfo>();
            if (root.TryGetProperty("includes", out var includes))
                foreach (var item in includes.EnumerateArray())
                {
                    var moduleName = item.TryGetProperty("module", out var modProp) ? modProp.GetString() : null;
                    if (moduleName is null)
                        continue; // skip malformed entries with missing or null "module" field

                    modules.Add(new ModuleDeploymentInfo
                    {
                        ModuleName = moduleName,
                        DatabaseName = item.TryGetProperty("database", out var db) ? db.GetString() : null,
                        Provider = item.TryGetProperty("provider", out var prov) ? prov.GetString() : null,
                        ConfigKey = item.TryGetProperty("configKey", out var ck) ? ck.GetString() : null,
                        DbContext = item.TryGetProperty("dbContext", out var dc) ? dc.GetString() : null
                    });
                }

            var boundaries = new List<BoundaryReadAccessInfo>();
            if (root.TryGetProperty("boundaries", out var bndArray))
                foreach (var bnd in bndArray.EnumerateArray())
                {
                    var name = bnd.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (name is null)
                        continue; // skip malformed entries with missing or null "name" field

                    var readAccess = new List<string>();
                    if (bnd.TryGetProperty("readAccess", out var ra))
                        foreach (var entity in ra.EnumerateArray())
                        {
                            var entityName = entity.GetString();
                            if (entityName is not null)
                                readAccess.Add(entityName);
                        }

                    boundaries.Add(new BoundaryReadAccessInfo { BoundaryName = name, EntityTypes = readAccess });
                }

            return new HostTopologyInfo
            {
                HostName = hostName,
                Modules = modules,
                Boundaries = boundaries
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // Valid JSON but the wrong shape (e.g. "includes" is an object not an array, or "host" is
            // a number): JsonElement.EnumerateArray()/GetString() throw InvalidOperationException.
            // Parse is documented to return null on invalid input, so treat shape errors the same way
            // instead of letting them propagate to the caller.
            return null;
        }
    }
}
