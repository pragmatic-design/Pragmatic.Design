using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Reads a list of permission values out of the source that declares it — a role's
///     <c>DefaultPermissions</c>, or a list marked <c>[PermissionSet]</c>.
/// </summary>
/// <remarks>
///     Shared by <see cref="PermissionTransform" />, which catalogues what a role grants, and
///     <see cref="PermissionSetTransform" />, which publishes a list so another assembly can read it: the
///     shapes a list may be written in are one answer, not two.
/// </remarks>
internal static class PermissionListReader
{
    /// <summary>
    ///     The semantic model for <paramref name="node" />: the context's own when the node is in the
    ///     same tree (the common case, and free), otherwise one obtained from the compilation — a
    ///     partial declaration can put the property in another file.
    /// </summary>
    internal static SemanticModel ModelFor(SemanticModel contextModel, SyntaxNode node)
        => node.SyntaxTree == contextModel.SyntaxTree
            ? contextModel
            : contextModel.Compilation.GetSemanticModel(node.SyntaxTree);

    /// <summary>
    ///     The strings a list-valued expression names, following a reference to wherever the list
    ///     actually lives.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Not only a collection expression or a <c>new[] { … }</c> written in place. Recognising
    ///         only those, a role whose <c>DefaultPermissions</c> reads <c>=&gt; Granted</c> — a
    ///         <c>private static readonly string[]</c> beside it — would come out of the catalogue as an
    ///         empty grant, while the runtime, which reads the property, grants every entry it names.
    ///         What is applied and what is catalogued would diverge, and the screen listing what a role
    ///         grants would state a falsehood. Nothing would fail: the runtime is right, so no request is
    ///         refused that should be allowed, and nobody compares the two.
    ///     </para>
    ///     <para>
    ///         <c>null</c> means "this is not a shape I can read", which is what lets the caller keep
    ///         looking; an empty array means "this list is empty", and the two must not be confused —
    ///         a role that grants nothing is a legitimate declaration.
    ///     </para>
    /// </remarks>
    internal static RoleListValues? ListValuesOf(
        ExpressionSyntax? expr, SemanticModel contextModel, int depth)
    {
        // A reference chain has no business being long, and a cycle would not compile — but the guard
        // costs one comparison and a generator that hangs takes the whole build with it.
        if (expr is null || depth > 4)
            return null;

        // Written in place: => ["a", Perms.Read] or => new[] { "a", Perms.Read }
        if (expr is CollectionExpressionSyntax collectionExpr)
            return ReadCollection(collectionExpr, contextModel, depth);

        if (expr is ImplicitArrayCreationExpressionSyntax implicitArray)
            return ReadList(implicitArray.Initializer.Expressions, contextModel);

        if (expr is ArrayCreationExpressionSyntax { Initializer: { } arrayInit })
            return ReadList(arrayInit.Expressions, contextModel);

        if (expr is ObjectCreationExpressionSyntax { Initializer: { } objectInit })
            return ReadList(objectInit.Expressions, contextModel);

        // Held elsewhere: => Granted, or => SomeType.Granted. Follow it to its own initializer.
        var symbol = ModelFor(contextModel, expr).GetSymbolInfo(expr).Symbol;

        foreach (var syntaxRef in Declarations(symbol, contextModel.Compilation))
        {
            var initializer = syntaxRef.GetSyntax() switch
            {
                VariableDeclaratorSyntax { Initializer.Value: { } value } => value,
                PropertyDeclarationSyntax declared => GetValueExpression(declared),
                _ => null
            };

            var values = ListValuesOf(initializer, contextModel, depth + 1);
            if (values is not null)
                return values;
        }

        return PublishedValues(symbol);
    }

    /// <summary>
    ///     The values another assembly states for this list, from the <c>[assembly: PermissionSetValues]</c>
    ///     its generator wrote for a <c>[PermissionSet]</c> member.
    /// </summary>
    /// <remarks>
    ///     A referenced assembly is metadata: the list has no initializer to follow, and a
    ///     <c>static readonly string[]</c> has no constant value either — so the values have to be stated.
    ///     The module declares, the compilation that reads the list composes.
    /// </remarks>
    private static RoleListValues? PublishedValues(ISymbol? symbol)
    {
        if (symbol is not (IFieldSymbol or IPropertySymbol) || symbol.ContainingAssembly is null)
            return null;

        var member = symbol.ToDisplayString();

        foreach (var attribute in symbol.ContainingAssembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != PermissionSetValuesAttribute
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[0].Value as string != member)
                continue;

            var values = attribute.ConstructorArguments[1].Values
                .Select(value => value.Value as string)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToImmutableArray();

            return RoleListValues.Empty with { Resolved = new EquatableArray<string>(values) };
        }

        return null;
    }

    /// <summary>The attribute an assembly carries for each list it publishes.</summary>
    internal const string PermissionSetValuesAttribute = "Pragmatic.Authorization.PermissionSetValuesAttribute";

    /// <summary>The declaring syntax of a field or property in this compilation, and of nothing else.</summary>
    /// <remarks>
    ///     A list held in another project is metadata to the build, with no syntax to follow. An IDE
    ///     hands the generator that project as a compilation, whose symbols keep their syntax in a tree
    ///     that is not this compilation's: binding it throws and discards the generator's whole output.
    ///     Only what the build can read is read.
    /// </remarks>
    private static ImmutableArray<SyntaxReference> Declarations(ISymbol? symbol, Compilation compilation)
        => symbol is IFieldSymbol or IPropertySymbol
            ? symbol.DeclaringSyntaxReferences
                .Where(reference => compilation.ContainsSyntaxTree(reference.SyntaxTree))
                .ToImmutableArray()
            : ImmutableArray<SyntaxReference>.Empty;

    /// <summary>The expression body of the property, or of its first accessor.</summary>
    internal static ExpressionSyntax? GetValueExpression(PropertyDeclarationSyntax propSyntax)
        => propSyntax.ExpressionBody?.Expression
           ?? propSyntax.AccessorList?.Accessors.FirstOrDefault()?.ExpressionBody?.Expression;

    /// <summary>
    ///     The constant string value of each expression that has one. Same reasoning as
    ///     the reading of a role's Name: a const reference belongs in the list too.
    /// </summary>
    /// <summary>
    ///     The strings the expressions name: the ones this compilation can fold, and the paths it
    ///     cannot.
    /// </summary>
    /// <remarks>
    ///     A name this run cannot bind is not a mistake — it is usually a constant the same run
    ///     generates, and a transform sees one compilation. Dropping it silently is what made a role's
    ///     catalogued grant smaller than the one it applies.
    /// </remarks>
    /// <summary>
    ///     A collection expression, element by element — a spread included.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A spread is how a hand-written role inherits — <c>[.. EmployeeRole.DefaultPermissions, …]</c> — and
    ///     it is not an element: skipped in silence, it would make the catalogue list a manager as granting
    ///     only what it adds while the runtime grants the employee's too. A spread whose list lives
    ///     in this compilation is followed like any other held-elsewhere list; one naming a <c>[Role]</c> class,
    ///     whose list the generator writes in this same run, is carried to where those lists are resolved; any
    ///     other is reported (PRAG1013) rather than dropped.
    /// </remarks>
    private static RoleListValues ReadCollection(
        CollectionExpressionSyntax collection, SemanticModel contextModel, int depth)
    {
        var resolved = ImmutableArray.CreateBuilder<string>();
        var unresolved = ImmutableArray.CreateBuilder<string>();
        var spreadRoles = ImmutableArray.CreateBuilder<string>();
        var unreadable = ImmutableArray.CreateBuilder<string>();

        // In the order written: the catalogue lists what the declaration names, as it names it.
        foreach (var element in collection.Elements)
        {
            if (element is ExpressionElementSyntax single)
            {
                var own = ReadList([single.Expression], contextModel);
                resolved.AddRange(own.Resolved.AsImmutableArray());
                unresolved.AddRange(own.Unresolved.AsImmutableArray());
                continue;
            }

            if (element is not SpreadElementSyntax spread)
                continue;

            if (ListValuesOf(spread.Expression, contextModel, depth + 1) is { } inner)
            {
                resolved.AddRange(inner.Resolved.AsImmutableArray());
                unresolved.AddRange(inner.Unresolved.AsImmutableArray());
                spreadRoles.AddRange(inner.SpreadRoles.AsImmutableArray());
                unreadable.AddRange(inner.UnreadableSpreads.AsImmutableArray());
            }
            else if (DeclaredRoleSpread(spread.Expression, contextModel) is { } role)
            {
                spreadRoles.Add(role);
            }
            else
            {
                unreadable.Add(spread.Expression.ToString());
            }
        }

        return new RoleListValues(resolved.ToImmutable(), unresolved.ToImmutable(),
            spreadRoles.ToImmutable(), unreadable.ToImmutable(), EquatableArray<string>.Empty);
    }

    /// <summary>
    ///     <c>X.DefaultPermissions</c> where <c>X</c> is a <c>[Role]</c> class of this compilation: its property is
    ///     generated in this run, so it has no syntax here — the role's full name stands for it.
    /// </summary>
    internal static string? DeclaredRoleSpread(ExpressionSyntax expression, SemanticModel contextModel)
    {
        if (expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "DefaultPermissions" } access)
            return null;

        return ModelFor(contextModel, access).GetSymbolInfo(access.Expression).Symbol is INamedTypeSymbol type
               && type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == DeclaredRoleTransform.RoleAttribute)
            ? type.ToDisplayString()
            : null;
    }

    private static RoleListValues ReadList(
        IEnumerable<ExpressionSyntax> expressions, SemanticModel contextModel)
    {
        var resolved = ImmutableArray.CreateBuilder<string>();
        var unresolved = ImmutableArray.CreateBuilder<string>();

        foreach (var expr in expressions)
        {
            if (ModelFor(contextModel, expr).GetConstantValue(expr) is { HasValue: true, Value: string value })
            {
                resolved.Add(value);
                continue;
            }

            // A literal that is not a string, or an expression naming nothing, is not a permission
            // path: only a name is worth carrying forward for the catalogue to answer.
            if (expr is IdentifierNameSyntax or MemberAccessExpressionSyntax)
                unresolved.Add(expr.ToString());
        }

        return new RoleListValues(
            new EquatableArray<string>(resolved.ToImmutable()),
            new EquatableArray<string>(unresolved.ToImmutable()),
            EquatableArray<string>.Empty,
            EquatableArray<string>.Empty,
            EquatableArray<string>.Empty);
    }
}
