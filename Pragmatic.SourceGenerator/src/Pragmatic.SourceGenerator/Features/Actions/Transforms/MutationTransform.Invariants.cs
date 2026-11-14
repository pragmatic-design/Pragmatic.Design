using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class MutationTransform
{
    private const string InvariantAttributeName = "InvariantAttribute";
    private const string InvariantAttributeNamespace = "Pragmatic.Persistence.Entity";

    /// <summary>
    ///     Collects the aggregate invariants of <paramref name="entityType"/>: parameterless instance
    ///     methods returning <see cref="bool"/> marked <c>[Invariant]</c>, walking the inheritance chain
    ///     so derived entities also enforce base-declared invariants. The generated invoker calls each
    ///     after apply and before persist.
    /// </summary>
    /// <param name="entityType">The entity whose rules these are — the aggregate, or a child of it.</param>
    /// <param name="consumer">
    ///     The assembly the invoker will be generated into, when it may differ from the entity's. An
    ///     <c>internal</c> method of another assembly cannot be called from there, and a generated call
    ///     to one is a <c>CS0122</c> in a file the author cannot edit — the shape a child of an
    ///     aggregate on the other side of a boundary has.
    /// </param>
    /// <param name="compilation">
    ///     The compilation, when the caller needs to know <b>what each rule reads</b>: an operation that
    ///     only loaded a row can answer a rule over a navigation it included, and not one over a
    ///     navigation it left out. Null leaves the navigations empty and
    ///     <c>BodyWasReadable</c> false, which is what a caller that does not care passes.
    /// </param>
    internal static ImmutableArray<InvariantModel> ParseInvariants(
        INamedTypeSymbol entityType, IAssemblySymbol? consumer = null, Compilation? compilation = null)
    {
        var builder = ImmutableArray.CreateBuilder<InvariantModel>();
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        for (var current = entityType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                // Must be callable from the generated invoker (another class in the same assembly):
                // private/protected methods would not compile, so only public/internal qualify.
                if (method.IsStatic
                    || method.Parameters.Length != 0
                    || method.ReturnType.SpecialType != SpecialType.System_Boolean
                    || method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
                    || !seen.Add(method.Name))
                    continue;

                if (method.DeclaredAccessibility == Accessibility.Internal
                    && consumer is not null
                    && !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, consumer))
                    continue;

                var attr = method.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.Name == InvariantAttributeName &&
                    a.AttributeClass.ContainingNamespace?.ToDisplayString() == InvariantAttributeNamespace);
                if (attr is null)
                    continue;

                var message = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as string
                    : attr.NamedArguments.FirstOrDefault(n => n.Key == "Message").Value.Value as string;

                var readable = compilation is not null
                               && method.DeclaringSyntaxReferences.Any(r => compilation.ContainsSyntaxTree(r.SyntaxTree));

                var member = NamedString(attr, "Member");

                builder.Add(new InvariantModel
                {
                    MethodName = method.Name,
                    Message = message,
                    MessageKey = NamedString(attr, "MessageKey"),
                    ErrorCode = NamedString(attr, "ErrorCode"),
                    Member = member is not null && WhyItCannotBeRead(entityType, member, consumer, read: false) is null
                        ? member
                        : null,
                    ParameterMembers = NamedStrings(attr, "Parameters")
                        .Where(p => WhyItCannotBeRead(entityType, p, consumer, read: true) is null)
                        .ToImmutableArray(),
                    Navigations = readable
                        ? Core.ProjectableBody.NavigationsOf(method, compilation!).ToImmutableArray()
                        : ImmutableArray<string>.Empty,
                    BodyWasReadable = readable
                });
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The methods of <paramref name="entityType" /> that carry <c>[Invariant]</c> and that the
    ///     generated invoker <b>cannot call</b>, each with the reason — PRAG0463.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The attribute is read <b>first</b> here, which is the inversion this exists for.
    ///         <see cref="ParseInvariants" /> filters on shape before it reads the attribute — correctly,
    ///         since a generated call to a private method is a CS0122 — so a method that asked to be a
    ///         rule and failed any condition was indistinguishable from one nobody had annotated. Five
    ///         conditions, five ways for a rule to read as enforced and never fire.
    ///     </para>
    ///     <para>
    ///         Kept separate from <see cref="ParseInvariants" /> rather than folded into it: that
    ///         function returns the models the invoker is generated from, is called from four places,
    ///         and its output shape is what the generated check depends on. A diagnostic pass that walks
    ///         the same members twice costs a walk over one type's methods and cannot change what is
    ///         generated.
    ///     </para>
    ///     <para>
    ///         The <b>cross-assembly internal</b> exit of <see cref="ParseInvariants" /> is deliberately
    ///         not reported here: an <c>internal</c> rule of another assembly is uncallable from this
    ///         invoker and the entity's own assembly enforces it on its own writes, so it is a rule
    ///         somebody keeps — unlike these five, which nobody does.
    ///     </para>
    /// </remarks>
    internal static ImmutableArray<UncallableInvariantModel> UncallableInvariants(INamedTypeSymbol entityType)
    {
        var builder = ImmutableArray.CreateBuilder<UncallableInvariantModel>();
        var callable = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        // The names ParseInvariants will have taken, in the same order it walks them, so "a rule with
        // this name is already enforced" means the same thing in both.
        for (var current = entityType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (!HasInvariantAttribute(method))
                    continue;

                var reason = WhyItCannotBeCalled(method, callable);
                if (reason is null)
                {
                    callable.Add(method.Name);
                    continue;
                }

                builder.Add(new UncallableInvariantModel
                {
                    MethodName = method.Name,
                    Reason = reason,
                    Location = Core.LocationInfo.From(
                        method.Locations.Length > 0 ? method.Locations[0] : null),
                });
            }
        }

        return builder.ToImmutable();
    }

    private static bool HasInvariantAttribute(IMethodSymbol method)
        => method.GetAttributes().Any(a =>
            a.AttributeClass?.Name == InvariantAttributeName &&
            a.AttributeClass.ContainingNamespace?.ToDisplayString() == InvariantAttributeNamespace);

    /// <summary>
    ///     Why the generated invoker cannot call this rule, or null when it can. The order matches
    ///     <see cref="ParseInvariants" />'s filter, and every branch names the condition in the words an
    ///     author would use about their own method.
    /// </summary>
    private static string? WhyItCannotBeCalled(
        IMethodSymbol method, System.Collections.Generic.ICollection<string> alreadyCallable)
    {
        if (method.IsStatic)
            return "it is static, and an invariant is asked of an instance";

        if (method.Parameters.Length != 0)
            return $"it takes {method.Parameters.Length} parameter(s), and the invoker has nothing to pass";

        if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
            return $"it returns {method.ReturnType.Name} and not bool, so there is no yes or no to check";

        if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            return $"it is {method.DeclaredAccessibility.ToString().ToLowerInvariant()}, and the invoker "
                   + "is another class: it needs at least internal";
        }

        if (alreadyCallable.Contains(method.Name))
            return "a rule of this entity with the same name is already enforced, and only the first is called";

        return null;
    }

    /// <summary>
    ///     The names an <c>[Invariant]</c> of <paramref name="entityType" /> gives as its <c>Member</c> or
    ///     <c>Parameters</c> that do not name a member the refusal can report — PRAG0471.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A string, even one written with <c>nameof</c>, is checked by nobody else: <c>nameof</c> accepts
    ///     a private property the generated invoker cannot read, and a literal accepts a name that does not
    ///     exist. <see cref="ParseInvariants" /> leaves those out of what it generates, so without this the
    ///     message's placeholder would stay unfilled, or the field go unmarked, in silence.
    /// </remarks>
    internal static ImmutableArray<UnreadableInvariantMemberModel> UnreadableInvariantMembers(INamedTypeSymbol entityType)
    {
        var builder = ImmutableArray.CreateBuilder<UnreadableInvariantMemberModel>();

        for (var current = entityType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                var attr = method.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.Name == InvariantAttributeName &&
                    a.AttributeClass.ContainingNamespace?.ToDisplayString() == InvariantAttributeNamespace);
                if (attr is null)
                    continue;

                var named = NamedString(attr, "Member") is { } member
                    ? new[] { (Name: member, Read: false) }
                    : [];

                foreach (var (name, read) in named.Concat(NamedStrings(attr, "Parameters").Select(p => (p, true))))
                {
                    if (WhyItCannotBeRead(entityType, name, consumer: null, read) is not { } reason)
                        continue;

                    builder.Add(new UnreadableInvariantMemberModel
                    {
                        MethodName = method.Name,
                        MemberName = name,
                        Reason = reason,
                        Location = Core.LocationInfo.From(method.Locations.Length > 0 ? method.Locations[0] : null),
                    });
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Why <paramref name="name" /> cannot be what an invariant reports, or null when it can: it must be
    ///     an instance property or field of the entity, and — when <paramref name="read" />, because its
    ///     value goes into the message — one the generated invoker can read.
    /// </summary>
    private static string? WhyItCannotBeRead(INamedTypeSymbol entityType, string name, IAssemblySymbol? consumer, bool read)
    {
        for (var current = entityType; current is not null; current = current.BaseType)
        {
            var symbol = current.GetMembers(name).FirstOrDefault(m => m is IPropertySymbol or IFieldSymbol);
            if (symbol is null)
                continue;

            if (symbol.IsStatic)
                return "it is static, and the rule is about a row";

            if (!read)
                return null;

            var accessibility = symbol is IPropertySymbol property
                ? property.GetMethod?.DeclaredAccessibility ?? Accessibility.NotApplicable
                : symbol.DeclaredAccessibility;

            if (accessibility is not (Accessibility.Public or Accessibility.Internal))
                return "the invoker is another class and cannot read it: it needs a getter at least internal";

            if (accessibility == Accessibility.Internal
                && consumer is not null
                && !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, consumer))
                return "it is internal to another assembly than the invoker's";

            return null;
        }

        return "the entity has no property or field of that name";
    }

    private static string? NamedString(AttributeData attr, string name)
        => attr.NamedArguments.FirstOrDefault(n => n.Key == name).Value.Value as string;

    private static System.Collections.Generic.IEnumerable<string> NamedStrings(AttributeData attr, string name)
    {
        var argument = attr.NamedArguments.FirstOrDefault(n => n.Key == name).Value;
        return argument.Kind == TypedConstantKind.Array
            ? argument.Values.Select(v => v.Value as string).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!)
            : [];
    }

    private const string TemporalRelationAttributeName = "TemporalRelationAttribute";

    /// <summary>
    ///     True when the entity has <c>[TemporalRelation]</c> with a constraint for which the generator
    ///     emits <c>ValidateTemporalConstraints</c> (MaxActive &gt; 0, or overlap disallowed). Mirrors the
    ///     TemporalValidationTemplate gate; the generated invoker then enforces it in the pipeline.
    /// </summary>
    private static bool HasTemporalConstraint(INamedTypeSymbol entityType)
    {
        foreach (var attr in entityType.GetAttributes())
        {
            var attributeClass = attr.AttributeClass;
            if (attributeClass is null)
                continue;

            var isTemporal =
                attributeClass.OriginalDefinition.Name == TemporalRelationAttributeName &&
                attributeClass.ContainingNamespace?.ToDisplayString() == InvariantAttributeNamespace;
            if (!isTemporal)
                continue;

            var maxActive = 0;
            var allowOverlap = false;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "MaxActive", Value.Value: int max })
                    maxActive = max;
                else if (namedArg is { Key: "AllowOverlap", Value.Value: bool overlap })
                    allowOverlap = overlap;
            }

            return maxActive > 0 || !allowOverlap;
        }

        return false;
    }
}
