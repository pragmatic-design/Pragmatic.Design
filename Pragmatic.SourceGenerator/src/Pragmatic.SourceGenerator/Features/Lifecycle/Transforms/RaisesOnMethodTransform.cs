using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Lifecycle.Models;

namespace Pragmatic.SourceGenerator.Features.Lifecycle.Transforms;

/// <summary>
///     Reads <c>[Raises&lt;TEvent&gt;]</c> off a <b>method</b> and records whether the declaring type is an
///     entity — the one place the declaration reads as wired, because the class-level form on an entity
///     is. <c>LifecycleEventsFeature</c> turns that into PRAG2753.
/// </summary>
/// <remarks>
///     On anything that is not an entity the member-level declaration is left alone on purpose: that is
///     what the host's event graph reads to attribute a raise to its origin (<c>PRAG0816</c> prints
///     <c>RecallDrugAction.Execute()</c>), and an operation raises through its own invoker. The fact is
///     reported rather than acted on here, so the decision and the diagnostic stay in one place.
/// </remarks>
internal static class RaisesOnMethodTransform
{
    public static RaisesOnMethodModel Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        var method = context.TargetSymbol as IMethodSymbol;

        var events = ImmutableArray.CreateBuilder<string>();
        foreach (var attr in context.Attributes)
            if (attr.AttributeClass is { TypeArguments.Length: 1 } ac &&
                ac.TypeArguments[0] is INamedTypeSymbol eventType)
                events.Add(eventType.Name);

        return new RaisesOnMethodModel
        {
            Origin = method is null ? "" : $"{method.ContainingType?.Name}.{method.Name}()",
            DeclaredOnAnEntity = method?.ContainingType is { } declaringType && IsEntity(declaringType),
            EventNames = events.ToImmutable(),
            LocationInfo = LocationInfo.From(method?.Locations.FirstOrDefault())
        };
    }

    /// <summary>
    ///     An entity: it carries <c>[Entity]</c>, or it derives from <c>DomainEventSource</c> (the base the
    ///     class-level declaration needs). Both are matched on the containing namespace too — a homonym in
    ///     the application would otherwise decide whether a diagnostic fires.
    /// </summary>
    private static bool IsEntity(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
            if (attr.AttributeClass?.OriginalDefinition is { Name: "EntityAttribute" } entityAttribute &&
                entityAttribute.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity")
                return true;

        for (var t = type.BaseType; t is not null; t = t.BaseType)
            if (t.Name == "DomainEventSource" && t.ContainingNamespace?.ToDisplayString() == "Pragmatic.Events")
                return true;

        return false;
    }
}
