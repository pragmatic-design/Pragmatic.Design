using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A terminal's chain into a plan: the conditions of every Where, the terminal's predicate and a cursor, under one <c>$and</c>; the
///     ordering; a Select's slot and the fields it reads; an aggregate's field. Written as the <c>DocPlan</c> initializer the interceptor uses.
/// </summary>
internal sealed partial class DocPlanner
{
    private readonly DocCall _call;
    private readonly DocModel _entity;
    private readonly Func<INamedTypeSymbol, DocModel?> _models;
    private readonly List<Diagnostic> _diagnostics;
    private readonly List<DocCondition> _conditions = [];
    private readonly List<(string Path, bool Descending)> _sort = [];
    private int _slot;
    private bool _failed;

    public DocPlanner(DocCall call, DocModel entity, Func<INamedTypeSymbol, DocModel?> models, List<Diagnostic> diagnostics)
    {
        _call = call;
        _entity = entity;
        _models = models;
        _diagnostics = diagnostics;
    }

    public string[]? Fields { get; private set; }

    public int SelectSlot { get; private set; } = -1;

    public string? AggregateField { get; private set; }

    /// <summary>The slot the terminal's own argument takes (its predicate's two-parameter form).</summary>
    public int TerminalSlot => _slot;

    /// <summary>Reads the chain and the terminal; false when something was reported.</summary>
    public bool Build()
    {
        foreach (var step in _call.Before)
            Step(step);
        RefuseOrderedByWhereUnread();
        Terminal();
        if (!_failed)
            Cosmos();
        return !_failed;
    }

    private void Step(DocStep step)
    {
        if (_groupKey is not null)
        {
            // After a GroupBy: its Select; then a Where over the groups (HAVING) and an ordering, stages after $group (Skip/Take at run time).
            if (step.Name == "Select" && _group is null)
            {
                _slot++; // the operator stores its delegate, as every Select does
                GroupSelect(step);
            }
            else if (_group is not null && step.Name == "Where")
            {
                Condition(step.Lambda, step, step.TakesArg ? _slot : -1, _groupModel, _having);
                if (step.TakesArg)
                    _slot++;
            }
            else if (_group is not null && step.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
            {
                if (step.Name.StartsWith("OrderBy", StringComparison.Ordinal))
                    _groupSort.Clear();
                var path = KeyPath(step.Lambda, step.Invocation, _groupModel);
                if (path is not null)
                    _groupSort.Add((path, step.Name.EndsWith("Descending", StringComparison.Ordinal)));
            }
            else
            {
                Fail(step.Invocation, $"{step.Name} after a GroupBy: a GroupBy is followed by its Select, then a Where over the groups, an ordering, ToListAsync");
            }

            return;
        }

        if (step.Name == "GroupBy")
        {
            GroupBy(step);
            return;
        }

        if (step.Name == "SelectMany")
        {
            Unwind(step);
            return;
        }

        if (step.Name == "Join")
        {
            Join(step);
            return;
        }

        if (_unwind is not null || JoinInitializer is not null)
        {
            Fail(step.Invocation, $"{step.Name} after a SelectMany or a Join: filter and order the documents before it");
            return;
        }

        if (SelectSlot >= 0 && step.Name != "Select")
        {
            Fail(step.Invocation, $"{step.Name} after a Select: put the filter and the ordering before the projection");
            return;
        }

        switch (step.Name)
        {
            case "Where":
                Condition(step.Lambda, step, step.TakesArg ? _slot : -1);
                if (step.TakesArg)
                    _slot++;
                return;
            case "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending":
            {
                if (step.Name.StartsWith("OrderBy", StringComparison.Ordinal))
                    _sort.Clear(); // a new OrderBy replaces the ordering, as LINQ
                var path = KeyPath(step.Lambda, step.Invocation);
                if (path is not null)
                    _sort.Add((path, step.Name.EndsWith("Descending", StringComparison.Ordinal)));
                return;
            }

            case "Select":
                if (SelectSlot >= 0)
                {
                    Fail(step.Invocation, "one Select per chain");
                    return;
                }

                SelectSlot = _slot++;
                Fields = ReadFields(step.Lambda);
                return;
            case "After":
                Cursor(step, _slot++);
                return;
            case "Matching":
                if (PredicateSlot >= 0)
                {
                    Fail(step.Invocation, "one Matching per chain: compose the specifications (&, |) before it");
                    return;
                }

                PredicateSlot = _slot++; // a predicate composed at runtime (DocPredicate): joined to the conditions per call
                return;
            case "OrderedBy":
                OrderedBy(step);
                return;
        }
    }

    /// <summary>The slot of the chain's <c>Matching</c> predicate; -1 when it has none.</summary>
    public int PredicateSlot { get; private set; } = -1;

    private void Terminal()
    {
        var name = _call.Name;
        var arguments = _call.Invocation.ArgumentList.Arguments;
        var lambda = arguments.Count > 0 ? arguments[0].Expression as LambdaExpressionSyntax : null;
        if (_unwind is not null || JoinInitializer is not null)
        {
            if (name != "ToListAsync" || lambda is not null)
                Fail(_call.Invocation, "a SelectMany or a Join ends in ToListAsync (Skip and Take before it)");
            return;
        }

        if (name is "MaxAsync" or "MinAsync" or "SumAsync")
        {
            if (SelectSlot >= 0)
            {
                Fail(_call.Invocation, $"{name} after a Select: aggregate a field of the document, before the projection");
                return;
            }

            if (lambda is SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } simple && _call.Model.GetDeclaredSymbol(simple.Parameter) is { } parameter)
            {
                var translator = new DocTranslator(_call.Model, _entity, _models, parameter, null, null, _diagnostics, _call.Invocation.GetLocation());
                if (translator.Path(body, out var field) is { } path && field is not null && field.Kind is not (DocKind.Embedded or DocKind.List))
                {
                    AggregateField = path;
                    return;
                }
            }

            Fail(_call.Invocation, $"{name} takes a field of the document: o => o.Total");
            return;
        }

        if (_groupKey is not null)
        {
            if (!Grouped || name != "ToListAsync")
                Fail(_call.Invocation, "a GroupBy ends in its Select and ToListAsync: g => new { g.Key, Count = g.Count() }");
            return;
        }

        if (Commands.Contains(name))
        {
            Command(lambda);
            return;
        }

        if (lambda is null)
            return;
        if (SelectSlot >= 0)
        {
            Fail(_call.Invocation, $"a predicate in {name} after a Select: filter before the projection");
            return;
        }

        var step = new DocStep(_call.Invocation, _call.Method);
        Condition(lambda, step, step.TakesArg ? _slot : -1);
    }

    /// <summary>A Where's condition, over the document (<paramref name="model" /> null) or over a grouped Select's result (into the HAVING list).</summary>
    private void Condition(LambdaExpressionSyntax? lambda, DocStep step, int argSlot, DocModel? model = null, List<DocCondition>? into = null)
    {
        var parameters = Parameters(lambda);
        if (lambda?.Body is not ExpressionSyntax body || parameters.Count == 0)
        {
            Fail(step.Invocation, "a predicate is a lambda with an expression body");
            return;
        }

        string? argumentCode = null;
        if (argSlot >= 0)
        {
            var argType = ArgType(step.Method, "TArg");
            if (argType is null || argType.IsAnonymousType)
            {
                Fail(step.Invocation, "the argument of the two-parameter form is a named type or a tuple (an anonymous type has no name the plan can cast to)");
                return;
            }

            argumentCode = $"(({argType.ToDisplayString(DocModel.Qualified)})s[{argSlot}]!)";
        }

        var translator = new DocTranslator(_call.Model, model ?? _entity, _models, parameters[0], parameters.Count > 1 ? parameters[1] : null, argumentCode, _diagnostics, step.Invocation.GetLocation());
        var condition = translator.Condition(body);
        if (condition is null || translator.Failed)
            _failed = true;
        else
            (into ?? _conditions).Add(condition);
    }

    /// <summary>The cursor's condition: after the last document's keys, in the ordering's directions (k1 > a, or k1 = a and k2 > b…).</summary>
    private void Cursor(DocStep step, int slot)
    {
        if (_sort.Count == 0)
        {
            Fail(step.Invocation, "After follows an OrderBy: the cursor is the ordering keys' values");
            return;
        }

        var cursorType = ArgType(step.Method, "TCursor");
        if (cursorType is null || cursorType.IsAnonymousType)
        {
            Fail(step.Invocation, "the cursor is the key's value, or a tuple of the keys' values in order");
            return;
        }

        var code = $"(({cursorType.ToDisplayString(DocModel.Qualified)})s[{slot}]!)";
        var tuple = _sort.Count > 1;
        if (tuple && (!cursorType.IsTupleType || ((INamedTypeSymbol)cursorType).TupleElements.Length != _sort.Count))
        {
            Fail(step.Invocation, $"the cursor is a tuple of {_sort.Count} values, one per ordering key");
            return;
        }

        var alternatives = new List<DocCondition>();
        for (var i = 0; i < _sort.Count; i++)
        {
            var terms = new List<DocCondition>();
            for (var j = 0; j < i; j++)
                terms.Add(new DocCondition.Compare(_sort[j].Path, "$eq", new DocValue(tuple ? $"{code}.Item{j + 1}" : code)));
            terms.Add(new DocCondition.Compare(_sort[i].Path, _sort[i].Descending ? "$lt" : "$gt", new DocValue(tuple ? $"{code}.Item{i + 1}" : code)));
            alternatives.Add(DocCondition.All(terms)!);
        }

        _conditions.Add(alternatives.Count == 1 ? alternatives[0] : new DocLogical("$or", alternatives));
    }

    private string? KeyPath(LambdaExpressionSyntax? lambda, InvocationExpressionSyntax invocation, DocModel? model = null)
    {
        var parameters = Parameters(lambda);
        if (lambda?.Body is ExpressionSyntax body && parameters.Count == 1)
        {
            var translator = new DocTranslator(_call.Model, model ?? _entity, _models, parameters[0], null, null, _diagnostics, invocation.GetLocation());
            if (translator.Path(body, out var field) is { } path && field is not null && field.Kind is not (DocKind.Embedded or DocKind.List))
                return path;
        }

        Fail(invocation, "an ordering key is a field of the document: o => o.CreatedAt");
        return null;
    }

    /// <summary>The top-level fields a Select's lambda reads (the projection of the find); null when it uses the document as a whole.</summary>
    private string[]? ReadFields(LambdaExpressionSyntax? lambda)
    {
        var parameters = Parameters(lambda);
        if (lambda is null || parameters.Count != 1)
            return null;
        var fields = new List<string>();
        foreach (var identifier in lambda.Body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (!SymbolEqualityComparer.Default.Equals(_call.Model.GetSymbolInfo(identifier).Symbol, parameters[0]))
                continue;
            if (identifier.Parent is not MemberAccessExpressionSyntax member || member.Expression != identifier || _entity.Field(member.Name.Identifier.ValueText) is not { } field)
                return null; // the document itself is used: read it whole
            if (!fields.Contains(field.Name))
                fields.Add(field.Name);
        }

        return fields.Count == 0 ? ["_id"] : fields.ToArray();
    }

    private List<IParameterSymbol> Parameters(LambdaExpressionSyntax? lambda)
    {
        var syntax = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => new List<ParameterSyntax> { simple.Parameter },
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToList(),
            _ => new List<ParameterSyntax>(),
        };
        return syntax.Select(p => _call.Model.GetDeclaredSymbol(p)).OfType<IParameterSymbol>().ToList();
    }

    private static ITypeSymbol? ArgType(IMethodSymbol method, string name)
    {
        var parameters = method.OriginalDefinition.TypeParameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == name)
                return method.TypeArguments[i];
        }

        return null;
    }

    private void Fail(SyntaxNode node, string reason)
    {
        _diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, node.GetLocation(), _call.Name, reason));
        _failed = true;
    }

    /// <summary>The plan's initializer: <c>new DocPlan&lt;E&gt;(origin, filter, sort, fields, selectSlot, aggregateField)</c>.</summary>
    public string Initializer(string origin)
    {
        var source = new StringBuilder();
        source.Append($"new global::Pragmatic.Persistence.Documents.Planning.DocPlan<{_entity.FullName}>({DocTypeEmitter.Literal(origin)}, ");
        var filter = DocCondition.All(_conditions);
        if (filter is null)
        {
            source.Append("null, ");
        }
        else
        {
            source.AppendLine("static (w, s) =>");
            source.AppendLine("            {");
            filter.Render(source, "                ");
            source.Append("            }, ");
        }

        source.Append(_sort.Count == 0 ? "null, " : "static w => { " + string.Concat(_sort.Select(k => $"w.Write({DocTypeEmitter.Literal(k.Path)}, {(k.Descending ? -1 : 1)}); ")) + "}, ");
        source.Append(Fields is null ? "null, " : "new string[] { " + string.Join(", ", Fields.Select(DocTypeEmitter.Literal)) + " }, ");
        source.Append(SelectSlot + ", ");
        source.Append(AggregateField is null ? "null" : DocTypeEmitter.Literal(AggregateField));
        source.Append(", ");
        source.Append(_cosmos ?? "null");
        source.Append(", ");
        source.Append(UpdateInitializer);
        source.Append(", ");
        source.Append(GroupInitializer);
        source.Append(", ");
        source.Append(UnwindInitializer);
        source.Append(", ");
        source.Append(PredicateSlot).Append(", ").Append(OrderingSlot);
        source.Append(')');
        return source.ToString();
    }

    /// <summary>The query for the PRAG3199 note.</summary>
    public string Describe()
        => (DocCondition.All(_conditions)?.Describe() ?? "{}") + (_sort.Count == 0 ? "" : " sort { " + string.Join(", ", _sort.Select(k => $"{k.Path}: {(k.Descending ? -1 : 1)}")) + " }")
           + (CosmosText is null ? "" : " | Cosmos DB: " + CosmosText);
}
