using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Abstractions.Analyzers;

/// <summary>
///     Flags message / domain-event / job-parameter payload types that the framework serializes but
///     which are not covered by any <c>[JsonSerializable]</c> in the project's
///     <c>JsonSerializerContext</c> (PRAG2800). Only active once the project declares at least one
///     source-defined <c>JsonSerializerContext</c> — i.e. it has opted into source-generated
///     serialization and is heading for AOT. Reported locally (per handler) so a code fix can apply.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JsonContextCoverageAnalyzer : DiagnosticAnalyzer
{
    private const string MessageHandlerMetadataName = "Pragmatic.Messaging.IMessageHandler`1";
    private const string DomainEventHandlerMetadataName = "Pragmatic.Events.IDomainEventHandler`1";
    private const string JobMetadataName = "Pragmatic.Jobs.IJob`1";
    private const string JsonSerializableMetadataName = "System.Text.Json.Serialization.JsonSerializableAttribute";
    private const string JsonSerializerContextMetadataName = "System.Text.Json.Serialization.JsonSerializerContext";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.MissingJsonContextForBoundaryType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var comp = start.Compilation;

            var markers = new[] { MessageHandlerMetadataName, DomainEventHandlerMetadataName, JobMetadataName }
                .Select(comp.GetTypeByMetadataName)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToImmutableArray();

            var jsonSerializableAttr = comp.GetTypeByMetadataName(JsonSerializableMetadataName);
            var jsonContextBase = comp.GetTypeByMetadataName(JsonSerializerContextMetadataName);
            if (markers.IsEmpty || jsonSerializableAttr is null || jsonContextBase is null)
                return;

            // One-time scan: build the set of types already covered by a [JsonSerializable] and detect
            // whether the project declares any JsonSerializerContext at all (the activation gate).
            var covered = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var hasUserContext = false;
            foreach (var type in EnumerateNamedTypes(comp.Assembly.GlobalNamespace))
            {
                if (!InheritsFrom(type, jsonContextBase))
                    continue;

                // Covers both user [JsonSerializable] contexts and the W3 SG-generated context, which
                // emits [JsonSerializable] markers for the types it covers — so only the types the SG
                // could NOT cover (records / immutable / init-only) remain flagged.
                hasUserContext = true;
                foreach (var t in EnumerateSerializableTypes(type, jsonSerializableAttr))
                    covered.Add(t);
            }

            // No context → the app is on the reflection path; these suggestions would be noise.
            if (!hasUserContext)
                return;

            start.RegisterSymbolAction(
                symbolCtx => AnalyzeHandler(symbolCtx, markers, covered),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeHandler(
        SymbolAnalysisContext context,
        ImmutableArray<INamedTypeSymbol> markers,
        HashSet<INamedTypeSymbol> covered)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        foreach (var iface in type.AllInterfaces)
        {
            if (!iface.IsGenericType
                || !markers.Any(m => SymbolEqualityComparer.Default.Equals(m, iface.OriginalDefinition))
                || iface.TypeArguments.Length != 1
                || iface.TypeArguments[0] is not INamedTypeSymbol payload
                || covered.Contains(payload))
            {
                continue;
            }

            var location = type.Locations.FirstOrDefault(l => l.IsInSource);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingJsonContextForBoundaryType,
                    location,
                    payload.Name));
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSerializableTypes(
        INamedTypeSymbol contextType, INamedTypeSymbol jsonSerializableAttr)
    {
        foreach (var attr in contextType.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, jsonSerializableAttr)
                && attr.ConstructorArguments.Length >= 1
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol serializedType)
            {
                yield return serializedType;
            }
        }
    }

    /// <summary>
    ///     Every named type in the assembly, including types nested inside other types.
    /// </summary>
    /// <remarks>
    ///     Nesting a <c>JsonSerializerContext</c> inside the class that uses it is an ordinary shape.
    ///     Walking namespaces only meant such a context was never seen: the coverage it declares was
    ///     ignored, so its types were reported as uncovered — and if it was the project's <i>only</i>
    ///     context, the activation gate never opened and nothing was reported at all.
    /// </remarks>
    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                foreach (var nested in EnumerateNamedTypes(childNs))
                    yield return nested;
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (var nested in EnumerateWithNested(type))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateWithNested(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var member in type.GetTypeMembers())
        {
            foreach (var nested in EnumerateWithNested(member))
                yield return nested;
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }
}
