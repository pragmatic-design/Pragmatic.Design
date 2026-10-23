using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The values of a lambda: how each one is classified (constant, argument member, definition text), converted and typed for the plan's parameters.</summary>
internal sealed partial class LambdaTranslator
{
    private string? Parameter(string path, ExpressionSyntax syntax)
    {
        if (InlineConstants && _convertedColumn is null && _model.GetConstantValue(Strip(syntax)) is { HasValue: true, Value: { } inline } && SqlLiteral(inline) is { } literal)
            return literal;
        var text = ValueText(syntax, out var isConstant);
        if (text is null)
            return null;
        var type = _model.GetTypeInfo(syntax).Type;
        var kind = KindOf(type);
        if (_convertedColumn is { Converter: { } converter } column && type is not null)
        {
            // Compared with a converted column: the value travels as the column stores it (a constant converts once, in the plan's field).
            text = ColumnModel.Convert(converter, text, type, column.Store);
            kind = KindOf(column.Store);
        }

        Add($"Captured.Evaluate({path})", kind, text, isConstant);
        return "{" + (_parameterPaths.Count - 1) + "}";
    }

    private void Add(string path, string kind, string text, bool isConstant)
    {
        _parameterPaths.Add(path);
        _parameterKinds.Add(kind);
        _parameterTexts.Add(text);
        _parameterIsConstant.Add(isConstant);
    }

    /// <summary>
    ///     The C# text the plan binds a value from: a constant's literal, the argument's member as <c>argN.Member</c>, or (not at a
    ///     call site) the expression as written. Null with <see cref="Error" /> when, at a call site, the value comes from the
    ///     enclosing method: the remedy is the argument form.
    /// </summary>
    public string? ValueText(ExpressionSyntax syntax, out bool isConstant)
    {
        isConstant = false;
        syntax = Strip(syntax);
        var constant = _model.GetConstantValue(syntax);
        if (constant.HasValue)
        {
            isConstant = true;
            return ConstantText(constant.Value, _model.GetTypeInfo(syntax).Type, typedEnum: _convertedColumn is not null); // a converter takes the enum itself; the database its int
        }

        if (_args is not null && RefersToBound(syntax))
            return ArgText(syntax);
        if (_inline)
            return Fail(syntax, $"'{syntax}' is a value from outside the lambda, which no expression tree carries here: pass it as the argument of the two-parameter form, Where((o, arg) => …, {syntax}), with several values as a tuple; or write the chain as a declared [Query]");
        return syntax.ToString();
    }

    /// <summary>
    ///     A value rooted in the argument parameter, rewritten over the plan's variable: <c>a.Since</c> → <c>arg0.Since</c>. Member
    ///     chains and instance calls on them (<c>a.Text.Trim()</c>) are fine; a type name, a static member or another variable is
    ///     not, because the plan's file has no usings of the call site's.
    /// </summary>
    /// <summary>Binds a parameter of the lambda's scope to the plan variable that holds its value (the argument form's <c>arg</c>, a declared chain's parameters).</summary>
    public void BindParameter(IParameterSymbol parameter, string variable)
    {
        // Copied before the change: a nested translator shares its parent's map until it binds something of its own.
        _args = _args is null ? new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default) : new Dictionary<IParameterSymbol, string>(_args, SymbolEqualityComparer.Default);
        _args[parameter] = variable;
    }

    private string? ArgText(ExpressionSyntax syntax)
    {
        var roots = new Dictionary<IdentifierNameSyntax, string>();
        foreach (var identifier in syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var symbol = _model.GetSymbolInfo(identifier).Symbol;
            if (symbol is IParameterSymbol p && _args!.TryGetValue(p, out var variable))
            {
                roots[identifier] = variable;
                continue;
            }

            var isMemberName = identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier && symbol is IPropertySymbol or IFieldSymbol or IMethodSymbol { IsStatic: false };
            if (!isMemberName)
                return Fail(syntax, $"'{identifier}' in '{syntax}' is not a member of the argument; the argument form reads the argument and its members only (a.Since, a.Ids), compute the rest before the query");
        }

        if (syntax.DescendantNodesAndSelf().Any(n => n is ThisExpressionSyntax or BaseExpressionSyntax))
            return Fail(syntax, $"'{syntax}' reads the enclosing object; the argument form reads the argument and its members only");
        return syntax.ReplaceNodes(roots.Keys, (original, _) => SyntaxFactory.ParseExpression(roots[original])).ToString(); // a variable, or an expression a definition passed
    }

    private bool RefersToBound(SyntaxNode syntax)
    {
        foreach (var identifier in syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (_model.GetSymbolInfo(identifier).Symbol is IParameterSymbol p && _args!.ContainsKey(p))
                return true;
        }

        return false;
    }

    /// <summary>A compile-time constant as a SQL literal (numbers in invariant form, strings with their quotes doubled); null for a type SQL has no literal for in every dialect.</summary>
    internal static string? SqlLiteral(object value) => value switch
    {
        string s => "'" + s.Replace("'", "''") + "'",
        int or long or short or byte => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>A constant as C# the plan's file compiles without usings: literals, and an enum as a cast of its underlying value (boxed as the int the database takes, unless a converter wants the enum itself).</summary>
    public static string ConstantText(object? value, ITypeSymbol? type, bool typedEnum = false)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable)
            type = nullable.TypeArguments[0];
        if (type is { TypeKind: TypeKind.Enum } && value is not null)
            return $"{(typedEnum ? "" : "(int)")}({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)})";
        return value switch
        {
            null => "null",
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            bool b => b ? "true" : "false",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d",
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f",
            uint u => u.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            _ => $"({value.GetType().FullName}){Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>The <c>SqlParamKind</c> a captured value is bound as: the database's type for it, typed once so the command can be prepared.</summary>
    public static string KindOf(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable)
            type = nullable.TypeArguments[0];
        if (type is null)
            return "Unknown";
        if (type.TypeKind == TypeKind.Enum)
            return "Int32";
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return "Bytes";
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Single })
            return "Vector";
        if (EntityModel.IsArrayColumn(type))
            return "Array";
        return type.SpecialType switch
        {
            SpecialType.System_String => "String",
            SpecialType.System_Int32 => "Int32",
            SpecialType.System_Int64 => "Int64",
            SpecialType.System_Decimal => "Decimal",
            SpecialType.System_Double => "Double",
            SpecialType.System_Boolean => "Boolean",
            SpecialType.System_DateTime => "DateTime",
            _ => type.ToDisplayString() switch
            {
                "System.Guid" => "Guid",
                "System.DateTimeOffset" => "DateTimeOffset",
                "System.DateOnly" => "DateOnly",
                "System.TimeOnly" => "TimeOnly",
                "System.TimeSpan" => "Int64", // bound as ticks
                _ => "Unknown",
            },
        };
    }
}
