using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     FAWMN transforms that surface "wrong-shape" messaging diagnostics (PRAG0800/0803/0813).
///     They run alongside the model-generation transforms — which stay null-returning for invalid triggers —
///     so a mistake (e.g. <c>[MessageHandler]</c> on a class without <c>IMessageHandler&lt;T&gt;</c>) is
///     reported instead of silently producing nothing. Each returns <c>null</c> for a valid trigger.
///     ([EnableOutbox] is a boundary-level attribute — its PRAG0831 lives in the Persistence
///     DbContext feature, not here.)
/// </summary>
internal static class MessagingShapeDiagnosticTransform
{
    /// <summary>PRAG0800: a <c>[MessageHandler]</c> class must implement <c>IMessageHandler&lt;T&gt;</c>.</summary>
    public static MessagingDiagnosticInfo? HandlerShape(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var implemented = symbol.AllInterfaces.Any(i =>
            i is { IsGenericType: true, Name: "IMessageHandler", TypeArguments.Length: 1 }
            && i.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging");

        return implemented
            ? null
            : new MessagingDiagnosticInfo(
                MessagingDiagnosticKind.HandlerMustImplementInterface,
                LocationInfo.From(FirstLocation(symbol)),
                [symbol.Name]);
    }

    /// <summary>PRAG0803: a <c>[MessageMiddleware]</c> class must implement <c>IMessageMiddleware</c>.</summary>
    public static MessagingDiagnosticInfo? MiddlewareShape(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var implemented = symbol.AllInterfaces.Any(i =>
            i.Name == "IMessageMiddleware"
            && i.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging");

        return implemented
            ? null
            : new MessagingDiagnosticInfo(
                MessagingDiagnosticKind.MiddlewareMustImplementInterface,
                LocationInfo.From(FirstLocation(symbol)),
                [symbol.Name]);
    }

    /// <summary>PRAG0813: <c>[Saga&lt;T&gt;]</c> requires <c>T</c> to be an enum.</summary>
    public static MessagingDiagnosticInfo? SagaStateShape(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var sagaAttr = context.Attributes[0];
        if (sagaAttr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 })
            return null;

        var stateType = sagaAttr.AttributeClass.TypeArguments[0];
        return stateType.TypeKind == TypeKind.Enum
            ? null
            : new MessagingDiagnosticInfo(
                MessagingDiagnosticKind.SagaStateNotEnum,
                LocationInfo.From(FirstLocation(symbol)),
                [symbol.Name, stateType.Name]);
    }

    /// <summary>
    ///     PRAG0836: <c>[PublicEvent]</c> marks a domain event as published, and a type that is not a
    ///     domain event cannot be marked.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Reported from a pipeline of its own and not from <c>AsyncApiFeature</c>, which is the
    ///     consumer: that feature returns before it reads the attribute — a type that does not implement
    ///     <c>IDomainEvent</c> is dropped by its syntax pre-filter, which requires a base list — so a
    ///     diagnostic written there would die with the output it reports on. This one starts from the
    ///     attribute, which is the only thing the author actually wrote.
    /// </remarks>
    public static MessagingDiagnosticInfo? PublicEventShape(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // By full name: an application attribute or interface that happens to be called IDomainEvent is
        // not this one, and the marker's whole meaning is which contract the event belongs to.
        var isDomainEvent = symbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == "Pragmatic.Events.IDomainEvent");

        return isDomainEvent
            ? null
            : new MessagingDiagnosticInfo(
                MessagingDiagnosticKind.PublicEventOnNonDomainEvent,
                LocationInfo.From(FirstLocation(symbol)),
                [symbol.Name]);
    }

    private static Location? FirstLocation(ISymbol symbol)
        => symbol.Locations.Length > 0 ? symbol.Locations[0] : null;
}
