using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Counts how many independently-committed stores one invocation touches.
/// </summary>
/// <remarks>
///     <para>
///         Each boundary owns a <c>DbContext</c> and its own <c>SaveChanges</c>. An action that writes
///         in its own boundary and calls another boundary's actions therefore produces two commits, the
///         inner one first — and nothing rolls it back if the outer step then fails. The same holds for
///         an action that writes nothing itself but calls two different boundaries.
///     </para>
///     <para>
///         So the signal is not "calls another boundary": that alone is atomic, one commit. It is
///         <b>more than one commit scope in one action</b>, which is what this counts.
///     </para>
///     <para>
///         It reads the calls, not the fields. A facade held and never used commits nothing, and — the
///         reason this matters — a step that declares its own undo is not an unguarded scope. Judging by
///         the field would demand a compensator on every operation of the callee boundary, including the
///         ones this caller never invokes, and the first real boundary fails that bar for a reason that
///         has nothing to do with the caller's risk.
///     </para>
///     <para>
///         Blind spot, and it is structural: a facade generated in the compilation being analysed is
///         not visible here, so two boundaries declared in the same assembly are not detected. Every
///         topology the framework produces puts a boundary in its own assembly — calling a facade means
///         referencing the assembly that carries it — but a same-assembly pair would slip through.
///     </para>
/// </remarks>
internal static class CommitScopeDetector
{
    private const string RepositoryInterface = "IRepository";
    private const string UnitOfWorkInterface = "IUnitOfWork";
    private const string AttributeNamespace = "Pragmatic.Actions.Attributes";

    /// <summary>
    ///     Returns the foreign boundary steps this type invokes without an undo, and whether it writes
    ///     into its own boundary's store.
    /// </summary>
    /// <param name="symbol">The action or mutation type.</param>
    /// <param name="declaration">Its declaration, whose body carries the calls.</param>
    /// <param name="semanticModel">Used to resolve the invoked methods.</param>
    /// <param name="ownBoundaryFullName">
    ///     The boundary it belongs to, fully qualified, or <c>null</c> when unresolved. A facade for
    ///     that boundary is its own store, not a second commit scope.
    /// </param>
    public static (ImmutableArray<string> UnguardedSteps, ImmutableArray<string> CompensatedSteps, bool WritesOwnStore) Detect(
        INamedTypeSymbol symbol,
        SyntaxNode? declaration,
        SemanticModel? semanticModel,
        string? ownBoundaryFullName)
    {
        var writesOwnStore = symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => !f.IsImplicitlyDeclared && !f.IsStatic && !f.IsConst
                      && f.Type is INamedTypeSymbol fieldType && IsOwnStoreWriter(fieldType));

        if (declaration is null || semanticModel is null)
            return (ImmutableArray<string>.Empty, ImmutableArray<string>.Empty, writesOwnStore);

        var steps = ImmutableArray.CreateBuilder<string>();
        var compensated = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (ResolveInvoked(semanticModel, invocation) is not { } method)
                continue;

            if (ForeignBoundaryOf(method.ContainingType, ownBoundaryFullName) is null)
                continue;

            // A step that undoes itself is guarded: the caller's failure runs its undo. Warning anyway
            // would push whoever did the work into [AcceptsPartialWrites], which would say something
            // false.
            var step = $"{method.ContainingType.Name}.{method.Name}";
            if (!seen.Add(step))
                continue;

            // A step that undoes itself is guarded, and the guard is what PRAG0424 asked for — so it
            // stops asking. It does not stop being a compensation without a log, which is PRAG0429's
            // subject: the two say different things about the same call, and both are true.
            if (HasAttribute(method, "CompensableStepAttribute"))
                compensated.Add(step);
            else
                steps.Add(step);
        }

        return (steps.ToImmutable(), compensated.ToImmutable(), writesOwnStore);
    }

    /// <summary>
    ///     The method an invocation names, even when overload resolution could not finish.
    /// </summary>
    /// <remarks>
    ///     The reason this is not just <c>GetSymbolInfo().Symbol</c>: an argument may itself be a member
    ///     this run is about to generate. <c>_knowledge.Items.IngestText(item.Id, …)</c> is the real
    ///     case — <c>Id</c> comes from the entity traits the same generator emits, so at transform time
    ///     the argument does not exist, resolution fails, and <c>Symbol</c> is null. The candidates are
    ///     still the <c>IngestText</c> overloads, which is all this needs: it asks which boundary the
    ///     call reaches, not which overload wins.
    ///
    ///     Missing this made the diagnostic silent on the one action it was built for, while firing
    ///     correctly on a probe two lines away whose arguments happened to be ordinary expressions.
    /// </remarks>
    private static IMethodSymbol? ResolveInvoked(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        var info = semanticModel.GetSymbolInfo(invocation);

        if (info.Symbol is IMethodSymbol resolved)
            return resolved;

        // All candidates of a failed resolution are overloads of the same method on the same type, so
        // the first one answers the only question asked here.
        return info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    /// <summary>Whether the decision to leave the inner writes committed has been recorded.</summary>
    public static bool AcceptsPartialWrites(INamedTypeSymbol symbol)
        => HasAttribute(symbol, "AcceptsPartialWritesAttribute");

    /// <summary>Whether the body was declared to be one transaction.</summary>
    public static bool IsTransactional(INamedTypeSymbol symbol)
        => HasAttribute(symbol, "TransactionalAttribute");

    /// <summary>
    ///     Whether the body invokes other actions or mutations through their invokers.
    /// </summary>
    /// <remarks>
    ///     Composition is the point — small actions, assembled — and it is also the moment the commit
    ///     stops being obvious: atomic with the steps visible to each other, one commit without that
    ///     visibility, or independent steps. Three outcomes, and an action that says nothing takes one
    ///     of them silently. This is what PRAG0428 asks about.
    /// </remarks>
    public static bool ComposesInvocations(
        SyntaxNode? declaration, SemanticModel? semanticModel, string? ownBoundaryFullName)
    {
        if (declaration is null || semanticModel is null)
            return false;

        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (ResolveInvoked(semanticModel, invocation) is not { } method)
                continue;

            // Through the boundary's own facade — the surface the generator emits for exactly this, and
            // for a long time the only form of composition the diagnostic could not see. Leaving it out
            // had it backwards: the idiomatic way went unasked while the raw invoker was caught.
            if (IsOwnBoundaryFacade(method.ContainingType, ownBoundaryFullName))
                return true;

            if (method.Name != "InvokeAsync")
                continue;

            var container = method.ContainingType?.OriginalDefinition;
            if (container?.ContainingNamespace?.ToDisplayString() != "Pragmatic.Actions.Invoker")
                continue;

            if (container.Name is "IDomainActionInvoker" or "IVoidDomainActionInvoker" or "IMutationInvoker")
                return true;
        }

        return false;
    }

    /// <summary>Whether the type declared how its composed steps commit.</summary>
    /// <summary>
    ///     Whether the call goes through a facade of the caller's <b>own</b> boundary.
    /// </summary>
    /// <remarks>
    ///     <c>ForeignBoundaryOf</c> cannot answer this: it returns <c>null</c> both for "not a facade"
    ///     and for "my own facade", because for its purpose — a write crossing a boundary — the two are
    ///     the same non-event. Here they are opposites.
    /// </remarks>
    private static bool IsOwnBoundaryFacade(INamedTypeSymbol? containingType, string? ownBoundaryFullName)
    {
        if (containingType is null || ownBoundaryFullName is null
            || containingType.TypeKind != TypeKind.Interface)
            return false;

        foreach (var attr in containingType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null
                || attrClass.OriginalDefinition.Name != "BoundaryActionsAttribute"
                || attrClass.OriginalDefinition.ContainingNamespace?.ToDisplayString() != AttributeNamespace
                || attrClass.TypeArguments.Length == 0)
                continue;

            var boundary = attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.Equals(boundary, ownBoundaryFullName, StringComparison.Ordinal);
        }

        return false;
    }

    public static bool DeclaresCommitStrategy(INamedTypeSymbol symbol)
        => IsTransactional(symbol) || CommitMode(symbol) is not null;

    /// <summary>
    ///     The <c>[CommitStrategy]</c> declared on the type, as the enum member name, or <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     The name rather than the numeric value: the generated invoker reads better, and a member
    ///     renamed in the enum stops compiling instead of silently meaning something else.
    /// </remarks>
    public static string? CommitMode(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass?.Name != "CommitStrategyAttribute"
                || attrClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
                continue;

            if (attr.ConstructorArguments.Length == 0)
                continue;

            var value = attr.ConstructorArguments[0];
            if (value.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType || value.Value is null)
                continue;

            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue && Equals(member.ConstantValue, value.Value))
                    return member.Name;
            }
        }

        return null;
    }

    /// <summary>
    ///     The commit mode the type inherits from its boundary, when it declares none of its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A boundary <b>is</b> a transaction boundary, so "how work in this boundary commits" is a
    ///         sentence the boundary can say — and saying it once beats repeating it on every action
    ///         that composes. The action still wins where it disagrees: the nearer declaration is the
    ///         one the reader of that file can see.
    ///     </para>
    ///     <para>
    ///         The boundary, not the module. In every module of both reference applications there is
    ///         exactly one boundary, so a per-module default would be the same statement said in a
    ///         vaguer place — and where a module ever holds two, they can sit on different databases,
    ///         which is precisely when one policy for both would be wrong.
    ///     </para>
    /// </remarks>
    public static string? InheritedCommitMode(
        string? boundaryFullName, SemanticModel? semanticModel)
    {
        if (boundaryFullName is null || semanticModel is null)
            return null;

        var metadataName = boundaryFullName.StartsWith("global::", StringComparison.Ordinal)
            ? boundaryFullName.Substring("global::".Length)
            : boundaryFullName;

        var boundary = semanticModel.Compilation.GetTypeByMetadataName(metadataName);
        return boundary is null ? null : CommitMode(boundary);
    }

    /// <summary>
    ///     Whether the boundary declares <c>[Transactional]</c>, which it cannot deliver (PRAG0431).
    /// </summary>
    public static bool BoundaryDeclaresTransactional(
        string? boundaryFullName, SemanticModel? semanticModel)
    {
        if (boundaryFullName is null || semanticModel is null)
            return false;

        var metadataName = boundaryFullName.StartsWith("global::", StringComparison.Ordinal)
            ? boundaryFullName.Substring("global::".Length)
            : boundaryFullName;

        var boundary = semanticModel.Compilation.GetTypeByMetadataName(metadataName);
        return boundary is not null && IsTransactional(boundary);
    }

    /// <summary>
    ///     A read repository is not a writer, which is why the name is matched exactly: <c>IRepository</c>
    ///     carries <c>Add</c>/<c>Remove</c>/<c>Update</c>, <c>IReadRepository</c> does not.
    /// </summary>
    private static bool IsOwnStoreWriter(INamedTypeSymbol fieldType)
    {
        var originalDef = fieldType.OriginalDefinition;
        if (originalDef.Name is not (RepositoryInterface or UnitOfWorkInterface))
            return false;

        var ns = originalDef.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns.StartsWith("Pragmatic.Persistence", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Returns the boundary behind a generated facade, or <c>null</c> when the type is not a facade
    ///     or belongs to the caller's own boundary.
    /// </summary>
    private static string? ForeignBoundaryOf(INamedTypeSymbol? containingType, string? ownBoundaryFullName)
    {
        if (containingType is null || containingType.TypeKind != TypeKind.Interface)
            return null;

        foreach (var attr in containingType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null
                || attrClass.OriginalDefinition.Name != "BoundaryActionsAttribute"
                || attrClass.OriginalDefinition.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
                continue;

            if (attrClass.TypeArguments.Length == 0)
                continue;

            var boundary = attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.Equals(boundary, ownBoundaryFullName, StringComparison.Ordinal) ? null : boundary;
        }

        return null;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass?.Name == attributeName
                && attrClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace)
                return true;
        }

        return false;
    }
}
