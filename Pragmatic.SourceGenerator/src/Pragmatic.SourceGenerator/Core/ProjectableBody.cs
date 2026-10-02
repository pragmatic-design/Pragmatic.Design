using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The body of a <c>[Projectable]</c> member, rewritten as an expression over a given source: what
///     <c>Entity.Expr</c> publishes, and what a projection that reads the member writes in place of its
///     getter.
/// </summary>
/// <remarks>
///     <para>
///         A projection that reads the getter is not translated: EF Core loads the row and runs the
///         getter in memory, over navigations nobody loaded. A sum over the children comes back zero,
///         with no error. The body is what the database can compute, so the body is what
///         goes into the query.
///     </para>
///     <para>
///         One rewrite for both, so the published expression and the inlined one cannot differ.
///     </para>
/// </remarks>
internal static class ProjectableBody
{
    private const string ProjectableAttribute = "Pragmatic.Persistence.Query.Attributes.ProjectableAttribute";
    private const string ComputedFilterAttribute = "Pragmatic.Persistence.Query.Attributes.ComputedFilterAttribute";
    private const string ProjectableBodyAttribute = "Pragmatic.Persistence.Query.Attributes.ProjectableBodyAttribute";

    /// <summary>
    ///     The source a body is written over when the source is not known yet: the body the module
    ///     publishes for other assemblies, and the one a nested DTO's initializer fills in with whatever
    ///     reaches it. An identifier nothing else is called.
    /// </summary>
    public const string PortableSource = "__pragmatic_source__";

    /// <summary>Whether the property is <c>[Projectable]</c>, or <c>[ComputedFilter]</c>, which implies it.</summary>
    public static bool IsProjectable(IPropertySymbol property) =>
        property.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() is ProjectableAttribute or ComputedFilterAttribute);

    /// <summary>
    ///     The body of <paramref name="property" /> over <paramref name="source" />, or null when it is
    ///     not a projectable member with an expression body — read from its syntax in
    ///     <paramref name="compilation" />, or, for one declared in a referenced assembly, from the body
    ///     its module published on <c>Expr.{Member}</c>.
    /// </summary>
    public static string? Of(IPropertySymbol property, Compilation compilation, string source)
        => Of(property, compilation, source, new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default));

    /// <summary>
    ///     Rewrites <paramref name="body" />, written on <paramref name="entity" />, as an expression over
    ///     <paramref name="source" />: members of the entity are read from it, types are fully
    ///     qualified, and a projectable member it names is replaced by that member's own body.
    /// </summary>
    public static string Rewrite(ExpressionSyntax body, SemanticModel model, INamedTypeSymbol entity, string source)
        => Rewrite(body, model, entity, source, new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default));

    /// <summary>
    ///     The specifications <paramref name="body" /> passes to a query that take a value from the row
    ///     — which the query cannot evaluate before it runs (PRAG0735).
    /// </summary>
    public static IEnumerable<ExpressionSyntax> SpecificationsReadingTheRow(
        ExpressionSyntax body, SemanticModel model, INamedTypeSymbol entity)
    {
        var generatedMembers = new HashSet<string>(
            TraitPropertyResolver.GetGeneratedProperties(entity).Select(p => p.Name), StringComparer.Ordinal);

        return body.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => SpecificationCall.Of(invocation, model))
            .Where(call => call is not null && call.ReadsTheRow(model, entity, generatedMembers))
            .Select(call => call!.Specification);
    }

    private static string? Of(
        IPropertySymbol property, Compilation compilation, string source, HashSet<IPropertySymbol> expanding)
    {
        if (!IsProjectable(property))
            return null;

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax { ExpressionBody: { } body }
                || !compilation.ContainsSyntaxTree(reference.SyntaxTree))
                continue;

            if (!expanding.Add(property))
                return null;

            var rewritten = Rewrite(body.Expression, compilation.GetSemanticModel(reference.SyntaxTree),
                property.ContainingType, source, expanding);
            expanding.Remove(property);
            return rewritten;
        }

        return Published(property)?.Replace(PortableSource, source);
    }

    /// <summary>
    ///     The navigations the body of <paramref name="property" /> reads from its own entity — through
    ///     the projectable members it names too — by name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         What an in-memory mapping has to load: it reads the getter, and the getter walks these. A
    ///         navigation left out is an empty collection, and a sum over it is zero, with no error.
    ///         A collection of objects and a reference to an <c>[Entity]</c> are navigations;
    ///         a navigation a <c>[Relation]</c> generates is recognised by name, as the rewrite does.
    ///     </para>
    ///     <para>
    ///         ⚠️ Only for a member whose syntax is in this compilation. A module publishes the body of a
    ///         projectable member for other assemblies, not the navigations it reads.
    ///     </para>
    /// </remarks>
    public static IEnumerable<string> NavigationsOf(IPropertySymbol property, Compilation compilation)
        => NavigationsOf(property, compilation, new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    ///     The navigations the body of <paramref name="method" /> reads from its own entity, by name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Same question, asked of a method: an <c>[Invariant]</c> is a parameterless <c>bool</c>
    ///         method and not a projectable property, and what an operation must have loaded before that
    ///         rule can answer is exactly this list. An action that did not include one of them would
    ///         evaluate the rule against an empty collection — a real amount against nothing, refused.
    ///     </para>
    ///     <para>
    ///         ⚠️ Only a body whose syntax is in this compilation, like the property form: across
    ///         assemblies there is nothing to read, and the answer is an <b>empty</b> list. The caller
    ///         must treat that as "cannot tell" and not as "reads nothing" — the two differ by a false
    ///         refusal.
    ///     </para>
    /// </remarks>
    public static IEnumerable<string> NavigationsOf(IMethodSymbol method, Compilation compilation)
    {
        var bodies = new List<ExpressionSyntax>();

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (!compilation.ContainsSyntaxTree(reference.SyntaxTree))
                continue;

            // An expression-bodied rule is the ordinary shape; a block body's return expressions are
            // read too, because a rule written over two lines reads the same navigations.
            switch (reference.GetSyntax())
            {
                case MethodDeclarationSyntax { ExpressionBody: { } arrow }:
                    bodies.Add(arrow.Expression);
                    break;
                case MethodDeclarationSyntax { Body: { } block }:
                    bodies.AddRange(block.DescendantNodes().OfType<ReturnStatementSyntax>()
                        .Select(r => r.Expression).OfType<ExpressionSyntax>());
                    break;
            }
        }

        return bodies
            .SelectMany(body => NavigationsIn(
                body, method.ContainingType, compilation,
                new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default)))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> NavigationsOf(
        IPropertySymbol property, Compilation compilation, HashSet<IPropertySymbol> expanding)
    {
        if (!IsProjectable(property) || !expanding.Add(property))
            yield break;

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax { ExpressionBody: { } body }
                || !compilation.ContainsSyntaxTree(reference.SyntaxTree))
                continue;

            foreach (var navigation in NavigationsIn(
                         body.Expression, property.ContainingType, compilation, expanding))
                yield return navigation;
        }

        expanding.Remove(property);
    }

    /// <summary>
    ///     The navigations one expression reads from <paramref name="entity" />: the walk both entry
    ///     points share, so a property's answer and a method's cannot drift apart.
    /// </summary>
    private static IEnumerable<string> NavigationsIn(
        ExpressionSyntax body, INamedTypeSymbol entity, Compilation compilation,
        HashSet<IPropertySymbol> expanding)
    {
        var generatedNavigations = new HashSet<string>(
            TraitPropertyResolver.GetRelationNavigations(entity).Select(n => n.Name), StringComparer.Ordinal);

        {
            var model = compilation.GetSemanticModel(body.SyntaxTree);
            foreach (var name in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                // The entity's own members only: a name that stands alone, or follows `this.`. After any
                // other dot it belongs to whatever is on the left — `l.Amount` is the child's.
                if (name.Parent is MemberAccessExpressionSyntax access
                    && ReferenceEquals(access.Name, name)
                    && access.Expression is not ThisExpressionSyntax)
                    continue;
                if (name.Parent is NameColonSyntax or NameEqualsSyntax)
                    continue;

                var symbol = model.GetSymbolInfo(name).Symbol;
                if (symbol is IPropertySymbol member && IsMemberOf(entity, member.ContainingType))
                {
                    if (IsProjectable(member))
                    {
                        foreach (var nested in NavigationsOf(member, compilation, expanding))
                            yield return nested;
                    }
                    else if (IsNavigation(member.Type))
                    {
                        yield return member.Name;
                    }
                }
                else if (generatedNavigations.Contains(name.Identifier.ValueText)
                         && BindsToTheGeneratedMember(name, symbol, model))
                {
                    yield return name.Identifier.ValueText;
                }
            }
        }
    }

    /// <summary>
    ///     Whether a name the entity will have a generated member for means that member, given what it
    ///     bound to while the member did not exist yet.
    /// </summary>
    /// <remarks>
    ///     Nothing, or a namespace or a type of the same name — a module folder called like the
    ///     navigation, a navigation called like its type. In the entity's own code the member wins: C#
    ///     looks in the type before the namespaces around it. The one exception is C#'s own: through a
    ///     same-named type, a <b>static</b> member is the type's (Color Color), so that access stays the
    ///     type's here too.
    /// </remarks>
    internal static bool BindsToTheGeneratedMember(IdentifierNameSyntax name, ISymbol? symbol, SemanticModel model)
        => symbol switch
        {
            null or INamespaceSymbol => true,
            INamedTypeSymbol => !(name.Parent is MemberAccessExpressionSyntax access
                                  && ReferenceEquals(access.Expression, name)
                                  && model.GetSymbolInfo(access).Symbol is { IsStatic: true }),
            _ => false
        };

    internal static bool IsMemberOf(INamedTypeSymbol entity, INamedTypeSymbol? containingType)
    {
        for (var current = entity; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, containingType))
                return true;

        return false;
    }

    /// <summary>A collection of objects, or a reference to an <c>[Entity]</c>.</summary>
    private static bool IsNavigation(ITypeSymbol type)
    {
        if (type.IsValueType || type.SpecialType == SpecialType.System_String)
            return false;

        var enumerable = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } named
            ? named
            : type.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        if (enumerable is not null)
            return enumerable.TypeArguments[0] is { IsValueType: false, SpecialType: not SpecialType.System_String };

        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            if (current.GetAttributes().Any(a => a.AttributeClass?.Name == "EntityAttribute"))
                return true;

        return false;
    }

    /// <summary>
    ///     The body the member's own module published, over <see cref="PortableSource" />, or null.
    /// </summary>
    /// <remarks>
    ///     A member compiled into a referenced assembly has no syntax here, so the body cannot be read —
    ///     it has to be declared by the module that could: the module declares, the host composes. The persistence generator writes it
    ///     on the member's <c>Expr</c> property, already expanded through the projectable members it
    ///     names.
    /// </remarks>
    private static string? Published(IPropertySymbol property)
    {
        var expr = property.ContainingType.GetTypeMembers("Expr").FirstOrDefault();
        var published = expr?.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault();

        return published?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ProjectableBodyAttribute)
            ?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static string Rewrite(
        ExpressionSyntax body, SemanticModel model, INamedTypeSymbol entity, string source,
        HashSet<IPropertySymbol> expanding)
    {
        var generatedMembers = new HashSet<string>(
            TraitPropertyResolver.GetGeneratedProperties(entity).Select(p => p.Name), StringComparer.Ordinal);
        var rewriter = new MemberPrefixRewriter(model, entity, generatedMembers, source, expanding);
        return rewriter.Visit(body)?.ToString() ?? body.ToString();
    }

    /// <summary>
    ///     Rewrites identifier references to entity members as reads from the source.
    ///     Handles both implicit (SubTotal) and explicit (this.SubTotal) member access.
    /// </summary>
    /// <remarks>
    ///     A member a generator adds — a relation's navigation or foreign key, a trait's column — is not
    ///     in this semantic model, which is the compilation before generation: it binds to nothing. It
    ///     is recognised by name among the members the generators will add. Only a name that
    ///     stands alone, or follows <c>this.</c>, is the entity's: after any other dot it belongs to
    ///     whatever is on the left — <c>l.Id</c> in a lambda over the children is the child's.
    /// </remarks>
    private sealed class MemberPrefixRewriter(
        SemanticModel semanticModel,
        INamedTypeSymbol containingType,
        HashSet<string> generatedMembers,
        string source,
        HashSet<IPropertySymbol> expanding) : CSharpSyntaxRewriter
    {
        /// <summary>
        ///     A sequence filtered with a specification becomes the call a query translates: the
        ///     specification's expression, not its compiled delegate.
        /// </summary>
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (SpecificationCall.Of(node, semanticModel) is not { } call)
                return base.VisitInvocationExpression(node);

            var rewrittenSource = Visit(call.Source)!.WithoutTrivia().ToString();
            var rewrittenSpecification = Visit(call.Specification)!.WithoutTrivia().ToString();
            return SyntaxFactory.ParseExpression(
                    SpecificationCall.QueryForm(call.Method, rewrittenSource, rewrittenSpecification))
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            // Handle explicit this.Property → source.Property
            if (node.Expression is ThisExpressionSyntax)
            {
                var symbol = semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol is IPropertySymbol or IFieldSymbol && IsMemberOfEntityType(symbol.ContainingType))
                    return Read(node.Name.WithoutTrivia(), symbol).WithTriviaFrom(node);

                if (symbol is null && generatedMembers.Contains(node.Name.Identifier.ValueText))
                    return Read(node.Name.WithoutTrivia(), null).WithTriviaFrom(node);
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Skip: this is the Name part of this.Property (handled by VisitMemberAccessExpression)
            if (node.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax })
                return base.VisitIdentifierName(node);

            var symbol = semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is IPropertySymbol or IFieldSymbol && IsMemberOfEntityType(symbol.ContainingType))
                return Read(node.WithoutTrivia(), symbol).WithTriviaFrom(node);

            if (StandsAlone(node) && generatedMembers.Contains(node.Identifier.ValueText)
                && BindsToTheGeneratedMember(node, symbol, semanticModel))
                return Read(node.WithoutTrivia(), null).WithTriviaFrom(node);

            // Qualify type references (enums, static classes) with FQN to avoid namespace issues
            if (symbol is INamedTypeSymbol typeSymbol &&
                node.Parent is MemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Expression, node))
            {
                var fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return SyntaxFactory.IdentifierName(fqn).WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }

        /// <summary>
        ///     The member read from the source — or, for a projectable member, its body: the getter of a
        ///     computed member is no column, and neither EF Core nor the database can see into it.
        /// </summary>
        private ExpressionSyntax Read(SimpleNameSyntax name, ISymbol? member)
        {
            if (member is IPropertySymbol property
                && Of(property, semanticModel.Compilation, source, expanding) is { } inlined)
                return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ParseExpression(inlined));

            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ParseExpression(source), name);
        }

        /// <summary>
        ///     Not the name after a dot, nor the name of a named argument: an identifier that, if it is a
        ///     member at all, can only be one of the entity's.
        /// </summary>
        private static bool StandsAlone(IdentifierNameSyntax node) => node.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => !ReferenceEquals(memberAccess.Name, node),
            NameColonSyntax or NameEqualsSyntax => false,
            _ => true
        };

        /// <summary>
        ///     Checks whether a type is the containing entity type or one of its base types.
        ///     Handles inherited properties (e.g., CreatedAt from a base class).
        /// </summary>
        private bool IsMemberOfEntityType(INamedTypeSymbol? memberContainingType)
        {
            if (memberContainingType is null)
                return false;

            var current = containingType;
            while (current is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, memberContainingType))
                    return true;
                current = current.BaseType;
            }

            return false;
        }
    }
}
