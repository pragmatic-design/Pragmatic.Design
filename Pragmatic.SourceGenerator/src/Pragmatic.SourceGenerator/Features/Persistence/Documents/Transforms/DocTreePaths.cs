using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     Where each part of a lambda's source is in the expression tree the compiler builds for it, as code over <c>Captured</c> (the Sql engine's
///     runtime reader, linked by the documents bridge): the body, then a binary's left or right, an operator's operand, a call's target or
///     argument, a nested lambda's body. A specification's captured value is read at runtime by that path (<c>Captured.Evaluate</c>), exactly the
///     node, no compilation of the tree.
/// </summary>
internal static class DocTreePaths
{
    private const string Captured = "global::Pragmatic.Persistence.Documents.Pragmatic.Captured";

    /// <summary>The paths of <paramref name="lambda" />'s body and its parts, from <c>Captured.Body(<paramref name="variable" />)</c>.</summary>
    public static Dictionary<SyntaxNode, string> Of(LambdaExpressionSyntax lambda, SemanticModel model, string variable)
    {
        var paths = new Dictionary<SyntaxNode, string>();
        if (lambda.Body is ExpressionSyntax body)
            Walk(body, $"{Captured}.Body({variable})", model, paths);
        return paths;
    }

    private static void Walk(ExpressionSyntax node, string path, SemanticModel model, Dictionary<SyntaxNode, string> paths)
    {
        paths[node] = path;
        switch (node)
        {
            case ParenthesizedExpressionSyntax paren:
                Walk(paren.Expression, path, model, paths);
                break;
            case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                Walk(postfix.Operand, path, model, paths); // the '!' is the compiler's only: no node in the tree
                break;
            case BinaryExpressionSyntax binary:
                Walk(binary.Left, $"{Captured}.Left({path})", model, paths);
                Walk(binary.Right, $"{Captured}.Right({path})", model, paths);
                break;
            case PrefixUnaryExpressionSyntax unary:
                Walk(unary.Operand, $"{Captured}.Operand({path})", model, paths);
                break;
            case CastExpressionSyntax cast:
                Walk(cast.Expression, $"{Captured}.Operand({path})", model, paths);
                break;
            case InvocationExpressionSyntax invocation when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method:
            {
                // An extension called as a method is a static call whose receiver is the first argument; an instance call has a target.
                var offset = 0;
                if (invocation.Expression is MemberAccessExpressionSyntax access)
                {
                    if (method.ReducedFrom is not null)
                    {
                        Walk(access.Expression, $"{Captured}.Argument({path}, 0)", model, paths);
                        offset = 1;
                    }
                    else if (!method.IsStatic)
                        Walk(access.Expression, $"{Captured}.Target({path})", model, paths);
                }

                var arguments = invocation.ArgumentList.Arguments;
                for (var i = 0; i < arguments.Count; i++)
                {
                    var argumentPath = $"{Captured}.Argument({path}, {i + offset})";
                    if (arguments[i].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax lambdaBody })
                        Walk(lambdaBody, $"{Captured}.Lambda({argumentPath})", model, paths);
                    else
                        Walk(arguments[i].Expression, argumentPath, model, paths);
                }

                break;
            }
        }
    }
}
