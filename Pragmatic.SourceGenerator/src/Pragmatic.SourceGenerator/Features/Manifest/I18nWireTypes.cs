using System.Linq;
using Pragmatic.SourceGenerator.Features.Manifest.Models;

namespace Pragmatic.SourceGenerator.Features.Manifest;

/// <summary>
///     The internationalization value types whose JSON converters an application needs as soon as one
///     of them travels on the wire, and the reading of a manifest that answers "does one?".
/// </summary>
/// <remarks>
///     <para>
///         The list is the one <c>JsonSerializerOptionsExtensions.AddPragmaticInternationalization</c>
///         installs converters for. It is written as names because the generator targets
///         netstandard2.0 and cannot reference the package — and pinned against the real types by
///         <c>TheConvertersAnAmountNeedsTests</c>, which names each with <c>typeof</c> and compares the
///         set with what the extension actually adds. A list that ages in silence is the failure this
///         repository has been paying for elsewhere.
///     </para>
///     <para>
///         Read off the manifest because that model <b>is</b> the wire closure: the endpoints' results,
///         their request members, and the members of those — walked once, for the client generator,
///         before anybody asked this question.
///     </para>
/// </remarks>
internal static class I18nWireTypes
{
    private const string Namespace = "Pragmatic.Internationalization.Types.";

    /// <summary>The types the converters are for, fully qualified as the manifest writes them.</summary>
    internal static readonly string[] Names =
    [
        Namespace + "Money",
        Namespace + "CurrencyCode",
        Namespace + "LanguageCode",
        Namespace + "CountryCode",
        Namespace + "CultureCode",
        Namespace + "LocalizedString"
    ];

    /// <summary>Whether anything this manifest publishes carries one of them.</summary>
    /// <remarks>
    ///     ⚠️ A prefix match on the declared type: the manifest writes a nullable as
    ///     <c>Money?</c> and a collection as <c>Money[]</c>, and both need the same converters.
    /// </remarks>
    public static bool AreOnTheWire(ManifestModel? manifest)
    {
        if (manifest is null)
            return false;

        foreach (var type in manifest.Types)
            foreach (var property in type.Properties)
                if (IsOne(property.Type))
                    return true;

        foreach (var endpoint in manifest.Endpoints)
        {
            if (IsOne(endpoint.ResponseType?.Type))
                return true;

            foreach (var parameter in endpoint.Parameters)
                if (IsOne(parameter.Type))
                    return true;

            if (endpoint.RequestBody is { } body)
                foreach (var property in body.Properties)
                    if (IsOne(property.Type))
                        return true;
        }

        return false;
    }

    private static bool IsOne(string? declaredType)
        => declaredType is not null && Names.Any(name => declaredType.StartsWith(name, System.StringComparison.Ordinal));
}
