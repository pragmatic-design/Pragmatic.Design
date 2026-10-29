using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The commands' part of a plan: <c>ExecuteDeleteAsync</c> takes the chain's filter only; <c>ExecuteUpdateAsync</c>'s setters
///     (<c>s => s.Set(o => o.Field, value).Set(o => o.Other, o => o.Other + 1)</c>, the value a constant, code over the two-parameter form's
///     argument, or computed from the document's fields) become the update's <c>$set</c> (a pipeline's when a value is computed), with the version
///     moved on when the document has one (a copy read before the update is then stale), and Cosmos DB's patch operations (<see cref="DocSetWriter" />).
/// </summary>
internal sealed partial class DocPlanner
{
    public static readonly HashSet<string> Commands = ["ExecuteDeleteAsync", "ExecuteUpdateAsync"];

    /// <summary>The update's sets; null for every other terminal.</summary>
    private DocSetWriter? _update;

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

        var update = new DocSetWriter(_entity);
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

            var valueSyntax = call.ArgumentList.Arguments[1].Expression;
            if (valueSyntax is SimpleLambdaExpressionSyntax { Body: ExpressionSyntax computedBody } computedLambda && _call.Model.GetDeclaredSymbol(computedLambda.Parameter) is IParameterSymbol document)
            {
                // The value computed from the document's own fields: Set(o => o.Stars, o => o.Stars + 1).
                var computed = new DocComputedTranslator(_call.Model, _entity, _models, document, _diagnostics).Translate(computedBody, field);
                if (computed is null)
                {
                    _failed = true;
                    return;
                }

                update.Computed(path, computed);
                continue;
            }

            var value = translator.SetValue(valueSyntax, field);
            if (value is null || translator.Failed)
            {
                _failed = true;
                return;
            }

            update.Value(path, value);
        }

        _update = update;
    }

    private string UpdateInitializer => _update is null ? "null" : $"static (w, s) => {{ {_update.Mongo()}}}";

    /// <summary>The plan's last arguments: Cosmos DB's patch operations, and whether MongoDB's update is a pipeline.</summary>
    private string CommandInitializer
        => _update is null ? "null, false" : (_update.Cosmos() is { } patch ? $"static (w, e, s) => {{ {patch}}}" : "null") + ", " + (_update.Pipeline ? "true" : "false");
}
