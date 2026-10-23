using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     An application function (<c>[SqlFunction]</c>) as the generator reads it: the definition method's <c>switch</c> on the
///     dialect, each arm interpreted symbolically through the <c>Fragment</c> combinators into a template per dialect, where
///     <c>{aN}</c> stands for the N-th argument's SQL. Null templates are dialects without an arm. Never executed.
/// </summary>
internal sealed class AppFunction
{
    private static readonly Regex ArgumentToken = new(@"\{a(\d+)\}", RegexOptions.Compiled);
    private static readonly SqlDialect[] Dialects = SqlDialects.All;

    private AppFunction(IMethodSymbol method, string?[] templates, string?[] notes)
    {
        Method = method;
        Templates = templates;
        Notes = notes;
    }

    public IMethodSymbol Method { get; }

    public string Name => Method.Name;

    public string?[] Templates { get; }

    /// <summary>The emulation note per dialect (<c>Fragment.Emulated</c>), null where the rendering is native.</summary>
    public string?[] Notes { get; }

    public bool IsPredicate => Method.ReturnType.SpecialType == SpecialType.System_Boolean;

    /// <summary>True when the definition could not be read: the method still gets its body (so the compiler reports only PRAG3013), nothing is registered.</summary>
    public bool IsBroken => Templates.All(t => t is null);

    /// <summary>A function whose definition is out of shape, kept for its body alone.</summary>
    public static AppFunction Broken(IMethodSymbol method) => new(method, new string?[Dialects.Length], new string?[Dialects.Length]);

    public string? Template(SqlDialect dialect) => Templates[(int)dialect];

    public string? Note(SqlDialect dialect) => Notes[(int)dialect];

    /// <summary>The template of a dialect with the arguments' SQL in place and its type tokens in the dialect's spelling; null when the dialect has no arm.</summary>
    public string? Render(SqlDialect dialect, string[] arguments)
        => Template(dialect) is { } template ? ArgumentToken.Replace(ResolveTypes(template, dialect), m => arguments[int.Parse(m.Groups[1].Value)]) : null;

    /// <summary>The runtime renderer's arms: each dialect's template as a concatenation over <c>a[i]</c>, null for a dialect without an arm.</summary>
    public IEnumerable<FunctionArmModel> RuntimeArms()
        => Dialects.Select(dialect => new FunctionArmModel(dialect.ToString(), Template(dialect) is { } template ? Concatenation(ResolveTypes(template, dialect)) : null));

    private static readonly Regex TypeToken = new(@"\{t(\w+)\}", RegexOptions.Compiled);

    /// <summary>The template's type tokens (<c>Fragment.Cast</c>) in the dialect's spelling: the same names everywhere but MySQL (SIGNED, DOUBLE, CHAR) and SQL Server's text.</summary>
    private static string ResolveTypes(string template, SqlDialect dialect) => TypeToken.Replace(template, m => (m.Groups[1].Value, dialect) switch
    {
        ("Integer", SqlDialect.MySql) => "SIGNED",
        ("Integer", _) => "INTEGER",
        ("BigInt", SqlDialect.MySql) => "SIGNED",
        ("BigInt", _) => "BIGINT",
        ("Numeric", SqlDialect.MySql) => "DECIMAL(18, 2)",
        ("Numeric", _) => "NUMERIC",
        ("Double", SqlDialect.MySql) => "DOUBLE",
        ("Double", _) => "FLOAT",
        (_, SqlDialect.MySql) => "CHAR",
        (_, SqlDialect.Oracle) => "VARCHAR2(4000)",
        (_, SqlDialect.SqlServer) => "nvarchar(max)",
        _ => "TEXT",
    });

    private static string Concatenation(string template)
    {
        var parts = new List<string>();
        var last = 0;
        foreach (Match match in ArgumentToken.Matches(template))
        {
            if (match.Index > last)
                parts.Add(SqlInterceptorGenerator.Literal(template.Substring(last, match.Index - last)));
            parts.Add($"a[{match.Groups[1].Value}]");
            last = match.Index + match.Length;
        }

        if (last < template.Length)
            parts.Add(SqlInterceptorGenerator.Literal(template.Substring(last)));
        return parts.Count == 0 ? "\"\"" : string.Join(" + ", parts);
    }

    /// <summary>
    ///     Reads a <c>[SqlFunction]</c> method: the definition of the same name (first parameter the dialect, the others fragments, one
    ///     per argument) and its body. Null with the reason when the shape is not the declared one.
    /// </summary>
    public static AppFunction? TryRead(IMethodSymbol method, Compilation compilation, out string? problem)
    {
        problem = null;
        if (!method.IsStatic || !method.IsPartialDefinition || method.IsGenericMethod)
        {
            problem = "a [SqlFunction] is a static partial method (not generic); the generator implements it";
            return null;
        }

        if (!method.ContainingType.IsStatic || !method.ContainingType.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<ClassDeclarationSyntax>().Any(c => c.Modifiers.Any(m => m.ValueText == "partial")))
        {
            problem = "the class must be static and partial, so the generator can add the implementation";
            return null;
        }

        var definition = method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>().FirstOrDefault(m => !m.IsPartialDefinition && m.ReturnType.Name == "SqlFragment" && m.Parameters.Length == method.Parameters.Length + 1 && m.Parameters[0].Type.Name == "SqlDialect");
        if (definition is null)
        {
            problem = $"no definition: add 'private static SqlFragment {method.Name}(SqlDialect dialect, {string.Join(", ", method.Parameters.Select(p => "SqlFragment " + p.Name))})' with a switch on the dialect built with Fragment.*";
            return null;
        }

        var syntax = definition.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var body = syntax?.ExpressionBody?.Expression ?? (syntax?.Body is { Statements.Count: 1 } block && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned } ? returned : null);
        if (syntax is null || body is null)
        {
            problem = "the definition must be one expression (an expression body, or one return)";
            return null;
        }

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var interpreter = new Interpreter(model, definition);
        var templates = new string?[Dialects.Length];
        var notes = new string?[Dialects.Length];
        if (body is SwitchExpressionSyntax @switch && interpreter.IsDialect(@switch.GoverningExpression))
        {
            foreach (var dialect in Dialects)
            {
                var arm = @switch.Arms.FirstOrDefault(a => Interpreter.Matches(a.Pattern, dialect));
                if (arm is null)
                    continue;
                templates[(int)dialect] = interpreter.Text(arm.Expression, out var note, out problem);
                notes[(int)dialect] = note;
                if (problem is not null)
                    return null;
            }
        }
        else
        {
            var text = interpreter.Text(body, out var note, out problem);
            if (problem is not null)
                return null;
            for (var i = 0; i < Dialects.Length; i++)
            {
                templates[i] = text;
                notes[i] = note;
            }
        }

        return new AppFunction(method, templates, notes);
    }

    /// <summary>The symbolic evaluation of a definition's expression into SQL text: combinators, operators, the definition's parameters as argument tokens, numbers as literals.</summary>
    private sealed class Interpreter(SemanticModel model, IMethodSymbol definition)
    {
        private string? _note;

        public bool IsDialect(ExpressionSyntax expression) => model.GetSymbolInfo(expression).Symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, definition.Parameters[0]);

        /// <summary>True when the arm's pattern names the dialect: <c>SqlDialect.X</c>, <c>SqlDialect.X or SqlDialect.Y</c>, or the discard.</summary>
        public static bool Matches(PatternSyntax pattern, SqlDialect dialect) => pattern switch
        {
            DiscardPatternSyntax => true,
            ConstantPatternSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } } => name == dialect.ToString(),
            BinaryPatternSyntax { RawKind: (int)SyntaxKind.OrPattern } or => Matches(or.Left, dialect) || Matches(or.Right, dialect),
            _ => false,
        };

        public string? Text(ExpressionSyntax expression, out string? note, out string? problem)
        {
            _note = null;
            problem = null;
            var text = Evaluate(expression, ref problem);
            note = _note;
            return text;
        }

        private string? Evaluate(ExpressionSyntax expression, ref string? problem)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax paren:
                    return Evaluate(paren.Expression, ref problem);
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                    return literal.Token.ValueText;
                case IdentifierNameSyntax identifier when model.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter && !SymbolEqualityComparer.Default.Equals(parameter, definition.Parameters[0]):
                {
                    var index = definition.Parameters.IndexOf(parameter) - 1;
                    return "{a" + index + "}";
                }

                case BinaryExpressionSyntax binary:
                {
                    var op = binary.Kind() switch
                    {
                        SyntaxKind.AddExpression => "+",
                        SyntaxKind.SubtractExpression => "-",
                        SyntaxKind.MultiplyExpression => "*",
                        SyntaxKind.DivideExpression => "/",
                        SyntaxKind.ModuloExpression => "%",
                        SyntaxKind.GreaterThanExpression => ">",
                        SyntaxKind.LessThanExpression => "<",
                        SyntaxKind.GreaterThanOrEqualExpression => ">=",
                        SyntaxKind.LessThanOrEqualExpression => "<=",
                        _ => null,
                    };
                    if (op is null)
                        return Fail(binary, "an operator a fragment has not", ref problem);
                    var left = Evaluate(binary.Left, ref problem);
                    var right = Evaluate(binary.Right, ref problem);
                    return left is null || right is null ? null : $"({left} {op} {right})";
                }

                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Fragment" }, Name.Identifier.ValueText: var combinator } } call:
                    return Combinator(call, combinator, ref problem);
                default:
                    return Fail(expression, "not a fragment: use the definition's parameters, numbers, the operators and Fragment.*", ref problem);
            }
        }

        private string? Combinator(InvocationExpressionSyntax call, string combinator, ref string? problem)
        {
            var arguments = call.ArgumentList.Arguments;
            switch (combinator)
            {
                case "Call" when arguments.Count >= 1 && StringOf(arguments[0].Expression) is { } function:
                {
                    var rendered = new List<string>();
                    for (var i = 1; i < arguments.Count; i++)
                    {
                        var text = Evaluate(arguments[i].Expression, ref problem);
                        if (text is null)
                            return null;
                        rendered.Add(text);
                    }

                    return $"{function}({string.Join(", ", rendered)})";
                }

                case "Keyword" when arguments.Count == 1 && StringOf(arguments[0].Expression) is { } keyword:
                    return keyword;
                case "Literal" when arguments.Count == 1 && StringOf(arguments[0].Expression) is { } value:
                    return "'" + value.Replace("'", "''") + "'";
                case "Extract" when arguments.Count == 2 && arguments[0].Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var field }:
                {
                    var value = Evaluate(arguments[1].Expression, ref problem);
                    return value is null ? null : $"EXTRACT({(field == "DayOfWeek" ? "DOW" : field.ToUpperInvariant())} FROM {value})";
                }

                case "Cast" when arguments.Count == 2 && arguments[1].Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var type }:
                {
                    var value = Evaluate(arguments[0].Expression, ref problem);
                    return value is null ? null : $"CAST({value} AS {TypeName(type)})";
                }

                case "And" or "Or" when arguments.Count == 2:
                {
                    var left = Evaluate(arguments[0].Expression, ref problem);
                    var right = Evaluate(arguments[1].Expression, ref problem);
                    return left is null || right is null ? null : $"({left} {combinator.ToUpperInvariant()} {right})";
                }

                case "Equal" when arguments.Count == 2:
                {
                    var left = Evaluate(arguments[0].Expression, ref problem);
                    var right = Evaluate(arguments[1].Expression, ref problem);
                    return left is null || right is null ? null : $"{left} = {right}";
                }

                case "Emulated" when arguments.Count == 2 && StringOf(arguments[1].Expression) is { } note:
                    _note = note;
                    return Evaluate(arguments[0].Expression, ref problem);
                default:
                    return Fail(call, $"Fragment.{combinator} with these arguments is not a combinator the generator reads (the name and the note must be string literals)", ref problem);
            }
        }

        /// <summary>A cast's type as a token (<c>{tInteger}</c>): the template is shared by the arms, the spelling is the dialect's when rendered (<see cref="ResolveTypes" />).</summary>
        private static string TypeName(string type) => "{t" + type + "}";

        private string? StringOf(ExpressionSyntax expression) => model.GetConstantValue(expression).Value as string;

        private static string? Fail(SyntaxNode node, string why, ref string? problem)
        {
            problem ??= $"'{node}': {why}";
            return null;
        }
    }
}
