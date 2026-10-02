using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

internal static partial class EndpointTransform
{
    private const string HttpStatusAttribute = "Pragmatic.Endpoints.Attributes.HttpStatusAttribute";

    /// <summary>The HTTP status an endpoint documents for one of its error types.</summary>
    /// <param name="errorType">The error type the operation declares.</param>
    /// <param name="compiled">
    ///     The assembly being compiled — the operation's. Only its declarations are read as syntax.
    /// </param>
    /// <remarks>
    ///     <para>
    ///         First a <c>StatusCode</c> literal declared in this compilation, walking up the error's
    ///         bases; then the first base, the error included, whose name is a well-known HTTP error.
    ///         <c>record WorksiteClosed : BusinessRuleError</c> is 422 because its base is: a fallback
    ///         that looked at the error's own name only would document every such rule as 400.
    ///     </para>
    ///     <para>
    ///         ⚠️ A base in a referenced project is read by name, never by syntax, even where its syntax
    ///         is at hand. In an IDE a referenced project is a compilation and the syntax is there; in the
    ///         build it is a DLL and it is not. Reading it when present made the editor and the build
    ///         write two contracts from the same code. A status a referenced base declares
    ///         under a name of its own is not visible to the build at all, so it is not used anywhere.
    ///     </para>
    /// </remarks>
    private static int GetErrorStatusCode(ITypeSymbol errorType, IAssemblySymbol compiled)
    {
        // Declared wins, and is read first: an attribute is metadata, so it is the one form the build can
        // read wherever the type lives. The syntax walk below sees only this compilation.
        if (DeclaredStatusCode(errorType) is { } declared)
            return declared;

        var resolved = TryResolveStatusCodeFromSyntax(errorType, compiled);
        if (resolved.HasValue)
            return resolved.Value;

        for (var current = errorType; current is not null; current = current.BaseType)
        {
            var known = current.Name switch
            {
                "NotFoundError" => 404,
                "BadRequestError" => 400,
                "UnauthorizedError" => 401,
                "ForbiddenError" => 403,
                "ConflictError" => 409,
                "ValidationError" => 422,
                "BusinessRuleError" => 422,
                "InternalServerError" => 500,
                "DependencyError" => 502,
                _ => (int?)null
            };

            if (known.HasValue)
                return known.Value;
        }

        return 400;
    }

    /// <summary>The status an error type declares with <c>[HttpStatus(n)]</c>, its bases included.</summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The attribute is <c>Inherited = false</c>, and the bases are walked here on purpose: a
    ///         family of errors states its status once, on the base, and a type that derives from it answers
    ///         with the same one. Reflection's inheritance would also have to be turned on for the runtime
    ///         to agree, which is why it is spelled out rather than left to <c>Inherited</c>.
    ///     </para>
    ///     <para>
    ///         This is the only form that survives an assembly boundary: a property value is not metadata,
    ///         an attribute argument is.
    ///     </para>
    /// </remarks>
    private static int? DeclaredStatusCode(ITypeSymbol errorType)
    {
        for (var current = errorType; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != HttpStatusAttribute)
                    continue;

                // A list pattern would need System.Index, which netstandard2.0 does not have.
                if (attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is int status)
                    return status;
            }
        }

        return null;
    }

    /// <summary>The status the type itself answers with, when this compilation can read it.</summary>
    /// <remarks>
    ///     Read beside <see cref="DeclaredStatusCode" /> to tell whether the two disagree: the document
    ///     would then say one thing and the response say another, and a reader has no way to know which
    ///     (PRAG0537).
    /// </remarks>
    internal static int? ContradictedStatusCode(ITypeSymbol errorType, IAssemblySymbol compiled)
    {
        if (DeclaredStatusCode(errorType) is not { } declared)
            return null;

        var own = TryResolveStatusCodeFromSyntax(errorType, compiled);
        return own.HasValue && own.Value != declared ? own.Value : null;
    }

    /// <summary>
    ///     Walks the error type hierarchy looking for a StatusCode property with a compile-time
    ///     literal value (expression-body "=> 404" or initializer "= 404"), declared in
    ///     <paramref name="compiled" />. Returns null if no constant can be resolved.
    /// </summary>
    private static int? TryResolveStatusCodeFromSyntax(ITypeSymbol errorType, IAssemblySymbol compiled)
    {
        var current = errorType;
        while (current is not null)
        {
            var prop = SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compiled)
                ? current.GetMembers("StatusCode")
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(p => !p.IsAbstract && p.DeclaringSyntaxReferences.Length > 0)
                : null;

            if (prop is not null)
            {
                foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
                {
                    var node = syntaxRef.GetSyntax();

                    // Case 1: expression-body property  "public override int StatusCode => 404;"
                    if (node is PropertyDeclarationSyntax { ExpressionBody.Expression: LiteralExpressionSyntax
                        {
                            Token.Value: int value1
                        }
                        })
                        return value1;

                    // Case 2: auto-property with initializer  "public override int StatusCode { get; } = 404;"
                    if (node is PropertyDeclarationSyntax { Initializer.Value: LiteralExpressionSyntax { Token.Value: int value2 } })
                        return value2;
                }
            }

            current = current.BaseType;
        }

        return null;
    }
}
