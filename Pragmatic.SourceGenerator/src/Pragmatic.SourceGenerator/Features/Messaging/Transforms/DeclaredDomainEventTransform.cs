using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     The domain events <b>declared in this compilation</b> — what an entity here can raise, and
///     therefore what this assembly's outbox can carry.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Why a publisher needs this at all.</b> A message-type registry derived from
///         <c>[MessageHandler]</c>s alone would give an assembly that only <em>publishes</em> none — and
///         <c>OutboxDeliveryService</c> resolves every row through the registered registries and
///         dead-letters what none of them recognises. Such a service could not read the rows it wrote
///         itself, and would drain its own outbox into the dead-letter store with no handler having run.
///         An application that publishes and consumes in one process cannot show it, because there the
///         handlers' registry happens to cover the published types.
///     </para>
///     <para>
///         The set is the declared events and nothing wider: a referenced assembly's events belong to
///         <b>its</b> registry, which it generates for itself, and every registry is registered as
///         one more entry of an enumerable the pump tries in turn. Widening this to "every event
///         visible in the compilation" would make two assemblies claim the same type and put the
///         deserialization of a contract in whoever happened to reference it.
///     </para>
///     <para>
///         The syntax pre-filter is the same one <c>AsyncApiFeature</c> uses and for the same reason: a
///         domain event necessarily implements <c>IDomainEvent</c>, directly or through a base, so a
///         declaration with no base list can never match and the semantic model is never asked about
///         the vast majority of types on an edit.
///     </para>
/// </remarks>
internal static class DeclaredDomainEventTransform
{
    private const string DomainEventInterface = "Pragmatic.Events.IDomainEvent";

    /// <summary>The predicate: a class or record that declares a base list.</summary>
    public static bool CouldBeAnEvent(SyntaxNode node, CancellationToken _)
        => node is ClassDeclarationSyntax { BaseList: not null }
            or RecordDeclarationSyntax { BaseList: not null };

    /// <summary>
    ///     The event's message-type model, or null when the declaration is not a domain event. Abstract
    ///     types are skipped: the outbox carries an instance, and the registry deserializes into the
    ///     concrete type the row names.
    /// </summary>
    public static MessageTypeModel? Transform(GeneratorSyntaxContext context, CancellationToken _)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol { IsAbstract: false } symbol)
            return null;

        var isEvent = false;
        foreach (var @interface in symbol.AllInterfaces)
        {
            if (@interface.ToDisplayString() != DomainEventInterface)
                continue;

            isEvent = true;
            break;
        }

        if (!isEvent)
            return null;

        return new MessageTypeModel
        {
            // The FQN without a global:: prefix: the registry template adds one where the type is
            // named and strips it for the case label, which has to match Type.FullName as the outbox
            // row stored it.
            Fqn = symbol.ToDisplayString(),
            ShortName = symbol.Name,
            AssemblyName = symbol.ContainingAssembly?.Name ?? "",
        };
    }
}
