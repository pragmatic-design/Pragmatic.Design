using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;

namespace Pragmatic.SourceGenerator.Features.Glossary;

/// <summary>
///     Standalone feature: generates an AsyncAPI document (<c>PragmaticAsyncApi.Json</c>) cataloguing
///     the domain events (types implementing <c>IDomainEvent</c>) as a compile-time constant.
/// </summary>
internal static class AsyncApiFeature
{
    private const string DomainEventInterface = "Pragmatic.Events.IDomainEvent";
    private const string IntegrationEventInterface = "Pragmatic.Events.IIntegrationEvent";
    private const string EventsNamespace = "Pragmatic.Events";
    private const string PublicEventAttribute = "PublicEventAttribute";
    private const string ObsoleteEventAttribute = "ObsoleteEventAttribute";

    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        // BaseList pre-filter: an event necessarily implements IDomainEvent (directly or via a
        // base), so a type with no base list can never match — this skips the semantic-model
        // GetDeclaredSymbol call for the vast majority of types on every edit.
        var events = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax { BaseList: not null } or RecordDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => ToEvent(ctx))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        var withAssembly = events.Collect().Combine(
            context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? ""));

        context.RegisterSourceOutputSafe(withAssembly, static (spc, data) =>
        {
            var (models, assemblyName) = data;
            if (models.Length == 0)
                return;

            var artifact = new AsyncApiTemplate(models, assemblyName).RenderOutput();
            if (!artifact.IsEmpty)
                spc.AddSource(artifact);
        });
    }

    private static AsyncApiEventModel? ToEvent(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol { IsAbstract: false } symbol)
            return null;

        var interfaces = symbol.AllInterfaces;
        var isEvent = interfaces.Any(i => i.ToDisplayString() == DomainEventInterface);
        if (!isEvent)
            return null;

        var attributes = symbol.GetAttributes();
        var isPublic = interfaces.Any(i => i.ToDisplayString() == IntegrationEventInterface)
            || attributes.Any(a => IsEventsMarker(a, PublicEventAttribute));
        var isObsolete = attributes.Any(a => IsEventsMarker(a, ObsoleteEventAttribute));

        return new AsyncApiEventModel
        {
            Name = symbol.Name,
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            IsPublic = isPublic,
            IsObsolete = isObsolete,
            Properties = CollectProperties(symbol)
        };
    }

    /// <summary>
    ///     True when the attribute is the Pragmatic.Events marker of that name. The interfaces above are
    ///     matched by full name; matching the attributes by simple name alone would let an unrelated
    ///     application attribute that happens to share the name rewrite the published contract.
    /// </summary>
    private static bool IsEventsMarker(AttributeData attribute, string name)
        => attribute.AttributeClass is { } cls
            && cls.Name == name
            && cls.ContainingNamespace?.ToDisplayString() == EventsNamespace;

    private static ImmutableArray<AsyncApiPropertyModel> CollectProperties(INamedTypeSymbol symbol)
    {
        var properties = ImmutableArray.CreateBuilder<AsyncApiPropertyModel>();
        // Deduplication is on the CLR name — a derived property SHADOWS its base by CLR identity, and
        // two distinct members could well share a JSON name after renaming.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var type = symbol; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.DeclaredAccessibility != Accessibility.Public || member.IsStatic || member.IsIndexer)
                    continue;
                if (!seen.Add(member.Name))
                    continue;
                properties.Add(new AsyncApiPropertyModel
                {
                    Name = JsonSchemaNaming.ToJsonName(member),
                    JsonType = MapJsonType(member.Type)
                });
            }
        }

        return properties.ToImmutable();
    }

    private static string MapJsonType(ITypeSymbol type)
    {
        var t = type is INamedTypeSymbol { Name: "Nullable", TypeArguments.Length: 1 } n ? n.TypeArguments[0] : type;
        return t.SpecialType switch
        {
            SpecialType.System_Boolean => "boolean",
            SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64
                or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 => "integer",
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => "number",
            SpecialType.System_String or SpecialType.System_Char => "string",
            _ => t.TypeKind == TypeKind.Enum ? "string"
                : t.Name is "Guid" or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan" ? "string"
                : "object"
        };
    }
}
