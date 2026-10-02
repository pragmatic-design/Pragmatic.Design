using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Renders the public and internal boundary interfaces with their method signatures.
/// </summary>
internal sealed partial class BoundaryInterfaceTemplate
{
    // =========================================================================
    // Public Interface
    // =========================================================================

    private void RenderPublicInterface()
    {
        var shortName = StripBoundarySuffix(_boundary.TypeName);
        XmlSummary($"Groups all public actions belonging to the {shortName} boundary.");
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

        // The marker that makes a cross-boundary call recognisable to the analyzer. Matching the name
        // shape instead would be a convention the compiler cannot check, and one any hand-written
        // interface could imitate.
        AppendLine($"[global::Pragmatic.Actions.Attributes.BoundaryActions<{_boundary.FullTypeName}>]");
        AppendLine($"public interface {_boundary.InterfaceName}");
        Block(() =>
        {
            // Sub-boundary property accessors
            if (_hasSubBoundaries)
            {
                foreach (var sub in _boundary.SubBoundaries)
                {
                    XmlSummary($"Access to {sub.Name} actions.");
                    AppendLine($"{sub.InterfaceName} {sub.PropertyName} {{ get; }}");
                    AppendLine();
                }
            }

            // Boundary-level methods
            var sorted = _publicMembers.OrderBy(m => m.TypeName).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                RenderInterfaceMethod(sorted[i]);
                if (i < sorted.Count - 1)
                    AppendLine();
            }
        });
    }

    // =========================================================================
    // Internal Interface
    // =========================================================================

    private void RenderInternalInterface()
    {
        var shortName = StripBoundarySuffix(_boundary.TypeName);

        if (_boundary.IsInternal)
        {
            // Standalone internal interface — no public base
            XmlSummary($"Internal-only actions for the {shortName} boundary.");
            AppendLine($"internal interface {_boundary.InternalInterfaceName}");
            Block(() =>
            {
                // Include ALL members (public + internal) since there's no public interface
                var allMembers = _publicMembers.AddRange(_internalMembers)
                    .OrderBy(m => m.TypeName).ToList();

                // Sub-boundary property accessors
                if (_hasSubBoundaries)
                {
                    foreach (var sub in _boundary.SubBoundaries)
                    {
                        XmlSummary($"Access to {sub.Name} actions.");
                        AppendLine($"{sub.InterfaceName} {sub.PropertyName} {{ get; }}");
                        AppendLine();
                    }
                }

                for (var i = 0; i < allMembers.Count; i++)
                {
                    RenderInterfaceMethod(allMembers[i], internalInterface: true);
                    if (i < allMembers.Count - 1)
                        AppendLine();
                }
            });
        }
        else
        {
            // Internal interface extends public interface
            XmlSummary(
                $"Internal actions for the {shortName} boundary. Extends <see cref=\"{_boundary.InterfaceName}\"/>. "
                + "Reachable only from inside this module, and calls through it run as internal calls: the "
                + "caller is an operation of this same boundary, already authorized for what it is doing. "
                + "LOCAL ONLY: it declares the preloaded shapes, which take a tracked entity, and a tracked "
                + "entity does not cross a process -- so a host that composes this boundary with "
                + "BoundaryMode.Remote registers the public interface and not this one. Code that injects "
                + "this interface is code of this module, and runs in the process that hosts the module.");
            AppendLine($"internal interface {_boundary.InternalInterfaceName} : {_boundary.InterfaceName}");
            Block(() =>
            {
                // The groups, retyped to their internal twins. `new` because the public interface this
                // one extends already declares the property with the public type: a caller inside the
                // module reaches the twin, one outside reaches the public one, and the object behind
                // both is the same.
                if (_hasSubBoundaries)
                {
                    foreach (var sub in _boundary.SubBoundaries)
                    {
                        if (!sub.HasInternalTwin)
                            continue;

                        XmlSummary($"Access to {sub.Name} actions, including the shapes that stay in this process.");
                        AppendLine($"new {sub.InternalInterfaceName} {sub.PropertyName} {{ get; }}");
                        AppendLine();
                    }
                }

                var sorted = _internalMembers.OrderBy(m => m.TypeName).ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    RenderInterfaceMethod(sorted[i], internalInterface: true);
                    if (i < sorted.Count - 1)
                        AppendLine();
                }

                // The preloaded shape for the operations the PUBLIC interface declares.
                //
                // ⚠️ Redeclared here rather than inherited, because it is not on the public interface
                // at all: that one is implemented over HTTP as well, and a tracked entity does not
                // cross a process boundary. An interface may add an overload of a method it inherits,
                // so an operation composing inside the module sees both shapes on this one seam.
                RenderPreloadedOverloadsFor(_publicMembers, precededByBlankLine: sorted.Count > 0);
            });
        }
    }

    // =========================================================================
    // Interface Method Rendering
    // =========================================================================

    /// <summary>
    ///     Tells a caller in another boundary that this step undoes itself, so PRAG0424 can be precise
    ///     about the steps it actually invokes rather than about the whole facade.
    /// </summary>
    private void RenderCompensableStep(BoundaryMemberModel member)
    {
        if (member.IsCompensable)
            AppendLine("[global::Pragmatic.Actions.Attributes.CompensableStep]");
    }

    /// <summary>
    ///     The shape that takes a row the caller already holds — internal interface only.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>IMutationInvoker</c> publishes this overload, and this is the boundary's equivalent:
    ///         without it an operation that has already read the entity could not reach its mutation
    ///         through the boundary without paying for a second <c>SELECT</c> of the row it is
    ///         holding. That cost is the whole argument for writing to the repository by hand, which
    ///         loses the permission, the validation and the register entry with it.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Not on the public interface.</b> That one is implemented twice — locally, and over
    ///         HTTP for a boundary an application marks remote — and a tracked entity does not cross a
    ///         process boundary. Publishing it there would force a remote method whose only behaviour
    ///         is to throw.
    ///     </para>
    /// </remarks>
    private bool PublishesPreloaded(BoundaryMemberModel member, bool internalInterface)
        => internalInterface && member.IsMutation && !member.MutationCreates
            && member.EntityFullTypeName is not null;

    /// <summary>Emits only the preloaded overloads, for members declared on another interface.</summary>
    private void RenderPreloadedOverloadsFor(
        System.Collections.Immutable.ImmutableArray<BoundaryMemberModel> members, bool precededByBlankLine)
    {
        var first = !precededByBlankLine;

        foreach (var member in members.OrderBy(m => m.TypeName))
        {
            if (!PublishesPreloaded(member, internalInterface: true))
                continue;

            if (!first)
                AppendLine();

            first = false;
            RenderPreloadedOverload(member);
        }
    }

    private void RenderPreloadedOverload(BoundaryMemberModel member)
    {
        var methodName = DeriveMethodName(member);
        var returnType = GetMethodReturnType(member);
        var paramName = MemberParamName(member);
        var crefName = StripGlobalPrefix(member.FullTypeName);

        XmlSummary(
            $"Invokes <see cref=\"{crefName}\"/> against a row the caller already holds, skipping the load.");
        RenderCompensableStep(member);
        AppendLine(
            $"{returnType} {methodName}({member.FullTypeName} {paramName}, {member.EntityFullTypeName} entity, "
            + "global::System.Threading.CancellationToken ct = default);");
    }

    private void RenderInterfaceMethod(BoundaryMemberModel member, bool internalInterface = false)
    {
        var methodName = DeriveMethodName(member);
        var returnType = GetMethodReturnType(member);
        var paramName = MemberParamName(member);
        var crefName = StripGlobalPrefix(member.FullTypeName);

        // DTO overload — always generated
        XmlSummary($"Invokes <see cref=\"{crefName}\"/>.");
        RenderCompensableStep(member);
        AppendLine(
            $"{returnType} {methodName}({member.FullTypeName} {paramName}, global::System.Threading.CancellationToken ct = default);");

        if (PublishesPreloaded(member, internalInterface))
        {
            AppendLine();
            RenderPreloadedOverload(member);
        }

        // Unwrapped overload — only for DomainActions with input properties
        if (member.HasInputProperties)
        {
            AppendLine();
            XmlSummary($"Invokes <see cref=\"{crefName}\"/> with unwrapped parameters.");
            RenderCompensableStep(member);
            var paramList = BuildUnwrappedParams(member);
            AppendLine($"{returnType} {methodName}({paramList});");
        }
    }

    private static string BuildUnwrappedParams(BoundaryMemberModel member)
    {
        var ordered = OrderProperties(member.InputProperties.AsImmutableArray());
        var parts = ordered
            .Select(p =>
            {
                var typeName = GetParamTypeName(p);
                var paramStr = $"{typeName} {TemplateHelpers.ToCamelCase(p.Name)}";
                var defaultValue = GetDefaultValueForParam(p);
                return defaultValue is not null ? $"{paramStr} = {defaultValue}" : paramStr;
            })
            .ToList();
        parts.Add("global::System.Threading.CancellationToken ct = default");
        return string.Join(", ", parts);
    }
}
