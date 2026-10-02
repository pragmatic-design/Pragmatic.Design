// Pragmatic.SourceGenerator - Composition - Metadata Reader

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Reads [PragmaticMetadata] and [PragmaticModuleMetadata] attributes from referenced assemblies for HOST mode.
/// </summary>
/// <remarks>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>MetadataReader.cs - Core metadata reading (this file)</description>
///             </item>
///             <item>
///                 <description>MetadataReader.Modules.cs - Module extraction and parsing</description>
///             </item>
///             <item>
///                 <description>MetadataReader.DomainModules.cs - Domain module metadata reading</description>
///             </item>
///             <item>
///                 <description>MetadataReader.Endpoints.cs - Endpoint metadata extraction from JSON</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
internal static partial class MetadataReader
{
    private const string MetadataAttributeFullName = "Pragmatic.Composition.Attributes.PragmaticMetadataAttribute";

    private const string DomainModuleMetadataAttributeFullName =
        "Pragmatic.Actions.Metadata.PragmaticModuleMetadataAttribute";


    /// <summary>
    ///     Reads all metadata from referenced assemblies.
    /// </summary>
    public static ImmutableArray<AssemblyMetadataModel> ReadFromReferences(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<AssemblyMetadataModel>();
        var metadataAttribute = compilation.GetTypeByMetadataName(MetadataAttributeFullName);

        if (metadataAttribute is null)
            return results.ToImmutable();

        // Scan all referenced assemblies
        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;

            var entries = ReadFromAssembly(assembly, metadataAttribute, cancellationToken);
            if (entries.Length > 0)
                results.Add(new AssemblyMetadataModel
                {
                    AssemblyName = assembly.Name,
                    Entries = entries
                });
        }

        return results.ToImmutable();
    }

    private static ImmutableArray<MetadataEntry> ReadFromAssembly(
        IAssemblySymbol assembly,
        INamedTypeSymbol metadataAttribute,
        CancellationToken cancellationToken)
    {
        var entries = ImmutableArray.CreateBuilder<MetadataEntry>();

        foreach (var attribute in assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, metadataAttribute))
                continue;

            var entry = ParseMetadataAttribute(attribute);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries.ToImmutable();
    }

    private static MetadataEntry? ParseMetadataAttribute(AttributeData attribute)
    {
        var args = attribute.ConstructorArguments;
        if (args.Length < 3)
            return null;

        // Category (enum)
        var categoryValue = args[0].Value;
        var category = categoryValue?.ToString() ?? "Unknown";

        // SchemaVersion (string)
        var schemaVersion = args[1].Value?.ToString() ?? "1.0.0";

        // JsonData (string)
        var jsonData = args[2].Value?.ToString() ?? "{}";

        // Parse registration methods from JSON
        var registrationMethod = ExtractMethod(jsonData, "registrationMethod");
        var secondary = ExtractMethod(jsonData, "mutationsRegistrationMethod");

        return new MetadataEntry
        {
            Category = category,
            SchemaVersion = schemaVersion,
            RegistrationMethod = registrationMethod,
            SecondaryRegistrationMethod = secondary,
            JsonData = jsonData
        };
    }

    private static string ExtractMethod(string jsonData, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop))
                return prop.GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Malformed metadata JSON — skip but log for debugging
            System.Diagnostics.Debug.WriteLine($"[Pragmatic.SG] Failed to extract registration method: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    ///     Extracts required configuration sections from Startup metadata entries across all assemblies.
    ///     Reads data.modules[*].requiredConfigs from MetadataCategory.Startup (category "3") entries.
    /// </summary>
    public static ImmutableArray<RequiredConfigSectionInfo> ExtractRequiredConfigSections(
        ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var results = ImmutableArray.CreateBuilder<RequiredConfigSectionInfo>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                // MetadataCategory.Startup = 3
                if (entry.Category != MetadataCategoryIds.Startup)
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    if (!doc.RootElement.TryGetProperty("data", out var data))
                        continue;

                    if (!data.TryGetProperty("modules", out var modules))
                        continue;

                    foreach (var module in modules.EnumerateArray())
                    {
                        var moduleType = string.Empty;
                        if (module.TryGetProperty("type", out var typeProp))
                            moduleType = typeProp.GetString() ?? string.Empty;

                        if (!module.TryGetProperty("requiredConfigs", out var configs))
                            continue;

                        foreach (var config in configs.EnumerateArray())
                        {
                            var sectionPath = config.GetString();
                            if (!string.IsNullOrEmpty(sectionPath))
                                results.Add(new RequiredConfigSectionInfo
                                {
                                    SectionPath = sectionPath!,
                                    SourceModuleType = moduleType
                                });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Malformed metadata JSON — skip but log for debugging
                    System.Diagnostics.Debug.WriteLine($"[Pragmatic.SG] Failed to parse startup metadata from {assembly.AssemblyName}: {ex.Message}");
                }
            }

        return results.ToImmutable();
    }
}
