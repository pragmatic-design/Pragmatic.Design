using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     Validates that required configuration keys exist in appsettings.json files
///     provided as AdditionalFiles. Cross-references with SG-detected requirements
///     (e.g., [RemoteBoundary] needs Pragmatic:RemoteBoundaries:{Module}:BaseUrl).
/// </summary>
internal static class ConfigurationValidator
{
    /// <summary>
    ///     Checks that all RemoteBoundary modules have BaseUrl configured in appsettings.json.
    /// </summary>
    public static void ValidateRemoteBoundaryConfig(
        SourceProductionContext context,
        IReadOnlyList<string> remoteBoundaryModules,
        IReadOnlyDictionary<string, string> configKeys)
    {
        foreach (var moduleName in remoteBoundaryModules)
        {
            var expectedKey = $"Pragmatic:RemoteBoundaries:{moduleName}:BaseUrl";

            if (!configKeys.ContainsKey(expectedKey))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.ConfigKeyMissing,
                    Location.None,
                    expectedKey,
                    $"RemoteBoundary<{moduleName}>",
                    "appsettings.json"));
            }
        }
    }

    /// <summary>
    ///     Parses appsettings.json and extracts all configuration key paths.
    ///     Keys are flattened with ":" separator (e.g., "Pragmatic:RemoteBoundaries:Billing:BaseUrl").
    /// </summary>
    public static Dictionary<string, string> ParseConfigKeys(SourceText? text)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (text is null)
            return keys;

        try
        {
            var json = JsonDocument.Parse(text.ToString());
            FlattenJson(json.RootElement, "", keys);
        }
        catch (JsonException)
        {
            // Malformed JSON — skip silently, the runtime will catch it
        }

        return keys;
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}:{property.Name}";
                    FlattenJson(property.Value, key, keys);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJson(item, $"{prefix}:{index}", keys);
                    index++;
                }

                break;
            default:
                keys[prefix] = element.ToString();
                break;
        }
    }
}
