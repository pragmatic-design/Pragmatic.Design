using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A hand-written statement as the root of the plan (<c>session.Sql&lt;T&gt;($"…")</c>): the interpolated string's text becomes the FROM
///     subquery, each hole the next parameter (its kind from the hole's type), bound from the values the handler kept in the
///     chain's root. The statement is read here, at the call site, never from the handler's text at runtime.
/// </summary>
internal sealed partial class PlanBuilder
{
    /// <summary>The Sql step, first of the chain: false when the statement is not an interpolated string written at the call site.</summary>
    private bool AddRaw(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        if (step.Invocation.ArgumentList.Arguments.Count != 1 || step.Invocation.ArgumentList.Arguments[0].Expression is not InterpolatedStringExpressionSyntax interpolated)
            return false;
        var text = new StringBuilder();
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax literal:
                    text.Append(literal.TextToken.ValueText);
                    break;
                case InterpolationSyntax hole:
                    // The hole is a parameter: never text. Its kind from the expression's type, its value from the root's values at bind time.
                    var index = _kinds.Count;
                    text.Append("@p").Append(index);
                    _binds.Add($"p[{index}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, q.Raw!.Values[{index}]);");
                    _kinds.Add(LambdaTranslator.KindOf(model.GetTypeInfo(hole.Expression).Type));
                    break;
                default:
                    return false;
            }
        }

        _raw = text.ToString();
        return true;
    }

    /// <summary>
    ///     The Function step, first of the chain: <c>name(@p0, @p1)</c> as the FROM, the name a constant string (bare names get <c>dbo.</c> on SQL
    ///     Server), each argument a parameter bound from the root's values. False with <see cref="StepError" />.
    /// </summary>
    private bool AddFunction(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        var arguments = step.Invocation.ArgumentList.Arguments;
        var name = arguments.Count > 0 ? model.GetConstantValue(arguments[0].Expression) : default;
        if (!name.HasValue || name.Value is not string functionName || functionName.Length == 0)
        {
            StepError = "the function's name is a constant string: session.Function<T>(\"name\", args)";
            return false;
        }

        var text = new StringBuilder();
        text.Append("{d").Append(_dialectPieces.Count).Append("}(");
        _dialectPieces.Add(new DialectPiece(functionName, functionName, functionName.Contains(".") ? functionName : "dbo." + functionName, functionName, functionName)); // Oracle: a SQL table macro is called in FROM as it is
        for (var i = 1; i < arguments.Count; i++)
        {
            var index = _kinds.Count;
            text.Append(i > 1 ? ", @p" : "@p").Append(index);
            _binds.Add($"p[{index}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, q.Raw!.Values[{i - 1}]);");
            _kinds.Add(LambdaTranslator.KindOf(model.GetTypeInfo(arguments[i].Expression).Type));
        }

        _rawFrom = text.Append(')').ToString();
        return true;
    }

    /// <summary>The hand-written statement as read (holes as @pN), for the model check at the call site; null for the other roots.</summary>
    public string? RawText => _raw;

    /// <summary>Why a Sql step could not be read, for the call site's diagnostic.</summary>
    internal const string RawShape = "the statement must be an interpolated string written at the call site ($\"SELECT … {value}\"): its text is read by the generator, its holes become parameters";
}
