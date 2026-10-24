using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The commands' part of a plan: <c>ExecuteDeleteAsync</c> takes the chain's filter only; <c>ExecuteUpdateAsync</c>'s setters
///     (<c>s => s.Set(o => o.Field, value).Set(…)</c>, the value a constant or code over the two-parameter form's argument) become the update
///     document's <c>$set</c>, with an <c>$inc</c> of the version when the document has one (a copy read before the update is then stale).
/// </summary>
internal sealed partial class DocPlanner
{
    public static readonly HashSet<string> Commands = ["ExecuteDeleteAsync", "ExecuteUpdateAsync"];

    /// <summary>The update document's elements as statements over <c>w</c> and <c>s</c>; null for every other terminal.</summary>
    private string? _update;

    private void Command(LambdaExpressionSyntax? lambda)
    {
        if (SelectSlot >= 0)
        {
            Fail(_call.Invocation, $"{_call.Name} after a Select: a command writes the documents the filter selects, not a projection");
            return;
        }

        if (_call.Name == "ExecuteUpdateAsync")
            Setters(lambda, new DocStep(_call.Invocation, _call.Method));
    }

    private void Setters(LambdaExpressionSyntax? lambda, DocStep step)
    {
        var parameters = Parameters(lambda);
        if (lambda?.Body is not ExpressionSyntax body || parameters.Count == 0)
        {
            Fail(step.Invocation, "the setters are a lambda with an expression body: s => s.Set(o => o.Field, value)");
            return;
        }

        string? argumentCode = null;
        if (step.TakesArg)
        {
            var argType = ArgType(step.Method, "TArg");
            if (argType is null || argType.IsAnonymousType)
            {
                Fail(step.Invocation, "the argument of the two-parameter form is a named type or a tuple (an anonymous type has no name the plan can cast to)");
                return;
            }

            argumentCode = $"(({argType.ToDisplayString(DocModel.Qualified)})s[{_slot}]!)";
        }

        // The Set calls, innermost first: s.Set(a).Set(b) is Set(b) on Set(a) on s.
        var calls = new List<InvocationExpressionSyntax>();
        var current = body;
        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Set" } access, ArgumentList.Arguments.Count: 2 } call)
        {
            calls.Insert(0, call);
            current = access.Expression;
        }

        if (calls.Count == 0 || current is not IdentifierNameSyntax root || !SymbolEqualityComparer.Default.Equals(_call.Model.GetSymbolInfo(root).Symbol, parameters[0]))
        {
            Fail(step.Invocation, "the setters are calls of Set on the lambda's parameter: s => s.Set(o => o.Field, value).Set(o => o.Other, value)");
            return;
        }

        var set = new StringBuilder();
        foreach (var call in calls)
        {
            if (call.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax { Body: ExpressionSyntax fieldBody } fieldLambda || _call.Model.GetDeclaredSymbol(fieldLambda.Parameter) is not IParameterSymbol row)
            {
                Fail(call, "Set's first argument selects a field: o => o.Status");
                return;
            }

            var translator = new DocTranslator(_call.Model, _entity, _models, row, parameters.Count > 1 ? parameters[1] : null, argumentCode, _diagnostics, call.GetLocation());
            var path = translator.Path(fieldBody, out var field);
            if (path is null || field is null || translator.Failed)
            {
                _failed = true;
                return;
            }

            if (field.Kind is DocKind.Embedded or DocKind.List || field == _entity.Key || field == _entity.Version || field == _entity.PartitionKey)
            {
                Fail(call, $"Set writes a value field: not the key, the version, the partition key, an embedded document or an array ({field.Property.Name})");
                return;
            }

            var value = translator.SetValue(call.ArgumentList.Arguments[1].Expression, field);
            if (value is null || translator.Failed)
            {
                _failed = true;
                return;
            }

            set.Append(value.IsNull ? $"w.WriteNull({DocTypeEmitter.Literal(path)}); " : $"global::Pragmatic.Persistence.Documents.Conversion.DocValues.Write(w, {DocTypeEmitter.Literal(path)}, {value.Code}); ");
        }

        var version = _entity.Version is { } v ? $"w.StartDocument(\"$inc\"); w.Write({DocTypeEmitter.Literal(v.Name)}, 1); w.EndDocument(); " : "";
        _update = $"w.StartDocument(\"$set\"); {set}w.EndDocument(); {version}";
    }

    private string UpdateInitializer => _update is null ? "null" : $"static (w, s) => {{ {_update}}}";
}
