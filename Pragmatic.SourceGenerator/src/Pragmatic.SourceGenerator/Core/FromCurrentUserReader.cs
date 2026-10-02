using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Reads <c>[FromCurrentUser]</c> off a property: whether it is there, and which member of the user
///     entity it names.
/// </summary>
/// <remarks>
///     <para>
///         One reader, because three places ask: the query transform, which records the binding; the
///         query's filter, which matches a bound value exactly and always; and the endpoint transform,
///         which must not read the property from the request. A property one of them thought was bound
///         and another did not would be a value the invoker fills and the URL overwrites.
///     </para>
///     <para>
///         ⚠️ <b>The member usually does not bind.</b> <c>nameof(Employee.Id)</c> names a member the
///         entity generator writes into this same compilation, so while the transform runs the argument
///         is an error and carries no value. The name is then read from the syntax, which says it just as
///         plainly — and the qualifier is kept, so a <c>nameof</c> over another type is reported instead
///         of binding a member of the same name on the user entity.
///     </para>
/// </remarks>
internal static class FromCurrentUserReader
{
    /// <summary>Whether the property carries <c>[FromCurrentUser]</c>.</summary>
    public static bool IsBound(IPropertySymbol property) => Find(property) is not null;

    /// <summary>The attribute on the property, or null.</summary>
    public static AttributeData? Find(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == AttributeNames.FromCurrentUser)
                return attribute;

        return null;
    }

    /// <summary>
    ///     The member the attribute names, with the type its <c>nameof</c> was written over.
    /// </summary>
    /// <param name="attribute">The <c>[FromCurrentUser]</c> attribute.</param>
    /// <param name="semanticModel">
    ///     The transform's semantic model, to resolve the <c>nameof</c> qualifier. A property declared in
    ///     another file of a partial query is resolved through its own tree's model.
    /// </param>
    /// <param name="member">The member's name; null for the member-less form.</param>
    /// <param name="qualifier">
    ///     The fully qualified type the <c>nameof</c> names the member of, or null when the argument is a
    ///     string literal or the qualifier does not resolve.
    /// </param>
    /// <returns>False when an argument was written and no member name can be read from it.</returns>
    public static bool TryReadMember(
        AttributeData attribute, SemanticModel semanticModel, out string? member, out string? qualifier)
    {
        member = null;
        qualifier = null;

        var argument = (attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)
            ?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;

        if (argument is InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                ArgumentList.Arguments.Count: 1
            } nameOf)
        {
            var target = nameOf.ArgumentList.Arguments[0].Expression;
            member = target switch
            {
                MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
                IdentifierNameSyntax name => name.Identifier.ValueText,
                _ => null
            };

            if (target is MemberAccessExpressionSyntax { Expression: var owner })
            {
                var model = owner.SyntaxTree == semanticModel.SyntaxTree
                    ? semanticModel
                    : semanticModel.Compilation.GetSemanticModel(owner.SyntaxTree);
                qualifier = (model.GetSymbolInfo(owner).Symbol as INamedTypeSymbol)
                    ?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return member is not null;
        }

        if (attribute.ConstructorArguments.Length > 0
            && attribute.ConstructorArguments[0] is { Kind: TypedConstantKind.Primitive, Value: string literal })
        {
            member = literal;
            return true;
        }

        // No argument, or an explicit null: the caller's id.
        return argument is null || argument.IsKind(SyntaxKind.NullLiteralExpression);
    }
}
