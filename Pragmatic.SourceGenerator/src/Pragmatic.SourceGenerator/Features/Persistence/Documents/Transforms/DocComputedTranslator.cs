using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A value an update computes from the document's own fields (<c>o =&gt; o.Price * 1.1m</c>, <c>o =&gt; o.Name + "!"</c>) into a
///     <see cref="DocComputed" />: the document's number and text fields, constants, <c>+ - * / %</c> and a unary minus, as an aggregation
///     expression (<c>$add</c>, <c>$subtract</c>, <c>$multiply</c>, <c>$divide</c>, <c>$mod</c>, <c>$concat</c>) and as C# over the document. An
///     integer division truncates as C#'s does (<c>$trunc</c>), a text field that is null concatenates as empty, as C# does. Anything else is
///     PRAG3101: a value the caller holds goes in the value form.
/// </summary>
internal sealed class DocComputedTranslator(SemanticModel model, DocModel entity, Func<INamedTypeSymbol, DocModel?> models, IParameterSymbol document, List<Diagnostic> diagnostics)
{
    private const string Subset = "a computed value uses the document's number and text fields, constants, + - * / % and a unary minus; a value the caller holds goes in the value form, Set(o => o.Field, value)";
    private readonly DocTranslator _paths = new(model, entity, models, document, null, null, diagnostics, Location.None);

    /// <summary>The value <paramref name="body" /> computes into <paramref name="field" />; null when it was reported.</summary>
    public DocComputed? Translate(ExpressionSyntax body, DocField field)
    {
        string? converter;
        switch (field.Kind)
        {
            case DocKind.Int32:
                converter = "$toInt";
                break;
            case DocKind.Int64:
                converter = "$toLong";
                break;
            case DocKind.Double:
                converter = "$toDouble";
                break;
            case DocKind.Decimal:
                converter = "$toDecimal";
                break;
            case DocKind.String:
                converter = null;
                break;
            default:
                Report(body, $"a computed value is written into a number or a text field, not into {field.Property.Name} ({field.Kind})");
                return null;
        }

        var write = Expression(body);
        if (write is null)
            return null;
        return new DocComputed(write, converter, $"({field.Property.Type.ToDisplayString(DocModel.Qualified)})({Code(body)})");
    }

    /// <summary>The statements writing <paramref name="expression" /> under the element their argument names; null when it is outside the subset.</summary>
    private Func<string, string>? Expression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax paren:
                return Expression(paren.Expression);
            case CastExpressionSyntax cast when IsNumber(model.GetTypeInfo(cast).Type):
                return Expression(cast.Expression); // a widening between numbers: the server's arithmetic widens on its own
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } minus:
            {
                var operand = Expression(minus.Operand);
                return operand is null ? null : slot => $"w.StartDocument({slot}); w.StartArray(\"$multiply\"); {operand("0")}w.Write(1, -1); w.EndArray(); w.EndDocument(); ";
            }

            case BinaryExpressionSyntax binary when Operator(binary) is { } op:
            {
                var left = Expression(binary.Left);
                var right = Expression(binary.Right);
                if (left is null || right is null)
                    return null;
                if (op == "$concat")
                    return slot => $"w.StartDocument({slot}); w.StartArray(\"$concat\"); {Text(left, "0")}{Text(right, "1")}w.EndArray(); w.EndDocument(); ";
                Func<string, string> arithmetic = slot => $"w.StartDocument({slot}); w.StartArray({DocTypeEmitter.Literal(op)}); {left("0")}{right("1")}w.EndArray(); w.EndDocument(); ";
                if (op != "$divide" || !IsInteger(model.GetTypeInfo(binary).Type))
                    return arithmetic;
                return slot => $"w.StartDocument({slot}); {arithmetic("\"$trunc\"")}w.EndDocument(); "; // C#'s integer division truncates
            }
        }

        var constant = model.GetConstantValue(expression);
        if (constant.HasValue)
        {
            if (constant.Value is string text)
                return slot => $"w.StartDocument({slot}); w.Write(\"$literal\", {DocTypeEmitter.Literal(text)}); w.EndDocument(); "; // a text starting with $ is a value, not a field
            if (constant.Value is not null && DocTranslator.Literal(constant.Value, asEnum: false) is { } number && IsNumber(model.GetTypeInfo(expression).Type))
                return slot => $"w.Write({slot}, {number}); ";
            return Report(expression, Subset);
        }

        if (_paths.Path(expression, out var field) is { } path && field?.Kind is DocKind.Int32 or DocKind.Int64 or DocKind.Double or DocKind.Decimal or DocKind.String)
            return slot => $"w.Write({slot}, {DocTypeEmitter.Literal("$" + path)}); ";
        return Report(expression, Subset);
    }

    /// <summary>A <c>$concat</c> operand: a null text as empty, as C#'s concatenation reads it.</summary>
    private static string Text(Func<string, string> operand, string slot)
        => $"w.StartDocument({slot}); w.StartArray(\"$ifNull\"); {operand("0")}w.Write(1, \"\"); w.EndArray(); w.EndDocument(); ";

    private string? Operator(BinaryExpressionSyntax binary)
    {
        var text = model.GetTypeInfo(binary).Type?.SpecialType == SpecialType.System_String;
        return binary.Kind() switch
        {
            SyntaxKind.AddExpression => text ? "$concat" : "$add",
            SyntaxKind.SubtractExpression => "$subtract",
            SyntaxKind.MultiplyExpression => "$multiply",
            SyntaxKind.DivideExpression => "$divide",
            SyntaxKind.ModuloExpression => "$mod",
            _ => null,
        };
    }

    /// <summary>The expression in C#, over <c>e</c> (the document as read) instead of the lambda's parameter.</summary>
    private string Code(ExpressionSyntax expression)
    {
        var code = expression.ToString();
        var start = expression.SpanStart;
        var uses = expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Where(id => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id).Symbol, document));
        foreach (var id in uses.OrderByDescending(id => id.SpanStart))
            code = code.Substring(0, id.SpanStart - start) + "e" + code.Substring(id.Span.End - start);
        return code;
    }

    private static bool IsNumber(ITypeSymbol? type)
        => type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal
            or SpecialType.System_Int16 or SpecialType.System_Byte;

    private static bool IsInteger(ITypeSymbol? type)
        => type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16 or SpecialType.System_Byte;

    private Func<string, string>? Report(SyntaxNode node, string reason)
    {
        diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, node.GetLocation(), node.ToString(), reason));
        return null;
    }
}
