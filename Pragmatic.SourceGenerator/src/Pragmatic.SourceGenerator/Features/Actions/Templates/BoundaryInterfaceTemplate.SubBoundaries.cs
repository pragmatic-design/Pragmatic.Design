using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Renders sub-boundary interfaces and their local implementations.
/// </summary>
internal sealed partial class BoundaryInterfaceTemplate
{
    // =========================================================================
    // Sub-boundary public interface
    // =========================================================================

    private void RenderSubPublicInterface(SubBoundaryModel sub)
    {
        var shortName = StripBoundarySuffix(_boundary.TypeName);

        // What the author wrote in [SubBoundary(Description = …)], when they wrote one.
        XmlSummary(sub.Description is { Length: > 0 } description
            ? description
            : $"Groups actions belonging to the {sub.Name} sub-boundary of {shortName}.");
        XmlRemarks(
            "A call through this interface is NOT an internal call: the permission declared on the "
            + "operation being invoked is enforced, the caller's own permission having authorized "
            + "the caller and not the callee. This is the cross-boundary contract -- what another "
            + "module injects -- and the operation's permission is exactly what guards that. "
            + "Where an operation answers for what it invokes, it declares [AbsorbsChildPermissions]; "
            + "a caller with no principal at all, such as a message handler or a job, has to say the "
            + "same thing, by declaring it or by entering ICallContext.EnterInternalCall() itself. "
            + "Inside the module, I{Boundary}InternalActions is the one that runs as an internal call. "
            + "On the request edge inject IMutationInvoker or IDomainActionInvoker for the one "
            + "operation needed, which is what a generated endpoint does; PRAG0441 reports the mistake.");

        // The sub-boundary interface carries the marker too. It is the one a caller actually invokes —
        // `_knowledge.Items.IngestText(...)` resolves to a method on this type, not on the root facade —
        // so leaving it off made PRAG0424 blind to every call through a sub-boundary.
        AppendLine($"[global::Pragmatic.Actions.Attributes.BoundaryActions<{_boundary.FullTypeName}>]");
        AppendLine($"public interface {sub.InterfaceName}");
        Block(() =>
        {
            var sorted = sub.PublicMembers.OrderBy(m => m.TypeName).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                RenderInterfaceMethod(sorted[i]);
                if (i < sorted.Count - 1)
                    AppendLine();
            }
        });
    }

    // =========================================================================
    // Sub-boundary local implementation
    // =========================================================================

    /// <summary>
    ///     The sub-boundary's internal twin: the shapes that take a row the caller already holds.
    /// </summary>
    /// <remarks>
    ///     Redeclared rather than added to the public interface, because that one is implemented over
    ///     HTTP as well and a tracked entity does not cross a process boundary. An operation inside the
    ///     module reaches this through the root's internal interface, so one path carries both shapes:
    ///     <c>Candidates.Settle(mutation, ct)</c> and <c>Candidates.Settle(mutation, entity, ct)</c>.
    /// </remarks>
    private void RenderSubInternalInterface(SubBoundaryModel sub)
    {
        XmlSummary(
            $"Internal actions of the {sub.Name} sub-boundary. Extends <see cref=\"{sub.InterfaceName}\"/> "
            + "with the shapes that only make sense in this process.");

        AppendLine($"internal interface {sub.InternalInterfaceName} : {sub.InterfaceName}");
        Block(() =>
        {
            // The group's own internal operations first, in full. Put flat onto the root's internal
            // interface, they would make the path a caller writes depend on the operation's
            // visibility rather than on its folder.
            var sorted = sub.InternalMembers.OrderBy(m => m.TypeName).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                RenderInterfaceMethod(sorted[i], internalInterface: true);
                if (i < sorted.Count - 1)
                    AppendLine();
            }

            // Then the preloaded shapes of the members the PUBLIC interface declares.
            RenderPreloadedOverloadsFor(sub.PublicMembers.AsImmutableArray(),
                precededByBlankLine: sorted.Count > 0);
        });
    }

    private void RenderSubLocalImplementation(SubBoundaryModel sub)
    {
        XmlSummary(
            $"Local in-process implementation of <see cref=\"{sub.InterfaceName}\"/> for callers inside this module. Delegates to invokers as an internal call.");

        Class(sub.ImplementationName, () => RenderSubLocalImplBody(sub, guarded: false),
            interfaces: new List<string>
            {
                sub.HasInternalTwin ? sub.InternalInterfaceName : sub.InterfaceName
            },
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    /// <summary>
    ///     The guarded twin of a sub-boundary implementation — the one the public sub-interface resolves
    ///     to, so a call from another module has the invoked operation's permission asked.
    /// </summary>
    /// <remarks>
    ///     A sub-boundary has one interface that can be injected, the public one, so the intra/cross
    ///     distinction cannot be drawn by which interface is injected: it is drawn by which instance the
    ///     container hands out. The root that answers the internal interface holds the unguarded twins;
    ///     the root behind the public interface holds these. The group's internal interface, when there
    ///     is one, is not a second way in: it is never registered, and only the root's internal
    ///     interface returns it.
    /// </remarks>
    private void RenderSubGuardedImplementation(SubBoundaryModel sub)
    {
        XmlSummary(
            $"Local in-process implementation of <see cref=\"{sub.InterfaceName}\"/> for callers outside this module. The invoked operation's own permission is enforced.");

        Class(GuardedImplementationName(sub.ImplementationName), () => RenderSubLocalImplBody(sub, guarded: true),
            interfaces: new List<string> { sub.InterfaceName },
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderSubLocalImplBody(SubBoundaryModel sub, bool guarded)
    {
        // ⚠️ The guarded twin implements the PUBLIC interface only, so it gets the public members and
        // nothing else. The unguarded one answers the internal interface, which is where the group's
        // own internal operations live.
        var members = (guarded ? sub.PublicMembers.AsImmutableArray()
                : sub.PublicMembers.AsImmutableArray().AddRange(sub.InternalMembers.AsImmutableArray()))
            .OrderBy(m => m.TypeName).ToList();

        // Fields. No invoker is injected — see the root implementation for why: a facade that takes its
        // operations' invokers cannot be built by an operation that is one of them.
        Field("_services", "global::System.IServiceProvider", AccessModifier.Private, isReadOnly: true);

        // Field — call context for internal call scoping, on the twin that enters one (see the root).
        if (!guarded)
            Field("_callContext", "global::Pragmatic.Pipeline.ICallContext?", AccessModifier.Private, isReadOnly: true);

        AppendLine();

        // Constructor — the provider and the optional call context
        var ctorParams = new List<MethodParameter>
        {
            new("global::System.IServiceProvider", "services"),
        };

        if (!guarded)
            ctorParams.Add(new MethodParameter("global::Pragmatic.Pipeline.ICallContext?", "callContext") { DefaultValue = "null" });

        Constructor(guarded ? GuardedImplementationName(sub.ImplementationName) : sub.ImplementationName, () =>
        {
            AppendLine("_services = services;");

            if (!guarded)
                AppendLine("_callContext = callContext;");
        }, ctorParams, AccessModifier.Public);

        // Methods — the same rendering as the root, with the same one-line difference
        foreach (var member in members)
            RenderLocalImplMethod(member, entersInternalCall: !guarded);
    }
}
