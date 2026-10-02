using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     Answers one question about the module being compiled: does <b>every</b> boundary it declares
///     carry <c>[EnableOutbox]</c>, and if so, which one to name.
/// </summary>
/// <remarks>
///     <para>
///         It exists for <c>PRAG0837</c>: an <c>[EventHandler]</c> of an outbox boundary is registered
///         and never entered, because the outbox interceptor takes the entity's events during the save.
///         The handler is in this compilation and so is its boundary, which is why this reads the
///         module's own assembly rather than the referenced ones.
///     </para>
///     <para>
///         ⚠️ <b>Every boundary, or nothing.</b> A module that declares two boundaries — one with the
///         outbox and one without — has handlers this cannot attribute: which boundary raises the event
///         is the entity's business, and that mapping lives in another feature's models. Reporting
///         anyway would fire on a handler that runs perfectly well, and a diagnostic that fires on a
///         working shape is worse than none (the lesson of <c>PRAG0836</c>'s controls). So the ambiguous
///         module gets silence, and the unambiguous one — every application in this repository — gets
///         the warning.
///     </para>
/// </remarks>
internal static class OutboxBoundaryReader
{
    private const string BoundaryAttribute = "Pragmatic.Actions.Attributes.BoundaryAttribute";
    private const string OutboxAttribute = "Pragmatic.Messaging.Attributes.EnableOutboxAttribute";

    /// <summary>
    ///     The boundary whose events leave through the outbox, or <see langword="null" /> when this
    ///     module declares no boundary, none with the outbox, or one of each.
    /// </summary>
    public static string? TheBoundaryThatSendsItsEventsToTheOutbox(
        Compilation compilation, CancellationToken ct)
    {
        string? withOutbox = null;

        foreach (var type in TypesOf(compilation.Assembly.GlobalNamespace, ct))
        {
            if (!Carries(type, BoundaryAttribute))
                continue;

            if (!Carries(type, OutboxAttribute))
                return null;

            withOutbox ??= type.Name;
        }

        return withOutbox;
    }

    private static bool Carries(ISymbol symbol, string attributeFullName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeFullName);

    private static IEnumerable<INamedTypeSymbol> TypesOf(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();
            yield return type;
        }

        foreach (var nested in ns.GetNamespaceMembers())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var type in TypesOf(nested, ct))
                yield return type;
        }
    }
}
